using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Exercises every entry of the real content catalogs through the engine (no ability is "data only").</summary>
    public class CatalogTests
    {

        public static IEnumerable<object[]> HeroAbilities() =>
            Real.Abilities.Values.Where(GridGame.Meta.Abilities.IsHeroUsable).Select(a => new object[] { a.Id });
        [TestCaseSource(nameof(HeroAbilities))]
        public void Every_hero_ability_resolves_against_a_live_board(string abilityId)
        {
            var c = Real;
            var a = c.Ability(abilityId);
            var hero = c.Classes.Values.First(x => x.Team == Team.Hero);
            var enemy = c.Classes.Values.First(x => x.Team == Team.Enemy && x.Footprint.W == 1 && x.SnakeSegments == 0 && x.ChargeAbility == null && x.Trap == null);
            var stage = new StageDef { Id = "cat", IsTest = true, Waves = { new WaveDef { Enemies = { new SpawnDef { Class = enemy.Id, Level = 1, X = 2, Y = 2 }, new SpawnDef { Class = enemy.Id, Level = 1, X = 3, Y = 2 } } } } };
            var setup = new BattleSetup { Stage = stage, Seed = 11, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = hero.Id, Level = 5, Start = new Coord(2, 3), Bar = new List<string> { a.Id } });
            setup.Party.Add(new HeroSpec { ClassId = hero.Id == "cleric" ? "paladin" : "cleric", Level = 5, Start = new Coord(4, 4) });
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            s.Bank.Add(12); // wilds pay any colored cost
            var h = s.Units.First(u => u.IsHero);
            h.Hp = h.MaxHp / 2; // so heals have something to do
            var status = BattleEngine.GetSlotStatus(s, h.Id, 0);
            Assert.That(status, Is.EqualTo(SlotStatus.Ready));
            Coord target = h.Pos;
            if (a.Mode != TargetMode.Auto)
            {
                var targets = BattleEngine.ValidTargets(s, h.Id, 0);
                Assert.That(targets.Count > 0, Is.True, $"{a.Id}: no valid targets");
                target = targets[0];
            }
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = h.Id, Slot = 0, Target = target });
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.False);
            bool resolvedNow = ev.Any(e => e is AbilityResolvedEvent r && r.AbilityId == a.Id) || ev.Any(e => e is TeleportedEvent);
            bool queued = ev.Any(e => e is CastQueuedEvent q && q.AbilityId == a.Id);
            Assert.That(resolvedNow || queued, Is.True, $"{a.Id} neither resolved nor queued: {string.Join(" | ", ev)}");
            for (int i = 0; i < 6 && queued && !resolvedNow && !s.IsOver; i++)
                resolvedNow = BattleEngine.Apply(s, new ShieldCommand()).Any(e => e is AbilityResolvedEvent r && r.AbilityId == a.Id);
            Assert.That(resolvedNow || s.IsOver, Is.True, $"{a.Id}: cast never resolved");
            AssertInvariants(s);
        }

        public static IEnumerable<object[]> ChargeAbilities() =>
            Real.Abilities.Values.Where(a => a.Id.StartsWith("charge_")).Select(a => new object[] { a.Id });
        [TestCaseSource(nameof(ChargeAbilities))]
        public void Every_enemy_charge_telegraphs_and_resolves(string abilityId)
        {
            var c = Real;
            var caster = c.Classes.Values.FirstOrDefault(x => x.ChargeAbility == abilityId && x.Footprint.W == 1);
            if (caster == null) { TestContext.Out.WriteLine($"{abilityId}: no 1x1 caster uses it"); return; }
            var stage = new StageDef { Id = "chg", IsTest = true, Waves = { new WaveDef { Enemies = { new SpawnDef { Class = caster.Id, Level = 1, X = 0, Y = 0 } } } } };
            var setup = new BattleSetup { Stage = stage, Seed = 5, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "paladin", Level = 20, Start = new Coord(5, 7) });
            var ab = c.Abilities[abilityId];
            // A pure rush (Howl) only goes out to hurry an ally already at a hero (GG-T123): give it one, in range.
            bool rush = ab.Filter == TargetFilter.AllyOnly && ab.BaseHeal <= 0 && ab.ApplyBuff == null;
            if (rush) stage.Waves[0].Enemies.Add(new SpawnDef { Class = caster.Id, Level = 1, X = 2, Y = 1 });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Level = 20, Start = rush ? new Coord(3, 1) : new Coord(0, 5) });
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            // A healing charge only goes out when an ally needs it: wound the caster.
            if (c.Abilities[abilityId].BaseHeal > 0) { var me = s.Units.First(u => !u.IsHero); me.Hp = me.MaxHp / 3; }
            var all = new List<BattleEvent>();
            for (int i = 0; i < 12 && !s.IsOver; i++) all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            Assert.That(all.Any(e => e is CastQueuedEvent q && q.IsCharge), Is.True);
            Assert.That(all.Any(e => e is AbilityResolvedEvent r && r.AbilityId == abilityId), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void Every_consumable_has_a_working_battle_effect_or_is_camp_only()
        {
            var c = Real;
            var problems = new List<string>();
            foreach (var item in c.Items.Values.Where(i => i.Type == ItemType.Consumable))
            {
                if (item.OutOfBattleOnly) { Assert.That(Camp.UsableAtCamp(item), Is.True, $"{item.Id}: camp-only item unusable at camp"); continue; }
                var stage = new StageDef { Id = "itm", IsTest = true, Waves = { new WaveDef { Enemies = { new SpawnDef { Class = "slime00", Level = 1, X = 2, Y = 2 } } } } };
                var setup = new BattleSetup { Stage = stage, Seed = 2, UnlockedSlots = 5 };
                setup.Party.Add(new HeroSpec { ClassId = "paladin", Level = 3, Start = new Coord(2, 3), Bar = new List<string> { "item:" + item.Id }, ItemCounts = { [item.Id] = 2 } });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Level = 3, Start = new Coord(4, 4) });
                setup.Party.Add(new HeroSpec { ClassId = "barbarian", Level = 3, Start = new Coord(0, 7) });
                var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
                var h = s.Units.First(u => u.IsHero);
                h.Hp = 5;
                var buffDef = c.TryBuff(item.CuresBuff) ?? c.TryBuff("poisoned");
                h.Buffs.Add(new BuffInstance { Def = buffDef, Remaining = 3 });
                if (item.Revives) { var dead = s.Units.First(u => u.Def.Id == "barbarian"); dead.Hp = 0; dead.OnBoard = false; }
                var a = BattleEngine.SlotAbility(s, h, 0);
                if (a == null) { problems.Add($"{item.Id}: no in-battle effect"); continue; }
                var targets = a.Mode == TargetMode.Auto ? new List<Coord> { h.Pos } : BattleEngine.ValidTargets(s, h.Id, 0);
                if (targets.Count == 0) { problems.Add($"{item.Id}: no target"); continue; }
                var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = h.Id, Slot = 0, Target = targets.Contains(h.Pos) ? h.Pos : targets[0] });
                if (!ev.Any(e => e is ItemUsedEvent)) { problems.Add($"{item.Id}: refused {string.Join(" | ", ev.OfType<AbilityRefusedEvent>())}"); continue; }
                bool effect = ev.Any(e => e is HealedEvent || e is DamagedEvent || e is RevivedEvent || e is BattleEndedEvent || e is OrbMintedEvent || (e is BuffRemovedEvent r && (r.Reason == "cured" || r.Reason == "cleansed")));
                if (!effect) problems.Add($"{item.Id}: used but nothing happened");
                AssertInvariants(s);
            }
            Assert.That(problems.Count == 0, Is.True, string.Join("\n", problems));
        }

        [Test]
        public void Smoke_bomb_escapes_and_keeps_wounds()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Esc");
            save.AddItem("smoke_bomb", 1);
            save.Hero("paladin").Bar[0] = "item:smoke_bomb";
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[0].Id, 1);
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            var pal = s.Units.First(u => u.Def.Id == "paladin");
            pal.Hp = 20;
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = pal.Id, Slot = 0, Target = pal.Pos });
            Assert.That(s.Phase, Is.EqualTo(BattlePhase.Escaped));
            int gold = save.Gold;
            var rep = BattleFactory.Commit(c, save, s);
            Assert.That(rep.Escaped, Is.True);
            Assert.That(save.Gold, Is.EqualTo(gold));
            Assert.That(save.Hero("paladin").Hp, Is.EqualTo(20));
            Assert.That(save.Count("smoke_bomb"), Is.EqualTo(0));
        }

        [Test]
        public void Camp_potion_heals_a_wounded_hero_and_tent_heals_everyone()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Camp");
            var h = save.Hero("paladin");
            h.Hp = 10;
            save.AddItem("healing_potion_basic", 1);
            int before = save.Count("healing_potion_basic");
            Assert.That(Camp.Use(c, save, "healing_potion_basic", "paladin").Ok, Is.True);
            Assert.That(h.Hp > 10 || h.Hp == 0, Is.True);
            Assert.That(save.Count("healing_potion_basic"), Is.EqualTo(before - 1));
            save.Hero("cleric").Hp = 3;
            save.AddItem("tent", 1);
            Assert.That(Camp.Use(c, save, "tent", null).Ok, Is.True);
            foreach (var x in save.Roster) Assert.That(x.Hp, Is.EqualTo(0));
        }

        [Test]
        public void Every_recipe_is_craftable_with_its_inputs()
        {
            var c = Real;
            foreach (var r in c.Recipes.Values)
            {
                // Station tier-gating (§24.6.1, GG-T35) is exercised separately; max both stations here
                // so this test keeps checking only that a recipe's own inputs are sufficient.
                var s = new SaveData { Gold = r.Gold, StationXp = { ["blacksmith"] = 9999, ["alchemist"] = 9999 } };
                s.Flags.Add(Forging.FirstForgeFlag); // the onboarding gate on armor-set recipes (GG-T178) is tested in EconomyLoopTests
                foreach (var ic in r.Inputs) s.AddItem(ic.Item, ic.Count);
                if (r.BaseItem != null) s.AddItem(r.BaseItem, 1);
                var res = Crafting.Craft(c, s, r);
                Assert.That(res.Ok, Is.True, $"{r.Id}: {res.Message}");
                Assert.That(s.Count(r.Result) >= r.ResultCount, Is.True);
            }
        }

        /// <summary>GG-T44: every healing/cure consumable the Merchant sells must also be brewable at
        /// the Alchemist, and brewing it (gold only — its reagents come from field drops) must cost
        /// meaningfully less per unit than just buying it, so gathering-and-mixing stays the efficient
        /// path while the shop stays the convenient one.</summary>
        [Test]
        public void Every_sold_healing_or_cure_potion_is_meaningfully_cheaper_brewed_than_bought()
        {
            var c = Real;
            bool IsHealingOrCure(ItemDef i) => i.Type == ItemType.Consumable && (i.BaseHealing > 0 || i.CuresBuff != null || i.CuresAll || i.Revives);
            var potions = c.Items.Values.Where(i => i.SoldByVendor && IsHealingOrCure(i)).ToList();
            Assert.That(potions, Is.Not.Empty);
            foreach (var potion in potions)
            {
                var recipe = c.Recipes.Values.FirstOrDefault(r => r.Station == CraftStation.Alchemy && r.Result == potion.Id);
                Assert.That(recipe != null, Is.True, $"{potion.Id}: sold by the Merchant but has no Alchemy recipe");
                float brewCostPerUnit = (float)recipe.Gold / recipe.ResultCount;
                Assert.That(brewCostPerUnit < potion.BaseCost * 0.6f, Is.True, $"{potion.Id}: brewing ({brewCostPerUnit}g/unit) isn't meaningfully cheaper than buying ({potion.BaseCost}g)");
            }
        }

        /// <summary>GG-T84: the Merchant sells the baseline, Common-tier, unenhanced weapons, armor and
        /// relics fresh at full durability, so "buy replacements" is always possible at camp. Anything
        /// rarer or enhanced (+N) stays drop/forge-only, and where the Forge can make a sold piece, forging
        /// it (gold only — its materials come from field drops) stays meaningfully cheaper than buying.</summary>
        [Test]
        public void The_merchant_sells_fresh_baseline_gear_and_only_baseline_gear()
        {
            var c = Real;
            var sold = Shop.Stock(c).Where(i => i.Type == ItemType.Equipment).ToList();
            Assert.That(sold.Any(i => i.Slot == EquipmentSlot.Weapon), Is.True, "the Merchant sells no fresh weapon");
            Assert.That(sold.Any(i => i.Slot == EquipmentSlot.Torso), Is.True, "the Merchant sells no fresh body armor");
            Assert.That(sold.Any(i => i.Durability <= 0), Is.True, "the Merchant sells no relic");
            var enhanced = new HashSet<string>(c.Recipes.Values.Where(r => r.BaseItem != null).Select(r => r.Result));
            foreach (var item in sold)
            {
                Assert.That(item.Rarity, Is.EqualTo(ItemRarity.Common), $"{item.Id}: only Common gear should be vendor-sold");
                Assert.That(enhanced.Contains(item.Id), Is.False, $"{item.Id}: an enhanced (+N) tier shouldn't be vendor-sold");
                var forge = c.Recipes.Values.FirstOrDefault(r => r.Station == CraftStation.Forge && r.Result == item.Id && r.BaseItem == null);
                if (forge != null)
                    Assert.That(forge.Gold < item.BaseCost * 0.6f, Is.True, $"{item.Id}: forging ({forge.Gold}g) isn't meaningfully cheaper than buying ({item.BaseCost}g)");

                var s = new SaveData { Gold = item.BaseCost };
                var r2 = Shop.Buy(c, s, new Dictionary<string, int> { [item.Id] = 1 });
                Assert.That(r2.Ok, Is.True, $"{item.Id}: {r2.Message}");
                Assert.That(s.Count(item.Id), Is.EqualTo(1));
                Assert.That(s.Worn.ContainsKey(item.Id), Is.False, $"{item.Id}: a bought piece must arrive factory-fresh, not worn");
                Assert.That(Shop.MaxBuy(s, item), Is.EqualTo(0), $"{item.Id}: gear is one-per-bag");
            }
        }

        public static IEnumerable<object[]> PotencyRecipes() =>
            Real.Recipes.Values.Where(r => r.PotencyResult != null).Select(r => new object[] { r.Id });
        [TestCaseSource(nameof(PotencyRecipes))]
        public void Brewing_with_the_potency_reagent_yields_the_greater_result_instead(string recipeId)
        {
            var c = Real;
            var r = c.Recipes[recipeId];
            var s = new SaveData { Gold = r.Gold, StationXp = { ["blacksmith"] = 9999, ["alchemist"] = 9999 } };
            foreach (var ic in r.PotencyInputs) s.AddItem(ic.Item, ic.Count);
            Assert.That(Crafting.HasInputs(s, r), Is.False, $"{recipeId}: potency reagents shouldn't also satisfy the base recipe");
            Assert.That(Crafting.HasPotencyInputs(s, r), Is.True);
            var res = Crafting.Craft(c, s, r, potency: true);
            Assert.That(res.Ok, Is.True, $"{recipeId}: {res.Message}");
            Assert.That(s.Count(r.PotencyResult), Is.EqualTo(r.PotencyResultCount));
            Assert.That(s.Count(r.Result), Is.EqualTo(0));
            foreach (var ic in r.PotencyInputs) Assert.That(s.Count(ic.Item), Is.EqualTo(0));
            var greater = c.Item(r.PotencyResult);
            var basic = c.Item(r.Result);
            Assert.That(greater.BaseHealing >= basic.BaseHealing && (greater.BaseHealing > basic.BaseHealing || greater.CuresAll && !basic.CuresAll), Is.True, $"{recipeId}: {greater.Id} should be a strict upgrade over {basic.Id}");
        }

        [Test] // GG-T47
        public void Every_weapon_upgrade_lines_top_step_has_a_named_passive()
        {
            var c = Real;
            var baseItems = c.Recipes.Values.Where(r => r.Station == CraftStation.Forge && r.BaseItem != null).Select(r => r.BaseItem).ToHashSet();
            var tops = c.Recipes.Values.Where(r => r.Station == CraftStation.Forge && r.BaseItem != null && !baseItems.Contains(r.Result))
                .Select(r => r.Result).Distinct().ToList();
            Assert.That(tops.Count, Is.EqualTo(100)); // one upgrade line per base weapon (GG-T181)
            foreach (var id in tops)
            {
                var item = c.Items[id];
                Assert.That(string.IsNullOrEmpty(item.PassiveName), Is.False, $"{id} has no named passive");
                Assert.That(string.IsNullOrEmpty(item.PassiveDescription), Is.False, $"{id} has no passive description");
                Assert.That(item.PincerApplyBuff != null || item.LifestealFraction > 0f || item.AdjacentAllyDamageReduction > 0f, Is.True, $"{id}'s passive ({item.PassiveName}) has no mechanic wired");
            }
        }

        [Test]
        public void Every_class_can_be_instantiated_and_has_art_reference()
        {
            foreach (var cls in Real.Classes.Values)
            {
                Assert.That(string.IsNullOrEmpty(cls.Name), Is.False, cls.Id);
                var st = GridGame.Core.Rules.Formulas.StatsAtLevel(cls, 10);
                Assert.That(GridGame.Core.Rules.Formulas.MaxHp(st, 10) > 0, Is.True, cls.Id);
                Assert.That(System.IO.File.Exists(System.IO.Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Thumbs", cls.Id + ".png")), Is.True, $"missing thumb for {cls.Id}");
            }
        }
    }
}
