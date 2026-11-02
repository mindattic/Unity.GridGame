using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    public struct AttackRoll
    {
        public int Damage;
        public HitKind Hit;
    }

    /// <summary>Combat math (bible §3.2, §13.1). Ported from the old Formulas with engine types removed.</summary>
    public static class Formulas
    {
        private const float HitShiftPerLevel = 3f;
        private const float DamageScalePerLevel = 1.07f;
        private const float CritMultiplier = 1.60f;
        private const float DefenseMitigation = 0.35f;
        private const float PenetrationFromAttack = 0.25f;
        private const float OffenseFloorFraction = 0.20f;

        /// <summary>Unarmed Mastery (GG-T86): flat part of the bare-handed offense bonus.</summary>
        public const float UnarmedMasteryFlat = 2f;
        /// <summary>Unarmed Mastery (GG-T86): bare-handed offense per point of AGI.</summary>
        public const float UnarmedMasteryPerAgility = 0.5f;
        /// <summary>Tag a class carries in content to fight bare-handed with Unarmed Mastery.</summary>
        public const string UnarmedTag = "Unarmed";

        /// <summary>Hero classes built around unarmed / martial-arts fighting (signature moves like
        /// martialartist_palm, pugilist_jabcross, monk_peace). They get Unarmed Mastery even before their
        /// content carries the <see cref="UnarmedTag"/> tag.</summary>
        public static readonly HashSet<string> UnarmedMasteryClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "monk", "martialartist", "pugilist", "streetfighter",
        };

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Whether this class fights with Unarmed Mastery (GG-T86).</summary>
        public static bool HasUnarmedMastery(ClassDef def) =>
            def != null && def.Team == Team.Hero && (UnarmedMasteryClasses.Contains(def.Id) || def.HasTag(UnarmedTag));

        /// <summary>
        /// Unarmed Mastery (GG-T86): a hero of an unarmed class with an empty weapon hand adds
        /// <c>2 + 0.5·AGI</c> to physical offense in place of a weapon (≈8 at level 1, growing with AGI, on par
        /// with what a Common–Rare weapon's stats add), and never has a weapon to wear down or shatter. Holding
        /// a weapon switches it off, so these classes are rewarded for fighting bare-handed. Every other class
        /// and every enemy gets 0, keeping a weapon the better choice for them.
        /// </summary>
        public static float UnarmedPower(Unit u)
        {
            if (u == null || !u.IsHero || !string.IsNullOrEmpty(u.WeaponId) || !HasUnarmedMastery(u.Def)) return 0f;
            return UnarmedMasteryFlat + UnarmedMasteryPerAgility * Math.Max(0f, u.Effective.Agility);
        }

        public static int MaxHp(Stats s, int level) => (int)Math.Floor(12f + s.Vitality * 5f + level * 1f);

        /// <summary>Level-derived stats: base + per-level growth + instant milestone bonuses, floored (old ActorData.GetStatsWithOptions).</summary>
        public static Stats StatsAtLevel(ClassDef def, int level)
        {
            if (level < 1) level = 1;
            var s = def.BaseStats.Clone();
            var milestones = new Dictionary<int, Stats>();
            foreach (var m in def.Milestones) if (m != null) milestones[m.Level] = m.Growth;
            for (int l = 2; l <= level; l++)
            {
                s = s + def.Growth;
                if (milestones.TryGetValue(l, out var bonus)) s = s + bonus;
            }
            s.Strength = (float)Math.Floor(s.Strength);
            s.Vitality = (float)Math.Floor(s.Vitality);
            s.Agility = (float)Math.Floor(s.Agility);
            s.Speed = (float)Math.Floor(s.Speed);
            s.Stamina = (float)Math.Floor(s.Stamina);
            s.Intelligence = (float)Math.Floor(s.Intelligence);
            s.Wisdom = (float)Math.Floor(s.Wisdom);
            s.Luck = (float)Math.Floor(s.Luck);
            return s;
        }

        public static float HitPercent(Stats s) => Clamp(80f + s.Speed * 0.5f + s.Luck * 0.2f, 5f, 95f);
        public static float CritPercent(Stats s) => Clamp(8f + s.Luck * 0.4f, 0f, 50f);

        public static HitKind RollHit(Unit attacker, Unit defender, Rng rng)
        {
            var a = attacker.Effective;
            int adv = attacker.Level - defender.Level;
            float hit = Clamp(HitPercent(a) + adv * HitShiftPerLevel, 5f, 95f);
            float acc = attacker.AccuracyMultiplier;
            if (acc < 1f) hit = Clamp(hit * acc, 5f, 95f);
            float crit = CritPercent(a);
            float roll = rng.Range(0f, 100f);
            if (roll <= crit) return HitKind.Critical;
            if (roll <= hit) return HitKind.Normal;
            return HitKind.Miss;
        }

        private static float Variance(Stats s, Rng rng, float range)
        {
            float lo = 1f - range, hi = 1f + range;
            float roll = rng.Range(lo, hi);
            float tilt = 0.99f + 0.02f * Clamp(s.Luck / 100f, 0f, 1f);
            return Clamp(roll * tilt, lo, hi);
        }

        private static float Mitigate(float off, float def)
        {
            float pen = off * PenetrationFromAttack;
            float effDef = Math.Max(0f, def - pen);
            float reduced = off - effDef * DefenseMitigation;
            float floor = off * OffenseFloorFraction;
            return Math.Max(1f, Math.Max(reduced, floor));
        }

        /// <summary>Physical pincer / melee attack (§13.1.1).</summary>
        public static AttackRoll Physical(Unit attacker, Unit defender, Rng rng, float multiplier = 1f)
        {
            var a = attacker.Effective;
            var d = defender.Effective;
            float off = a.Strength + attacker.WeaponPower + UnarmedPower(attacker);
            float def = d.Vitality;
            float raw = Mitigate(off, def);
            raw *= defender.ResistanceMultiplier(DamageType.Physical);
            float varied = raw * Variance(a, rng, 0.10f);
            var hit = RollHit(attacker, defender, rng);
            if (hit == HitKind.Miss) return new AttackRoll { Damage = 0, Hit = HitKind.Miss };
            float typeMult = hit == HitKind.Critical ? CritMultiplier : 1f;
            float levelMult = (float)Math.Pow(DamageScalePerLevel, attacker.Level - defender.Level);
            int dmg = Math.Max(1, (int)Math.Floor(varied * typeMult * levelMult * multiplier));
            float inc = defender.IncomingDamageMultiplier;
            if (inc != 1f) dmg = Math.Max(1, (int)Math.Floor(dmg * inc));
            if (defender.ResistanceMultiplier(DamageType.Physical) <= 0f) dmg = 0;
            return new AttackRoll { Damage = dmg, Hit = hit };
        }

        /// <summary>Enemy magic hit (charge resolution): INT vs WIS, same mitigation shape (§13.1).</summary>
        public static AttackRoll Magic(Unit caster, Unit target, DamageType type, Rng rng)
        {
            var a = caster.Effective;
            var d = target.Effective;
            float raw = Mitigate(a.Intelligence, d.Wisdom);
            float res = target.ResistanceMultiplier(type);
            raw *= res;
            float varied = raw * Variance(a, rng, 0.10f);
            var hit = RollHit(caster, target, rng);
            if (hit == HitKind.Miss) return new AttackRoll { Damage = 0, Hit = HitKind.Miss };
            float typeMult = hit == HitKind.Critical ? CritMultiplier : 1f;
            float levelMult = (float)Math.Pow(DamageScalePerLevel, caster.Level - target.Level);
            int dmg = res <= 0f ? 0 : Math.Max(1, (int)Math.Floor(varied * typeMult * levelMult));
            float inc = target.IncomingDamageMultiplier;
            if (dmg > 0 && inc != 1f) dmg = Math.Max(1, (int)Math.Floor(dmg * inc));
            return new AttackRoll { Damage = dmg, Hit = hit };
        }

        /// <summary>Dispatcher spell damage (§13.1.2): flat base × resistance × wet bonus × incoming mult, min 1 unless immune.</summary>
        public static int SpellDamage(AbilityDef spell, Unit target, bool wetBonus)
        {
            float res = target.ResistanceMultiplier(spell.DamageType);
            float elem = wetBonus ? BattleConfig.LightningWhenWetMultiplier : 1f;
            float final = (float)Math.Round(spell.BaseDamage * res * elem * target.IncomingDamageMultiplier);
            if (spell.BaseDamage > 0 && res > 0f) final = Math.Max(1f, final);
            return (int)final;
        }

        /// <summary>
        /// Role identity (§13.1.5): a hero's spell lands harder the higher its INT (physical skills: STR), and its heals
        /// mend more the higher its WIS — so a Red Mage's Fireball outclasses a Fighter's. 12 is par; ±3.5% per point.
        /// </summary>
        public static float CasterPower(AbilityDef a, Unit caster)
        {
            if (caster == null || !caster.IsHero || a == null) return 1f;
            var s = caster.Effective;
            float stat = a.DamageType == DamageType.Physical ? s.Strength : s.Intelligence;
            return Clamp(1f + (stat - 12f) * BattleConfig.CasterPowerPerPoint, BattleConfig.CasterPowerMin, BattleConfig.CasterPowerMax);
        }

        public static float HealPower(Unit caster)
        {
            if (caster == null || !caster.IsHero) return 1f;
            return Clamp(1f + (caster.Effective.Wisdom - 12f) * BattleConfig.CasterPowerPerPoint, BattleConfig.CasterPowerMin, BattleConfig.CasterPowerMax);
        }

        /// <summary>WIS+INT shortens cast time, floored at 25% of base (old Formulas.CastTime).</summary>
        public static float CastTime(float baseSeconds, Stats s)
        {
            if (baseSeconds <= 0f) return 0f;
            float skill = Math.Max(0f, s.Wisdom + s.Intelligence);
            float scale = 50f / (50f + skill * 0.5f);
            return Math.Max(baseSeconds * 0.25f, baseSeconds * scale);
        }

        public static int SecondsToTicks(float seconds) => Math.Max(1, (int)Math.Round(seconds * BattleConfig.TicksPerSecond));

        /// <summary>XP awarded for defeating an enemy (old ExperienceHelper.Calculate shape).</summary>
        public static int XpReward(Unit enemy)
        {
            var s = enemy.Effective;
            float phys = s.Strength + s.Vitality;
            float magic = s.Intelligence + s.Wisdom;
            float reward = enemy.Def.XpReward + enemy.Level * 2f + phys * 0.10f + magic * 0.08f;
            return Math.Max(1, (int)Math.Round(reward));
        }

        /// <summary>Total XP needed to go from <paramref name="level"/> to level+1.</summary>
        public static int XpToNext(int level) => 50 + level * level * 10;

        public static (int level, int intoLevel) LevelFromTotalXp(int totalXp)
        {
            int level = 1, remaining = Math.Max(0, totalXp);
            while (level < 99 && remaining >= XpToNext(level)) { remaining -= XpToNext(level); level++; }
            return (level, remaining);
        }

        public static int TotalXpForLevel(int level)
        {
            int total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return total;
        }

        /// <summary>Skill points earned per level (GG-T49), plus a bonus every milestone level.</summary>
        public const int SkillPointsPerLevel = 1;
        public const int SkillPointMilestoneInterval = 5;
        public const int SkillPointMilestoneBonus = 1;

        /// <summary>Skill points earned crossing from <paramref name="fromLevel"/> to <paramref name="toLevel"/>
        /// (exclusive/inclusive) — summed per level so a multi-level jump (a big XP grant, a guest or recruit
        /// arriving above level 1) still hits every milestone along the way.</summary>
        public static int SkillPointsForLevelUp(int fromLevel, int toLevel)
        {
            int total = 0;
            for (int l = fromLevel + 1; l <= toLevel; l++)
            {
                total += SkillPointsPerLevel;
                if (l % SkillPointMilestoneInterval == 0) total += SkillPointMilestoneBonus;
            }
            return total;
        }

        /// <summary>Per-action chance that a Power Amplifier appears, from the living party's combined Luck (§1.2.6).</summary>
        public static float AmplifierChance(float partyLuck) =>
            Clamp(BattleConfig.AmplifierBaseChance + partyLuck * BattleConfig.AmplifierChancePerLuck, 0f, BattleConfig.AmplifierMaxChance);
    }
}
