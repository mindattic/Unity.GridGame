using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Bloom (GG-T347, BIBLE §22.3.4, STORY.md §5): a new postgame chapter from the owner's eight
    /// red-blossom-over-white-ruins landscapes, the first chapter after the ending. Where the Hierarch's stolen aether came
    /// down, the Bloom grows over broken Concord stone; Redline harvest it, the last Red Seal wardens and the Bulwark burn
    /// it, and moss golems and moon-changed Wild guard it.</summary>
    public class BloomTests
    {
        private static readonly string[] Stages = { "bloom-01", "bloom-02", "bloom-03" };

        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        /// <summary>Redline harvesters, Pale Concord holdouts (the Red Seal, the Bulwark), moss golems and moon-changed Wild.</summary>
        private static bool BloomFoe(string cls) =>
            cls.StartsWith("ganger") || cls.StartsWith("templar") || cls.StartsWith("heavytrooper") || cls.StartsWith("treegolem")
            || cls.StartsWith("wolf") || cls.StartsWith("werewolf");

        [Test]
        public void The_bloom_opens_the_postgame_right_after_the_ending_and_validates()
        {
            var db = Real;
            var ids = db.Campaign.Themes.Select(t => t.Id).ToList();
            Assert.That(ids.IndexOf("bloom"), Is.EqualTo(ids.IndexOf("cathedral") + 1), "the first chapter after the finale");
            Assert.That(ids.IndexOf("greenvalley2"), Is.EqualTo(ids.IndexOf("bloom") + 1), "the old postgame follows it");
            Assert.That(db.Campaign.Ending.Stage, Is.EqualTo("cathedral-03"), "the ending still follows the Hierarch");

            var bloom = db.Campaign.Themes.Single(t => t.Id == "bloom");
            Assert.That(bloom.Name, Is.EqualTo("The Bloom"));
            Assert.That(bloom.Backgrounds, Is.EqualTo("bloom"));
            Assert.That(bloom.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(bloom.MilestoneReward, Is.EqualTo("chest_gilded"), "a postgame chapter's milestone");
            Assert.That(bloom.Stages, Is.EqualTo(Stages));

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("bloom")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var save = Progression.NewGame(db, "T");
            Assert.That(Stages.Select(id => Progression.StageRecommendedLevel(db, save, db.Stages[id])), Is.EqualTo(new[] { 25, 26, 27 }),
                "past the Cathedral's 22-24");
            Assert.That(Stages.All(id => db.Stages[id].LevelOverride != null), "postgame: a LevelOverride keeps it out of the main campaign");
            var order = db.CampaignStages().Select(s => s.Id).ToList();
            Assert.That(order.IndexOf("bloom-01"), Is.EqualTo(SaveMigrations.BloomInsertedAt));
        }

        [Test]
        public void Its_stages_are_harvesters_burners_and_growths_ending_on_the_red_seals_last_warden()
        {
            var db = Real;
            foreach (var id in Stages)
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("bloom"), id);
                Assert.That(st.Name, Does.StartWith("The Bloom"), id);
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                Assert.That(st.Blocks, Is.Not.Empty, $"{id}: broken white stone as blocks");
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                {
                    Assert.That(BloomFoe(e.Class), $"{id}: {e.Class} belongs in the Bloom");
                    Assert.That(e.RuleBreak, Is.EqualTo(RuleBreak.None), $"{id}: rule-breakers are main finales only");
                }
            }
            var classes = Stages.SelectMany(id => db.Stages[id].Waves).SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
            foreach (var family in new[] { "ganger", "templar", "heavytrooper", "treegolem" })
                Assert.That(classes.Any(c => c.StartsWith(family)), $"the {family} family fights here");

            var finale = db.Stages["bloom-03"].Waves.Last().Enemies.Select(e => e.Class).ToList();
            Assert.That(finale[0], Is.EqualTo("templar05"), "the boss: the Red Seal's last warden, come to burn the Bloom");
            Assert.That(finale.Skip(1).All(c => c.StartsWith("heavytrooper")), "with the Bulwark beside him");

            var talk = db.DialogueOrder.Single(d => d.Id == "bloom_party_talk");
            Assert.That((talk.Trigger.Kind, talk.Trigger.Id), Is.EqualTo((DialogueTriggerKind.ThemeStart, "bloom")));
        }

        [Test]
        public void The_bloom_shows_its_eight_landscapes_and_its_own_board_tile()
        {
            var set = Backdrops.Probe("bloom", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((8, true)), "bloom_01 .. bloom_08");
            Assert.That(Backdrops.Resolve("bloom", 9, s => Backdrops.Probe(s, OnDisk), t => Backdrops.SetOf(Real, t)), Is.EqualTo("Backgrounds/bloom_02"));
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Tiles", "bloom.png")), "the Bloom's board tile");
        }

        [Test]
        public void A_save_from_before_the_bloom_keeps_its_frontier_and_armor_sets_follow()
        {
            var db = Real;
            var order = db.CampaignStages().Select(s => s.Id).ToList();

            var postgame = new SaveData { Version = 7, HighestClearedStage = 25 }; // v7 index 25 = The Moors Beyond the Stills II
            SaveMigrations.Migrate(postgame);
            Assert.That(postgame.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(order[postgame.HighestClearedStage], Is.EqualTo("greenvalley-05"), "the same stage is still its furthest clear");
            Assert.That(Progression.IsStageUnlocked(postgame, order.IndexOf("bloom-03")), Is.True, "the Bloom opens behind it");

            var finale = new SaveData { Version = 7, HighestClearedStage = 23 }; // the Cathedral III, the finale, cleared
            SaveMigrations.Migrate(finale);
            Assert.That(order[finale.HighestClearedStage], Is.EqualTo("cathedral-03"));
            Assert.That(order[finale.HighestClearedStage + 1], Is.EqualTo("bloom-01"), "next up is the Bloom");

            var older = new SaveData { Version = 6, HighestClearedStage = 22 }; // v6 index 22 = The Moors Beyond the Stills II, through both shifts
            SaveMigrations.Migrate(older);
            Assert.That(order[older.HighestClearedStage], Is.EqualTo("greenvalley-05"));

            var levels = db.ArmorSetOrder.ToDictionary(s => s.Id, s => s.Level);
            Assert.That(levels["templar"], Is.EqualTo(25), "sets below stay put");
            Assert.That(levels["dreadwyrm"], Is.EqualTo(order.Count), "the top set tracks the whole campaign");
        }
    }
}
