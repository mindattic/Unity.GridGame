using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>Camp NPCs join along the way (§25.0.1, GG-T119): each Camp service stays hidden until its keeper joins
    /// at a story beat; the camp routine only uses joined services; Test Mode and old saves have everyone already.</summary>
    public class CampNpcTests
    {
        private static ContentDb Real => TestKit.Real;

        private static readonly string[] Npcs = { CampNpcs.Merchant, CampNpcs.Blacksmith, CampNpcs.Alchemist, CampNpcs.Enchantress, CampNpcs.Innkeeper };

        [Test]
        public void The_five_camp_npcs_are_the_dialogue_npcs()
        {
            Assert.That(CampNpcs.All, Is.EquivalentTo(Npcs));
        }

        [Test]
        public void A_new_game_has_met_nobody_yet()
        {
            var s = Progression.NewGame(Real, "T");
            Assert.That(Npcs.Where(n => CampNpcs.Joined(s, n)), Is.Empty);
        }

        /// <summary>Every path through a dialogue (each choice taken at each choice list, loops cut), as the saves
        /// those paths leave behind.</summary>
        private static List<SaveData> EveryPath(DialogueDef d, int gold = 1000)
        {
            var ends = new List<SaveData>();
            var queue = new Queue<List<int>>();
            queue.Enqueue(new List<int>());
            while (queue.Count > 0 && ends.Count < 200)
            {
                var picks = queue.Dequeue();
                var s = Progression.NewGame(Real, "T");
                s.Gold = gold;
                var r = new DialogueRunner(Real, s, d);
                int i = 0, guard = 0;
                while (!r.Finished && guard++ < 50)
                {
                    if (!r.AwaitingChoice) { r.Advance(); continue; }
                    if (i < picks.Count) { r.Choose(picks[i++]); continue; }
                    for (int k = 1; k < r.Choices.Count && picks.Count < 6; k++) queue.Enqueue(picks.Append(k).ToList());
                    picks = picks.Append(0).ToList();
                    i++;
                    r.Choose(0);
                }
                Assert.That(r.Finished, $"{d.Id}: a path never ends");
                ends.Add(s);
            }
            return ends;
        }

        [Test]
        public void Every_npc_joins_on_every_branch_of_a_story_beat()
        {
            foreach (var npc in Npcs)
            {
                var beats = Real.DialogueOrder.Where(d => d.Trigger.Kind != DialogueTriggerKind.Talk
                    && d.Nodes.Any(n => n.Effects.Concat(n.Choices.SelectMany(ch => ch.Effects)).Any(e => e.Type == DialogueEffectType.JoinNpc && e.Id == npc))).ToList();
                Assert.That(beats, Is.Not.Empty, $"no story beat joins the {npc}");
                var beat = beats[0];
                Assert.That(beat.Conditions, Is.Empty, $"{beat.Id}: a join beat must always fire, or the {npc} could be missed for good");
                foreach (var end in EveryPath(beat))
                    Assert.That(CampNpcs.Joined(end, npc), $"{beat.Id}: a branch leaves the {npc} behind");
            }
        }

        [Test]
        public void Every_npc_has_a_camp_talk()
        {
            var s = Progression.NewGame(Real, "T");
            CampNpcs.JoinAll(s);
            foreach (var npc in Npcs)
                Assert.That(DialogueRunner.TalkTopic(Real, s, ContentLoader.NpcFlagPrefix + npc), Is.Not.Null, $"nothing to say to the {npc}");
        }

        /// <summary>Launching the campaign in order (every pending beat per launch, as StageLauncher does) meets every NPC
        /// early: the Merchant before battle 1, the Innkeeper and Blacksmith inside the Green Valley expedition (the
        /// Blacksmith once a hero's gear first wears below 60%, GG-T175 — here after two stages), the Alchemist at the
        /// Sandsea and the Enchantress at the Fens — well before the end of the first act.</summary>
        [Test]
        public void Playing_the_campaign_in_order_meets_everyone_by_the_third_expedition()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            var met = new Dictionary<string, string>();
            foreach (var stage in c.CampaignStages())
            {
                DialogueRunner.PlayPendingBeatsHeadless(c, s, stage.Id);
                foreach (var n in Npcs) if (CampNpcs.Joined(s, n) && !met.ContainsKey(n)) met[n] = stage.Id;
                s.ClearedStages.Add(stage.Id);
                if (stage.Id == "greenvalley-02") WearLeadWeapon(c, s, 0.5f);
            }
            Assert.That(met.Keys, Is.EquivalentTo(Npcs), "someone is never met");
            Assert.That(met[CampNpcs.Merchant], Is.EqualTo("greenvalley-01"));
            Assert.That(met[CampNpcs.Innkeeper], Is.EqualTo("greenvalley-02"));
            Assert.That(met[CampNpcs.Blacksmith], Is.EqualTo("greenvalley-03"));
            Assert.That(met[CampNpcs.Alchemist], Is.EqualTo("desert-01"));
            Assert.That(met[CampNpcs.Enchantress], Is.EqualTo("swamp-01"));
        }

        /// <summary>GG-T179 (§25.0.8): the Enchantress joins once the first armor piece is forged — wherever the party is —
        /// and hands over an Arcane Dust toward the first enchant; a party that never forges armor meets her at the Fens.</summary>
        [Test]
        public void The_enchantress_joins_after_the_first_armor_piece()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-02");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Enchantress), Is.False, "no armor yet");
            s.Flags.Add(Forging.FirstArmorFlag);
            int dust = s.Count("mat_arcane_dust");
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-02");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Enchantress), Is.True, "joins after the first armor, still in Green Valley");
            Assert.That(s.Count("mat_arcane_dust"), Is.EqualTo(dust + 1), "she brings an Arcane Dust");
            Assert.That(CampUnlocks.JoinHint(c, CampNpcs.Enchantress), Is.EqualTo("Joins once you've forged your first armor"));
        }

        /// <summary>Gives the lead a weapon worn to <paramref name="fraction"/> of its max, as battles would.</summary>
        internal static Repair.WornPiece WearLeadWeapon(ContentDb c, SaveData s, float fraction)
        {
            var w = c.Items.Values.First(i => i.Type == ItemType.Equipment && i.Slot == EquipmentSlot.Weapon && i.Durability >= 10);
            var h = s.Hero(s.Party[0]);
            h.WeaponId = w.Id;
            h.WeaponDurability = (int)(w.Durability * fraction);
            return Repair.MostWorn(c, s);
        }

        [Test]
        public void The_blacksmith_joins_once_gear_first_wears_below_sixty_percent()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Blacksmith), Is.False, "no gear yet: nothing to mend");
            WearLeadWeapon(c, s, 0.7f);
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Blacksmith), Is.False, "70% is still fine");
            WearLeadWeapon(c, s, 0.55f);
            Assert.That(DialogueRunner.PendingBeat(c, s, "greenvalley-01")?.Id, Is.EqualTo("greenvalley_smith"));
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Blacksmith), Is.True);

            var never = Progression.NewGame(c, "T");
            DialogueRunner.PlayPendingBeatsHeadless(c, never, "desert-01");
            Assert.That(CampNpcs.Joined(never, CampNpcs.Blacksmith), Is.True, "a party that never wears gear still meets him at the Sandsea");
        }

        /// <summary>GG-T176 (§25.0.5): the Alchemist's beat fires once the bag holds the loot for a first bottle brew,
        /// hands over an Empty Bottle so that brew can happen, and still fires at the Sandsea for a party that never loots.</summary>
        [Test]
        public void The_alchemist_joins_once_the_loot_for_a_first_brew_is_in_the_bag()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Alchemist), Is.False, "nothing looted yet");
            s.AddItem("mat_wolf_pelt", 1);
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Alchemist), Is.False, "a pelt alone isn't a healing brew");
            s.AddItem("mat_tattered_cloth", 1);
            Assert.That(DialogueRunner.PendingBeat(c, s, "greenvalley-01")?.Id, Is.EqualTo("desert_alchemist"));
            DialogueRunner.PlayPendingBeatsHeadless(c, s, "greenvalley-01");
            Assert.That(CampNpcs.Joined(s, CampNpcs.Alchemist), Is.True);
            Assert.That(s.Count(ContentLoader.EmptyBottle), Is.EqualTo(1), "he hands over the first bottle");
            Assert.That(Crafting.HasInputs(s, c.Recipes["recipe_bottle_healing"]), Is.True, "so the first brew is ready");

            var never = Progression.NewGame(c, "T");
            DialogueRunner.PlayPendingBeatsHeadless(c, never, "desert-01");
            Assert.That(CampNpcs.Joined(never, CampNpcs.Alchemist), Is.True, "a party that never loots still meets him at the Sandsea");
        }

        [Test]
        public void Test_mode_saves_start_with_every_npc_joined()
        {
            var s = TestLoop.NewSave(Real);
            Assert.That(Npcs.All(n => CampNpcs.Joined(s, n)));
        }

        [Test]
        public void Old_saves_migrate_with_every_npc_joined_and_new_ones_keep_their_flags()
        {
            var old = Progression.NewGame(Real, "Old");
            old.Version = 3;
            SaveMigrations.Migrate(old);
            Assert.That(Npcs.All(n => CampNpcs.Joined(old, n)), "a v3 save was already using every service");
            Assert.That(old.Version, Is.EqualTo(SaveData.CurrentVersion));

            var fresh = Progression.NewGame(Real, "New");
            fresh.Flags.Add("npc:merchant");
            SaveMigrations.Migrate(fresh);
            Assert.That(Npcs.Where(n => CampNpcs.Joined(fresh, n)), Is.EqualTo(new[] { CampNpcs.Merchant }), "a current save keeps exactly who it has met");
        }

        [Test]
        public void Camp_routine_uses_no_service_whose_npc_has_not_joined()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            s.Gold = 5000;
            s.Hero("paladin").Hp = 10;
            s.Inventory.Remove(CampRoutine.StopgapPotion);
            CampRoutine.Run(c, s);
            Assert.That(s.Hero("paladin").Hp, Is.EqualTo(10), "rested without an Innkeeper");
            Assert.That(s.Count(CampRoutine.StopgapPotion), Is.EqualTo(0), "bought or brewed without a Merchant or Alchemist");
            Assert.That(s.StationXp.Values.Sum(), Is.EqualTo(0), "forged, repaired or salvaged without a Blacksmith");
        }

        [Test]
        public void Camp_routine_uses_each_service_once_its_npc_joins()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            s.Gold = 5000;
            s.Hero("paladin").Hp = 10;
            s.Inventory.Remove(CampRoutine.StopgapPotion);
            s.Flags.Add("npc:" + CampNpcs.Innkeeper);
            CampRoutine.Run(c, s);
            Assert.That(s.Hero("paladin").Hp, Is.EqualTo(0), "the Innkeeper lets it rest");
            Assert.That(s.Count(CampRoutine.StopgapPotion), Is.EqualTo(0), "still no Merchant");
            s.Flags.Add("npc:" + CampNpcs.Merchant);
            CampRoutine.Run(c, s);
            Assert.That(s.Count(CampRoutine.StopgapPotion), Is.GreaterThan(0), "the Merchant sells potions");
        }
    }
}
