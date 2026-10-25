using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T181: 100 unique base weapons across every weapon class, each with its own stat profile or trait,
    /// a way to get it and an icon; every one carries the generated +1/+2/+3 and six-enchant variants.</summary>
    public class WeaponRosterTests
    {
        private static List<ItemDef> BaseWeapons(ContentDb c)
        {
            var derived = new HashSet<string>(c.Recipes.Values.Where(r => r.BaseItem != null).Select(r => r.Result));
            return c.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && !derived.Contains(i.Id)).ToList();
        }

        [Test]
        public void There_are_100_base_weapons_spread_over_every_weapon_class()
        {
            var weapons = BaseWeapons(Real);
            Assert.That(weapons.Count, Is.EqualTo(100));
            foreach (var wc in new[] { WeaponClass.Simple, WeaponClass.Martial, WeaponClass.Arcane })
                Assert.That(weapons.Count(w => w.WeaponClass == wc), Is.GreaterThanOrEqualTo(25), $"too few {wc} weapons");
            foreach (var rarity in new[] { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary })
                Assert.That(weapons.Any(w => w.Rarity == rarity), Is.True, $"no {rarity} weapon");
        }

        [Test]
        public void Every_base_weapon_has_a_distinct_stat_profile_or_trait()
        {
            string Profile(ItemDef w)
            {
                var s = w.Stats;
                return string.Join(",", s.Strength, s.Vitality, s.Agility, s.Speed, s.Stamina, s.Intelligence, s.Wisdom, s.Luck,
                    w.PincerDamageBonus, w.RegenPerTurn, w.StartsProtected, w.BattleStartManaOrbs, w.BankCapacityBonus,
                    w.PincerApplyBuff, w.PincerApplyBuffChance, w.LifestealFraction, w.AdjacentAllyDamageReduction,
                    string.Join("/", w.Resistances.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}{kv.Value}")));
            }
            var dupes = BaseWeapons(Real).GroupBy(Profile).Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(w => w.Id))).ToList();
            Assert.That(dupes, Is.Empty, "weapons sharing a profile: " + string.Join("; ", dupes));
        }

        [Test]
        public void Every_base_weapon_can_be_bought_forged_or_looted()
        {
            var c = Real;
            var sources = new HashSet<string>(Shop.Stock(c).Select(i => i.Id));
            sources.UnionWith(c.Recipes.Values.Where(r => r.Station == CraftStation.Forge && r.BaseItem == null).Select(r => r.Result));
            sources.UnionWith(c.DropTables.Values.SelectMany(d => d.Entries).Select(e => e.Item));
            sources.UnionWith(c.Bounties.Values.Where(b => b.RewardItem != null).Select(b => b.RewardItem));
            var stranded = BaseWeapons(c).Where(w => !sources.Contains(w.Id)).Select(w => w.Id).ToList();
            Assert.That(stranded, Is.Empty, "weapons with no shop, forge or drop source: " + string.Join(", ", stranded));
        }

        [Test]
        public void Every_base_weapon_has_an_upgrade_line_and_all_six_enchants()
        {
            var c = Real;
            int enchants = c.Enchants.Count;
            foreach (var w in BaseWeapons(c))
            {
                Assert.That(c.Recipes.Values.Any(r => r.Station == CraftStation.Forge && r.BaseItem == w.Id), Is.True, $"{w.Id}: no upgrade");
                Assert.That(c.Recipes.Values.Count(r => r.Station == CraftStation.Enchant && r.BaseItem == w.Id), Is.EqualTo(enchants), $"{w.Id}: enchant variants");
            }
        }

        /// <summary>GG-T194: every enchant variant (all six, for every weapon, originals and the GG-T181 roster
        /// alike) moves one rarity step up from its base item and keeps the base's durability. The original 13
        /// weapons' Poison and Holy rows were hand-authored as a flat Uncommon/100, breaking the rule.</summary>
        [Test]
        public void Every_enchant_variant_moves_up_one_rarity_and_keeps_base_durability()
        {
            var c = Real;
            var rarityOrder = new[]
            {
                ItemRarity.Junk, ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary
            };
            var bad = new List<string>();
            foreach (var recipe in c.Recipes.Values.Where(r => r.Station == CraftStation.Enchant))
            {
                var baseItem = c.Item(recipe.BaseItem);
                var result = c.Item(recipe.Result);
                var baseIndex = Array.IndexOf(rarityOrder, baseItem.Rarity);
                var expectedRarity = rarityOrder[Math.Min(rarityOrder.Length - 1, baseIndex + 1)];
                if (result.Rarity != expectedRarity || result.Durability != baseItem.Durability)
                    bad.Add($"{result.Id}: expected {expectedRarity}/{baseItem.Durability}, got {result.Rarity}/{result.Durability}");
            }
            Assert.That(bad, Is.Empty, "enchant variants breaking the shared rule (rarity +1 from base, base durability): " + string.Join("; ", bad));
        }

        [Test]
        public void Every_weapon_item_has_an_icon()
        {
            var dir = Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "ItemIcons");
            var missing = Real.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && !File.Exists(Path.Combine(dir, i.Id + ".png")))
                .Select(i => i.Id).ToList();
            Assert.That(missing, Is.Empty, "weapons without an icon (run tools/make_icons.py --missing): " + string.Join(", ", missing));
        }
    }
}
