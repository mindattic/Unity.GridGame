using System;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>A Tame landed (GG-T303): the monster is on the party's side for <see cref="Turns"/> of its own turns.</summary>
    public sealed class TamedEvent : BattleEvent
    {
        public int TamerId; public int UnitId; public string AbilityId;
        public float Chance; public int Turns; public int BondNeeded;
        public override string ToString() => $"Tamed {UnitId} by {TamerId} ({Chance:0.00}, {Turns} turns, bond {BondNeeded})";
    }

    /// <summary>A Tame was tried and the monster shrugged it off (the roll failed).</summary>
    public sealed class TameFailedEvent : BattleEvent
    {
        public int TamerId; public int UnitId; public float Chance;
        public override string ToString() => $"TameFailed {UnitId} by {TamerId} ({Chance:0.00})";
    }

    /// <summary>A tamed monster's own turn ended: its bond grew (and, once full, it stays for the battle).</summary>
    public sealed class BondGrewEvent : BattleEvent
    {
        public int UnitId; public int Bond; public int BondNeeded; public int TurnsLeft;
        public bool Full => BondNeeded > 0 && Bond >= BondNeeded;
        public override string ToString() => $"Bond {UnitId} {Bond}/{BondNeeded} ({TurnsLeft} turns left)";
    }

    /// <summary>A tame wore off: the monster is back on the enemy team where it stands.</summary>
    public sealed class TameEndedEvent : BattleEvent
    {
        public int UnitId;
        public override string ToString() => $"TameEnded {UnitId}";
    }

    /// <summary>
    /// Taming rules (§25.11.3, GG-T303), read-only and shared by the engine, the view and the meta layer. A tame is one
    /// ability effect (<see cref="AbilityDef.Tame"/>) so any source — the Monster Tamer's Tame, the Dog Whistle, later
    /// pathways — plugs in by carrying it.
    /// </summary>
    public static class Taming
    {
        /// <summary>Chance bounds: never certain, never hopeless.</summary>
        public const float MinChance = 0.05f, MaxChance = 0.95f;
        /// <summary>Each level the user is above (below) the monster adds (takes) this much of the base, clamped to ×0.5..×1.5.</summary>
        public const float LevelStep = 0.05f;
        /// <summary>Flat chance per point of the user's Luck and of its Wisdom (a way with beasts).</summary>
        public const float PerLuck = 0.004f, PerWisdom = 0.004f;
        /// <summary>Bond a monster needs when its class authors none: this, plus one per <see cref="BondLevelStep"/> levels.</summary>
        public const int BaseBond = 3, BondLevelStep = 5;

        /// <summary>Why this unit can't be tamed, or null when it can: a living enemy with no society (not Sentient, so it
        /// can't pincer on its own), not a boss, not remains/an egg/a corpse, one tile and not a snake.</summary>
        public static string WhyNot(Unit u)
        {
            if (u == null || !u.Alive) return "Nothing to tame";
            if (u.Team != Team.Enemy) return "Not a foe";
            if (u.IsRemains || u.IsEgg || u.IsCorpse) return "Nothing to tame";
            if (u.Def.HasTag("Boss") || u.RuleBreak != RuleBreak.None || (u.Def.BossPhases != null && u.Def.BossPhases.Count > 0)) return "Too mighty to tame";
            if (u.Def.HasTag("Sentient")) return "Too clever to tame";
            if (u.IsMultiTile || u.IsSnakeMember) return "Too big to tame";
            if (u.Def.HasTag("Untameable")) return "Can't be tamed";
            return null;
        }

        public static bool Tameable(Unit u) => WhyNot(u) == null;

        /// <summary>
        /// The chance <paramref name="user"/>'s tame <paramref name="a"/> lands on <paramref name="target"/>: base × (1.5 − HP
        /// fraction left, so a fresh monster halves it and a nearly-beaten one nearly doubles it) × level factor
        /// (1 + 0.05 per level of the gap, ×0.5..×1.5) + 0.4% per Luck and per Wisdom, clamped to 5..95%. Deterministic.
        /// </summary>
        public static float Chance(AbilityDef a, Unit user, Unit target)
        {
            if (a == null || a.Tame <= 0f || user == null || !Tameable(target)) return 0f;
            float hp = 1.5f - Math.Max(0f, Math.Min(1f, target.HpFraction));
            float level = Math.Max(0.5f, Math.Min(1.5f, 1f + LevelStep * (user.Level - target.Level)));
            var st = user.Effective;
            float c = a.Tame * hp * level + st.Luck * PerLuck + st.Wisdom * PerWisdom;
            return Math.Max(MinChance, Math.Min(MaxChance, c));
        }

        /// <summary>Bond the monster needs before it asks to join: its class's <see cref="ClassDef.TameBond"/>, else
        /// <see cref="BaseBond"/> + one per <see cref="BondLevelStep"/> levels.</summary>
        public static int BondFor(ClassDef def, int level) =>
            def != null && def.TameBond > 0 ? def.TameBond : BaseBond + Math.Max(0, level) / BondLevelStep;

        /// <summary>Does this tamed unit, standing at the end of a won battle, ask to join (bond full)?</summary>
        public static bool WantsToJoin(Unit u) => u != null && u.Alive && u.IsWildTamed && u.Team == Team.Hero && u.Bonded;
    }
}
