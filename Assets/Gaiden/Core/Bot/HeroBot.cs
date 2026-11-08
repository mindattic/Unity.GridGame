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

        /// <summary>
        /// GG-T292: the smart bot reads the bar (§2). It weighs a pending hero cast against the foes that act before it
        /// lands, so it pushes back or Slows a foe to let the cast fire first, prices a planned cast by the blows that land
        /// before it, values a pincer by the turn its pushback costs a foe about to act, sets up the next pincer, and
        /// Guards rather than shuffling when nothing worth a turn is on offer. Off for the casual bot (an unthinking
        /// player): on by default only for a bot with no randomness.
        /// </summary>
        public bool TimelineAware;
        /// <summary>A/B knob for balance soaks: a TimelineAware bot that still plans the board but ignores the bar.</summary>
        public bool IgnoreBar;
        private bool ReadsBar => TimelineAware && !IgnoreBar;

        /// <summary>Smart bot: where each hero last moved from, so it never shuttles a hero back and forth (GG-T292).</summary>
        private readonly Dictionary<int, Coord> cameFrom = new Dictionary<int, Coord>();
        /// <summary>Smart bot: actions in a row that took no enemy HP (a stall), and the enemy HP total last seen.</summary>
        private int stalled;
        private int lastEnemyHp = -1;

        /// <summary>
        /// GG-T311: seconds the bot hesitates with a hero held before it moves (route planning), on top of the human drag
        /// model (<see cref="BattleEngine.DragSeconds"/>). The smart bot 0.4 s, the casual bot a full second.
        /// </summary>
        public float ThinkSeconds;
        public const float SmartThinkSeconds = 0.4f, CasualThinkSeconds = 1f;
        /// <summary>GG-T311: a hop of this many tiles or fewer takes only <see cref="QuickThinkFactor"/> of the think time.</summary>
        public const int QuickDragTiles = 2;
        public const float DefaultQuickThinkFactor = 0.5f;
        public float QuickThinkFactor = DefaultQuickThinkFactor;
        /// <summary>GG-T311: after this many windows in a row in which it had nothing it could do in time (no candidate at
        /// all, not a choice to Guard) the bot hurries (no think time) — a player locked out of a window they could use
        /// eventually just moves.</summary>
        public const int HurryAfterLockouts = 2;
        private int lockouts;
        /// <summary>GG-T306: windows in a row without drawing blood after which the smart bot drags with no think time.</summary>
        public const int StallHurry = 4;
        private bool lockedOut;

        public HeroBot(int seed, float randomness = 0f)
        {
            rng = new Rng(seed);
            Randomness = randomness;
            TimelineAware = randomness <= 0f;
            ThinkSeconds = TimelineAware ? SmartThinkSeconds : CasualThinkSeconds;
        }

        /// <summary>GG-T321: the skilled-human profile, the balance probe for difficulty 10. It reads the board and the bar
        /// like the smart bot, but takes a human's time to plan a route and sometimes picks a near-best move
        /// (<see cref="SkillLevel"/>). The smart bot plays the 4 s timeline near-perfectly, so it stays as the ceiling reference.</summary>
        public const float SkilledSkillLevel = 80f, SkilledThinkSeconds = 0.8f;

        public static HeroBot Skilled(int seed) => new HeroBot(seed) { SkillLevel = SkilledSkillLevel, ThinkSeconds = SkilledThinkSeconds };

        /// <summary>The best drag on offer (hints and automation teach moving, so they ask for a move specifically).</summary>
        public MoveCommand BestMove(BattleState s) => Decide(s, movesOnly: true) as MoveCommand;

        public BattleCommand Decide(BattleState s)
        {
            var cmd = Decide(s, false);
            if (cmd is MoveCommand m && s.UnitById(m.HeroId) is Unit mover) cameFrom[m.HeroId] = mover.Pos;
            lockouts = lockedOut ? lockouts + 1 : cmd is ShieldCommand ? lockouts : 0;
            return cmd;
        }

        private BattleCommand Decide(BattleState s, bool movesOnly)
        {
            if (!movesOnly) lockedOut = false;
            // GG-T308: a stall is counted per window (the party now takes several actions in one).
            bool freshWindow = !s.Units.Exists(u => u.IsHero && u.Alive && u.ActedThisWindow);
            if (TimelineAware && freshWindow)
            {
                int hp = 0;
                foreach (var e in s.LivingList(Team.Enemy)) hp += e.Hp;
                stalled = lastEnemyHp >= 0 && hp >= lastEnemyHp ? stalled + 1 : 0;
                lastEnemyHp = hp;
            }
            // A recruitable whose every term is met (GG-T132) is worth more than any swing: talk it round first.
            if (!movesOnly && Recruitment.ReadyTalk(s) is TalkCommand talk) return talk;
            // GG-T311: no hero's action fits the gap — the party has to give the window up.
            if (BattleEngine.TooLittleTime(s)) return movesOnly ? null : new ShieldCommand();
            var heroes = s.LivingList(Team.Hero);
            bool lone = EnemyPlanner.LoneHero(s);
            var candidates = new List<(BattleCommand cmd, float score)>();
            // GG-T66: displacement bonus per move candidate (index into candidates), applied only when no pincer is on offer.
            var displacement = new List<(int index, float bonus)>();
            bool pincerAvailable = false;
            // GG-T188/GG-T311 (§2.0): picking a hero up starts the clock and the bot drags like a human — its think time,
            // the pickup, then DragSecondsPerTile a tile — so a path longer than the window is cut where the time runs out
            // (forced drop), and a gap too short for pickup + think + one tile offers no drag at all.
            // A short hop (QuickDragTiles or fewer) is half the deliberation, so a ~1 s gap still fits one.
            float window = BattleEngine.TurnWindowSeconds(s);
            // GG-T306: a stalled smart bot hurries too — a party with only buffs on its bars never counted as locked out,
            // so against a fast regenerating foe (1 s gaps) it buffed for ever instead of walking one tile at a time.
            float thinkNow = lockouts >= HurryAfterLockouts || stalled >= StallHurry ? 0f : ThinkSeconds;
            int fullSteps = BattleEngine.MaxDragSteps(s, thinkNow);
            int quickSteps = Math.Min(QuickDragTiles, BattleEngine.MaxDragSteps(s, thinkNow * QuickThinkFactor));
            int maxSteps = Math.Max(fullSteps, quickSteps);

            foreach (var h in heroes)
            {
                // GG-T311: a gap too short to drag in still leaves this hero's bar (no `continue` past the slots).
                bool canDrag = BattleEngine.CanMove(s, h.Id) && maxSteps > 0;
                void AddDrag(List<Coord> full)
                {
                    var path = full.Count > maxSteps ? full.GetRange(0, maxSteps) : full;
                    var pv = BattleEngine.PreviewMove(s, h.Id, path);
                    if (pv.StepsTaken.Count == 0) return;
                    int walked = pv.StepsTaken.Count;
                    float think = walked <= quickSteps && full.Count <= quickSteps ? thinkNow * QuickThinkFactor : thinkNow;
                    float held = full.Count > maxSteps ? window : think + BattleEngine.DragSeconds(walked);
                    if (window > 0f) held = Math.Min(held, window);
                    float score = ScoreMove(s, pv, h);
                    if (ReadsBar) score += MoveTimeValue(s, pv, h, held);
                    if (!pincerAvailable && HasRealPincer(s, pv)) pincerAvailable = true;
                    float push = lone ? 0f : DisplacementScore(s, pv, h, TimelineAware);
                    if (push > 0f) displacement.Add((candidates.Count, push));
                    candidates.Add((new MoveCommand { HeroId = h.Id, Path = new List<Coord>(pv.StepsTaken), HeldSeconds = held }, score));
                }
                for (int y = 0; canDrag && y < BattleConfig.Rows; y++)
                    for (int x = 0; x < BattleConfig.Cols; x++)
                    {
                        var dest = new Coord(x, y);
                        if (dest == h.Pos) continue;
                        foreach (var full in LPaths(h.Pos, dest)) AddDrag(full);
                    }
                // GG-T306 (§14.1.3): the smart bot also asks the shared pincer planner for a short winding walk (swapping
                // through allies and foes) that the straight L-drags miss: one that completes a pincer, sets up an end
                // or slots a supporter into a chain. It is previewed and scored like any other drag, inside the same
                // minimum-action-time limit (maxSteps, the drag that fits the gap).
                if (canDrag && TimelineAware && !lone)
                {
                    var plan = PincerPlanner.Plan(s, h, Math.Min(maxSteps, PincerPlanner.MaxSearchSteps));
                    if (plan.Kind != PincerPlanKind.None && plan.Path.Count > 0 && !IsLPath(h.Pos, plan.Path)) AddDrag(plan.Path);
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

            lockedOut = candidates.Count == 0 && !movesOnly;
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
            if (!movesOnly && ReadsBar && !lone && best < GuardValue(s)) return new ShieldCommand();
            if (!movesOnly && best < BattleConfig.BotGuardBelowScore && (lone || rng.Chance(BattleConfig.BotGuardChance))) return new ShieldCommand();
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

        /// <summary>True when <paramref name="path"/> is one of <see cref="LPaths"/> to its own end (already a candidate).</summary>
        private static bool IsLPath(Coord from, List<Coord> path)
        {
            foreach (var l in LPaths(from, path[path.Count - 1]))
            {
                if (l.Count != path.Count) continue;
                bool same = true;
                for (int i = 0; i < l.Count && same; i++) same = l[i] == path[i];
                if (same) return true;
            }
            return false;
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
                    // An AxisWard (§14.9) turns a row pincer aside: it only cracks the ward, so a column pincer is worth far more.
                    if (RuleBreakRules.WardsPincer(o, pv.Positions.TryGetValue(p.A.Id, out var wa) ? wa : p.A.Pos, pv.Positions.TryGetValue(p.B.Id, out var wb) ? wb : p.B.Pos))
                    { score += 3f; continue; }
                    score += 10f;
                    if (o.HasPlate)
                    {
                        // A plated foe: an attacker on its facing side only dents the plate.
                        foreach (var att in new[] { p.A, p.B })
                            if (pv.Positions.TryGetValue(att.Id, out var ap) && SideFacing(o, ap)) score -= 4f;
                    }
                    var e = s.TurnEntryOf(o.Id);
                    if (e != null && BattleEngine.SecondsUntilAct(s, e) <= BattleConfig.PushbackMaxSeconds + 1f) score += 4f; // the pincer pushes a foe about to act (GG-T286)
                    if (ReadsBar) // GG-T292: what the pincer's pushback buys on the bar, if the foe lives through it
                        score += (o.HpFraction < 0.4f ? 0.5f : 1f) * PushValue(s, o, Formulas.SecondsToTicks(Math.Min(BattleConfig.PincerPushbackSeconds, BattleConfig.PushbackMaxSeconds)));
                    if (o.HpFraction < 0.4f) score += 3f;
                    if (ChargesTempoDrain(s, o)) score += 6f; // GG-T275: staggering a Temporal Drag keeps the party's tempo
                }
                // GG-T306: the smart bot weighs each supporter by what its support effect brings (PincerPlanner.SupportWorth).
                if (TimelineAware) { foreach (var su in p.SupportersA) score += 0.6f * PincerPlanner.SupportWorth(su); foreach (var su in p.SupportersB) score += 0.6f * PincerPlanner.SupportWorth(su); }
                else score += 2f * (p.SupportersA.Count + p.SupportersB.Count);
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
            // Region rules (GG-T359): crossing a hazardous painted tile costs a little, ending on one (or under a
            // telegraphed hazard about to land) costs a lot, so soaks walk around barricade fires and out of strike zones.
            if (s.Region.Tiles.Count > 0 || s.Timeline.Exists(x => x.Kind == TimelineKind.Hazard))
            {
                foreach (var step in pv.StepsTaken) score -= 1.5f * RegionBoard.DangerAt(s, step, hero);
                if (pv.Positions.TryGetValue(hero.Id, out var end)) score -= 6f * RegionBoard.DangerAt(s, end, hero);
                else if (RegionBoard.DangerAt(s, hero.Pos, hero) > 0f) score -= 6f; // stepping off a danger tile is worth it
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
                if (TimelineAware && pv.Pincers.Count == 0)
                {
                    // GG-T292: no pincer now — stand where an ally can close one next turn, never shuttle back, and
                    // close in harder the longer the party has gone without drawing blood.
                    if (!lone) score += SetupValue(s, pv, hero, at);
                    if (cameFrom.TryGetValue(hero.Id, out var back) && back == at) score -= 10f;
                    if (stalled >= 4)
                    {
                        int nearest = 99;
                        foreach (var e in s.LivingList(Team.Enemy))
                            if (!e.IsCorpse && pv.Positions.TryGetValue(e.Id, out var ep)) nearest = Math.Min(nearest, Math.Abs(ep.X - at.X) + Math.Abs(ep.Y - at.Y));
                        score += 0.4f * (8 - Math.Min(8, nearest));
                    }
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
        private static float DisplacementScore(BattleState s, MovePreview pv, Unit mover, bool onlySetups = false)
        {
            float score = 0f;
            foreach (var u in s.Units)
            {
                if (!u.Alive || u.IsCorpse || u.IsNeutral || u.Id == mover.Id) continue; // shuffling a neutral sets nothing up
                if (!pv.Positions.TryGetValue(u.Id, out var to) || to == u.Pos) continue;
                if (!onlySetups) score += 6f; // GG-T292: the smart bot pays only for a shove that sets a pincer up (no shuffling loops)
                var other = u.Team == Team.Hero ? Team.Enemy : Team.Hero;
                foreach (var v in s.LivingList(other))
                {
                    if (v.IsCorpse || !pv.Positions.TryGetValue(v.Id, out var vp)) continue;
                    if (Math.Abs(vp.X - to.X) + Math.Abs(vp.Y - to.Y) == 1) { score += onlySetups ? 5f : 3f; break; }
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

        /// <summary>True while <paramref name="o"/> charges a spell that would lower the party's tempo (a Chronomancer's Drag),
        /// or a siphon that would drain orbs the party still holds (GG-T332).</summary>
        internal static bool ChargesTempoDrain(BattleState s, Unit o)
        {
            foreach (var e in s.Timeline)
            {
                if (e.Kind != TimelineKind.Cast || !e.IsCharge || e.OwnerId != o.Id) continue;
                var ability = s.Content.TryAbility(e.AbilityId);
                if (ability != null && ability.DrainsOrbs > 0 && s.Bank.Count > 0) return true;
                var buff = s.Content.TryBuff(ability?.ApplyBuff);
                if (buff != null && buff.Tempo < 0) return true;
            }
            return false;
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
            bool close = FightIsClose(s);
            int castTicks = a.Kind == AbilityKind.Spell && a.CastTime > 0f ? Formulas.SecondsToTicks(Formulas.CastTime(a.CastTime, caster.Effective)) : 0;
            // GG-T266: a beneficial buff on a foe-targeting ability lands on the caster, once per cast.
            var selfGift = a.Filter == TargetFilter.EnemyOnly ? s.Content.TryBuff(a.ApplyBuff) : null;
            if (selfGift != null && selfGift.Kind == BuffKind.Buff && targets.Count > 0 && !caster.HasBuff(selfGift.Id))
                score += close ? BuffValue(selfGift, caster, s) : 0.3f;
            // GG-T246: effects that don't depend on who is caught in the shape.
            if (a.Revives)
            {
                foreach (var u in s.Units) if (u.Team == Team.Hero && !u.Alive && !u.IsCorpse) { score += 40f; break; }
            }
            if (a.GrantsOrbs > 0) score += Math.Min(a.GrantsOrbs, s.Bank.Capacity - s.Bank.Count) * 3f - (s.Bank.IsFull ? 6f : 0f);
            if (a.StepBack > 0 && caster.HpFraction < 0.6f)
                foreach (var e in s.LivingList(Team.Enemy)) if (e.IsAdjacentTo(caster)) { score += 3f; break; }
            foreach (var t in targets)
            {
                // A recruitable that wants healing (GG-T132) is worth a heal even at half HP.
                if (t.IsNeutral && a.BaseHeal > 0 && t.Recruitable != null && t.Recruitable.WantsHealing && !t.RecruitMarks.Contains("healed")) score += 10f;
                if (t.Team != caster.Team && !t.IsNeutral)
                {
                    if (BattleEngine.IsArmored(s, t)) continue;
                    bool kills = false;
                    if (a.BaseDamage > 0)
                    {
                        int dmg = (int)Math.Round(Formulas.SpellDamage(a, t, false) * Formulas.CasterPower(a, caster) * caster.OutgoingDamageMultiplier);
                        score += Math.Min(t.Hp, dmg) * 0.6f + (t.Hp <= dmg ? 8f : 0f);
                        kills = t.Hp <= dmg;
                    }
                    var curse = s.Content.TryBuff(a.ApplyBuff);
                    if (curse != null && !t.HasBuff(curse.Id)) score += DebuffValue(curse) * Math.Max(0.25f, a.ApplyBuffChance);
                    if (a.StealsMana) score += s.Bank.IsFull ? 0f : 3f;
                    if (a.HastenU > 0) score -= 5f;
                    if (!ReadsBar) { if (a.Pushback > 0f) score += 3f; } // GG-T183: shoving a foe back on the timeline buys the party a turn
                    else if (!kills)
                    {
                        // GG-T292: the smart bot prices a push or a Slow by what it buys on the bar: a pending cast that now
                        // lands first, a threatening foe's blow postponed, a charge staggered. A cast-time spell only buys
                        // it if it lands before the foe acts.
                        int act = EnemyActTicks(s, t);
                        float landsFirst = castTicks == 0 || (act >= 0 && act >= castTicks) ? 1f : 0.3f;
                        if (a.Pushback > 0f) score += landsFirst * (1f + PushValue(s, t, Formulas.SecondsToTicks(Math.Min(a.Pushback, BattleConfig.PushbackMaxSeconds))));
                        if (curse != null && curse.TimelineSpeedMultiplier < 1f && !t.HasBuff(curse.Id))
                            score += landsFirst * Math.Max(0.25f, a.ApplyBuffChance) * SlowValue(s, t, curse.TimelineSpeedMultiplier);
                    }
                    // GG-T336: a Countermarch trades this foe's turn with the furthest-off foe's: worth what postponing it buys,
                    // less what the straggler brings forward; the casual bot only sends a threatening foe to the back.
                    if (a.SwapsTurns) score += ReadsBar ? CountermarchValue(s, t) : (Countermarch.Partner(s, t) != null && Threatens(s, t) ? 3f : -5f);
                    if ((a.BaseDamage > 0 || a.Pushback > 0f) && ChargesTempoDrain(s, t)) score += 5f; // GG-T275: stagger a Temporal Drag
                    if (a.BaseHeal > 0 && a.Filter == TargetFilter.EnemyOnly) score += Math.Min(caster.MaxHp - caster.Hp, a.BaseHeal) * 0.3f; // drain
                    if (a.Knockback > 0)
                    {
                        // Peeling a foe off a hurt hero is worth more than shoving it for its own sake.
                        bool peel = false;
                        foreach (var h in s.LivingList(Team.Hero)) if (h.IsAdjacentTo(t) && h.HpFraction < 0.5f) peel = true;
                        score += 1.5f * a.Knockback + (peel ? 3f : 0f);
                        // GG-T368: a shove into an open hole (or onto a floor about to give) is worth a lot more.
                        var d = new Coord(t.Pos.X - caster.Pos.X, t.Pos.Y - caster.Pos.Y);
                        var dir = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Coord(Math.Sign(d.X), 0) : new Coord(0, Math.Sign(d.Y));
                        score += 12f * RegionBoard.ShoveFloorValue(s, t, dir, a.Knockback);
                    }
                    if (a.Pull > 0)
                    {
                        score += 2.5f;
                        int px = caster.Pos.X - t.Pos.X, py = caster.Pos.Y - t.Pos.Y;
                        var pdir = Math.Abs(px) >= Math.Abs(py) ? new Coord(Math.Sign(px), 0) : new Coord(0, Math.Sign(py));
                        score += 12f * RegionBoard.ShoveFloorValue(s, t, pdir, a.Pull, caster); // GG-T368: drag a foe into a hole
                    }
                    if (a.SwapsPlaces) score += 2f;
                }
                else
                {
                    if (a.BaseHeal > 0) score += Math.Min(t.MaxHp - t.Hp, a.BaseHeal) * (t.HpFraction < 0.5f ? 0.9f : 0.2f);
                    if (a.RemovesDebuffs) foreach (var b in t.Buffs) if (b.Def.Kind == BuffKind.Debuff) score += 4f;
                    if (a.RemovesBuff != null && t.HasBuff(a.RemovesBuff)) score += 5f;
                    if (s.NetTempo < 0)
                        // GG-T275: lifting a Slow (a negative-tempo debuff) gives the party its tempo back.
                        foreach (var b in t.Buffs)
                            if (b.Def.Tempo < 0 && (a.RemovesDebuffs || a.RemovesBuff == b.Def.Id)) { score += 4f * -b.Def.Tempo; break; }
                    if (a.HastenU > 0f && t.Alive && t.IsHero && s.LivingList(Team.Enemy).Count > 0
                        && BattleEngine.EnemyTempoMultiplier(s) > BattleConfig.TempoMinMultiplier + 0.01f)
                    {
                        // Party tempo (GG-T268): each point slows every enemy icon; worth most while foes are near.
                        int pts = Math.Max(1, (int)Math.Round(a.HastenU / BattleConfig.HastenUPerTempo));
                        score += pts * (close ? 5f : 2.5f);
                    }
                    var gift = s.Content.TryBuff(a.ApplyBuff);
                    if (gift != null && gift.Kind == BuffKind.Buff && !t.HasBuff(gift.Id) && s.LivingList(Team.Enemy).Count > 0)
                    {
                        // Only worth a turn when the fight is close: a foe within 2 tiles of some hero.
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
                        else score += close ? BuffValue(gift, t, s) : 0.3f;
                    }
                    if ((a.BaseDamage > 0 && a.BaseHeal <= 0) || (a.ApplyBuff != null && s.Content.TryBuff(a.ApplyBuff)?.Kind == BuffKind.Debuff)) score -= 20f; // harm+heal spares friends (GG-T183)
                }
            }
            // A lone hero's bar is its only way to hurt anything (no pincers with one hero): spend mana and items on
            // damage freely rather than hoarding them for a pincer that can never come (GG-T123).
            // GG-T333: an essence heal costs the caster HP. Never bleed below 40% for it; otherwise price the HP given.
            if (a.LifeCost > 0f && score > 0f)
            {
                int cost = Formulas.LifeCostHp(a, caster);
                if (cost <= 0 || caster.Hp - cost < caster.MaxHp * 0.4f) return 0f;
                score -= cost * 0.3f;
            }
            bool lone = EnemyPlanner.LoneHero(s) && score > 0f && a.BaseDamage > 0;
            if (ReadsBar && castTicks > 0 && score > 0f) score -= LatePenalty(s, a, targets, castTicks);
            if (ReadsBar && score > 0f) score += AbilityTimeValue(s, caster, a, targets);
            if (a.Kind == AbilityKind.Spell)
            {
                score -= a.Cost * OrbPenalty(s, lone);
                // GG-T373: while the party saves for a big spell it can't yet afford, a cheaper spell's orbs also cost the
                // turns they push that spell back, so only a cheap spell worth clearly more than its price is cast.
                int goal = lone ? 0 : SavingFor(s);
                if (goal > a.Cost) score -= a.Cost * (TimelineAware ? SaveSurchargeSmart : SaveSurchargeCasual);
            }
            if (a.Kind == AbilityKind.Item) score -= lone ? 1f : 4f;
            return score;
        }

        // ── GG-T292: reading the bar (the smart bot) ────────────────────────────────────────────────────────────

        /// <summary>Clock ticks until this foe next acts: its charge landing if it holds one, else its turn icon; -1 never.</summary>
        internal static int EnemyActTicks(BattleState s, Unit f)
        {
            var e = s.CastEntryOf(f.Id) ?? s.TurnEntryOf(f.Id);
            return e == null ? -1 : BattleEngine.TicksUntilAct(s, e);
        }

        /// <summary>Can this foe's next action hurt a hero: a charge in hand, or a hero within its move plus reach.</summary>
        internal static bool Threatens(BattleState s, Unit f, Unit hero = null)
        {
            if (f.IsCorpse || f.IsNeutral) return false;
            if (s.CastEntryOf(f.Id) != null && hero == null) return true;
            int reach = Math.Max(0, f.Def.MoveRange) + Math.Max(1, f.Def.AttackRange);
            foreach (var h in s.LivingList(Team.Hero))
                if ((hero == null || h == hero) && f.DistanceTo(h) <= reach) return true;
            return false;
        }

        /// <summary>
        /// What delaying foe <paramref name="f"/> by <paramref name="ticks"/> buys: every pending hero cast that it acted
        /// before and now acts after (worth what that cast does to or against it), the next blow of a threatening foe
        /// postponed, a charge staggered.
        /// </summary>
        internal static float PushValue(BattleState s, Unit f, int ticks)
        {
            int before = EnemyActTicks(s, f);
            if (before < 0 || ticks <= 0) return 0f;
            float v = LandsFirstValue(s, f, before, before + ticks);
            if (s.CastEntryOf(f.Id) != null) v += 2f;
            else if (Threatens(s, f) && before == BattleEngine.TurnTicks(s)) v += 1.5f;
            return v;
        }

        /// <summary>GG-T356: seconds of delay at which a Countermarch has bought the party a full window to answer the
        /// postponed foe (heal its prey, step out of reach, kill it); less delay buys proportionally less.</summary>
        public const float CountermarchFullDelaySeconds = 3f;
        /// <summary>GG-T356: a Countermarch spends a hero action, so it is only worth one when it averts at least this much
        /// harm: a heavy blow (about 60% of a hero's HP), a completed enemy pincer or a KO. Below it the bot skips it.</summary>
        public const float CountermarchMinValue = 6f;

        /// <summary>
        /// GG-T336/GG-T356: what a Countermarch on <paramref name="f"/> is worth to the smart bot, priced by the time it
        /// buys. It is a trade, not a push: the window still ends at the same moment, but the foe acts at the straggler's
        /// time and the straggler at the foe's. So it is worth the harm of the foe's next turn, scaled by how many seconds
        /// it is delayed (<see cref="CountermarchFullDelaySeconds"/> buys all of it), less the harm the straggler now does
        /// at that moment, plus the pending hero casts that now land before the foe (less those that no longer beat the
        /// straggler). Full weight when the foe is the next to act, less for one further back (others act first anyway).
        /// Under <see cref="CountermarchMinValue"/> it averts nothing dangerous and is skipped (non-positive).
        /// Nothing to trade: -5.
        /// </summary>
        public static float CountermarchValue(BattleState s, Unit f)
        {
            var mine = Countermarch.OnBar(s, f);
            var theirs = mine == null ? null : Countermarch.Partner(s, f);
            if (theirs == null) return -5f;
            var other = s.UnitById(theirs.OwnerId);
            int myAt = BattleEngine.TicksUntilAct(s, mine), theirAt = BattleEngine.TicksUntilAct(s, theirs);
            // Turn icons only: a charge the foe already holds still lands when it would.
            float v = LandsFirstValue(s, f, myAt, theirAt) - LandsFirstValue(s, other, myAt, theirAt);
            float bought = Math.Min(1f, (theirAt - myAt) / (float)Formulas.SecondsToTicks(CountermarchFullDelaySeconds));
            int window = BattleEngine.TurnTicks(s);
            float weight = window < 0 || myAt <= window ? 1f : 0.4f;
            v += weight * (bought * NextTurnHarm(s, f) - NextTurnHarm(s, other));
            return v >= CountermarchMinValue ? v : Math.Min(v, 0f) - 2f;
        }

        /// <summary>
        /// GG-T356: how much harm foe <paramref name="f"/>'s next turn would do to the party, in score points: the worst
        /// blow on a hero it can reach (10 for a blow of its whole HP, +12 when it would KO), doubled and +4 when its turn
        /// completes an enemy pincer (both ends strike, undodgeable), or 0.7 of the charge it would start (the charge lands
        /// later). Mirrors the enemy turn (RunEnemyTurn): a completing pincer, else a charge, else step and strike.
        /// </summary>
        public static float NextTurnHarm(BattleState s, Unit f)
        {
            if (f == null || !f.Alive || f.IsCorpse || f.IsNeutral || f.IsImmobile || f.IsRemains || f.IsEgg) return 0f;
            var pincer = EnemyPlanner.PlanPincer(s, f);
            bool completes = pincer != null && pincer.Kind == PincerPlanKind.Complete;
            if (!completes && s.CastEntryOf(f.Id) == null)
            {
                var charge = EnemyPlanner.PlanCast(s, f, false);
                if (charge != null)
                    return charge.Target != null && charge.Target.IsHero ? 0.7f * BlowHarm(Formulas.ExpectedCharge(charge.Ability, f, charge.Target), charge.Target) : 0f;
            }
            float worst = 0f;
            foreach (var h in s.LivingList(Team.Hero))
            {
                if (!Threatens(s, f, h)) continue;
                float dmg = Formulas.ExpectedPhysical(f, h, f.OutgoingDamageMultiplier) * (completes ? 2f : 1f);
                worst = Math.Max(worst, BlowHarm(dmg, h));
            }
            return worst > 0f && completes ? worst + 4f : worst;
        }

        private static float BlowHarm(float dmg, Unit h) => 10f * Math.Min(1f, dmg / Math.Max(1, h.Hp)) + (dmg >= h.Hp ? 12f : 0f);

        /// <summary>The same for a Slow (turn-icon rate × <paramref name="mult"/>): a charge isn't slowed, and a stun isn't.</summary>
        internal static float SlowValue(BattleState s, Unit f, float mult)
        {
            var e = s.TurnEntryOf(f.Id);
            if (e == null || s.CastEntryOf(f.Id) != null || !(mult < 1f)) return 0f;
            int before = BattleEngine.TicksUntilAct(s, e);
            if (before < 0) return 0f;
            int rate = BattleEngine.TurnRatePerTick(s, f);
            int slowRate = Math.Max(1, (int)Math.Round(rate * mult));
            int left = Math.Max(0, BattleConfig.BarLength - e.Ip);
            int after = Math.Max(0, e.RecoverTicks) + Math.Max(0, e.StunTicks) + (left + slowRate - 1) / slowRate; // recovery isn't slowed (GG-T318)
            // A Slow lasts its turns, so the lap after is slowed too: a threatening foe loses most of a turn over the fight.
            return LandsFirstValue(s, f, before, after) + (Threatens(s, f) ? 2f : 0.5f);
        }

        /// <summary>Hero casts in flight that foe <paramref name="f"/> acted before (before ticks) and acts after (after ticks).</summary>
        private static float LandsFirstValue(BattleState s, Unit f, int before, int after)
        {
            float v = 0f;
            foreach (var c in s.Timeline)
            {
                if (c.Kind != TimelineKind.Cast || c.CasterTeam != Team.Hero) continue;
                int r = BattleEngine.TicksUntilAct(s, c);
                if (r < 0 || before >= r || after < r) continue; // a cast wins a tie (casts resolve first)
                v += CastStake(s, c, f);
            }
            return v;
        }

        /// <summary>What a pending hero cast landing before foe <paramref name="f"/> acts is worth.</summary>
        private static float CastStake(BattleState s, TimelineEntry c, Unit f)
        {
            var a = s.Content.TryAbility(c.AbilityId);
            var caster = s.UnitById(c.OwnerId);
            if (a == null || caster == null) return 0f;
            float v = 0f;
            foreach (var t in TargetResolver.Collect(s, a, caster, c.TargetTile))
            {
                if (t == f && a.BaseDamage > 0)
                {
                    int dmg = (int)Math.Round(Formulas.SpellDamage(a, t, false) * Formulas.CasterPower(a, caster) * caster.OutgoingDamageMultiplier);
                    v += dmg >= t.Hp ? 12f : 2f; // a kill before it acts erases its turn
                }
                else if (t == f && a.ApplyBuff != null) v += 3f; // the debuff is on before it acts
                else if (t.Team == Team.Hero && a.BaseHeal > 0 && t.HpFraction < 0.6f && Threatens(s, f, t)) v += 4f + 8f * (1f - t.HpFraction);
                else if (t.Team == Team.Hero && a.ApplyBuff != null && Threatens(s, f, t)) v += 1.5f;
            }
            if (a.Revives) v += 3f;
            return Math.Min(15f, v);
        }

        /// <summary>A planned cast's exposure: the threatening blows that land before it does.</summary>
        private static float LatePenalty(BattleState s, AbilityDef a, List<Unit> targets, int castTicks)
        {
            float p = 0f;
            foreach (var f in s.LivingList(Team.Enemy))
            {
                int act = EnemyActTicks(s, f);
                if (act < 0 || act >= castTicks) continue;
                foreach (var t in targets)
                {
                    if (t == f && a.BaseDamage > 0) p += 1.5f; // it gets its turn first
                    else if (t.Team == Team.Hero && a.BaseHeal > 0 && t.HpFraction < 0.5f && Threatens(s, f, t)) p += 3f; // the heal may come too late
                }
            }
            return p;
        }

        // ── GG-T308: the party's window is a shared budget ─────────────────────────────────────────────────────

        /// <summary>What one more hero action in this window is worth to the smart bot (in score points).</summary>
        private const float ActionWorth = 4f;

        /// <summary>Heroes other than <paramref name="actor"/> who may still act in this window.</summary>
        private static int OthersLeft(BattleState s, int actor)
        {
            int n = 0;
            foreach (var h in BattleEngine.HeroesLeftToAct(s)) if (h.Id != actor) n++;
            return n;
        }

        /// <summary>
        /// Clock ticks the window gains when the foes in <paramref name="pushed"/> act <paramref name="ticks"/> later: the
        /// window is the soonest enemy turn, so it grows only while a pushed foe was (one of) the next to act.
        /// </summary>
        internal static int WindowGainTicks(BattleState s, ICollection<int> pushed, int ticks)
        {
            int before = BattleEngine.TurnTicks(s);
            if (before < 0 || ticks <= 0 || pushed.Count == 0) return 0;
            int after = int.MaxValue;
            foreach (var e in s.Timeline)
            {
                if (e.Kind != TimelineKind.Turn) continue;
                var o = s.UnitById(e.OwnerId);
                if (o == null || o.IsHero) continue;
                int t = BattleEngine.TicksUntilTurn(s, o.Id);
                if (t <= 0) continue;
                if (pushed.Contains(o.Id)) t += ticks;
                after = Math.Min(after, t);
            }
            return after == int.MaxValue ? 0 : Math.Max(0, after - before);
        }

        /// <summary>
        /// The window against the heroes still to act: seconds bought (a pushback on the next foe to act) are worth the
        /// actions they make room for, and spending the window (a long drag, a forced drop) costs the actions it denies.
        /// </summary>
        private static float TimeBalance(BattleState s, int actor, int spendTicks, int gainTicks)
        {
            int others = OthersLeft(s, actor);
            int window = BattleEngine.TurnTicks(s);
            if (others == 0 || window < 0) return 0f;
            // GG-T311: another hero needs at least its cheapest action's time (a minimum drag or a skill's use time).
            int need = BattleEngine.NeedTicks(Math.Min(BattleEngine.MinDragSeconds, BattleConfig.HeroActionSeconds));
            int room = Math.Min(others, Math.Max(0, window - spendTicks) / need);
            int roomAfter = Math.Min(others, Math.Max(0, window + gainTicks - spendTicks) / need);
            return ActionWorth * (roomAfter - room) - 0.5f * ActionWorth * (others - room);
        }

        /// <summary>A drag spends its hold; a hero pincer pushes every foe it strikes PincerPushbackSeconds back.</summary>
        private static float MoveTimeValue(BattleState s, MovePreview pv, Unit hero, float held)
        {
            var pushed = new HashSet<int>();
            foreach (var p in pv.Pincers) foreach (var o in p.Opponents) if (o.Alive && !o.IsCorpse) pushed.Add(o.Id);
            int gain = WindowGainTicks(s, pushed, Formulas.SecondsToTicks(Math.Min(BattleConfig.PincerPushbackSeconds, BattleConfig.PushbackMaxSeconds)));
            return TimeBalance(s, hero.Id, BattleEngine.CommandTicks(s, new MoveCommand { HeroId = hero.Id, HeldSeconds = held }), gain);
        }

        /// <summary>An ability spends HeroActionSeconds; an instant Pushback, or a sure instant Slow, on the next foe buys time.</summary>
        private static float AbilityTimeValue(BattleState s, Unit caster, AbilityDef a, List<Unit> targets)
        {
            var pushed = new HashSet<int>();
            int push = 0;
            bool instant = a.Kind != AbilityKind.Spell || a.CastTime <= 0f;
            if (instant && a.Pushback > 0f)
            {
                push = Formulas.SecondsToTicks(Math.Min(a.Pushback, BattleConfig.PushbackMaxSeconds));
                foreach (var t in targets) if (t.Team == Team.Enemy && !t.IsCorpse) pushed.Add(t.Id);
            }
            var slow = s.Content.TryBuff(a.ApplyBuff);
            if (instant && push == 0 && slow != null && slow.TimelineSpeedMultiplier > 0f && slow.TimelineSpeedMultiplier < 1f && a.ApplyBuffChance >= 0.99f)
                foreach (var t in targets)
                {
                    int left = t.Team == Team.Enemy && !t.HasBuff(slow.Id) ? BattleEngine.TicksUntilTurn(s, t.Id) : -1;
                    var te = s.TurnEntryOf(t.Id);
                    if (left > 0 && te != null) left -= Math.Max(0, te.RecoverTicks) + Math.Max(0, te.StunTicks); // only time on the bar is slowed (GG-T318)
                    if (left <= 0) continue;
                    pushed.Add(t.Id); // a Slow stretches the foe's remaining ticks on the bar by 1/mult − 1
                    push = Math.Max(push, (int)(left * (1f / slow.TimelineSpeedMultiplier - 1f)));
                }
            int gain = WindowGainTicks(s, pushed, push);
            return TimeBalance(s, caster.Id, BattleEngine.CommandTicks(s, new UseSlotCommand { HeroId = caster.Id }), gain);
        }

        /// <summary>
        /// GG-T292: a move with no pincer that ends beside a foe whose far side is open, with another hero able to reach
        /// it next turn, sets up a pincer.
        /// </summary>
        private static float SetupValue(BattleState s, MovePreview pv, Unit mover, Coord at)
        {
            int reach = BattleEngine.MaxDragSteps(s);
            if (reach == int.MaxValue) reach = 99;
            float best = 0f;
            foreach (var e in s.LivingList(Team.Enemy))
            {
                if (e.IsCorpse || e.W != 1 || e.H != 1 || BattleEngine.IsArmored(s, e)) continue;
                if (!pv.Positions.TryGetValue(e.Id, out var ep)) continue;
                int dx = ep.X - at.X, dy = ep.Y - at.Y;
                if (Math.Abs(dx) + Math.Abs(dy) != 1) continue;
                var far = new Coord(ep.X + dx, ep.Y + dy);
                if (!s.Walkable(far) || s.MovableAt(far) != null) continue;
                bool taken = false;
                foreach (var kv in pv.Positions) if (kv.Value == far && s.UnitById(kv.Key) is Unit u && u.Alive) { taken = true; break; }
                if (taken) continue;
                foreach (var h in s.LivingList(Team.Hero))
                {
                    if (h.Id == mover.Id || !pv.Positions.TryGetValue(h.Id, out var hp)) continue;
                    // GG-T308: an ally still to act this window can close it before the foes move.
                    if (Math.Abs(hp.X - far.X) + Math.Abs(hp.Y - far.Y) <= reach) { best = Math.Max(best, (e.HpFraction < 0.4f ? 4f : 3f) + (h.ActedThisWindow ? 0f : 2f)); break; }
                }
            }
            return best;
        }

        /// <summary>
        /// GG-T292: what Guarding is worth now — the 15% guard on every hero when a threatening foe acts next, plus the
        /// mana its unspent turn banks. Never while the party has stalled (Guarding must not stall a fight further).
        /// </summary>
        private float GuardValue(BattleState s)
        {
            if (stalled >= 3) return float.NegativeInfinity;
            float v = BattleEngine.GuardCharge(s) / 25f; // 40 charge (the cap) ≈ 1.6
            foreach (var f in s.LivingList(Team.Enemy))
                if (EnemyActTicks(s, f) == BattleEngine.TurnTicks(s) && Threatens(s, f)) { v += 1f; break; }
            return v;
        }

        /// <summary>GG-T246: what one orb is worth keeping. A filling bank makes hoarding pointless (every orb minted into
        /// a full bank overflows), so the price of spending falls as it fills.</summary>
        private static float OrbPenalty(BattleState s, bool lone)
        {
            if (lone) return 0.5f;
            if (s.Bank.IsFull) return 0.2f;
            float fill = s.Bank.Capacity > 0 ? (float)s.Bank.Count / s.Bank.Capacity : 0f;
            return 1.5f * (1f - 0.8f * fill);
        }

        /// <summary>GG-T373: a spell of at least this many orbs is one the party saves for (the 5–12 band of the cost curve).</summary>
        public const int SaveForCost = 5;
        /// <summary>GG-T373: extra price per orb of a cheaper spell while saving — the smart bot saves harder than the casual one.</summary>
        public const float SaveSurchargeSmart = 0.6f, SaveSurchargeCasual = 0.3f;

        /// <summary>GG-T373: the orb price of the nearest big spell (<see cref="SaveForCost"/>+) on a living hero's unlocked bar
        /// that the bank can hold but can't pay for yet, or 0 when there's nothing to save for. The nearest, so a party with a
        /// 5- and a 10-orb spell saves up to the 5 first; once the bank holds it, the 10 becomes the goal.</summary>
        public static int SavingFor(BattleState s)
        {
            int goal = 0;
            foreach (var h in s.LivingList(Team.Hero))
            {
                if (h.IsIndependent) continue;
                for (int i = 0; i < h.Bar.Count && i < h.UnlockedSlots; i++)
                {
                    var sl = h.Bar[i];
                    if (sl == null || sl.IsEmpty || sl.IsItem || sl.IsWeapon) continue;
                    var a = s.Content.TryAbility(sl.AbilityId);
                    if (a == null || a.Kind != AbilityKind.Spell || a.Cost < SaveForCost) continue;
                    if (a.Cost <= s.Bank.Count || a.Cost > s.Bank.Capacity) continue;
                    if (goal == 0 || a.Cost < goal) goal = a.Cost;
                }
            }
            return goal;
        }

        private static bool FightIsClose(BattleState s)
        {
            foreach (var ally in s.LivingList(Team.Hero))
                foreach (var e in s.LivingList(Team.Enemy)) if (e.DistanceTo(ally) <= 2) return true;
            return false;
        }

        /// <summary>GG-T246: a debuff on a foe, valued by what it actually does rather than a flat +3.</summary>
        private static float DebuffValue(BuffDef d)
        {
            if (d.Kind != BuffKind.Debuff) return 0f;
            float v = 1f;
            if (d.HaltsTimeline || d.Immobile) v += 6f;
            if (d.TimelineSpeedMultiplier < 1f) v += 4f;
            if (d.IncomingDamageMultiplier > 1f) v += 4f;
            if (d.AccuracyMultiplier < 1f) v += 3f;
            if (d.BlocksSpells) v += 3f;
            if (d.OutgoingDamageMultiplier < 1f) v += 3f;
            if (d.DamagePerTick > 0) v += d.DamagePerTick * Math.Max(1, d.Duration) * 0.5f;
            return v;
        }

        /// <summary>GG-T246: a non-taunt buff on an ally in a close fight, valued by its numbers.</summary>
        private static float BuffValue(BuffDef d, Unit t, BattleState s)
        {
            float v = 1f;
            int turns = d.Unit == DurationUnit.Turns ? Math.Max(1, d.Duration) : 1;
            if (d.OutgoingDamageMultiplier > 1f) v += (d.OutgoingDamageMultiplier - 1f) * 10f * turns;
            bool exposed = false;
            foreach (var e in s.LivingList(Team.Enemy)) if (e.IsAdjacentTo(t) || e.DistanceTo(t) <= Math.Max(1, e.Def.AttackRange)) { exposed = true; break; }
            if (d.IncomingDamageMultiplier < 1f) v += (1f - d.IncomingDamageMultiplier) * (exposed ? 15f : 4f);
            if (d.HealPerTick > 0) v += d.HealPerTick * Math.Max(1, d.Duration) * 0.5f * (1f - t.HpFraction);
            if (d.TimelineSpeedMultiplier > 1f) v += (d.TimelineSpeedMultiplier - 1f) * 10f;
            if (d.AccuracyMultiplier > 1f) v += 2f;
            return v;
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
