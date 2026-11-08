using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Morrow's Shadow (GG-T348, BIBLE §22.3.4, STORY.md §5): the final postgame chapter, from the owner's ten
    /// landscapes of a colossal moon over cracked plains with floating rocks. Where Morrow hangs lowest and its pull lifts
    /// stone: the Nearing made visible. Void Sentinels, the Ember (Infernalists, Hellspawn) and moon-changed Wild; the
    /// Demon Lord stands in as its boss until the owner makes a unique one.</summary>
    public class MorrowsShadowTests
    {
        private static readonly string[] Stages = { "morrowsshadow-01", "morrowsshadow-02", "morrowsshadow-03" };

        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        /// <summary>Void Sentinels, the Ember and the moon-changed Wild.</summary>
        private static bool ShadowFoe(string cls) =>
            cls == "voidsentinel" || cls == "demonlord" || cls.StartsWith("infernalist") || cls.StartsWith("hellspawn")
            || cls.StartsWith("wolf") || cls.StartsWith("werewolf") || cls == "yeti" || cls == "vulture" || cls == "flyingmonkey"
            || DarkMonsters.Contains(cls); // GG-T365

        private static readonly string[] DarkMonsters = { "lampsnuffer", "duskstalker", "gloommender" };

        [Test]
        public void Morrows_shadow_is_fought_in_the_dark_by_its_own_monsters()
        {
            var db = Real;
            var rule = db.TryRegionRule("morrows-dark");
            Assert.That(rule.Hooks.Single(h => h.On == RuleHook.BattleStart).Effects.Single().Flag, Is.EqualTo(RuleFlags.Darkness));
            Assert.That(db.TryTileState("brazier-light").Lights, Is.True);
            foreach (var id in Stages)
            {
                Assert.That(RegionRuleCatalog.ActiveFor(db, db.Stages[id]).Select(r => r.Id), Is.EqualTo(new[] { "morrows-dark" }), id);
                var classes = db.Stages[id].Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
                foreach (var m in DarkMonsters) Assert.That(classes, Does.Contain(m), $"{id} fields the {m}");
            }
            Assert.That(db.TryAbility(db.Classes["lampsnuffer"].ChargeAbility).RuleEffects.Single().Kind, Is.EqualTo(RuleEffectKind.ClearTiles));
            Assert.That(db.Classes["duskstalker"].OnDeathRule.Single().State, Is.EqualTo("brazier-light"));
            // GG-T377: Holy power lights the ground in a rule of its own (painting it never brings Morrow's dark elsewhere).
            Assert.That(db.TryTileState("holy-light").Lights, Is.True);
            Assert.That(db.TileStateRule["holy-light"], Is.EqualTo("holy-light"));
            foreach (var a in new[] { "aegisguard_judgment_of_warding", "flask_holy_burst" })
                Assert.That(db.TryAbility(a).RuleEffects.Single().State, Is.EqualTo("holy-light"), a);
            foreach (var m in DarkMonsters)
                Assert.That(db.Classes[m].Portrait, Is.EqualTo("mannequin"), $"{m}: placeholder art until its portrait lands");
        }

        [Test]
        public void In_the_dark_a_foe_is_hidden_out_of_every_heros_sight_unless_it_stands_in_light()
        {
            var db = Real;
            var (s, _) = Battle(db, db.Stages["morrowsshadow-01"], ("knight", 1, 7), ("cleric", 4, 7));
            Assert.That(s.Region.HasFlag(RuleFlags.Darkness), Is.True, "the battle opens dark");
            foreach (var foe in s.Units.Where(u => u.Alive && u.Team == Team.Enemy))
            {
                bool near = s.Units.Any(h => h.Alive && h.IsHero && foe.Tiles().Any(t => t.Manhattan(h.Pos) <= RegionBoard.DarkSight));
                bool lit = foe.Tiles().Any(t => db.TryTileState(s.Region.At(t)?.State)?.Lights == true);
                Assert.That(RegionBoard.Hidden(s, foe), Is.EqualTo(!near && !lit), foe.Def.Id);
            }
            Assert.That(s.Units.Where(u => u.IsHero).All(h => !RegionBoard.Hidden(s, h)), "heroes are never hidden");
        }

        [Test]
        public void Morrows_shadow_is_the_last_chapter_of_the_campaign_and_validates()
        {
            var db = Real;
            var themes = db.Campaign.Themes;
            Assert.That(themes.Last().Id, Is.EqualTo("morrowsshadow"), "the final postgame chapter");
            Assert.That(themes[themes.Count - 2].Id, Is.EqualTo("cave2"), "after Under the Anchors (GG-T357: the postgame in story order)");

            var t = themes.Last();
            Assert.That(t.Name, Is.EqualTo("Morrow's Shadow"));
            Assert.That(t.Backgrounds, Is.EqualTo("morrowsshadow"));
            Assert.That(t.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(t.MilestoneReward, Is.EqualTo("chest_gilded"));
            Assert.That(t.Stages, Is.EqualTo(Stages));

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("morrowsshadow")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var save = Progression.NewGame(db, "T");
            Assert.That(Stages.Select(id => Progression.StageRecommendedLevel(db, save, db.Stages[id])), Is.EqualTo(new[] { 38, 39, 40 }),
                "the top of the postgame curve (GG-T357: past Under the Anchors' 36-37)");
            var order = db.CampaignStages().Select(s => s.Id).ToList();
            Assert.That(order.Skip(order.Count - 3), Is.EqualTo(Stages), "appended at the end, so no save index shifts");

            var levels = db.ArmorSetOrder.ToDictionary(s => s.Id, s => s.Level);
            Assert.That(levels["dreadwyrm"], Is.EqualTo(order.Count), "the top set tracks the whole campaign");
            Assert.That((levels["templar"], levels["reaper"], levels["sanguine"]), Is.EqualTo((25, 30, 35)));
        }

        [Test]
        public void Its_stages_are_void_sentinels_the_ember_and_the_moon_changed_ending_on_the_demon_lord()
        {
            var db = Real;
            foreach (var id in Stages)
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("morrowsshadow"), id);
                Assert.That(st.Name, Does.StartWith("Morrow's Shadow"), id);
                Assert.That(st.LevelOverride, Is.Not.Null, $"{id} is postgame");
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                Assert.That(st.Blocks, Is.Not.Empty, $"{id}: fallen moon-rock as blocks");
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                {
                    Assert.That(ShadowFoe(e.Class), $"{id}: {e.Class} belongs under Morrow");
                    Assert.That(e.RuleBreak, Is.EqualTo(RuleBreak.None), $"{id}: rule-breakers are main finales only");
                }
            }
            var classes = Stages.SelectMany(id => db.Stages[id].Waves).SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
            Assert.That(classes, Does.Contain("voidsentinel"));
            Assert.That(classes.Any(c => c.StartsWith("infernalist")) && classes.Any(c => c.StartsWith("hellspawn")), "the Ember");
            Assert.That(classes.Any(c => c.StartsWith("wolf") || c == "yeti" || c == "werewolf00"), "the moon-changed Wild");

            var finale = db.Stages["morrowsshadow-03"].Waves.Last().Enemies.Select(e => e.Class).ToList();
            Assert.That(finale, Is.EqualTo(new[] { "demonlord", "voidsentinel", "infernalist03" }), "the Demon Lord, a Sentinel and a Bloodbinder");

            var talk = db.DialogueOrder.Single(d => d.Id == "morrowsshadow_party_talk");
            Assert.That((talk.Trigger.Kind, talk.Trigger.Id), Is.EqualTo((DialogueTriggerKind.ThemeStart, "morrowsshadow")));
        }

        [Test]
        public void Morrows_shadow_shows_its_ten_landscapes_and_its_own_board_tile()
        {
            var set = Backdrops.Probe("morrowsshadow", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((10, true)), "morrowsshadow_01 .. morrowsshadow_10");
            Assert.That(Backdrops.Resolve("morrowsshadow", 13, s => Backdrops.Probe(s, OnDisk), t => Backdrops.SetOf(Real, t)), Is.EqualTo("Backgrounds/morrowsshadow_04"));
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Tiles", "morrowsshadow.png")), "its board tile");
        }
    }
}
