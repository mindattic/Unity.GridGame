using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        /// <summary>An enemy icon reached the trigger (§14): boss phases → skip/charge/trap/move → pincer or melee → end-of-turn ticks.</summary>
        private void RunEnemyTurn(Unit enemy)
        {
            MintTimeBank();
            Emit(new TurnStartedEvent { UnitId = enemy.Id });

            if (enemy.IsRemains) { RiseAgain(enemy); return; }
            if (enemy.IsEgg) { Hatch(enemy); return; }
            enemy.TurnsTaken++;

            if (!string.IsNullOrEmpty(enemy.Def.LaysEgg) && enemy.TurnsTaken % Math.Max(1, enemy.Def.EggInterval) == 0)
                LayEgg(enemy);

            var phase = AdvanceBossPhases(enemy);
            if (S.IsOver || !enemy.Alive) return;

            if (enemy.IsImmobile)
            {
                Emit(new TurnSkippedEvent { UnitId = enemy.Id, Reason = enemy.HasBuff("sleep") ? "asleep" : "immobile" });
                EndEnemyTurn(enemy);
                return;
            }

            if (BrainTurn(enemy)) return;

            bool alreadyCasting = S.CastEntryOf(enemy.Id) != null;
            var charge = alreadyCasting ? null : EnemyPlanner.PlanCast(S, enemy, phase?.PrefersCharge ?? false);
            if (charge != null)
            {
                QueueCharge(enemy, charge);
                EndEnemyTurn(enemy);
                return;
            }

            if (TryLayTrap(enemy)) { EndEnemyTurn(enemy); return; }

            int steps = Math.Max(0, enemy.Def.MoveRange);
            for (int i = 0; i < steps && enemy.Alive && !S.IsOver; i++)
            {
                var dest = EnemyPlanner.PlanStep(S, enemy);
                if (dest == enemy.Pos) break;
                if (dest.Manhattan(enemy.Pos) == 2 ? !LeapEnemy(enemy, dest) : !StepEnemy(enemy, dest)) break;
            }
            if (!enemy.Alive || S.IsOver) return;

            EnemyAttack(enemy);
            if (CheckEnd()) return;
            EndEnemyTurn(enemy);
        }

        /// <summary>
        /// Brains that change the shape of a turn (§14.6): Skirmishers strike first and dart away; Blinkers (every 2nd
        /// turn) and Burrowers (every 3rd) vanish and reappear beside their prey, then strike. True = the turn is done.
        /// </summary>
        private bool skirmishing;

        private bool BrainTurn(Unit enemy)
        {
            var brain = enemy.Def.Brain;
            bool heroAdjacent = false;
            foreach (var h in S.Living(Team.Hero)) if (enemy.IsAdjacentTo(h)) heroAdjacent = true;
            if (brain == Brain.Skirmisher && heroAdjacent && !enemy.IsMultiTile)
            {
                // A glancing blow on the way out (×0.8), and only one tile of retreat — close enough to be punished
                // (records showed two-tile darts made skirmishers unpincerable, §22.6).
                skirmishing = true;
                EnemyAttack(enemy);
                skirmishing = false;
                if (CheckEnd() || !enemy.Alive) return true;
                var away = EnemyPlanner.PlanRetreat(S, enemy);
                if (away != enemy.Pos) StepEnemy(enemy, away);
                Announce($"{enemy.Name} darts away!", enemy.Id);
                EndEnemyTurn(enemy);
                return true;
            }
            bool blink = brain == Brain.Blinker && enemy.TurnsTaken % 2 == 0;
            bool burrow = brain == Brain.Burrower && enemy.TurnsTaken % 3 == 0;
            if ((blink || burrow) && !heroAdjacent && !enemy.IsMultiTile)
            {
                var target = EnemyPlanner.PickTarget(S, enemy);
                var spot = target == null ? null : EnemyPlanner.SurfaceBeside(S, enemy, target);
                if (spot == null) return false;
                var from = enemy.Pos;
                if (!Relocate(enemy, spot.Value)) return false;
                Emit(new TeleportedEvent { UnitId = enemy.Id, From = from, To = spot.Value });
                Announce(burrow ? $"{enemy.Name} bursts up from below!" : $"{enemy.Name} blinks beside {target.Name}!", enemy.Id);
                EnemyAttack(enemy);
                if (CheckEnd()) return true;
                EndEnemyTurn(enemy);
                return true;
            }
            return false;
        }

        /// <summary>A Hopper's two-tile leap (over a unit, a block or nothing).</summary>
        private bool LeapEnemy(Unit enemy, Coord dest)
        {
            var from = enemy.Pos;
            if (from.Manhattan(dest) != 2 || enemy.IsMultiTile || !FootprintOpen(enemy, dest)) return false; // never onto a unit or a movable
            if (!Relocate(enemy, dest)) return false;
            Emit(new SteppedEvent { UnitId = enemy.Id, From = from, To = dest });
            LeaveTrail(enemy, from);
            return true;
        }

        /// <summary>Trail kits (slime, mire, quicksand): the tile just left becomes a trap; at most 3 per enemy, oldest first.</summary>
        private void LeaveTrail(Unit enemy, Coord from)
        {
            if (string.IsNullOrEmpty(enemy.Def.Trail) || !C.Traps.ContainsKey(enemy.Def.Trail)) return;
            if (!S.FreeForSpawn(from) || S.TrapAt(from) != null) return;
            var mine = S.Traps.FindAll(t => t.OwnerId == enemy.Id);
            if (mine.Count >= 3) S.Traps.Remove(mine[0]);
            S.Traps.Add(new Trap { At = from, TrapId = enemy.Def.Trail, OwnerId = enemy.Id });
            Emit(new TrapPlacedEvent { ById = enemy.Id, At = from, TrapId = enemy.Def.Trail });
        }

        /// <summary>Remains whose turn arrives raise their owner on the same tile at half HP (no loot the second time).</summary>
        private void RiseAgain(Unit remains)
        {
            var def = C.TryClass(remains.RisesAs);
            var at = remains.Pos;
            remains.Hp = 0; remains.OnBoard = false;
            S.Timeline.RemoveAll(e => e.OwnerId == remains.Id);
            if (def == null) return;
            var u = SpawnEnemy(def, remains.RisesLevel, at.X, at.Y);
            if (u == null) return;
            u.Hp = Math.Max(1, u.MaxHp / 2);
            u.NoLoot = true;
            Emit(new RoseAgainEvent { RemainsId = remains.Id, UnitId = u.Id, At = u.Pos });
            Announce($"{u.Name} rises again!", u.Id);
        }

        /// <summary>An egg layer drops a solid egg (§14.8) on a free adjacent tile; it hatches when its own turn arrives.</summary>
        private void LayEgg(Unit layer)
        {
            var def = C.TryClass(layer.Def.LaysEgg);
            if (def == null) return;
            // Beside the layer's whole footprint, on a tile a spawn may take (no unit, movable or trap) — otherwise
            // SpawnEnemy would quietly fall back to a random tile across the board (GG-T97).
            int ew = Math.Max(1, def.Footprint?.W ?? 1), eh = Math.Max(1, def.Footprint?.H ?? 1);
            var spots = new List<Coord>();
            foreach (var t in layer.Tiles())
                foreach (var d in Coord.Cardinals)
                {
                    var c = t + d;
                    if (!layer.Occupies(c) && !spots.Contains(c) && FootprintFree(c, ew, eh)) spots.Add(c);
                }
            if (spots.Count == 0) return;
            var at = spots[S.Rng.Range(0, spots.Count)];
            var egg = SpawnEnemy(def, layer.Level, at.X, at.Y);
            if (egg == null) return;
            egg.RisesAs = layer.Def.Id;
            egg.RisesLevel = layer.Level;
            Emit(new EggLaidEvent { EggId = egg.Id, FromUnitId = layer.Id, At = egg.Pos });
            Announce($"{layer.Name} lays an egg!", layer.Id);
        }

        /// <summary>An egg whose turn arrives hatches into its real enemy on the same tile, at full HP.</summary>
        private void Hatch(Unit egg)
        {
            var def = C.TryClass(egg.RisesAs);
            var at = egg.Pos;
            egg.Hp = 0; egg.OnBoard = false;
            S.Timeline.RemoveAll(e => e.OwnerId == egg.Id);
            if (def == null) return;
            var u = SpawnEnemy(def, egg.RisesLevel, at.X, at.Y);
            if (u == null) return;
            Emit(new EggHatchedEvent { EggId = egg.Id, UnitId = u.Id, At = u.Pos });
            Announce($"{u.Name} hatches!", u.Id);
        }

        private void EndEnemyTurn(Unit enemy)
        {
            if (enemy.Alive) FaceTarget(enemy);
            // Regenerate kit: knits 6% of MaxHP at the end of its own turn.
            if (enemy.Alive && HasPassive(enemy, "regenerate") && enemy.Hp < enemy.MaxHp) Heal(enemy, Math.Max(1, enemy.MaxHp * BattleConfig.RegeneratePercent / 100), enemy);
            if (enemy.Alive) TickTurnBuffs(enemy, false);
            CheckEnd();
        }

        private BossPhaseDef AdvanceBossPhases(Unit enemy)
        {
            var phases = enemy.Def.BossPhases;
            if (phases == null || phases.Count == 0) return null;
            while (enemy.BossPhase + 1 < phases.Count && enemy.HpFraction <= phases[enemy.BossPhase + 1].HpThreshold)
            {
                enemy.BossPhase++;
                var ph = phases[enemy.BossPhase];
                Emit(new BossPhaseEvent { UnitId = enemy.Id, Phase = enemy.BossPhase, Text = ph.Announce });
                if (!string.IsNullOrEmpty(ph.Announce)) Announce(ph.Announce, enemy.Id);
                var a = C.TryAbility(ph.OnEnterAbility);
                if (a != null) ResolveAbility(enemy, a, enemy.Pos, enemy);
                if (S.IsOver || !enemy.Alive) return null;
            }
            return enemy.BossPhase >= 0 ? phases[enemy.BossPhase] : null;
        }

        private void QueueCharge(Unit enemy, ChargePlan plan)
        {
            int ticks = Formulas.SecondsToTicks(plan.Ability.CastTime > 0f ? plan.Ability.CastTime : BattleConfig.DefaultChargeCastSeconds);
            var entry = new TimelineEntry
            {
                Id = S.NextEntryId++,
                Kind = TimelineKind.Cast,
                OwnerId = enemy.Id,
                AbilityId = plan.Ability.Id,
                CasterTeam = Team.Enemy,
                TargetUnitId = plan.Target.Id,
                TargetTile = plan.Target.Pos,
                TotalCastTicks = ticks,
                RemainingTicks = ticks,
                IsCharge = true,
                ChargeShape = plan.Shape,
                LineTiles = plan.Shape == ChargeShape.Line ? plan.Line : null,
            };
            S.Timeline.Add(entry);
            Emit(new CastQueuedEvent { EntryId = entry.Id, CasterId = enemy.Id, AbilityId = plan.Ability.Id, Ticks = ticks, IsCharge = true, LineTiles = entry.LineTiles, TargetUnitId = plan.Target.Id, TargetTile = plan.Target.Pos });
            Announce($"{enemy.Name} is charging {plan.Ability.Name}!", enemy.Id);
        }

        private bool TryLayTrap(Unit enemy)
        {
            if (enemy.Def.Trap == null || !C.Traps.TryGetValue(enemy.Def.Trap, out var trap)) return false;
            foreach (var h in S.Living(Team.Hero)) if (enemy.IsAdjacentTo(h)) return false;
            if (!S.Rng.Chance(trap.LayChance)) return false;
            var spots = new List<Coord>();
            foreach (var d in Coord.Cardinals)
            {
                var c = enemy.Pos + d;
                if (S.FreeForSpawn(c) && S.TrapAt(c) == null) spots.Add(c);
            }
            if (spots.Count == 0) return false;
            var at = spots[S.Rng.Range(0, spots.Count)];
            S.Traps.Add(new Trap { At = at, TrapId = trap.Id, OwnerId = enemy.Id });
            Emit(new TrapPlacedEvent { ById = enemy.Id, At = at, TrapId = trap.Id });
            return true;
        }

        /// <summary>One cardinal step. 1×1 enemies step into free tiles; multi-tile bosses shove heroes behind them (§1.5 rule 3); snake bodies follow the head.</summary>
        private bool StepEnemy(Unit enemy, Coord dest)
        {
            var delta = dest - enemy.Pos;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) != 1) return false;
            var from = enemy.Pos;

            if (enemy.IsMultiTile)
            {
                if (!EnemyPlanner.FootprintStepLegal(S, enemy, dest)) return false;
                // Heroes in the entered tiles are swapped to the tile the body vacates on that lane.
                var shoves = new List<(Unit hero, Coord to)>();
                for (int dy = 0; dy < enemy.H; dy++)
                    for (int dx = 0; dx < enemy.W; dx++)
                    {
                        var entered = new Coord(dest.X + dx, dest.Y + dy);
                        if (enemy.Occupies(entered)) continue;
                        var occ = S.UnitAt(entered);
                        if (occ == null) continue;
                        if (!occ.IsHero) return false;
                        shoves.Add((occ, VacatedTile(enemy, from, delta, entered)));
                    }
                // Each shoved hero (and movable) takes the tile the body vacates on its own lane: one entered tile per
                // lane, one vacated tile per lane, so no two of them can ever be sent to the same tile (GG-T97).
                var shoved = new HashSet<Unit>();
                foreach (var sh in shoves) shoved.Add(sh.hero);
                if (!Relocate(enemy, dest, shoved.Contains, movablesYield: true)) return false;
                Emit(new SteppedEvent { UnitId = enemy.Id, From = from, To = dest });
                foreach (var mv in S.Movables.ToArray())
                    if (enemy.Occupies(mv.Pos) && !OccupiedBefore(enemy, from, mv.Pos))
                        ShoveMovable(mv, VacatedTile(enemy, from, delta, mv.Pos), enemy);
                foreach (var (hero, to) in shoves)
                {
                    var hf = hero.Pos;
                    if (!Relocate(hero, to)) continue;
                    Emit(new DisplacedEvent { UnitId = hero.Id, From = hf, To = to, ByUnitId = enemy.Id });
                    if (BreaksOnMove(hero)) RemoveBuffsWhere(hero, b => b.Def.BreaksOnMove, "moved");
                    TriggerTrap(hero);
                }
                return true;
            }

            if (S.UnitAt(dest) != null || !S.Walkable(dest)) return false;
            var pushedTile = S.MovableAt(dest);
            if (!Relocate(enemy, dest, movablesYield: true)) return false;
            Emit(new SteppedEvent { UnitId = enemy.Id, From = from, To = dest });
            // A movable is pushed like an actor, into the tile just left - for a snake, the tile its tail just left,
            // since the body slides into every tile the head vacates (GG-T97: it used to land under segment 1).
            var vacated = enemy.SnakeHeadId == enemy.Id ? FollowSnake(enemy, from) : from;
            if (pushedTile != null) ShoveMovable(pushedTile, vacated, enemy);
            LeaveTrail(enemy, from);
            return true;
        }

        /// <summary>
        /// A movable tile is displaced like an actor: it takes the tile its pusher just left (§1.1.2). That tile can
        /// hold a pickup (an enemy may stand on one, §13.3.1), and two items never share a tile (§1.1.3, GG-T129), so
        /// then the movable slides on to the nearest open, item-free tile instead — the pickup stays where it lies.
        /// </summary>
        private void ShoveMovable(MovableTile mv, Coord to, Unit by)
        {
            if (!PickupBlocksMovable(to) && MoveMovable(mv, to, by)) return;
            // Its own lane is taken by a pickup (or, a bug, by a unit): never leave it under a unit or on another item.
            var best = NearestMovableSpot(mv, to);
            if (best != null) MoveMovable(mv, best.Value, by);
            else S.Movables.Remove(mv);
        }

        private static bool OccupiedBefore(Unit u, Coord anchor, Coord c) =>
            c.X >= anchor.X && c.X < anchor.X + u.W && c.Y >= anchor.Y && c.Y < anchor.Y + u.H;

        /// <summary>For a footprint stepping by <paramref name="delta"/>, the tile it vacates on the same lane as <paramref name="entered"/>.</summary>
        private static Coord VacatedTile(Unit u, Coord from, Coord delta, Coord entered)
        {
            if (delta.X > 0) return new Coord(from.X, entered.Y);
            if (delta.X < 0) return new Coord(from.X + u.W - 1, entered.Y);
            if (delta.Y > 0) return new Coord(entered.X, from.Y);
            return new Coord(entered.X, from.Y + u.H - 1);
        }

        /// <summary>Body segments slide into the tile ahead of them; returns the last tile vacated (the tail's old tile).</summary>
        private Coord FollowSnake(Unit head, Coord headFrom)
        {
            var members = new List<Unit>();
            foreach (var u in S.Units)
                if (u.Alive && u.SnakeHeadId == head.Id && u.SnakeIndex > 0) members.Add(u);
            members.Sort((a, b) => a.SnakeIndex.CompareTo(b.SnakeIndex));
            var trail = headFrom;
            foreach (var m in members)
            {
                var prev = m.Pos;
                if (!FootprintOpen(m, trail) || !Relocate(m, trail)) break;
                Emit(new SteppedEvent { UnitId = m.Id, From = prev, To = trail });
                trail = prev;
            }
            return trail;
        }

        /// <summary>Humanoid enemies resolve any pincer they're part of; otherwise melee the adjacent hero their personality picks.</summary>
        private void EnemyAttack(Unit enemy)
        {
            if (enemy.IsHumanoid && !enemy.IsMultiTile)
            {
                var pincers = PincerDetector.Detect(S, Team.Enemy, enemy);
                bool any = false;
                foreach (var p in pincers) if (p.A == enemy || p.B == enemy) any = true;
                if (any)
                {
                    Announce($"{enemy.Name} pincers!", enemy.Id);
                    ResolvePincers(Team.Enemy, enemy, enemy);
                    return;
                }
            }
            var adjacent = new List<Unit>();
            foreach (var h in S.Units)
                if (h.Alive && h.IsHero && enemy.IsAdjacentTo(h)) adjacent.Add(h);
            var target = EnemyPlanner.PickFrom(S, enemy, adjacent);
            if (target == null && enemy.IsRanged)
            {
                var shot = EnemyPlanner.RangedTarget(S, enemy);
                if (shot != null)
                {
                    Emit(new AttackEvent { AttackerId = enemy.Id, TargetId = shot.Id, IsRanged = true });
                    var r = Formulas.Physical(enemy, shot, S.Rng, KitDamageMultiplier(enemy));
                    int hp0 = shot.Hp;
                    Damage(shot, r.Damage, enemy, r.Hit, DamageType.Physical);
                    if (r.Hit != HitKind.Miss) AfterLandedHit(enemy, shot, hp0 - shot.Hp);
                }
                return;
            }
            if (target == null) return;
            Emit(new AttackEvent { AttackerId = enemy.Id, TargetId = target.Id, IsMelee = true });
            var roll = Formulas.Physical(enemy, target, S.Rng, KitDamageMultiplier(enemy));
            int hpBefore = target.Hp;
            Damage(target, roll.Damage, enemy, roll.Hit, DamageType.Physical);
            if (roll.Hit != HitKind.Miss) { AfterLandedHit(enemy, target, hpBefore - target.Hp); MaybeCounter(target, enemy); }
            if (HasPassive(enemy, "double_attack") && target.Alive && enemy.Alive)
            {
                Emit(new AttackEvent { AttackerId = enemy.Id, TargetId = target.Id, IsMelee = true });
                var r2 = Formulas.Physical(enemy, target, S.Rng, 0.5f * KitDamageMultiplier(enemy));
                int hp2 = target.Hp;
                Damage(target, r2.Damage, enemy, r2.Hit, DamageType.Physical);
                if (r2.Hit != HitKind.Miss) AfterLandedHit(enemy, target, hp2 - target.Hp);
            }
        }
    }
}
