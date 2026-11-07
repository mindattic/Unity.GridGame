using System;
using System.Collections.Generic;
using System.Globalization;

namespace GridGame.Core.Battle
{
    /// <summary>One step of the 1..10 difficulty dial (§22.9, GG-T309). Every number is a plain multiplier on the
    /// designed game (level 10 = the enemy side exactly as authored and balanced).</summary>
    public sealed class DifficultyRow
    {
        public int Level;
        /// <summary>One word for the step ("Gentle" … "Intended").</summary>
        public string Name;
        /// <summary>Enemy MaxHP and armor plates at spawn.</summary>
        public float EnemyHp = 1f;
        /// <summary>Damage an enemy deals to a hero.</summary>
        public float EnemyDamage = 1f;
        /// <summary>Added to every non-test enemy spawn level (never below 1).</summary>
        public int EnemyLevelOffset;
        /// <summary>GG-T311: enemy turn-icon pace (the timeline). Slower foes leave wider gaps between their turns, so
        /// the party gets to act more often between them (§2.0 minimum action time).</summary>
        public float EnemySpeed = 1f;
        /// <summary>GG-T318 spacing: a foe re-entering the bar is held in recovery until it would act at least this many
        /// seconds from every other foe's turn… (0 on the neutral row: no spacing).</summary>
        public float MinGapSeconds;
        /// <summary>…but never held more than this much extra. When no time in that range fits, it takes the widest gap
        /// on offer, so clusters can still happen.</summary>
        public float MaxSpreadSeconds;
        /// <summary>± seeded jitter on each re-entry, so the rhythm never repeats mechanically.</summary>
        public float JitterSeconds;
        /// <summary>Coins every kill pays.</summary>
        public float RewardGold = 1f;
        /// <summary>XP every kill pays.</summary>
        public float RewardXp = 1f;
        /// <summary>Each drop-table entry's chance (capped at 100%).</summary>
        public float DropChance = 1f;
    }

    /// <summary>
    /// The difficulty dial (owner, GG-T309: "On a 1-10 scale you would start on 10 and let players adjust the game
    /// if they feel it is too easy or hard … the higher the difficulty the greater the rewards"). One table, read by
    /// the battle (enemy HP/damage/level at spawn, coins/XP/drop chance per kill) and by Settings (the one-line
    /// description). Level 10 is the designed difficulty every balance pass targets: the enemy columns are exactly
    /// 1.0 there, so lowering the dial only ever softens the game. Level 0 (<see cref="Neutral"/>) is the raw rules
    /// with no scaling at all — rules tests and hand-built setups use it.
    /// </summary>
    public static class DifficultyTable
    {
        public const int Min = 1, Max = 10, Default = 10;
        /// <summary>No scaling of any kind (a <see cref="BattleSetup"/> nobody set a difficulty on).</summary>
        public const int Neutral = 0;

        private static readonly DifficultyRow NeutralRow = new DifficultyRow { Level = Neutral, Name = "Neutral" };

        /// <summary>Monotonic in every column: enemies never get weaker and rewards never shrink as the level rises.</summary>
        public static readonly DifficultyRow[] Rows =
        {
            //                   hp     dmg   lvl  reward speed  minGap spread
            Row(1,  "Gentle",    0.55f, 0.60f, -2, 0.6f, 0.60f, 1.60f, 2.00f),
            Row(2,  "Gentle",    0.60f, 0.65f, -2, 0.7f, 0.65f, 1.55f, 1.90f),
            Row(3,  "Easy",      0.65f, 0.70f, -1, 0.8f, 0.70f, 1.50f, 1.80f),
            Row(4,  "Easy",      0.70f, 0.75f, -1, 0.9f, 0.75f, 1.45f, 1.70f),
            Row(5,  "Moderate",  0.75f, 0.80f, -1, 1.0f, 0.80f, 1.40f, 1.60f),
            Row(6,  "Moderate",  0.80f, 0.85f,  0, 1.1f, 0.85f, 1.30f, 1.45f),
            Row(7,  "Hard",      0.85f, 0.90f,  0, 1.2f, 0.90f, 1.25f, 1.35f),
            Row(8,  "Hard",      0.90f, 0.93f,  0, 1.3f, 0.93f, 1.15f, 1.20f),
            Row(9,  "Very hard", 0.95f, 0.97f,  0, 1.4f, 0.97f, 1.10f, 1.10f),
            Row(10, "Intended",  1.00f, 1.00f,  0, 1.5f, 1.00f, 1.00f, 1.00f),
        };

        /// <summary>GG-T318: the re-entry jitter on every non-neutral row.</summary>
        public const float RowJitterSeconds = 0.25f;

