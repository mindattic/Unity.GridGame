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
    /// <summary>Optional side quests (bible §22.11, GG-T237): content, the dialogue vocabulary, battle progress, hand-in.</summary>
    public class QuestTests
    {
        private static bool PortraitOnDisk(string id) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png"));

        private static SaveData NewSave() => Progression.NewGame(Real, "Quests");

        private static BattleState Won(SaveData s, string stageId, int seed = 7)
        {
            var b = BattleEngine.Create(Real, BattleFactory.Build(Real, s, Real.Stage(stageId), seed), new List<BattleEvent>());
            b.Phase = BattlePhase.Victory;
            return b;
        }

        [Test]
        public void Quest_content_is_valid_and_spread_over_the_five_main_biomes()
        {
            var problems = ContentLoader.Validate(Real, PortraitOnDisk).Where(p => p.StartsWith("quest") || p.Contains("quest_")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(Real.QuestOrder.Count, Is.InRange(8, 12));
            var main = Real.Campaign.Themes.Take(5).Select(t => t.Id);
            Assert.That(main.All(t => Real.QuestOrder.Any(q => q.Theme == t)), Is.True, "every main biome has a quest");
            foreach (var q in Real.QuestOrder)
                Assert.That(Real.DialogueOrder.Any(d => d.AllEffects().Any(e => e.Type == DialogueEffectType.StartQuest && e.Id == q.Id)), Is.True, $"{q.Id} has a giver");
        }

        [Test]
        public void Validation_rejects_a_quest_nobody_gives_and_bad_references()
        {
            var db = ContentLoader.Load(name => { var f = Path.Combine(ContentDir, name + ".json"); return File.Exists(f) ? File.ReadAllText(f) : null; });
            db.QuestOrder.Add(new QuestDef { Id = "q_orphan", Name = "Orphan", Summary = "x", RewardGold = 1, Objective = new QuestObjective { Kind = QuestObjectiveKind.Defeat, Id = "paladin" } });
            db.Quests["q_orphan"] = db.QuestOrder.Last();
            var p = ContentLoader.Validate(db).Where(x => x.StartsWith("quest q_orphan")).ToList();
            Assert.That(p, Has.Some.Contains("no dialogue offers it"));
            Assert.That(p, Has.Some.Contains("Defeat needs an enemy class"));
        }

        [Test]
        public void A_quest_is_offered_only_after_its_unlock_stage_and_the_conversation_starts_it()
        {
            var s = NewSave();
            var q = Real.TryQuest("q_wells_wolves");
            Assert.That(Quests.State(Real, s, q), Is.EqualTo(QuestState.Locked));
            var offer = Real.Dialogues["quest_offer_wells_wolves"];
            Assert.That(DialogueRunner.All(Real, s, offer.Conditions), Is.False, "not offered before its stage");
            s.ClearedStages.Add(q.Unlock);
            Assert.That(DialogueRunner.All(Real, s, offer.Conditions), Is.True);
            s.Flags.Add("npc:innkeeper");
            s.SeenDialogues.Add("camp_innkeeper_chat");
            Assert.That(DialogueRunner.TalkTopic(Real, s, "npc:innkeeper")?.Id, Is.EqualTo("quest_offer_wells_wolves"), "an unheard offer comes before a heard chat");

            var r = new DialogueRunner(Real, s, offer);
            r.Choose(r.Choices.FindIndex(c => c.Effects.Any(e => e.Type == DialogueEffectType.StartQuest)));
            Assert.That(Quests.State(Real, s, q), Is.EqualTo(QuestState.Active));
            Assert.That(r.Log, Has.Some.Contains(q.Name));
            Assert.That(DialogueRunner.Check(Real, s, new DialogueCondition { Type = DialogueConditionType.QuestActive, Id = q.Id }), Is.True);
            Assert.That(DialogueRunner.All(Real, s, offer.Conditions), Is.False, "no longer offered once under way");
        }

        [Test]
        public void Saying_not_now_keeps_the_quest_in_the_journal_to_accept_later()
        {
            var s = NewSave();
            s.ClearedStages.Add("greenvalley-01");
            var offer = Real.Dialogues["quest_offer_wells_wolves"];
            var r = new DialogueRunner(Real, s, offer);
            r.Choose(r.Choices.FindIndex(c => c.Label == "Not now."));
            while (!r.Finished) r.Advance();
            Assert.That(Quests.Heard(Real, s).Select(q => q.Id), Does.Contain("q_wells_wolves"));
            Assert.That(Quests.Start(Real, s, "q_wells_wolves").Ok, Is.True);
            Assert.That(Quests.Start(Real, s, "q_wells_wolves").Ok, Is.False, "can't take it twice");
        }

        [Test]
        public void Defeat_quests_count_kills_on_victory_only_and_hand_in_pays_once()
        {
            var s = NewSave();
            var q = Real.TryQuest("q_wells_wolves");
            Quests.Start(Real, s, q.Id);
            var lost = Won(s, "greenvalley-01");
            lost.Phase = BattlePhase.Defeat;
            lost.Results.Kills[q.Objective.Id] = 3;
            BattleFactory.Commit(Real, s, lost);
            Assert.That(Quests.Progress(s, q), Is.EqualTo(0), "a defeat counts nothing");

            var win = Won(s, "greenvalley-01");
            win.Results.Kills[q.Objective.Id] = 3;
            var rep = BattleFactory.Commit(Real, s, win);
            Assert.That(Quests.Progress(s, q), Is.EqualTo(3));
            Assert.That(rep.QuestProgress, Has.Some.Contains("3/"));
            Assert.That(Quests.TurnIn(Real, s, q.Id).Ok, Is.False, "not finished yet");

            var win2 = Won(s, "greenvalley-01", 8);
            win2.Results.Kills[q.Objective.Id] = 9;
            BattleFactory.Commit(Real, s, win2);
            Assert.That(Quests.Progress(s, q), Is.EqualTo(Quests.Goal(q)), "capped at the goal");
            Assert.That(Quests.ReadyCount(Real, s), Is.EqualTo(1));

            int gold = s.Gold, potions = s.Count(q.RewardItem);
            Assert.That(Quests.TurnIn(Real, s, q.Id).Ok, Is.True);
            Assert.That(s.Gold, Is.EqualTo(gold + q.RewardGold));
            Assert.That(s.Count(q.RewardItem), Is.EqualTo(potions + 1));
            Assert.That(Quests.State(Real, s, q), Is.EqualTo(QuestState.Done));
            Assert.That(Quests.TurnIn(Real, s, q.Id).Ok, Is.False, "pays once");
            Assert.That(Quests.Start(Real, s, q.Id).Ok, Is.False, "a done quest doesn't restart");
        }

        [Test]
        public void Clear_stage_quests_check_the_party_size_and_nobody_falling()
        {
            var s = NewSave();
            var light = Real.TryQuest("q_travel_light");
            Assert.That(light.Objective.MaxHeroes, Is.EqualTo(3));
            Quests.Start(Real, s, light.Id);
            Summon.Join(Real, s, Real.Summon.Pool.First(id => s.Hero(id) == null));
            Assert.That(s.Party.Count, Is.EqualTo(4));
            BattleFactory.Commit(Real, s, Won(s, light.Objective.Id));
            Assert.That(Quests.IsReady(Real, s, light), Is.False, "four heroes is too many");
            s.Party.RemoveAt(3);
            BattleFactory.Commit(Real, s, Won(s, light.Objective.Id, 9));
            Assert.That(Quests.IsReady(Real, s, light), Is.True);

            var ward = Real.TryQuest("q_hold_the_ward");
            Quests.Start(Real, s, ward.Id);
            var fell = Won(s, ward.Objective.Id);
            fell.Units.First(u => u.IsHero).Hp = 0;
            BattleFactory.Commit(Real, s, fell);
            Assert.That(Quests.IsReady(Real, s, ward), Is.False, "someone fell");
            BattleFactory.Commit(Real, s, Won(s, ward.Objective.Id, 10));
            Assert.That(Quests.IsReady(Real, s, ward), Is.True);
            var sp = s.Party.ToDictionary(id => id, id => s.Hero(id).SkillPoints);
            Assert.That(Quests.TurnIn(Real, s, ward.Id).Ok, Is.True);
            foreach (var id in s.Party) Assert.That(s.Hero(id).SkillPoints, Is.EqualTo(sp[id] + ward.RewardSkillPoints));
        }

        [Test]
        public void Bring_quests_read_the_bag_and_take_the_items_on_hand_in_and_a_reward_flag_opens_a_follow_up()
        {
            var s = NewSave();
            var q = Real.TryQuest("q_grown_not_forged");
            Quests.Start(Real, s, q.Id);
            s.AddItem(q.Objective.Id, Quests.Goal(q) - 1);
            Assert.That(Quests.IsReady(Real, s, q), Is.False);
            s.AddItem(q.Objective.Id, 2);
            Assert.That(Quests.IsReady(Real, s, q), Is.True);
            Assert.That(Quests.TurnIn(Real, s, q.Id).Ok, Is.True);
            Assert.That(s.Count(q.Objective.Id), Is.EqualTo(1), "only the asked-for count is handed over");
            Assert.That(s.Flags, Does.Contain(q.RewardFlag));
            s.Flags.Add("npc:blacksmith");
            s.SeenDialogues.Add("camp_blacksmith_chat");
            Assert.That(DialogueRunner.TalkTopic(Real, s, "npc:blacksmith")?.Id, Is.EqualTo("quest_after_grown_not_forged"));
        }

        [Test]
        public void Old_saves_get_an_empty_journal()
        {
            var s = NewSave();
            s.Quests = null;
            SaveMigrations.Migrate(s, Real);
            Assert.That(s.Quests, Is.Not.Null);
            Assert.That(Quests.ReadyCount(Real, s), Is.EqualTo(0));
        }
    }
}
