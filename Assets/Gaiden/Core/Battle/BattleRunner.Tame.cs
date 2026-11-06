using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Taming (§25.11.3, GG-T303): a Tame effect turns a wild, non-Sentient monster to the party's side for a few of its
    /// own turns. Like Umaro in FF VI it is never commanded — it keeps its own timeline icon and, on its turn, charges the
    /// nearest foe on its own simple AI (walking straight through friend and foe, shoving them aside: it may well wreck
    /// a pincer you were setting up). It carries the Pincer keyword, so it ends and supports pincers with the party.
    /// Every own turn it fights for you fills its bond; once full the tame no longer wears off, and standing at a victory
    /// it asks to join (domestication, Meta). Domesticated monsters spawn as the same independent allies.
    /// </summary>
    internal sealed partial class BattleRunner
    {
        /// <summary>The Tame effect of <paramref name="a"/> on <paramref name="t"/>: one Rng draw against <see cref="Taming.Chance"/>.</summary>
        private void TryTame(Unit caster, Unit t, AbilityDef a)
        {
            var why = Taming.WhyNot(t);
            if (why != null) { Announce(why, t.Id); return; }
            float chance = Taming.Chance(a, caster, t);
            if (!S.Rng.Chance(chance))
            {
                Emit(new TameFailedEvent { TamerId = caster.Id, UnitId = t.Id, Chance = chance });
                Announce($"{t.Name} resists!", t.Id);
                return;
            }
            // Whatever it was charging at the party fizzles: it fights for them now.
            foreach (var e in S.Timeline.FindAll(x => x.OwnerId == t.Id && x.Kind == TimelineKind.Cast))
            {
                S.Timeline.Remove(e);
                Emit(new CastCancelledEvent { EntryId = e.Id, CasterId = t.Id, AbilityId = e.AbilityId, Reason = "tamed" });
            }
            t.Team = Team.Hero;
            t.Tamed = true;
            t.ActedThisWindow = false;
            t.TameTurnsLeft = Math.Max(t.TameTurnsLeft, Math.Max(1, a.TameTurns));
            if (t.BondNeeded <= 0) t.BondNeeded = Taming.BondFor(t.Def, t.Level);
            EnsureTurnEntry(t);
            Emit(new TamedEvent { TamerId = caster.Id, UnitId = t.Id, AbilityId = a.Id, Chance = chance, Turns = t.TameTurnsLeft, BondNeeded = t.BondNeeded });
            Announce($"{t.Name} is tamed!", t.Id);
        }

        /// <summary>A domesticated monster from the roster joins the battle as an independent ally (GG-T303).</summary>
        private void Domesticate(Unit u)
        {
            u.Domesticated = true;
            EnsureTurnEntry(u);
        }

        /// <summary>An independent ally acts on its own turn icon: give it one if it has none (just tamed, revived).</summary>
        private void EnsureTurnEntry(Unit u)
        {
            if (!u.OnBoard || S.TurnEntryOf(u.Id) != null) return;
            S.Timeline.Add(new TimelineEntry { Id = S.NextEntryId++, Kind = TimelineKind.Turn, OwnerId = u.Id, Ip = S.Rng.Range(0, BattleConfig.SpawnStaggerIp + 1) });
        }

        /// <summary>The tame wears off: back on the enemy team, where it stands (its bond so far is kept for a re-tame).</summary>
        private void EndTame(Unit u)
        {
            u.Team = Team.Enemy;
            u.Tamed = false;
            u.TameTurnsLeft = 0;
            u.ActedThisWindow = false;
            Emit(new TameEndedEvent { UnitId = u.Id });
            Announce($"{u.Name} breaks free!", u.Id);
        }

        /// <summary>An independent ally's own turn (GG-T303): start-of-turn triggers, then charge the nearest foe and strike —
        /// closing any pincer it stands at the end of — then end-of-turn triggers, its buffs tick and its bond grows. It
        /// does not end the party's window (only an enemy turn does).</summary>
        private void RunAllyTurn(Unit u)
        {
            Emit(new TurnStartedEvent { UnitId = u.Id });
            u.TurnsTaken++;
            firedThisAction.Clear();
            TriggersTurnStart(u);
            if (S.IsOver || !u.Alive) return;
            if (u.Cooldowns.Count > 0)
                foreach (var k in new List<string>(u.Cooldowns.Keys))
                    if (u.Cooldowns[k] > 0) u.Cooldowns[k]--;

            if (u.IsImmobile)
            {
                Emit(new TurnSkippedEvent { UnitId = u.Id, Reason = u.HasBuff("sleep") ? "asleep" : "immobile" });
                EndAllyTurn(u);
                return;
            }

            if (!(u.IsRanged && RangedFoe(u) != null))
            {
                var path = AllyPath(u);
                int steps = Math.Max(0, u.Def.MoveRange);
                if (path.Count > steps) path = path.GetRange(0, steps);
                if (path.Count > 0)
                {
                    var taken = new List<Coord>();
                    WalkPath(u, path, taken);
                    if (taken.Count > 0 && u.Alive && BreaksOnMove(u)) RemoveBuffsWhere(u, b => b.Def.BreaksOnMove, "moved");
                }
            }
            if (S.IsOver || CheckEnd() || !u.Alive) return;

            AllyAttack(u);
            if (CheckEnd()) return;
            EndAllyTurn(u);
        }

        private void EndAllyTurn(Unit u)
        {
            if (u.Alive) Fire(TriggerOn.ActionEnd, u, new TriggerContext());
            if (u.Alive) Fire(TriggerOn.TurnEnd, u, new TriggerContext());
            if (u.Alive) TickTurnBuffs(u, false);
            if (u.Alive && u.Tamed && u.IsHero)
            {
                bool wasFull = u.Bonded;
                if (u.Bond < u.BondNeeded) u.Bond++;
                if (!u.Bonded) u.TameTurnsLeft--;
                Emit(new BondGrewEvent { UnitId = u.Id, Bond = u.Bond, BondNeeded = u.BondNeeded, TurnsLeft = Math.Max(0, u.TameTurnsLeft) });
                if (u.Bonded && !wasFull) Announce($"{u.Name} trusts you completely!", u.Id);
                if (!u.Bonded && u.TameTurnsLeft <= 0) EndTame(u);
            }
            CheckEnd();
        }

        /// <summary>A foe this unit's ranged attack reaches now: same row or column, within <see cref="ClassDef.AttackRange"/>,
        /// nothing in between. Nearest first, then lowest id.</summary>
        private Unit RangedFoe(Unit u)
        {
            Unit best = null; int bestD = int.MaxValue;
            foreach (var o in S.Units)
            {
                if (!AllyFoe(o)) continue;
                var at = o.Pos;
                if (at.X != u.Pos.X && at.Y != u.Pos.Y) continue;
                int d = at.Manhattan(u.Pos);
                if (d < 1 || d > u.Def.AttackRange) continue;
                var step = new Coord(Math.Sign(at.X - u.Pos.X), Math.Sign(at.Y - u.Pos.Y));
                bool clear = true;
                for (var c = u.Pos + step; c != at; c = c + step) if (S.UnitAt(c) != null || S.IsBlock(c)) clear = false;
                if (clear && (d < bestD || (d == bestD && o.Id < best.Id))) { best = o; bestD = d; }
            }
            return best;
        }

        private static bool AllyFoe(Unit o) => o.Alive && o.Team == Team.Enemy && !o.IsCorpse;

        /// <summary>
        /// The tamed beast's route (deliberately not clever about the party's setup): the shortest cardinal path to any tile
        /// beside a foe, walking through anyone one tile big — friend or foe swap into the tile it leaves, exactly like a
        /// hero drag. Empty when it already stands beside a foe or nothing is reachable. Deterministic (BFS, fixed order).
        /// </summary>
        private List<Coord> AllyPath(Unit u)
        {
            var path = new List<Coord>();
            if (u.IsMultiTile) return path;
            bool Goal(Coord c)
            {
                foreach (var o in S.Units)
                {
                    if (!AllyFoe(o) || o.Occupies(c)) continue;
                    foreach (var t in o.Tiles()) if (t.IsCardinalNeighbor(c)) return true;
                }
                return false;
            }
            bool Passable(Coord c)
            {
                if (!S.Walkable(c)) return false;
                var occ = S.UnitAt(c);
                return occ == null || (occ != u && !occ.IsMultiTile && !occ.IsSnakeMember && !occ.IsRemains);
            }
            if (Goal(u.Pos)) return path;
            var prev = new Dictionary<Coord, Coord> { [u.Pos] = u.Pos };
            var queue = new Queue<Coord>();
            queue.Enqueue(u.Pos);
            Coord? found = null;
            while (queue.Count > 0 && found == null)
            {
                var cur = queue.Dequeue();
                foreach (var d in Coord.Cardinals)
                {
                    var n = cur + d;
                    if (prev.ContainsKey(n) || !Passable(n)) continue;
                    prev[n] = cur;
                    if (Goal(n)) { found = n; break; }
                    queue.Enqueue(n);
                }
            }
            if (found == null) return path;
            for (var c = found.Value; c != u.Pos; c = prev[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>Close a pincer it stands at the end of (it carries the Pincer keyword), else hit the weakest adjacent foe,
        /// else shoot one in range.</summary>
        private void AllyAttack(Unit u)
        {
            if (u.CanPincer && !u.IsMultiTile)
            {
                bool any = false;
                foreach (var p in PincerDetector.Detect(S, Team.Hero, u))
                    if ((p.A == u || p.B == u) && p.Opponents.Exists(o => o.Alive)) any = true;
                if (any)
                {
                    Announce($"{u.Name} pincers!", u.Id);
                    ResolvePincers(Team.Hero, u, u);
                    return;
                }
            }
            Unit target = null;
            foreach (var o in S.Units)
            {
                if (!AllyFoe(o) || !u.IsAdjacentTo(o)) continue;
                if (target == null || o.HpFraction < target.HpFraction || (o.HpFraction == target.HpFraction && o.Id < target.Id)) target = o;
            }
            if (target != null) { Strike(u, target, 1f, BlowKind.Melee); return; }
            if (u.IsRanged && RangedFoe(u) is Unit shot) Strike(u, shot, 1f, BlowKind.Ranged);
        }
    }
}
