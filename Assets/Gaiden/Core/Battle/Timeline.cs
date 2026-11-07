using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    public enum TimelineKind { Turn, Cast }

    /// <summary>
    /// One icon on the shared timeline. Turn icons load from IP 0 to BarLength at one linear rate; cast icons count
    /// their remaining ticks down to 0. Both resolve on the same clock — whichever arrives first resolves first
    /// (bible §2.4 / §2.6). Where an icon is drawn is the seconds until it acts
    /// (<see cref="BattleEngine.SecondsUntilAct"/>, <see cref="BattleEngine.BarU"/>: 0 = the left edge, acting now).
    /// </summary>
    public sealed class TimelineEntry
    {
        public int Id;
        public TimelineKind Kind;
        public int OwnerId;

        // Turn icons
        public int Ip;
        public int StunTicks;
        /// <summary>GG-T318: ticks left off the bar after acting (the recovery tray) before the icon re-enters at its Ip.</summary>
        public int RecoverTicks;
        /// <summary>GG-T319: the recovery this icon started with (plus any pushback since), for the tray's countdown ring.</summary>
        public int RecoverTotalTicks;

        // Cast icons
        public string AbilityId;
        public Team CasterTeam;
        public int TargetUnitId = -1;
        public Coord TargetTile;
        public int TotalCastTicks;
        public int RemainingTicks;
        public int AccumulatedDelayTicks;
        public bool IsCharge;
        public ChargeShape ChargeShape;
        public List<Coord> LineTiles;

        public TimelineEntry Clone()
        {
            var e = (TimelineEntry)MemberwiseClone();
            if (LineTiles != null) e.LineTiles = new List<Coord>(LineTiles);
            return e;
        }
    }
}
