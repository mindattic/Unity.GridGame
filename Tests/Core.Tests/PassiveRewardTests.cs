using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Learnable-passive rewards (bible §25.6.4, GG-T301): Tomes from quests, bounties, campaign milestones, chests
    /// and battle drops; teaching; the passive slot cap; save round-trip; reward-table validation.</summary>
    public class PassiveRewardTests
    {
        private static SaveData NewSave() => Progression.NewGame(Real, "Tomes");

        private static ContentDb Fresh() =>
            ContentLoader.Load(name => { var f = Path.Combine(ContentDir, name + ".json"); return File.Exists(f) ? File.ReadAllText(f) : null; });

        private static BattleState Won(SaveData s, string stageId, int seed = 7)
        {
            var b = BattleEngine.Create(Real, BattleFactory.Build(Real, s, Real.Stage(stageId), seed), new List<BattleEvent>());
            b.Phase = BattlePhase.Victory;
            return b;
        }

        private static List<ItemDef> Tomes_() => Real.Items.Values.Where(i => i.Type == ItemType.Tome).ToList();

        [Test]
        public void Reward_tables_are_valid_and_every_source_grants_tomes()
        {
            var problems = ContentLoader.Validate(Real).Where(p => p.Contains("Tome") || p.Contains("Chest") || p.Contains("tome_") || p.Contains("chest_")
                                                                   || p.Contains("milestone") || p.StartsWith("droptable") || p.Contains("LearnableBy")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            bool IsTome(string id) => Real.TryItem(id)?.Type == ItemType.Tome;
            var enemyTables = Real.Classes.Values.Where(c => c.Team == Team.Enemy && c.DropTable != null).Select(c => c.DropTable).Distinct().Select(id => Real.DropTables[id]).ToList();
            var chestTables = Real.Items.Values.Where(i => i.Type == ItemType.Chest).Select(i => Real.DropTables[i.OpensTable]).ToList();
            Assert.That(Real.QuestOrder.Count(q => IsTome(q.RewardItem)), Is.GreaterThan(0), "quests");
            Assert.That(Real.Bounties.Values.Count(b => IsTome(b.RewardItem)), Is.GreaterThan(0), "bounties");
            Assert.That(Real.Campaign.Themes.Count(t => IsTome(t.MilestoneReward)), Is.GreaterThanOrEqualTo(5), "campaign milestones");
            Assert.That(chestTables.Count(t => t.Entries.Any(e => IsTome(e.Item))), Is.EqualTo(chestTables.Count), "every chest can hold a tome");
            Assert.That(enemyTables.Count(t => t.Entries.Any(e => IsTome(e.Item))), Is.GreaterThan(0), "battle-finish loot");
            Assert.That(enemyTables.All(t => t.Entries.Any(e => e.Item == "chest_wooden")), Is.True, "every enemy family can drop a chest");

            // Every committed battle passive has a Tome, and every Tome is reachable from some source.
            foreach (var a in Real.Abilities.Values.Where(a => a.Trigger != null && new[] { "double_attack", "counter_attack", "lifesteal", "tough_hide", "berserk", "regenerate", "poison_ward", "steadfast", "thorns", "mana_siphon" }.Contains(a.Id)))
                Assert.That(Tomes_().Any(t => t.TeachesAbility == a.Id), Is.True, $"no tome teaches {a.Id}");
            var sources = new HashSet<string>(Real.QuestOrder.Select(q => q.RewardItem).Concat(Real.Bounties.Values.Select(b => b.RewardItem))
                .Concat(Real.Campaign.Themes.Select(t => t.MilestoneReward)).Concat(Real.DropTables.Values.SelectMany(t => t.Entries.Select(e => e.Item))).Where(x => x != null));
            foreach (var t in Tomes_()) Assert.That(sources, Does.Contain(t.Id), $"{t.Id} is granted nowhere");
        }

        [Test]
        public void Validation_rejects_bad_tomes_chests_restrictions_and_milestones()
        {
            var db = Fresh();
            db.Items["tome_bad"] = new ItemDef { Id = "tome_bad", Name = "Bad", Type = ItemType.Tome, TeachesAbility = "heal" };
            db.Items["tome_none"] = new ItemDef { Id = "tome_none", Name = "None", Type = ItemType.Tome };
            db.Items["chest_bad"] = new ItemDef { Id = "chest_bad", Name = "Bad", Type = ItemType.Chest, OpensTable = "nope" };
            db.Items["chest_nest"] = new ItemDef { Id = "chest_nest", Name = "Nest", Type = ItemType.Chest, OpensTable = "chest_nest" };
            db.DropTables["chest_nest"] = new DropTableDef { Id = "chest_nest", Entries = { new DropEntry { Item = "chest_wooden", Chance = 0.5f } } };
            db.Items["potion_teaches"] = new ItemDef { Id = "potion_teaches", Name = "P", Type = ItemType.Consumable, TeachesAbility = "thorns" };
            db.Abilities["thorns"].LearnableBy.Add("dragon");
            db.Campaign.Themes[0].MilestoneReward = "no_such_item";
            var p = ContentLoader.Validate(db);
            Assert.That(p, Has.Some.Contains("item tome_bad: 'heal' isn't a Passive or Reactive"));
            Assert.That(p, Has.Some.Contains("item tome_none: a Tome needs TeachesAbility"));
            Assert.That(p, Has.Some.Contains("item chest_bad: unknown drop table 'nope'"));
            Assert.That(p, Has.Some.Contains("holds another chest 'chest_wooden'"));
            Assert.That(p, Has.Some.Contains("item potion_teaches: only a Tome may set TeachesAbility"));
            Assert.That(p, Has.Some.Contains("ability thorns: LearnableBy 'dragon'"));
            Assert.That(p, Has.Some.Contains("unknown milestone reward 'no_such_item'"));
        }

        [Test]
        public void A_tome_teaches_a_chosen_hero_once_and_respects_class_restrictions()
        {
            var s = NewSave();
            s.AddItem("tome_thorns", 2);
            Assert.That(Tomes.Candidates(Real, s, "tome_thorns").Count, Is.EqualTo(s.Roster.Count));
            var r = Tomes.Teach(Real, s, "tome_thorns", "paladin");
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(s.Hero("paladin").Known, Does.Contain("thorns"));
            Assert.That(s.Count("tome_thorns"), Is.EqualTo(1));
            Assert.That(Tomes.Teach(Real, s, "tome_thorns", "paladin").Ok, Is.False, "already known");
            Assert.That(s.Count("tome_thorns"), Is.EqualTo(1), "a refused teach keeps the tome");
            Assert.That(Tomes.Teach(Real, s, "tome_counter_attack", "paladin").Ok, Is.False, "no tome in the bag");

            var db = Fresh();
            db.Abilities["thorns"].LearnableBy.Add("Tank");
            Assert.That(Tomes.WhyNot(db, s, "tome_thorns", "cleric"), Does.Contain("Tank"));
            Assert.That(Tomes.WhyNot(db, s, "tome_thorns", "barbarian"), Does.Contain("Tank"));
            db.Abilities["thorns"].LearnableBy.Add("cleric");
            Assert.That(Tomes.WhyNot(db, s, "tome_thorns", "cleric"), Is.Null, "a class id also qualifies");
        }

        [Test]
        public void A_learned_passive_fights_with_the_hero()
        {
            var s = NewSave();
            s.AddItem("tome_counter_attack", 1);
            Assert.That(Tomes.Teach(Real, s, "tome_counter_attack", "barbarian").Ok, Is.True);
            var b = BattleEngine.Create(Real, BattleFactory.Build(Real, s, Real.Stage("greenvalley-01"), 3), new List<BattleEvent>());
            var u = b.Units.First(x => x.IsHero && x.Def.Id == "barbarian");
            Assert.That(u.Passives, Does.Contain("counter_attack"));
            Assert.That(Triggers.Sources(Real, u).Select(a => a.Id), Does.Contain("counter_attack"));
            Assert.That(b.Units.First(x => x.IsHero && x.Def.Id == "paladin").Passives, Does.Not.Contain("counter_attack"));
        }

        [Test]
        public void Only_the_passive_slots_worth_of_learned_passives_are_on_and_the_player_picks_which()
        {
            var s = NewSave();
            var h = s.Hero("cleric");
            foreach (var id in new[] { "tome_thorns", "tome_regenerate", "tome_steadfast" })
            {
                s.AddItem(id, 1);
                Assert.That(Tomes.Teach(Real, s, id, "cleric").Ok, Is.True);
            }
            Assert.That(PassiveLoadout.Slots(h), Is.EqualTo(2));
            Assert.That(PassiveLoadout.Learned(Real, h).Select(a => a.Id), Is.EqualTo(new[] { "thorns", "regenerate", "steadfast" }));
            Assert.That(PassiveLoadout.Active(Real, h), Is.EqualTo(new[] { "thorns", "regenerate" }), "first learned, first on");
            Assert.That(BattleFactory.SpecFor(Real, h).Passives, Is.EqualTo(new[] { "thorns", "regenerate" }));

            Assert.That(PassiveLoadout.Toggle(Real, h, "steadfast").Ok, Is.False, "slots full");
            Assert.That(PassiveLoadout.Toggle(Real, h, "thorns").Ok, Is.True);
            Assert.That(PassiveLoadout.Toggle(Real, h, "steadfast").Ok, Is.True);
            Assert.That(PassiveLoadout.Active(Real, h), Is.EquivalentTo(new[] { "regenerate", "steadfast" }));
            Assert.That(BattleFactory.SpecFor(Real, h).Passives, Is.EquivalentTo(new[] { "regenerate", "steadfast" }));
            Assert.That(PassiveLoadout.Toggle(Real, h, "heal").Ok, Is.False, "not a learned passive");

            // A fourth learned with a slot free goes straight on once the player has chosen; level 10 opens a third slot.
            Assert.That(PassiveLoadout.Toggle(Real, h, "steadfast").Ok, Is.True);
            s.AddItem("tome_poison_ward", 1);
            Tomes.Teach(Real, s, "tome_poison_ward", "cleric");
            Assert.That(PassiveLoadout.Active(Real, h), Is.EquivalentTo(new[] { "regenerate", "poison_ward" }));
            h.TotalXp = Formulas.TotalXpForLevel(10);
            Assert.That(PassiveLoadout.Slots(h), Is.EqualTo(3));
            Assert.That(PassiveLoadout.Toggle(Real, h, "thorns").Ok, Is.True);
            Assert.That(PassiveLoadout.Active(Real, h).Count, Is.EqualTo(3));
        }

        [Test]
        public void Class_kit_passives_are_innate_and_take_no_slot()
        {
            var s = NewSave();
            var kitted = Real.Classes.Values.First(c => c.Team == Team.Hero && c.Abilities.Any(id => RewardCatalog.Teachable(Real.TryAbility(id))));
            var passive = kitted.Abilities.First(id => RewardCatalog.Teachable(Real.TryAbility(id)));
            var h = Progression.NewHero(Real, kitted.Id);
            s.Roster.Add(h);
            Assert.That(PassiveLoadout.Innate(Real, h).Select(a => a.Id), Does.Contain(passive));
            Assert.That(PassiveLoadout.Learned(Real, h).Select(a => a.Id), Does.Not.Contain(passive));
            var tome = Tomes_().FirstOrDefault(t => t.TeachesAbility == passive);
            if (tome != null) { s.AddItem(tome.Id, 1); Assert.That(Tomes.WhyNot(Real, s, tome.Id, h.ClassId), Is.EqualTo("Already knows it")); }
        }

        [Test]
        public void Passive_choices_milestones_and_chest_count_survive_a_save_round_trip()
        {
            var s = NewSave();
            var h = s.Hero("paladin");
            s.AddItem("tome_thorns", 1); Tomes.Teach(Real, s, "tome_thorns", "paladin");
            s.AddItem("tome_berserk", 1); Tomes.Teach(Real, s, "tome_berserk", "paladin");
            PassiveLoadout.Toggle(Real, h, "thorns");
            s.Milestones.Add("greenvalley");
            s.ChestsOpened = 4;
            var back = SaveFiles.Deserialize(SaveFiles.Serialize(s));
            var bh = back.Hero("paladin");
            Assert.That(bh.PassivesChosen, Is.True);
            Assert.That(bh.ActivePassives, Is.EqualTo(new[] { "berserk" }));
            Assert.That(PassiveLoadout.Active(Real, bh), Is.EqualTo(new[] { "berserk" }));
            Assert.That(back.Milestones, Does.Contain("greenvalley"));
            Assert.That(back.ChestsOpened, Is.EqualTo(4));
        }

        [Test]
        public void Quest_and_bounty_rewards_grant_tomes()
        {
            var s = NewSave();
            var q = Real.TryQuest("q_grown_not_forged");
            Assert.That(Real.TryItem(q.RewardItem).Type, Is.EqualTo(ItemType.Tome));
            Quests.Start(Real, s, q.Id);
            s.AddItem(q.Objective.Id, Quests.Goal(q));
            Assert.That(Quests.TurnIn(Real, s, q.Id).Ok, Is.True);
            Assert.That(s.Count(q.RewardItem), Is.EqualTo(1));

            var b = Real.Bounties["bounty_cave_dweller"];
            Assert.That(Real.TryItem(b.RewardItem).Type, Is.EqualTo(ItemType.Tome));
            s.Bounty.ActiveId = b.Id;
            s.Bounty.Progress = b.Kills;
            Assert.That(Bounties.Claim(Real, s).Ok, Is.True);
            Assert.That(s.Count(b.RewardItem), Is.EqualTo(1));
        }

        [Test]
        public void A_themes_first_full_clear_pays_its_milestone_once()
        {
            var s = NewSave();
            var theme = Real.Campaign.Themes[0];
            string last = theme.Stages.Last();
            var first = BattleFactory.Commit(Real, s, Won(s, theme.Stages[0]));
            Assert.That(first.Milestone, Is.Null, "only the theme's last stage");
            var rep = BattleFactory.Commit(Real, s, Won(s, last));
            Assert.That(rep.Milestone, Is.EqualTo(theme.Name));
            Assert.That(rep.Items.ContainsKey(theme.MilestoneReward), Is.True, "shown with the loot");
            Assert.That(s.Count(theme.MilestoneReward), Is.EqualTo(1));
            var again = BattleFactory.Commit(Real, s, Won(s, last, 9));
            Assert.That(again.Milestone, Is.Null);
            Assert.That(s.Count(theme.MilestoneReward), Is.EqualTo(1), "paid once");
        }

        [Test]
        public void Battle_finish_loot_carries_tomes_and_chests_into_the_bag()
        {
            var s = NewSave();
            var won = Won(s, "greenvalley-01");
            won.Results.Items["tome_lifesteal"] = 1;
            won.Results.Items["chest_iron"] = 1;
            var rep = BattleFactory.Commit(Real, s, won);
            Assert.That(rep.Items["tome_lifesteal"], Is.EqualTo(1));
            Assert.That(s.Count("tome_lifesteal"), Is.EqualTo(1));
            Assert.That(s.Count("chest_iron"), Is.EqualTo(1));
            Assert.That(Real.DropTables["vampire"].Entries.Any(e => e.Item == "tome_lifesteal"), Is.True, "the Vampire Lord can drop one");
        }

        [Test]
        public void Opening_a_chest_rolls_its_table_deterministically_and_never_comes_up_empty()
        {
            var s = NewSave();
            s.AddItem("chest_wooden", 3);
            var twin = SaveFiles.Deserialize(SaveFiles.Serialize(s));
            for (int i = 0; i < 3; i++)
            {
                int gold = s.Gold;
                var r = Chests.Open(Real, s, "chest_wooden", out var haul);
                var r2 = Chests.Open(Real, twin, "chest_wooden", out var haul2);
                Assert.That(r.Ok, Is.True, r.Message);
                Assert.That(haul.Items.Count, Is.GreaterThan(0));
                Assert.That(haul.Gold, Is.InRange(40, 120));
                Assert.That(s.Gold, Is.GreaterThanOrEqualTo(gold + haul.Gold));
                Assert.That(r2.Message, Is.EqualTo(r.Message), "same save, same chest");
            }
            Assert.That(s.Count("chest_wooden"), Is.EqualTo(0));
            Assert.That(s.ChestsOpened, Is.EqualTo(3));
            Assert.That(Chests.Open(Real, s, "chest_wooden", out _).Ok, Is.False);
            Assert.That(Chests.Open(Real, s, "hi_potion", out _).Ok, Is.False);

            // Over many opens every tome a chest lists turns up.
            var table = Real.DropTables["chest_gilded"];
            var rng = new GridGame.Core.Rng(5);
            var seen = new HashSet<string>();
            for (int i = 0; i < 400; i++) foreach (var id in Chests.Roll(Real, table, rng).Items.Keys) seen.Add(id);
            foreach (var e in table.Entries) Assert.That(seen, Does.Contain(e.Item));
        }

        [Test]
        public void The_camp_routine_opens_chests_and_teaches_tomes()
        {
            var s = NewSave();
            s.AddItem("tome_thorns", 1);
            s.AddItem("chest_wooden", 1);
            CampRoutine.Run(Real, s);
            Assert.That(s.Count("chest_wooden"), Is.EqualTo(0));
            Assert.That(s.Count("tome_thorns"), Is.EqualTo(0));
            Assert.That(s.Party.Select(s.Hero).Any(h => PassiveLoadout.Active(Real, h).Contains("thorns")), Is.True);
        }
    }
}
