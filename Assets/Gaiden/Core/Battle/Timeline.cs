using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>Turn: a unit's turn icon. Cast: a charging spell. Hazard: a region rule's telegraphed event (GG-T359) —
    /// no owner unit (OwnerId 0); it counts down like a cast and resolves its rule event's effects on arrival.</summary>
    public enum TimelineKind { Turn, Cast, Hazard }

    /// <summary>
    /// One icon on the shared timeline. Turn icons load from IP 0 to BarLength at one linear rate; cast and hazard icons
    /// count their remaining ticks down to 0. All resolve on the same clock — whichever arrives first resolves first
    /// (bible §2.4 / §2.6; ties: casts, then hazards, then turns). Where an icon is drawn is the seconds until it acts
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

        // Hazard icons (GG-T359): RemainingTicks/TotalCastTicks count down as for a cast.
        /// <summary>The rule event (regionrules.json) it resolves.</summary>
        public string RuleEvent;
        /// <summary>The rule that queued it (null when a monster ability or a death did).</summary>
        public string RuleId;
        /// <summary>The telegraphed tiles (warned on the board until it lands).</summary>
        public List<Coord> HazardTiles;
        /// <summary>The unit that queued it (its subject when it lands, if still standing); -1 = none.</summary>
        public int SourceUnitId = -1;

        public TimelineEntry Clone()
        {
            var e = (TimelineEntry)MemberwiseClone();
            if (LineTiles != null) e.LineTiles = new List<Coord>(LineTiles);
            if (HazardTiles != null) e.HazardTiles = new List<Coord>(HazardTiles);
            return e;
        }
    }
}
