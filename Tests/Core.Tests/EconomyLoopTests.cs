using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T136: the content half of "every system hands off to the next" — nothing a fight drops is a dead
    /// end, and nothing a station asks for is unobtainable.</summary>
    public class EconomyLoopTests
    {
        private static ContentDb C => TestKit.Real;

        private static HashSet<string> RecipeInputs() =>
            new HashSet<string>(C.Recipes.Values.SelectMany(r =>
                r.Inputs.Select(i => i.Item).Concat(r.PotencyInputs.Select(i => i.Item)).Concat(r.BaseItem != null ? new[] { r.BaseItem } : new string[0])),
                System.StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> Dropped() =>
            new HashSet<string>(C.DropTables.Values.SelectMany(d => d.Entries.Select(e => e.Item))
                .Concat(C.Bounties.Values.Where(b => b.RewardItem != null).Select(b => b.RewardItem)), System.StringComparer.OrdinalIgnoreCase);

        [Test]
        public void Every_dropped_crafting_material_feeds_at_least_one_recipe()
        {
            var inputs = RecipeInputs();
            var orphans = Dropped().Select(C.TryItem)
                .Where(i => i != null && i.Type == ItemType.CraftingMaterial && !inputs.Contains(i.Id))
                .Select(i => i.Id).OrderBy(x => x).ToList();
            Assert.That(orphans, Is.Empty, "dropped materials no recipe uses: " + string.Join(", ", orphans));
        }

        /// <summary>GG-T176 (§25.0.5): the Merchant sells Empty Bottles cheaply, and every bottle recipe brews from one
        /// bottle plus looted trophies for less than the potion costs to buy — even with every input bought.</summary>
        [Test]
        public void Bottle_recipes_brew_from_loot_for_less_than_buying_the_potion()
        {
            var bottle = C.Item(ContentLoader.EmptyBottle);
            Assert.That(Shop.Stock(C), Has.Member(bottle), "the Merchant sells Empty Bottles");
            Assert.That(bottle.BaseCost, Is.InRange(1, 5), "cheaply");
            var recipes = Brewing.BottleRecipes(C);
            Assert.That(recipes.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(recipes.Where(r => r.Tier <= 1), Is.Not.Empty, "a first brew is open from the start");
            var dropped = Dropped();
            foreach (var r in recipes)
            {
                Assert.That(C.Item(r.Result).Type, Is.EqualTo(ItemType.Consumable), r.Id);
                Assert.That(r.Inputs.Where(i => i.Item == ContentLoader.EmptyBottle).Sum(i => i.Count), Is.EqualTo(1), r.Id + ": one bottle");
                foreach (var i in r.Inputs.Where(i => i.Item != ContentLoader.EmptyBottle))
                    Assert.That(dropped, Does.Contain(i.Item), $"{r.Id}: {i.Item} is looted from enemies");
                Assert.That(Brewing.ShopCost(C, r), Is.LessThan(Brewing.BuyCost(C, r)), r.Id + ": cheaper than buying");
            }
        }

        [Test]
        public void The_camp_routine_buys_a_bottle_to_brew_its_loot()
        {
            var s = Progression.NewGame(C, "T");
            CampNpcs.JoinAll(s);
            s.Gold = 50;
            s.AddItem("mat_wolf_pelt", 1); s.AddItem("mat_tattered_cloth", 1);
            CampRoutine.Run(C, s);
            Assert.That(s.Count("mat_wolf_pelt"), Is.EqualTo(0), "the pelt went into a Healing Potion");
            Assert.That(s.Count(CampRoutine.StopgapPotion), Is.GreaterThanOrEqualTo(1));
        }

        /// <summary>GG-T177 (§25.0.6): the Forge's starter weapon recipes are always affordable (no gold) and take only
        /// materials the first theme's enemies drop, and between them they suit every starting hero.</summary>
        [Test]
        public void Starter_weapon_recipes_cost_no_gold_and_take_only_first_theme_drops()
        {
            var starters = Forging.StarterRecipes(C);
            Assert.That(starters.Count, Is.GreaterThanOrEqualTo(3));
            var first = C.Campaign.Themes[0];
            var early = new HashSet<string>(first.Stages.Select(id => C.Stages[id]).SelectMany(st => st.Waves).SelectMany(w => w.Enemies)
                .Select(e => C.TryClass(e.Class)?.DropTable).Where(t => t != null && C.DropTables.ContainsKey(t))
                .SelectMany(t => C.DropTables[t].Entries.Select(e => e.Item)));
            foreach (var r in starters)
            {
                Assert.That(r.Gold, Is.EqualTo(0), r.Id);
                Assert.That(r.Tier, Is.EqualTo(1), r.Id);
                Assert.That(C.Item(r.Result).Slot, Is.EqualTo(EquipmentSlot.Weapon), r.Id);
                foreach (var i in r.Inputs) Assert.That(early, Does.Contain(i.Item), $"{r.Id}: {i.Item} drops in {first.Name}");
            }
            var s = Progression.NewGame(C, "T");
            foreach (var r in starters) foreach (var i in r.Inputs) s.AddItem(i.Item, i.Count);
            foreach (var id in Progression.StartingRoster)
                Assert.That(starters.Any(r => Forging.SuitedHero(C, s, C.Item(r.Result))?.ClassId == id), Is.True, $"a starter weapon suits the {id}");
        }

        /// <summary>GG-T177: the guided first forge makes the starter weapon the Forge walks and equips it on the hero it suits;
        /// the next forge is an ordinary one.</summary>
        [Test]
        public void The_first_forge_equips_the_starter_weapon_on_the_hero_it_suits()
        {
            var s = Progression.NewGame(C, "T");
            CampNpcs.JoinAll(s);
            Assert.That(Forging.First(C, s).Ready, Is.False, "nothing looted yet");
            s.AddItem("mat_slime_gel", 2); s.AddItem("mat_tattered_cloth", 1);
            var plan = Forging.First(C, s);
            Assert.That(plan.Recipe.Id, Is.EqualTo("recipe_starter_lantern"));
            Assert.That(plan.Ready, Is.True);
            Assert.That(plan.Hero.ClassId, Is.EqualTo("cleric"), "a Wisdom lantern suits the Cleric");
            int gold = s.Gold;
            var r = Forging.Forge(C, s, plan.Recipe);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(r.Message, Does.Contain("Cleric equips it"));
            Assert.That(s.Hero("cleric").WeaponId, Is.EqualTo("eq_lantern_tallow"));
            Assert.That(s.Gold, Is.EqualTo(gold), "free");
            Assert.That(Forging.First(C, s), Is.Null, "the first forge is done");
            s.AddItem("mat_wolf_pelt", 1); s.AddItem("mat_cracked_fang", 2);
            Forging.Forge(C, s, C.Recipes["recipe_starter_club"]);
            Assert.That(s.Count("eq_club_oak"), Is.EqualTo(1), "later forges go to the bag");
        }

        /// <summary>GG-T178 (§25.0.7): the armor-set recipes stay shut at the Forge until the first weapon is made; then the
        /// guided first armor forges one set piece and puts it on the hero it suits, and the next forge is an ordinary one.</summary>
        [Test]
        public void Armor_recipes_open_after_the_first_weapon_and_the_first_piece_is_worn()
        {
            var s = Progression.NewGame(C, "T");
            CampNpcs.JoinAll(s);
            s.Gold = 200;
            s.AddItem("mat_leather", 2); s.AddItem("mat_wolf_pelt", 1);
            var legs = C.Recipes["recipe_wayfarer_legs"];
            Assert.That(Forging.IsSetArmor(C, legs), Is.True);
            Assert.That(Forging.ArmorOpen(C, s, legs), Is.False, "shut before the first forge");
            Assert.That(Forging.FirstArmor(C, s), Is.Null);
            Assert.That(Crafting.Craft(C, s, legs).Ok, Is.False, "can't be forged yet");
            Assert.That(Forging.ArmorOpen(C, s, C.Recipes["recipe_boots_leather"]), Is.True, "non-set armor isn't gated");

            s.Flags.Add(Forging.FirstForgeFlag);
            Assert.That(Forging.SetArmorRecipes(C).All(r => Forging.ArmorOpen(C, s, r)), Is.True, "open after the first weapon");
            var plan = Forging.FirstArmor(C, s);
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Recipe.Id, Is.EqualTo("recipe_wayfarer_legs"), "the ready set piece");
            Assert.That(plan.Ready, Is.True);
            Assert.That(plan.Hero.ClassId, Is.EqualTo("barbarian"), "Speed/Luck trousers suit the Barbarian's even growth best");
            int gold = s.Gold;
            var r = Forging.Forge(C, s, plan.Recipe);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(r.Message, Does.Contain("Barbarian wears it"));
            Assert.That(Equipment.Equipped(s.Hero("barbarian"), EquipmentSlot.Legs), Is.EqualTo("eq_wayfarer_legs"));
            Assert.That(s.Gold, Is.EqualTo(gold - legs.Gold), "armor costs its gold");
            Assert.That(s.Flags, Does.Contain(Forging.FirstArmorFlag));
            Assert.That(Forging.FirstArmor(C, s), Is.Null, "the first armor is done");
            s.AddItem("mat_leather", 2); s.AddItem("mat_wolf_pelt", 1);
            Forging.Forge(C, s, legs);
            Assert.That(s.Count("eq_wayfarer_legs"), Is.EqualTo(1), "later forges go to the bag");
        }

        /// <summary>GG-T179 (§25.0.8): armor enchants grant real Passive abilities; the Apprentice ones take Arcane Dust and
        /// first-theme drops only, so the guided first enchant is reachable from Green Valley.</summary>
        [Test]
        public void Armor_enchants_grant_passives_and_the_first_ones_take_first_theme_drops()
        {
            var all = ArmorEnchanting.All(C);
            Assert.That(all.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(all.Select(e => e.Passive).Distinct().Count(), Is.EqualTo(all.Count), "each enchant grants its own passive");
            foreach (var e in all) Assert.That(C.Ability(e.Passive).Kind, Is.EqualTo(AbilityKind.Passive), e.Id);
            var first = C.Campaign.Themes[0];
            var early = new HashSet<string>(first.Stages.Select(id => C.Stages[id]).SelectMany(st => st.Waves).SelectMany(w => w.Enemies)
                .Select(e => C.TryClass(e.Class)?.DropTable).Where(t => t != null && C.DropTables.ContainsKey(t))
                .SelectMany(t => C.DropTables[t].Entries.Select(e => e.Item)));
            var apprentice = all.Where(e => e.Tier <= 1).ToList();
            Assert.That(apprentice.Count, Is.GreaterThanOrEqualTo(3));
            foreach (var e in apprentice)
            {
                Assert.That(e.Inputs.Select(i => i.Item), Does.Contain("mat_arcane_dust"), e.Id);
                foreach (var i in e.Inputs) Assert.That(early, Does.Contain(i.Item), $"{e.Id}: {i.Item} drops in {first.Name}");
            }
        }

        /// <summary>GG-T179: the first armor enchant is free and binds its passive into the worn piece; the piece keeps it
        /// through unequip and re-equip and carries it into battle; the next enchant costs its gold and replaces it.</summary>
        [Test]
        public void An_armor_enchant_binds_into_the_worn_piece_and_stays_with_it()
        {
            var s = Progression.NewGame(C, "T");
            CampNpcs.JoinAll(s);
            s.Flags.Add(Forging.FirstForgeFlag);
            s.Gold = 300;
            s.AddItem("mat_leather", 2); s.AddItem("mat_wolf_pelt", 1);
            Forging.Forge(C, s, C.Recipes["recipe_wayfarer_legs"]);
            var barb = s.Hero("barbarian");
            Assert.That(ArmorEnchanting.First(C, s), Is.Not.Null, "the first enchant is ahead once armor is worn");
            var regen = C.TryArmorEnchant("ae_regeneration");
            Assert.That(ArmorEnchanting.Enchant(C, s, barb, EquipmentSlot.Legs, regen).Ok, Is.False, "materials missing");
            s.AddItem("mat_arcane_dust", 2); s.AddItem("mat_frog_gland", 2); s.AddItem("mat_slime_gel", 2);
            int gold = s.Gold;
            var r = ArmorEnchanting.Enchant(C, s, barb, EquipmentSlot.Legs, regen);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(r.Message, Does.Contain("Regenerate"));
            Assert.That(s.Gold, Is.EqualTo(gold), "the first rune is free");
            Assert.That(barb.Armor[EquipmentSlot.Legs].Enchant, Is.EqualTo("ae_regeneration"));
            Assert.That(s.Count("mat_frog_gland"), Is.EqualTo(0));
            Assert.That(s.Flags, Does.Contain(ArmorEnchanting.FirstEnchantFlag));
            Assert.That(ArmorEnchanting.First(C, s), Is.Null, "the first enchant is done");
            Assert.That(BattleFactory.SpecFor(C, barb).Armor[EquipmentSlot.Legs].Enchant, Is.EqualTo("ae_regeneration"), "carried into battle");

            Equipment.Unequip(C, s, barb, EquipmentSlot.Legs);
            Assert.That(barb.Armor.ContainsKey(EquipmentSlot.Legs), Is.False);
            Assert.That(Equipment.Equip(C, s, barb, EquipmentSlot.Legs, "eq_wayfarer_legs").Ok, Is.True);
            Assert.That(barb.Armor[EquipmentSlot.Legs].Enchant, Is.EqualTo("ae_regeneration"), "kept through unequip and re-equip");

            var ward = C.TryArmorEnchant("ae_poison_ward");
            gold = s.Gold;
            r = ArmorEnchanting.Enchant(C, s, barb, EquipmentSlot.Legs, ward);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(r.Message, Does.Contain("replacing Regeneration"));
            Assert.That(s.Gold, Is.EqualTo(gold - ward.Gold), "later enchants cost their gold");
            Assert.That(barb.Armor[EquipmentSlot.Legs].Enchant, Is.EqualTo("ae_poison_ward"));
        }

        /// <summary>GG-T178: an Apprentice set piece every starting hero can wear takes only first-theme drops, so the
        /// guided first armor is reachable from Green Valley; bots and New Game+ skip the gate.</summary>
        [Test]
        public void A_first_armor_piece_is_forgeable_from_first_theme_drops()
        {
            var first = C.Campaign.Themes[0];
            var early = new HashSet<string>(first.Stages.Select(id => C.Stages[id]).SelectMany(st => st.Waves).SelectMany(w => w.Enemies)
                .Select(e => C.TryClass(e.Class)?.DropTable).Where(t => t != null && C.DropTables.ContainsKey(t))
                .SelectMany(t => C.DropTables[t].Entries.Select(e => e.Item)));
            var reachable = Forging.SetArmorRecipes(C).Where(r => r.Tier <= 1 && r.Inputs.All(i => early.Contains(i.Item))).ToList();
            Assert.That(reachable, Is.Not.Empty);
            var s = Progression.NewGame(C, "T");
            s.Flags.Add(Forging.FirstForgeFlag);
            s.Gold = 500;
            foreach (var r in reachable) foreach (var i in r.Inputs) s.AddItem(i.Item, i.Count);
            foreach (var id in Progression.StartingRoster)
                Assert.That(reachable.Any(r => Forging.SuitedWearer(C, s, C.Item(r.Result)) != null && Equipment.Fits(C, s.Hero(id), C.Item(r.Result), C.Item(r.Result).Slot)),
                    Is.True, $"the {id} can wear a first armor piece");
            var bot = Progression.NewGame(C, "B"); bot.TestMode = true;
            Assert.That(Forging.ArmorOpen(C, bot, reachable[0]), Is.True, "bots aren't gated");
        }

        [Test]
        public void Every_recipe_input_can_be_obtained()
        {
            var obtainable = Dropped();
            obtainable.UnionWith(Shop.Stock(C).Select(i => i.Id));
            obtainable.UnionWith(C.Recipes.Values.Select(r => r.Result));
            obtainable.UnionWith(C.Recipes.Values.Where(r => r.PotencyResult != null).Select(r => r.PotencyResult));
            var unobtainable = RecipeInputs().Where(id => !obtainable.Contains(id)).OrderBy(x => x).ToList();
            Assert.That(unobtainable, Is.Empty, "recipe inputs no drop, shop or recipe provides: " + string.Join(", ", unobtainable));
        }

        [Test]
        public void Every_dropped_item_is_sellable_or_usable()
        {
            var dead = Dropped().Select(C.TryItem)
                .Where(i => i != null && i.Type == ItemType.CraftingMaterial && i.BaseCost <= 0 && !RecipeInputs().Contains(i.Id))
                .Select(i => i.Id).ToList();
            Assert.That(dead, Is.Empty, "dropped items with no sale value and no use: " + string.Join(", ", dead));
        }
    }
}
