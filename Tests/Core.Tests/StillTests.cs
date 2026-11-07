using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Still (GG-T344, BIBLE §22.3, STORY.md §5): the great tithe-dome at the foot of Halcyon's spire, a
    /// campaign chapter between the Anchor Wastes and Halcyon built from six landscapes the owner made — and the backdrop
    /// lookup that lets a theme show more than four of them.</summary>
    public class StillTests
    {
        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        private static readonly HashSet<string> Concord = new HashSet<string> { "bulwark", "choir", "templar", "undead", "cyberzombie", "cyclops", "ceramicknight", "hag" };

        [Test]
        public void The_still_sits_between_the_anchor_wastes_and_halcyon_and_validates()
        {
            var db = Real;
            var ids = db.Campaign.Themes.Select(t => t.Id).ToList();
            Assert.That(ids.IndexOf("still"), Is.EqualTo(ids.IndexOf("cave") + 1));
            Assert.That(ids.IndexOf("cityruins"), Is.EqualTo(ids.IndexOf("still") + 1));
            var still = db.Campaign.Themes.Single(t => t.Id == "still");
            Assert.That(still.Name, Is.EqualTo("The Still"));
            Assert.That(still.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(still.Stages, Is.EqualTo(new[] { "still-01", "still-02", "still-03" }), "as many stages as the other main themes");

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("still") || p.Contains("hymn_stops")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var save = Progression.NewGame(db, "T");
            var levels = still.Stages.Select(id => Progression.StageRecommendedLevel(db, save, db.Stages[id])).ToList();
            Assert.That(levels, Is.EqualTo(new[] { 13, 14, 15 }), "between the Anchor Wastes (10-12) and Halcyon");
            Assert.That(Progression.StageRecommendedLevel(db, save, db.Stages["cityruins-03"]), Is.EqualTo(18));
            foreach (var id in still.Stages)
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("still"), id);
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                    Assert.That(Concord, Does.Contain(db.Classes[e.Class].DropTable), $"{id}: {e.Class} is the Concord's (or its Hollow)");
            }
        }

        [Test]
        public void The_keeper_of_the_clocks_breaks_the_turn_order_and_tells_the_crew_what_the_tanks_hold()
        {
            var db = Real;
            var finale = db.Stages["still-03"].Waves.Last();
            var boss = finale.Enemies.Single(e => e.RuleBreak != RuleBreak.None);
            Assert.That(boss.Class, Is.EqualTo("chronomancer"), "an existing Concord unit, reused as the stage boss");
            Assert.That(boss.RuleBreak, Is.EqualTo(RuleBreak.TwinTurn));
            var intro = db.DialogueOrder.Single(d => d.Id == "boss_intro_still");
            Assert.That(intro.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.StageStart));
            Assert.That(intro.Trigger.Id, Is.EqualTo("still-03"));
            Assert.That(intro.Nodes.Any(n => n.Text.Contains("Stored")), "the beat: the Hollow are stored in the tanks");
            var talk = db.DialogueOrder.Single(d => d.Id == "still_party_talk");
            Assert.That(talk.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.ThemeStart));
            Assert.That(talk.Trigger.Id, Is.EqualTo("still"));
            Assert.That(db.QuestOrder.Any(q => q.Theme == "still"), "the Still has a side quest like every main theme");
        }

        [Test]
        public void A_theme_shows_as_many_backgrounds_as_its_set_has_numbered_from_01()
        {
            BackdropSet Probe(string set) => Backdrops.Probe(set, OnDisk);
            string SetOf(string theme) => Backdrops.SetOf(Real, theme);
            string Resolve(string theme, int i) => Backdrops.Resolve(theme, i, Probe, SetOf);

            var still = Probe("still");
            Assert.That(still.Count, Is.EqualTo(6), "the six landscapes");
            Assert.That(still.Padded, Is.True, "owner rule: still_01 .. still_06");
            Assert.That(Real.Campaign.Themes.Single(t => t.Id == "still").Backgrounds, Is.EqualTo("still"), "the campaign names its set");
            Assert.That(Resolve("still", 0), Is.EqualTo("Backgrounds/still_01"));
            Assert.That(Resolve("still", 4), Is.EqualTo("Backgrounds/still_05"), "no longer wrapped at four");
            Assert.That(Resolve("still", 6), Is.EqualTo("Backgrounds/still_01"));
            Assert.That(Resolve("still", -1), Is.EqualTo("Backgrounds/still_06"));

            // GG-T353 renamed the first five themes' sets after their regions (cave -> anchorwastes_01..04).
            Assert.That(Resolve("cave", 5), Is.EqualTo("Backgrounds/anchorwastes_02"), "a four-image set wraps at four");
            Assert.That(Resolve("cave2", 1), Is.EqualTo("Backgrounds/anchorwastes_02"), "a postgame theme borrows its base theme's art");
            Assert.That(Resolve("nowhere", 2), Is.EqualTo("Backgrounds/tithemoors_03"), "an unknown theme falls to the default theme's set");
            Assert.That(Resolve(null, 0), Is.EqualTo("Backgrounds/tithemoors_01"));
            Assert.That(OnDisk(Backdrops.Fallback), "the last-resort backdrop exists");
            Assert.That(Backdrops.Resolve("x", 0, _ => default(BackdropSet)), Is.EqualTo(Backdrops.Fallback), "no art at all");

            // A legacy unpadded set (<set>_0 ..) still resolves, though no file uses that numbering any more.
            var legacy = new HashSet<string> { "Backgrounds/old_0", "Backgrounds/old_1" };
            Assert.That(Backdrops.Resolve("old", 3, set => Backdrops.Probe(set, legacy.Contains)), Is.EqualTo("Backgrounds/old_1"));

            // A theme's set name maps it onto differently named files.
            var renamed = new HashSet<string> { "Backgrounds/tithemoors_01", "Backgrounds/tithemoors_02" };
            Assert.That(Backdrops.Resolve("greenvalley", 3, set => Backdrops.Probe(set, renamed.Contains), t => t == "greenvalley" ? "tithemoors" : t),
                Is.EqualTo("Backgrounds/tithemoors_02"));
            Assert.That(Backdrops.Key("still", 9, true), Is.EqualTo("Backgrounds/still_10"));
            Assert.That(Backdrops.BaseTheme("greenvalley2"), Is.EqualTo("greenvalley"));

            foreach (var theme in Real.Campaign.Themes.Select(t => t.Id).Concat(Real.CampaignStages().Select(s => s.Theme)).Distinct())
                Assert.That(Probe(SetOf(theme)).Count > 0 || Probe(SetOf(Backdrops.BaseTheme(theme))).Count > 0, $"{theme} has art of its own or its base theme's");
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Tiles", "still.png")), "the Still's board tile");
        }

        [Test]
        public void A_save_from_before_the_still_keeps_its_frontier()
        {
            var db = Real;
            var order = db.CampaignStages().Select(s => s.Id).ToList();
            Assert.That(order.IndexOf("still-01"), Is.EqualTo(SaveMigrations.StillInsertedAt));

            var past = new SaveData { Version = 4, HighestClearedStage = 13 }; // old index 13 = Halcyon II
            SaveMigrations.Migrate(past);
            Assert.That(past.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(order[past.HighestClearedStage], Is.EqualTo("cityruins-02"), "the same stage is still its furthest clear");
            Assert.That(Progression.IsStageUnlocked(past, order.IndexOf("still-03")), Is.True, "the Still opens behind it");

            var before = new SaveData { Version = 4, HighestClearedStage = 11 }; // the Anchor Wastes just cleared
            SaveMigrations.Migrate(before);
            Assert.That(order[before.HighestClearedStage + 1], Is.EqualTo("still-01"), "next up is the Still");
        }
    }
}
