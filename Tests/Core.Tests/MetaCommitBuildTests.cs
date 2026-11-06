using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Covers BattleFactory.Commit/Build branches, Expeditions.AfterBattle and CampRoutine.Run
    /// that MetaLoopTests' end-to-end flows don't happen to exercise (GG-T32).</summary>
    public class MetaCommitBuildTests
    {
        private static BattleState DirectWin(ContentDb c, SaveData s, string stageId, int seed = 5)
        {
            var st = BattleEngine.Create(c, BattleFactory.Build(c, s, stageId, seed), new List<BattleEvent>());
            st.Phase = BattlePhase.Victory;
            return st;
        }

        private static BattleState FightWithCoins(ContentDb c, SaveData s, string stageId, int coins)
        {
            Expeditions.Begin(c, s, stageId);
            var st = DirectWin(c, s, stageId, 7);
            st.Results.Coins = coins;
            return st;
        }

        // ── Commit: the report names its stage ──

        /// <summary>GG-T127: the report carries the fought stage's name and theme, so the level-up and results screens
        /// never look a generated stage (a harness skirmish, a Crucible run) back up after it has been pruned.</summary>
        [Test]
        public void A_report_names_its_stage_even_after_a_generated_stage_is_pruned()
        {
            // A private ContentDb: this test registers and prunes stages, which the shared Real must never see.
            var c = ContentLoader.Load(name => { var p = System.IO.Path.Combine(ContentDir, name + ".json"); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null; });
            var s = Progression.NewGame(c, "Skirmish");
            var src = c.CampaignStages()[0];
            var synth = new StageDef { Id = "skirmish-test", Name = src.Name + " Skirmish", Theme = src.Theme, Background = src.Background, LevelOverride = 1, Waves = new List<WaveDef> { src.Waves[0] } };
            c.Stages[synth.Id] = synth;
            var b = DirectWin(c, s, synth.Id);
            c.Stages.Remove(synth.Id); // what randombattle's prune does while the results are still up
            var rep = BattleFactory.Commit(c, s, b);
            Assert.That(rep.StageId, Is.EqualTo(synth.Id));
            Assert.That(rep.StageName, Is.EqualTo(synth.Name));
            Assert.That(rep.StageTheme, Is.EqualTo(src.Theme));

            var crucible = Crucible.Prepare(c, s);
            var cb = BattleEngine.Create(c, BattleFactory.Build(c, s, crucible.Id, 3), new List<BattleEvent>());
            cb.Phase = BattlePhase.Defeat;
            var crep = BattleFactory.Commit(c, s, cb);
            Assert.That(crep.StageName, Is.EqualTo(crucible.Name));
            Assert.That(crep.StageTheme, Is.EqualTo(crucible.Theme));
        }

        // ── Commit: equipment ──

        [Test]
        public void Victory_with_a_final_weapon_swap_returns_the_old_one_and_removes_the_new_one()
        {
            var c = Real; var s = Progression.NewGame(c, "FW");
            var hero = s.Hero(s.Party[0]);
            var weapons = c.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && i.Type == ItemType.Equipment).Take(2).ToList();
            var oldWeapon = weapons[0];
            var newWeapon = weapons[1];
            hero.WeaponId = oldWeapon.Id;
            hero.WeaponDurability = oldWeapon.Durability;
            s.AddItem(newWeapon.Id, 1);

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            st.Results.FinalWeapon[hero.ClassId] = newWeapon.Id;
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.WeaponId, Is.EqualTo(newWeapon.Id));
            Assert.That(hero.WeaponRepairs, Is.EqualTo(0));
            Assert.That(s.Count(oldWeapon.Id), Is.EqualTo(1)); // the old weapon goes back to the bag
            Assert.That(s.Count(newWeapon.Id), Is.EqualTo(0)); // the new one leaves it
        }

        [Test]
        public void Negative_weapon_durability_on_a_broken_unit_clears_the_slot()
        {
            var c = Real; var s = Progression.NewGame(c, "WD");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Durability > 0);
            hero.WeaponId = weapon.Id;
            hero.WeaponDurability = weapon.Durability;

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            var unit = st.Units.First(u => u.IsHero && u.Def.Id == hero.ClassId);
            unit.WeaponId = null; // shattered mid-battle
            st.Results.WeaponDurability[hero.ClassId] = -1;
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.WeaponId, Is.Null);
            Assert.That(hero.WeaponDurability, Is.EqualTo(0));
            Assert.That(hero.WeaponRepairs, Is.EqualTo(0));
        }

        [Test]
        public void Negative_armor_durability_on_a_broken_unit_clears_the_slot()
        {
            var c = Real; var s = Progression.NewGame(c, "AD");
            var hero = s.Hero(s.Party[0]);
            var armor = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.Durability > 0);
            hero.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = armor.Id, Durability = armor.Durability };

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            var unit = st.Units.First(u => u.IsHero && u.Def.Id == hero.ClassId);
            unit.Gear.Remove(EquipmentSlot.Torso); // broke mid-battle
            st.Results.ArmorDurability[hero.ClassId] = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.Torso] = -1 };
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.Armor.ContainsKey(EquipmentSlot.Torso), Is.False);
        }

        [Test] // GG-T41
        public void A_shattered_weapon_becomes_a_broken_fragment_instead_of_vanishing()
        {
            var c = Real; var s = Progression.NewGame(c, "BW");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Durability > 0);
            hero.WeaponId = weapon.Id;
            hero.WeaponDurability = weapon.Durability;

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            var unit = st.Units.First(u => u.IsHero && u.Def.Id == hero.ClassId);
            unit.WeaponId = null; // shattered mid-battle
            st.Results.WeaponDurability[hero.ClassId] = -1;
            st.Results.BrokenWeapons[hero.ClassId] = new List<string> { weapon.Id };
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.WeaponId, Is.Null);
            Assert.That(s.Count(weapon.Id), Is.EqualTo(0)); // not a whole item back in the bag
            Assert.That(s.BrokenCount(weapon.Id), Is.EqualTo(1));
        }

        [Test] // GG-T41
        public void A_shattered_armor_becomes_a_broken_fragment_instead_of_vanishing()
        {
            var c = Real; var s = Progression.NewGame(c, "BA");
            var hero = s.Hero(s.Party[0]);
            var armor = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.Durability > 0);
            hero.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = armor.Id, Durability = armor.Durability };

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            var unit = st.Units.First(u => u.IsHero && u.Def.Id == hero.ClassId);
            unit.Gear.Remove(EquipmentSlot.Torso); // broke mid-battle
            st.Results.ArmorDurability[hero.ClassId] = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.Torso] = -1 };
            st.Results.BrokenArmors[hero.ClassId] = new List<string> { armor.Id };
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.Armor.ContainsKey(EquipmentSlot.Torso), Is.False);
            Assert.That(s.Count(armor.Id), Is.EqualTo(0));
            Assert.That(s.BrokenCount(armor.Id), Is.EqualTo(1));
        }

        [Test] // GG-T41
        public void A_weapon_that_shattered_and_had_a_bar_backup_auto_equipped_does_not_also_return_whole_to_the_bag()
        {
            var c = Real; var s = Progression.NewGame(c, "SW");
            var hero = s.Hero(s.Party[0]);
            var weapons = c.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && i.Type == ItemType.Equipment && i.Durability > 0).Take(2).ToList();
            var oldWeapon = weapons[0];
            var backup = weapons[1];
            hero.WeaponId = oldWeapon.Id;
            hero.WeaponDurability = oldWeapon.Durability;
            s.AddItem(backup.Id, 1);

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            // oldWeapon shattered mid-battle and the bar backup auto-equipped in its place.
            st.Results.BrokenWeapons[hero.ClassId] = new List<string> { oldWeapon.Id };
            st.Results.FinalWeapon[hero.ClassId] = backup.Id;
            st.Results.WeaponDurability[hero.ClassId] = backup.Durability;
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.WeaponId, Is.EqualTo(backup.Id));
            Assert.That(s.Count(oldWeapon.Id), Is.EqualTo(0)); // the shattered one never returns to the bag whole
            Assert.That(s.BrokenCount(oldWeapon.Id), Is.EqualTo(1));
            Assert.That(s.Count(backup.Id), Is.EqualTo(0)); // the backup left the bag onto the hero
        }

        [Test] // GG-T70
        public void A_bar_weapon_that_was_swapped_in_and_shattered_does_not_stay_whole_in_the_bag()
        {
            var c = Real; var s = Progression.NewGame(c, "SW2");
            var hero = s.Hero(s.Party[0]);
            var weapons = c.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && i.Type == ItemType.Equipment && i.Durability > 0).Take(2).ToList();
            var main = weapons[0];
            var spare = weapons[1];
            hero.WeaponId = main.Id;
            hero.WeaponDurability = main.Durability;
            s.AddItem(spare.Id, 1);

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            // Swapped to the spare, the spare shattered, and the main weapon auto-equipped back from the bar.
            st.Results.BrokenWeapons[hero.ClassId] = new List<string> { spare.Id };
            st.Results.FinalWeapon[hero.ClassId] = main.Id;
            st.Results.WeaponDurability[hero.ClassId] = main.Durability;
            BattleFactory.Commit(c, s, st);

            Assert.That(hero.WeaponId, Is.EqualTo(main.Id));
            Assert.That(s.Count(spare.Id), Is.EqualTo(0)); // not both a Broken fragment and a whole copy
            Assert.That(s.BrokenCount(spare.Id), Is.EqualTo(1));
            Assert.That(s.Count(main.Id), Is.EqualTo(0)); // still in hand, not also back in the bag
        }

        [Test] // GG-T70
        public void A_hero_whose_start_tile_is_a_block_spawns_on_a_walkable_tile()
        {
            var c = Real; var s = Progression.NewGame(c, "BL");
            var setup = BattleFactory.Build(c, s, c.CampaignStages()[0].Id, 3);
            var start = new Coord(1, 6);
            setup.Party[0].Start = start;
            setup.Stage = new StageDef { Id = "_blocked", IsTest = true, Blocks = { start }, Waves = setup.Stage.Waves };
            var st = BattleEngine.Create(c, setup, new List<BattleEvent>());
            foreach (var h in st.Units.Where(u => u.IsHero)) Assert.That(st.Walkable(h.Pos), Is.True, $"{h} spawned on {h.Pos}");
        }

        [Test] // GG-T41
        public void Broken_fragments_cannot_be_equipped_and_salvaging_one_grants_materials_and_blacksmith_xp()
        {
            var c = Real; var s = Progression.NewGame(c, "SB2");
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Durability > 0);
            s.AddBroken(weapon.Id, 1);

            // A Broken fragment never shows up as an equip candidate — it was never added to Inventory.
            var hero = s.Hero(s.Party[0]);
            Assert.That(s.Count(weapon.Id), Is.EqualTo(0));
            Assert.That(Equipment.Candidates(c, s, EquipmentSlot.Weapon), Does.Not.Contain(weapon));

            var res = Crafting.SalvageBroken(c, s, weapon.Id);
            Assert.That(res.Ok, Is.True, res.Message);
            Assert.That(s.BrokenCount(weapon.Id), Is.EqualTo(0));
            Assert.That(StationProgress.Xp(s, StationKind.Blacksmith), Is.EqualTo(Crafting.SalvageXp));

            Assert.That(Crafting.SalvageBroken(c, s, weapon.Id).Ok, Is.False); // nothing left to salvage
        }

        // ── Commit: XP, HP, progression ──

        [Test]
        public void A_level_up_refreshes_hp_to_full_while_a_fallen_hero_revives_at_1_hp()
        {
            var c = Real; var s = Progression.NewGame(c, "LV");
            var leveler = s.Hero(s.Party[0]);
            var fallen = s.Hero(s.Party[1]);
            leveler.TotalXp = 55; // 5 short of levelling at 60 (Formulas.XpToNext(1))
            fallen.TotalXp = 0;

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            st.Results.Xp = 10; // pushes the leveler over, not the other
            var fallenUnit = st.Units.First(u => u.IsHero && u.Def.Id == fallen.ClassId);
            fallenUnit.Hp = 0;
            BattleFactory.Commit(c, s, st);

            Assert.That(Progression.Level(leveler), Is.EqualTo(2));
            Assert.That(leveler.Hp, Is.EqualTo(0)); // 0 = full
            Assert.That(Progression.Level(fallen), Is.EqualTo(1));
            Assert.That(fallen.Hp, Is.EqualTo(1));
        }

        [Test] // GG-T49
        public void Leveling_up_in_battle_grants_skill_points_including_the_milestone_bonus()
        {
            var c = Real; var s = Progression.NewGame(c, "SP");
            var leveler = s.Hero(s.Party[0]);
            leveler.TotalXp = Formulas.TotalXpForLevel(5) - 5; // level 4, 5 XP short of hitting level 5 (a milestone)
            leveler.SkillPoints = 0;

            var st = DirectWin(c, s, c.CampaignStages()[0].Id);
            st.Results.Xp = 10; // pushes the leveler across the level-5 milestone
            BattleFactory.Commit(c, s, st);

            Assert.That(Progression.Level(leveler), Is.EqualTo(5));
            Assert.That(leveler.SkillPoints, Is.EqualTo(Formulas.SkillPointsForLevelUp(4, 5)));
            Assert.That(leveler.SkillPoints, Is.EqualTo(2)); // 1 per level + the milestone bonus
        }

        [Test]
        public void FirstClear_is_true_only_on_the_first_victory_of_a_stage()
        {
            var c = Real; var s = Progression.NewGame(c, "FC");
            var stageId = c.CampaignStages()[0].Id;
            var first = BattleFactory.Commit(c, s, DirectWin(c, s, stageId));
            var second = BattleFactory.Commit(c, s, DirectWin(c, s, stageId));
            Assert.That(first.FirstClear, Is.True);
            Assert.That(second.FirstClear, Is.False);
        }

        [Test]
        public void Replaying_an_earlier_stage_never_lowers_HighestClearedStage()
        {
            var c = Real; var s = Progression.NewGame(c, "HC");
            var stages = c.CampaignStages();
            BattleFactory.Commit(c, s, DirectWin(c, s, stages[2].Id));
            Assert.That(s.HighestClearedStage, Is.EqualTo(2));
            BattleFactory.Commit(c, s, DirectWin(c, s, stages[0].Id));
            Assert.That(s.HighestClearedStage, Is.EqualTo(2));
        }

        [Test]
        public void CampaignComplete_is_true_only_on_the_last_stage()
        {
            var c = Real; var s = Progression.NewGame(c, "CC");
            var stages = c.CampaignStages();
            var early = BattleFactory.Commit(c, s, DirectWin(c, s, stages[0].Id));
            var last = BattleFactory.Commit(c, s, DirectWin(c, s, stages[stages.Count - 1].Id));
            Assert.That(early.CampaignComplete, Is.False);
            Assert.That(last.CampaignComplete, Is.True);
        }

        [Test]
        public void Commit_reports_the_newly_unlocked_stages_name_once()
        {
            var c = Real; var s = Progression.NewGame(c, "US");
            var stages = c.CampaignStages();
            var first = BattleFactory.Commit(c, s, DirectWin(c, s, stages[0].Id));
            var replay = BattleFactory.Commit(c, s, DirectWin(c, s, stages[0].Id));
            Assert.That(first.UnlockedStage, Is.EqualTo(stages[1].Name));
            Assert.That(replay.UnlockedStage, Is.Null);
        }

        // ── Build ──

        [Test]
        public void Build_excludes_roster_heroes_from_appearing_as_guests_but_never_a_fallen_guest()
        {
            var c = Real; var s = Progression.NewGame(c, "EX");
            s.LostGuests.Add("drifter"); // an old save's "lost forever" guest — permadeath is off, so they come back (GG-T114)
            var setup = BattleFactory.Build(c, s, c.CampaignStages()[0].Id, 1);
            foreach (var h in s.Roster) Assert.That(setup.ExcludedGuests, Does.Contain(h.ClassId));
            Assert.That(setup.ExcludedGuests, Does.Not.Contain("drifter"));
        }

        [Test]
        public void Build_honours_a_stages_level_override()
        {
            var c = Real; var s = Progression.NewGame(c, "LO");
            var overridden = c.CampaignStages().First(x => x.LevelOverride.HasValue);
            var setup = BattleFactory.Build(c, s, overridden.Id, 1);
            Assert.That(setup.RecommendedLevel, Is.EqualTo(overridden.LevelOverride.Value));
        }

        [Test]
        public void Build_adds_mana_casks_to_start_orbs()
        {
            var c = Real; var s = Progression.NewGame(c, "MC");
            s.Gold = 10000;
            var stageId = c.CampaignStages()[0].Id;
            int before = BattleFactory.Build(c, s, stageId, 1).StartOrbs;
            Assert.That(CaravanUpgrades.Buy(c, s, "mana_casks").Ok, Is.True);
            Assert.That(BattleFactory.Build(c, s, stageId, 1).StartOrbs, Is.EqualTo(before + 1));
        }

        // ── Expeditions ──

        [Test]
        public void AfterBattle_bonus_includes_the_strongbox_upgrade()
        {
            var c = Real; var s = Progression.NewGame(c, "SB");
            s.Gold = 10000;
            Assert.That(CaravanUpgrades.Buy(c, s, "strongbox").Ok, Is.True);
            var theme = c.Campaign.Themes[0];
            BattleReport last = null;
            int totalCoins = 0;
            foreach (var id in theme.Stages)
            {
                last = BattleFactory.Commit(c, s, FightWithCoins(c, s, id, 100));
                totalCoins += 100;
            }
            Assert.That(last.ExpeditionComplete, Is.True);
            int expected = (int)System.Math.Round(totalCoins * (Expeditions.CompletionBonus + CaravanUpgrades.Value(c, s, "strongbox")));
            Assert.That(last.ExpeditionBonus, Is.EqualTo(expected));
        }

        [Test]
        public void StartOrbs_is_zero_for_a_stage_outside_the_active_expeditions_theme()
        {
            var c = Real; var s = Progression.NewGame(c, "OT");
            var theme0 = c.Campaign.Themes[0];
            var theme1 = c.Campaign.Themes[1];
            Expeditions.Begin(c, s, theme0.Stages[0]);
            s.Expedition.CarriedOrbs = 5;
            Assert.That(Expeditions.StartOrbs(c, s, theme1.Stages[0]), Is.EqualTo(0));
        }

        [Test]
        public void Abandon_fails_when_no_expedition_is_active()
        {
            var c = Real; var s = Progression.NewGame(c, "AB");
            Assert.That(Expeditions.Abandon(s).Ok, Is.False);
        }

        // ── CampRoutine ──

        [Test]
        public void CampRoutine_never_spends_below_zero_gold()
        {
            var c = Real; var s = Progression.NewGame(c, "CR"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.Gold = 50;
            for (int i = 0; i < 20; i++)
            {
                CampRoutine.Run(c, s);
                Assert.That(s.Gold >= 0, Is.True);
                s.Gold += 37; // trickle gold in between runs to reach different affordability thresholds
            }
        }

        /// <summary>GG-T44: with reagents in the bag, restocking potions should brew them (cheap, from
        /// drops) instead of buying from the Merchant at full shop price.</summary>
        [Test]
        public void CampRoutine_brews_potions_instead_of_buying_when_it_has_the_reagents()
        {
            var c = Real; var s = Progression.NewGame(c, "CRBrew"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.Inventory.Remove("healing_potion_basic");
            s.Gold = 5000;
            s.AddItem("mat_slime_gel", 20); // enough reagents to brew every potion needed, never enough to need to buy
            CampRoutine.Run(c, s);
            Assert.That(s.Count("healing_potion_basic") >= 6, Is.True);
            Assert.That(s.Count("mat_slime_gel") < 20, Is.True, "reagents weren't spent — camp routine bought potions instead of brewing them");
        }

        /// <summary>GG-T111: the potion-restock loop used to ignore a failed brew/buy and spin forever. Here every
        /// restock call fails (the brew always overflows the stack, the shop cart totals 0g → "Nothing to buy")
        /// while the loop's pre-checks still pass; Run must give up and return.</summary>
        [Test]
        public void CampRoutine_terminates_when_every_potion_restock_fails()
        {
            var c = ContentLoader.Load(name =>
            {
                var p = System.IO.Path.Combine(ContentDir, name + ".json");
                return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null;
            });
            var potion = c.Item(CampRoutine.StopgapPotion);
            potion.BaseCost = 0;
            var brew = c.Recipes.Values.First(r => r.Station == CraftStation.Alchemy && r.Result == potion.Id);
            brew.ResultCount = potion.MaxStack + 1;
            var s = Progression.NewGame(c, "CRFail"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.Inventory.Remove(potion.Id);
            s.Gold = 5000;
            foreach (var ic in brew.Inputs) s.AddItem(ic.Item, ic.Count * 10);
            Assert.That(Crafting.IsUnlocked(s, brew), Is.True, "precondition: the brew must look possible so the loop tries it");

            List<string> log = null;
            var run = System.Threading.Tasks.Task.Run(() => log = CampRoutine.Run(c, s));
            Assert.That(run.Wait(System.TimeSpan.FromSeconds(20)), Is.True, "CampRoutine.Run never returned — the restock loop spun on failed calls");
            Assert.That(s.Count(potion.Id), Is.EqualTo(0));
            Assert.That(log, Is.Not.Null);
        }

        /// <summary>GG-T54: Broken fragments (GG-T41) can't be equipped or repaired, so a camp visit should
        /// always scrap them for materials instead of letting them sit in the bag forever.</summary>
        [Test]
        public void CampRoutine_salvages_broken_fragments_on_every_visit()
        {
            var c = Real; var s = Progression.NewGame(c, "CRBroken"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.AddBroken("eq_sword_iron", 2);
            CampRoutine.Run(c, s);
            Assert.That(s.BrokenCount("eq_sword_iron"), Is.EqualTo(0));
            Assert.That(s.Count("mat_iron_ore") > 0 || s.Count("mat_wood_plank") > 0, Is.True, "salvaging a Broken fragment should have yielded its materials");
        }

        /// <summary>GG-T54: once a repair costs as much as the piece is worth (GG-T42's "Salvage instead"
        /// threshold), the camp routine should scrap it for materials rather than pour gold into a piece
        /// that's about to shatter again anyway.</summary>
        [Test]
        public void CampRoutine_salvages_an_equipped_piece_instead_of_an_uneconomical_repair()
        {
            var c = Real; var s = Progression.NewGame(c, "CRSalvageEquip"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            var h = s.Hero(s.Party[0]);
            h.WeaponId = "eq_sword_iron";
            h.WeaponDurability = 0;
            h.WeaponRepairs = 5; // repair cost grows 1.6x per prior repair — comfortably past the BaseCost ceiling here
            s.Gold = 100000;
            var log = CampRoutine.Run(c, s);
            Assert.That(log.Any(m => m.Contains("Salvaged") && m.Contains("Iron Sword")), Is.True);
            Assert.That(log.Any(m => m.Contains("Repaired Iron Sword")), Is.False);
        }

        /// <summary>GG-T54 (owner direction): the Bargain shelf's cheap gear is a deliberate player gamble
        /// (10-40% durability left, real risk of shattering before it can be repaired) — an automated buyer
        /// can't judge that risk, so it must never take an offer below the safety threshold.</summary>
        [Test]
        public void CampRoutine_never_buys_a_bargain_offer_too_close_to_shattering()
        {
            var c = Real;
            BargainOffer risky = null;
            int riskyRotation = -1;
            for (int rot = 0; rot < 500 && risky == null; rot++)
            {
                var probe = new SaveData { BargainRotation = rot };
                var offer = Bargains.Shelf(c, probe).FirstOrDefault(o => o.Durability < o.Item.Durability * CampRoutine.MinSafeBargainDurabilityFraction);
                if (offer != null) { risky = offer; riskyRotation = rot; }
            }
            Assert.That(risky != null, Is.True, "expected at least one near-shatter offer within 500 rotations");

            var s = Progression.NewGame(c, "CRBargainRisky"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.BargainRotation = riskyRotation;
            s.Gold = 100000;
            foreach (var h in s.Roster) { h.WeaponId = null; h.ArmorId = null; }
            CampRoutine.Run(c, s);
            Assert.That(s.Count(risky.Item.Id), Is.EqualTo(0));
        }

        /// <summary>GG-T87: with an open party slot and the gold for it, a camp visit recruits from the
        /// Summon Circle (and the recruit lands in the party, not the reserve).</summary>
        [Test]
        public void CampRoutine_recruits_into_an_open_party_slot_when_affordable()
        {
            var c = Real; var s = Progression.NewGame(c, "CRSummon"); CampNpcs.JoinAll(s); // the small start leaves slots open (GG-T118); every Camp service open (GG-T119)
            s.Gold = Summon.Price(c, s);
            CampRoutine.Run(c, s);
            Assert.That(s.Recruits, Is.EqualTo(1));
            Assert.That(s.Party.Count, Is.EqualTo(4));
        }

        /// <summary>GG-T87: a full party leaves no slot, so the camp routine doesn't spend on a reserve recruit.</summary>
        [Test]
        public void CampRoutine_does_not_recruit_with_a_full_party()
        {
            var c = Real; var s = Progression.NewGame(c, "CRSummonFull", new[] { "paladin", "barbarian", "cleric", "redmage", "engineer", "knight" }); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.Gold = 100000;
            CampRoutine.Run(c, s);
            Assert.That(s.Recruits, Is.EqualTo(0));
        }

        /// <summary>GG-T87: a finished bounty is claimed, and the next accepted contract is one the stages
        /// still ahead can actually complete — never Slime Culling, whose 5 kills outnumber every slime00 the
        /// campaign spawns.</summary>
        [Test]
        public void CampRoutine_claims_a_finished_bounty_and_accepts_only_an_achievable_one()
        {
            var c = Real; var s = Progression.NewGame(c, "CRBounty"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            CampRoutine.Run(c, s);
            Assert.That(s.Bounty.ActiveId, Is.Not.Null.And.Not.EqualTo("bounty_slime_culling"));
            var first = Bounties.Active(c, s);
            s.Bounty.Progress = first.Kills;
            CampRoutine.Run(c, s);
            Assert.That(s.Bounty.Claimed, Does.Contain(first.Id));
            Assert.That(s.Bounty.ActiveId, Is.Not.Null.And.Not.EqualTo(first.Id));
        }

        /// <summary>GG-T54: banked skill points go into the hero's home tree, deepening one branch, and nothing
        /// leaks off-tree while the home tree still has a node to buy.</summary>
        [Test]
        public void CampRoutine_spends_skill_points_in_the_home_tree_first()
        {
            var c = Real; var s = Progression.NewGame(c, "CRTalents"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            var h = s.Hero(s.Party[0]);
            var home = Talents.HomeTree(c, h);
            Assert.That(home, Is.Not.Null, "precondition: the hero has a home tree");
            h.SkillPoints = 3;
            var log = CampRoutine.Run(c, s);
            Assert.That(Talents.PointsSpent(c, h), Is.GreaterThan(0), "the routine should have spent banked points");
            Assert.That(h.TreeNodes.All(k => k.StartsWith(home.Id + "/")), Is.True, "every point should land in the home tree first");
            Assert.That(log.Any(m => m.Contains(CampRoutine.TalentMarker)), Is.True);
            // Deepening beats spreading: the second purchase follows up the first branch rather than opening another.
            var bought = home.Nodes.Where(n => Talents.Bought(h, home, n)).ToList();
            Assert.That(bought.Count(n => n.Prerequisites.Count == 0 && n.RequiredPointsInTree > 0), Is.EqualTo(1), "only one branch opener bought");
        }

        /// <summary>GG-T54: a hero whose next home-tree node costs more than it holds banks the points for it.</summary>
        [Test]
        public void CampRoutine_banks_points_for_a_home_node_it_cannot_afford_yet()
        {
            var c = Real; var s = Progression.NewGame(c, "CRTalentsBank"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            var h = s.Hero(s.Party[0]);
            var home = Talents.HomeTree(c, h);
            for (int sp = 1; sp <= 6; sp++)
            {
                h.SkillPoints += sp;
                CampRoutine.Run(c, s);
                var next = CampRoutine.NextTalent(h, home);
                // Points left over only ever mean the home tree's next pick is dearer than the balance — never a skipped buy
                // or a point spent off-tree.
                if (h.SkillPoints > 0) Assert.That(next != null && next.Cost > h.SkillPoints, Is.True, $"{h.SkillPoints} SP left with an affordable home node waiting");
                Assert.That(h.TreeNodes.All(k => k.StartsWith(home.Id + "/")), Is.True);
            }
        }

        /// <summary>GG-T54: a Bargain offer at or above the safety threshold is still a legitimate cheap
        /// upgrade the camp routine should take for an empty slot.</summary>
        [Test]
        public void CampRoutine_buys_a_safe_bargain_offer_for_an_empty_slot()
        {
            var c = Real;
            BargainOffer safe = null;
            int safeRotation = -1;
            for (int rot = 0; rot < 500 && safe == null; rot++)
            {
                var probe = new SaveData { BargainRotation = rot };
                var offer = Bargains.Shelf(c, probe).FirstOrDefault(o => o.Item.Slot == EquipmentSlot.Weapon && o.Durability >= o.Item.Durability * CampRoutine.MinSafeBargainDurabilityFraction);
                if (offer != null) { safe = offer; safeRotation = rot; }
            }
            Assert.That(safe != null, Is.True, "expected at least one safe weapon offer within 500 rotations");

            var s = Progression.NewGame(c, "CRBargainSafe"); CampNpcs.JoinAll(s); // every Camp service open (GG-T119)
            s.BargainRotation = safeRotation;
            s.Gold = 100000;
            foreach (var h in s.Roster) h.WeaponId = null;
            CampRoutine.Run(c, s);
            var hero = s.Hero(s.Party[0]);
            Assert.That(hero.WeaponId, Is.Not.Null);
        }
    }
}
