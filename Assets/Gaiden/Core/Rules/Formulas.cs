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
        /// <summary>The blow would have landed but the defender's <see cref="Formulas.Evasion"/> slipped it (GG-T297);
        /// <see cref="Hit"/> is then <see cref="HitKind.Miss"/> and <see cref="Damage"/> 0.</summary>
        public bool Dodged;
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

        /// <summary>GG-T318: the median enemy SPD at <paramref name="level"/> across classes.json (11 at Lv1, ~17.75 at Lv10, ~33
        /// at Lv30). Speed is judged against it, so a fast foe stays fast relative to its peers at every level.</summary>
        public static float ReferenceEnemySpeed(int level) => 11f + 0.75f * (Math.Max(1, level) - 1);

        public static float HitPercent(Stats s) => Clamp(80f + s.Speed * 0.5f + s.Luck * 0.2f, 5f, 95f);
        public static float CritPercent(Stats s) => Clamp(8f + s.Luck * 0.4f, 0f, 50f);

        /// <summary>Evasion from AGI per point (GG-T297, §13.1.0): percentage points shaved off an attacker's hit chance.</summary>
        public const float EvasionPerAgility = 0.2f;
        /// <summary>Evasion from Luck per point (GG-T297).</summary>
        public const float EvasionPerLuck = 0.1f;
        /// <summary>Cap on the stat-derived part of Evasion (a high-level AGI build tops out here before buffs/gear).</summary>
        public const float EvasionStatCap = 25f;
        /// <summary>Hard cap on Evasion including buffs and gear (GG-T297).</summary>
        public const float EvasionCap = 40f;

        /// <summary>
        /// Defender Evasion (GG-T297, §13.1.0), in percentage points: <c>0.2·AGI + 0.1·Luck</c> (capped at 25) plus every
        /// <see cref="BuffDef.EvasionBonus"/> and gear <see cref="ItemDef.EvasionBonus"/> it carries, clamped 0..40.
        /// A level-1 hero sits near 3.6 (AGI 12, Luck 12), a ninja near 5, a level-30 skirmisher near 9–12.
        /// </summary>
        public static float Evasion(Unit defender) => defender == null ? 0f : Evasion(defender.Effective, defender.EvasionBonus);

        /// <summary>Evasion from bare stats plus <paramref name="bonus"/> points (the Bestiary reads a class's base stats).</summary>
        public static float Evasion(Stats d, float bonus = 0f)
        {
            float stat = Clamp(Math.Max(0f, d.Agility) * EvasionPerAgility + Math.Max(0f, d.Luck) * EvasionPerLuck, 0f, EvasionStatCap);
            return Clamp(stat + bonus, 0f, EvasionCap);
        }

        public static HitKind RollHit(Unit attacker, Unit defender, Rng rng) => RollHit(attacker, defender, rng, true, out _);

        /// <summary>
        /// One hit roll, one Rng draw (GG-T297 keeps the draw count so seeded replays only differ where Evasion bites).
        /// The attacker's own hit band is as before; a would-be hit whose roll falls in the top <see cref="Evasion"/>
        /// points of that band is <paramref name="dodged"/> instead (returned as a Miss). <paramref name="dodgeable"/>
        /// false (pincer and supporter blows, §13.1.0) skips Evasion entirely.
        /// </summary>
        public static HitKind RollHit(Unit attacker, Unit defender, Rng rng, bool dodgeable, out bool dodged)
        {
            dodged = false;
            var a = attacker.Effective;
            int adv = attacker.Level - defender.Level;
            float hit = Clamp(HitPercent(a) + adv * HitShiftPerLevel, 5f, 95f);
            float acc = attacker.AccuracyMultiplier;
            if (acc < 1f) hit = Clamp(hit * acc, 5f, 95f);
            float crit = CritPercent(a);
            if (attacker.SupportCritBonus > 0f) crit = Clamp(crit + attacker.SupportCritBonus * 100f, 0f, SupportDef.MaxSupportedCritPercent); // GG-T298
            float roll = rng.Range(0f, 100f);
            HitKind kind = roll <= crit ? HitKind.Critical : roll <= hit ? HitKind.Normal : HitKind.Miss;
            if (kind != HitKind.Miss && dodgeable && roll > hit - Evasion(defender))
            {
                dodged = true;
                return HitKind.Miss;
            }
            return kind;
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
        public static AttackRoll Physical(Unit attacker, Unit defender, Rng rng, float multiplier = 1f, bool dodgeable = true)
        {
            var a = attacker.Effective;
            var d = defender.Effective;
            float off = a.Strength + attacker.WeaponPower + UnarmedPower(attacker);
            float def = d.Vitality;
            float raw = Mitigate(off, def);
            raw *= defender.ResistanceMultiplier(DamageType.Physical);
            float varied = raw * Variance(a, rng, 0.10f);
            var hit = RollHit(attacker, defender, rng, dodgeable, out bool dodged);
            if (hit == HitKind.Miss) return new AttackRoll { Damage = 0, Hit = HitKind.Miss, Dodged = dodged };
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
            var hit = RollHit(caster, target, rng, true, out bool dodged);
            if (hit == HitKind.Miss) return new AttackRoll { Damage = 0, Hit = HitKind.Miss, Dodged = dodged };
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
