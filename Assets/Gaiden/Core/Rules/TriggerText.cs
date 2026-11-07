using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    /// <summary>
    /// A Passive or Reactive ability's rule as one generated sentence (§4.1, GG-T296), e.g.
    /// "When [attacked directly]: 15% chance to [Counterstrike] (×0.5)". Keyword terms sit in [brackets] so the
    /// glossary (GG-T300) can make them tappable. Built from the trigger data alone, so text never drifts from rules.
    /// </summary>
    public static class TriggerText
    {
        /// <summary>"Passive" or "Reactive", or null for an Active ability.</summary>
        public static string KindLabel(AbilityDef a) =>
            a == null ? null : a.Kind == AbilityKind.Reactive ? "Reactive" : a.Kind == AbilityKind.Passive ? "Passive" : null;

        public static string Describe(ContentDb c, AbilityDef a)
        {
            var t = a?.Trigger;
            if (t == null) return a?.Description ?? "";
            var effects = new List<string>();
            foreach (var e in t.Effects) effects.Add(Effect(c, e));
            string what = string.Join(" and ", effects);
            if (t.Chance < 1f) what = $"{Pct(t.Chance)} chance to {what}";
            if (t.ChancePerLuck > 0f) what += " (better with [Luck])";
            string when = When(c, t);
            string cond = Condition(c, t.Condition);
            var sb = new System.Text.StringBuilder();
            if (when == null) sb.Append(cond == null ? Cap(what) : $"While {cond}: {what}");
            else sb.Append(cond == null ? $"{when}: {what}" : $"{when}, if {cond}: {what}");
            var extras = new List<string>();
            if (t.OncePerAction) extras.Add("once per action");
            if (t.Cooldown > 0) extras.Add($"then rests {t.Cooldown} turn{(t.Cooldown == 1 ? "" : "s")}");
            if (extras.Count > 0) sb.Append(" (").Append(string.Join(", ", extras)).Append(')');
            sb.Append('.');
            return sb.ToString();
        }

        private static string When(ContentDb c, TriggerDef t)
        {
            switch (t.On)
            {
                case TriggerOn.Always: return null;
                case TriggerOn.OnAttack: return "When it attacks";
                case TriggerOn.OnLandedHit: return "When its blow lands";
                case TriggerOn.OnDamaged: return "When it takes damage";
                case TriggerOn.OnAttackedDirect: return "When [attacked directly]";
                case TriggerOn.OnAttackedAny: return "When a foe's attack lands on it";
                case TriggerOn.OnDodge: return "When it [dodges]";
                case TriggerOn.OnSurrounded: return $"At turn start, when [surrounded] by {t.Count}+ foes";
                case TriggerOn.OnStatusGained: return string.IsNullOrEmpty(t.Status) ? "When it gains a status" : $"When it gains [{BuffName(c, t.Status)}]";
                case TriggerOn.OnLowHp: return $"When a hit drops it below {Pct(t.Threshold)} HP";
                case TriggerOn.OnAllyKO: return "When an ally falls";
                case TriggerOn.OnKill: return "When it fells a foe";
                case TriggerOn.OnPincerAsAttacker: return "After it strikes in a [pincer]";
                case TriggerOn.OnPincerAsSupporter: return "After it supports a [pincer]";
                case TriggerOn.TurnStart: return "At the start of its turn";
                case TriggerOn.TurnEnd: return "At the end of its turn";
                case TriggerOn.ActionEnd: return "After each of its actions";
                default: return t.On.ToString();
            }
        }

        private static string Effect(ContentDb c, TriggerEffect e)
        {
            switch (e.Kind)
            {
                case TriggerEffectKind.Heal: return $"heal {Who(e, "itself")} {Pct(e.Amount)} of max HP";
                case TriggerEffectKind.Lifesteal: return $"[Lifesteal] {Pct(e.Amount)} of the damage";
                case TriggerEffectKind.ApplyBuff: return $"give {Who(e, "itself")} [{BuffName(c, e.Buff)}]";
                case TriggerEffectKind.Counterstrike: return $"[Counterstrike] (×{e.Amount:0.##})";
                case TriggerEffectKind.FollowUp: return $"strike again (×{e.Amount:0.##})";
                case TriggerEffectKind.CastAbility: return $"cast [{c?.TryAbility(e.Ability)?.Name ?? e.Ability}] for free";
                case TriggerEffectKind.Reflect: return $"[Reflect] {Pct(e.Amount)} of the damage at the attacker";
                case TriggerEffectKind.Cleanse: return $"[Cleanse] {Who(e, "itself")}";
                case TriggerEffectKind.ManaCharge: return $"add {e.Amount:0} [mana charge]";
                case TriggerEffectKind.Pushback: return $"[Pushback] {Who(e, "the attacker")} {e.Amount:0.#}s";
                case TriggerEffectKind.DamageMultiplier: return $"its blows hit ×{e.Amount:0.##}";
                case TriggerEffectKind.ReductionMultiplier: return e.Amount <= 0f ? "it takes no damage" : $"it takes ×{e.Amount:0.##} damage";
                case TriggerEffectKind.Immunity:
                    var names = new List<string>();
                    if (e.Statuses != null) foreach (var s in e.Statuses) names.Add($"[{BuffName(c, s)}]");
                    return "it can't be " + string.Join(" or ", names);
                default: return e.Kind.ToString();
            }
        }

        private static string Condition(ContentDb c, TriggerCondition k)
        {
            if (k == null) return null;
            var parts = new List<string>();
            if (k.SelfHpBelow > 0f) parts.Add($"below {Pct(k.SelfHpBelow)} HP");
            if (k.SelfHpAbove > 0f) parts.Add($"above {Pct(k.SelfHpAbove)} HP");
            if (k.HasStatus != null) parts.Add($"it has [{BuffName(c, k.HasStatus)}]");
            if (k.LacksStatus != null) parts.Add($"it lacks [{BuffName(c, k.LacksStatus)}]");
            if (k.AdjacentFoesAtLeast > 0) parts.Add($"{k.AdjacentFoesAtLeast}+ foes are adjacent");
            if (k.OtherTag != null) parts.Add($"the foe is [{k.OtherTag}]");
            if (k.OtherHasStatus != null) parts.Add($"the foe has [{BuffName(c, k.OtherHasStatus)}]");
            if (k.DamageType.HasValue) parts.Add($"the damage is [{k.DamageType.Value}]");
            return parts.Count == 0 ? null : string.Join(" and ", parts);
        }

        private static string Who(TriggerEffect e, string fallback) =>
            e.Who == EffectTarget.Self ? "itself" : e.Who == EffectTarget.Target ? "the target" : e.Who == EffectTarget.Attacker ? "the attacker" : fallback;

        private static string BuffName(ContentDb c, string id) => c?.TryBuff(id)?.Name ?? id;

        private static string Pct(float f) => $"{Math.Round(f * 100f, 1):0.#}%";

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
