using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    /// <summary>What a <see cref="PincerPlan"/> achieves (GG-T306).</summary>
    public enum PincerPlanKind { None, Complete, Setup, Support }

    /// <summary>A planned walk for a pincer-capable unit: the steps (each enters an empty tile or swaps with the occupant),
    /// what it achieves and its score.</summary>
    public sealed class PincerPlan
    {
        public List<Coord> Path = new List<Coord>();
        public PincerPlanKind Kind;
        public float Score;
        public Coord End;
        /// <summary>Units the walk swaps with (the occupant of an entered tile takes the tile just left), in order.</summary>
        public List<int> Swapped = new List<int>();
        public override string ToString() => $"{Kind} {Score:0.0} via [{string.Join(" ", Path)}]";
    }

    /// <summary>
    /// The shared pincer planner (bible §14.1.3, GG-T306). A unit with the Pincer keyword (heroes, Sentient enemies, tamed
    /// monsters: <see cref="Unit.CanPincer"/>) does not only line itself up: it searches every walk it can make this turn —
    /// up to its steps, each entering an empty tile or <b>swapping</b> with the one-tile unit standing there (friend or foe,
    /// exactly like a hero drag, §1.1) — and scores the board each walk leaves:
    /// <list type="number">
    /// <item><b>Complete</b>: it is now an end of a pincer with live opponents (worth each opponent, plus every supporter's
    /// own <see cref="SupportDef"/> effect, so an ally swapped into a chain counts).</item>
    /// <item><b>Setup</b>: no pincer yet, but it stands at one end of an opponent line whose far end is open and a
    /// pincer-capable ally can reach that tile on its next action.</item>
    /// <item><b>Support</b>: it (or the ally it swapped) joins the chain of an ally pincer already on the board.</item>
    /// </list>
    /// A walk that leaves it, or any ally, trapped in an opponent pincer is penalised. Pure and deterministic: it moves
    /// units' <c>Pos</c> to look and restores them, never draws from the Rng, and walks are tried in a fixed order
    /// (<see cref="Coord.Cardinals"/>; the first of equal scores wins). Bounded: at most <see cref="MaxSearchSteps"/> steps
    /// (4 × 3 × 3 = 52 walks at most per unit).
    /// </summary>
    public static class PincerPlanner
    {
        public const int MaxSearchSteps = 3;

        // Score weights (§14.1.3 cheat sheet).
        public const float OpponentValue = 10f, WeakOpponentBonus = 3f, SetupBase = 6f, SetupPerOpponent = 1f,
            SupportJoinFactor = 0.6f, TrappedSelfPenalty = 40f, TrappedAllyPenalty = 12f, StepCost = 0.25f, SwapCost = 0.25f,
            TrapStepPenalty = 8f;

        /// <summary>The best walk for <paramref name="mover"/> of at most <paramref name="maxSteps"/> steps (capped at
        /// <see cref="MaxSearchSteps"/>); the empty walk is always a candidate. Kind None when nothing is worth it.</summary>
        public static PincerPlan Plan(BattleState s, Unit mover, int maxSteps)
        {
            if (mover == null) return new PincerPlan();
            var best = new PincerPlan { End = mover.Pos };
            if (!mover.Alive || !mover.CanPincer || mover.IsMultiTile || mover.IsSnakeMember) return best;
            int depth = Math.Max(0, Math.Min(maxSteps, MaxSearchSteps));
            int trappedBefore = TrappedAllies(s, mover.Team, out _, mover);
            var origin = mover.Pos;
            var path = new List<Coord>();
            var swapped = new List<int>();
            var undo = new List<(Unit u, Coord at)>();
            float bestScore = float.NegativeInfinity;

            void Consider()
            {
                var (kind, score) = Evaluate(s, mover, trappedBefore);
                score -= StepCost * path.Count + SwapCost * swapped.Count;
                if (kind == PincerPlanKind.None) return;
                if (score > bestScore + 0.001f)
                {
                    bestScore = score;
                    best = new PincerPlan { Path = new List<Coord>(path), Kind = kind, Score = score, End = mover.Pos, Swapped = new List<int>(swapped) };
                }
            }

            void Walk(int left)
            {
                Consider();
                if (left <= 0) return;
                foreach (var d in Coord.Cardinals)
                {
                    var next = mover.Pos + d;
                    if (!s.Walkable(next) || path.Contains(next) || next == origin) continue;
                    var occ = s.UnitAt(next);
                    if (occ != null && !Swappable(occ, mover)) continue;
                    if (occ != null && occ.IsHero && mover.Team == Team.Enemy && !BattleConfig.EnemyPincerSwapsHeroes) continue;
                    if (mover.Team == Team.Hero && s.TrapAt(next) != null) continue; // traps bite the hero team only
                    var from = mover.Pos;
                    undo.Add((mover, from));
                    if (occ != null) { undo.Add((occ, occ.Pos)); occ.Pos = from; swapped.Add(occ.Id); }
                    mover.Pos = next;
                    path.Add(next);
                    Walk(left - 1);
                    path.RemoveAt(path.Count - 1);
                    if (occ != null) { swapped.RemoveAt(swapped.Count - 1); var o = undo[undo.Count - 1]; o.u.Pos = o.at; undo.RemoveAt(undo.Count - 1); }
                    var m = undo[undo.Count - 1]; m.u.Pos = m.at; undo.RemoveAt(undo.Count - 1);
                }
            }

            try { Walk(depth); }
            finally
            {
                for (int i = undo.Count - 1; i >= 0; i--) undo[i].u.Pos = undo[i].at;
                mover.Pos = origin;
            }
            return best;
        }

        /// <summary>The occupant of a tile a walk may enter by swapping: one tile big, alive, not a snake member, remains,
        /// corpse or neutral (§1.1: the same units a hero drag slides through, minus the ones a foe has no business moving).</summary>
        public static bool Swappable(Unit occ, Unit mover) =>
            occ != null && occ != mover && occ.Alive && !occ.IsMultiTile && !occ.IsSnakeMember && !occ.IsRemains && !occ.IsCorpse && !occ.IsNeutral;

        /// <summary>Scores the board as it stands (the mover already on its candidate tile).</summary>
        public static (PincerPlanKind kind, float score) Evaluate(BattleState s, Unit mover, int trappedBefore)
        {
            var team = mover.Team;
            bool boardWide = mover.IsCommandable; // a hero's drop resolves every hero pincer; a foe or a tamed beast only its own
            float complete = 0f, support = 0f;
            foreach (var p in PincerDetector.Detect(s, team, mover))
            {
                bool mine = p.A == mover || p.B == mover;
                float v = PincerValue(s, p);
                if (v <= 0f) continue;
                if (mine || boardWide) complete += v;
                else if (p.SupportersA.Contains(mover) || p.SupportersB.Contains(mover)) support += SupportJoinFactor * SupportWorth(mover);
            }
            int trapped = TrappedAllies(s, team, out bool selfTrapped, mover);
            float danger = TrappedAllyPenalty * Math.Max(0, trapped - trappedBefore);
            var brain = mover.Def?.Brain ?? Brain.Hunter;
            if (selfTrapped && !(team == Team.Enemy && (brain == Brain.Berserker || brain == Brain.Bomber))) danger += TrappedSelfPenalty;

            if (complete > 0f) return (PincerPlanKind.Complete, complete + support - danger);
            float setup = SetupValue(s, mover);
            if (setup > 0f) return (PincerPlanKind.Setup, setup + support - danger);
            if (support > 0f) return (PincerPlanKind.Support, support - danger);
            return (PincerPlanKind.None, -danger);
        }

        /// <summary>A pincer's worth: each live, unarmored opponent, more for a weak one, plus every supporter's effect.</summary>
        public static float PincerValue(BattleState s, Pincer p)
        {
            float v = 0f;
            foreach (var o in p.Opponents)
            {
                if (!o.Alive || o.IsCorpse || BattleEngine.IsArmored(s, o)) continue;
                v += OpponentValue + (o.HpFraction < 0.4f ? WeakOpponentBonus : 0f);
            }
            if (v <= 0f) return 0f;
            foreach (var su in p.SupportersA) v += SupportWorth(su);
            foreach (var su in p.SupportersB) if (!p.SupportersA.Contains(su)) v += SupportWorth(su);
            return v;
        }

        /// <summary>What one supporter brings to a pincer, by its class's support effect (§1.2.3) — the planner's reason to
        /// put a healer behind an attacker rather than a second striker.</summary>
        public static float SupportWorth(Unit u)
        {
            var sd = PincerDetector.SupportOf(u);
            float m = sd.Magnitude;
            switch (sd.Effect)
            {
                case SupportEffect.Strike: return 2f + 4f * m;            // 0.5 → 4
                case SupportEffect.Heal: return 2f + 10f * m;             // 0.12 → 3.2
                case SupportEffect.Buff: return 3f;
                case SupportEffect.Haste: return 2f + m;                  // 1 s → 3
                case SupportEffect.Mana: return 2f + m / 25f;
                case SupportEffect.Cleanse: return 2f;
                case SupportEffect.Crit: return 2f + 6f * m;              // +25% → 3.5
                case SupportEffect.Cast: return 2f + 4f * m;
                case SupportEffect.Status: return 2f + 3f * m;            // 40% → 3.2
                case SupportEffect.Pushback: return 2f + 2f * m;          // 0.5 s → 3
                default: return 2f;
            }
        }

        /// <summary>
        /// Setup: the mover stands at one end of an unbroken line of opponents whose far end is an open tile (empty, or held
        /// by a one-tile unit a pincer-capable ally could swap out) that such an ally can reach on its next action.
        /// </summary>
        public static float SetupValue(BattleState s, Unit mover)
        {
            float best = 0f;
            foreach (var d in Coord.Cardinals)
            {
                int n = 0; bool real = false;
                var c = mover.Pos + d;
                while (BattleState.InBounds(c))
                {
                    var occ = s.UnitAt(c);
                    if (occ == null || occ.IsNeutral || occ.Team == mover.Team) break;
                    n++;
                    if (!occ.IsCorpse && !BattleEngine.IsArmored(s, occ)) real = true;
                    c = c + d;
                }
                if (n == 0 || !real || !s.Walkable(c)) continue;
                var at = s.UnitAt(c);
                if (at != null && (at.Team != mover.Team || !Swappable(at, mover))) continue;
                if (at != null && at.CanPincer) continue; // it would already be a pincer
                foreach (var ally in s.Units)
                {
                    if (!ally.Alive || ally == mover || ally == at || ally.Team != mover.Team || !ally.CanPincer || ally.IsMultiTile) continue;
                    int reach = NextReach(s, ally);
                    if (reach <= 0 || ally.Pos.Manhattan(c) > reach) continue;
                    best = Math.Max(best, SetupBase + SetupPerOpponent * n);
                    break;
                }
            }
            return best;
        }

        /// <summary>How far an ally can walk on its next action: a foe or tamed beast its MoveRange; a commanded hero that
        /// may still act this window the drag that fits it, else a full drag next window.</summary>
        private static int NextReach(BattleState s, Unit ally)
        {
            if (ally.IsImmobile) return 0;
            if (ally.IsCommandable)
            {
                int r = BattleEngine.MaxDragSteps(s);
                return r == int.MaxValue ? 99 : Math.Max(r, 1);
            }
            return Math.Max(0, ally.Def.MoveRange);
        }

        /// <summary>Allies of <paramref name="team"/> standing trapped in an opponent pincer right now (and whether the mover is).</summary>
        public static int TrappedAllies(BattleState s, Team team, out bool moverTrapped, Unit mover)
        {
            moverTrapped = false;
            var other = team == Team.Hero ? Team.Enemy : Team.Hero;
            var seen = new HashSet<Unit>();
            foreach (var p in PincerDetector.Detect(s, other, null))
                foreach (var o in p.Opponents)
                    if (o.Alive && seen.Add(o) && o == mover) moverTrapped = true;
            return seen.Count;
        }
    }
}
