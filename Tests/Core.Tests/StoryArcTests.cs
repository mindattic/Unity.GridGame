using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T231: the story start to finish — prologue and ending crawls, the level-gated interlude, boss
    /// introductions (StageStart) and the ending flag.</summary>
    public class StoryArcTests
    {
        private static ContentDb Real => TestKit.Real;

        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(TestKit.RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        private static readonly string[] Finales = { "greenvalley-03", "desert-03", "swamp-03", "cave-03", "still-03", "cityruins-03" };

        [Test]
        public void Shipped_story_content_validates()
        {
            var problems = ContentLoader.Validate(Real, PortraitOnDisk).Where(p => p.StartsWith("campaign") || p.StartsWith("dialogue")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void The_campaign_has_a_prologue_and_an_ending_after_the_vampire_lord()
        {
            Assert.That(Real.Campaign.Prologue?.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(Real.Campaign.Ending?.Crawl, Is.Not.Null.And.Not.Empty);
            Assert.That(Real.Campaign.Ending.Stage, Is.EqualTo("cityruins-03"));
            Assert.That(Real.Stage("cityruins-03").Waves.Last().Enemies.Any(e => e.Class == "vampire"), "the ending follows the Vampire Lord's stage");
        }

        [Test]
        public void Every_main_biome_finale_opens_with_a_boss_introduction()
        {
            foreach (var stage in Finales)
            {
                var intro = Real.DialogueOrder.FirstOrDefault(d => d.Trigger.Kind == DialogueTriggerKind.StageStart && d.Trigger.Id == stage);
                Assert.That(intro, Is.Not.Null, $"no boss intro for {stage}");
                var foes = Real.Stage(stage).Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToHashSet();
                Assert.That(intro.Speakers.Any(sp => sp.Class != null && foes.Contains(sp.Class)), $"{intro.Id}: the boss who speaks fights in {stage}");
            }
        }

        [Test]
        public void A_boss_introduction_plays_once_and_only_before_its_own_stage()
        {
            var s = Progression.NewGame(Real, "T");
            s.SeenDialogues.UnionWith(Real.DialogueOrder.Where(d => d.Trigger.Kind != DialogueTriggerKind.StageStart).Select(d => d.Id));
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-02"), Is.Null);
            var beat = DialogueRunner.PendingBeat(Real, s, "greenvalley-03");
            Assert.That(beat?.Id, Is.EqualTo("boss_intro_greenvalley"));
            DialogueRunner.PlayHeadless(Real, s, beat);
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-03"), Is.Null);
        }

        [Test]
        public void The_level_ten_interlude_waits_for_a_hero_to_reach_level_ten_then_plays_once()
        {
            var s = Progression.NewGame(Real, "T");
            Assert.That(DialogueRunner.PendingInterlude(Real, s), Is.Null);
            s.Roster[1].TotalXp = Formulas.TotalXpForLevel(10);
            var beat = DialogueRunner.PendingInterlude(Real, s);
            Assert.That(beat?.Id, Is.EqualTo("interlude_level10_motives"));
            Assert.That(beat.Crawl, Is.Not.Null.And.Not.Empty, "it opens with a crawl");
            DialogueRunner.PlayHeadless(Real, s, beat);
            Assert.That(DialogueRunner.PendingInterlude(Real, s), Is.Null);
            Assert.That(s.Flags.Any(f => f.StartsWith("motive:")), "the answer is remembered");
        }

        [Test]
        public void Bots_live_through_the_interlude_headlessly_and_test_mode_never_sees_it()
        {
            var s = Progression.NewGame(Real, "T");
            s.Roster[0].TotalXp = Formulas.TotalXpForLevel(10);
            DialogueRunner.PlayPendingBeatsHeadless(Real, s, "greenvalley-01");
            Assert.That(s.SeenDialogues, Does.Contain("interlude_level10_motives"));

            var t = Progression.NewGame(Real, "T");
            t.TestMode = true;
            t.Roster[0].TotalXp = Formulas.TotalXpForLevel(10);
            Assert.That(DialogueRunner.PendingInterlude(Real, t), Is.Null);
        }

        [Test]
        public void The_ending_is_due_once_after_the_final_victory_and_new_game_plus_restores_it()
        {
            var s = Progression.NewGame(Real, "T");
            Assert.That(Story.EndingDue(Real, s, false, "cityruins-03"), Is.False, "not on a defeat");
            Assert.That(Story.EndingDue(Real, s, true, "cave-03"), Is.False, "not on another stage");
            Assert.That(Story.EndingDue(Real, s, true, "cityruins-03"), Is.True);
            Story.MarkEndingSeen(s);
            Assert.That(Story.EndingDue(Real, s, true, "cityruins-03"), Is.False, "plays once");

            s.HighestClearedStage = Real.CampaignStages().Count - 1;
            Assert.That(Progression.StartNewGamePlus(Real, s).Ok);
            Assert.That(Story.EndingDue(Real, s, true, "cityruins-03"), Is.True, "New Game+ plays the ending again");

            var t = Progression.NewGame(Real, "T");
            t.TestMode = true;
            Assert.That(Story.EndingDue(Real, t, true, "cityruins-03"), Is.False, "Test Mode never sees story");
        }

        private static ContentDb Fixture(DialogueDef d)
        {
            var db = new ContentDb();
            db.Classes["knight"] = new ClassDef { Id = "knight", Name = "Knight", Team = Team.Hero, Role = "Tank", HomeTree = "t" };
            db.SkillTrees["t"] = new SkillTreeDef { Id = "t", Name = "T" };
            db.Stages["s1"] = new StageDef { Id = "s1", Name = "S1" };
            db.Campaign.Themes.Add(new ThemeDef { Id = "vale", Name = "Vale", Stages = { "s1" } });
            db.Dialogues[d.Id] = d;
            db.DialogueOrder.Add(d);
            return db;
        }

        private static DialogueDef Beat(DialogueTrigger trigger, string crawl = null, string title = "T") => new DialogueDef
        {
            Id = "d", Title = title, Crawl = crawl, Trigger = trigger,
            Speakers = { new DialogueSpeaker { Id = "k", Class = "knight" } },
            Nodes = { new DialogueNode { Id = "a", Speaker = "k", Text = "Hi" } },
        };

        private static string[] Problems(ContentDb db) => ContentLoader.Validate(db).Where(p => p.StartsWith("dialogue") || p.StartsWith("campaign")).ToArray();

        [Test]
        public void Validation_catches_bad_story_triggers_and_crawls()
        {
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.LevelReached, Level = 10 }, "Text"))), Is.Empty);
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.StageStart, Id = "s1" }))), Is.Empty);
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.LevelReached, Level = 1 }))), Has.Some.Contains("Level of 2"));
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.StageStart, Id = "nope" }))), Has.Some.Contains("unknown StageStart stage"));
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.Talk, Id = "party" }, "Text"))), Has.Some.Contains("only a story beat"));
            Assert.That(Problems(Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.StageStart, Id = "s1" }, "Text", null))), Has.Some.Contains("needs a Title"));
        }

        [Test]
        public void Validation_checks_the_prologue_and_ending()
        {
            var db = Fixture(Beat(new DialogueTrigger { Kind = DialogueTriggerKind.StageStart, Id = "s1" }));
            db.Campaign.Prologue = new CrawlDef { Title = "P", Crawl = "x", Backdrop = "vale" };
            db.Campaign.Ending = new CrawlDef { Title = "E", Crawl = "y", Stage = "s1" };
            Assert.That(Problems(db), Is.Empty);
            db.Campaign.Ending.Stage = "nope";
            db.Campaign.Prologue.Backdrop = "mars";
            db.Campaign.Prologue.Stage = "s1";
            var p = Problems(db);
            Assert.That(p, Has.Some.Contains("campaign Ending: needs a campaign Stage"));
            Assert.That(p, Has.Some.Contains("unknown backdrop theme"));
            Assert.That(p, Has.Some.Contains("only the Ending names a Stage"));
        }
    }
}
