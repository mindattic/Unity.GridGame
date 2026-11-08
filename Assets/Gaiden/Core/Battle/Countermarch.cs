using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Countermarch (§25.11.2.1, GG-T336, the Tactician): a foe's turn icon trades places on the timeline with the foe
    /// whose turn is furthest off — the one about to act goes to the back, the straggler steps up to its time. Both must be
    /// on the bar (not recovering, not Frozen) and on the enemy side (not an independent ally). It is a trade, not a push:
    /// nothing is gained on the bar overall, only who acts when, so it isn't bound by the Pushback cap.
    /// </summary>
    public static class Countermarch
    {
        /// <summary>The turn entry <paramref name="foe"/> would trade with (furthest off, strictly later than it), or null.</summary>
        public static TimelineEntry Partner(BattleState s, Unit foe)
        {
            var mine = OnBar(s, foe);
            if (mine == null) return null;
            int myAt = BattleEngine.TicksUntilAct(s, mine);
            TimelineEntry best = null;
            int bestAt = myAt;
            foreach (var e in s.Timeline)
            {
                if (e == mine || e.Kind != TimelineKind.Turn) continue;
                var o = s.UnitById(e.OwnerId);
                if (o == null || o.Team != foe.Team || OnBar(s, o) != e) continue;
                int at = BattleEngine.TicksUntilAct(s, e);
                if (at > bestAt || (at == bestAt && best != null && e.Id > best.Id)) { best = e; bestAt = at; }
            }
            return best;
        }

        /// <summary>The unit's turn entry when it is on the bar and able to move along it: alive, an enemy-side unit that
        /// isn't independent, not recovering, not Frozen. Null otherwise.</summary>
        public static TimelineEntry OnBar(BattleState s, Unit u)
        {
            if (u == null || !u.Alive || u.Team != Team.Enemy || u.IsIndependent || u.IsTimelineHalted) return null;
            var e = s.TurnEntryOf(u.Id);
            if (e == null || BattleEngine.InRecovery(e) || BattleEngine.TicksUntilAct(s, e) < 0) return null;
            return e;
        }

        /// <summary>Sets a turn icon so it acts in exactly <paramref name="ticks"/> clock ticks at its current rate: its load
        /// first, then a stand-still for whatever the bar can't hold.</summary>
        public static void SetTicksUntilAct(BattleState s, TimelineEntry e, Unit owner, int ticks)
        {
            int rate = BattleEngine.TurnRatePerTick(s, owner);
            int ip = BattleConfig.BarLength - System.Math.Max(0, ticks) * rate;
            e.StunTicks = 0;
            if (ip < 0) { e.StunTicks = (-ip + rate - 1) / rate; ip = 0; }
            e.Ip = System.Math.Min(BattleConfig.BarLength - 1, ip);
        }
    }
}
