using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>One painted tile (GG-T359): which state, which rule painted it, its counter and how long it has left.</summary>
    public sealed class TileMark
    {
        public string State;
        public string Rule;
        public int Counter;
        /// <summary>Clock ticks left (0 = until cleared).</summary>
        public int LifeTicks;
        /// <summary>Copied from the state at paint time, so <see cref="BattleState.Walkable"/> needs no content lookup.</summary>
        public bool Blocks;
        public bool Hazard;
        public TileMark Clone() => (TileMark)MemberwiseClone();
    }

    /// <summary>
    /// The board's region-rule state (GG-T359, BIBLE §14.20): the rules in play, painted tiles, named anchors, board-wide
    /// flags and each hook's match count. Queued rule events live on the timeline (<see cref="TimelineKind.Hazard"/>).
    /// </summary>
    public sealed class RegionState
    {
        public List<string> Rules = new List<string>();
        public Dictionary<Coord, TileMark> Tiles = new Dictionary<Coord, TileMark>();
        public Dictionary<string, Coord> Anchors = new Dictionary<string, Coord>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HookCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public TileMark At(Coord c) => Tiles.Count > 0 && Tiles.TryGetValue(c, out var m) ? m : null;
        public bool BlocksAt(Coord c) => Tiles.Count > 0 && Tiles.TryGetValue(c, out var m) && m.Blocks;
        public bool HasFlag(string flag) => flag != null && Flags.Contains(flag);
        public Coord? Anchor(string name) => name != null && Anchors.TryGetValue(name, out var c) ? c : (Coord?)null;

        public RegionState Clone()
        {
            var r = new RegionState
            {
                Rules = new List<string>(Rules),
                Anchors = new Dictionary<string, Coord>(Anchors, StringComparer.OrdinalIgnoreCase),
                Flags = new HashSet<string>(Flags, StringComparer.OrdinalIgnoreCase),
                HookCounts = new Dictionary<string, int>(HookCounts, StringComparer.Ordinal),
            };
            foreach (var kv in Tiles) r.Tiles[kv.Key] = kv.Value.Clone();
            return r;
        }
    }

    /// <summary>Pure queries on the region state for the rules, the planners, the bot and the view (GG-T359).</summary>
    public static class RegionBoard
    {
        /// <summary>Does <paramref name="u"/> shrug off effects from any of these sources (rule, tile state, event, ability ids)?</summary>
        public static bool Immune(Unit u, string a, string b = null, string c = null)
        {
            var list = u?.Def?.RuleImmune;
            if (list == null || list.Count == 0) return false;
            foreach (var id in list)
                if (id != null && (Same(id, a) || Same(id, b) || Same(id, c))) return true;
            return false;
        }

        private static bool Same(string x, string y) => y != null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        /// <summary>Is <paramref name="e"/> a queued rule event whose tiles are dangerous?</summary>
        public static bool IsHazardEntry(BattleState s, TimelineEntry e) =>
            e.Kind == TimelineKind.Hazard && (s.Content?.TryRuleEvent(e.RuleEvent)?.Hazard ?? true);

        /// <summary>
        /// How dangerous standing on <paramref name="c"/> is for <paramref name="u"/> (0 = safe): a hazardous painted state
        /// counts 1, a telegraphed hazard landing there counts 2 (more the sooner it lands). Immune units read 0.
        /// The bot and the hero path planner steer by it.
        /// </summary>
        public static float DangerAt(BattleState s, Coord c, Unit u)
        {
            float d = 0f;
            var m = s.Region.At(c);
            if (m != null && m.Hazard && !Immune(u, m.Rule, m.State)) d += 1f;
            foreach (var e in s.Timeline)
            {
                if (e.Kind != TimelineKind.Hazard || e.HazardTiles == null || !e.HazardTiles.Contains(c)) continue;
                if (!IsHazardEntry(s, e) || Immune(u, e.RuleId, e.RuleEvent)) continue;
                float soon = 1f - Math.Min(1f, Math.Max(0, e.RemainingTicks) / (float)(BattleConfig.TicksPerSecond * BattleConfig.BarSeconds));
                d += 2f + 2f * soon;
            }
            return d;
        }

        /// <summary>True when the timeline should mask enemy turn icons (a rule raised <see cref="RuleFlags.MaskTimeline"/>).</summary>
        public static bool MasksTimeline(BattleState s) => s.Region.HasFlag(RuleFlags.MaskTimeline);
    }
}
