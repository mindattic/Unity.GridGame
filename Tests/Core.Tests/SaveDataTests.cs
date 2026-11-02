using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Meta;
using System.Linq;
using NUnit.Framework;

namespace GridGame.Tests
{
    public class SaveDataTests
    {
        private static SaveData NewSave() => new SaveData
        {
            Roster = { new HeroSave { ClassId = "Paladin" } }
        };

        [Test]
        public void Migrate_of_null_returns_null()
        {
            Assert.That(SaveMigrations.Migrate(null), Is.Null);
        }

        [Test]
        public void Migrate_rejects_a_version_newer_than_current()
        {
            var s = new SaveData { Version = SaveData.CurrentVersion + 1 };
            var ex = Assert.Throws<InvalidOperationException>(() => SaveMigrations.Migrate(s));
            Assert.That(ex.Message, Does.Contain(s.Version.ToString()));
        }

        [Test]
        public void Migrate_restores_null_sections()
        {
            var s = NewSave();
            s.Bounty = null;
            s.Worn = null;
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.Bounty, Is.Not.Null);
            Assert.That(back.Worn, Is.Not.Null);
            // Settings left the save for the player profile (GG-T99): see ProfileTests.
        }

        [Test]
        public void Migrate_pads_short_hero_bar()
        {
            var s = NewSave();
            s.Roster[0].Bar = new List<string> { "attack" };
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.Roster[0].Bar.Count, Is.EqualTo(5));
            Assert.That(back.Roster[0].Bar[0], Is.EqualTo("attack"));
        }

        /// <summary>GG-T89: a pre-v3 save's single Armor slot becomes the new Torso slot, carrying its
        /// durability/repairs/mastery along with it (not just the item id).</summary>
        [Test]
        public void Migrate_v2_drains_the_old_armor_slot_into_torso()
        {
            var s = NewSave();
            s.Version = 2;
            s.Roster[0].ArmorId = "eq_armor_leather";
            s.Roster[0].ArmorDurability = 40;
            s.Roster[0].ArmorRepairs = 2;
            s.Roster[0].ArmorMastery = 1.2f;
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.Version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(back.Roster[0].Armor.ContainsKey(EquipmentSlot.Torso), Is.True);
            var piece = back.Roster[0].Armor[EquipmentSlot.Torso];
            Assert.That(piece.ItemId, Is.EqualTo("eq_armor_leather"));
            Assert.That(piece.Durability, Is.EqualTo(40));
            Assert.That(piece.Repairs, Is.EqualTo(2));
            Assert.That(piece.Mastery, Is.EqualTo(1.2f));
            Assert.That(back.Roster[0].ArmorId, Is.Null); // legacy field drained and cleared
        }

        /// <summary>GG-T89: a pre-v3 save's three generic Relic slots become named jewelry slots, in order
        /// (Necklace, then Earring1, then Earring2) — a best-effort carry-over, not a category match.</summary>
        [Test]
        public void Migrate_v2_drains_the_old_relic_list_into_named_jewelry_slots()
        {
            var s = NewSave();
            s.Version = 2;
            s.Roster[0].Relics = new List<string> { "eq_ring_copper", "eq_amulet_iron", null };
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.Roster[0].Jewelry[EquipmentSlot.Necklace], Is.EqualTo("eq_ring_copper"));
            Assert.That(back.Roster[0].Jewelry[EquipmentSlot.Earring1], Is.EqualTo("eq_amulet_iron"));
            Assert.That(back.Roster[0].Jewelry.ContainsKey(EquipmentSlot.Earring2), Is.False);
        }

        [Test]
        public void AddItem_ignores_null_id_and_zero_delta()
        {
            var s = NewSave();
            s.AddItem(null, 5);
            Assert.That(s.Inventory, Is.Empty);
            s.AddItem("potion", 0);
            Assert.That(s.Inventory, Is.Empty);
        }

        [Test]
        public void AddItem_removes_the_key_when_count_drops_to_zero_or_below()
        {
            var s = NewSave();
            s.AddItem("potion", 3);
            Assert.That(s.Count("potion"), Is.EqualTo(3));
            s.AddItem("potion", -3);
            Assert.That(s.Inventory.ContainsKey("potion"), Is.False);
            Assert.That(s.Count("potion"), Is.EqualTo(0));

            s.AddItem("elixir", 2);
            s.AddItem("elixir", -9); // drives it below zero, not just to zero
            Assert.That(s.Inventory.ContainsKey("elixir"), Is.False);
        }

        [Test]
        public void AddItem_trims_worn_copies_most_worn_first_so_worn_never_outnumbers_owned()
        {
            var s = NewSave();
            s.AddItem("sword", 3);
            s.Worn["sword"] = new List<WornGear>
            {
                new WornGear { Durability = 10 },
                new WornGear { Durability = 50 },
                new WornGear { Durability = 30 },
            };
            s.AddItem("sword", -2); // owned drops to 1; worn must shrink to the least-worn single copy
            Assert.That(s.Count("sword"), Is.EqualTo(1));
            Assert.That(s.Worn["sword"], Has.Exactly(1).Items);
            var worn = s.Worn["sword"].Single();
            Assert.That(worn.Durability, Is.EqualTo(50)); // highest remaining durability = least worn, so it survives

            s.AddItem("sword", -1); // owned drops to 0; the worn list is removed entirely
            Assert.That(s.Worn.ContainsKey("sword"), Is.False);
        }

        [Test]
        public void Hero_lookup_is_case_insensitive()
        {
            var s = NewSave();
            Assert.That(s.Hero("paladin"), Is.SameAs(s.Roster[0]));
            Assert.That(s.Hero("PALADIN"), Is.SameAs(s.Roster[0]));
            Assert.That(s.Hero("cleric"), Is.Null);
        }

        [Test]
        public void Count_of_a_missing_item_is_zero()
        {
            var s = NewSave();
            Assert.That(s.Count("nonexistent"), Is.EqualTo(0));
            Assert.That(s.Count(null), Is.EqualTo(0));
        }
    }
}
