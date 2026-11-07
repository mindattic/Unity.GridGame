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
    /// <summary>GG-T182: 100 armor pieces in 20 named five-piece sets with 2- and 5-piece bonuses.</summary>
    public class ArmorSetTests
    {
        private static readonly EquipmentSlot[] Slots = BattleEngine.ArmorSlots;

        // ── Content ─────────────────────────────────────────────────────────────

        [Test]
        public void Twenty_sets_of_five_make_a_hundred_unique_armor_pieces()
        {
            var c = Real;
            Assert.That(c.ArmorSetOrder.Count, Is.EqualTo(20));
            var pieces = c.ArmorSetOrder.SelectMany(s => s.Pieces).ToList();
            Assert.That(pieces.Count, Is.EqualTo(100));
            Assert.That(pieces.Distinct().Count(), Is.EqualTo(100), "a piece is listed twice");
            Assert.That(pieces.Select(id => c.Item(id).Name).Distinct().Count(), Is.EqualTo(100), "two pieces share a name");
            foreach (var set in c.ArmorSetOrder)
            {
                var items = set.Pieces.Select(c.Item).ToList();
                Assert.That(items.Select(i => i.Slot), Is.EquivalentTo(Slots), $"{set.Id} needs one piece per armor slot");
                Assert.That(items.All(i => i.ArmorWeight == set.Weight), $"{set.Id} mixes weights");
                var lines = items.Select(i => ItemStatsKey(i)).Distinct().Count();
                Assert.That(lines, Is.EqualTo(5), $"{set.Id} has two pieces with the same stat line");
            }
        }

        private static string ItemStatsKey(ItemDef i) =>
            $"{i.Stats.Strength},{i.Stats.Vitality},{i.Stats.Agility},{i.Stats.Speed},{i.Stats.Stamina},{i.Stats.Intelligence},{i.Stats.Wisdom},{i.Stats.Luck}";

        [Test]
        public void Every_set_has_a_two_and_a_five_piece_bonus()
        {
            foreach (var set in Real.ArmorSetOrder)
            {
                Assert.That(set.Bonuses.Select(b => b.Pieces), Is.EquivalentTo(new[] { 2, 5 }), set.Id);
                var five = set.Bonuses.First(b => b.Pieces == 5);
                Assert.That(ArmorSets.Describe(five), Is.Not.Empty, set.Id);
                bool special = five.Resistances.Count > 0 || five.PincerDamageBonus > 0 || five.RegenPerTurn > 0 || five.BattleStartManaOrbs > 0 || five.StartsProtected;
                Assert.That(special, $"{set.Id}'s 5-piece bonus should do more than add stats");
            }
        }

        [Test]
        public void Sets_spread_across_every_weight_and_the_campaign_level_range()
        {
            var c = Real;
            foreach (ArmorWeight w in System.Enum.GetValues(typeof(ArmorWeight)))
                Assert.That(c.ArmorSetOrder.Count(s => s.Weight == w), Is.GreaterThanOrEqualTo(5), $"too few {w} sets");
            int campaignLevels = c.CampaignStages().Count;
            Assert.That(c.ArmorSetOrder.Min(s => s.Level), Is.EqualTo(1));
            Assert.That(c.ArmorSetOrder.Max(s => s.Level), Is.EqualTo(campaignLevels));
            // No stretch of the campaign more than five levels long without a new set.
            var levels = c.ArmorSetOrder.Select(s => s.Level).OrderBy(x => x).ToList();
            for (int i = 1; i < levels.Count; i++) Assert.That(levels[i] - levels[i - 1], Is.LessThanOrEqualTo(5));
            // Higher-level sets are never cheaper or weaker than the first one.
            var first = c.ArmorSetOrder.OrderBy(s => s.Level).First();
            var last = c.ArmorSetOrder.OrderBy(s => s.Level).Last();
            Assert.That(c.Item(last.Pieces[1]).BaseCost, Is.GreaterThan(c.Item(first.Pieces[1]).BaseCost));
            Assert.That(c.Item(last.Pieces[1]).Rarity, Is.GreaterThan(c.Item(first.Pieces[1]).Rarity));
        }

        [Test]
        public void Every_set_piece_has_a_source_drop_shop_or_forge_recipe()
        {
            var c = Real;
            var dropped = new HashSet<string>(c.DropTables.Values.SelectMany(d => d.Entries.Select(e => e.Item)));
            var shop = new HashSet<string>(Shop.Stock(c).Select(i => i.Id));
            var forged = new HashSet<string>(c.Recipes.Values.Where(r => r.Station == CraftStation.Forge && r.BaseItem == null).Select(r => r.Result));
            var sources = new Dictionary<string, int> { ["drop"] = 0, ["shop"] = 0, ["forge"] = 0 };
            foreach (var id in c.ArmorSetOrder.SelectMany(s => s.Pieces))
            {
                bool d = dropped.Contains(id), s = shop.Contains(id), f = forged.Contains(id);
                Assert.That(d || s || f, $"{id} has no drop, shop or forge source");
                if (d) sources["drop"]++;
                if (s) sources["shop"]++;
                if (f) sources["forge"]++;
            }
            Assert.That(sources.Values, Has.All.GreaterThan(0), "every kind of source should be used");
        }

        [Test]
        public void Real_armor_sets_pass_validation()
        {
            var problems = ContentLoader.Validate(Real).Where(p => p.StartsWith("armor set")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // ── Rules ───────────────────────────────────────────────────────────────

        /// <summary>Mini content plus one Light set "testset" whose pieces are "ts_head".."ts_boots".</summary>
        private static ContentDb WithSet(SetBonusDef five = null)
        {
            var db = Mini();
            var set = new ArmorSetDef { Id = "testset", Name = "Test", Weight = ArmorWeight.Light, Level = 1 };
            foreach (var slot in Slots)
            {
                var id = "ts_" + slot.ToString().ToLowerInvariant();
                db.Items[id] = new ItemDef { Id = id, Name = "Test " + slot, Type = ItemType.Equipment, Slot = slot, ArmorWeight = ArmorWeight.Light, Durability = 50, Stats = new Stats { Vitality = 1 } };
                set.Pieces.Add(id);
                db.SetByItem[id] = set;
            }
            set.Bonuses.Add(new SetBonusDef { Pieces = 2, Stats = new Stats { Strength = 4 } });
            set.Bonuses.Add(five ?? new SetBonusDef { Pieces = 5, Stats = new Stats { Strength = 6 }, RegenPerTurn = 3, PincerDamageBonus = 0.1f, Resistances = { [DamageType.Fire] = 0.5f } });
            db.ArmorSets[set.Id] = set;
            db.ArmorSetOrder.Add(set);
            return db;
        }

        private static string[] Pieces(int n) => Slots.Take(n).Select(s => "ts_" + s.ToString().ToLowerInvariant()).ToArray();

        [Test]
        public void Bonus_tiers_switch_on_at_two_and_five_pieces()
        {
            var db = WithSet();
            Assert.That(ArmorSets.ActiveBonuses(db, Pieces(1)), Is.Empty);
            var two = ArmorSets.Progress(db, Pieces(2)).Single();
            Assert.That(two.Worn, Is.EqualTo(2));
            Assert.That(two.Active.Select(b => b.Pieces), Is.EqualTo(new[] { 2 }));
            Assert.That(two.Next.Pieces, Is.EqualTo(5));
            var four = ArmorSets.Progress(db, Pieces(4)).Single();
            Assert.That(four.Active.Count, Is.EqualTo(1));
            var five = ArmorSets.Progress(db, Pieces(5)).Single();
            Assert.That(five.Active.Select(b => b.Pieces), Is.EqualTo(new[] { 2, 5 }));
            Assert.That(five.Next, Is.Null);
            Assert.That(ArmorSets.BonusStats(db, Pieces(5)).Strength, Is.EqualTo(10));
            // The same piece listed twice (or a non-set piece) never counts extra.
            Assert.That(ArmorSets.Progress(db, new[] { Pieces(1)[0], Pieces(1)[0], "fang" }).Single().Worn, Is.EqualTo(1));
        }

        [Test]
        public void Describe_names_every_part_of_a_bonus()
        {
            var b = new SetBonusDef { Pieces = 5, Stats = new Stats { Strength = 3 }, Resistances = { [DamageType.Fire] = 0.6f }, PincerDamageBonus = 0.1f, RegenPerTurn = 2, BattleStartManaOrbs = 1, StartsProtected = true };
            var text = ArmorSets.Describe(b);
            foreach (var part in new[] { "STR +3", "Fire ×0.6", "Pincer +10%", "Regen 2/turn", "+1 orbs at battle start", "Starts battles Protected" })
                Assert.That(text, Does.Contain(part));
        }

        private static BattleState BattleWith(ContentDb db, params string[] armor)
        {
            var spec = new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) };
            foreach (var id in armor)
            {
                spec.Armor[db.Item(id).Slot] = new ArmorPieceSpec { ItemId = id };
                spec.EquippedItemIds.Add(id);
            }
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(spec);
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            return BattleEngine.Create(db, setup, new List<BattleEvent>());
        }

        [Test]
        public void A_hero_wearing_two_pieces_gets_the_two_piece_bonus_in_battle()
        {
            var db = WithSet();
            float one = Hero(BattleWith(db, Pieces(1)), "knight").Effective.Strength;
            var k = Hero(BattleWith(db, Pieces(2)), "knight");
            Assert.That(k.Effective.Strength, Is.EqualTo(one + 4));
            Assert.That(k.SetBonuses.Count, Is.EqualTo(1));
            Assert.That(k.RegenPerTurn, Is.EqualTo(0));
        }

        [Test]
        public void A_full_set_adds_the_five_piece_stats_resistance_regen_and_pincer_bonus()
        {
            var db = WithSet();
            var bare = Hero(BattleWith(db), "knight");
            var k = Hero(BattleWith(db, Pieces(5)), "knight");
            Assert.That(k.Effective.Strength, Is.EqualTo(bare.Effective.Strength + 10));
            Assert.That(k.RegenPerTurn, Is.EqualTo(3));
            Assert.That(k.PincerDamageBonus, Is.EqualTo(0.1f).Within(1e-5));
            Assert.That(k.ResistanceMultiplier(DamageType.Fire), Is.EqualTo(bare.ResistanceMultiplier(DamageType.Fire) * 0.5f).Within(1e-5));
        }

        [Test]
        public void A_full_set_with_battle_start_perks_seeds_orbs_and_protection()
        {
            var db = WithSet(new SetBonusDef { Pieces = 5, BattleStartManaOrbs = 2, StartsProtected = true });
            var bare = BattleWith(db);
            var s = BattleWith(db, Pieces(5));
            Assert.That(Hero(s, "knight").HasBuff("protection"), Is.True);
            Assert.That(Hero(bare, "knight").HasBuff("protection"), Is.False);
            Assert.That(s.Bank.Count, Is.EqualTo(System.Math.Min(bare.Bank.Count + 2, s.Bank.Capacity)));
        }

        [Test]
        public void A_piece_breaking_mid_battle_takes_its_set_bonus_with_it()
        {
            var db = WithSet();
            db.Items["ts_head"].Durability = 1; // the first armor slot breaks on the first landed hit
            db.Classes["wolf"].MoveRange = 0;
            db.Classes["wolf"].BaseStats.Luck = 0;
            var s = BattleWith(db, Pieces(2));
            var k = Hero(s, "knight");
            float withBonus = k.Effective.Strength;
            var all = new List<BattleEvent>();
            for (int i = 0; i < 6 && k.Gear.ContainsKey(EquipmentSlot.Head); i++)
            {
                s.TurnEntryOf(s.Units.First(u => u.Def.Id == "wolf" && u.Alive).Id).Ip = BattleConfig.BarLength - 1;
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            }
            Assert.That(k.Gear.ContainsKey(EquipmentSlot.Head), Is.False, "the head piece should have broken");
            Assert.That(k.SetBonuses, Is.Empty);
            Assert.That(k.Effective.Strength, Is.EqualTo(withBonus - 4));
            Assert.That(all.Any(e => e is AnnounceEvent a && a.Text == "Test set bonus lost"), Is.True);
        }

        // ── Meta (Status / Equip read models) ──────────────────────────────────

        [Test]
        public void Status_preview_and_equip_preview_include_set_progress()
        {
            var db = WithSet();
            var h = new HeroSave { ClassId = "knight" };
            var p = Pieces(2);
            h.Armor[EquipmentSlot.Head] = new EquippedArmor { ItemId = p[0], Durability = 50 };
            float oneStr = Progression.Preview(db, h).Effective.Strength;
            h.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = p[1], Durability = 50 };
            Assert.That(Progression.Preview(db, h).Effective.Strength, Is.EqualTo(oneStr + 4), "Status totals include the 2-piece bonus");
            Assert.That(Equipment.WornSets(db, h).Single().Worn, Is.EqualTo(2));

            var legs = Pieces(3)[2];
            Assert.That(Equipment.WornSetsWith(db, h, EquipmentSlot.Legs, legs).Single().Worn, Is.EqualTo(3));
            Assert.That(Equipment.WornSetsWith(db, h, EquipmentSlot.Torso, null).Single().Worn, Is.EqualTo(1));
            var (now, then) = Equipment.Compare(db, h, EquipmentSlot.Torso, null);
            Assert.That(then.Strength, Is.EqualTo(now.Strength - 4), "the Equip compare shows the bonus going away");
        }

        // ── Validation ──────────────────────────────────────────────────────────

        [Test]
        public void Validate_rejects_a_malformed_set()
        {
            var db = WithSet();
            var set = db.ArmorSets["testset"];
            db.Items["ts_boots"].ArmorWeight = ArmorWeight.Heavy;
            set.Pieces[1] = set.Pieces[0];
            set.Bonuses.Add(new SetBonusDef { Pieces = 7 });
            var problems = string.Join("\n", ContentLoader.Validate(db));
            Assert.That(problems, Does.Contain("is Heavy, set is Light"));
            Assert.That(problems, Does.Contain("two pieces for Head"));
            Assert.That(problems, Does.Contain("bonus at 7 pieces"));
        }
    }
}
