using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Region rules (GG-T359, BIBLE §14.20): the battle half. A stage's rules (its theme's plus its own) come into play at
    /// setup; their hooks run from the funnels (battle start, wave spawn, a unit's turn start/end, a unit entering a tile,
    /// a death); monster abilities (<see cref="AbilityDef.RuleEffects"/>) and deaths (<see cref="ClassDef.OnDeathRule"/>)
    /// run the same effects. Every hazard a rule makes goes on the timeline first (<see cref="TimelineKind.Hazard"/>), so
    /// the party always sees it coming. Deterministic: rules in their listed order, hooks in file order, effects in order,
    /// and only RandomFree/RandomUnits areas and a hook's Chance draw from the battle Rng. No-op with no rule in play and
    /// in previews.
    /// </summary>
    internal sealed partial class BattleRunner
    {
        /// <summary>Hooks inside hooks (a rule's damage kills, its death hook moves someone, who enters a tile...) stop here.</summary>
        private const int MaxRegionDepth = 4;
        private int regionDepth;

        /// <summary>Who and what an effect list runs for.</summary>
        private sealed class RuleCtx
        {
            public string Rule, Event, State, Ability;
            /// <summary>The unit whose turn/step/death it is, or the caster; may be null (battle start, wave spawn, a hazard).</summary>
            public Unit Subject;
            /// <summary>Where the subject stands (or fell); null with no subject.</summary>
            public Coord? At;
            /// <summary>An ability's anchor.</summary>
            public Coord? Target;
            /// <summary>The resolving event's telegraphed tiles.</summary>
            public List<Coord> EventTiles;
        }

        private bool RegionActive => !PreviewOnly && !S.IsOver;

        // ── Funnels ────────────────────────────────────────────────────────────────────────────────

        /// <summary>Setup: the stage's rules come into play (each announced), then their BattleStart hooks run.</summary>
        private void RegionBattleStart()
        {
            S.Region = new RegionState();
            foreach (var r in RegionRuleCatalog.ActiveFor(C, S.Stage))
            {
                S.Region.Rules.Add(r.Id);
                Emit(new RegionRuleEvent { RuleId = r.Id, Name = r.Name, Description = r.Description });
            }
            RegionHooks(RuleHook.BattleStart, null);
        }

        private void RegionTurn(RuleHook on, Unit u)
        {
            if (S.Region.Rules.Count == 0 || u == null || !u.Alive) return;
            RegionHooks(on, u);
        }

        /// <summary>A unit moved into a tile (its anchor tile is the one the hooks read).</summary>
        private void RegionEnter(Unit u)
        {
            if (S.Region.Rules.Count == 0 || u == null || !u.Alive || PreviewOnly) return;
            RegionHooks(RuleHook.EnterTile, u);
        }

        /// <summary>A unit fell at <paramref name="at"/>: its class's OnDeathRule, then the UnitDeath hooks.</summary>
        private void RegionDeath(Unit u, Coord at)
        {
            if (!RegionActive || u?.Def == null) return;
            if (u.Def.OnDeathRule.Count > 0)
                RunRuleEffects(u.Def.OnDeathRule, new RuleCtx { Subject = u, At = at });
            if (S.Region.Rules.Count > 0) RegionHooks(RuleHook.UnitDeath, u, at);
        }

        /// <summary>An ability resolved: its rule effects run with the caster as subject and the anchor as Target.</summary>
        private void RegionAbility(Unit caster, AbilityDef a, Coord anchor)
        {
            if (!RegionActive || a == null || a.RuleEffects.Count == 0 || caster == null) return;
            RunRuleEffects(a.RuleEffects, new RuleCtx { Ability = a.Id, Subject = caster, At = caster.Pos, Target = anchor });
        }

        /// <summary>Runs every matching hook of every rule in play for <paramref name="on"/>.</summary>
        private void RegionHooks(RuleHook on, Unit subject, Coord? at = null)
        {
            if (!RegionActive || S.Region.Rules.Count == 0 || regionDepth >= MaxRegionDepth) return;
            Coord? where = at ?? (subject != null ? subject.Pos : (Coord?)null);
            foreach (var ruleId in S.Region.Rules.ToArray())
            {
                var rule = C.TryRegionRule(ruleId);
                if (rule == null) continue;
                for (int i = 0; i < rule.Hooks.Count && !S.IsOver; i++)
                {
                    var h = rule.Hooks[i];
                    if (h.On != on || !TeamMatches(h.Team, subject)) continue;
                    if (h.Wave >= 0 && S.Wave != h.Wave) continue;
                    if (subject != null && RegionBoard.Immune(subject, rule.Id)) continue;
                    if (h.State != null)
                    {
                        var mark = where.HasValue ? S.Region.At(where.Value) : null;
                        if (mark == null || !string.Equals(mark.State, h.State, StringComparison.OrdinalIgnoreCase)) continue;
                        if (subject != null && RegionBoard.Immune(subject, mark.State)) continue;
                    }
                    string key = rule.Id + "#" + i;
                    S.Region.HookCounts.TryGetValue(key, out int n);
                    S.Region.HookCounts[key] = ++n;
                    if (n % Math.Max(1, h.Every) != 0) continue;
                    if (h.Chance < 1f && !S.Rng.Chance(h.Chance)) continue;
                    RunRuleEffects(h.Effects, new RuleCtx { Rule = rule.Id, State = h.State, Subject = subject, At = where });
                }
            }
        }

        private static bool TeamMatches(RuleTeam t, Unit u) =>
            t == RuleTeam.Any || (u != null && (t == RuleTeam.Hero ? u.Team == Team.Hero : u.Team == Team.Enemy));

        // ── The timeline ─────────────────────────────────────────────────────────────────────────

        /// <summary>A queued rule event reached the left edge: it lands on its tiles, then re-arms (Repeat) or leaves the bar.</summary>
        private void ResolveHazard(TimelineEntry e)
        {
            var def = C.TryRuleEvent(e.RuleEvent);
            bool rearm = def != null && def.Repeat && e.TotalCastTicks > 0;
            if (rearm) e.RemainingTicks = e.TotalCastTicks;
            else S.Timeline.Remove(e);
            Emit(new HazardLandedEvent { EntryId = e.Id, EventId = e.RuleEvent, RuleId = e.RuleId, Tiles = new List<Coord>(e.HazardTiles ?? new List<Coord>()), Rearmed = rearm });
            if (def == null || PreviewOnly) return;
            var source = e.SourceUnitId >= 0 ? S.UnitById(e.SourceUnitId) : null;
            if (source != null && !source.Alive) source = null;
            RunRuleEffects(def.Effects, new RuleCtx
            {
                Rule = e.RuleId ?? (C.RuleEventRule.TryGetValue(def.Id, out var owner) ? owner : null),
                Event = def.Id, Subject = source, At = source?.Pos, EventTiles = e.HazardTiles ?? new List<Coord>(),
            });
            CheckEnd();
        }

        /// <summary>Painted tiles with a lifetime fade as the clock runs.</summary>
        private void TickRegionTiles()
        {
            List<Coord> gone = null;
            foreach (var kv in S.Region.Tiles)
            {
                var m = kv.Value;
                if (m.LifeTicks <= 0) continue;
                if (--m.LifeTicks > 0) continue;
                (gone ??= new List<Coord>()).Add(kv.Key);
            }
            if (gone == null) return;
            var byState = new Dictionary<string, RegionTilesEvent>();
            foreach (var c in gone)
            {
                var m = S.Region.Tiles[c];
                S.Region.Tiles.Remove(c);
                if (!byState.TryGetValue(m.State, out var ev0)) byState[m.State] = ev0 = new RegionTilesEvent { RuleId = m.Rule, State = m.State, Change = "cleared" };
                ev0.Tiles.Add(c);
            }
            foreach (var x in byState.Values) Emit(x);
        }

        // ── Effects ──────────────────────────────────────────────────────────────────────────────

        private void RunRuleEffects(List<RuleEffectDef> effects, RuleCtx ctx)
        {
            if (effects == null || effects.Count == 0 || regionDepth >= MaxRegionDepth) return;
            regionDepth++;
            try
            {
                foreach (var e in effects)
                {
                    if (S.IsOver) return;
                    RunRuleEffect(e, ctx);
                }
            }
            finally { regionDepth--; }
        }

        private void RunRuleEffect(RuleEffectDef e, RuleCtx ctx)
        {
            switch (e.Kind)
            {
                case RuleEffectKind.MarkTiles: MarkTiles(e, ctx); break;
                case RuleEffectKind.ClearTiles: ClearTiles(e, ctx); break;
                case RuleEffectKind.ModifyTiles: ModifyTiles(e, ctx); break;
                case RuleEffectKind.Damage:
                    foreach (var u in RuleUnitsOf(e, ctx)) if (u.Alive) Damage(u, e.Amount, null, HitKind.Normal, e.DamageType, RuleLabel(ctx));
                    break;
                case RuleEffectKind.Heal:
                    foreach (var u in RuleUnitsOf(e, ctx)) if (u.Alive) Heal(u, e.Amount, null);
                    break;
                case RuleEffectKind.ApplyBuff:
                {
                    var buff = C.TryBuff(e.Buff);
                    if (buff == null) break;
                    foreach (var u in RuleUnitsOf(e, ctx)) if (u.Alive) ApplyBuff(u, buff, buff.Duration);
                    break;
                }
                case RuleEffectKind.GrantOrbs:
                    foreach (var u in RuleUnitsOf(e, ctx))
                        if (u.Alive && u.IsHero)
                            for (int i = 0; i < e.Amount; i++) MintOrb(u.Id, "rule:" + (ctx.Rule ?? ctx.Event ?? ctx.Ability));
                    break;
                case RuleEffectKind.MoveUnits: MoveUnits(e, ctx); break;
                case RuleEffectKind.Schedule: ScheduleRuleEvent(e, ctx); break;
                case RuleEffectKind.AdjustEvents: AdjustRuleEvents(e); break;
                case RuleEffectKind.PlaceAnchor:
                {
                    var tiles = AreaOf(e, ctx);
                    if (tiles.Count == 0 || string.IsNullOrEmpty(e.Anchor)) break;
                    var from = S.Region.Anchor(e.Anchor);
                    S.Region.Anchors[e.Anchor] = tiles[0];
                    Emit(new RegionAnchorEvent { RuleId = ctx.Rule, Anchor = e.Anchor, From = from, To = tiles[0] });
                    break;
                }
                case RuleEffectKind.MoveAnchor: MoveAnchor(e, ctx); break;
                case RuleEffectKind.SetFlag:
                case RuleEffectKind.ClearFlag:
                {
                    bool on = e.Kind == RuleEffectKind.SetFlag;
                    if (string.IsNullOrEmpty(e.Flag) || (on ? !S.Region.Flags.Add(e.Flag) : !S.Region.Flags.Remove(e.Flag))) break;
                    Emit(new RegionFlagEvent { RuleId = ctx.Rule, Flag = e.Flag, On = on });
                    break;
                }
            }
        }

        /// <summary>The float/banner note a rule's blow carries: the rule's Prose name, else the event or ability.</summary>
        private string RuleLabel(RuleCtx ctx) => C.TryRegionRule(ctx.Rule)?.Name ?? C.TryAbility(ctx.Ability)?.Name ?? ctx.Event ?? "Hazard";

        private void MarkTiles(RuleEffectDef e, RuleCtx ctx)
        {
            var def = C.TryTileState(e.State);
            if (def == null) return;
            string rule = ctx.Rule ?? (C.TileStateRule.TryGetValue(def.Id, out var owner) ? owner : null);
            var ev0 = new RegionTilesEvent { RuleId = rule, State = def.Id, Change = "marked" };
            List<(Unit u, Coord at)> falls = null;
            List<(Coord at, TileMark mark, TileReactionDef r)> reactions = null;
            foreach (var c in AreaOf(e, ctx))
            {
                if (S.IsBlock(c)) continue;
                // GG-T370: an element meets the mark already there (elements.json, GG-T371): the mark reacts and the
                // incoming state is spent (fire melts ice, ice douses fire, fire burns slime away, fire sets a mine off).
                var had = S.Region.At(c);
                if (had != null && !StateMatches(had, def.Id))
                {
                    var react = ElementReaction(def, had);
                    if (react != null) { (reactions ??= new List<(Coord, TileMark, TileReactionDef)>()).Add((c, had, react)); continue; }
                    if (e.KeepMarks) continue;
                }
                // A blocking state is never painted under anyone or any board item: the board stays legal (§1.1, GG-T97).
                // A hole (GG-T368) is the one exception: it opens under whoever stands there, and they fall.
                bool hold = false;
                var faller = def.Blocks ? HoleFaller(def, rule, c, out hold) : null;
                if (hold) continue;
                S.Region.Tiles[c] = NewMark(def, rule, e.Amount);
                ev0.Tiles.Add(c);
                if (faller != null) (falls ??= new List<(Unit, Coord)>()).Add((faller, c));
            }
            if (ev0.Tiles.Count > 0)
            {
                EnsureRuleInPlay(rule);
                Emit(ev0);
                AfterMarked(def, rule, ev0.Tiles, falls);
            }
            if (reactions != null)
                foreach (var (at, mark, r) in reactions)
                    if (!S.IsOver) ReactTile(at, mark, r);
        }

        // ── Elements on tiles and hazard trails (GG-T370, §14.22) ────────────────────────────────

        /// <summary>The elements.json reaction (GG-T371) a state carrying an element sets off on a tile holding
        /// <paramref name="had"/>, or null.</summary>
        private TileReactionDef ElementReaction(TileStateDef incoming, TileMark had)
        {
            if (incoming?.Element == null || C.Elements == null) return null;
            var row = C.Elements.Of(incoming.Element.Value);
            var r = row == null ? null : ElementCounters.Match(row.Tiles, had.State);
            return TileReactions.UsableOnMark(C, r) ? r : null;
        }

        /// <summary>
        /// One tile reaction on a painted tile (an element painted onto it, or a hero's element or tool covering it): Clear
        /// washes the mark off; Becomes turns it into the Into state (a mine into an armed mine, whose OnMark queues its
        /// blast), which brings that state's rule into play. A blocking Into waits under a unit or item, as ever.
        /// </summary>
        private void ReactTile(Coord c, TileMark m, TileReactionDef r)
        {
            if (S.Region.At(c) != m) return;
            if (r.Kind == TileReactionKind.Clear)
            {
                S.Region.Tiles.Remove(c);
                var cleared = new RegionTilesEvent { RuleId = m.Rule, State = m.State, Change = "cleared" };
                cleared.Tiles.Add(c);
                Emit(cleared);
                return;
            }
            var next = C.TryTileState(r.Into);
            if (next == null) return; // an Into that names a trap belongs to the trap system (GG-T371)
            string rule = C.TileStateRule.TryGetValue(next.Id, out var owner) ? owner : m.Rule;
            bool hold = false;
            var faller = next.Blocks ? HoleFaller(next, rule, c, out hold) : null;
            if (hold) return;
            S.Region.Tiles[c] = NewMark(next, rule, 0);
            EnsureRuleInPlay(rule);
            var marked = new RegionTilesEvent { RuleId = rule, State = next.Id, Change = "marked" };
            marked.Tiles.Add(c);
            Emit(marked);
            AfterMarked(next, rule, new List<Coord> { c }, faller != null ? new List<(Unit, Coord)> { (faller, c) } : null);
        }

        /// <summary>A rule whose state reaches the board (a monster's trail, its death, a reaction) comes into play on any
        /// stage, announced like the stage's own, so its hooks run for the tiles it paints.</summary>
        private void EnsureRuleInPlay(string ruleId)
        {
            if (ruleId == null || S.Region.Rules.Contains(ruleId) || !(C.TryRegionRule(ruleId) is RegionRuleDef r)) return;
            S.Region.Rules.Add(r.Id);
            Emit(new RegionRuleEvent { RuleId = r.Id, Name = r.Name, Description = r.Description });
        }

        /// <summary>A unit stepped off <paramref name="from"/>: its class's TrailRule runs there every TrailEvery-th step.</summary>
        private void RegionTrail(Unit u, Coord from)
        {
            if (!RegionActive || u?.Def == null || u.Def.TrailRule.Count == 0 || !u.Alive) return;
            if (++u.TrailSteps < Math.Max(1, u.Def.TrailEvery)) return;
            u.TrailSteps = 0;
            RunRuleEffects(u.Def.TrailRule, new RuleCtx { Subject = u, At = from });
        }

        /// <summary>The painted tiles in <paramref name="area"/> a hero's reactions work on (GG-T370).</summary>
        private List<(Coord at, TileMark mark, TileReactionDef r)> MarkReactions(List<TileReactionDef> reactions, List<Coord> area)
        {
            var list = new List<(Coord, TileMark, TileReactionDef)>();
            if (S.Region.Tiles.Count == 0) return list;
            foreach (var c in area)
                if (S.Region.At(c) is TileMark m && ElementCounters.Match(reactions, m.State) is TileReactionDef r && TileReactions.UsableOnMark(C, r))
                    list.Add((c, m, r));
            return list;
        }

        /// <summary>Can a blocking <paramref name="def"/> take tile <paramref name="c"/>? It waits (<paramref name="hold"/>)
        /// under a board item or a unit, except a hole, which opens under a unit that can fall (returned).</summary>
        private Unit HoleFaller(TileStateDef def, string rule, Coord c, out bool hold)
        {
            hold = false;
            var who = S.UnitAt(c);
            if (who == null && S.ItemFree(c)) return null;
            if (def.Pit && who != null && S.ItemFree(c) && CanFall(who, rule, def.Id)) return who;
            hold = true;
            return null;
        }

        /// <summary>After tiles took <paramref name="def"/>: whoever stood on a hole falls, then the state's OnMark runs on
        /// each tile still holding it.</summary>
        private void AfterMarked(TileStateDef def, string rule, List<Coord> tiles, List<(Unit u, Coord at)> falls)
        {
            if (falls != null)
                foreach (var (u, at) in falls)
                    if (u.Alive && !S.IsOver) Fall(u, at, u.Pos, def, rule);
            if (def.OnMark.Count == 0) return;
            foreach (var c in tiles.ToArray())
            {
                if (S.IsOver) return;
                if (!StateMatches(S.Region.At(c), def.Id)) continue;
                RunRuleEffects(def.OnMark, new RuleCtx { Rule = rule, State = def.Id, Subject = S.UnitAt(c), At = c });
            }
        }

        // ── Holes (GG-T368, §14.21) ──────────────────────────────────────────────────────────────

        /// <summary>Only a one-tile, living, ordinary unit falls (bosses, snakes, remains, corpses and eggs don't), and
        /// never one immune to the hole's rule or state.</summary>
        private static bool CanFall(Unit u, string rule, string state) =>
            u != null && u.Alive && u.OnBoard && !u.IsMultiTile && !u.IsSnakeMember && !u.IsRemains && !u.IsCorpse && !u.IsEgg
            && !RegionBoard.Immune(u, rule, state);

        /// <summary>The open hole on <paramref name="c"/> <paramref name="u"/> would fall into, or null.</summary>
        private TileStateDef PitFor(Unit u, Coord c, out TileMark mark)
        {
            mark = S.Region.At(c);
            if (mark == null || !mark.Blocks) return null;
            var def = C.TryTileState(mark.State);
            return def != null && def.Pit && CanFall(u, mark.Rule, mark.State) ? def : null;
        }

        /// <summary>A shove, pull or rule move would carry <paramref name="u"/> into an open hole on <paramref name="next"/>:
        /// it goes in and falls (true), instead of slamming into it as a wall.</summary>
        private bool TryShoveIntoPit(Unit u, Coord next)
        {
            if (PreviewOnly || S.Region.Tiles.Count == 0 || !BattleState.InBounds(next)) return false;
            var def = PitFor(u, next, out var mark);
            if (def == null) return false;
            Fall(u, next, u.Pos, def, mark.Rule);
            return true;
        }

        /// <summary>
        /// <paramref name="u"/> fell into the hole at <paramref name="hole"/> (it stood there as it opened, or was carried in
        /// from <paramref name="from"/>): it climbs out on the nearest free tile (<see cref="FallLanding"/>), then loses the
        /// hole's FallDamage share of its max HP. On a board with no free tile at all it trades places with the nearest unit
        /// that can be moved, who is jostled onto the hole and fills it in; with nobody to trade with, the hole fills in
        /// under the faller (a full board never loses a unit to a hole).
        /// </summary>
        private void Fall(Unit u, Coord hole, Coord from, TileStateDef def, string rule)
        {
            var fell = new FellEvent { UnitId = u.Id, From = from, Hole = hole, RuleId = rule, SwappedId = -1 };
            var land = FallLanding(u, hole);
            if (land.HasValue)
            {
                if (!Relocate(u, land.Value)) return;
                fell.To = land.Value;
            }
            else
            {
                var other = NearestSwap(u, hole);
                FillHole(hole);
                fell.Filled = true;
                if (other != null)
                {
                    var at = other.Pos;
                    other.Pos = hole;
                    u.Pos = at;
                    fell.SwappedId = other.Id;
                }
                fell.To = u.Pos;
            }
            int dmg = def.FallDamage > 0f ? Math.Max(1, (int)Math.Round(u.MaxHp * def.FallDamage)) : 0;
            fell.Damage = dmg;
            Emit(fell);
            if (u.Pos != from) ShovePickupsUnder(u, hole);
            if (BreaksOnMove(u)) RemoveBuffsWhere(u, b => b.Def.BreaksOnMove, "moved");
            if (def.FallDamage > 0f) Damage(u, dmg, null, HitKind.Normal, DamageType.Physical, "Fell!");
            if (u.Alive && u.Pos != from) TriggerTrap(u); // it lands somewhere new: a trap, a pickup, a tile's hooks
            if (fell.SwappedId >= 0 && S.UnitById(fell.SwappedId) is Unit o && o.Alive) TriggerTrap(o);
        }

        /// <summary>
        /// Where a faller climbs out (GG-T368): breadth-first outward from the hole, ring by ring (Manhattan distance 1 is
        /// "next to the hole", then 2, ...), the first ring with a tile its whole footprint may land on: free for something
        /// to appear (no unit, block, item or trap: <see cref="BattleState.FreeForSpawn"/>, the GG-T227 spawn rule; its own
        /// old tile counts as free). A ring's safe tiles (no rule danger) are preferred, then one is drawn from the battle
        /// Rng. A second pass accepts traps and pickups. Null on a board with no such tile anywhere.
        /// </summary>
        private Coord? FallLanding(Unit u, Coord hole)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int maxRing = BattleConfig.Cols + BattleConfig.Rows;
                for (int d = 1; d <= maxRing; d++)
                {
                    var safe = new List<Coord>();
                    var any = new List<Coord>();
                    for (int y = 0; y < BattleConfig.Rows; y++)
                        for (int x = 0; x < BattleConfig.Cols; x++)
                        {
                            var c = new Coord(x, y);
                            if (c.Manhattan(hole) != d || !LandingOpen(u, c, pass == 1)) continue;
                            any.Add(c);
                            if (RegionBoard.DangerAt(S, c, u) <= 0f) safe.Add(c);
                        }
                    var pool = safe.Count > 0 ? safe : any;
                    if (pool.Count > 0) return pool[S.Rng.Range(0, pool.Count)];
                }
            }
            return null;
        }

        /// <summary>Can <paramref name="u"/>'s footprint land with its anchor on <paramref name="anchor"/>? Pass one wants a
        /// tile free for something to appear (no trap either); <paramref name="loose"/> lets a trap or pickup be there.</summary>
        private bool LandingOpen(Unit u, Coord anchor, bool loose)
        {
            for (int dy = 0; dy < u.H; dy++)
                for (int dx = 0; dx < u.W; dx++)
                {
                    var t = new Coord(anchor.X + dx, anchor.Y + dy);
                    if (!S.Walkable(t) || S.MovableAt(t) != null) return false;
                    var occ = S.UnitAt(t);
                    if (occ != null && occ != u) return false;
                    if (!loose && (S.PickupAt(t) != null || S.TrapAt(t) != null)) return false;
                }
            return true;
        }

        /// <summary>The unit a faller trades places with on a full board: the nearest one-tile unit that can be moved
        /// (ties: lowest id), never the faller.</summary>
        private Unit NearestSwap(Unit u, Coord hole)
        {
            Unit best = null; int bestD = int.MaxValue;
            foreach (var o in S.Units)
            {
                if (o == u || !o.Alive || !o.OnBoard || o.IsMultiTile || o.IsSnakeMember || o.IsRemains || o.IsCorpse || o.IsEgg) continue;
                int d = o.Pos.Manhattan(hole);
                if (d < bestD || (d == bestD && best != null && o.Id < best.Id)) { bestD = d; best = o; }
            }
            return best;
        }

        private void FillHole(Coord hole)
        {
            var m = S.Region.At(hole);
            if (m == null) return;
            S.Region.Tiles.Remove(hole);
            var ev0 = new RegionTilesEvent { RuleId = m.Rule, State = m.State, Change = "cleared" };
            ev0.Tiles.Add(hole);
            Emit(ev0);
        }

        /// <summary>A unit was struck by a foe and lives: the rules' Hit hooks run (a cracked floor cracks, GG-T368).</summary>
        private void RegionHit(Unit u)
        {
            if (S.Region.Rules.Count == 0 || u == null || !u.Alive || !u.OnBoard || PreviewOnly) return;
            RegionHooks(RuleHook.Hit, u);
        }

        private static TileMark NewMark(TileStateDef def, string rule, int counter) => new TileMark
        {
            State = def.Id, Rule = rule, Counter = counter, Blocks = def.Blocks, Hazard = def.Hazard,
            LifeTicks = def.LifeSeconds > 0f ? Math.Max(1, (int)Math.Round(def.LifeSeconds * BattleConfig.TicksPerSecond)) : 0,
        };

        private bool StateMatches(TileMark m, string state) =>
            m != null && (state == null || string.Equals(m.State, state, StringComparison.OrdinalIgnoreCase));

        private void ClearTiles(RuleEffectDef e, RuleCtx ctx)
        {
            var byState = new Dictionary<string, RegionTilesEvent>();
            foreach (var c in AreaOf(e, ctx))
            {
                var m = S.Region.At(c);
                if (!StateMatches(m, e.State)) continue;
                S.Region.Tiles.Remove(c);
                if (!byState.TryGetValue(m.State, out var ev0)) byState[m.State] = ev0 = new RegionTilesEvent { RuleId = m.Rule, State = m.State, Change = "cleared" };
                ev0.Tiles.Add(c);
            }
            foreach (var x in byState.Values) Emit(x);
        }

        /// <summary>Counters move; at its Threshold a state becomes its Becomes (or clears). A tile that would turn blocking
        /// under someone holds at its threshold until they step off (the next modify retries); a hole opens under them and
        /// they fall (GG-T368).</summary>
        private void ModifyTiles(RuleEffectDef e, RuleCtx ctx)
        {
            var byState = new Dictionary<string, RegionTilesEvent>();
            var turned = new List<(TileStateDef def, string rule, Coord at, Unit faller)>();
            void Note(string rule, string state, string change, Coord c)
            {
                string k = state + "|" + change;
                if (!byState.TryGetValue(k, out var ev0)) byState[k] = ev0 = new RegionTilesEvent { RuleId = rule, State = state, Change = change };
                ev0.Tiles.Add(c);
            }
            foreach (var c in AreaOf(e, ctx))
            {
                var m = S.Region.At(c);
                if (!StateMatches(m, e.State)) continue;
                m.Counter += e.Amount;
                var def = C.TryTileState(m.State);
                if (def == null || def.Threshold <= 0 || m.Counter < def.Threshold) { Note(m.Rule, m.State, "changed", c); continue; }
                var next = C.TryTileState(def.Becomes);
                if (next == null) { S.Region.Tiles.Remove(c); Note(m.Rule, m.State, "cleared", c); continue; }
                bool hold = false;
                var faller = next.Blocks ? HoleFaller(next, m.Rule, c, out hold) : null;
                if (hold) { m.Counter = def.Threshold; Note(m.Rule, m.State, "changed", c); continue; }
                S.Region.Tiles[c] = NewMark(next, m.Rule, 0);
                Note(m.Rule, next.Id, "marked", c);
                turned.Add((next, m.Rule, c, faller));
            }
            foreach (var x in byState.Values) Emit(x);
            foreach (var (def, rule, at, faller) in turned)
            {
                if (S.IsOver) return;
                AfterMarked(def, rule, new List<Coord> { at }, faller != null ? new List<(Unit, Coord)> { (faller, at) } : null);
            }
        }

        /// <summary>Moves each unit up to Amount tiles, a free tile at a time (bosses, snakes and remains don't budge);
        /// each tile entered springs what lies there and runs the EnterTile hooks.</summary>
        private void MoveUnits(RuleEffectDef e, RuleCtx ctx)
        {
            var area = AreaOf(e, ctx);
            Coord? point = S.Region.Anchor(e.Anchor) ?? (area.Count > 0 ? area[0] : ctx.Target);
            foreach (var u in RuleUnitsOf(e, ctx))
            {
                if (!u.Alive || u.IsMultiTile || u.IsSnakeMember || u.IsRemains || u.IsCorpse) continue;
                for (int step = 0; step < e.Amount && u.Alive && !S.IsOver; step++)
                {
                    var dir = Direction(e.Move, u.Pos, point);
                    if (dir == null) break;
                    var next = u.Pos + dir.Value;
                    if (TryShoveIntoPit(u, next)) break; // carried into an open hole (GG-T368)
                    if (!S.FreeToEnter(next)) break;
                    var from = u.Pos;
                    if (!Relocate(u, next)) break;
                    Emit(new DisplacedEvent { UnitId = u.Id, From = from, To = next, ByUnitId = -1 });
                    ShovePickupsUnder(u, from);
                    if (BreaksOnMove(u)) RemoveBuffsWhere(u, b => b.Def.BreaksOnMove, "moved");
                    TriggerTrap(u);
                }
            }
        }

        /// <summary>One step's direction: a fixed board direction, or toward/away from <paramref name="point"/> along the
        /// longer axis (null when already there, or no point).</summary>
        private static Coord? Direction(RuleMove move, Coord from, Coord? point)
        {
            switch (move)
            {
                case RuleMove.Up: return Coord.Up;
                case RuleMove.Down: return Coord.Down;
                case RuleMove.Left: return Coord.Left;
                case RuleMove.Right: return Coord.Right;
            }
            if (!point.HasValue || point.Value == from) return null;
            int dx = point.Value.X - from.X, dy = point.Value.Y - from.Y;
            var toward = Math.Abs(dx) >= Math.Abs(dy) ? new Coord(Math.Sign(dx), 0) : new Coord(0, Math.Sign(dy));
            return move == RuleMove.Away ? new Coord(-toward.X, -toward.Y) : toward;
        }

        private void MoveAnchor(RuleEffectDef e, RuleCtx ctx)
        {
            var at = S.Region.Anchor(e.Anchor);
            if (!at.HasValue) return;
            var area = AreaOf(e, ctx);
            Coord? point = area.Count > 0 ? area[0] : ctx.Target;
            var c = at.Value;
            for (int i = 0; i < e.Amount; i++)
            {
                var dir = Direction(e.Move, c, point);
                if (dir == null || !BattleState.InBounds(c + dir.Value)) break;
                c = c + dir.Value;
            }
            if (c == at.Value) return;
            S.Region.Anchors[e.Anchor] = c;
            Emit(new RegionAnchorEvent { RuleId = ctx.Rule, Anchor = e.Anchor, From = at, To = c });
        }

        /// <summary>Puts a rule event on the timeline as its own icon, telegraphing the area's tiles.</summary>
        private void ScheduleRuleEvent(RuleEffectDef e, RuleCtx ctx)
        {
            var def = C.TryRuleEvent(e.Event);
            if (def == null) return;
            float secs = e.Seconds > 0f ? e.Seconds : def.Seconds;
            int ticks = Math.Max(1, (int)Math.Round(secs * BattleConfig.TicksPerSecond));
            var tiles = AreaOf(e, ctx);
            string rule = ctx.Rule ?? (C.RuleEventRule.TryGetValue(def.Id, out var owner) ? owner : null);
            var entry = new TimelineEntry
            {
                Id = S.NextEntryId++, Kind = TimelineKind.Hazard, OwnerId = 0, CasterTeam = Team.Neutral,
                RuleEvent = def.Id, RuleId = rule, HazardTiles = new List<Coord>(tiles),
                TotalCastTicks = ticks, RemainingTicks = ticks, SourceUnitId = ctx.Subject?.Id ?? -1,
            };
            S.Timeline.Add(entry);
            Emit(new HazardQueuedEvent { EntryId = entry.Id, EventId = def.Id, RuleId = rule, Tiles = new List<Coord>(tiles), Seconds = ticks / (float)BattleConfig.TicksPerSecond });
        }

        /// <summary>Retimes (or calls off) every queued rule event of e.Event (all of them when null).</summary>
        private void AdjustRuleEvents(RuleEffectDef e)
        {
            int delta = (int)Math.Round(e.Seconds * BattleConfig.TicksPerSecond);
            foreach (var x in S.Timeline.ToArray())
            {
                if (x.Kind != TimelineKind.Hazard) continue;
                if (e.Event != null && !string.Equals(x.RuleEvent, e.Event, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Cancel)
                {
                    S.Timeline.Remove(x);
                    Emit(new HazardRetimedEvent { EntryId = x.Id, EventId = x.RuleEvent, Cancelled = true });
                    continue;
                }
                // Never sooner than the next tick: a hazard always shows on the bar before it lands.
                x.RemainingTicks = Math.Max(1, x.RemainingTicks + delta);
                if (x.RemainingTicks > x.TotalCastTicks) x.TotalCastTicks = x.RemainingTicks;
                Emit(new HazardRetimedEvent { EntryId = x.Id, EventId = x.RuleEvent, TicksLeft = x.RemainingTicks });
            }
        }

        // ── Areas and units ──────────────────────────────────────────────────────────────────────

        /// <summary>The tiles an effect works on, in row-major order (random picks in draw order), each once, on the board.</summary>
        private List<Coord> AreaOf(RuleEffectDef e, RuleCtx ctx)
        {
            var list = new List<Coord>();
            void Add(Coord c) { if (BattleState.InBounds(c) && !list.Contains(c)) list.Add(c); }
            switch (e.Area)
            {
                case RuleArea.Self: if (ctx.At.HasValue) Add(ctx.At.Value); break;
                case RuleArea.Target: if (ctx.Target.HasValue) Add(ctx.Target.Value); else if (ctx.At.HasValue) Add(ctx.At.Value); break;
                case RuleArea.Anchor: { var a = S.Region.Anchor(e.Anchor); if (a.HasValue) Add(a.Value); break; }
                case RuleArea.EventTiles: if (ctx.EventTiles != null) foreach (var c in ctx.EventTiles) Add(c); else if (ctx.Target.HasValue) Add(ctx.Target.Value); break;
                case RuleArea.Tiles: foreach (var c in e.Tiles) Add(c); break;
                case RuleArea.Row: for (int x = 0; x < BattleConfig.Cols; x++) Add(new Coord(x, e.Index)); break;
                case RuleArea.Column: for (int y = 0; y < BattleConfig.Rows; y++) Add(new Coord(e.Index, y)); break;
                case RuleArea.WithState:
                    for (int y = 0; y < BattleConfig.Rows; y++)
                        for (int x = 0; x < BattleConfig.Cols; x++)
                            if (StateMatches(S.Region.At(new Coord(x, y)), e.State)) Add(new Coord(x, y));
                    break;
                case RuleArea.All:
                    for (int y = 0; y < BattleConfig.Rows; y++) for (int x = 0; x < BattleConfig.Cols; x++) Add(new Coord(x, y));
                    break;
                case RuleArea.RandomFree:
                {
                    var pool = new List<Coord>();
                    for (int y = 0; y < BattleConfig.Rows; y++)
                        for (int x = 0; x < BattleConfig.Cols; x++)
                        {
                            var c = new Coord(x, y);
                            if (S.FreeForSpawn(c) && S.Region.At(c) == null) pool.Add(c);
                        }
                    for (int i = 0; i < e.Count && pool.Count > 0; i++) { int k = S.Rng.Range(0, pool.Count); Add(pool[k]); pool.RemoveAt(k); }
                    break;
                }
                case RuleArea.RandomUnits:
                {
                    var pool = new List<Unit>();
                    foreach (var u in S.Units)
                        if (u.Alive && u.OnBoard && !u.IsCorpse && !u.IsRemains && !u.IsEgg && TeamMatches(e.Team, u)) pool.Add(u);
                    for (int i = 0; i < e.Count && pool.Count > 0; i++) { int k = S.Rng.Range(0, pool.Count); Add(pool[k].Pos); pool.RemoveAt(k); }
                    break;
                }
            }
            if (e.Radius > 0 && list.Count > 0)
            {
                var core = new List<Coord>(list);
                foreach (var c in core)
                    for (int dy = -e.Radius; dy <= e.Radius; dy++)
                        for (int dx = -e.Radius; dx <= e.Radius; dx++)
                            if (Math.Abs(dx) + Math.Abs(dy) <= e.Radius) Add(new Coord(c.X + dx, c.Y + dy));
            }
            return list;
        }

        /// <summary>The living units a unit effect reaches, in unit-id order, minus any immune to its rule, event, state or ability.</summary>
        private List<Unit> RuleUnitsOf(RuleEffectDef e, RuleCtx ctx)
        {
            var list = new List<Unit>();
            switch (e.Units)
            {
                case RuleUnits.Subject:
                    if (ctx.Subject != null) list.Add(ctx.Subject);
                    break;
                case RuleUnits.All:
                    foreach (var u in S.Units) list.Add(u);
                    break;
                default:
                {
                    var area = AreaOf(e, ctx);
                    foreach (var u in S.Units)
                    {
                        if (!u.Alive) continue;
                        foreach (var c in area) if (u.Occupies(c)) { list.Add(u); break; }
                    }
                    break;
                }
            }
            list.RemoveAll(u => !u.Alive || !u.OnBoard || u.IsCorpse || u.IsEgg || !TeamMatches(e.Team, u)
                                || RegionBoard.Immune(u, ctx.Rule, ctx.Event, ctx.Ability)
                                || (ctx.State != null && RegionBoard.Immune(u, ctx.State))
                                || (e.State != null && RegionBoard.Immune(u, e.State)));
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }
    }
}
