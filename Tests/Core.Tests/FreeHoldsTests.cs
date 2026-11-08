using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Free Holds (GG-T346, BIBLE §22.3.4, STORY.md §5): the owner's seven walled hill-fort landscapes become
    /// the backgrounds of the postgame chapter "The Moors Beyond the Stills" (<c>greenvalley2</c>). Its two stages are
    /// re-skinned, not rebuilt: militia behind the palisades, the Captain's people, Redline, and holds whose Hollow never
    /// got their aether back.</summary>
    public class FreeHoldsTests
    {
        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        [Test]
        public void The_moors_beyond_the_stills_show_the_free_holds()
        {
            var db = Real;
            var t = db.Campaign.Themes.Single(x => x.Id == "greenvalley2");
            Assert.That(t.Name, Is.EqualTo("The Moors Beyond the Stills"));
            Assert.That(t.Backgrounds, Is.EqualTo("freeholds"));
            Assert.That(t.Stages, Is.EqualTo(new[] { "greenvalley-04", "greenvalley-05" }), "the existing stages, re-skinned");

            var set = Backdrops.Probe("freeholds", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((7, true)), "freeholds_01 .. freeholds_07");
            Assert.That(Backdrops.Resolve("greenvalley2", 9, s => Backdrops.Probe(s, OnDisk), x => Backdrops.SetOf(db, x)), Is.EqualTo("Backgrounds/freeholds_03"));
            Assert.That(Backdrops.Resolve("greenvalley", 0, s => Backdrops.Probe(s, OnDisk), x => Backdrops.SetOf(db, x)), Is.EqualTo("Backgrounds/tithemoors_01"), "the Tithe Moors keep theirs");

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("greenvalley2") || p.Contains("greenvalley-04") || p.Contains("greenvalley-05")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Its_stages_are_the_free_holds_militia_redline_and_the_hollow()
        {
            var db = Real;
            var levels = new[] { 28, 29 }; // GG-T357: right after the Bloom's 25-27 on the postgame curve (was 15)
            var ids = new[] { "greenvalley-04", "greenvalley-05" };
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("greenvalley2"), $"{id}: the battle shows the Free Holds' backdrop");
                Assert.That(st.Name, Does.StartWith("The Free Holds"), id);
                Assert.That(st.LevelOverride, Is.EqualTo(levels[i]), $"{id}: the postgame level curve");
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                var classes = st.Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
                Assert.That(classes.Count(c => c.StartsWith("soldier")), Is.GreaterThanOrEqualTo(4), $"{id}: hold militia");
                Assert.That(classes.Any(c => c.StartsWith("ganger")), $"{id}: Redline, selling the Bloom");
                Assert.That(st.Waves.SelectMany(w => w.Enemies).All(e => e.RuleBreak == RuleBreak.None), $"{id}: rule-breakers are main finales only");
            }
            Assert.That(db.Stages["greenvalley-04"].Waves.SelectMany(w => w.Enemies).Count(e => e.Class.StartsWith("undead")), Is.EqualTo(2), // GG-T357: two-foe waves at 28
                "a hold whose Hollow never got their aether back");
            Assert.That(db.Stages["greenvalley-05"].Waves.Last().Enemies.Select(e => e.Class), Does.Contain("captain"), "the Captain's people, and the Captain");

            var talk = db.DialogueOrder.Single(d => d.Id == "greenvalley2_party_talk");
            Assert.That((talk.Trigger.Kind, talk.Trigger.Id), Is.EqualTo((DialogueTriggerKind.ThemeStart, "greenvalley2")));
            Assert.That(db.ThemeOf("greenvalley-04").Id, Is.EqualTo("greenvalley2"));
        }
    }
}
