using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// The trigger engine (§4.1, GG-T296): every Passive and Reactive ability, heroes' and enemies' alike, fires from here.
    /// The funnels (Damage, Strike, Kill, ApplyBuff, pincer resolve, turn and action boundaries) call
    /// <see cref="Fire"/>; the pure rules live in <see cref="Triggers"/>.
    ///
    /// Determinism (GG-LAW-9): owners in unit-id order, each owner's abilities in <see cref="Triggers.Sources"/> order,
    /// and only <see cref="TriggerDef.Chance"/> below 1 draws from the battle Rng.
    /// Recursion: a Reactive never fires from inside another Reactive's effects (a reaction never provokes a reaction,
    /// the old Counter guard generalised), and no ability re-enters itself (the old Thorns guard) — so a Passive like
    /// Lifesteal still rides a counter blow, but a counter is never countered.
    /// </summary>
    internal sealed partial class BattleRunner
    {
        private int reactiveDepth;
        private readonly HashSet<string> firingAbilities = new HashSet<string>();
        /// <summary>"unitId:abilityId" fired this action, for <see cref="TriggerDef.OncePerAction"/> (cleared per command and per enemy turn).</summary>
        private readonly HashSet<string> firedThisAction = new HashSet<string>();

        /// <summary>Fires <paramref name="on"/> for one owner.</summary>
        private void Fire(TriggerOn on, Unit owner, TriggerContext ctx)
        {
            if (owner == null || !owner.OnBoard || owner.Def == null || S.IsOver) return;
            if (owner.IsCorpse || owner.IsRemains || owner.IsEgg) return;
            foreach (var a in Triggers.Sources(C, owner))
            {
                var t = a.Trigger;
                if (t.On != on || S.IsOver) continue;
                bool reactive = a.Kind == AbilityKind.Reactive;
                if (reactive && reactiveDepth > 0) continue;
                if (firingAbilities.Contains(a.Id)) continue;
                if (t.Cooldown > 0 && owner.TriggerCooldowns.TryGetValue(a.Id, out var cd) && cd > 0) continue;
                string key = owner.Id + ":" + a.Id;
                if (t.OncePerAction && firedThisAction.Contains(key)) continue;
                if (!Triggers.Matches(S, t, owner, ctx)) continue;
                if (t.Chance < 1f || t.ChancePerLuck > 0f)
                {
                    float chance = Math.Max(0f, Math.Min(1f, t.Chance + t.ChancePerLuck * owner.Effective.Luck));
                    if (!S.Rng.Chance(chance)) continue;
                }

                int mark = ev.Count;
                firingAbilities.Add(a.Id);
                if (reactive) reactiveDepth++;
                TriggerEffectKind? first = null;
                try
                {
                    foreach (var e in t.Effects)
                        if (RunEffect(a, e, owner, ctx) && first == null) first = e.Kind;
                }
                finally
                {
                    firingAbilities.Remove(a.Id);
                    if (reactive) reactiveDepth--;
                }
                if (first == null) continue;
                if (t.Cooldown > 0) owner.TriggerCooldowns[a.Id] = t.Cooldown;
                if (t.OncePerAction) firedThisAction.Add(key);
                // The cue goes ahead of the effects it caused (the float reads "Counter!" before the counter blow).
                if (!PreviewOnly)
                    ev.Insert(Math.Min(mark, ev.Count), new TriggeredEvent
                    {
                        UnitId = owner.Id, AbilityId = a.Id, Kind = a.Kind, On = on, Effect = first.Value,
                        Callout = string.IsNullOrEmpty(t.Callout) ? a.Name : t.Callout,
                    });
            }
        }

        /// <summary>Fires <paramref name="on"/> for every living unit <paramref name="who"/> accepts, in unit-id order.</summary>
        private void FireAll(TriggerOn on, Func<Unit, bool> who, Func<Unit, TriggerContext> ctx)
        {
            var owners = new List<Unit>();
            foreach (var u in S.Units) if (u.Alive && who(u)) owners.Add(u);
            owners.Sort((x, y) => x.Id.CompareTo(y.Id));
            foreach (var u in owners) if (u.Alive) Fire(on, u, ctx(u));
        }

        /// <summary>The owner's turn begins: trigger cooldowns tick, then TurnStart and OnSurrounded fire.</summary>
        private void TriggersTurnStart(Unit owner)
        {
            if (owner.TriggerCooldowns.Count > 0)
                foreach (var k in new List<string>(owner.TriggerCooldowns.Keys))
                    if (owner.TriggerCooldowns[k] > 0) owner.TriggerCooldowns[k]--;
            Fire(TriggerOn.TurnStart, owner, new TriggerContext());
            if (owner.Alive) Fire(TriggerOn.OnSurrounded, owner, new TriggerContext());
        }

        private bool RunEffect(AbilityDef a, TriggerEffect e, Unit owner, TriggerContext ctx)
        {
            switch (e.Kind)
            {
                case TriggerEffectKind.Heal:
                {
                    var who = Who(e.Who, owner, ctx, owner);
                    if (who == null || !who.Alive || who.Hp >= who.MaxHp) return false;
                    Heal(who, Triggers.FloorScaled(who.MaxHp, e.Amount), owner);
                    return true;
                }
                case TriggerEffectKind.Lifesteal:
                    if (ctx.Damage <= 0 || !owner.Alive || owner.Hp >= owner.MaxHp) return false;
                    Heal(owner, Math.Max(1, (int)Math.Round(ctx.Damage * e.Amount)), owner);
                    return true;
                case TriggerEffectKind.ApplyBuff:
                {
                    var who = Who(e.Who, owner, ctx, owner);
                    var buff = C.TryBuff(e.Buff);
                    if (who == null || !who.Alive || buff == null) return false;
                    ApplyBuff(who, buff, buff.Duration);
                    return true;
                }
                case TriggerEffectKind.Counterstrike:
                {
                    var victim = Who(e.Who, owner, ctx, ctx.Attacker ?? ctx.Target);
                    if (victim == null || !victim.Alive || !owner.Alive || !Triggers.Foes(owner, victim) || !InReach(owner, victim)) return false;
                    Strike(owner, victim, e.Amount, BlowKind.Counter);
                    return true;
                }
                case TriggerEffectKind.FollowUp:
                {
                    var victim = ctx.Target;
                    if (victim == null || !victim.Alive || !owner.Alive) return false;
                    Strike(owner, victim, ctx.StrikeMultiplier * e.Amount, ctx.Blow, followUp: true);
                    return true;
                }
                case TriggerEffectKind.CastAbility:
                {
                    var cast = C.TryAbility(e.Ability);
                    if (cast == null || !owner.Alive) return false;
                    if (cast.Kind == AbilityKind.Spell && owner.IsSilenced) return false;
                    var at = Who(e.Who, owner, ctx, ctx.Attacker ?? ctx.Target ?? owner);
                    if (at != null && !at.Alive) at = null;
                    Coord anchor = cast.Mode == TargetMode.Auto ? TargetResolver.AutoAnchor(owner) : (at ?? owner).Pos;
                    if (cast.Mode != TargetMode.Auto && at == null) return false;
                    ResolveAbility(owner, cast, anchor, cast.Mode == TargetMode.Auto ? null : at);
                    return true;
                }
                case TriggerEffectKind.Reflect:
                {
                    var attacker = ctx.Attacker;
                    if (ctx.Damage <= 0 || attacker == null || !attacker.Alive || !Triggers.Foes(owner, attacker)) return false;
                    Damage(attacker, Math.Max(1, (int)Math.Round(ctx.Damage * e.Amount)), owner, HitKind.Normal, DamageType.Physical, a.Name);
                    return true;
                }
                case TriggerEffectKind.Cleanse:
                {
                    var who = Who(e.Who, owner, ctx, owner);
                    if (who == null || !who.Alive) return false;
                    int before = who.Buffs.Count;
                    RemoveBuffsWhere(who, b => b.Def.Kind == BuffKind.Debuff, "cleansed");
                    return who.Buffs.Count < before;
                }
                case TriggerEffectKind.ManaCharge:
                    if (!owner.IsHero) return false;
                    AddCharge((int)e.Amount, owner.Id, "passive");
                    return true;
                case TriggerEffectKind.Pushback:
                {
                    var who = Who(e.Who, owner, ctx, ctx.Attacker ?? ctx.Target);
                    if (who == null || !who.Alive || !Triggers.Foes(owner, who) || pushedThisAction.Contains(who.Id)) return false;
                    Pushback(who, e.Amount);
                    return true;
                }
                default:
                    return false; // Always-on modifiers are read where their number is computed, never "fired"
            }
        }

        private static Unit Who(EffectTarget who, Unit owner, TriggerContext ctx, Unit fallback)
        {
            switch (who)
            {
                case EffectTarget.Self: return owner;
                case EffectTarget.Target: return ctx.Target;
                case EffectTarget.Attacker: return ctx.Attacker;
                default: return fallback;
            }
        }

        /// <summary>A counterstrike needs a weapon that reaches: adjacent, or within a ranged unit's AttackRange.</summary>
        private static bool InReach(Unit owner, Unit victim) =>
            owner.IsAdjacentTo(victim) || (owner.IsRanged && owner.DistanceTo(victim) <= owner.Def.AttackRange);
    }
}