        private static DifficultyRow Row(int level, string name, float hp, float dmg, int lvl, float reward, float speed, float minGap, float spread) => new DifficultyRow
        {
            Level = level, Name = name, EnemyHp = hp, EnemyDamage = dmg, EnemyLevelOffset = lvl, EnemySpeed = speed,
            MinGapSeconds = minGap, MaxSpreadSeconds = spread, JitterSeconds = RowJitterSeconds,
            RewardGold = reward, RewardXp = reward, DropChance = reward,
        };

        /// <summary>A stored level clamped into 1..10; anything out of range (a hand-edited file) reads as the default.</summary>
        public static int Clamp(int level) => level < Min || level > Max ? Default : level;

        /// <summary>The row for <paramref name="level"/>; <see cref="Neutral"/> (or anything below 1) is the unscaled row.</summary>
        public static DifficultyRow At(int level) => level <= Neutral ? NeutralRow : Rows[Math.Min(Max, level) - 1];

        /// <summary>"×1.5": the headline reward multiplier (gold; XP and drops use the same curve today).</summary>
        public static string RewardLabel(int level) => "×" + At(level).RewardGold.ToString("0.0#", CultureInfo.InvariantCulture);

        /// <summary>Settings' one-line description, e.g. "Intended: the designed fight. Rewards ×1.5."</summary>
        public static string Describe(int level)
        {
            var r = At(Clamp(level));
            string foes = r.Level == Max ? "the designed fight"
                : $"foes {Pct(r.EnemyHp)} HP, {Pct(r.EnemyDamage)} damage, {Pct(r.EnemySpeed)} speed" + (r.EnemyLevelOffset != 0 ? $", {r.EnemyLevelOffset} Lv" : "");
            return $"{r.Name}: {foes}. Rewards {RewardLabel(r.Level)}.";
        }

        private static string Pct(float f) => ((int)Math.Round(f * 100f)).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>Everything wrong with <see cref="Rows"/> (empty = valid): one row per level 1..10 in order, positive
        /// multipliers, every enemy column non-decreasing and every reward column rising, level 10 enemies at exactly 1.0.</summary>
        public static List<string> Problems()
        {
            var p = new List<string>();
            if (Rows.Length != Max) p.Add($"difficulty: {Rows.Length} rows, expected {Max}");
            for (int i = 0; i < Rows.Length; i++)
            {
                var r = Rows[i];
                if (r.Level != i + 1) p.Add($"difficulty: row {i} is level {r.Level}, expected {i + 1}");
                if (r.EnemyHp <= 0f || r.EnemyDamage <= 0f || r.EnemySpeed <= 0f || r.RewardGold <= 0f || r.RewardXp <= 0f || r.DropChance <= 0f)
                    p.Add($"difficulty {r.Level}: multipliers must be positive");
                if (string.IsNullOrEmpty(r.Name)) p.Add($"difficulty {r.Level}: no name");
                if (r.MinGapSeconds < 0f || r.MaxSpreadSeconds < 0f || r.JitterSeconds < 0f)
                    p.Add($"difficulty {r.Level}: spacing columns must not be negative");
                if (i == 0) continue;
                var q = Rows[i - 1];
                if (r.EnemyHp < q.EnemyHp || r.EnemyDamage < q.EnemyDamage || r.EnemyLevelOffset < q.EnemyLevelOffset || r.EnemySpeed < q.EnemySpeed)
                    p.Add($"difficulty {r.Level}: an enemy column falls below level {q.Level}'s");
                // Less spacing is more pressure, so the spacing columns may only fall as the level rises.
                if (r.MinGapSeconds > q.MinGapSeconds || r.MaxSpreadSeconds > q.MaxSpreadSeconds)
                    p.Add($"difficulty {r.Level}: a spacing column rises above level {q.Level}'s");
                if (r.RewardGold <= q.RewardGold || r.RewardXp <= q.RewardXp || r.DropChance <= q.DropChance)
                    p.Add($"difficulty {r.Level}: a reward column doesn't rise above level {q.Level}'s");
            }
            if (Rows.Length == Max)
            {
                var top = Rows[Max - 1];
                if (top.EnemyHp != 1f || top.EnemyDamage != 1f || top.EnemyLevelOffset != 0 || top.EnemySpeed != 1f)
                    p.Add("difficulty 10: the enemy side must be exactly as designed (1.0, offset 0)");
            }
            return p;
        }

        /// <summary>Scales an amount by <paramref name="m"/>, rounding, never below <paramref name="floor"/> (unchanged at 1.0).</summary>
        public static int Scale(int amount, float m, int floor = 0) =>
            m == 1f ? amount : Math.Max(floor, (int)Math.Round(amount * m, MidpointRounding.AwayFromZero));
    }
}
