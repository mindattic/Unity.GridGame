using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Lantern Road (GG-T345, BIBLE §22.3.2, STORY.md §5): the lantern-lit pilgrim road out of the Tithe Moors
    /// toward the Shardfall, a campaign chapter built from four landscapes the owner made. Existing units only: oni
    /// road-raiders, moon-changed gorge beasts and the militia on the caravan's trail; an oni with Undying at the top.</summary>
    public class LanternRoadTests
    {
        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        /// <summary>Drop tables of the road's foes: the oni, the Wild of the gorges, and the militia/Redline chasing the caravan.</summary>
        private static readonly HashSet<string> RoadFoes = new HashSet<string> { "oni", "wolf", "werewolf00", "bat", "desertflyer", "steppinrazor", "soldier", "ganger" };

        [Test]
        public void The_lantern_road_sits_between_the_tithe_moors_and_the_shardfall_and_validates()
        {
            var db = Real;
            var ids = db.Campaign.Themes.Select(t => t.Id).ToList();
            Assert.That(ids.IndexOf("lanternroad"), Is.EqualTo(ids.IndexOf("greenvalley") + 1));
            Assert.That(ids.IndexOf("desert"), Is.EqualTo(ids.IndexOf("lanternroad") + 1));
            var road = db.Campaign.Themes.Single(t => t.Id == "lanternroad");
            Assert.That(road.Name, Is.EqualTo("The Lantern Road"));
            Assert.That(road.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(road.MilestoneReward, Is.Not.Null);
            Assert.That(road.Stages, Is.EqualTo(new[] { "lanternroad-01", "lanternroad-02", "lanternroad-03" }), "as many stages as the other main themes");

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("lanternroad") || p.Contains("masks_off")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var save = Progression.NewGame(db, "T");
            int Level(string id) => Progression.StageRecommendedLevel(db, save, db.Stages[id]);
            Assert.That(road.Stages.Select(Level), Is.EqualTo(new[] { 4, 5, 6 }), "right after the Tithe Moors (1-3)");
            Assert.That(new[] { "desert-01", "desert-03", "swamp-03", "cave-03", "still-03", "cityruins-03" }.Select(Level),
                Is.EqualTo(new[] { 7, 9, 12, 15, 18, 21 }), "every later chapter moves three levels");
            foreach (var id in road.Stages)
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("lanternroad"), id);
                Assert.That(st.LevelOverride, Is.Null, $"{id} is main campaign");
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                    Assert.That(RoadFoes, Does.Contain(db.Classes[e.Class].DropTable), $"{id}: {e.Class} belongs on the road");
            }
            var classes = road.Stages.SelectMany(id => db.Stages[id].Waves).SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
            Assert.That(classes, Does.Contain("oni00").And.Contain("oni01").And.Contain("oni02"), "the oni road-raiders");
            Assert.That(classes.Any(c => c.StartsWith("steppinrazor")), "the gorge's steppin razors");
        }

        [Test]
        public void The_mask_on_the_pass_gets_back_up_once()
        {
            var db = Real;
            var finale = db.Stages["lanternroad-03"].Waves.Last();
            var boss = finale.Enemies.Single(e => e.RuleBreak != RuleBreak.None);
            Assert.That(boss.Class, Is.EqualTo("oni02"), "an existing unit, reused as the stage boss");
            Assert.That(boss.RuleBreak, Is.EqualTo(RuleBreak.Undying));
            Assert.That(finale.Enemies.Count(e => e.RuleBreak == RuleBreak.None), Is.GreaterThan(0), "Undying needs raiders beside it to rise");
            var intro = db.DialogueOrder.Single(d => d.Id == "boss_intro_lanternroad");
            Assert.That(intro.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.StageStart));
            Assert.That(intro.Trigger.Id, Is.EqualTo("lanternroad-03"));
            var talk = db.DialogueOrder.Single(d => d.Id == "lanternroad_party_talk");
            Assert.That(talk.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.ThemeStart));
            Assert.That(talk.Trigger.Id, Is.EqualTo("lanternroad"));
            Assert.That(db.QuestOrder.Any(q => q.Theme == "lanternroad"), "a side quest like every main theme");
        }

        [Test]
        public void The_road_shows_its_four_landscapes_and_its_own_board_tile()
        {
            var set = Backdrops.Probe("lanternroad", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((4, true)), "lanternroad_01 .. lanternroad_04");
            Assert.That(Backdrops.SetOf(Real, "lanternroad"), Is.EqualTo("lanternroad"));
            Assert.That(Backdrops.Resolve("lanternroad", 5, s => Backdrops.Probe(s, OnDisk), t => Backdrops.SetOf(Real, t)), Is.EqualTo("Backgrounds/lanternroad_02"));
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Tiles", "lanternroad.png")), "the road's board tile");
        }

        [Test]
        public void A_save_from_before_the_lantern_road_keeps_its_frontier_and_armor_sets_follow()
        {
            var db = Real;
            var order = db.CampaignStages().Select(s => s.Id).ToList();
            Assert.That(order.IndexOf("lanternroad-01"), Is.EqualTo(SaveMigrations.LanternRoadInsertedAt));

            var past = new SaveData { Version = 5, HighestClearedStage = 4 }; // v5 index 4 = Shardfall II
            SaveMigrations.Migrate(past);
            Assert.That(past.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(order[past.HighestClearedStage], Is.EqualTo("desert-02"), "the same stage is still its furthest clear");
            Assert.That(Progression.IsStageUnlocked(past, order.IndexOf("lanternroad-03")), Is.True, "the road opens behind it");

            var before = new SaveData { Version = 5, HighestClearedStage = 2 }; // the Tithe Moors just cleared
            SaveMigrations.Migrate(before);
            Assert.That(order[before.HighestClearedStage + 1], Is.EqualTo("lanternroad-01"), "next up is the Lantern Road");

            var older = new SaveData { Version = 4, HighestClearedStage = 13 }; // v4 index 13 = Halcyon II, through both shifts
            SaveMigrations.Migrate(older);
            Assert.That(order[older.HighestClearedStage], Is.EqualTo("cityruins-02"));

            var levels = db.ArmorSetOrder.ToDictionary(s => s.Id, s => s.Level);
            Assert.That(levels["bramblehide"], Is.EqualTo(3), "sets below the road stay put");
            Assert.That(levels["gutterscrap"], Is.EqualTo(7), "sets from the Shardfall on move three levels");
            Assert.That(levels["dreadwyrm"], Is.EqualTo(order.Count), "the top set tracks the whole campaign");
        }
    }
}
