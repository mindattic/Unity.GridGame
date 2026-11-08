using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T357 (BIBLE §22.3.4): the postgame in story order on one rising level curve. The Bloom first, then the
    /// five older chapters (the Free Holds, the Underspire, the Drowned Arks, Deep Shardfall, Under the Anchors), then
    /// Morrow's Shadow, last and highest; one level a stage, continuing the main campaign's i + 1 curve.</summary>
    public class PostgameCurveTests
    {
        private static readonly string[] PostgameThemes = { "bloom", "greenvalley2", "cityruins2", "swamp2", "desert2", "cave2", "morrowsshadow" };

        [Test]
        public void The_postgame_runs_in_story_order_after_the_finale()
        {
            var ids = Real.Campaign.Themes.Select(t => t.Id).ToList();
            int start = ids.IndexOf("cathedral") + 1;
            Assert.That(ids.Skip(start), Is.EqualTo(PostgameThemes), "the Bloom, the five older chapters, Morrow's Shadow last");
        }

        [Test]
        public void The_level_curve_rises_one_a_stage_from_the_cathedral_to_morrows_shadow()
        {
            var db = Real;
            var save = Progression.NewGame(db, "T");
            var stages = db.CampaignStages();
            var levels = stages.Select(st => Progression.StageRecommendedLevel(db, save, st)).ToList();
            for (int i = 0; i < levels.Count; i++)
                Assert.That(levels[i], Is.EqualTo(i + 1), $"{stages[i].Id}: one level a stage, the postgame continuing the main curve");
            Assert.That(levels.Last(), Is.EqualTo(40), "Morrow's Shadow III tops the campaign");
            Assert.That(db.ArmorSetOrder.Max(s => s.Level), Is.EqualTo(levels.Last()), "the top armor set tracks the last level");
            Assert.That(stages.SkipWhile(st => st.LevelOverride == null).All(st => st.LevelOverride != null),
                "every postgame stage keeps its LevelOverride (what marks the postgame for the soaks and the quest rule)");
        }

        [Test]
        public void Postgame_waves_have_the_endgame_shape()
        {
            // Measured at 22-24 (GG-T350): three-foe waves took the Cathedral to casual 4/0/3 of 24, two-foe waves to 17-19.
            // The re-levelled older chapters follow the Cathedral's, the Bloom's and Morrow's Shadow's shape: two foes a wave,
            // three in a stage's finale wave at most.
            var db = Real;
            foreach (var theme in db.Campaign.Themes.Where(t => PostgameThemes.Contains(t.Id)))
                foreach (var id in theme.Stages)
                {
                    var waves = db.Stages[id].Waves;
                    for (int w = 0; w < waves.Count - 1; w++)
                        Assert.That(waves[w].Enemies.Count, Is.LessThanOrEqualTo(2), $"{id} wave {w + 1}");
                    Assert.That(waves.Last().Enemies.Count, Is.LessThanOrEqualTo(3), $"{id} finale wave");
                }
        }

        [Test]
        public void A_v8_save_keeps_its_furthest_clear_through_the_reorder()
        {
            var db = Real;
            var order = db.CampaignStages().Select(s => s.Id).ToList();

            // v8 order from 27: greenvalley-04/05, desert-04/05, swamp-04/05, cave-04/05, cityruins-04/05, morrowsshadow-01..03.
            string[] v8 = { "greenvalley-04", "greenvalley-05", "desert-04", "desert-05", "swamp-04", "swamp-05", "cave-04", "cave-05", "cityruins-04", "cityruins-05" };
            for (int i = 0; i < v8.Length; i++)
                Assert.That(order[SaveMigrations.PostgameReorderMap[i]], Is.EqualTo(v8[i]), "the map points each old index at the same stage");

            var bloom = new SaveData { Version = 8, HighestClearedStage = 26 }; // the Bloom III cleared
            SaveMigrations.Migrate(bloom);
            Assert.That(bloom.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(order[bloom.HighestClearedStage + 1], Is.EqualTo("greenvalley-04"), "next up is the Free Holds");

            var shardfall = new SaveData { Version = 8, HighestClearedStage = 30 }; // v8 index 30 = Deep Shardfall V (Free Holds, Shardfall cleared)
            SaveMigrations.Migrate(shardfall);
            Assert.That(order[shardfall.HighestClearedStage], Is.EqualTo("desert-05"), "the same furthest clear");
            Assert.That(order[shardfall.HighestClearedStage + 1], Is.EqualTo("cave-04"), "Under the Anchors next");
            Assert.That(Progression.IsStageUnlocked(shardfall, order.IndexOf("cityruins-04")), Is.True, "the Underspire opens behind it");
            Assert.That(Progression.IsStageUnlocked(shardfall, order.IndexOf("greenvalley-05")), Is.True, "nothing it cleared locks again");

            var underspire = new SaveData { Version = 8, HighestClearedStage = 36 }; // every older chapter cleared
            SaveMigrations.Migrate(underspire);
            Assert.That(order[underspire.HighestClearedStage + 1], Is.EqualTo("morrowsshadow-01"));

            var morrow = new SaveData { Version = 8, HighestClearedStage = 38 }; // Morrow's Shadow II: did not move
            SaveMigrations.Migrate(morrow);
            Assert.That(morrow.HighestClearedStage, Is.EqualTo(38));

            var fresh = new SaveData { Version = 8, HighestClearedStage = -1 };
            SaveMigrations.Migrate(fresh);
            Assert.That(fresh.HighestClearedStage, Is.EqualTo(-1));

            var older = new SaveData { Version = 7, HighestClearedStage = 27 }; // v7 27 = Deep Shardfall V, through v8 and v9
            SaveMigrations.Migrate(older);
            Assert.That(order[older.HighestClearedStage], Is.EqualTo("desert-05"));
        }
    }
}
