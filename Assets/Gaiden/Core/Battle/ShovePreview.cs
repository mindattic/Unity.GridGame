using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>One actor a previewed drag displaces (GG-T190): its tile now and the tile the drop leaves it on.</summary>
    public struct ShovedActor
    {
        public int UnitId;
        public Coord From;
        public Coord To;
        public bool IsEnemy;
    }

    /// <summary>
    /// Shoving as a deliberate tactic (GG-T190, owner 2026-10-21): read off a <see cref="MovePreview"/>, the whole
    /// migration a drag would cause — every other hero and enemy it slides aside, from where to where — and which of
    /// the pincers the drop completes exist only because someone was shoved into place. The drag preview draws it,
    /// and it is computed from the same <see cref="BattleEngine.PreviewMove"/> walk the committed move runs, so it
    /// matches the real result (<c>ShovePreviewTests</c>).
    /// </summary>
    public sealed class ShovePreview
    {
        /// <summary>The hero being dragged.</summary>
        public int HeroId = -1;
        /// <summary>Every other living actor the drop leaves on a different tile than it stands on now.</summary>
        public readonly List<ShovedActor> Displaced = new List<ShovedActor>();
        /// <summary>The previewed pincers, in chain order.</summary>
        public readonly List<Pincer> Pincers = new List<Pincer>();

        public static ShovePreview Of(BattleState s, int heroId, MovePreview pv)
        {
            var sp = new ShovePreview { HeroId = heroId };
            if (s == null || pv == null) return sp;
            sp.Pincers.AddRange(pv.Pincers);
            foreach (var u in s.Units)
            {
                if (!u.Alive || u.Id == heroId || !pv.Positions.TryGetValue(u.Id, out var to) || to == u.Pos) continue;
                sp.Displaced.Add(new ShovedActor { UnitId = u.Id, From = u.Pos, To = to, IsEnemy = u.Team == Team.Enemy });
            }
            return sp;
        }

        /// <summary>Would the drop leave this unit (never the dragged hero) on a different tile?</summary>
        public bool IsShoved(int unitId) => Displaced.Exists(d => d.UnitId == unitId);

        /// <summary>
        /// A pincer the drop completes only because the drag shoved someone into place: an opponent slid into the
        /// line, or the other attacker slid onto its end — not the held hero simply walking up to it.
        /// </summary>
        public bool CompletedByShove(Pincer p)
        {
            if (p == null) return false;
            if ((p.A.Id != HeroId && IsShoved(p.A.Id)) || (p.B.Id != HeroId && IsShoved(p.B.Id))) return true;
            foreach (var o in p.Opponents) if (IsShoved(o.Id)) return true;
            return false;
        }

        /// <summary>How many of the previewed pincers a shove completes.</summary>
        public int ShovePincerCount
        {
            get { int n = 0; foreach (var p in Pincers) if (CompletedByShove(p)) n++; return n; }
        }

        /// <summary>The enemies a shove lands inside a pincer line this drop.</summary>
        public List<int> EnemiesShovedIntoPincers()
        {
            var ids = new List<int>();
            foreach (var p in Pincers)
                foreach (var o in p.Opponents)
                    if (IsShoved(o.Id) && !ids.Contains(o.Id)) ids.Add(o.Id);
            return ids;
        }
    }
}
