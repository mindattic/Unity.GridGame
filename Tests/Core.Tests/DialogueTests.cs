using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>The dialogue engine (§27.1, GG-T117): dialogues.json validation, DialogueRunner's walk/choices/
    /// effects in the save vocabulary, and the story-beat / Camp Talk triggers.</summary>
    public class DialogueTests
    {
        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(TestKit.RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        private static ContentDb Real => TestKit.Real;

        private static SaveData NewSave(int gold = 200) { var s = Progression.NewGame(Real, "T"); s.Gold = gold; return s; }

        // ── content ────────────────────────────────────────────────────────────

        [Test]
        public void Shipped_dialogues_validate_with_their_portraits()
        {
            Assert.That(Real.DialogueOrder, Is.Not.Empty, "dialogues.json ships a sample");
            var problems = ContentLoader.Validate(Real, PortraitOnDisk).Where(p => p.StartsWith("dialogue")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Shipped_sample_exercises_branching_conditions_and_effects()
        {
            var all = Real.DialogueOrder;
            Assert.That(all.Any(d => d.Trigger.Kind == DialogueTriggerKind.ThemeStart), "a theme-start beat");
            Assert.That(all.Any(d => d.Trigger.Kind == DialogueTriggerKind.StageCleared), "a between-battles beat");
            Assert.That(all.Any(d => d.Trigger.Kind == DialogueTriggerKind.Talk), "a Camp Talk dialogue");
            var choices = all.SelectMany(d => d.Nodes).SelectMany(n => n.Choices).ToList();
            Assert.That(choices.Any(c => c.Conditions.Count > 0), "a conditional choice");
            var effects = choices.SelectMany(c => c.Effects).Concat(all.SelectMany(d => d.Nodes).SelectMany(n => n.Effects)).Select(e => e.Type).ToHashSet();
            Assert.That(effects, Is.SupersetOf(new[] { DialogueEffectType.SetFlag, DialogueEffectType.JoinNpc, DialogueEffectType.RecruitHero, DialogueEffectType.GrantGold }));
        }

        private static ContentDb Fixture(DialogueDef d)
        {
            var db = new ContentDb();
            db.Items["potion"] = new ItemDef { Id = "potion", Name = "Potion", Type = ItemType.Consumable };
            db.Classes["knight"] = new ClassDef { Id = "knight", Name = "Knight", Team = Team.Hero, Role = "Tank", HomeTree = "t" };
            db.Classes["goblin"] = new ClassDef { Id = "goblin", Name = "Goblin", Team = Team.Enemy };
            db.SkillTrees["t"] = new SkillTreeDef { Id = "t", Name = "T" };
            db.Campaign.Themes.Add(new ThemeDef { Id = "vale", Name = "Vale" });
            db.Dialogues[d.Id] = d;
            db.DialogueOrder.Add(d);
            return db;
        }

        private static DialogueDef Valid() => new DialogueDef
        {
            Id = "d",
            Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.ThemeStart, Id = "vale" },
            Speakers = { new DialogueSpeaker { Id = "k", Class = "knight" }, new DialogueSpeaker { Id = "npc", Name = "Smith", Portrait = "smith" } },
            Nodes =
            {
                new DialogueNode { Id = "a", Speaker = "k", Text = "Hi", Next = "b" },
                new DialogueNode
                {
                    Id = "b", Speaker = "npc", Text = "Choose",
                    Choices =
                    {
                        new DialogueChoice { Label = "Pay", Next = "c", Conditions = { new DialogueCondition { Type = DialogueConditionType.GoldAtLeast, Amount = 5 } } },
                        new DialogueChoice { Label = "Leave" },
                    },
                },
                new DialogueNode { Id = "c", Speaker = "npc", Text = "Thanks" },
            },
        };

        private static List<string> Problems(DialogueDef d, System.Func<string, bool> art = null) => ContentLoader.Validate(Fixture(d), art);

        [Test]
        public void Valid_fixture_has_no_problems() => Assert.That(Problems(Valid(), _ => true), Is.Empty);

        [Test]
        public void Dangling_node_ids_fail()
        {
            var d = Valid(); d.Nodes[0].Next = "nowhere";
            Assert.That(Problems(d), Has.Some.Contains("unknown next node 'nowhere'"));
            d = Valid(); d.Nodes[1].Choices[0].Next = "zzz";
            Assert.That(Problems(d), Has.Some.Contains("unknown next node 'zzz'"));
            d = Valid(); d.Start = "zz";
            Assert.That(Problems(d), Has.Some.Contains("unknown start node"));
        }

        [Test]
        public void Unreachable_nodes_fail()
        {
            var d = Valid(); d.Nodes.Add(new DialogueNode { Id = "orphan", Speaker = "k", Text = "..." });
            Assert.That(Problems(d), Has.Some.Contains("node 'orphan' is unreachable"));
        }

        [Test]
        public void Missing_speakers_and_portraits_fail()
        {
            var d = Valid(); d.Nodes[0].Speaker = "ghost";
            Assert.That(Problems(d), Has.Some.Contains("unknown speaker 'ghost'"));
            d = Valid(); d.Speakers[0].Class = "nobody";
            Assert.That(Problems(d), Has.Some.Contains("unknown class 'nobody'"));
            Assert.That(Problems(Valid(), id => id != "smith"), Has.Some.Contains("missing portrait 'smith'"));
            d = Valid(); d.Speakers[1].Name = null;
            Assert.That(Problems(d), Has.Some.Contains("needs a Class, or a Name and a Portrait"));
            d = Valid(); d.Speakers[0].Class = "$with";
            Assert.That(Problems(d), Has.Some.Contains("\"$with\" only fits a Talk"));
        }

        [Test]
        public void A_choice_list_needs_an_unconditional_way_out()
        {
            var d = Valid(); d.Nodes[1].Choices[1].Conditions.Add(new DialogueCondition { Type = DialogueConditionType.FlagSet, Id = "x" });
            Assert.That(Problems(d), Has.Some.Contains("every choice is conditional"));
        }

        [Test]
        public void Bad_conditions_effects_and_triggers_fail()
        {
            var d = Valid(); d.Nodes[1].Choices[1].Effects.Add(new DialogueEffect { Type = DialogueEffectType.RecruitHero, Id = "goblin" });
            Assert.That(Problems(d), Has.Some.Contains("RecruitHero needs a hero class"));
            d = Valid(); d.Nodes[1].Choices[1].Effects.Add(new DialogueEffect { Type = DialogueEffectType.JoinNpc, Id = "jester" });
            Assert.That(Problems(d), Has.Some.Contains("unknown NPC 'jester'"));
            d = Valid(); d.Nodes[1].Choices[1].Effects.Add(new DialogueEffect { Type = DialogueEffectType.GrantItem, Id = "gem" });
            Assert.That(Problems(d), Has.Some.Contains("unknown item 'gem'"));
            d = Valid(); d.Nodes[1].Choices[1].Effects.Add(new DialogueEffect { Type = DialogueEffectType.ClearFlag, Id = "npc:merchant" });
            Assert.That(Problems(d), Has.Some.Contains("reserved"));
            d = Valid(); d.Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.ThemeStart, Id = "moon" };
            Assert.That(Problems(d), Has.Some.Contains("unknown trigger theme 'moon'"));
            d = Valid(); d.Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.Talk, Id = "npc:jester" };
            Assert.That(Problems(d), Has.Some.Contains("unknown Talk NPC"));
        }

        // ── runner ─────────────────────────────────────────────────────────────

        private static DialogueDef Merchant => Real.Dialogues["greenvalley_road_merchant"];

        [Test]
        public void Runner_walks_lines_then_waits_for_a_choice()
        {
            var s = NewSave();
            var r = new DialogueRunner(Real, s, Merchant);
            Assert.That(r.Current.Name, Is.EqualTo("Wandering Merchant"));
            Assert.That(r.AwaitingChoice, Is.False);
            Assert.That(r.Advance(), Is.True);
            Assert.That(r.Current.Name, Is.EqualTo(Real.Class(s.Party[0]).Name), "$lead resolves to the first party member");
            Assert.That(r.Current.Portrait, Is.EqualTo(s.Party[0]));
            r.Advance();
            Assert.That(r.AwaitingChoice, Is.True);
            Assert.That(r.Advance(), Is.False, "a choice can't be skipped by advancing");
            Assert.That(r.Choices, Has.Count.EqualTo(3));
            Assert.That(r.Transcript, Has.Count.EqualTo(3));
        }

        [Test]
        public void Conditions_hide_choices_the_save_cant_take()
        {
            var r = new DialogueRunner(Real, NewSave(gold: 10), Merchant);
            r.Advance(); r.Advance();
            Assert.That(r.Choices.Select(c => c.Label), Has.None.Contains("map"), "not enough gold for the map");
            Assert.That(r.Choose(5).Ok, Is.False);
        }

        [Test]
        public void Choice_effects_change_the_save_and_the_dialogue_ends_seen()
        {
            var s = NewSave(gold: 100);
            int potions = s.Count("healing_potion_basic");
            var r = new DialogueRunner(Real, s, Merchant);
            r.Advance(); r.Advance();
            int map = r.Choices.FindIndex(c => c.Label.Contains("map"));
            Assert.That(r.Choose(map).Ok);
            Assert.That(s.Gold, Is.EqualTo(80));
            Assert.That(s.Flags, Does.Contain("bought_map"));
            Assert.That(s.Count("healing_potion_basic"), Is.EqualTo(potions + 1), "node effects apply on arrival");
            Assert.That(r.Transcript[2].Answer, Does.Contain("map"));
            r.Choose(r.Choices.FindIndex(c => c.Label == "Then ride with us."));
            Assert.That(DialogueRunner.NpcJoined(s, "merchant"));
            Assert.That(r.Log, Has.Some.Contains("Merchant joins"));
            Assert.That(r.Finished, Is.False);
            r.Advance();
            Assert.That(r.Finished);
            Assert.That(s.SeenDialogues, Does.Contain(Merchant.Id));
        }

        [Test]
        public void Every_branch_of_the_road_merchant_ends_with_the_merchant_joined()
        {
            // Nobody is lost for good, and no Camp service can be refused away (GG-T119 hides unjoined services).
            for (int pick = 0; pick < 3; pick++)
            {
                var s = NewSave(gold: 100);
                var r = new DialogueRunner(Real, s, Merchant);
                int guard = 0;
                while (!r.Finished && guard++ < 20)
                {
                    if (r.AwaitingChoice) r.Choose(System.Math.Min(pick, r.Choices.Count - 1)); else r.Advance();
                }
                Assert.That(r.Finished);
                Assert.That(DialogueRunner.NpcJoined(s, "merchant"), $"branch {pick}");
            }
        }

        [Test]
        public void Gold_effects_never_go_below_zero_and_recruits_join_once()
        {
            var s = NewSave(gold: 5);
            DialogueRunner.ApplyEffect(Real, s, new DialogueEffect { Type = DialogueEffectType.GrantGold, Amount = -50 });
            Assert.That(s.Gold, Is.EqualTo(0));
            int roster = s.Roster.Count;
            Assert.That(DialogueRunner.ApplyEffect(Real, s, new DialogueEffect { Type = DialogueEffectType.RecruitHero, Id = "sellsword" }), Does.Contain("joins"));
            Assert.That(DialogueRunner.ApplyEffect(Real, s, new DialogueEffect { Type = DialogueEffectType.RecruitHero, Id = "sellsword" }), Is.Null);
            Assert.That(s.Roster.Count, Is.EqualTo(roster + 1));
            DialogueRunner.ApplyEffect(Real, s, new DialogueEffect { Type = DialogueEffectType.JoinNpc, Id = "blacksmith" });
            DialogueRunner.ApplyEffect(Real, s, new DialogueEffect { Type = DialogueEffectType.ClearFlag, Id = "npc:blacksmith" });
            Assert.That(DialogueRunner.NpcJoined(s, "blacksmith"), "ClearFlag can't un-join an NPC");
        }

        // ── triggers ───────────────────────────────────────────────────────────

        [Test]
        public void Theme_start_beat_plays_once_and_never_in_test_mode()
        {
            var s = NewSave();
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-01")?.Id, Is.EqualTo("greenvalley_road_merchant"));
            // A postgame theme (GG-T120) carries its own single party-talk beat, played the same way. (It's past the
            // Blacksmith's and Alchemist's fallback theme, so their GearWorn (GG-T175) and CanBrew (GG-T176) beats would
            // come first on a save that never met them.)
            s.SeenDialogues.Add("greenvalley_smith"); s.SeenDialogues.Add("desert_alchemist");
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-04")?.Id, Is.EqualTo("greenvalley2_party_talk"));
            s.SeenDialogues.Add("greenvalley_road_merchant"); s.SeenDialogues.Add("greenvalley_party_talk");
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-01"), Is.Null);
            var t = TestLoop.NewSave(Real);
            Assert.That(DialogueRunner.PendingBeat(Real, t, "greenvalley-01"), Is.Null);
        }

        [Test]
        public void Stage_cleared_beat_waits_for_the_clear_and_its_conditions()
        {
            var s = NewSave();
            s.SeenDialogues.Add("greenvalley_road_merchant"); s.SeenDialogues.Add("greenvalley_party_talk");
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-02"), Is.Null, "greenvalley-01 isn't cleared yet");
            s.ClearedStages.Add("greenvalley-01");
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-02")?.Id, Is.EqualTo("greenvalley_recruit_mage"), "the story recruit comes first (GG-T118)");
            s.SeenDialogues.Add("greenvalley_recruit_mage");
            Assert.That(DialogueRunner.PendingBeat(Real, s, "greenvalley-02")?.Id, Is.EqualTo("greenvalley_after_first"));
            // A beat's own conditions gate it too (a fixture: the shipped join beats are unconditional, GG-T119).
            var d = new DialogueDef
            {
                Id = "gated", Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.StageCleared, Id = "greenvalley-01" },
                Conditions = { new DialogueCondition { Type = DialogueConditionType.NpcJoined, Id = "merchant" } },
                Speakers = { new DialogueSpeaker { Id = "a", Name = "A" } },
                Nodes = { new DialogueNode { Id = "n", Speaker = "a", Text = "x" } },
            };
            var db = Fixture(d);
            Assert.That(DialogueRunner.PendingBeat(db, s, "greenvalley-02"), Is.Null, "merchant hasn't joined");
            s.Flags.Add("npc:merchant");
            Assert.That(DialogueRunner.PendingBeat(db, s, "greenvalley-02")?.Id, Is.EqualTo("gated"));
        }

        [Test]
        public void Camp_talk_offers_the_party_and_joined_npcs()
        {
            var s = NewSave();
            Assert.That(DialogueRunner.TalkPartners(s), Is.EqualTo(s.Party));
            s.Flags.Add("npc:merchant");
            Assert.That(DialogueRunner.TalkPartners(s).Last(), Is.EqualTo("npc:merchant"));
            Assert.That(DialogueRunner.TalkTopic(Real, s, "npc:merchant")?.Id, Is.EqualTo("camp_merchant_chat"));
            var hero = s.Party[1];
            Assert.That(DialogueRunner.TalkTopic(Real, s, hero)?.Id, Is.EqualTo("talk_quiz_" + hero), "the hero's own unseen quiz (GG-T133) comes first");
            s.SeenDialogues.Add("talk_quiz_" + hero);
            var topic = DialogueRunner.TalkTopic(Real, s, hero);
            Assert.That(topic?.Id, Is.EqualTo("camp_party_chat"));
            var r = new DialogueRunner(Real, s, topic, hero);
            Assert.That(r.Current.Portrait, Is.EqualTo(hero), "$with is the hero being talked to");
            Assert.That(r.Choices.Select(c => c.Label), Has.Some.Contains("merchant"), "NpcJoined condition opens a topic");
        }

        [Test]
        public void Merchant_can_hire_a_hero_through_dialogue()
        {
            var s = NewSave(gold: 400);
            s.Flags.Add("npc:merchant");
            var r = new DialogueRunner(Real, s, DialogueRunner.TalkTopic(Real, s, "npc:merchant"));
            int hire = r.Choices.FindIndex(c => c.Label.Contains("guard"));
            Assert.That(hire, Is.GreaterThanOrEqualTo(0));
            r.Choose(hire);
            Assert.That(s.Hero("sellsword"), Is.Not.Null);
            Assert.That(s.Gold, Is.EqualTo(100));
            var again = new DialogueRunner(Real, s, DialogueRunner.TalkTopic(Real, s, "npc:merchant"));
            Assert.That(again.Choices.Select(c => c.Label), Has.None.Contains("guard"), "RosterLacks hides it once hired");
        }

        [Test]
        public void Every_hero_class_has_a_talk_quiz_whose_right_answer_pays_one_skill_point_once()
        {
            var heroes = Real.Classes.Values.Where(k => k.Team == Team.Hero).Select(k => k.Id).ToList();
            Assert.That(heroes, Is.Not.Empty);
            foreach (var id in heroes)
            {
                var quiz = Real.DialogueOrder.FirstOrDefault(d => d.Id == "talk_quiz_" + id);
                Assert.That(quiz, Is.Not.Null, $"{id} has no Talk quiz");
                Assert.That(quiz.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.Talk));
                Assert.That(quiz.Trigger.Id, Is.EqualTo(id));
                Assert.That(quiz.Once, Is.False, "the quiz is replayable");
                var rewarded = quiz.Nodes.SelectMany(n => n.Choices).Where(ch => ch.Effects.Any(e => e.Type == DialogueEffectType.GrantSkillPoint)).ToList();
                Assert.That(rewarded, Has.Count.EqualTo(1), $"{id}: exactly one right answer");
                Assert.That(quiz.Nodes.SelectMany(n => n.Choices).Count(), Is.GreaterThanOrEqualTo(3), $"{id}: needs wrong answers too");
            }
        }

        [Test]
        public void Talk_quiz_right_answer_grants_a_skill_point_once_and_a_wrong_one_never()
        {
            var s = NewSave();
            var hero = s.Party[0];
            var quiz = Real.Dialogues["talk_quiz_" + hero];
            int before = s.Hero(hero).SkillPoints;

            int Pick(bool right)
            {
                var r = new DialogueRunner(Real, s, quiz, hero);
                while (!r.AwaitingChoice && !r.Finished) r.Advance();
                int i = r.Choices.FindIndex(ch => ch.Effects.Any(e => e.Type == DialogueEffectType.GrantSkillPoint) == right);
                r.Choose(i);
                while (!r.Finished) { if (!r.Advance()) r.Choose(0); }
                return s.Hero(hero).SkillPoints;
            }

            Assert.That(Pick(false), Is.EqualTo(before), "a wrong answer earns nothing");
            Assert.That(Pick(true), Is.EqualTo(before + 1), "the right answer earns the point");
            Assert.That(Pick(true), Is.EqualTo(before + 1), "replaying can't farm it");
            Assert.That(DialogueRunner.TalkTopic(Real, s, hero), Is.Not.Null, "still offered after it's been played");

            var clear = new DialogueEffect { Type = DialogueEffectType.ClearFlag, Id = DialogueRunner.SkillPointFlag(quiz.Id, hero) };
            DialogueRunner.ApplyEffect(Real, s, clear);
            Assert.That(s.Flags, Does.Contain(DialogueRunner.SkillPointFlag(quiz.Id, hero)), "ClearFlag can't reset the guard");
        }

        [Test]
        public void Old_saves_without_dialogue_fields_migrate()
        {
            var s = NewSave(); s.Flags = null; s.SeenDialogues = null;
            SaveMigrations.Migrate(s);
            Assert.That(s.Flags, Is.Not.Null);
            Assert.That(s.SeenDialogues, Is.Not.Null);
        }
    }
}
