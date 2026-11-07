using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T118: a new game starts with a small party and the rest join at story beats through dialogue
    /// (JoinHero), on top of rescued guests and the Summon Circle — and nobody is ever lost for good.</summary>
    public class StoryRecruitTests
    {
        private static ContentDb Real => TestKit.Real;

        private static readonly string[] RecruitBeats = { "greenvalley_recruit_mage", "desert_recruit_support", "cave_recruit_tank", "cityruins_recruit_healer" };

        private static DialogueEffect Join(string id, string role = null) => new DialogueEffect { Type = DialogueEffectType.JoinHero, Id = id, Role = role };

        [Test]
        public void A_new_game_starts_small()
        {
            Assert.That(Progression.StartingPartySize, Is.InRange(2, 3));
            Assert.That(Progression.StartingPartySize, Is.LessThan(Progression.MaxPartySize));
            Assert.That(Progression.StartingRoster.Length, Is.EqualTo(Progression.StartingPartySize));
            var s = Progression.NewGame(Real, "Small");
            Assert.That(s.Party.Count, Is.EqualTo(Progression.StartingPartySize));
            Assert.That(s.Roster.Count, Is.EqualTo(Progression.StartingPartySize));
        }

        [Test]
        public void JoinHero_brings_the_named_hero_then_falls_back_to_its_role_then_to_a_missing_role()
        {
            var c = Real; var s = Progression.NewGame(c, "J");
            Assert.That(DialogueRunner.ApplyEffect(c, s, Join("redmage", "Mage")), Does.Contain("Red Mage joins the party"));
            Assert.That(s.Party, Does.Contain("redmage"));

            // Already owned → another Mage nobody has yet, never a repeat.
            string next = Summon.StoryPick(c, s, "redmage", "Mage");
            Assert.That(next, Is.Not.Null.And.Not.EqualTo("redmage"));
            Assert.That(c.Class(next).Role, Is.EqualTo("Mage"));

            // No class and no role → a role the roster lacks (Support here: Tank, Striker, Healer, Mage are covered).
            string gap = Summon.StoryPick(c, s, null, null);
            Assert.That(c.Class(gap).Role, Is.EqualTo("Support"));
        }

        [Test]
        public void Fallback_recruits_leave_the_summon_pool_and_stage_guests_alone()
        {
            var c = Real; var s = Progression.NewGame(c, "Pool");
            var guests = c.Stages.Values.SelectMany(st => st.Waves).SelectMany(w => w.Guests).Select(g => g.Class).ToHashSet();
            for (int i = 0; i < 80; i++)
            {
                var pick = Summon.StoryPick(c, s, null, i % 2 == 0 ? "Striker" : null);
                if (pick == null) break;
                Assert.That(c.Summon.Pool, Does.Not.Contain(pick));
                Assert.That(guests, Does.Not.Contain(pick));
                Assert.That(s.Hero(pick), Is.Null);
                Summon.Join(c, s, pick);
            }
            // Once every story-eligible hero is owned, JoinHero is a harmless no-op.
            int before = s.Roster.Count;
            Assert.That(DialogueRunner.ApplyEffect(c, s, Join(null, "Striker")), Is.Null);
            Assert.That(s.Roster.Count, Is.EqualTo(before));
        }

        [Test]
        public void A_full_party_sends_the_recruit_to_the_reserve()
        {
            var c = Real; var s = Progression.NewGame(c, "Full", new[] { "paladin", "barbarian", "cleric", "redmage", "engineer", "knight" });
            Assert.That(DialogueRunner.ApplyEffect(c, s, Join("sage")), Does.Contain("reserve"));
            Assert.That(s.Hero("sage"), Is.Not.Null);
            Assert.That(s.Party.Count, Is.EqualTo(Progression.MaxPartySize));
            Assert.That(s.Party, Does.Not.Contain("sage"));
        }

        [Test]
        public void The_recruit_speaker_is_the_hero_who_joins()
        {
            var c = Real; var s = Progression.NewGame(c, "Face", new[] { "redmage", "paladin", "cleric" }); // the preferred Mage is taken
            var def = c.Dialogues["greenvalley_recruit_mage"];
            var r = new DialogueRunner(c, s, def);
            string recruit = r.Recruit;
            Assert.That(recruit, Is.Not.Null.And.Not.EqualTo("redmage"));
            Assert.That(r.Current.Name, Is.EqualTo(c.Class(recruit).Name));
            Assert.That(r.Current.Portrait, Is.EqualTo(recruit));
            while (!r.Finished) { if (!r.Advance()) r.Choose(0); }
            Assert.That(s.Party, Does.Contain(recruit));
            Assert.That(r.Log, Has.Some.Contains(c.Class(recruit).Name));
        }

        [Test]
        public void Every_path_through_every_recruit_beat_brings_exactly_one_new_hero()
        {
            var c = Real;
            foreach (var id in RecruitBeats)
            {
                var def = c.Dialogues[id];
                for (int pick = 0; pick < 4; pick++)
                {
                    var s = Progression.NewGame(c, "Path");
                    int before = s.Roster.Count;
                    var r = new DialogueRunner(c, s, def);
                    while (!r.Finished) { if (!r.Advance()) r.Choose(System.Math.Min(pick, r.Choices.Count - 1)); }
                    Assert.That(s.Roster.Count, Is.EqualTo(before + 1), $"{id} path {pick}");
                    Assert.That(s.SeenDialogues, Does.Contain(id));
                }
            }
        }

        [Test]
        public void Story_beats_grow_a_trio_to_a_full_party_by_the_third_theme()
        {
            var c = Real; var s = Progression.NewGame(c, "Grow");
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(s.Party.Count, Is.EqualTo(3), "nobody joins before the first battle");
            s.ClearedStages.Add("greenvalley-01"); s.HighestClearedStage = 0;
            var log = DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-02");
            Assert.That(s.Party.Count, Is.EqualTo(4), string.Join("; ", log));
            s.ClearedStages.Add("greenvalley-02"); s.ClearedStages.Add("greenvalley-03"); s.HighestClearedStage = 2;
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "desert-01");
            Assert.That(s.Party.Count, Is.EqualTo(5));
            Assert.That(DialogueRunner.PlayPendingBeatsHeadless(c, s, "desert-01"), Is.Empty, "a beat plays once");
            // GG-T291: the sixth slot fills at the start of the third theme (The Anchor Wastes).
            s.ClearedStages.Add("desert-01"); s.ClearedStages.Add("desert-02"); s.ClearedStages.Add("desert-03"); s.HighestClearedStage = 5;
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "cave-01");
            Assert.That(s.Party.Count, Is.EqualTo(Progression.MaxPartySize));
        }

        [Test]
        public void Story_recruits_arrive_at_the_rosters_experience()
        {
            var c = Real; var s = Progression.NewGame(c, "Xp");
            foreach (var h in s.Roster) h.TotalXp = 2000;
            DialogueRunner.ApplyEffect(c, s, Join("redmage"));
            Assert.That(s.Hero("redmage").TotalXp, Is.EqualTo(2000));
        }

        [Test]
        public void Test_mode_shows_no_story_but_its_recruits_still_join()
        {
            var c = Real; var s = TestLoop.NewSave(c);
            Assert.That(s.Party.Count, Is.EqualTo(Progression.StartingPartySize));
            s.ClearedStages.Add("greenvalley-01"); s.HighestClearedStage = 0;
            Assert.That(DialogueRunner.PendingBeat(c, s, "greenvalley-02"), Is.Null, "Test Mode never shows story");
            var cycle = TestCycle.Begin(c, s, "greenvalley-02", 1);
            Assert.That(s.Party.Count, Is.EqualTo(Progression.StartingPartySize + 1));
            Assert.That(cycle.ToString(), Does.Contain("story:").And.Contain("joins the party"));
        }

        [Test]
        public void JoinHero_validation()
        {
            DialogueDef D(DialogueEffect e, string speakerClass = "knight") => new DialogueDef
            {
                Id = "j",
                Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.ThemeStart, Id = "greenvalley" },
                Speakers = { new DialogueSpeaker { Id = "x", Class = speakerClass } },
                Nodes = { new DialogueNode { Id = "a", Speaker = "x", Text = "Hi", Effects = e == null ? new List<DialogueEffect>() : new List<DialogueEffect> { e } } },
            };
            List<string> Problems(DialogueDef d)
            {
                var db = new ContentDb();
                db.Classes["knight"] = new ClassDef { Id = "knight", Name = "Knight", Team = Team.Hero, Role = "Tank", HomeTree = "t" };
                db.Classes["goblin"] = new ClassDef { Id = "goblin", Name = "Goblin", Team = Team.Enemy };
                db.SkillTrees["t"] = new SkillTreeDef { Id = "t", Name = "T" };
                db.Campaign.Themes.Add(new ThemeDef { Id = "greenvalley", Name = "GV" });
                db.Dialogues[d.Id] = d; db.DialogueOrder.Add(d);
                return ContentLoader.Validate(db).Where(p => p.StartsWith("dialogue")).ToList();
            }
            Assert.That(Problems(D(Join("knight", "Tank"))), Is.Empty);
            Assert.That(Problems(D(Join(null, "Healer"), "$recruit")), Is.Empty);
            Assert.That(Problems(D(Join(null))), Has.Some.Contains("JoinHero needs a hero class Id, a Role"));
            Assert.That(Problems(D(Join("goblin"))), Has.Some.Contains("JoinHero needs a hero class"));
            Assert.That(Problems(D(Join("knight", "Wizard"))), Has.Some.Contains("unknown Role 'Wizard'"));
            Assert.That(Problems(D(null, "$recruit")), Has.Some.Contains("\"$recruit\" needs a JoinHero"));
        }
    }
}
