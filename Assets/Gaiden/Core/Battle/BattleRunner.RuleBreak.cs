using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>A finale boss broke a standard rule (§14.9, GG-T234). <see cref="Arrived"/> marks the entrance banner
    /// naming the rule and its counterplay; every other one marks the rule biting.</summary>
    public sealed class RuleBrokenEvent : BattleEvent
    {
        public int UnitId; public RuleBreak Rule; public string Text; public bool Arrived;
        public override string ToString() => $"RuleBroken {UnitId} {Rule}{(Arrived ? " arrives" : "")}";
    }

    /// <summary>Rule-break queries shared by the engine and the bot (§14.9).</summary>
    public static class RuleBreakRules
    {
        /// <summary>Row pincers an AxisWard turns aside; the last of them shatters it.</summary>
        public const int AxisWardCracks = 3;
        /// <summary>A GravityStamp boss stamps on every Nth own turn…</summary>
        public const int StampEvery = 2;
        /// <summary>…dragging every hero within this many tiles (Manhattan)…</summary>
        public const int StampReach = 3;
        /// <summary>…this many tiles toward itself.</summary>
        public const int StampPull = 2;

        /// <summary>Does <paramref name="target"/>'s ward turn aside a hero pincer whose attackers stand at <paramref name="a"/>
        /// and <paramref name="b"/>? A side-to-side (same row) pincer glances off until the ward has cracked
        /// <see cref="AxisWardCracks"/> times; a top-to-bottom pincer always lands.</summary>
        public static bool WardsPincer(Unit target, Coord a, Coord b) =>
            target != null && target.RuleBreak == RuleBreak.AxisWard && target.RuleBreakCount < AxisWardCracks && a.Y == b.Y;

        /// <summary>Is this own turn (already counted in <see cref="Unit.TurnsTaken"/>) a stamp turn?</summary>
        public static bool StampDue(Unit u) => u.RuleBreak == RuleBreak.GravityStamp && u.TurnsTaken > 0 && u.TurnsTaken % StampEvery == 0;
    }

    internal sealed partial class BattleRunner
    {
        /// <summary>Foes whose ward turned aside the pincer resolving now: they take no attacker or supporter strike.</summary>
        private readonly HashSet<Unit> wardedThisPincer = new HashSet<Unit>();

        private static void ArmRuleBreak(Unit u, RuleBreak r)
        {
            if (u != null) u.RuleBreak = r;
        }

        /// <summary>The entrance banner for every rule-breaker among <paramref name="spawned"/>.</summary>
        private void AnnounceRuleBreaks(List<int> spawned)
        {
            if (spawned == null) return;
            foreach (var id in spawned)
            {
                var u = S.UnitById(id);
                if (u == null || u.RuleBreak == RuleBreak.None) continue;
                Emit(new RuleBrokenEvent { UnitId = u.Id, Rule = u.RuleBreak, Arrived = true, Text = RuleBreaks.Banner(u.Name, u.RuleBreak) });
            }
        }

        private void RuleBroken(Unit u, string text) => Emit(new RuleBrokenEvent { UnitId = u.Id, Rule = u.RuleBreak, Text = text });

        private bool HeroBeside(Unit u)
        {
            foreach (var h in S.Living(Team.Hero)) if (u.IsAdjacentTo(h)) return true;
            return false;
        }

        /// <summary>Start of a mobile rule-breaker's own turn: MireSwap and GravityStamp bite here. True = the turn is over
        /// (the battle ended or the boss fell).</summary>
        private bool RuleBreakBeforeTurn(Unit enemy)
        {
            if (enemy.RuleBreak == RuleBreak.MireSwap) MireSwap(enemy);
            else if (RuleBreakRules.StampDue(enemy)) GravityStamp(enemy);
            return S.IsOver || !enemy.Alive;
        }

        /// <summary>End of a rule-breaker's own turn: TwinTurn's second turn, GravityStamp's warning.</summary>
        private void RuleBreakAfterTurn(Unit enemy)
        {
            if (S.IsOver || !enemy.Alive || enemy.IsImmobile) return;
            if (enemy.RuleBreak == RuleBreak.TwinTurn && !HeroBeside(enemy)) TwinTurn(enemy);
            else if (enemy.RuleBreak == RuleBreak.GravityStamp && (enemy.TurnsTaken + 1) % RuleBreakRules.StampEvery == 0)
                Announce($"{enemy.Name} raises a foot: it stamps next turn!", enemy.Id);
        }

        /// <summary>TwinTurn: a second march and strike — no cast, trap or brain trick, and its buffs don't tick twice.</summary>
        private void TwinTurn(Unit enemy)
        {
            RuleBroken(enemy, $"{enemy.Name} marches again!");
            Emit(new TurnStartedEvent { UnitId = enemy.Id });
            int steps = Math.Max(0, enemy.Def.MoveRange);
            for (int i = 0; i < steps && enemy.Alive && !S.IsOver; i++)
            {
                var dest = EnemyPlanner.PlanStep(S, enemy);
                if (dest == enemy.Pos) break;
                if (dest.Manhattan(enemy.Pos) == 2 ? !LeapEnemy(enemy, dest) : !StepEnemy(enemy, dest)) break;
            }
            if (!enemy.Alive || S.IsOver) return;
            EnemyAttack(enemy);
            if (CheckEnd() || !enemy.Alive) return;
            FaceTarget(enemy);
        }

        /// <summary>MireSwap: cornered (a hero beside it), it trades tiles with the ally standing farthest from any hero.</summary>
        private void MireSwap(Unit boss)
        {
            if (!Budges(boss) || !HeroBeside(boss)) return;
            Unit best = null; int bestD = -1;
            foreach (var e in S.Living(Team.Enemy))
            {
                if (e == boss || e.IsCorpse || e.IsEgg || !Budges(e) || HeroBeside(e)) continue;
                int d = int.MaxValue;
                foreach (var h in S.Living(Team.Hero)) d = Math.Min(d, e.DistanceTo(h));
                if (d > bestD) { bestD = d; best = e; }
            }
            if (best == null) return;
            RuleBroken(boss, $"{boss.Name} trades places with {best.Name}!");
            SwapPlaces(boss, best);
        }

        /// <summary>GravityStamp: every hero within reach (and not already beside it) is dragged toward the boss, nearest first.</summary>
        private void GravityStamp(Unit boss)
        {
            RuleBroken(boss, $"{boss.Name} stamps: the ground drags you in!");
            var pulled = new List<Unit>();
            foreach (var h in S.Living(Team.Hero))
                if (!boss.IsAdjacentTo(h) && boss.DistanceTo(h) <= RuleBreakRules.StampReach) pulled.Add(h);
            pulled.Sort((a, b) => boss.DistanceTo(a) != boss.DistanceTo(b) ? boss.DistanceTo(a).CompareTo(boss.DistanceTo(b)) : a.Id.CompareTo(b.Id));
            foreach (var h in pulled)
                if (h.Alive && boss.Alive && !S.IsOver) Pull(boss, h, RuleBreakRules.StampPull);
        }

        /// <summary>AxisWard: called as a hero pincer resolves — each warded foe turns this pincer aside and cracks.</summary>
        private void WardPincer(Pincer p, Team team)
        {
            wardedThisPincer.Clear();
            if (team != Team.Hero) return;
            foreach (var o in p.Opponents)
            {
                if (!o.Alive || !RuleBreakRules.WardsPincer(o, p.A.Pos, p.B.Pos)) continue;
                wardedThisPincer.Add(o);
                o.RuleBreakCount++;
                int left = RuleBreakRules.AxisWardCracks - o.RuleBreakCount;
                Emit(new DamagedEvent { TargetId = o.Id, SourceId = p.A.Id, Amount = 0, Hit = HitKind.Normal, Type = DamageType.Physical, HpAfter = o.Hp, Note = "Warded!" });
                RuleBroken(o, left > 0
                    ? $"The pincer glances off {o.Name}! Pincer it top to bottom ({left} more to break the ward)"
                    : $"{o.Name}'s ward shatters: pincer it any way you like!");
            }
        }

        /// <summary>Undying: the first fall while any other foe still stands is refused — it rises at half HP, debuffs shed.</summary>
        private bool DefiesDeath(Unit u)
        {
            if (u.RuleBreak != RuleBreak.Undying || u.RuleBreakCount > 0 || u.Team != Team.Enemy) return false;
            bool thralls = false;
            foreach (var e in S.Units)
                if (e != u && e.Alive && e.Team == Team.Enemy && !e.IsCorpse && !e.IsRemains && !e.IsEgg) thralls = true;
            if (!thralls) return false;
            u.RuleBreakCount = 1;
            u.Hp = Math.Max(1, u.MaxHp / 2);
            RemoveBuffsWhere(u, b => b.Def.Kind == BuffKind.Debuff, "rose");
            RuleBroken(u, $"{u.Name} refuses to fall while its thralls stand!");
            Emit(new HealedEvent { TargetId = u.Id, SourceId = u.Id, Amount = u.Hp, HpAfter = u.Hp });
            return true;
        }
    }
}
