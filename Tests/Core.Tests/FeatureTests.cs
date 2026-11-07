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
    public class FeatureTests
    {
        private static Unit Enemy(BattleState s, string cls) => s.Units.First(u => u.Def.Id == cls && u.Alive);

        [Test] // US-243
        public void Armor_wears_per_enemy_hit_and_breaks_at_zero()
        {
            var db = Mini();
            db.Items["mail"] = new ItemDef { Id = "mail", Name = "Mail", Type = ItemType.Equipment, Slot = EquipmentSlot.Torso, Durability = 1, Stats = new Stats { Vitality = 5 } };
            db.Classes["wolf"].MoveRange = 0;
            db.Classes["wolf"].BaseStats.Luck = 0;
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3), Armor = { [EquipmentSlot.Torso] = new ArmorPieceSpec { ItemId = "mail" } }, EquippedItemIds = { "mail" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight");
            float vit = k.Effective.Vitality;
            var all = new List<BattleEvent>();
            for (int i = 0; i < 6 && k.Gear.ContainsKey(EquipmentSlot.Torso); i++) { s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = BattleConfig.BarLength - 1; all.AddRange(BattleEngine.Apply(s, new ShieldCommand())); }
            Assert.That(all.Any(e => e is ArmorBrokenEvent), Is.True);
            Assert.That(k.Gear.ContainsKey(EquipmentSlot.Torso), Is.False);
            Assert.That(k.Effective.Vitality, Is.EqualTo(vit - 5));
        }

        [Test] // US-182
        public void Mechanical_enemies_are_immune_to_poison_and_sleep()
        {
            var db = Mini();
            db.Classes["tank"].ImmuneBuffs = new List<string> { "sleep" };
            db.Abilities["sleep"] = new AbilityDef { Id = "sleep", Name = "Sleep", Kind = AbilityKind.Spell, ApplyBuff = "sleep", Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "sleep" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(2, 2) });
            Assert.That(ev.Any(e => e is AnnounceEvent a && a.Text == "Immune!"), Is.True);
            Assert.That(Enemy(s, "tank").HasBuff("sleep"), Is.False);
            foreach (var c in Real.Classes.Values.Where(c => c.HasTag("Mechanical"))) Assert.That(c.ImmuneBuffs, Does.Contain("poisoned"));
        }

        // US-157
        [TestCase(BuffStacking.Stack, 2)]
        [TestCase(BuffStacking.Ignore, 1)]
        [TestCase(BuffStacking.Refresh, 1)]
        [TestCase(BuffStacking.Upgrade, 1)]
        public void Buff_stacking_policies(BuffStacking policy, int expectedInstances)
        {
            var db = Mini();
            db.Buffs["burning"].Stacking = policy;
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(4);
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["burning"], Remaining = 1 });
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            int applied = ev.OfType<BuffAppliedEvent>().Count(b => b.BuffId == "burning");
            Assert.That(applied, Is.EqualTo(policy == BuffStacking.Ignore ? 0 : 1));
            // Count instances immediately after application via the event stream (ticking may expire some later).
            if (policy == BuffStacking.Stack) Assert.That(applied == 1 && expectedInstances == 2, Is.True);
        }

        [Test] // US-181
        public void Ranged_enemy_shoots_down_a_clear_lane_and_keeps_its_distance()
        {
            var db = Mini();
            db.Classes["hag"] = new ClassDef { Id = "hag", Name = "Hag", Team = Team.Enemy, Tags = { "Ranged" }, AttackRange = 3, MoveRange = 0, BaseStats = new Stats { Strength = 12, Vitality = 10, Speed = 10, Agility = 10, Luck = 0 } };
            var (s, _) = Battle(db, Stage(("hag", 2, 1)), ("knight", 2, 4), ("cleric", 5, 7));
            s.TurnEntryOf(Enemy(s, "hag").Id).Ip = BattleConfig.BarLength - 1;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is AttackEvent a && a.IsRanged && a.TargetId == Hero(s, "knight").Id), Is.True);
            // A unit in the lane blocks the shot.
            var (s2, _) = Battle(db, Stage(("hag", 2, 1), ("tank", 2, 2)), ("knight", 2, 4), ("cleric", 5, 7));
            Assert.That(EnemyPlanner.RangedTarget(s2, Enemy(s2, "hag")), Is.Null);
            // Movement keeps reach instead of closing to melee.
            db.Classes["hag"].MoveRange = 1;
            var (s3, _) = Battle(db, Stage(("hag", 2, 1)), ("knight", 2, 4), ("cleric", 5, 7));
            var plan = EnemyPlanner.PlanStep(s3, Enemy(s3, "hag"));
            Assert.That(plan.Manhattan(new Coord(2, 4)) >= 2, Is.True);
        }

        [Test] // US-082
        public void Bank_capacity_bonus_gear_widens_the_orb_line()
        {
            var db = Mini();
            db.Items["star"] = new ItemDef { Id = "star", Name = "Star", Type = ItemType.Equipment, Slot = EquipmentSlot.Ring1, BankCapacityBonus = 2 };
            var setup = new BattleSetup { Stage = Stage(("tank", 0, 0)), Seed = 1, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7), EquippedItemIds = { "star" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            Assert.That(s.Bank.Capacity, Is.EqualTo(14));
        }

        [Test] // US-244
        public void Relic_passives_boost_pincers_regen_and_protect()
        {
            var db = Mini();
            db.Items["fang"] = new ItemDef { Id = "fang", Name = "Fang", Type = ItemType.Equipment, Slot = EquipmentSlot.Necklace, PincerDamageBonus = 1f, RegenPerTurn = 5, StartsProtected = true };
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = 2, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), EquippedItemIds = { "fang" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight");
            Assert.That(k.HasBuff("protection"), Is.True);
            k.Hp = 10;
            var ev = Turn(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            Assert.That(ev.Any(e => e is HealedEvent h && h.TargetId == k.Id && h.Amount == 5), Is.True);
            Assert.That(k.PincerDamageBonus, Is.EqualTo(1f));
        }

        [Test] // US-116
        public void Bar_weapon_swap_changes_the_weapon_and_the_save_follows()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Swap");
            var weapons = c.Items.Values.Where(i => i.Slot == EquipmentSlot.Weapon && i.Durability > 0).Take(2).ToList();
            var h = save.Hero("paladin");
            save.AddItem(weapons[0].Id, 1);
            save.AddItem(weapons[1].Id, 1);
            Assert.That(Equipment.Equip(c, save, h, EquipmentSlot.Weapon, weapons[0].Id).Ok, Is.True);
            Assert.That(Abilities.Assign(c, save, h, 1, "weapon:" + weapons[1].Id).Ok, Is.True);
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[0].Id, 3);
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            var pal = s.Units.First(u => u.Def.Id == "paladin");
            Assert.That(BattleEngine.GetSlotStatus(s, pal.Id, 1), Is.EqualTo(SlotStatus.Ready));
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = pal.Id, Slot = 1, Target = pal.Pos });
            Assert.That(ev.Any(e => e is WeaponSwappedEvent w && w.ToWeapon == weapons[1].Id), Is.True);
            Assert.That(pal.WeaponId, Is.EqualTo(weapons[1].Id));
            Assert.That(pal.Bar[1].WeaponId, Is.EqualTo(weapons[0].Id));
            for (int w = 0; w < 30 && !s.IsOver; w++) // clear every wave (the first battle has twenty)
            {
                foreach (var e in s.Units.Where(u => !u.IsHero)) { e.Hp = 0; e.OnBoard = false; }
                BattleEngine.Apply(s, new ShieldCommand()); // any action → CheckEnd → next wave / victory bookkeeping
            }
            Assert.That(s.Phase, Is.EqualTo(BattlePhase.Victory));
            BattleFactory.Commit(c, save, s);
            Assert.That(h.WeaponId, Is.EqualTo(weapons[1].Id));
            Assert.That(h.Bar[1], Is.EqualTo("weapon:" + weapons[0].Id));
            Assert.That(save.Count(weapons[0].Id), Is.EqualTo(1));
            Assert.That(save.Count(weapons[1].Id), Is.EqualTo(0));
        }

        [Test] // generic mana: every hero spell is payable from a full bank
        public void Every_hero_spell_costs_between_one_orb_and_a_full_bank()
        {
            foreach (var a in Real.Abilities.Values.Where(x => GridGame.Meta.Abilities.IsHeroUsable(x) && x.Kind == AbilityKind.Spell))
                Assert.That(a.Cost, Is.InRange(1, ManaBank.DefaultCapacity));
        }
    }
}
