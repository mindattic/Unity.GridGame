using System;
using System.Collections.Generic;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// GG-T318 (§2.12): spaces foes out on the timeline. A foe that just acted would naturally act again
    /// <c>natural</c> ticks from now. It may be held in recovery up to <c>maxSpread</c> extra ticks so its next turn
    /// lands at least <c>minGap</c> ticks from every other foe's. When nothing in that range fits, it takes the
    /// delay with the widest gap on offer, so a crowded bar still clusters (the party Guards through it). Pure and
    /// deterministic.
    /// </summary>
    public static class TimelineSpacing
    {
        /// <summary>Extra recovery ticks (0..maxSpread) for an icon whose next turn would land at <paramref name="natural"/>,
        /// given the ticks until every other foe acts (<paramref name="others"/>; negative entries are ignored).</summary>
        public static int Delay(int natural, IReadOnlyList<int> others, int minGap, int maxSpread)
        {
            if (minGap <= 0 || others == null || others.Count == 0) return 0;
            maxSpread = Math.Max(0, maxSpread);
            int bestDelay = 0, bestGap = -1;
            for (int d = 0; d <= maxSpread; d++)
            {
                int gap = SmallestGap(natural + d, others);
                if (gap >= minGap) return d;
                if (gap > bestGap) { bestGap = gap; bestDelay = d; }
            }
            return bestDelay;
        }

        /// <summary>The distance from <paramref name="at"/> to the nearest of <paramref name="others"/> (int.MaxValue with none).</summary>
        public static int SmallestGap(int at, IReadOnlyList<int> others)
        {
            int best = int.MaxValue;
            foreach (var o in others)
            {
                if (o < 0) continue;
                best = Math.Min(best, Math.Abs(at - o));
            }
            return best;
        }
    }
}
