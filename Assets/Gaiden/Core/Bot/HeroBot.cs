using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Bot
{
    /// <summary>
    /// A player stand-in that drives the real command API. Greedy one-ply: it previews every
    /// L-shaped drag for every hero and every castable slot, scores the outcome, and picks the best.
    /// Used by headless soak tests (balance + soft-lock detection) and the in-game autoplay harness.
    /// </summary>
    public sealed class HeroBot
    {
        private readonly Rng rng;
        /// <summary>0 = always best move, 1 = always random legal move.</summary>
        public float Randomness;

        /// <summary>
        /// 0-100, default 100 (unchanged, strict-best behavior — headless soak tests and balance records never
        /// see this move). Below 100, move selection softens from a hard argmax into a Boltzmann/softmax draw
        /// over every candidate's score (owner request: a demo bot that plays with a believable range of skill,
        /// from a perfect campaign down to "only basic understanding of the game"). Decision quality only —
        /// execution precision (whether a drag lands clean, a diagonal slip lands clean) is a separate concern
        /// handled by the simulated cursor in BattleScreen, not here.
        /// </summary>
        public float SkillLevel = 100f;

        public HeroBot(int seed, float randomness = 0f)
        {
            rng = new Rng(seed);
            Randomness = randomness;
        }

        /// <summary>The best drag on offer (hints and automation teach moving, so they ask for a move specifically).</summary>
        public MoveCommand BestMove(BattleState s) => Decide(s, movesOnly: true) as MoveCommand;

        public BattleCommand Decide(BattleState s) => Decide(s, false);

        private BattleCommand Decide(BattleState s, bool movesOnly)
        {
            // A recruitable whose every term is met (GG-T132) is worth more than any swing: talk it round first.
            if (!movesOnly && Recruitment.ReadyTalk(s) is TalkCommand talk) return talk;
            var heroes = s.LivingList(Team.Hero);
            bool lone = EnemyPlanner.LoneHero(s);
            var candidates = new List<(BattleCommand cmd, float score)>();
            // GG-T66: displacement bonus per move candidate (index into candidates), applied only when no pincer is on offer.
            var displacement = new List<(int index, float bonus)>();
            bool pincerAvailable = false;
            // GG-T188 (§2.0): picking a hero up starts the clock; a drag takes BotSecondsPerStep a tile, so a path longer
            // than the turn window is cut where the time runs out — the hero is dropped there (forced drop).
            float window = BattleEngine.TurnWindowSeconds(s);
            int maxSteps = BattleEngine.MaxDragSteps(s);

            foreach (var h in heroes)
            {
                if (!BattleEngine.CanMove(s, h.Id) || maxSteps <= 0) continue;
                for (int y = 0; y < BattleConfig.Rows; y++)
                    for (int x = 0; x < BattleConfig.Cols; x++)
                    {
                        var dest = new Coord(x, y);
                        if (dest == h.Pos) continue;
                        foreach (var full in LPaths(h.Pos, dest))
                        {
                            var path = full.Count > maxSteps ? full.GetRange(0, maxSteps) : full;
                            var pv = BattleEngine.PreviewMove(s, h.Id, path);
                            if (pv.StepsTaken.Count == 0) continue;
                            float score = ScoreMove(s, pv, h);
                            if (!pincerAvailable && HasRealPincer(s, pv)) pincerAvailable = true;
                            float push = lone ? 0f : DisplacementScore(s, pv, h);
                            if (push > 0f) displacement.Add((candidates.Count, push));
                            float held = full.Count > maxSteps ? window : pv.StepsTaken.Count * BattleConfig.BotSecondsPerStep;
                            if (window > 0f) held = Math.Min(held, window);
                            candidates.Add((new MoveCommand { HeroId = h.Id, Path = new List<Coord>(pv.StepsTaken), HeldSeconds = held }, score));
                        }
                    }

                for (int slot = 0; slot < h.Bar.Count && !movesOnly; slot++)
                {
                    if (BattleEngine.GetSlotStatus(s, h.Id, slot) != SlotStatus.Ready) continue;
                    var a = BattleEngine.SlotAbility(s, h, slot);
                    if (a == null) continue;
                    if (a.Mode == TargetMode.Auto)
                    {
                        float sc = ScoreAbility(s, h, a, h.Pos);
                        if (sc > 0) candidates.Add((new UseSlotCommand { HeroId = h.Id, Slot = slot, Target = h.Pos }, sc));
                        continue;
                    }
                    foreach (var t in BattleEngine.ValidTargets(s, h.Id, slot))
                    {
                        float sc = ScoreAbility(s, h, a, t);
                        if (sc > 0) candidates.Add((new UseSlotCommand { HeroId = h.Id, Slot = slot, Target = t }, sc));
                    }
                }
            }

            // GG-T66: with no pincer on offer anywhere, sliding a hero through an ally or foe to reposition it is the
            // next-best way to make progress (it sets up the next turn's pincer), so it becomes a deliberate,
            // high-priority move rather than an incidental side effect of walking somewhere.
            if (!pincerAvailable)
                foreach (var (index, bonus) in displacement)
                    candidates[index] = (candidates[index].cmd, candidates[index].score + bonus);

            if (candidates.Count == 0) return movesOnly ? null : new ShieldCommand();
            if (Randomness > 0f && rng.Chance(Randomness)) return candidates[rng.Range(0, candidates.Count)].cmd;

            float best = float.NegativeInfinity;
            var bestList = new List<BattleCommand>();
            foreach (var (cmd, score) in candidates)
            {
                if (score > best + 0.001f) { best = score; bestList.Clear(); bestList.Add(cmd); }
                else if (Math.Abs(score - best) <= 0.001f) bestList.Add(cmd);
            }
            // A lone hero (GG-T123, §22.10) can't pincer: with no damage, pickup or heal worth taking it guards rather
            // than wandering — foes no longer flee a lone hero (EnemyPlanner.LoneHero), so they come to its bar.
            if (!movesOnly && best < 0.5f && (lone || rng.Chance(0.2f))) return new ShieldCommand();
            if (SkillLevel < 99.9f) return SkillWeightedPick(candidates, best);
            return bestList[rng.Range(0, bestList.Count)];
        }

        /// <summary>
        /// Boltzmann/softmax draw over every candidate, temperature scaled by (100 - SkillLevel): at 100 this is
        /// never called (strict best); as skill falls the pool of plausible picks widens smoothly from "always
        /// the best" toward "near-uniform random legal move" — the same mechanism as the HeroBot's Randomness
        /// knob, but continuous and driven by one skill number instead of an all-or-nothing coin flip.
        /// </summary>
        private BattleCommand SkillWeightedPick(List<(BattleCommand cmd, float score)> candidates, float best)
        {
            float skill01 = Math.Max(0f, Math.Min(100f, SkillLevel)) / 100f;
            const float maxTemperature = 12f;
            float temperature = Math.Max(0.05f, maxTemperature * (1f - skill01) * (1f - skill01));
            float sum = 0f;
            var weights = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                float w = (float)Math.Exp((candidates[i].score - best) / temperature);
                weights[i] = w;
                sum += w;
            }
            float roll = rng.NextFloat() * sum;
            float acc = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc) return candidates[i].cmd;
            }
            return candidates[candidates.Count - 1].cmd;
        }

        public static IEnumerable<List<Coord>> LPaths(Coord from, Coord to)
        {
            yield return Path(from, to, true);
            if (from.X != to.X && from.Y != to.Y) yield return Path(from, to, false);
        }

        private static List<Coord> Path(Coord from, Coord to, bool xFirst)
        {
            var list = new List<Coord>();
            var c = from;
            void StepX() { while (c.X != to.X) { c = new Coord(c.X + Math.Sign(to.X - c.X), c.Y); list.Add(c); } }
            void StepY() { while (c.Y != to.Y) { c = new Coord(c.X, c.Y + Math.Sign(to.Y - c.Y)); list.Add(c); } }
            if (xFirst) { StepX(); StepY(); } else { StepY(); StepX(); }
            return list;
        }

        private float ScoreMove(BattleState s, MovePreview pv, Unit hero)
        {
            float score = 0f;
            bool lone = EnemyPlanner.LoneHero(s);
            foreach (var p in pv.Pincers)
            {
                foreach (var o in p.Opponents)
                {
                    if (BattleEngine.IsArmored(s, o)) { score -= 1f; continue; }
                    if (o.IsCorpse) { score += 1f; continue; } // clearing a body is worth little on its own
                    score += 10f;
                    if (o.HasPlate)
                    {
                        // A plated foe: an attacker on its facing side only dents the plate.
                        foreach (var att in new[] { p.A, p.B })
                            if (pv.Positions.TryGetValue(att.Id, out var ap) && SideFacing(o, ap)) score -= 4f;
                    }
                    var e = s.TurnEntryOf(o.Id);
                    if (e != null && e.InZone) score += 4f;
                    if (o.HpFraction < 0.4f) score += 3f;
                }
                score += 2f * (p.SupportersA.Count + p.SupportersB.Count);
                // Power Amplifier (owner direction 2026-10-11): a pincer attacker (never a supporter)
                // who ends this move standing on the amplifier strikes amplified — worth real priority.
                if (s.Amplifier.HasValue)
                    foreach (var att in new[] { p.A, p.B })
                        if (pv.Positions.TryGetValue(att.Id, out var ap) && ap == s.Amplifier.Value) score += 8f;
            }
            // Pickups along the path (§13.3.1): worth a detour, more so a tonic for a hurt hero.
            foreach (var step in pv.StepsTaken)
            {
                var pk = s.PickupAt(step);
                if (pk == null) continue;
                s.Content.Pickups.TryGetValue(pk.PickupId, out var def);
                score += 4f + (def != null && def.HealFraction > 0f ? 8f * (1f - hero.HpFraction) : 1f);
            }
            // Mild preference for staying out of melee range of soon-acting enemies when nothing else is on offer.
            if (pv.Positions.TryGetValue(hero.Id, out var at))
            {
                foreach (var e in s.LivingList(Team.Enemy))
                {
                    if (!pv.Positions.TryGetValue(e.Id, out var ep)) continue;
                    int d = Math.Abs(ep.X - at.X) + Math.Abs(ep.Y - at.Y);
                    if (d == 1 && hero.HpFraction < 0.35f) score -= 3f;
                    score += (lone ? 0.15f : 0.05f) * (8 - Math.Min(8, d)); // a lone hero closes in to bring its bar to bear
                }
            }
            return score + rng.NextFloat() * 0.01f;
        }

        /// <summary>True when this move would land a pincer on a real (unarmored, living, non-corpse) foe.</summary>
        private static bool HasRealPincer(BattleState s, MovePreview pv)
        {
            foreach (var p in pv.Pincers)
                foreach (var o in p.Opponents)
                    if (!o.IsCorpse && !BattleEngine.IsArmored(s, o)) return true;
            return false;
        }

        /// <summary>
        /// GG-T66: value of the units this move slides out of the way (a hero passing through an ally or foe
        /// displaces it). Each repositioned unit is worth a solid bonus, more when it ends somewhere that sets up
        /// a pincer next turn: a displaced ally beside a foe, or a displaced foe beside a hero.
        /// </summary>
        private static float DisplacementScore(BattleState s, MovePreview pv, Unit mover)
        {
            float score = 0f;
            foreach (var u in s.Units)
            {
                if (!u.Alive || u.IsCorpse || u.IsNeutral || u.Id == mover.Id) continue; // shuffling a neutral sets nothing up
                if (!pv.Positions.TryGetValue(u.Id, out var to) || to == u.Pos) continue;
                score += 6f;
                var other = u.Team == Team.Hero ? Team.Enemy : Team.Hero;
                foreach (var v in s.LivingList(other))
                {
                    if (v.IsCorpse || !pv.Positions.TryGetValue(v.Id, out var vp)) continue;
                    if (Math.Abs(vp.X - to.X) + Math.Abs(vp.Y - to.Y) == 1) { score += 3f; break; }
                }
            }
            return score;
        }

        private static bool SideFacing(Unit o, Coord at)
        {
            int dx = at.X - o.Pos.X, dy = at.Y - o.Pos.Y;
            var side = Math.Abs(dx) > Math.Abs(dy) || (Math.Abs(dx) == Math.Abs(dy) && o.Facing.X != 0) ? new Coord(Math.Sign(dx), 0) : new Coord(0, Math.Sign(dy));
            return side == o.Facing;
        }

        private float ScoreAbility(BattleState s, Unit caster, AbilityDef a, Coord anchor)
        {
            if (a.IsTeleport)
            {
                var copy = s.Clone();
                var cu = copy.UnitById(caster.Id);
                cu.Pos = anchor;
                float sc = 0f;
                foreach (var p in PincerDetector.Detect(copy, Team.Hero, cu)) sc += 10f * p.Opponents.Count;
                return sc - 2f;
            }
            var targets = TargetResolver.Collect(s, a, caster, anchor);
            float score = 0f;
            foreach (var t in targets)
            {
                // A recruitable that wants healing (GG-T132) is worth a heal even at half HP.
                if (t.IsNeutral && a.BaseHeal > 0 && t.Recruitable != null && t.Recruitable.WantsHealing && !t.RecruitMarks.Contains("healed")) score += 10f;
                if (t.Team != caster.Team && !t.IsNeutral)
                {
                    if (BattleEngine.IsArmored(s, t)) continue;
                    if (a.BaseDamage > 0)
                    {
                        int dmg = (int)Math.Round(Formulas.SpellDamage(a, t, false) * Formulas.CasterPower(a, caster) * caster.OutgoingDamageMultiplier);
                        score += Math.Min(t.Hp, dmg) * 0.6f + (t.Hp <= dmg ? 8f : 0f);
                    }
                    if (a.ApplyBuff != null && !t.HasBuff(a.ApplyBuff)) score += 3f;
                    if (a.StealsMana) score += s.Bank.IsFull ? 0f : 3f;
                    if (a.HastenU > 0) score -= 5f;
                    if (a.DelayU > 0f) score += 3f; // GG-T183: shoving a foe back on the timeline buys the party a turn
                    if (a.BaseHeal > 0 && a.Filter == TargetFilter.EnemyOnly) score += Math.Min(caster.MaxHp - caster.Hp, a.BaseHeal) * 0.3f; // drain
                }
                else
                {
                    if (a.BaseHeal > 0) score += Math.Min(t.MaxHp - t.Hp, a.BaseHeal) * (t.HpFraction < 0.5f ? 0.9f : 0.2f);
                    if (a.RemovesDebuffs) foreach (var b in t.Buffs) if (b.Def.Kind == BuffKind.Debuff) score += 4f;
                    var gift = s.Content.TryBuff(a.ApplyBuff);
                    if (gift != null && gift.Kind == BuffKind.Buff && !t.HasBuff(gift.Id) && s.LivingList(Team.Enemy).Count > 0)
                    {
                        // Only worth a turn when the fight is close: a foe within 2 tiles of some hero.
                        bool close = false;
                        foreach (var ally in s.LivingList(Team.Hero))
                            foreach (var e in s.LivingList(Team.Enemy)) if (e.DistanceTo(ally) <= 2) close = true;
                        if (gift.Taunts)
                        {
                            // Taunt (§14.1.1): worth it when the tank is sturdy and allies are exposed or casting.
                            if (t.HpFraction >= 0.45f)
                            {
                                // Records showed the bot almost never taunting: value it by the exposed allies it would cover.
                                int exposed = 0;
                                foreach (var ally in s.LivingList(Team.Hero))
                                    if (ally != t) foreach (var e in s.LivingList(Team.Enemy)) if (e.IsAdjacentTo(ally) || e.DistanceTo(ally) <= Math.Max(1, e.Def.AttackRange)) { exposed++; break; }
                                if (exposed > 0 || s.HeroCastsInFlight > 0) score += 6f + 5f * exposed + 3f * s.HeroCastsInFlight;
                            }
                        }
                        else if (gift.OutgoingDamageMultiplier > 1f) score += close ? 1.2f : 0f;
                        else score += close ? 1f : 0.3f;
                    }
                    if ((a.BaseDamage > 0 && a.BaseHeal <= 0) || (a.ApplyBuff != null && s.Content.TryBuff(a.ApplyBuff)?.Kind == BuffKind.Debuff)) score -= 20f; // harm+heal spares friends (GG-T183)
                }
            }
            // A lone hero's bar is its only way to hurt anything (no pincers with one hero): spend mana and items on
            // damage freely rather than hoarding them for a pincer that can never come (GG-T123).
            bool lone = EnemyPlanner.LoneHero(s) && score > 0f && a.BaseDamage > 0;
            if (a.Kind == AbilityKind.Spell) score -= a.Cost * (lone ? 0.5f : 1.5f);
            if (a.Kind == AbilityKind.Item) score -= lone ? 1f : 4f;
            return score;
        }
    }

    /// <summary>Plays a battle to completion with a bot; returns the final state (or throws on a stuck battle).</summary>
    public static class BotRunner
    {
        public static BattleState Play(ContentDb content, BattleSetup setup, HeroBot bot, int maxActions, Action<BattleCommand, List<BattleEvent>, BattleState> observe = null)
        {
            var events = new List<BattleEvent>();
            var s = BattleEngine.Create(content, setup, events);
            observe?.Invoke(null, events, s);
            int refusedInARow = 0;
            for (int i = 0; i < maxActions && !s.IsOver; i++)
            {
                var cmd = bot.Decide(s);
                var ev = BattleEngine.Apply(s, cmd);
                observe?.Invoke(cmd, ev, s);
                bool acted = ev.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent);
                refusedInARow = acted ? 0 : refusedInARow + 1;
                if (refusedInARow > 5) throw new InvalidOperationException($"Bot stuck: {cmd} refused repeatedly ({string.Join("; ", ev)})");
            }
            return s;
        }
    }
}
