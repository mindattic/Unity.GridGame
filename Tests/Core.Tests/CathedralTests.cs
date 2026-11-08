using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Choir's Cathedral (GG-T350, BIBLE §22.3.3, STORY.md §5): the main campaign's finale at the top of
    /// Halcyon's spire, built from four landscapes the owner made. The organ is the Hierarch's reservoir and the Choir
    /// sings the tithe up into it; the Hierarch is its own enemy class now, no longer the playable Vampire.</summary>
    public class CathedralTests
    {
        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        /// <summary>The Choir (cultist01-03) and the Concord: the Bulwark, the Kiln Guard, the Red Seal and its Chromed Hollow.</summary>
        private static bool CathedralFoe(string cls) =>
            cls == "hierarch" || cls.StartsWith("cultist") || cls.StartsWith("heavytrooper") || cls.StartsWith("ceramicknight") || cls.StartsWith("templar") || cls.StartsWith("cyberzombie");

        [Test]
        public void The_cathedral_follows_halcyon_as_the_last_main_chapter_and_validates()
        {
            var db = Real;
            var ids = db.Campaign.Themes.Select(t => t.Id).ToList();
            Assert.That(ids.IndexOf("cathedral"), Is.EqualTo(ids.IndexOf("cityruins") + 1));
            Assert.That(ids.IndexOf("bloom"), Is.EqualTo(ids.IndexOf("cathedral") + 1), "the postgame starts after it (GG-T347: with The Bloom)");
            var cathedral = db.Campaign.Themes.Single(t => t.Id == "cathedral");
            Assert.That(cathedral.Name, Is.EqualTo("The Choir's Cathedral"));
            Assert.That(cathedral.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(cathedral.MilestoneReward, Is.Not.Null);
            Assert.That(cathedral.Stages, Is.EqualTo(new[] { "cathedral-01", "cathedral-02", "cathedral-03" }));

            var problems = ContentLoader.Validate(db, PortraitOnDisk).Where(p => p.Contains("cathedral") || p.Contains("hierarch") || p.Contains("organ")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            var save = Progression.NewGame(db, "T");
            int Level(string id) => Progression.StageRecommendedLevel(db, save, db.Stages[id]);
            Assert.That(cathedral.Stages.Select(Level), Is.EqualTo(new[] { 22, 23, 24 }), "right after Halcyon (19-21)");
            Assert.That(Level("cityruins-03"), Is.EqualTo(21), "nothing before it moves");
            foreach (var id in cathedral.Stages)
            {
                var st = db.Stages[id];
                Assert.That(st.Theme, Is.EqualTo("cathedral"), id);
                Assert.That(st.LevelOverride, Is.Null, $"{id} is main campaign");
                Assert.That(st.Waves, Has.Count.EqualTo(5), id);
                foreach (var e in st.Waves.SelectMany(w => w.Enemies))
                    Assert.That(CathedralFoe(e.Class), $"{id}: {e.Class} is the Choir or the Concord");
            }
            var classes = cathedral.Stages.SelectMany(id => db.Stages[id].Waves).SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
            Assert.That(classes, Does.Contain("cultist01").And.Contain("cultist02").And.Contain("cultist03"), "the whole Choir");
            Assert.That(classes.Any(c => c.StartsWith("heavytrooper")) && classes.Any(c => c.StartsWith("ceramicknight")) && classes.Any(c => c.StartsWith("templar")),
                "the Bulwark, the Kiln Guard and the Red Seal");
        }

        [Test]
        public void The_hierarch_is_its_own_enemy_class_on_the_mannequin_placeholder()
        {
            var db = Real;
            var h = db.Classes["hierarch"];
            Assert.That(h.Team, Is.EqualTo(Team.Enemy));
            Assert.That(h.HasTag("Boss"));
            Assert.That(db.Classes["vampire"].Team, Is.EqualTo(Team.Hero), "the Vampire stays a playable hero");
            Assert.That(db.Stages.Values.SelectMany(s => s.Waves).SelectMany(w => w.Enemies)
                .Where(e => e.RuleBreak != RuleBreak.None).Select(e => e.Class), Does.Not.Contain("vampire"), "the Vampire is no longer fielded as the boss");

            // No art yet (owner 2026-11-07, GG-T351): Art.Portrait falls back to the mannequin, so the class declares it
            // and frames the mannequin exactly as the mannequin does.
            Assert.That(PortraitOnDisk("hierarch"), Is.False);
            Assert.That(h.Portrait, Is.EqualTo("mannequin"));
            var m = db.Classes["mannequin"];
            Assert.That((h.Thumbnail.Zoom, h.Thumbnail.X, h.Thumbnail.Y), Is.EqualTo((m.Thumbnail.Zoom, m.Thumbnail.X, m.Thumbnail.Y)));
            Assert.That((h.Figure.X, h.Figure.Y, h.Figure.W, h.Figure.H), Is.EqualTo((m.Figure.X, m.Figure.Y, m.Figure.W, m.Figure.H)));

            // The kit: a drain charge, the organ's reservoir at half HP, lifesteal on every blow.
            var charge = db.Abilities[h.ChargeAbility];
            Assert.That(charge.Id, Is.EqualTo("charge_tithe_draught"));
            Assert.That((charge.Shape, charge.Filter, charge.BaseDamage > 0, charge.BaseHeal > 0), Is.EqualTo((TargetShape.AllEnemies, TargetFilter.EnemyOnly, true, true)), "a drain on the whole party");
            var phase = h.BossPhases.Single(p => p.OnEnterAbility != null);
            Assert.That(phase.HpThreshold, Is.EqualTo(0.5f));
            Assert.That(phase.PrefersCharge, Is.True);
            var pour = db.Abilities[phase.OnEnterAbility];
            Assert.That((pour.Shape, pour.BaseHeal > 0, pour.ApplyBuff, pour.HastenU > 0f), Is.EqualTo((TargetShape.Self, true, "empowered", true)));
            Assert.That(GridGame.Meta.Abilities.IsHeroUsable(pour), Is.False, "a free boss-phase spell is never learnable");
            Assert.That(GridGame.Meta.Abilities.IsHeroUsable(charge), Is.False);
            Assert.That(h.Abilities, Does.Contain("vampires_kiss"));
            Assert.That(db.Bounties["bounty_vampire_lord"].TargetClass, Is.EqualTo("hierarch"), "the Hierarch bounty hunts the Hierarch");
        }

        [Test]
        public void The_hierarch_rises_once_while_its_choir_still_sings()
        {
            var db = Real;
            var finale = db.Stages["cathedral-03"].Waves.Last();
            var boss = finale.Enemies.Single(e => e.RuleBreak != RuleBreak.None);
            Assert.That(boss.Class, Is.EqualTo("hierarch"));
            Assert.That(boss.RuleBreak, Is.EqualTo(RuleBreak.Undying));
            Assert.That(finale.Enemies.Where(e => e.RuleBreak == RuleBreak.None).Select(e => e.Class).All(c => c.StartsWith("cultist")),
                "the Choir sings beside it: silence them and it stays down");
            var intro = db.DialogueOrder.Single(d => d.Id == "boss_intro_cathedral");
            Assert.That((intro.Trigger.Kind, intro.Trigger.Id), Is.EqualTo((DialogueTriggerKind.StageStart, "cathedral-03")));
            Assert.That(intro.Speakers.Single(s => s.Id == "boss").Portrait, Is.EqualTo("mannequin"), "the Hierarch speaks with the placeholder art");
            var talk = db.DialogueOrder.Single(d => d.Id == "cathedral_party_talk");
            Assert.That((talk.Trigger.Kind, talk.Trigger.Id), Is.EqualTo((DialogueTriggerKind.ThemeStart, "cathedral")));
            Assert.That(db.QuestOrder.Any(q => q.Theme == "cathedral"), "a side quest like every main theme");
        }

        [Test]
        public void Halcyon_iii_ends_on_a_kiln_guard_captain_instead_of_the_hierarch()
        {
            var db = Real;
            var finale = db.Stages["cityruins-03"].Waves.Last();
            var boss = finale.Enemies.Single(e => e.RuleBreak != RuleBreak.None);
            Assert.That((boss.Class, boss.RuleBreak), Is.EqualTo(("ceramicknight06", RuleBreak.AxisWard)));
            Assert.That(db.Stages["cityruins-03"].Waves.SelectMany(w => w.Enemies).Select(e => e.Class), Does.Not.Contain("vampire").And.Not.Contain("hierarch"));
            var intro = db.DialogueOrder.Single(d => d.Trigger.Kind == DialogueTriggerKind.StageStart && d.Trigger.Id == "cityruins-03");
            Assert.That(intro.Speakers.Any(s => s.Class == "ceramicknight06"));
            Assert.That(db.Campaign.Ending.Stage, Is.EqualTo("cathedral-03"));
        }

        [Test]
        public void The_cathedral_shows_its_four_landscapes_and_its_own_board_tile()
        {
            var set = Backdrops.Probe("cathedral", OnDisk);
            Assert.That((set.Count, set.Padded), Is.EqualTo((4, true)), "cathedral_01 .. cathedral_04");
            Assert.That(Backdrops.SetOf(Real, "cathedral"), Is.EqualTo("cathedral"));
            Assert.That(Backdrops.Resolve("cathedral", 5, s => Backdrops.Probe(s, OnDisk), t => Backdrops.SetOf(Real, t)), Is.EqualTo("Backgrounds/cathedral_02"));
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Tiles", "cathedral.png")), "the cathedral's board tile");
        }

        [Test]
        public void A_save_from_before_the_cathedral_keeps_its_frontier_and_armor_sets_follow()
        {
            var db = Real;
            var order = db.CampaignStages().Select(s => s.Id).ToList();
            Assert.That(order.IndexOf("cathedral-01"), Is.EqualTo(SaveMigrations.CathedralInsertedAt));

            var postgame = new SaveData { Version = 6, HighestClearedStage = 22 }; // v6 index 22 = The Moors Beyond the Stills II
            SaveMigrations.Migrate(postgame);
            Assert.That(postgame.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(order[postgame.HighestClearedStage], Is.EqualTo("greenvalley-05"), "the same stage is still its furthest clear");
            Assert.That(Progression.IsStageUnlocked(postgame, order.IndexOf("cathedral-03")), Is.True, "the cathedral opens behind it");

            var halcyon = new SaveData { Version = 6, HighestClearedStage = 20 }; // Halcyon III, the old finale, cleared
            SaveMigrations.Migrate(halcyon);
            Assert.That(order[halcyon.HighestClearedStage], Is.EqualTo("cityruins-03"));
            Assert.That(order[halcyon.HighestClearedStage + 1], Is.EqualTo("cathedral-01"), "next up is the cathedral");

            var older = new SaveData { Version = 4, HighestClearedStage = 13 }; // v4 index 13 = Halcyon II, through every shift
            SaveMigrations.Migrate(older);
            Assert.That(order[older.HighestClearedStage], Is.EqualTo("cityruins-02"));

            var levels = db.ArmorSetOrder.ToDictionary(s => s.Id, s => s.Level);
            Assert.That(levels["wispweave"], Is.EqualTo(22), "sets below the cathedral stay put");
            Assert.That(levels["dreadwyrm"], Is.EqualTo(order.Count), "the top set tracks the whole campaign");
        }
    }
}
