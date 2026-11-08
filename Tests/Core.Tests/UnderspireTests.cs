using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Street markets (GG-T349, BIBLE §22.3.4, STORY.md §5): the owner's nine neon underspire night-market
    /// landscapes become the backgrounds of the postgame chapter "The Underspire" (<c>cityruins2</c>). Its two stages are
    /// re-cast as the Street's home turf: Redline, the Murder, the Herd, the Chromed Hollow and the Choir, everyone who wants
    /// the spire now the Hierarch is gone.</summary>
    public class UnderspireTests
    {
        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        /// <summary>The Street (Redline, the Murder, the Herd), the Concord's Chromed Hollow and the Choir.</summary>
        private static bool UnderspireFoe(string cls) =>
            cls.StartsWith("ganger") || cls.StartsWith("harpy") || cls.StartsWith("minotaur") || cls.StartsWith("cyberzombie") || cls.StartsWith("cultist");

        [Test]
        public void The_underspire_shows_the_street_markets()
        {
            var db = Real;
            var t = db.Campaign.Themes.Single(x => x.Id == "cityruins2");
            Assert.That(t.Name, Is.EqualTo("The Underspire"));
            Assert.That(t.Backgrounds, Is.EqualTo("underspire"));
            Assert.That(t.Stages, Is.EqualTo(new[] { "cityruins-04", "cityruins-05" }));

            var set = Backdrops.Probe("underspire", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((9, true)), "underspire_01 .. underspire_09");
            Assert.That(Backdrops.Resolve("cityruins2", 10, s => Backdrops.Probe(s, OnDisk), x => Backdrops.SetOf(db, x)), Is.EqualTo("Backgrounds/underspire_02"));
            Assert.That(Backdrops.Resolve("cityruins", 0, s => Backdrops.Probe(s, OnDisk), x => Backdrops.SetOf(db, x)), Is.EqualTo("Backgrounds/halcyon_01"), "Halcyon keeps its own");

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("cityruins2") || p.Contains("cityruins-04") || p.Contains("cityruins-05")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Its_stages_are_the_street_the_hollow_and_the_choir()
        {
            var db = Real;
            var all = new[] { "cityruins-04", "cityruins-05" }.SelectMany(id => db.Stages[id].Waves).SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
            foreach (var id in new[] { "cityruins-04", "cityruins-05" })
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("cityruins2"), $"{id}: the battle shows the underspire's backdrop");
                Assert.That(st.Name, Does.StartWith("The Underspire"), id);
                Assert.That(st.LevelOverride, Is.EqualTo(15), $"{id}: postgame level unchanged");
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                {
                    Assert.That(UnderspireFoe(e.Class), $"{id}: {e.Class} belongs in the underspire");
                    Assert.That(e.RuleBreak, Is.EqualTo(RuleBreak.None), $"{id}: rule-breakers are main finales only");
                }
            }
            foreach (var family in new[] { "ganger", "harpy", "minotaur", "cyberzombie", "cultist" })
                Assert.That(all.Any(c => c.StartsWith(family)), $"the {family} family fights here");
            Assert.That(all, Does.Not.Contain("vampire").And.Not.Contain("mannequin"), "no playable hero or training dummy fielded as a foe");
            Assert.That(db.Stages["cityruins-05"].Waves.Last().Enemies.Select(e => e.Class), Is.EquivalentTo(new[] { "minotaur01", "cultist01", "ganger06" }),
                "everyone who wants the spire: the Herd Boss, the Choir and Redline");

            var talk = db.DialogueOrder.Single(d => d.Id == "cityruins2_party_talk");
            Assert.That((talk.Trigger.Kind, talk.Trigger.Id), Is.EqualTo((DialogueTriggerKind.ThemeStart, "cityruins2")));
        }
    }
}
