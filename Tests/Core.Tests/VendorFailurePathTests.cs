using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Failure paths for the vendor/crafting/repair/inn gold economy (GG-T30): every rejection must
    /// leave gold and inventory untouched, and the pricing/growth formulas must hold at their edges.</summary>
    public class VendorFailurePathTests
    {
        /// <summary>A small, fully controlled catalog (on top of <see cref="Mini"/>'s hero classes) so prices,
        /// stacks and recipes are exact numbers rather than whatever the real content happens to contain.</summary>
        private static ContentDb BuildContent()
        {
            var c = Mini();

            ItemDef Item(string id, ItemType type, int baseCost, int maxStack = 99, int durability = 0, EquipmentSlot slot = EquipmentSlot.None, int baseHealing = 0) =>
                c.Items[id] = new ItemDef { Id = id, Name = id, Type = type, BaseCost = baseCost, MaxStack = maxStack, Durability = durability, Slot = slot, BaseHealing = baseHealing, SoldByVendor = true };

            Item("elixir", ItemType.Consumable, 20, maxStack: 5, baseHealing: 30);
            Item("herb", ItemType.CraftingMaterial, 2);
            Item("ore", ItemType.CraftingMaterial, 5);
            Item("trash", ItemType.CraftingMaterial, 0);
            Item("questkey", ItemType.QuestItem, 50);
            Item("warhammer", ItemType.Equipment, 100, maxStack: 1, durability: 100, slot: EquipmentSlot.Weapon);
            Item("finehammer", ItemType.Equipment, 150, maxStack: 1, durability: 100, slot: EquipmentSlot.Weapon);
            Item("plate", ItemType.Equipment, 100, maxStack: 1, durability: 100, slot: EquipmentSlot.Torso);

            c.Recipes["craft_elixir"] = new RecipeDef { Id = "craft_elixir", Station = CraftStation.Alchemy, Inputs = { new ItemCount { Item = "herb", Count = 2 } }, Gold = 10, Result = "elixir", ResultCount = 1 };
            c.Recipes["craft_warhammer"] = new RecipeDef { Id = "craft_warhammer", Station = CraftStation.Forge, Inputs = { new ItemCount { Item = "ore", Count = 2 } }, Gold = 30, Result = "warhammer", ResultCount = 1 };
            c.Recipes["upgrade_warhammer"] = new RecipeDef { Id = "upgrade_warhammer", Station = CraftStation.Forge, Inputs = { new ItemCount { Item = "ore", Count = 1 } }, Gold = 20, BaseItem = "warhammer", Result = "finehammer", ResultCount = 1 };

            c.Caravan["armory"] = new CaravanUpgradeDef { Id = "armory", Levels = { new CaravanLevel { Cost = 100, Value = 0.3f, Text = "Cheaper repairs" } } };
            c.Caravan["medic_wagon"] = new CaravanUpgradeDef { Id = "medic_wagon", Levels = { new CaravanLevel { Cost = 100, Value = 2f, Text = "Inn on the road" } } };
            return c;
        }

        private static SaveData NewSave(int gold = 0) => new SaveData { Gold = gold };

        // ── Shop.Buy ──

        [Test]
        public void Buy_rejects_an_empty_cart_without_touching_gold()
        {
            var c = BuildContent();
            var s = NewSave(1000);
            var result = Shop.Buy(c, s, new Dictionary<string, int>());
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(1000));
            Assert.That(s.Inventory, Is.Empty);
        }

        [Test]
        public void Buy_rejects_insufficient_gold_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(5); // elixir costs 20
            var result = Shop.Buy(c, s, new Dictionary<string, int> { ["elixir"] = 1 });
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(5));
            Assert.That(s.Count("elixir"), Is.EqualTo(0));
        }

        [Test]
        public void Buy_rejects_quantity_above_MaxBuy_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(10000);
            int tooMany = Shop.MaxBuy(s, c.Item("elixir")) + 1;
            var result = Shop.Buy(c, s, new Dictionary<string, int> { ["elixir"] = tooMany });
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(10000));
            Assert.That(s.Count("elixir"), Is.EqualTo(0));
        }

        [Test]
        public void Buy_rejects_a_negative_quantity_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(10000);
            var result = Shop.Buy(c, s, new Dictionary<string, int> { ["elixir"] = -1 });
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(10000));
            Assert.That(s.Count("elixir"), Is.EqualTo(0));
        }

        [Test]
        public void Buy_rejects_an_unknown_item_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(10000);
            var result = Shop.Buy(c, s, new Dictionary<string, int> { ["no_such_item"] = 1 });
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(10000));
            Assert.That(s.Inventory, Is.Empty);
        }

        // ── Shop.Sell ──

        [Test]
        public void Sell_rejects_selling_more_than_owned_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(0);
            s.AddItem("elixir", 2);
            var result = Shop.Sell(c, s, new Dictionary<string, int> { ["elixir"] = 3 });
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(0));
            Assert.That(s.Count("elixir"), Is.EqualTo(2));
        }

        // ── Shop.SellPrice / CartTotal / Sellable ──
        [TestCase(1, 1)]   // floor(0.5) = 0, clamped to minimum 1
        [TestCase(7, 3)]   // floor(3.5) = 3
        [TestCase(100, 50)]
        public void SellPrice_is_half_BaseCost_floored_with_a_minimum_of_one(int baseCost, int expected)
        {
            var item = new ItemDef { Id = "x", BaseCost = baseCost };
            Assert.That(Shop.SellPrice(item), Is.EqualTo(expected));
        }

        [Test]
        public void CartTotal_prices_buying_at_BaseCost_and_selling_at_SellPrice()
        {
            var c = BuildContent();
            var cart = new Dictionary<string, int> { ["elixir"] = 3 };
            Assert.That(Shop.CartTotal(c, cart, selling: false), Is.EqualTo(3 * c.Item("elixir").BaseCost));
            Assert.That(Shop.CartTotal(c, cart, selling: true), Is.EqualTo(3 * Shop.SellPrice(c.Item("elixir"))));
        }

        [Test]
        public void Sellable_excludes_quest_items_and_zero_cost_items()
        {
            var c = BuildContent();
            var s = NewSave(0);
            s.AddItem("elixir", 1);
            s.AddItem("questkey", 1);
            s.AddItem("trash", 1);
            var sellable = Shop.Sellable(c, s);
            Assert.That(sellable.Any(i => i.Id == "elixir"), Is.True);
            Assert.That(sellable.Any(i => i.Id == "questkey"), Is.False);
            Assert.That(sellable.Any(i => i.Id == "trash"), Is.False);
        }

        // ── Crafting.Craft / HasInputs ──

        [Test]
        public void Craft_fails_on_gold_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(0);
            s.AddItem("herb", 2);
            var result = Crafting.Craft(c, s, c.Recipes["craft_elixir"]);
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(0));
            Assert.That(s.Count("herb"), Is.EqualTo(2));
            Assert.That(s.Count("elixir"), Is.EqualTo(0));
        }

        [Test]
        public void Craft_fails_on_missing_inputs_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var s = NewSave(100);
            s.AddItem("herb", 1); // recipe needs 2
            var result = Crafting.Craft(c, s, c.Recipes["craft_elixir"]);
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(100));
            Assert.That(s.Count("herb"), Is.EqualTo(1));
            Assert.That(s.Count("elixir"), Is.EqualTo(0));
            Assert.That(Crafting.HasInputs(s, c.Recipes["craft_elixir"]), Is.False);
        }

        [Test]
        public void Craft_fails_on_missing_BaseItem_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var recipe = c.Recipes["upgrade_warhammer"];
            var s = NewSave(100);
            s.AddItem("ore", 1); // has the input, but no warhammer to upgrade
            Assert.That(Crafting.HasInputs(s, recipe), Is.False);
            var result = Crafting.Craft(c, s, recipe);
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(100));
            Assert.That(s.Count("ore"), Is.EqualTo(1));
            Assert.That(s.Count("finehammer"), Is.EqualTo(0));
        }

        [Test]
        public void Craft_fails_at_the_result_stack_cap_without_touching_gold_or_inventory()
        {
            var c = BuildContent();
            var recipe = c.Recipes["craft_elixir"];
            var s = NewSave(100);
            s.AddItem("herb", 4);
            s.AddItem("elixir", c.Item("elixir").MaxStack); // already full
            var result = Crafting.Craft(c, s, recipe);
            Assert.That(result.Ok, Is.False);
            Assert.That(s.Gold, Is.EqualTo(100));
            Assert.That(s.Count("herb"), Is.EqualTo(4));
            Assert.That(s.Count("elixir"), Is.EqualTo(c.Item("elixir").MaxStack));
        }

        [Test]
        public void HasInputs_is_true_only_once_every_input_and_the_base_item_are_present()
        {
            var c = BuildContent();
            var recipe = c.Recipes["upgrade_warhammer"];
            var s = NewSave(0);
            Assert.That(Crafting.HasInputs(s, recipe), Is.False);
            s.AddItem("ore", 1);
            Assert.That(Crafting.HasInputs(s, recipe), Is.False, "still missing the base item");
            s.AddItem("warhammer", 1);
            Assert.That(Crafting.HasInputs(s, recipe), Is.True);
        }

        [Test]
        public void SalvageYield_is_empty_when_there_is_no_base_forge_recipe_and_no_fallback()
        {
            var c = BuildContent();
            // "herb" is a plain crafting material (no equipment slot), so it matches neither a base forge
            // recipe nor the equipment type/rarity fallback table (GG-T42).
            Assert.That(Crafting.SalvageYield(c, c.Item("herb"), 0), Is.Empty);
        }

        [Test]
        public void SalvageYield_at_full_durability_matches_half_its_forge_recipe_inputs()
        {
            var c = BuildContent();
            // "warhammer" has a base forge recipe (craft_warhammer), so at full durability it should yield
            // half its inputs, unscaled.
            var warhammer = c.Item("warhammer");
            var yield = Crafting.SalvageYield(c, warhammer, warhammer.Durability);
            Assert.That(yield, Has.Exactly(1).Items);
            Assert.That(yield[0].Item, Is.EqualTo("ore"));
            Assert.That(yield[0].Count, Is.EqualTo(1)); // half of 2, floored, minimum 1
        }

        // ── Repair ──

        [Test]
        public void Repair_cost_is_zero_at_full_durability()
        {
            var item = new ItemDef { Id = "w", BaseCost = 100, Durability = 10 };
            Assert.That(Repair.Cost(item, Repair.EffectiveMax(item, 0), 0), Is.EqualTo(0));
        }

        [Test]
        public void Repair_cost_grows_roughly_1_6x_per_prior_repair()
        {
            var item = new ItemDef { Id = "w", BaseCost = 100, Durability = 100 };
            // Hold "missing" constant across repair counts by offsetting current with EffectiveMax, so only
            // the 1.6x-per-repair multiplier in Repair.Cost moves between calls.
            int cost0 = Repair.Cost(item, Repair.EffectiveMax(item, 0) - 10, 0);
            int cost1 = Repair.Cost(item, Repair.EffectiveMax(item, 1) - 10, 1);
            int cost2 = Repair.Cost(item, Repair.EffectiveMax(item, 2) - 10, 2);
            Assert.That(cost1 > cost0, Is.True);
            Assert.That(cost2 > cost1, Is.True);
            Assert.That((double)cost1 / cost0, Is.InRange(1.5, 1.8));
            Assert.That((double)cost2 / cost1, Is.InRange(1.5, 1.8));
        }

        [Test]
        public void Repair_cost_never_drops_below_one_when_anything_is_missing()
        {
            var item = new ItemDef { Id = "w", BaseCost = 1, Durability = 1000 };
            Assert.That(Repair.Cost(item, Repair.EffectiveMax(item, 0) - 1, 0), Is.EqualTo(1));
        }

        [Test]
        public void EffectiveMax_floors_at_one_once_repairs_exceed_durability()
        {
            var item = new ItemDef { Id = "w", BaseCost = 10, Durability = 3 };
            Assert.That(Repair.EffectiveMax(item, 5), Is.EqualTo(1));
        }

        [Test]
        public void IsUneconomical_once_repeated_repairs_push_cost_to_or_past_BaseCost()
        {
            var item = new ItemDef { Id = "w", BaseCost = 10, Durability = 10 };
            Assert.That(Repair.IsUneconomical(item, 0, 0), Is.False);
            Assert.That(Repair.IsUneconomical(item, 0, 4), Is.True);
        }

        [Test]
        public void Armory_discount_rounds_the_price_up_not_down()
        {
            var c = BuildContent();
            var s = NewSave(0);
            var item = new ItemDef { Id = "w", BaseCost = 100, Durability = 100 };
            // Pick a repair state whose undiscounted cost (7) times the 30% discount (0.7) lands on a
            // fraction (4.9), so a floor vs. ceiling would disagree.
            int current = Repair.EffectiveMax(item, 0) - 21;
            int full = Repair.Cost(item, current, 0);
            Assert.That(full, Is.EqualTo(7));
            s.Caravan["armory"] = 1;
            int discounted = Repair.Cost(c, s, item, current, 0);
            Assert.That(discounted < full, Is.True);
            Assert.That(discounted, Is.EqualTo((int)Math.Ceiling(full * (1f - 0.3f))));
            Assert.That(discounted, Is.Not.EqualTo((int)Math.Floor(full * (1f - 0.3f))));
        }

        [Test]
        public void RepairWeapon_fails_without_gold_and_succeeds_restoring_durability()
        {
            var c = BuildContent();
            var hero = Progression.NewHero(c, "knight");
            hero.WeaponId = "warhammer";
            hero.WeaponDurability = 1;
            hero.WeaponRepairs = 0;
            var s = NewSave(0);
            Assert.That(Repair.RepairWeapon(c, s, hero).Ok, Is.False);
            Assert.That(hero.WeaponDurability, Is.EqualTo(1));

            s.Gold = 10000;
            Assert.That(Repair.RepairWeapon(c, s, hero).Ok, Is.True);
            // Restored to the new effective max (after this repair's cap drop), so the row never reads
            // above its max (GG-T196) — not the pre-repair max at EffectiveMax(item, 0).
            Assert.That(hero.WeaponRepairs, Is.EqualTo(1));
            Assert.That(hero.WeaponDurability, Is.EqualTo(Repair.EffectiveMax(c.Item("warhammer"), 1)));
        }

        [Test]
        public void RepairArmor_fails_without_gold_and_succeeds_restoring_durability()
        {
            var c = BuildContent();
            var hero = Progression.NewHero(c, "knight");
            hero.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = "plate", Durability = 1, Repairs = 0 };
            var s = NewSave(0);
            Assert.That(Repair.RepairArmor(c, s, hero, EquipmentSlot.Torso).Ok, Is.False);
            Assert.That(hero.Armor[EquipmentSlot.Torso].Durability, Is.EqualTo(1));

            s.Gold = 10000;
            Assert.That(Repair.RepairArmor(c, s, hero, EquipmentSlot.Torso).Ok, Is.True);
            // Restored to the new effective max (after this repair's cap drop), so the row never reads
            // above its max (GG-T196) — not the pre-repair max at EffectiveMax(item, 0).
            Assert.That(hero.Armor[EquipmentSlot.Torso].Repairs, Is.EqualTo(1));
            Assert.That(hero.Armor[EquipmentSlot.Torso].Durability, Is.EqualTo(Repair.EffectiveMax(c.Item("plate"), 1)));
        }

        [Test]
        public void Repair_row_never_reads_above_its_max_right_after_a_repair()
        {
            // GG-T196: a repair used to restore the piece to its *old* max and only then drop the cap by
            // one, so the Blacksmith row briefly read e.g. "150/149" until the next battle. It must read
            // e.g. "149/149" the instant the repair lands — for every repair, not just the first.
            var c = BuildContent();
            var hero = Progression.NewHero(c, "knight");
            hero.WeaponId = "warhammer";
            hero.WeaponDurability = 1;
            hero.WeaponRepairs = 0;
            hero.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = "plate", Durability = 1, Repairs = 0 };
            var s = NewSave(10000);
            s.Roster.Add(hero);

            for (int i = 0; i < 3; i++)
            {
                // Simulate a battle wearing the gear down by 1 before the next repair, the way play
                // actually reaches the "repaired but reads over max" state.
                hero.WeaponDurability--;
                hero.Armor[EquipmentSlot.Torso].Durability--;
                Assert.That(Repair.RepairWeapon(c, s, hero).Ok, Is.True);
                Assert.That(Repair.RepairArmor(c, s, hero, EquipmentSlot.Torso).Ok, Is.True);
                foreach (var piece in Repair.Pieces(c, s))
                    Assert.That(piece.Durability, Is.LessThanOrEqualTo(piece.Max), $"{piece.RowName} read {piece.Durability}/{piece.Max}");
            }
        }

        // ── Inn ──

        [Test]
        public void MissingHp_is_zero_at_full_health()
        {
            var c = BuildContent();
            var hero = Progression.NewHero(c, "knight");
            var s = NewSave(0);
            int max = Progression.Preview(c, hero).MaxHp;

            hero.Hp = 0; // save convention: 0 = full health
            Assert.That(Inn.MissingHp(c, s, hero), Is.EqualTo(0));

            hero.Hp = max;
            Assert.That(Inn.MissingHp(c, s, hero), Is.EqualTo(0));

            hero.Hp = max - 1;
            Assert.That(Inn.MissingHp(c, s, hero), Is.EqualTo(1));
        }

        [Test]
        public void Inn_cost_applies_the_medic_wagon_premium_only_while_on_an_expedition()
        {
            var c = BuildContent();
            var hero = Progression.NewHero(c, "knight");
            int max = Progression.Preview(c, hero).MaxHp;
            int missing = Math.Min(15, max - 1);
            hero.Hp = max - missing;
            var s = NewSave(0);
            s.Roster.Add(hero);

            int homeCost = Inn.Cost(c, s);
            Assert.That(homeCost, Is.EqualTo((int)Math.Ceiling(missing * Inn.GoldPerHp)));

            s.Caravan["medic_wagon"] = 1; // Levels[0].Value = 2x premium
            s.Expedition = new ExpeditionSave();
            int roadCost = Inn.Cost(c, s);
            Assert.That(roadCost, Is.EqualTo((int)Math.Ceiling(missing * Inn.GoldPerHp * 2f)));
            Assert.That(roadCost > homeCost, Is.True);
        }
    }
}
