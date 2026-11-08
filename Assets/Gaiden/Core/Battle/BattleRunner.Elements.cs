using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>GG-T371 (§3.3.1): a hero's ability washed a trap off a tile, or turned it into another (elements.json tile
    /// reactions: Fire burns the slime trail away, Ice freezes the mire solid, the Disarm Kit pulls a mine).</summary>
    public sealed class TrapClearedEvent : BattleEvent
    {
        public int ById; public Coord At; public string TrapId;
        /// <summary>The trap it became (a Becomes reaction), or null when the tile is clear.</summary>
        public string Into;
        public override string ToString() => Into == null ? $"TrapCleared {TrapId} at {At}" : $"TrapCleared {TrapId} -> {Into} at {At}";
    }

    /// <summary>Which tiles an ability's tile reactions reach (GG-T371). Pure: the targeting check and the runner share it.</summary>
    public static class TileReactions
    {
        /// <summary>The tiles a resolving ability covers (none for whole-team and self shapes).</summary>
        public static List<Coord> Area(AbilityDef a, Coord anchor) =>
            a.Shape == TargetShape.AllAllies || a.Shape == TargetShape.AllEnemies || a.Shape == TargetShape.Self
                ? new List<Coord>() : TargetResolver.Tiles(anchor, a.Shape, a.Radius);

        /// <summary>Would this hero ability, anchored here, clear or change at least one trap? (A tile pick with no unit in
        /// reach is still a fair pick then: throw the Disarm Kit at the mine, the fire flask at the slime trail.)</summary>
        public static bool ReachesTrap(BattleState s, AbilityDef a, Unit caster, Coord anchor)
        {
            if (s?.Content == null || caster == null || !caster.IsHero || (s.Traps.Count == 0 && s.Region.Tiles.Count == 0)) return false;
            var reactions = ElementCounters.ReactionsOf(s.Content, a);
            if (reactions.Count == 0) return false;
            foreach (var c in Area(a, anchor))
            {
                if (s.TrapAt(c) is Trap t && Usable(s.Content, ElementCounters.Match(reactions, t.TrapId))) return true;
                if (s.Region.At(c) is TileMark m && UsableOnMark(s.Content, ElementCounters.Match(reactions, m.State))) return true; // GG-T370
            }
            return false;
        }

        /// <summary>A reaction the battle can run on a trap: Clear, or Becomes another trap (a Becomes into a GG-T359 tile
        /// state is that system's to paint).</summary>
        public static bool Usable(ContentDb c, TileReactionDef r) =>
            r != null && (r.Kind == TileReactionKind.Clear || (r.Into != null && c.Traps.ContainsKey(r.Into)));

        /// <summary>A reaction the battle can run on a painted region tile (GG-T370): Clear, or Becomes another tile state.</summary>
        public static bool UsableOnMark(ContentDb c, TileReactionDef r) =>
            r != null && (r.Kind == TileReactionKind.Clear || c.TryTileState(r.Into) != null);

        /// <summary>Does this ability do anything to the units it reaches? (A tool that only works on tiles does not.)</summary>
        public static bool TouchesUnits(AbilityDef a) =>
            a.BaseDamage > 0 || a.BaseHeal > 0 || a.ApplyBuff != null || a.RemovesDebuffs || a.RemovesBuff != null || a.Knockback > 0
            || a.Pull > 0 || a.SwapsPlaces || a.HastenU > 0f || a.Pushback > 0f || a.StealsMana || a.Tame > 0f || a.SwapsTurns;
    }

    internal sealed partial class BattleRunner
    {
        /// <summary>GG-T371: run a hero ability's tile reactions (its tool set, and its element's when it deals damage) on the
        /// traps it covers. True when that was all it does (no unit in reach, or a tile-only tool): it is then resolved here,
        /// so the dispatcher doesn't call it a miss. Enemy abilities leave the board alone (their own snares stay put).</summary>
        private bool ResolveTileReactions(Unit caster, AbilityDef a, Coord anchor)
        {
            if (caster == null || !caster.IsHero || a.IsTeleport || a.Escape || a.Revives) return false;
            var reactions = ElementCounters.ReactionsOf(C, a);
            if (reactions.Count == 0 || (S.Traps.Count == 0 && S.Region.Tiles.Count == 0)) return false;
            var area = TileReactions.Area(a, anchor);
            var hits = new List<(Trap Trap, TileReactionDef Reaction)>();
            foreach (var tile in area)
                if (S.TrapAt(tile) is Trap t && ElementCounters.Match(reactions, t.TrapId) is TileReactionDef r && TileReactions.Usable(C, r))
                    hits.Add((t, r));
            var marks = MarkReactions(reactions, area); // GG-T370: painted tiles too (fire on ice, a hit on a mine, salts on slime)
            if (hits.Count == 0 && marks.Count == 0) return false;

            bool boardOnly = !TileReactions.TouchesUnits(a) || TargetResolver.Collect(S, a, caster, anchor).Count == 0;
            if (boardOnly)
            {
                NoteRecover(caster, a);
                var resolved = new AbilityResolvedEvent { CasterId = caster.Id, AbilityId = a.Id, Anchor = anchor };
                resolved.Tiles.AddRange(area);
                Emit(resolved);
            }
            foreach (var (trap, r) in hits)
            {
                string into = r.Kind == TileReactionKind.Becomes ? r.Into : null;
                string was = trap.TrapId;
                if (into == null) S.Traps.Remove(trap);
                else trap.TrapId = into;
                Emit(new TrapClearedEvent { ById = caster.Id, At = trap.At, TrapId = was, Into = into });
            }
            foreach (var (at, mark, r) in marks)
                if (!S.IsOver) ReactTile(at, mark, r);
            return boardOnly;
        }
    }
}
