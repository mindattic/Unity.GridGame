using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Camp-screen edge cases (GG-T31): equip/unequip, the ability bar, learning, and new-game/progression rules.</summary>
    public class LoadoutAndProgressionTests
    {
        // ── Equipment.Fits ──

        [Test]
        public void Fits_rejects_non_equipment_and_null()
        {
            var c = Real;
            var material = c.Items.Values.First(i => i.Type == ItemType.CraftingMaterial);
            Assert.That(Equipment.Fits(material, EquipmentSlot.Weapon), Is.False);
            Assert.That(Equipment.Fits(null, EquipmentSlot.Ring1), Is.False);
        }

        [Test]
        public void Fits_allows_a_ring_only_into_either_ring_slot()
        {
            var c = Real;
            var ring = c.Items.Values.First(i => i.IsRing);
            Assert.That(Equipment.Fits(ring, EquipmentSlot.Ring1), Is.True);
            Assert.That(Equipment.Fits(ring, EquipmentSlot.Ring2), Is.True);
            Assert.That(Equipment.Fits(ring, EquipmentSlot.Earring1), Is.False);
            Assert.That(Equipment.Fits(ring, EquipmentSlot.Weapon), Is.False);
            Assert.That(Equipment.Fits(ring, EquipmentSlot.Torso), Is.False);
        }

        [Test]
        public void Fits_allows_an_earring_only_into_either_earring_slot()
        {
            var c = Real;
            var earring = c.Items.Values.First(i => i.IsEarring);
            Assert.That(Equipment.Fits(earring, EquipmentSlot.Earring1), Is.True);
            Assert.That(Equipment.Fits(earring, EquipmentSlot.Earring2), Is.True);
            Assert.That(Equipment.Fits(earring, EquipmentSlot.Ring1), Is.False);
        }

        [Test]
        public void Fits_matches_weapons_and_armor_to_their_own_slot_only()
        {
            var c = Real;
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon);
            var armor = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso);
            Assert.That(Equipment.Fits(weapon, EquipmentSlot.Weapon), Is.True);
            Assert.That(Equipment.Fits(weapon, EquipmentSlot.Torso), Is.False);
            Assert.That(Equipment.Fits(weapon, EquipmentSlot.Ring1), Is.False);
            Assert.That(Equipment.Fits(armor, EquipmentSlot.Torso), Is.True);
            Assert.That(Equipment.Fits(armor, EquipmentSlot.Weapon), Is.False);
        }

        // ── Equipment.Equip / Unequip ──

        [Test]
        public void Equip_rejects_an_item_that_does_not_fit_the_slot()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq1");
            var hero = s.Hero(s.Party[0]);
            var armor = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso);
            s.AddItem(armor.Id, 1);
            var result = Equipment.Equip(c, s, hero, EquipmentSlot.Weapon, armor.Id);
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.WeaponId, Is.Null);
            Assert.That(s.Count(armor.Id), Is.EqualTo(1)); // nothing consumed on a rejected equip
        }

        [Test]
        public void Equip_rejects_an_item_the_player_does_not_own()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq2");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon);
            Assert.That(s.Count(weapon.Id), Is.EqualTo(0));
            var result = Equipment.Equip(c, s, hero, EquipmentSlot.Weapon, weapon.Id);
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.WeaponId, Is.Null);
        }

        [Test]
        public void Unequip_an_empty_slot_fails()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq3");
            var hero = s.Hero(s.Party[0]);
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Ring1), Is.Null);
            Assert.That(Equipment.Unequip(c, s, hero, EquipmentSlot.Ring1).Ok, Is.False);
        }

        [Test]
        public void Compare_previews_stats_before_and_after_equipping_a_candidate()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq4");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Stats.Strength > 0);
            var (now, then) = Equipment.Compare(c, hero, EquipmentSlot.Weapon, weapon.Id);
            Assert.That(hero.WeaponId, Is.Null); // Compare previews only; it doesn't mutate the hero
            Assert.That(then.Strength, Is.Not.EqualTo(now.Strength));
        }

        /// <summary>A Masterwork weapon's stat bonus must carry into the "then" preview unchanged when the slot
        /// being compared is a different one (armor): Compare's scratch copy used to drop WeaponMastery/ArmorMastery
        /// for every slot except the one being swapped, understating the preview for any hero with Masterwork gear.</summary>
        [Test]
        public void Compare_keeps_the_other_equipped_slots_masterwork_bonus()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq5");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Stats.Strength > 0);
            s.AddItem(weapon.Id, 1);
            Equipment.Equip(c, s, hero, EquipmentSlot.Weapon, weapon.Id);
            hero.WeaponMastery = 1.2f; // simulate an equipped Masterwork weapon
            var armor = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.Stats.Strength == 0);
            var (now, then) = Equipment.Compare(c, hero, EquipmentSlot.Torso, armor.Id);
            Assert.That(then.Strength, Is.EqualTo(now.Strength)); // swapping armor must not touch the weapon's own bonus
        }

        /// <summary>GG-T89: Head/Torso/Legs/Gloves/Boots are five independent slots — equipping one must not
        /// disturb another, and each tracks its own durability.</summary>
        [Test]
        public void Equip_fills_each_armor_slot_independently()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq6");
            var hero = s.Hero(s.Party[0]);
            var head = c.Items.Values.First(i => i.Slot == EquipmentSlot.Head);
            var torso = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso);
            s.AddItem(head.Id, 1);
            s.AddItem(torso.Id, 1);
            Assert.That(Equipment.Equip(c, s, hero, EquipmentSlot.Head, head.Id).Ok, Is.True);
            Assert.That(Equipment.Equip(c, s, hero, EquipmentSlot.Torso, torso.Id).Ok, Is.True);
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Head), Is.EqualTo(head.Id));
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Torso), Is.EqualTo(torso.Id));
            Assert.That(Equipment.Unequip(c, s, hero, EquipmentSlot.Head).Ok, Is.True);
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Head), Is.Null);
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Torso), Is.EqualTo(torso.Id)); // untouched
        }

        /// <summary>Ring1 and Ring2 are independent instances of the same category — two different rings can be
        /// worn at once (GG-T89), the same way the old Relic1-3 let you mix and match.</summary>
        [Test]
        public void Equip_fills_both_ring_slots_with_different_rings()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Eq7");
            var hero = s.Hero(s.Party[0]);
            var rings = c.Items.Values.Where(i => i.IsRing).Take(2).ToList();
            Assert.That(rings.Count, Is.EqualTo(2), "content needs at least 2 ring items for this test");
            s.AddItem(rings[0].Id, 1);
            s.AddItem(rings[1].Id, 1);
            Assert.That(Equipment.Equip(c, s, hero, EquipmentSlot.Ring1, rings[0].Id).Ok, Is.True);
            Assert.That(Equipment.Equip(c, s, hero, EquipmentSlot.Ring2, rings[1].Id).Ok, Is.True);
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Ring1), Is.EqualTo(rings[0].Id));
            Assert.That(Equipment.Equipped(hero, EquipmentSlot.Ring2), Is.EqualTo(rings[1].Id));
        }

        // ── Proficiency gating (GG-T52): armor weight + weapon class ──

        [Test]
        public void EffectiveWeaponProficiencies_always_includes_Simple_plus_the_class_default()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric"); // no WeaponProficiencies in content: Simple only
            var barbarian = Progression.NewHero(c, "barbarian"); // warrior tree: Martial by default
            Assert.That(Progression.EffectiveWeaponProficiencies(c, cleric), Is.EquivalentTo(new[] { WeaponClass.Simple }));
            Assert.That(Progression.EffectiveWeaponProficiencies(c, barbarian), Is.EquivalentTo(new[] { WeaponClass.Simple, WeaponClass.Martial }));
        }

        [Test]
        public void EffectiveWeaponProficiencies_grows_once_the_unlock_passive_is_known()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric");
            cleric.Known.Add("martial_weapons_proficiency");
            Assert.That(Progression.EffectiveWeaponProficiencies(c, cleric), Does.Contain(WeaponClass.Martial));
            cleric.Known.Add("arcane_focus_proficiency");
            Assert.That(Progression.EffectiveWeaponProficiencies(c, cleric), Does.Contain(WeaponClass.Arcane));
        }

        [Test]
        public void EffectiveArmorProficiency_defaults_to_Heavy_for_the_tank_archetype_and_Medium_elsewhere()
        {
            var c = Real;
            var paladin = Progression.NewHero(c, "paladin"); // guardian (Tank) tree
            var cleric = Progression.NewHero(c, "cleric");
            Assert.That(Progression.EffectiveArmorProficiency(c, paladin), Is.EqualTo(ArmorWeight.Heavy));
            Assert.That(Progression.EffectiveArmorProficiency(c, cleric), Is.EqualTo(ArmorWeight.Medium));
        }

        [Test]
        public void EffectiveArmorProficiency_jumps_straight_to_Heavy_once_the_unlock_passive_is_known()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric");
            Assert.That(Progression.EffectiveArmorProficiency(c, cleric), Is.LessThan(ArmorWeight.Heavy));
            cleric.Known.Add("heavy_armor_proficiency");
            Assert.That(Progression.EffectiveArmorProficiency(c, cleric), Is.EqualTo(ArmorWeight.Heavy));
        }

        [Test]
        public void ProficiencyGate_locks_a_Martial_weapon_for_a_Simple_only_class_and_names_the_unlock()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            var gate = Equipment.ProficiencyGate(c, cleric, sword);
            Assert.That(gate, Is.EqualTo("Martial Weapons"));
            Assert.That(Equipment.Fits(c, cleric, sword, EquipmentSlot.Weapon), Is.False);
        }

        [Test]
        public void ProficiencyGate_locks_Heavy_armor_for_a_non_tank_class_and_names_the_unlock()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric");
            var plate = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.ArmorWeight == ArmorWeight.Heavy);
            var gate = Equipment.ProficiencyGate(c, cleric, plate);
            Assert.That(gate, Is.EqualTo("Heavy Armor Proficiency"));
            Assert.That(Equipment.Fits(c, cleric, plate, EquipmentSlot.Torso), Is.False);
        }

        [Test]
        public void ProficiencyGate_is_null_once_proficient_either_by_class_default_or_by_passive()
        {
            var c = Real;
            var barbarian = Progression.NewHero(c, "barbarian");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            Assert.That(Equipment.ProficiencyGate(c, barbarian, sword), Is.Null); // warrior tree: Martial by default

            var cleric = Progression.NewHero(c, "cleric");
            Assert.That(Equipment.ProficiencyGate(c, cleric, sword), Is.Not.Null);
            cleric.Known.Add("martial_weapons_proficiency");
            Assert.That(Equipment.ProficiencyGate(c, cleric, sword), Is.Null); // earned it
        }

        [Test]
        public void ProficiencyGate_never_gates_Simple_weapons_or_Light_Medium_armor()
        {
            var c = Real;
            var cleric = Progression.NewHero(c, "cleric");
            var dagger = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Simple);
            var leather = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.ArmorWeight == ArmorWeight.Light);
            Assert.That(Equipment.ProficiencyGate(c, cleric, dagger), Is.Null);
            Assert.That(Equipment.ProficiencyGate(c, cleric, leather), Is.Null);
        }

        [Test]
        public void Equip_refuses_gear_the_hero_is_not_proficient_in_and_names_the_unlock_in_the_message()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Prof1");
            var cleric = s.Hero("cleric");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            s.AddItem(sword.Id, 1);
            var result = Equipment.Equip(c, s, cleric, EquipmentSlot.Weapon, sword.Id);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("Martial Weapons"));
            Assert.That(cleric.WeaponId, Is.Null);
            Assert.That(s.Count(sword.Id), Is.EqualTo(1)); // nothing consumed on a rejected equip
        }

        [Test]
        public void Equip_succeeds_once_the_hero_has_earned_the_matching_proficiency()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Prof2");
            var cleric = s.Hero("cleric");
            cleric.Known.Add("martial_weapons_proficiency");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            s.AddItem(sword.Id, 1);
            Assert.That(Equipment.Equip(c, s, cleric, EquipmentSlot.Weapon, sword.Id).Ok, Is.True);
            Assert.That(cleric.WeaponId, Is.EqualTo(sword.Id));
        }

        [Test]
        public void Candidates_shape_only_still_lists_a_locked_piece_so_the_Equip_screen_can_show_it()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Prof3");
            var cleric = s.Hero("cleric");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            s.AddItem(sword.Id, 1);
            Assert.That(Equipment.Candidates(c, s, EquipmentSlot.Weapon).Select(i => i.Id), Does.Contain(sword.Id));
            Assert.That(Equipment.Candidates(c, s, EquipmentSlot.Weapon, cleric).Select(i => i.Id), Does.Not.Contain(sword.Id));
        }

        /// <summary>GG-T52: a save written before gear gating existed may already have a hero wearing something
        /// their class isn't nominally trained for; migration must grandfather the proficiency in rather than
        /// strip the gear out from under a returning player.</summary>
        [Test]
        public void SaveMigrations_grandfathers_a_proficiency_for_already_equipped_gear()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Prof4");
            var cleric = s.Hero("cleric");
            var sword = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.WeaponClass == WeaponClass.Martial);
            var plate = c.Items.Values.First(i => i.Slot == EquipmentSlot.Torso && i.ArmorWeight == ArmorWeight.Heavy);
            // Simulate a pre-GG-T52 save: gear equipped directly, bypassing today's Equipment.Equip gate.
            cleric.WeaponId = sword.Id;
            cleric.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = plate.Id };
            Assert.That(Equipment.ProficiencyGate(c, cleric, sword), Is.Not.Null);
            Assert.That(Equipment.ProficiencyGate(c, cleric, plate), Is.Not.Null);

            SaveMigrations.Migrate(s, c);

            Assert.That(cleric.Known, Does.Contain("martial_weapons_proficiency"));
            Assert.That(cleric.Known, Does.Contain("heavy_armor_proficiency"));
            Assert.That(Equipment.ProficiencyGate(c, cleric, sword), Is.Null);
            Assert.That(Equipment.ProficiencyGate(c, cleric, plate), Is.Null);
        }

        [Test]
        public void SaveMigrations_without_content_skips_the_grandfather_clause_instead_of_throwing()
        {
            var s = new SaveData();
            s.Roster.Add(new HeroSave { ClassId = "cleric", WeaponId = "eq_sword_iron" });
            Assert.DoesNotThrow(() => SaveMigrations.Migrate(s));
        }

        // ── Abilities.Assign ──

        [Test]
        public void Assign_rejects_an_out_of_range_slot()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As1");
            var hero = s.Hero(s.Party[0]);
            Assert.That(Abilities.Assign(c, s, hero, -1, null).Ok, Is.False);
            Assert.That(Abilities.Assign(c, s, hero, Progression.MaxBarSlots, null).Ok, Is.False);
        }

        [Test]
        public void Assign_rejects_a_slot_beyond_what_is_unlocked()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As1b");
            var hero = s.Hero(s.Party[0]);
            // Every slot ships unlocked today (owner direction 2026-10-11), so the "locked" guard and the
            // "out of range" guard currently coincide at the same boundary: Progression.UnlockedSlots(s) ==
            // Progression.MaxBarSlots for every save. This still exercises that the slot is rejected right
            // at that boundary, which is what the "locked" check guards against if thresholds ever return.
            Assert.That(Progression.UnlockedSlots(s), Is.EqualTo(Progression.MaxBarSlots));
            Assert.That(Abilities.Assign(c, s, hero, Progression.UnlockedSlots(s), null).Ok, Is.False);
        }

        [Test]
        public void Assign_rejects_a_non_consumable_item_entry()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As2");
            var hero = s.Hero(s.Party[0]);
            var material = c.Items.Values.First(i => i.Type == ItemType.CraftingMaterial);
            var result = Abilities.Assign(c, s, hero, 0, "item:" + material.Id);
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.Bar[0], Is.EqualTo("taunt")); // untouched
        }

        [Test]
        public void Assign_rejects_a_weapon_entry_without_a_spare_in_the_bag()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As3");
            var hero = s.Hero(s.Party[0]);
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon);
            Assert.That(s.Count(weapon.Id), Is.EqualTo(0));
            var result = Abilities.Assign(c, s, hero, 0, "weapon:" + weapon.Id);
            Assert.That(result.Ok, Is.False);
        }

        [Test]
        public void Assign_rejects_an_unlearned_ability()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As4");
            var hero = s.Hero(s.Party[0]); // paladin
            Assert.That(hero.Known, Does.Not.Contain("heal")); // learnable, but not known out of the gate
            var result = Abilities.Assign(c, s, hero, 0, "heal");
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.Bar[0], Is.EqualTo("taunt")); // untouched
        }

        [Test]
        public void Assign_moves_a_duplicate_entry_instead_of_copying_it()
        {
            var c = Real;
            var s = Progression.NewGame(c, "As5");
            var hero = s.Hero(s.Party[0]); // paladin, bar[0] == "taunt" by default loadout
            Assert.That(hero.Bar[0], Is.EqualTo("taunt"));
            var result = Abilities.Assign(c, s, hero, 3, "taunt");
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(hero.Bar[0], Is.Null); // vacated, not duplicated
            Assert.That(hero.Bar[3], Is.EqualTo("taunt"));
            Assert.That(hero.Bar.Count(e => e == "taunt"), Is.EqualTo(1));
        }

        // ── Abilities.Learn ──

        [Test]
        public void Learn_rejects_an_ability_outside_the_hero_class_and_not_a_spell()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Le1");
            var hero = s.Hero("paladin");
            Assert.That(c.Class("paladin").Abilities.Any(id => id == "steal"), Is.False);
            var result = Abilities.Learn(c, s, hero, "steal");
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.Known, Does.Not.Contain("steal"));
        }

        [Test]
        public void Learn_rejects_when_gold_is_short()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Le2");
            var hero = s.Hero("paladin");
            s.Gold = 0;
            Assert.That(Abilities.Learnable(c, hero).Any(a => a.Id == "heal"), Is.True);
            var result = Abilities.Learn(c, s, hero, "heal");
            Assert.That(result.Ok, Is.False);
            Assert.That(hero.Known, Does.Not.Contain("heal"));
        }

        [Test]
        public void Learn_succeeds_and_spends_gold_on_a_learnable_ability()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Le3");
            var hero = s.Hero("paladin");
            s.Gold = 10000;
            int price = Abilities.Price(c.Ability("heal"));
            int before = s.Gold;
            Assert.That(Abilities.Learn(c, s, hero, "heal").Ok, Is.True);
            Assert.That(hero.Known, Does.Contain("heal"));
            Assert.That(s.Gold, Is.EqualTo(before - price));
        }

        // ── Price formula ──
        [TestCase("shield_bash", 300)]   // Skill: flat 300
        [TestCase("heal", 200)]          // Spell, Cost 1: 120 + 80*1
        [TestCase("fireball", 360)]      // Spell, Cost 3 (GG-T373): 120 + 80*3
        public void Price_follows_the_skill_flat_and_spell_cost_formula(string abilityId, int expected)
        {
            Assert.That(Abilities.Price(Real.Ability(abilityId)), Is.EqualTo(expected));
        }

        // ── IsHeroUsable ──

        [Test]
        public void IsHeroUsable_excludes_passives_charge_abilities_and_enrage()
        {
            var c = Real;
            Assert.That(Abilities.IsHeroUsable(c.Ability("double_attack")), Is.False); // Passive
            Assert.That(Abilities.IsHeroUsable(c.Ability("charge_fireball")), Is.False); // charge_ prefix
            Assert.That(Abilities.IsHeroUsable(c.Ability("enrage")), Is.False);
            Assert.That(Abilities.IsHeroUsable(c.Ability("chrono_rewind")), Is.False); // the Chronomancer's free boss-phase Rewind
            Assert.That(Abilities.IsHeroUsable(c.Ability("shield_bash")), Is.True);
            Assert.That(Abilities.IsHeroUsable(c.Ability("fireball")), Is.True);
        }

        // ── Abilities.Swap ──

        [Test]
        public void Swap_rejects_the_same_slot()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Sw1");
            var hero = s.Hero(s.Party[0]);
            Assert.That(Abilities.Swap(s, hero, 2, 2).Ok, Is.False);
        }

        [Test]
        public void Swap_rejects_an_out_of_range_or_locked_slot()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Sw2");
            var hero = s.Hero(s.Party[0]);
            Assert.That(Abilities.Swap(s, hero, -1, 0).Ok, Is.False);
            Assert.That(Abilities.Swap(s, hero, 0, Progression.MaxBarSlots).Ok, Is.False);
        }

        [Test]
        public void Swap_exchanges_two_unlocked_slots()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Sw3");
            var hero = s.Hero(s.Party[0]);
            var a = hero.Bar[0];
            var b = hero.Bar[1];
            Assert.That(Abilities.Swap(s, hero, 0, 1).Ok, Is.True);
            Assert.That(hero.Bar[0], Is.EqualTo(b));
            Assert.That(hero.Bar[1], Is.EqualTo(a));
        }

        // ── Progression.NewGame ──
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void NewGame_falls_back_to_Lightbearer_on_a_blank_name(string name)
        {
            var s = Progression.NewGame(Real, name);
            Assert.That(s.Name, Is.EqualTo("Lightbearer"));
        }

        [Test]
        public void NewGame_skips_unknown_class_ids_in_a_custom_party()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Custom", new[] { "paladin", "not_a_real_class", "cleric" });
            Assert.That(s.Party, Is.EqualTo(new[] { "paladin", "cleric" }));
            Assert.That(s.Roster.Count, Is.EqualTo(2));
        }

        // ── Progression.CanStartNewGamePlus / IsStageUnlocked ──

        [Test]
        public void CanStartNewGamePlus_requires_the_whole_campaign_cleared()
        {
            var c = Real;
            var s = Progression.NewGame(c, "NG1");
            Assert.That(Progression.CanStartNewGamePlus(c, s), Is.False);
            s.HighestClearedStage = c.CampaignStages().Count - 1;
            Assert.That(Progression.CanStartNewGamePlus(c, s), Is.True);
        }

        [Test]
        public void IsStageUnlocked_opens_only_the_next_uncleared_stage()
        {
            var c = Real;
            var s = Progression.NewGame(c, "NG2");
            Assert.That(Progression.IsStageUnlocked(s, 0), Is.True);
            Assert.That(Progression.IsStageUnlocked(s, 1), Is.False);
            s.HighestClearedStage = 0;
            Assert.That(Progression.IsStageUnlocked(s, 1), Is.True);
            Assert.That(Progression.IsStageUnlocked(s, 2), Is.False);
        }
    }
}
