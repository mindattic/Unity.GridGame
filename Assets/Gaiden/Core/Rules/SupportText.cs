using System;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    /// <summary>
    /// A class's support action (§1.2.3, GG-T298) as one generated sentence, e.g. "As a [Supporter] (reach 2): gives the
    /// attacker +20% [Crit] chance on its pincer blows." Keyword terms sit in [brackets] for the glossary (GG-T300);
    /// built from the <see cref="SupportDef"/> alone so the card never drifts from the rule.
    /// </summary>
    public static class SupportText
    {
        public static string Describe(ContentDb c, SupportDef d) => Describe(c, d, false);

        /// <summary>
        /// <paramref name="named"/> (GG-T307, the Bestiary and a foe's InspectCard) adds the support's own name:
        /// "As a [Supporter] (reach 1), Tongue Lash: adds 0.5 s of [Pushback] to each trapped foe."
        /// </summary>
        public static string Describe(ContentDb c, SupportDef d, bool named)
        {
            d = d ?? SupportDef.Default;
            string label = named && !string.IsNullOrEmpty(d.Label) ? ", " + d.Label : "";
            return $"As a [Supporter] (reach {Math.Max(1, d.Range)}){label}: {Body(c, d)}.";
        }

        /// <summary>The action alone ("strikes each trapped foe at 50% power"), lower-case, no full stop.</summary>
        public static string Body(ContentDb c, SupportDef d)
        {
            string who = Who(d.Target);
            switch (d.Effect)
            {
                case SupportEffect.Strike:
                    return $"strikes each trapped foe at {Pct(d.Magnitude > 0f ? d.Magnitude : BattleConfig.SupporterDamageFraction)} power";
                case SupportEffect.Heal: return $"heals {who} {Pct(d.Magnitude)} of max HP";
                case SupportEffect.Buff: return $"gives {who} {Term(c, BuffName(c, d.Buff))}";
                case SupportEffect.Haste: return $"[Hastens] {who} {Sec(d.Magnitude)}";
                case SupportEffect.Mana: return $"adds {Math.Max(1, (int)Math.Round(d.Magnitude * BattleConfig.SupportManaFactor))} mana charge";
                case SupportEffect.Cleanse: return $"[Cleanses] every debuff from {who}";
                case SupportEffect.Crit: return $"gives the attacker +{Pct(d.Magnitude)} [Crit] chance on its pincer blows";
                case SupportEffect.Cast: return $"casts a random spell from its kit on each trapped foe at {Pct(d.Magnitude)} power";
                case SupportEffect.Status:
                    return d.Magnitude >= 1f
                        ? $"inflicts {Term(c, BuffName(c, d.Buff))} on each trapped foe"
                        : $"{Glossary.ChanceWord(d.Magnitude)} chance ({Pct(d.Magnitude)}) to inflict {Term(c, BuffName(c, d.Buff))} on each trapped foe";
                case SupportEffect.Pushback: return $"adds {Sec(d.Magnitude)} of [Pushback] to each trapped foe";
                default: return d.Effect.ToString().ToLowerInvariant();
            }
        }

        /// <summary>The few words a cut-in float carries after the label ("+20% Crit", "Fireball", "Poisoned 50%").</summary>
        public static string Short(ContentDb c, SupportEffect e, float amount, string abilityId = null, string buffId = null)
        {
            switch (e)
            {
                case SupportEffect.Crit: return $"+{Pct(amount)} Crit";
                case SupportEffect.Cast: return c?.TryAbility(abilityId)?.Name ?? "Spell";
                case SupportEffect.Status: return BuffName(c, buffId);
                case SupportEffect.Pushback: return $"Pushback +{Sec(amount)}";
                case SupportEffect.Haste: return $"Haste {Sec(amount)}";
                default: return null;
            }
        }

        private static string Who(SupportTarget t)
        {
            switch (t)
            {
                case SupportTarget.Chain: return "everyone on its chain";
                case SupportTarget.Party: return "all its allies"; // GG-T307: reads right for a foe's side too
                case SupportTarget.Self: return "itself";
                default: return "the attacker";
            }
        }

        private static string BuffName(ContentDb c, string id) => c?.TryBuff(id)?.Name ?? id ?? "?";
        private static string Term(ContentDb c, string name) => c?.TryTerm(name) != null ? $"[{name}]" : name;
        private static string Pct(float f) => $"{(int)Math.Round(f * 100f)}%";
        private static string Sec(float s) => s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}
