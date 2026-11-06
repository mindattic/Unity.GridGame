using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using Unity.Shared.Core.Json;
using Unity.Shared.Core;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The 1..10 difficulty dial (GG-T309, §22.9): one table, monotonic, 10 = the designed game, rewards rising
    /// with the level, a default of 10 and the §22.9 presets migrating onto it.</summary>
    [TestFixture]
    public class DifficultyTests
    {
        [Test]
        public void The_table_is_valid_and_monotonic_with_ten_as_the_designed_game()
        {
            Assert.That(DifficultyTable.Problems(), Is.Empty);
            Assert.That(ContentLoader.Validate(Real).Where(p => p.StartsWith("difficulty")), Is.Empty);
            var rows = DifficultyTable.Rows;
            Assert.That(rows.Select(r => r.Level), Is.EqualTo(Enumerable.Range(1, 10)));
            for (int i = 1; i < rows.Length; i++)
            {
                Assert.That(rows[i].EnemyHp, Is.GreaterThanOrEqualTo(rows[i - 1].EnemyHp));
                Assert.That(rows[i].EnemyDamage, Is.GreaterThanOrEqualTo(rows[i - 1].EnemyDamage));
                Assert.That(rows[i].EnemyLevelOffset, Is.GreaterThanOrEqualTo(rows[i - 1].EnemyLevelOffset));
                Assert.That(rows[i].RewardGold, Is.GreaterThan(rows[i - 1].RewardGold));
                Assert.That(rows[i].RewardXp, Is.GreaterThan(rows[i - 1].RewardXp));
                Assert.That(rows[i].DropChance, Is.GreaterThan(rows[i - 1].DropChance));
            }
            var top = DifficultyTable.At(10);
            Assert.That((top.EnemyHp, top.EnemyDamage, top.EnemyLevelOffset), Is.EqualTo((1f, 1f, 0)));
            Assert.That(rows[0].EnemyHp, Is.LessThan(1f));
            Assert.That(rows[0].RewardGold, Is.LessThan(1f));
            // Neutral (0) is the raw rules: no scaling at all.
            var n = DifficultyTable.At(DifficultyTable.Neutral);
            Assert.That((n.EnemyHp, n.EnemyDamage, n.EnemyLevelOffset, n.RewardGold, n.RewardXp, n.DropChance), Is.EqualTo((1f, 1f, 0, 1f, 1f, 1f)));
        }

        [Test]
        public void Reward_multiplier_runs_from_0_6_to_1_5_and_is_named_in_the_description()
        {
            Assert.That(DifficultyTable.At(1).RewardGold, Is.EqualTo(0.6f).Within(1e-4));
            Assert.That(DifficultyTable.At(5).RewardGold, Is.EqualTo(1.0f).Within(1e-4));
            Assert.That(DifficultyTable.At(10).RewardGold, Is.EqualTo(1.5f).Within(1e-4));
            Assert.That(DifficultyTable.RewardLabel(10), Is.EqualTo("×1.5"));
            Assert.That(DifficultyTable.RewardLabel(1), Is.EqualTo("×0.6"));
            Assert.That(DifficultyTable.Describe(10), Does.Contain("Rewards ×1.5"));
            Assert.That(DifficultyTable.Describe(1), Does.Contain("55% HP").And.Contain("Rewards ×0.6"));
        }

        [Test]
        public void New_settings_and_profiles_start_at_ten()
        {
            Assert.That(new SettingsSave().Difficulty.Level, Is.EqualTo(10));
            Assert.That(GaidenSettings.Read(null).Difficulty.Level, Is.EqualTo(10));
            Assert.That(GaidenSettings.Read(Fresh()).Difficulty.Level, Is.EqualTo(10));
        }

        [TestCase("Easy", 1f, 1f, 5)]
        [TestCase("Normal", 1f, 1f, 10)]
        [TestCase("Hard", 1f, 1f, 10)]
        [TestCase("Custom", 1f, 1f, 10)]
        [TestCase("Custom", 1.5f, 2f, 10)]
        [TestCase("Custom", 0.5f, 0.5f, 1)]
        [TestCase("Custom", 0.75f, 0.75f, 4)]
        [TestCase(null, 1f, 1f, 10)]
        public void A_settings_file_with_the_old_preset_migrates_onto_the_dial(string preset, float hp, float dmg, int expected)
        {
            var ps = Fresh();
            var d = ps.Game.GetOrAddObject(GaidenSettings.Keys.Difficulty);
            if (preset != null) d.Set("preset", preset);
            d.Set("enemyHp", hp); d.Set("enemyDamage", dmg); d.Set("rewardGold", 1f);
            var s = GaidenSettings.Read(ps);
            Assert.That(s.Difficulty.Level, Is.EqualTo(expected));
            // Written back as a level; the scaffold's keys are gone.
            GaidenSettings.Write(ps, s);
            var back = ps.Game.GetObject(GaidenSettings.Keys.Difficulty);
            Assert.That(back.GetInt(GaidenSettings.DifficultyLevelKey), Is.EqualTo(expected));
            Assert.That(back.ContainsKey("preset") || back.ContainsKey("enemyHp") || back.ContainsKey("rewardGold"), Is.False);
            Assert.That(GaidenSettings.Read(ps).Difficulty.Level, Is.EqualTo(expected));
        }

        [Test]
        public void A_stored_level_round_trips_and_an_out_of_range_one_reads_as_ten()
        {
            var ps = Fresh();
            GaidenSettings.Write(ps, new SettingsSave { Difficulty = new DifficultySettings { Level = 3 } });
            Assert.That(GaidenSettings.Read(ps).Difficulty.Level, Is.EqualTo(3));
            ps.Game.GetObject(GaidenSettings.Keys.Difficulty).Set(GaidenSettings.DifficultyLevelKey, 42);
            Assert.That(GaidenSettings.Read(ps).Difficulty.Level, Is.EqualTo(10));
        }

        [Test]
        public void Enemy_hp_and_level_rise_monotonically_with_the_dial_and_ten_is_the_designed_fight()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Dial");
            var stage = c.CampaignStages()[6];
            (int hp, int levels) Foes(int difficulty)
            {
                var s = BattleEngine.Create(c, BattleFactory.Build(c, save, stage.Id, 11, difficulty), new System.Collections.Generic.List<BattleEvent>());
                var foes = s.Units.Where(u => u.Team == Team.Enemy).ToList();
                return (foes.Sum(u => u.MaxHp + u.PlateMax), foes.Sum(u => u.Level));
            }
            var neutral = Foes(DifficultyTable.Neutral);
            var prev = (hp: 0, levels: 0);
            for (int d = 1; d <= 10; d++)
            {
                var f = Foes(d);
                Assert.That(f.hp, Is.GreaterThanOrEqualTo(prev.hp), $"enemy HP fell at difficulty {d}");
                Assert.That(f.levels, Is.GreaterThanOrEqualTo(prev.levels), $"enemy levels fell at difficulty {d}");
                prev = f;
            }
            Assert.That(Foes(10), Is.EqualTo(neutral), "difficulty 10 fights exactly the designed (unscaled) enemies");
            Assert.That(Foes(1).hp, Is.LessThan(neutral.hp * 0.7));
        }

        [Test]
        public void Kill_rewards_scale_with_the_dial()
        {
            (int coins, int xp) Win(int difficulty)
            {
                var db = Mini();
                var setup = new BattleSetup { Stage = Stage(("goblin", 1, 0)), Seed = 7, UnlockedSlots = 5, Difficulty = difficulty };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 0) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(2, 1) });
                var s = BattleEngine.Create(db, setup, new System.Collections.Generic.List<BattleEvent>());
                for (int i = 0; i < 6 && !s.IsOver; i++)
                {
                    var cl = Hero(s, "cleric");
                    BattleEngine.Apply(s, new MoveCommand { HeroId = cl.Id, Path = cl.Pos == new Coord(2, 0) ? P((2, 1)) : P((2, 0)) });
                }
                Assert.That(s.Phase, Is.EqualTo(BattlePhase.Victory));
                return (s.Results.Coins, s.Results.Xp);
            }
            var raw = Win(DifficultyTable.Neutral);
            var ten = Win(10);
            var one = Win(1);
            Assert.That(ten.coins, Is.EqualTo(DifficultyTable.Scale(raw.coins, 1.5f, 1)));
            Assert.That(ten.xp, Is.EqualTo(DifficultyTable.Scale(raw.xp, 1.5f, 1)));
            Assert.That(one.coins, Is.LessThan(raw.coins));
            Assert.That(one.xp, Is.LessThan(raw.xp));
            Assert.That(ten.xp, Is.GreaterThan(raw.xp));
        }

        [Test]
        public void The_report_carries_the_dial_for_the_results_screen()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Dial");
            var s = BattleEngine.Create(c, BattleFactory.Build(c, save, c.CampaignStages()[0].Id, 3, 7), new System.Collections.Generic.List<BattleEvent>());
            s.Phase = BattlePhase.Defeat;
            Assert.That(BattleFactory.Commit(c, save, s).Difficulty, Is.EqualTo(7));
        }

        private static ProfileSettings Fresh() => ProfileSettings.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gg-t309-" + System.Guid.NewGuid().ToString("N"), "settings.json"));
    }
}
