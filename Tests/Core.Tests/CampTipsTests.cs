using System.Linq;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T173: the camp tutorial shows each station's tip once, never to bots, and can be replayed.</summary>
    public class CampTipsTests
    {
        [Test]
        public void A_station_tip_is_pending_until_marked_seen()
        {
            var s = new SaveData();
            Assert.That(CampTips.Pending(s, "shop"), Is.Not.Null);
            CampTips.MarkSeen(s, "shop");
            Assert.That(CampTips.Pending(s, "shop"), Is.Null);
            Assert.That(CampTips.Pending(s, "blacksmith"), Is.Not.Null, "other stations are unaffected");
        }

        [Test]
        public void Bot_played_saves_and_unknown_stations_get_no_tip()
        {
            Assert.That(CampTips.Pending(new SaveData { TestMode = true }, "shop"), Is.Null);
            Assert.That(CampTips.Pending(new SaveData(), "settings"), Is.Null);
            Assert.That(CampTips.Pending(null, "shop"), Is.Null);
        }

        [Test]
        public void Replay_brings_every_tip_back()
        {
            var s = new SaveData();
            foreach (var t in CampTips.All) CampTips.MarkSeen(s, t.Station);
            Assert.That(CampTips.All.Any(t => CampTips.Pending(s, t.Station) != null), Is.False);
            CampTips.ResetAll(s);
            Assert.That(CampTips.All.All(t => CampTips.Pending(s, t.Station) != null), Is.True);
        }

        [Test]
        public void An_older_save_without_the_field_migrates_with_none_seen()
        {
            var s = new SaveData { SeenCampTips = null };
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.SeenCampTips, Is.Not.Null.And.Empty);
        }

        [Test]
        public void The_blacksmith_tip_names_and_points_at_the_most_worn_piece()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            var tip = CampTips.For("blacksmith");
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "no worn gear: the tip reads as written");
            var worn = CampNpcTests.WearLeadWeapon(c, s, 0.5f);
            var resolved = CampTips.Resolve(c, s, tip);
            Assert.That(resolved.Target, Is.EqualTo(Repair.RowNameFor(s.Party[0], GridGame.Core.Content.EquipmentSlot.Weapon)));
            Assert.That(resolved.Text, Does.Contain(worn.Item.Name).And.Contain((int)System.Math.Round(worn.Fraction * 100) + "%"));
            Assert.That(CampTips.Resolve(c, s, CampTips.For("shop")), Is.SameAs(CampTips.For("shop")), "other tips are untouched");
        }

        [Test]
        public void The_alchemist_tip_walks_the_first_bottle_brew()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            var tip = CampTips.For("alchemist");
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "no loot: the tip reads as written");
            s.AddItem("mat_wolf_pelt", 1); s.AddItem("mat_tattered_cloth", 1);
            var noBottle = CampTips.Resolve(c, s, tip);
            Assert.That(noBottle.Target, Is.EqualTo("recipe-recipe_bottle_healing"));
            Assert.That(noBottle.Text, Does.Contain("Empty Bottle").And.Contain("Merchant"));
            s.AddItem(GridGame.Core.Content.ContentLoader.EmptyBottle, 1);
            Assert.That(Brewing.FirstBrew(c, s)?.Id, Is.EqualTo("recipe_bottle_healing"));
            var ready = CampTips.Resolve(c, s, tip);
            Assert.That(ready.Target, Is.EqualTo(CampTips.CraftButton));
            Assert.That(ready.Text, Does.Contain("Healing Potion").And.Contain("Wolf Pelt").And.Contain("Tap Brew"));
        }

        [Test]
        public void The_forge_tip_walks_the_first_forge()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            CampNpcs.JoinAll(s);
            var tip = CampTips.For(Forging.TipStation);
            Assert.That(CampTips.Pending(s, Forging.TipStation), Is.Not.Null, "the Forge tab has its own tip");
            s.AddItem("mat_broken_blade", 1);
            var short1 = CampTips.Resolve(c, s, tip);
            Assert.That(short1.Target, Does.StartWith(CampTips.RecipeRowPrefix + "recipe_starter_"));
            Assert.That(short1.Text, Does.Contain("Bring back"));
            s.AddItem("mat_broken_blade", 1); s.AddItem("mat_iron_ore", 1);
            var ready = CampTips.Resolve(c, s, tip);
            Assert.That(ready.Target, Is.EqualTo(CampTips.CraftButton));
            Assert.That(ready.Text, Does.Contain("Training Rapier").And.Contain("2 Broken Blade").And.Contain("Tap Craft"));
            Assert.That(ready.Text, Does.Contain(c.Class(Forging.First(c, s).Hero.ClassId).Name));
            s.Flags.Add(Forging.FirstForgeFlag);
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "after the first forge the tip reads as written");
        }

        [Test]
        public void The_armor_tip_walks_the_first_armor_piece()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            CampNpcs.JoinAll(s);
            s.Gold = 200;
            var tip = CampTips.For(Forging.ArmorTipStation);
            Assert.That(tip, Is.Not.Null, "the Forge has an armor tip");
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "before the first weapon there's no armor step");
            s.Flags.Add(Forging.FirstForgeFlag);
            s.AddItem("mat_leather", 1);
            var short1 = CampTips.Resolve(c, s, tip);
            Assert.That(short1.Target, Does.StartWith(CampTips.RecipeRowPrefix + "recipe_"));
            Assert.That(short1.Text, Does.Contain("Bring back"));
            s.AddItem("mat_leather", 1); s.AddItem("mat_wolf_pelt", 1);
            var ready = CampTips.Resolve(c, s, tip);
            Assert.That(ready.Target, Is.EqualTo(CampTips.CraftButton));
            Assert.That(ready.Text, Does.Contain("Wayfarer's Trousers").And.Contain("2 Leather").And.Contain("Barbarian").And.Contain("Tap Craft"));
            s.Flags.Add(Forging.FirstArmorFlag);
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "after the first armor the tip reads as written");
        }

        /// <summary>GG-T179 (§25.0.8): once the first armor piece is worn, the Enchantress's tip walks binding the first
        /// armor enchant into it — what's missing first, then ringing Enchant — and reads as written once that's done.</summary>
        [Test]
        public void The_enchantress_tip_walks_the_first_armor_enchant()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            CampNpcs.JoinAll(s);
            var tip = CampTips.For("enchantress");
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "no armor worn: the tip reads as written");
            s.Flags.Add(Forging.FirstForgeFlag);
            s.Gold = 200;
            s.AddItem("mat_leather", 2); s.AddItem("mat_wolf_pelt", 1);
            Assert.That(Forging.Forge(c, s, c.Recipes["recipe_wayfarer_legs"]).Ok, Is.True);
            s.AddItem("mat_frog_gland", 2);
            var short1 = CampTips.Resolve(c, s, tip);
            Assert.That(short1.Target, Is.EqualTo(ArmorEnchanting.EnchantRowPrefix + "ae_regeneration"));
            Assert.That(short1.Text, Does.Contain("Bring back").And.Contain("Arcane Dust").And.Contain("Wayfarer's Trousers"));
            s.AddItem("mat_arcane_dust", 1);
            var ready = CampTips.Resolve(c, s, tip);
            Assert.That(ready.Target, Is.EqualTo(CampTips.CraftButton));
            Assert.That(ready.Text, Does.Contain("Regeneration").And.Contain("Barbarian").And.Contain("Tap Enchant"));
            var plan = ArmorEnchanting.First(c, s);
            Assert.That(ArmorEnchanting.Enchant(c, s, plan.Target.Hero, plan.Target.Slot, plan.Enchant).Ok, Is.True);
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "after the first enchant the tip reads as written");
        }

        /// <summary>GG-T180 (§25.0.9): once the first armor enchant is bound, the Abilities screen's training tip names the
        /// guided first skill and its hero and rings Learn; it reads as written once that skill is trained.</summary>
        [Test]
        public void The_training_tip_walks_the_first_skill_after_the_first_enchant()
        {
            var c = TestKit.Real;
            var s = Progression.NewGame(c, "T");
            var tip = CampTips.For(Training.TipStation);
            Assert.That(Training.Step(s), Is.False, "no enchant yet: training isn't introduced");
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip));
            s.Flags.Add(ArmorEnchanting.FirstEnchantFlag);
            var plan = Training.First(c, s);
            Assert.That(plan, Is.Not.Null);
            var ready = CampTips.Resolve(c, s, tip);
            Assert.That(ready.Target, Is.EqualTo(CampTips.CraftButton));
            string hero = c.Class(plan.Hero.ClassId).Name;
            Assert.That(ready.Text, Does.Contain(plan.Skill.Name).And.Contain(hero).And.Contain("free").And.Contain("Tap Learn"));
            Assert.That(Abilities.Learn(c, s, plan.Hero, plan.Skill.Id).Ok, Is.True);
            Assert.That(CampTips.Resolve(c, s, tip), Is.SameAs(tip), "after the first skill the tip reads as written");
        }

        [Test]
        public void Every_tip_has_one_station_a_title_and_a_sentence()
        {
            Assert.That(CampTips.All.Select(t => t.Station).Distinct().Count(), Is.EqualTo(CampTips.All.Count));
            foreach (var t in CampTips.All)
            {
                Assert.That(t.Title, Is.Not.Empty, t.Station);
                Assert.That(t.Text, Is.Not.Empty, t.Station);
            }
        }
    }
}
