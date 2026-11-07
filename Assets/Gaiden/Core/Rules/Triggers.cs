using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    /// <summary>The weapon blow a trigger context came from (GG-T296).</summary>
    public enum BlowKind { None, Pincer, Support, Melee, Ranged, Counter }

    /// <summary>What happened, as the trigger engine sees it (§4.1, GG-T296). "Other" for a condition is the Attacker, else the Target.</summary>
    public sealed class TriggerContext
    {
        /// <summary>The unit the owner hit or is acting on (OnLandedHit/OnAttack/OnKill target, the fallen ally, the backed attacker).</summary>
        public Unit Target;
        /// <summary>The unit that hit the owner (OnAttacked*, OnDamaged, OnDodge) or felled its ally.</summary>
        public Unit Attacker;
        public int Damage;
        public DamageType? Type;
        public BlowKind Blow;
        /// <summary>The multiplier of the blow that fired it (FollowUp strikes at this × its Amount).</summary>
        public float StrikeMultiplier = 1f;
        public string Status;
        /// <summary>OnLowHp: the owner's HP fraction before the hit.</summary>
        public float HpBefore = 1f;

        public Unit Other => Attacker ?? Target;
    }

    /// <summary>
    /// The pure half of the trigger engine (§4.1, GG-T296): where a unit's passives and reactives come from, whether a
    /// trigger's event parameters and condition hold, and the always-on modifiers. The battle runner fires the events
    /// (BattleRunner.Triggers.cs) and resolves the effects; nothing here mutates state or touches the Rng.
    /// </summary>
    public static class Triggers
    {
        public static bool IsTriggered(AbilityDef a) =>
            a != null && a.Trigger != null && (a.Kind == AbilityKind.Passive || a.Kind == AbilityKind.Reactive);

        /// <summary>Two units on opposite sides of the fight (neutrals are nobody's foe).</summary>
        public static bool Foes(Unit a, Unit b) =>
            a != null && b != null && a.Team != b.Team && a.Team != Team.Neutral && b.Team != Team.Neutral;

        private static readonly ConditionalWeakTable<ItemDef, AbilityDef> weaponPassives = new ConditionalWeakTable<ItemDef, AbilityDef>();

        /// <summary>A named weapon's lifesteal (§24.6.1, Vampiric Edge) as a trigger: its PassiveName heals the wielder
        /// LifestealFraction of every landed blow.</summary>
        public static AbilityDef WeaponPassive(ItemDef weapon)
        {
            if (weapon == null || weapon.LifestealFraction <= 0f) return null;
            return weaponPassives.GetValue(weapon, w => new AbilityDef
            {
                Id = "weapon:" + w.Id,
                Name = string.IsNullOrEmpty(w.PassiveName) ? w.Name : w.PassiveName,
                Description = w.PassiveDescription,
                Kind = AbilityKind.Passive,
                Trigger = new TriggerDef
                {
                    On = TriggerOn.OnLandedHit,
                    Effects = { new TriggerEffect { Kind = TriggerEffectKind.Lifesteal, Amount = w.LifestealFraction } },
                },
            });
        }

        /// <summary>
        /// Every Passive/Reactive ability <paramref name="u"/> has, in the fixed evaluation order: its class kit
        /// (<see cref="ClassDef.Abilities"/>), then passives it learned (<see cref="Unit.Passives"/>: skill-tree grants,
        /// and the hook a reward ticket adds to), then enchants on intact armor (slot order), then its weapon's named
        /// passive. An ability reached twice counts once.
        /// </summary>
        public static List<AbilityDef> Sources(ContentDb c, Unit u)
        {
            var list = new List<AbilityDef>();
            if (u?.Def == null) return list;
            void Add(AbilityDef a)
            {
                if (!IsTriggered(a)) return;
                foreach (var x in list) if (x.Id == a.Id) return;
                list.Add(a);
            }
            if (u.Def.Abilities != null) foreach (var id in u.Def.Abilities) Add(c.TryAbility(id));
            foreach (var id in u.Passives) Add(c.TryAbility(id));
            foreach (var slot in BattleEngine.ArmorSlots)
                if (u.Gear.TryGetValue(slot, out var piece) && piece.Durability > 0 && piece.Enchant != null)
                    Add(c.TryAbility(c.TryArmorEnchant(piece.Enchant)?.Passive));
            if (u.IsHero && u.WeaponId != null) Add(WeaponPassive(c.TryItem(u.WeaponId)));
            return list;
        }

        /// <summary>The trigger's event parameters (Count/Threshold/Status) and its condition hold for this context.</summary>
        public static bool Matches(BattleState s, TriggerDef t, Unit owner, TriggerContext ctx)
        {
            switch (t.On)
            {
                case TriggerOn.OnSurrounded: if (AdjacentFoes(s, owner) < t.Count) return false; break;
                case TriggerOn.OnLowHp: if (!(ctx.HpBefore >= t.Threshold && owner.HpFraction < t.Threshold && owner.Hp > 0)) return false; break;
                case TriggerOn.OnStatusGained: if (!string.IsNullOrEmpty(t.Status) && ctx.Status != t.Status) return false; break;
            }
            return ConditionHolds(s, t.Condition, owner, ctx);
        }

        public static bool ConditionHolds(BattleState s, TriggerCondition c, Unit owner, TriggerContext ctx)
        {
            if (c == null) return true;
            if (c.SelfHpBelow > 0f && !(owner.HpFraction < c.SelfHpBelow)) return false;
            if (c.SelfHpAbove > 0f && !(owner.HpFraction > c.SelfHpAbove)) return false;
            if (c.HasStatus != null && !owner.HasBuff(c.HasStatus)) return false;
            if (c.LacksStatus != null && owner.HasBuff(c.LacksStatus)) return false;
            if (c.AdjacentFoesAtLeast > 0 && AdjacentFoes(s, owner) < c.AdjacentFoesAtLeast) return false;
            if (c.OtherTag != null && (ctx?.Other == null || !ctx.Other.Def.HasTag(c.OtherTag))) return false;
            if (c.OtherHasStatus != null && (ctx?.Other == null || !ctx.Other.HasBuff(c.OtherHasStatus))) return false;
            if (c.OtherHpBelow > 0f && (ctx?.Other == null || !(ctx.Other.HpFraction < c.OtherHpBelow))) return false;
            if (c.DamageType.HasValue && (ctx?.Type == null || ctx.Type.Value != c.DamageType.Value)) return false;
            return true;
        }

        public static int AdjacentFoes(BattleState s, Unit owner)
        {
            int n = 0;
            foreach (var o in s.Units)
                if (o.Alive && o != owner && !o.IsCorpse && !o.IsRemains && Foes(owner, o) && owner.IsAdjacentTo(o)) n++;
            return n;
        }

        /// <summary>Always-on DamageMultiplier product for the owner's weapon blow on <paramref name="target"/> (Berserk).</summary>
        public static float OutgoingMultiplier(BattleState s, Unit owner, Unit target)
        {
            float m = 1f;
            var ctx = new TriggerContext { Target = target, Type = DamageType.Physical };
            foreach (var a in Sources(s.Content, owner))
                if (a.Trigger.On == TriggerOn.Always && ConditionHolds(s, a.Trigger.Condition, owner, ctx))
                    foreach (var e in a.Trigger.Effects)
                        if (e.Kind == TriggerEffectKind.DamageMultiplier) m *= e.Amount;
            return m;
        }

        /// <summary>Always-on ReductionMultiplier product for damage of <paramref name="type"/> the owner takes from <paramref name="source"/> (Tough Hide).</summary>
        public static float IncomingMultiplier(BattleState s, Unit owner, Unit source, DamageType type)
        {
            float m = 1f;
            var ctx = new TriggerContext { Attacker = source, Type = type };
            foreach (var a in Sources(s.Content, owner))
                if (a.Trigger.On == TriggerOn.Always && ConditionHolds(s, a.Trigger.Condition, owner, ctx))
                    foreach (var e in a.Trigger.Effects)
                        if (e.Kind == TriggerEffectKind.ReductionMultiplier) m *= e.Amount;
            return m;
        }

        /// <summary>The Always-on Immunity that wards <paramref name="owner"/> off <paramref name="buffId"/>, or null (Poison Ward, Steadfast).</summary>
        public static AbilityDef ImmunityTo(BattleState s, Unit owner, string buffId)
        {
            foreach (var a in Sources(s.Content, owner))
                if (a.Trigger.On == TriggerOn.Always && ConditionHolds(s, a.Trigger.Condition, owner, null))
                    foreach (var e in a.Trigger.Effects)
                        if (e.Kind == TriggerEffectKind.Immunity && e.Statuses != null && e.Statuses.Contains(buffId)) return a;
            return null;
        }

        /// <summary>Scales an amount by a fraction, flooring with a hair of slack so float fractions (0.06f, 0.7f) land on
        /// the same whole numbers integer percentages did; never below 1.</summary>
        public static int FloorScaled(int amount, float fraction) => Math.Max(1, (int)Math.Floor(amount * (double)fraction + 1e-4));
    }
}
