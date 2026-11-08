using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>GG-T371 (BIBLE §3.3.1), owner 2026-11-08: "always a way to defeat them (cast fire against ice, be able to
    /// buy a consumable with elemental effects in case your party doesn't have what it needs specced into the build, there
    /// is always a way)." The elemental interaction table (elements.json), the Merchant's flasks and board tools, the
    /// "always a way" validator, and the tile reactions in battle.</summary>
    public class AlwaysAWayTests
    {
        /// <summary>A private copy of the real content (TestKit.Real is shared, so never mutate it).</summary>
        private static ContentDb Fresh() => ContentLoader.Load(name =>
        {
            var p = Path.Combine(ContentDir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        });

        private static List<string> Check(ContentDb db)
        {
            var p = new List<string>();
            ElementCounters.Validate(db, p);
            return p;
        }

        // ── the real content ──

        [Test]
        public void The_real_content_always_has_a_way()
        {
            var problems = Check(Real);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(ContentLoader.Validate(Real).Where(p => p.Contains("GG-T371")), Is.Empty);
        }

        [Test]
        public void Every_damage_type_has_one_row_and_the_table_matches_the_engine()
        {
            var t = Real.Elements;
            foreach (DamageType d in Enum.GetValues(typeof(DamageType)))
                Assert.That(t.Elements.Count(e => e.Type == d), Is.EqualTo(1), $"{d}");
            // The dispatcher hard-codes these (§6, BattleRunner.Abilities): Fire thaws Frozen and steams Wet off; Lightning on Wet ×1.5.
            Assert.That(t.Of(DamageType.Fire).Breaks, Is.EquivalentTo(new[] { "frozen", "wet" }));
            Assert.That(t.Of(DamageType.Lightning).Exploits, Is.EquivalentTo(new[] { "wet" }));
            // The owner's example: fire against ice, and back.
            Assert.That(t.Of(DamageType.Fire).Counters, Does.Contain(DamageType.Ice));
            Assert.That(t.Of(DamageType.Ice).Counters, Does.Contain(DamageType.Fire));
            Assert.That(t.Of(DamageType.Holy).Counters, Does.Contain(DamageType.Dark));
        }

        [Test]
        public void The_merchant_sells_a_flask_of_every_element_from_the_start_priced_against_the_potions()
        {
            var c = Real;
            var stock = Shop.Stock(c);
            int potion = c.Item("healing_potion_basic").BaseCost, grenade = c.Item("cons_grenade").BaseCost;
            foreach (DamageType d in Enum.GetValues(typeof(DamageType)))
            {
                if (d == DamageType.Physical) continue;
                var flask = stock.FirstOrDefault(i => ElementCounters.DamageOf(BattleEngine.ResolveAbilityId(c, "item:" + i.Id)) == d);
                Assert.That(flask, Is.Not.Null, $"no {d} consumable on the Merchant's shelf");
                Assert.That(flask.Rarity, Is.EqualTo(ItemRarity.Common), flask.Id);
                Assert.That(flask.BaseCost, Is.InRange(potion, grenade), $"{flask.Id}: priced between a Healing Potion and a Hand Grenade");
            }
            foreach (var id in new[] { "flask_fire", "flask_frost", "flask_storm", "flask_venom", "flask_holy", "flask_shadow", "flask_arcane", "cons_purifying_salts", "cons_disarm_kit" })
            {
                var item = c.Item(id);
                Assert.That(stock, Does.Contain(item), $"{id} is always on the shelf");
                var s = new SaveData { Gold = item.BaseCost };
                Assert.That(Shop.Buy(c, s, new Dictionary<string, int> { [id] = 1 }).Ok, Is.True, id);
                Assert.That(c.TryText("item:" + id), Is.Not.Null, $"{id}: its description is Prose text (item:{id})");
            }
        }

        [Test]
        public void Every_trap_has_a_merchant_tool_or_element_that_clears_it()
        {
            var c = Real;
            var shelf = ElementCounters.Purchasable(c);
            foreach (var trap in c.Traps.Values)
                Assert.That(shelf.Any(x => ElementCounters.Match(ElementCounters.ReactionsOf(c, x.Ability), trap.Id) != null), Is.True, trap.Id);
            Assert.That(ElementCounters.Match(ElementCounters.ReactionsOf(c, c.Ability("disarm_kit_use")), "caltrops"), Is.Not.Null);
            Assert.That(ElementCounters.Match(ElementCounters.ReactionsOf(c, c.Ability("purifying_salts_use")), "slime_trail"), Is.Not.Null);
            Assert.That(ElementCounters.Match(ElementCounters.ReactionsOf(c, c.Ability("flask_fire_burst")), "slime_trail"), Is.Not.Null, "fire burns slime away");
        }

        // ── the validator reports what breaks the promise ──

        [Test]
        public void Validator_reports_a_weakness_the_merchant_cannot_sell()
        {
            var db = Fresh();
            db.Items["flask_holy"].SoldByVendor = false;
            var p = Check(db);
            Assert.That(p.Any(x => x.Contains("lich") && x.Contains("weak to Holy")), Is.True, string.Join("\n", p));
            Assert.That(p.Any(x => x.Contains("elements.json Holy")), Is.True);
        }

        [Test]
        public void Validator_reports_a_foe_that_walls_off_everything_the_merchant_sells()
        {
            var db = Fresh();
            foreach (DamageType d in Enum.GetValues(typeof(DamageType)))
                if (d != DamageType.Physical) db.Classes["lich"].Resistances[d] = 0f;
            var p = Check(db);
            Assert.That(p.Any(x => x.Contains("lich") && x.Contains("no Merchant consumable hurts it")), Is.True, string.Join("\n", p));
        }

        [Test]
        public void Validator_reports_a_foe_that_resists_its_own_counter()
        {
            var db = Fresh();
            db.Classes["infernalist01"].Resistances[DamageType.Ice] = 0.5f; // resists Fire and now Ice, which counters Fire
            var p = Check(db);
            Assert.That(p.Any(x => x.Contains("infernalist01 resists Fire and also Ice")), Is.True, string.Join("\n", p));
        }

        [Test]
        public void Validator_reports_a_rule_breaker_with_no_counter_and_a_trap_nothing_clears()
        {
            var db = Fresh();
            db.Elements.RuleBreaks.RemoveAll(r => r.Rule == RuleBreak.Undying);
            db.Items["cons_disarm_kit"].SoldByVendor = false;
            var p = Check(db);
            Assert.That(p.Any(x => x.Contains("hierarch") && x.Contains("Undying")), Is.True, string.Join("\n", p));
            Assert.That(p.Any(x => x.Contains("trap caltrops")), Is.True, string.Join("\n", p));
        }

        [Test]
        public void Validator_reports_an_element_gated_ward_no_sold_element_breaks()
        {
            var db = Fresh();
            db.Buffs["ice_armour"] = new BuffDef { Id = "ice_armour", Name = "Ice Armour", Kind = BuffKind.Buff, IncomingDamageMultiplier = 0.1f };
            db.Abilities["raise_ice_armour"] = new AbilityDef { Id = "raise_ice_armour", Name = "Ice Armour", Kind = AbilityKind.Skill, Shape = TargetShape.Self, Mode = TargetMode.Auto, ApplyBuff = "ice_armour" };
            db.Classes["lich"].Abilities.Add("raise_ice_armour");
            Assert.That(Check(db).Any(x => x.Contains("[ice_armour]")), Is.True, "nothing breaks the ward yet");
            db.Elements.Of(DamageType.Fire).Breaks.Add("ice_armour");
            Assert.That(Check(db).Any(x => x.Contains("[ice_armour]")), Is.False, "fire breaks it and the Merchant sells fire");
        }

        [Test]
        public void Validator_reports_a_tile_hazard_nothing_clears_and_unknown_tile_ids()
        {
            var db = Fresh();
            var p = new List<string>();
            ElementCounters.Validate(db, p, id => id == "ice_sheet", new[] { "ice_sheet" });
            Assert.That(p.Any(x => x.Contains("tile state ice_sheet")), Is.True, "GG-T370's hazards need a counter too");
            db.Elements.Of(DamageType.Fire).Tiles.Add(new TileReactionDef { On = "ice_sheet" });
            p.Clear();
            ElementCounters.Validate(db, p, id => id == "ice_sheet", new[] { "ice_sheet" });
            Assert.That(p, Is.Empty, string.Join("\n", p));
            p.Clear();
            ElementCounters.Validate(db, p); // no GG-T359 tile states known: the id is unknown
            Assert.That(p.Any(x => x.Contains("unknown trap or tile state 'ice_sheet'")), Is.True);
        }

        // ── in battle ──

        private static ContentDb MiniWithTools()
        {
            var db = Mini();
            db.Traps["caltrops"] = new TrapDef { Id = "caltrops", Name = "Caltrops", Damage = 8 };
            db.Traps["slime_trail"] = new TrapDef { Id = "slime_trail", Name = "Slime Trail" };
            db.Elements = new ElementTableDef
            {
                Elements = { new ElementDef { Type = DamageType.Fire, Tiles = { new TileReactionDef { On = "slime_trail" } } } },
                Reactions = { new ReactionSetDef { Id = "disarm", Tiles = { new TileReactionDef { On = "caltrops" } } } },
            };
            db.Abilities["disarm"] = new AbilityDef { Id = "disarm", Name = "Disarm Kit", Kind = AbilityKind.Item, Shape = TargetShape.Square, Radius = 1, Mode = TargetMode.PickTile, Filter = TargetFilter.Any, TileReaction = "disarm" };
            db.Abilities["fire_flask"] = new AbilityDef { Id = "fire_flask", Name = "Fire Flask", Kind = AbilityKind.Item, BaseDamage = 20, DamageType = DamageType.Fire, Shape = TargetShape.Cross, Radius = 1, Mode = TargetMode.PickTile, Filter = TargetFilter.EnemyOnly };
            db.Items["kit"] = new ItemDef { Id = "kit", Name = "Disarm Kit", Type = ItemType.Consumable, OnUseAbility = "disarm", MaxStack = 5, BaseCost = 40, SoldByVendor = true };
            db.Items["flask"] = new ItemDef { Id = "flask", Name = "Fire Flask", Type = ItemType.Consumable, OnUseAbility = "fire_flask", MaxStack = 5, BaseCost = 55, SoldByVendor = true };
            return db;
        }

        [Test]
        public void The_disarm_kit_pulls_a_mine_with_nobody_near_it()
        {
            var db = MiniWithTools();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var at = new Coord(2, 4);
            s.Traps.Add(new Trap { At = at, TrapId = "caltrops", OwnerId = -1 });
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { ItemId = "kit", Charges = 1, MaxCharges = 5 };
            Assert.That(BattleEngine.ValidTargets(s, k.Id, 0), Does.Contain(at), "a tile beside a trap is a fair pick for the kit");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = at });
            Assert.That(ev.OfType<TrapClearedEvent>().Single().TrapId, Is.EqualTo("caltrops"));
            Assert.That(s.TrapAt(at), Is.Null);
            Assert.That(ev.OfType<AbilityRefusedEvent>(), Is.Empty);
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Missed!"), Is.False, "a tile-only tool never misses");
            AssertInvariants(s);
        }

        [Test]
        public void Fire_burns_a_slime_trail_away_but_leaves_a_mine_it_has_no_reaction_to()
        {
            var db = MiniWithTools();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var slime = new Coord(2, 4);
            var mine = new Coord(4, 2);
            s.Traps.Add(new Trap { At = slime, TrapId = "slime_trail", OwnerId = -1 });
            s.Traps.Add(new Trap { At = mine, TrapId = "caltrops", OwnerId = -1 });
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { ItemId = "flask", Charges = 2, MaxCharges = 5 };
            var targets = BattleEngine.ValidTargets(s, k.Id, 0);
            Assert.That(targets, Does.Contain(slime));
            Assert.That(targets, Does.Not.Contain(mine), "fire does nothing to caltrops, and nobody stands there");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = slime });
            Assert.That(ev.OfType<TrapClearedEvent>().Select(e => e.TrapId), Is.EqualTo(new[] { "slime_trail" }));
            Assert.That(s.TrapAt(slime), Is.Null);
            Assert.That(s.TrapAt(mine), Is.Not.Null);
        }

        [Test]
        public void A_flask_on_a_foe_standing_by_a_trap_hits_the_foe_and_clears_the_tile()
        {
            var db = MiniWithTools();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 5, 7), ("cleric", 4, 7));
            var slime = new Coord(2, 4);
            s.Traps.Add(new Trap { At = slime, TrapId = "slime_trail", OwnerId = -1 });
            var tank = s.Units.First(u => u.Def.Id == "tank");
            int hp = tank.Hp;
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { ItemId = "flask", Charges = 1, MaxCharges = 5 };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = tank.Pos });
            Assert.That(tank.Hp, Is.LessThan(hp), "the foe still takes the hit");
            Assert.That(ev.OfType<TrapClearedEvent>().Any(e => e.At == slime), Is.True);
        }

        // ── the camp bot ──

        [Test]
        public void Camp_routine_buys_the_counter_the_next_stage_needs()
        {
            var c = Real;
            var stages = c.CampaignStages();
            int next = stages.FindIndex(st => ElementCounters.CountersFor(c, st).Count > 0);
            Assert.That(next, Is.GreaterThanOrEqualTo(0), "some stage fields a foe that resists or lays traps");
            var s = Progression.NewGame(c, "T");
            s.Flags.Add("npc:" + CampNpcs.Merchant);
            s.HighestClearedStage = next - 1;
            s.Gold = 20000;
            CampRoutine.Run(c, s);
            foreach (var item in ElementCounters.CountersFor(c, stages[next]))
                Assert.That(s.Count(item.Id), Is.GreaterThanOrEqualTo(1), $"{item.Id} for {stages[next].Id}");
        }

        [Test]
        public void Camp_routine_buys_no_counter_without_the_merchant()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            s.Gold = 5000;
            CampRoutine.Run(c, s);
            foreach (var (item, _) in ElementCounters.Purchasable(c).Where(x => x.Item.Id.StartsWith("flask_") || x.Item.Id == "cons_disarm_kit" || x.Item.Id == "cons_purifying_salts"))
                Assert.That(s.Count(item.Id), Is.EqualTo(0), item.Id);
        }
    }
}
