using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>What a neutral recruitable wants before it will join (§25.11.1, GG-T132). All of a recruitable's
    /// conditions must hold at the moment a hero talks to it.</summary>
    public enum RecruitConditionType
    {
        /// <summary>A hero has healed it this battle (<see cref="RecruitCondition.Id"/> = that hero's class, null = any hero).
        /// A recruitable that asks for this enters wounded, so the heal shows.</summary>
        HealedBy,
        /// <summary>It has stood beside a hero (where a supporter stands) while that hero struck a pincer
        /// (<see cref="RecruitCondition.Id"/> = that hero's class, null = any hero).</summary>
        ParticipatedInPincerWith,
        /// <summary>Pay <see cref="RecruitCondition.Amount"/> gold from the caravan's banked coins, at the moment it joins.</summary>
        PayGold,
        /// <summary>Only this hero class can talk it round (<see cref="RecruitCondition.Id"/>).</summary>
        TalkedBy,
    }

    public sealed class RecruitCondition
    {
        public RecruitConditionType Type;
        /// <summary>Hero class id (HealedBy / ParticipatedInPincerWith optional, TalkedBy required).</summary>
        public string Id;
        /// <summary>PayGold: the price.</summary>
        public int Amount;
    }

    /// <summary>
    /// A neutral unit met in a wave (§25.11.1, GG-T132; Fire Emblem's "talk"): a hero class standing aside on
    /// <see cref="Team.Neutral"/> — neither side fights it, it doesn't hold up the wave. A hero cardinally beside it
    /// issues a <c>TalkCommand</c>: with every <see cref="Conditions"/> met it flips to the hero team as a guest ally
    /// (standing at a victory, it joins the roster); otherwise it says what it wants and the talk costs no time.
    /// </summary>
    public sealed class RecruitableDef
    {
        /// <summary>The node of a Recruit dialogue spoken when it joins; the lines before it are spoken while it still wants something.</summary>
        public const string JoinNode = "join";

        /// <summary>A hero class id.</summary>
        public string Class;
        public int Level = 1;
        /// <summary>Preferred tile (0-based col / row); -1 = a free tile in the middle rows.</summary>
        public int X = -1;
        public int Y = -1;
        /// <summary>HP it enters with, as a percent of max (1-100). 0 = 50 when it wants healing, else full.</summary>
        public int HpPercent;
        /// <summary>A <see cref="DialogueDef"/> with a Recruit trigger naming this class.</summary>
        public string Dialogue;
        public List<RecruitCondition> Conditions = new List<RecruitCondition>();

        public bool WantsHealing => Conditions.Exists(c => c != null && c.Type == RecruitConditionType.HealedBy);

        /// <summary>Gold it asks for in all (every PayGold summed).</summary>
        public int GoldAsked
        {
            get { int g = 0; foreach (var c in Conditions) if (c != null && c.Type == RecruitConditionType.PayGold) g += c.Amount; return g; }
        }

        public int EntryHpPercent => HpPercent > 0 ? Math.Min(100, HpPercent) : WantsHealing ? 50 : 100;

        /// <summary>Content validation (GG-LAW-10): a hero class, a Recruit dialogue for that class, sane conditions.</summary>
        public static void Validate(ContentDb db, RecruitableDef r, string where, List<string> p)
        {
            if (r == null) { p.Add($"{where}: null"); return; }
            bool IsHero(string id) => db.TryClass(id)?.Team == Team.Hero;
            string w = $"{where} {r.Class}";
            if (!IsHero(r.Class)) p.Add($"{where}: needs a hero class, has '{r.Class}'");
            if (r.HpPercent < 0 || r.HpPercent > 100) p.Add($"{w}: HpPercent must be 0-100");
            if (r.Dialogue == null || !db.Dialogues.TryGetValue(r.Dialogue, out var d)) p.Add($"{w}: unknown dialogue '{r.Dialogue}'");
            else if (d.Trigger?.Kind != DialogueTriggerKind.Recruit || !string.Equals(d.Trigger.Id, r.Class, StringComparison.OrdinalIgnoreCase))
                p.Add($"{w}: dialogue '{r.Dialogue}' needs a Recruit trigger naming '{r.Class}'");
            if (r.Conditions.Count == 0) p.Add($"{w}: needs at least one condition (else it's just a guest)");
            foreach (var c in r.Conditions)
            {
                if (c == null) { p.Add($"{w}: null condition"); continue; }
                switch (c.Type)
                {
                    case RecruitConditionType.HealedBy:
                    case RecruitConditionType.ParticipatedInPincerWith:
                        if (c.Id != null && !IsHero(c.Id)) p.Add($"{w}: {c.Type} names unknown hero class '{c.Id}'");
                        break;
                    case RecruitConditionType.PayGold:
                        if (c.Amount <= 0) p.Add($"{w}: PayGold needs a positive Amount");
                        break;
                    case RecruitConditionType.TalkedBy:
                        if (!IsHero(c.Id)) p.Add($"{w}: TalkedBy needs a hero class, has '{c.Id}'");
                        break;
                }
            }
        }
    }
}
