using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Supporter actions (§1.2.3, GG-T187, widened by GG-T298): what a living ally chained to a pincer attacker does when
    /// the pincer resolves — its class's <see cref="SupportDef"/>. Usable by either team (an enemy class with a Support
    /// gets the same effects; one without strikes at 50%).
    /// </summary>
    internal sealed partial class BattleRunner
    {
        /// <summary>
        /// Crit supporters act first (GG-T298): each lends the attacker its chain reaches +Magnitude crit chance for this
        /// pincer's blows (several stack, the total capped at <see cref="SupportDef.MaxSupportedCritPercent"/>).
        /// <see cref="ResolvePincer"/> clears the loan once both attackers have struck.
        /// </summary>
        private void LendSupportCrits(Pincer p, List<Unit> supporters, Team team)
        {
            foreach (var sup in supporters)
            {
                if (!sup.Alive || S.IsOver) continue;
                var def = PincerDetector.SupportOf(sup);
                if (def.Effect != SupportEffect.Crit) continue;
                var attacker = p.AttackerOf(sup);
                if (!attacker.Alive) continue;
                float bonus = Math.Min(SupportDef.MaxCritBonus, Math.Max(0f, def.Magnitude));
                attacker.SupportCritBonus += bonus;
                var ev = new SupportActionEvent { SupporterId = sup.Id, AttackerId = attacker.Id, Effect = def.Effect, Label = def.DisplayLabel, Amount = bonus };
                ev.Targets.Add(attacker.Id);
                Emit(ev);
            }
        }

        /// <summary>
        /// A supporter's class support action (§1.2.3): the classic strike on every trapped opponent, a heal / buff / haste /
        /// mana / cleanse on the attacker it's chained to, the chain itself, the party or itself, or (GG-T298) a spell from
        /// its kit, a status or a shove on the trapped opponents. Pushback is collected into <paramref name="push"/> so the
        /// pincer pushes each foe once. Crit is handled up front by <see cref="LendSupportCrits"/>.
        /// </summary>
        private void PerformSupport(Pincer p, Unit sup, Team team, Dictionary<Unit, float> push)
        {
            var def = PincerDetector.SupportOf(sup);
            var attacker = p.AttackerOf(sup);
            var targets = new List<Unit>();
            AbilityDef spell = null;
            if (HitsTrapped(def.Effect)) { foreach (var o in p.Opponents) if (o.Alive && !wardedThisPincer.Contains(o)) targets.Add(o); }
            else
                switch (def.Target)
                {
                    case SupportTarget.Self: targets.Add(sup); break;
                    case SupportTarget.Chain: foreach (var u in p.ChainOf(sup)) if (u.Alive) targets.Add(u); break;
                    case SupportTarget.Party: foreach (var u in S.Living(team)) targets.Add(u); break;
                    default: if (attacker.Alive) targets.Add(attacker); break;
                }
            if (def.Effect == SupportEffect.Cast)
            {
                spell = PickSupportSpell(sup);
                if (spell == null || targets.Count == 0) return; // no damaging spell in its kit (or silenced): it does nothing
            }
            var ev = new SupportActionEvent
            {
                SupporterId = sup.Id, AttackerId = attacker.Id, Effect = def.Effect, Label = def.DisplayLabel,
                AbilityId = spell?.Id, BuffId = def.Effect == SupportEffect.Status ? def.Buff : null, Amount = def.Magnitude,
            };
            foreach (var t in targets) ev.Targets.Add(t.Id);
            Emit(ev);

            switch (def.Effect)
            {
                case SupportEffect.Strike:
                    float frac = def.Magnitude > 0f ? def.Magnitude : BattleConfig.SupporterDamageFraction;
                    foreach (var target in targets)
                        if (sup.Alive && target.Alive) Strike(sup, target, frac, BlowKind.Support);
                    break;
                case SupportEffect.Heal:
                    foreach (var t in targets)
                        if (t.Alive && t.Hp < t.MaxHp) Heal(t, Math.Max(1, (int)Math.Round(t.MaxHp * def.Magnitude)), sup);
                    break;
                case SupportEffect.Buff:
                    var buff = C.TryBuff(def.Buff);
                    if (buff != null) foreach (var t in targets) ApplyBuff(t, buff, buff.Duration);
                    break;
                case SupportEffect.Haste:
                    foreach (var t in targets) if (t.Alive) HastenSeconds(t, def.Magnitude, "support:" + sup.Id);
                    break;
                case SupportEffect.Mana:
                    if (team == Team.Hero) AddCharge(Math.Max(1, (int)Math.Round(def.Magnitude * BattleConfig.SupportManaFactor)), sup.Id, "support");
                    break;
                case SupportEffect.Cleanse:
                    foreach (var t in targets) RemoveBuffsWhere(t, b => b.Def.Kind == BuffKind.Debuff, "cleansed");
                    break;
                case SupportEffect.Cast:
                    foreach (var t in targets)
                        if (sup.Alive && t.Alive && !S.IsOver) SupportSpellHit(sup, spell, t, def.Magnitude);
                    break;
                case SupportEffect.Status:
                    var status = C.TryBuff(def.Buff);
                    if (status == null) break;
                    float chance = Math.Min(1f, def.Magnitude);
                    foreach (var t in targets)
                        if (t.Alive && (chance >= 1f || S.Rng.Chance(chance))) ApplyBuff(t, status, status.Duration);
                    break;
                case SupportEffect.Pushback:
                    foreach (var t in targets)
                    {
                        push.TryGetValue(t, out var had);
                        push[t] = had + def.Magnitude;
                    }
                    break;
            }
        }

        /// <summary>Effects aimed at the trapped opponents rather than at allies.</summary>
        public static bool HitsTrapped(SupportEffect e) =>
            e == SupportEffect.Strike || e == SupportEffect.Cast || e == SupportEffect.Status || e == SupportEffect.Pushback;

        /// <summary>
        /// The supporter's damaging spells — its class kit, then (heroes) its battle bar, in that order, each once: Spell
        /// kind, BaseDamage &gt; 0, not ally-only. One is drawn with the battle Rng (no draw when there is only one); null
        /// when it has none or is silenced.
        /// </summary>
        private AbilityDef PickSupportSpell(Unit sup)
        {
            if (sup.IsSilenced) return null;
            var list = SupportSpells(C, sup);
            if (list.Count == 0) return null;
            return list.Count == 1 ? list[0] : list[S.Rng.Range(0, list.Count)];
        }

        /// <summary>Every damaging spell <paramref name="u"/> could echo as a Cast supporter (GG-T298), in kit then bar order.</summary>
        public static List<AbilityDef> SupportSpells(ContentDb c, Unit u)
        {
            var list = new List<AbilityDef>();
            void Add(string id)
            {
                var a = c.TryAbility(id);
                if (a == null || a.Kind != AbilityKind.Spell || a.BaseDamage <= 0 || a.Filter == TargetFilter.AllyOnly) return;
                foreach (var x in list) if (x.Id == a.Id) return;
                list.Add(a);
            }
            if (u?.Def?.Abilities != null) foreach (var id in u.Def.Abilities) Add(id);
            if (u?.Bar != null) foreach (var slot in u.Bar) if (slot?.AbilityId != null) Add(slot.AbilityId);
            return list;
        }

        /// <summary>One echoed spell landing on one trapped foe: the spell's own damage (resistances, the caster's INT
        /// power) × <paramref name="power"/>, no crit, no rider status — a support echo, not the real cast.</summary>
        private void SupportSpellHit(Unit sup, AbilityDef spell, Unit t, float power)
        {
            bool wet = spell.DamageType == DamageType.Lightning && t.HasBuff("wet");
            int dmg = Formulas.SpellDamage(spell, t, wet);
            if (dmg <= 0) { Damage(t, 0, sup, HitKind.Normal, spell.DamageType, "Immune"); return; }
            dmg = Math.Max(1, (int)Math.Round(dmg * Formulas.CasterPower(spell, sup) * sup.OutgoingDamageMultiplier * power));
            Damage(t, dmg, sup, HitKind.Normal, spell.DamageType, wet ? "Shock!" : null);
        }

        /// <summary>
        /// Haste in seconds (GG-T298, §2.3): the target acts <paramref name="seconds"/> sooner — the mirror of
        /// <see cref="Pushback"/>. A turn icon gains that much IP at its current rate (never past a full bar); a cast is
        /// rushed by that many ticks; a hero also lifts party tempo by the bar fraction it covers (§2.11).
        /// </summary>
        private void HastenSeconds(Unit t, float seconds, string source)
        {
            if (!(seconds > 0f) || !t.Alive) return;
            seconds = Math.Min(seconds, SupportDef.MaxHasteSeconds);
            int ticks = Formulas.SecondsToTicks(seconds);
            if (t.IsHero)
                AddTempo(source, Math.Max(1, (int)Math.Round(seconds / BattleConfig.BarSeconds / BattleConfig.HastenUPerTempo)), BattleConfig.DefaultTempoTurns);
            var turn = S.TurnEntryOf(t.Id);
            if (turn != null)
            {
                float from = BattleEngine.BarU(S, turn);
                turn.StunTicks = 0;
                turn.Ip = Math.Min(BattleConfig.BarLength, turn.Ip + ticks * BattleEngine.TurnRatePerTick(S, t));
                Emit(new HastenedEvent { EntryId = turn.Id, UnitId = t.Id, FromU = from, ToU = BattleEngine.BarU(S, turn) });
                return;
            }
            var cast = S.CastEntryOf(t.Id);
            if (cast != null)
            {
                float from = BattleEngine.BarU(S, cast);
                cast.RemainingTicks = Math.Max(0, cast.RemainingTicks - ticks);
                Emit(new HastenedEvent { EntryId = cast.Id, UnitId = t.Id, FromU = from, ToU = BattleEngine.BarU(S, cast) });
            }
        }
    }
}
