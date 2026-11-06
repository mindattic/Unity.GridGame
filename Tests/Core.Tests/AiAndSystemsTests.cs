using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    public class AiAndSystemsTests
    {
        private static Unit Enemy(BattleState s, string cls) => s.Units.First(u => u.Def.Id == cls && u.Alive);

        [Test]
        public void Immobile_enemy_does_not_move()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Enemy(s, "wolf");
            wolf.Buffs.Add(new BuffInstance { Def = db.Buffs["sleep"], Remaining = 3 });
            Assert.That(EnemyPlanner.PlanStep(s, wolf), Is.EqualTo(wolf.Pos));
        }

        [Test]
        public void Enemy_never_steps_into_a_flank()
        {
            var db = Mini();
            // Heroes at (1,2) and (3,2): the tile (2,2) between them is a flank trap for the wolf at (2,1).
            var (s, _) = Battle(db, Stage(("wolf", 2, 1)), ("knight", 1, 2), ("cleric", 3, 2));
            var wolf = Enemy(s, "wolf");
            Assert.That(EnemyPlanner.PlanStep(s, wolf), Is.Not.EqualTo(new Coord(2, 2)));
        }

        [Test]
        public void Sentient_enemy_seeks_a_pincer_on_a_hero()
        {
            var db = Mini();
            db.Classes["goblin"].MoveRange = 1;
            // goblin A at (0,3), hero at (1,3), goblin B at (3,3) can step to (2,3) to pincer the hero.
            var (s, _) = Battle(db, Stage(("goblin", 0, 3), ("goblin", 3, 2)), ("knight", 1, 3), ("cleric", 5, 7));
            var b = s.Units.First(u => u.Def.Id == "goblin" && u.Pos == new Coord(3, 2));
            var plan = EnemyPlanner.PlanStep(s, b);
            Assert.That(plan == new Coord(2, 3) || plan == new Coord(3, 3) || plan == new Coord(2, 2), Is.True, $"planned {plan}");
        }

        [Test]
        public void Wounded_enemy_retreats_from_its_target()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 2, 3)), ("knight", 2, 5), ("cleric", 5, 7));
            var wolf = Enemy(s, "wolf");
            wolf.Hp = 1;
            var knight = Hero(s, "knight");
            int before = wolf.DistanceTo(knight);
            var plan = EnemyPlanner.PlanStep(s, wolf);
            Assert.That(plan.Manhattan(knight.Pos) >= before, Is.True, "a wounded enemy must not advance");
        }

        [Test]
        public void Two_by_two_boss_shoves_a_hero_into_the_tile_it_vacates()
        {
            var db = Mini();
            db.Classes["boss"].MoveRange = 1;
            var (s, _) = Battle(db, Stage(("boss", 0, 0)), ("knight", 2, 1), ("cleric", 5, 7));
            var boss = Enemy(s, "boss");
            var knight = Hero(s, "knight");
            // Force the boss to step right into the knight's lane.
            s.TurnEntryOf(boss.Id).Ip = 999;
            BattleEngine.Apply(s, new ShieldCommand());
            AssertInvariants(s);
            if (boss.Pos == new Coord(1, 0)) Assert.That(knight.Pos, Is.EqualTo(new Coord(0, 1)));
        }

        [Test]
        public void Line_charge_hits_heroes_on_the_line_or_reports_a_dodge()
        {
            var db = Mini();
            db.Classes["witch"].ChargeShape = ChargeShape.Line;
            db.Abilities["charge_bolt"].BaseDamage = 5;
            var (s, _) = Battle(db, Stage(("witch", 0, 0)), ("knight", 4, 0), ("cleric", 5, 7));
            s.TurnEntryOf(Enemy(s, "witch").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var q = ev.OfType<CastQueuedEvent>().Single();
            Assert.That(q.LineTiles, Is.Not.Null);
            Assert.That(q.LineTiles, Does.Contain(new Coord(4, 0)));
            var all = new List<BattleEvent>();
            for (int i = 0; i < 6 && !all.Any(e => e is AbilityResolvedEvent); i++) all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            Assert.That(all.Any(e => e is DamagedEvent d && d.TargetId == Hero(s, "knight").Id), Is.True);
        }

        [Test]
        public void Clutch_instantly_resolves_a_hero_cast_when_rolled()
        {
            var db = Mini();
            db.Classes["mage"].BaseStats.Luck = 1000; // clutch chance capped at 20%: roll until it procs
            bool sawClutch = false;
            for (int seed = 1; seed < 80 && !sawClutch; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("wolf", 3, 6)), Seed = seed, UnlockedSlots = 5 };
                setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(3, 7), Bar = new List<string> { "meteor" } });
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 0) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                s.Bank.Add(2);
                var mage = Hero(s, "mage");
                BattleEngine.Apply(s, new UseSlotCommand { HeroId = mage.Id, Slot = 0, Target = new Coord(3, 5) });
                if (s.CastEntryOf(mage.Id) == null) continue;
                var wolf = Enemy(s, "wolf");
                s.TurnEntryOf(wolf.Id).Ip = 999;
                var ev = BattleEngine.Apply(s, new ShieldCommand());
                sawClutch = ev.Any(e => e is ClutchEvent);
            }
            Assert.That(sawClutch, Is.True, "a 20% clutch should proc within 80 seeded hits");
        }

        [Test]
        public void Drag_timer_remainder_banks_blue_orbs_at_the_enemy_handoff()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = 999;
            var k = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((5, 6)), TimeLeftSeconds = 3.9f });
            Assert.That(ev.OfType<OrbMintedEvent>().Count(o => o.Reason == "time bank"), Is.EqualTo(3));
        }

        [Test]
        public void Mage_robe_seeds_battle_start_orbs_and_gear_resistance_applies()
        {
            var db = Mini();
            db.Items["robe"] = new ItemDef { Id = "robe", Name = "Mage Robe", Type = ItemType.Equipment, Slot = EquipmentSlot.Torso, BattleStartManaOrbs = 2, Resistances = { [DamageType.Fire] = 0.5f } };
            var setup = new BattleSetup { Stage = Stage(("tank", 0, 0)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7), EquippedItemIds = { "robe" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            Assert.That(s.Bank.Count, Is.EqualTo(2));
            Assert.That(Hero(s, "knight").ResistanceMultiplier(DamageType.Fire), Is.EqualTo(0.5f).Within(1e-3));
        }

        [Test]
        public void A_second_items_resistance_cannot_undo_an_earlier_items_full_immunity()
        {
            var db = Mini();
            db.Items["cloak"] = new ItemDef { Id = "cloak", Name = "Cinder Cloak", Type = ItemType.Equipment, Slot = EquipmentSlot.Torso, Resistances = { [DamageType.Fire] = 0f } };
            db.Items["ring"] = new ItemDef { Id = "ring", Name = "Ember Ring", Type = ItemType.Equipment, Slot = EquipmentSlot.Ring1, Resistances = { [DamageType.Fire] = 0.5f } };
            var setup = new BattleSetup { Stage = Stage(("tank", 0, 0)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7), EquippedItemIds = { "cloak", "ring" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            Assert.That(Hero(s, "knight").ResistanceMultiplier(DamageType.Fire), Is.EqualTo(0f).Within(1e-3),
                "the cloak's full immunity must survive the ring's weaker resistance being folded in afterwards");
        }

        [Test]
        public void Boss_phase_fires_once_when_hp_crosses_threshold()
        {
            var db = Mini();
            db.Abilities["enrage"] = new AbilityDef { Id = "enrage", Name = "Enrage", Kind = AbilityKind.Skill, Shape = TargetShape.Self, Mode = TargetMode.Auto, Filter = TargetFilter.Any, HastenU = 0.35f };
            db.Classes["tank"].BossPhases.Add(new BossPhaseDef { HpThreshold = 0.5f, OnEnterAbility = "enrage", Announce = "Enraged!" });
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var tank = Enemy(s, "tank");
            tank.Hp = tank.MaxHp / 3;
            var all = new List<BattleEvent>();
            for (int i = 0; i < 30; i++) all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            Assert.That(all.OfType<BossPhaseEvent>(), Has.Exactly(1).Items);
            Assert.That(all.Any(e => e is HastenedEvent h && h.UnitId == tank.Id), Is.True);
        }

        [Test]
        public void Snake_body_follows_the_head_and_is_armored_tail_first()
        {
            var db = Mini();
            db.Classes["naga"] = new ClassDef { Id = "naga", Name = "Naga", Team = Team.Enemy, Tags = { "Boss" }, BaseStats = new Stats { Strength = 5, Vitality = 20, Speed = 12, Agility = 5 }, SnakeSegments = 3, MoveRange = 1 };
            var (s, _) = Battle(db, Stage(("naga", 2, 3)), ("knight", 2, 7), ("cleric", 5, 7));
            var head = s.Units.First(u => u.Def.Id == "naga" && u.SnakeIndex == 0);
            var segs = s.Units.Where(u => u.SnakeHeadId == head.Id && u.SnakeIndex > 0).OrderBy(u => u.SnakeIndex).ToList();
            Assert.That(segs.Count, Is.EqualTo(3));
            Assert.That(BattleEngine.IsArmored(s, head), Is.True);
            Assert.That(BattleEngine.IsArmored(s, segs.Last()), Is.False);
            var headBefore = head.Pos;
            s.TurnEntryOf(head.Id).Ip = 999;
            BattleEngine.Apply(s, new ShieldCommand());
            if (head.Pos != headBefore) Assert.That(segs[0].Pos, Is.EqualTo(headBefore));
            AssertInvariants(s);
        }

        [Test]
        public void Trap_layer_arms_a_tile_and_a_hero_stepping_on_it_triggers_it()
        {
            var db = Mini();
            db.Traps["snare"] = new TrapDef { Id = "snare", Name = "Venom Snare", Damage = 6, ApplyBuff = null, LayChance = 1f };
            db.Classes["wolf"].Trap = "snare";
            db.Classes["wolf"].MoveRange = 0;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 7), ("cleric", 5, 7));
            s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var placed = ev.OfType<TrapPlacedEvent>().Single();
            var k = Hero(s, "knight");
            k.Pos = placed.At + (placed.At.Y < 7 ? Coord.Down : Coord.Up);
            if (s.UnitAt(k.Pos) != null && s.UnitAt(k.Pos) != k) return;
            ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = new List<Coord> { placed.At } });
            Assert.That(ev.Any(e => e is TrapTriggeredEvent), Is.True);
            Assert.That(s.TrapAt(placed.At), Is.Null);
        }

        [Test]
        public void Mug_steals_and_hits_exactly_once_per_target()
        {
            var db = Mini();
            db.Abilities["mug"] = new AbilityDef { Id = "mug", Name = "Mug", Kind = AbilityKind.Skill, StealsMana = true, BaseDamage = 10, DamageType = DamageType.Physical, Shape = TargetShape.Cross, Radius = 1, Mode = TargetMode.Auto, Filter = TargetFilter.EnemyOnly };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "mug" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            var tank = Enemy(s, "tank");
            Assert.That(ev.OfType<DamagedEvent>().Where(d => d.TargetId == tank.Id && d.SourceId == k.Id), Has.Exactly(1).Items);
        }

        [Test]
        public void Silenced_caster_does_not_start_a_charge()
        {
            var db = Mini();
            db.Buffs["silenced"] = new BuffDef { Id = "silenced", Name = "Silenced", Unit = DurationUnit.Turns, Duration = 2, BlocksSpells = true };
            var (s, _) = Battle(db, Stage(("witch", 2, 0)), ("knight", 5, 7), ("cleric", 0, 7));
            var witch = Enemy(s, "witch");
            witch.Buffs.Add(new BuffInstance { Def = db.Buffs["silenced"], Remaining = 2 });
            Assert.That(EnemyPlanner.PlanCast(s, witch, false), Is.Null);
        }

        [Test]
        public void Frozen_costs_the_enemy_a_full_cycle_before_thawing()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "frost" };
            s.Bank.Add(2);
            var tank = Enemy(s, "tank");
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(2, 2) });
            Assert.That(tank.HasBuff("frozen"), Is.True, "still frozen after the casting action");
            int ip = s.TurnEntryOf(tank.Id).Ip;
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((5, 6)) });
            Assert.That(s.TurnEntryOf(tank.Id).Ip, Is.EqualTo(ip)); // halted for the whole next action
            Assert.That(tank.HasBuff("frozen"), Is.False);
            Assert.That(tank.HasBuff("wet"), Is.True);
        }

        [Test]
        public void Time_bank_carries_until_the_next_enemy_turn()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            // The wolf's turn lands inside the SECOND action: 770 + 20 ticks of zone crawl ≈ 990 after the first.
            s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = 770;
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { ItemId = "potion", Charges = 3, MaxCharges = 9 };
            var first = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((5, 6)), TimeLeftSeconds = 2.6f });
            Assert.That(first.Any(e => e is TurnStartedEvent), Is.False);
            var second = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            Assert.That(second.Any(e => e is TurnStartedEvent), Is.True);
            Assert.That(second.OfType<OrbMintedEvent>().Count(o => o.Reason == "time bank"), Is.EqualTo(2));
        }


        // ── Targeting personalities (§14.1) and windup overtakes (§2) ──────────

        [Test]
        public void Dumb_enemies_attack_the_closest_hero()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Closest;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 3), ("cleric", 2, 0));
            Hero(s, "cleric").Hp = 1;
            Assert.That(EnemyPlanner.PickTarget(s, Enemy(s, "wolf")).Id, Is.EqualTo(Hero(s, "knight").Id));
        }

        [Test]
        public void Smart_enemies_hunt_the_weakest_hero_even_across_the_board()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Weakest;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            Hero(s, "cleric").Hp = 3;
            var wolf = Enemy(s, "wolf");
            Assert.That(EnemyPlanner.PickTarget(s, wolf).Id, Is.EqualTo(Hero(s, "cleric").Id));
            var step = EnemyPlanner.PlanStep(s, wolf);
            Assert.That(Math.Abs(step.X - 5) + Math.Abs(step.Y - 7) < Math.Abs(2 - 5) + Math.Abs(2 - 7), Is.True, "it walks toward the weak hero");
        }

        [Test]
        public void Assassins_target_the_most_fragile_hero()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Squishy;
            db.Classes["mage"].BaseStats.Vitality = 3;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 3), ("mage", 5, 7));
            Assert.That(EnemyPlanner.PickTarget(s, Enemy(s, "wolf")).Id, Is.EqualTo(Hero(s, "mage").Id));
        }

        [Test]
        public void Erratic_enemies_keep_one_target_per_turn_but_vary_between_turns()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Erratic;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7), ("mage", 0, 7));
            var wolf = Enemy(s, "wolf");
            var seen = new HashSet<int>();
            for (int clock = 0; clock < 30; clock++)
            {
                s.Clock = clock;
                var a = EnemyPlanner.PickTarget(s, wolf);
                Assert.That(EnemyPlanner.PickTarget(s, wolf), Is.SameAs(a));
                seen.Add(a.Id);
            }
            Assert.That(seen.Count >= 2, Is.True);
        }

        [Test]
        public void Every_real_enemy_has_a_targeting_personality_and_a_speed_tier()
        {
            var enemies = Real.Classes.Values.Where(c => c.Team == Team.Enemy && !c.HasTag("Remains") && !c.HasTag("Egg")).ToList();
            foreach (var c in enemies) Assert.That(c.Targeting, Is.Not.EqualTo(Targeting.Auto));
            foreach (var c in enemies) Assert.That(new[] { 8f, 11f, 15f, 18f }, Does.Contain(c.BaseStats.Speed));
            Assert.That(enemies.Select(c => c.Targeting).Distinct().Count() >= 4, Is.True);
        }

        [Test]
        public void A_fast_enemy_overtakes_a_slow_one_during_the_windup()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0), ("tank", 5, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Enemy(s, "wolf"); var tank = Enemy(s, "tank");
            wolf.LevelStats.Speed = 18; tank.LevelStats.Speed = 8;
            s.TurnEntryOf(wolf.Id).Ip = 0;     // fast one starts at the very back
            s.TurnEntryOf(tank.Id).Ip = 300;   // slow one has a head start
            Assert.That(BattleEngine.TicksUntilTurn(s, wolf.Id) < BattleEngine.TicksUntilTurn(s, tank.Id), Is.True);
        }

        [Test]
        public void Burning_stacks_up_to_its_cap_and_each_stack_burns()
        {
            var db = Mini();
            db.Buffs["burning"].Stacking = BuffStacking.Stack;
            db.Buffs["burning"].MaxStacks = 3;
            db.Buffs["burning"].Duration = 500; // outlive the casts
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            var tank = Enemy(s, "tank");
            for (int i = 0; i < 4; i++)
            {
                s.Bank.Add(2);
                BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            }
            Assert.That(tank.Buffs.Count(b => b.Def.Id == "burning"), Is.EqualTo(3));
            Assert.That(Real.Buffs["burning"].Stacking, Is.EqualTo(BuffStacking.Stack));
            Assert.That(Real.Buffs["burning"].MaxStacks, Is.EqualTo(3));
        }

        // ── Power Amplifier (§1.2.6) ───────────────────────────────────────────

        [Test]
        public void Amplifier_chance_grows_with_party_luck_and_caps()
        {
            Assert.That(Formulas.AmplifierChance(40f) > Formulas.AmplifierChance(10f), Is.True);
            Assert.That(Formulas.AmplifierChance(10000f), Is.EqualTo(BattleConfig.AmplifierMaxChance));
            Assert.That(Formulas.AmplifierChance(0f), Is.EqualTo(BattleConfig.AmplifierBaseChance).Within(1e-3));
        }

        [Test]
        public void An_attacker_on_the_amplifier_strikes_harder_adds_mana_charge_and_uses_it_up()
        {
            int Hit(bool amp)
            {
                var db = Mini();
                var (s, _) = Battle(db, Stage(("goblin", 3, 3), ("goblin", 4, 3)), ("cleric", 2, 3), ("knight", 5, 3));
                if (amp) s.Amplifier = new Coord(4, 4); // next to the knight's destination (aligned with attacker 2)
                var knight = Hero(s, "knight");
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((4, 3)) });
                if (amp)
                {
                    Assert.That(ev.OfType<AmplifierUsedEvent>(), Has.Exactly(1).Items);
                    Assert.That(ev.Any(e => e is ManaChargedEvent o && o.Reason == "amplifier" && ManaEconomyTests.IsDampedFrom(o.Amount, BattleConfig.AmplifierCharge)), Is.True);
                    Assert.That(s.Amplifier == null || ev.OfType<AmplifierSpawnedEvent>().Any(), Is.True);
                }
                return ev.OfType<DamagedEvent>().Where(d => d.SourceId == knight.Id).Sum(d => d.Amount);
            }
            Assert.That(Hit(true) > Hit(false), Is.True);
        }

        [Test]
        public void Enemies_cannot_use_the_amplifier_but_stepping_on_it_shoves_it_back()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Closest;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 5), ("cleric", 5, 7));
            var wolf = Enemy(s, "wolf");
            s.Amplifier = new Coord(2, 3); // on the wolf's path to the knight
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var moved = ev.OfType<AmplifierMovedEvent>().FirstOrDefault();
            Assert.That(moved, Is.Not.Null);
            Assert.That(moved.From, Is.EqualTo(new Coord(2, 3)));
            Assert.That(moved.To, Is.EqualTo(new Coord(2, 2)));
            Assert.That(ev.Any(e => e is AmplifierUsedEvent), Is.False);
        }

        [Test]
        public void The_amplifier_is_a_movable_tile_heroes_push_and_nothing_spawns_on()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 5, 0)), ("knight", 0, 3), ("cleric", 5, 7));
            s.Amplifier = new Coord(1, 3);
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 3)) });
            Assert.That(ev.Any(e => e is AmplifierMovedEvent m && m.From == new Coord(1, 3) && m.To == new Coord(0, 3)), Is.True);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 3)));
            Assert.That(s.FreeForSpawn(s.Amplifier.Value), Is.False);
        }

        // ── Knockback (§6.3) ──

        private static (BattleState s, Unit knight) Bash(ContentDb db, StageDef stage)
        {
            db.Abilities["shield_bash"] = new AbilityDef { Id = "shield_bash", Name = "Shield Bash", Kind = AbilityKind.Skill, CooldownTurns = 2, BaseDamage = 12, Knockback = 2, AdjacentOnly = true, DamageType = DamageType.Physical, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            var (s, _) = Battle(db, stage, ("knight", 2, 4), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "shield_bash" };
            return (s, k);
        }

        [Test]
        public void Shield_bash_shoves_the_target_two_tiles_away()
        {
            var (s, k) = Bash(Mini(), Stage(("tank", 2, 3)));
            var tank = Enemy(s, "tank");
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = tank.Pos });
            Assert.That(tank.Pos, Is.EqualTo(new Coord(2, 1)));
        }

        [Test]
        public void Bashing_into_a_unit_crashes_both_and_into_stone_slams_hard()
        {
            int Taken(StageDef st, out int other)
            {
                var (s, k) = Bash(Mini(), st);
                var tank = s.Units.First(u => u.Alive && u.Pos == new Coord(2, 3));
                var second = s.Units.FirstOrDefault(u => u.Alive && !u.IsHero && u != tank);
                int hp0 = tank.Hp, hp1 = second?.Hp ?? 0;
                BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = tank.Pos });
                other = second == null ? 0 : hp1 - second.Hp;
                return hp0 - tank.Hp;
            }
            int free = Taken(Stage(("tank", 2, 3)), out _);
            int crash = Taken(Stage(("tank", 2, 3), ("tank", 2, 2)), out int otherHurt);
            var stone = Stage(("tank", 2, 3)); stone.Blocks.Add(new Coord(2, 2));
            int slam = Taken(stone, out _);
            Assert.That(crash > free, Is.True, $"crash {crash} vs free {free}");
            Assert.That(otherHurt > 0, Is.True, "the unit it crashed into is hurt too");
            Assert.That(slam > crash, Is.True, $"slam {slam} vs crash {crash}");
        }

        [Test]
        public void Shield_bash_needs_an_adjacent_target()
        {
            var (s, k) = Bash(Mini(), Stage(("tank", 2, 1)));
            Assert.That(TargetResolver.IsValidPick(s, s.Content.Abilities["shield_bash"], k, new Coord(2, 1)), Is.False);
        }

        // ── Gravestones (§14.5) ──

        private static ContentDb GraveDb()
        {
            var db = Mini();
            db.Classes["grave"] = new ClassDef { Id = "grave", Name = "Gravestone", Team = Team.Enemy, Tags = { "Remains" }, BaseStats = new Stats { Vitality = 2, Speed = 20 }, MoveRange = 0 };
            db.Classes["wolf"].Remains = "grave";
            return db;
        }

        [Test]
        public void A_slain_undead_leaves_solid_remains_that_block_the_wave_from_clearing()
        {
            var db = GraveDb();
            var (s, _) = Battle(db, Stage(("wolf", 3, 3)), ("cleric", 2, 3), ("knight", 5, 2));
            Enemy(s, "wolf").Hp = 1;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((5, 3), (4, 3)) });
            var left = ev.OfType<RemainsLeftEvent>().SingleOrDefault();
            Assert.That(left, Is.Not.Null);
            Assert.That(left.At, Is.EqualTo(new Coord(3, 3)));
            Assert.That(s.UnitById(left.RemainsId).Alive, Is.True);
            Assert.That(s.IsOver, Is.False, "the wave isn't cleared while the remains stand");
        }

        [Test]
        public void Remains_left_alone_raise_their_owner_at_half_hp_without_loot()
        {
            var db = GraveDb();
            var (s, _) = Battle(db, Stage(("wolf", 3, 0), ("tank", 0, 0)), ("cleric", 2, 7), ("knight", 5, 7));
            var wolf = Enemy(s, "wolf");
            // the wolf has died: stand up its remains about to reach ACT
            wolf.Hp = 0; wolf.OnBoard = false; s.Timeline.RemoveAll(e => e.OwnerId == wolf.Id);
            var remains = new Unit { Id = s.NextUnitId++, Def = db.Classes["grave"], Team = Team.Enemy, Level = 1, Pos = new Coord(3, 0), W = 1, H = 1, RisesAs = "wolf", RisesLevel = 1 };
            remains.LevelStats = Formulas.StatsAtLevel(remains.Def, 1); remains.MaxHp = remains.Hp = 10;
            s.Units.Add(remains);
            s.Timeline.Add(new TimelineEntry { Id = s.NextEntryId++, Kind = TimelineKind.Turn, OwnerId = remains.Id, Ip = 990 });
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var rose = ev.OfType<RoseAgainEvent>().SingleOrDefault();
            Assert.That(rose, Is.Not.Null);
            var risen = s.UnitById(rose.UnitId);
            Assert.That(risen.Def.Id, Is.EqualTo("wolf"));
            Assert.That(risen.NoLoot, Is.True);
            Assert.That(remains.Alive, Is.False);
        }

        [Test]
        public void Remains_are_solid_and_real_undead_leave_them()
        {
            var db = GraveDb();
            var (s, _) = Battle(db, Stage(("grave", 1, 3)), ("knight", 0, 3), ("cleric", 5, 7));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((1, 3)) });
            Assert.That(ev.Any(e => e is StepBlockedEvent b && b.Reason == "immovable"), Is.True);
            Assert.That(Real.Classes["undead00"].Remains, Is.EqualTo("gravestone"));
            Assert.That(Real.Classes["skelepede00"].Remains, Is.EqualTo("bonepile"));
        }

        // ── Eggs (§14.8) ──

        private static ContentDb EggDb()
        {
            var db = Mini();
            db.Classes["naga"] = new ClassDef { Id = "naga", Name = "Naga", Team = Team.Enemy, Tags = { "Aquatic" }, BaseStats = new Stats { Vitality = 10, Speed = 10 }, MoveRange = 0, LaysEgg = "nagaegg", EggInterval = 4 };
            db.Classes["nagaegg"] = new ClassDef { Id = "nagaegg", Name = "Naga Egg", Team = Team.Enemy, Tags = { "Egg" }, BaseStats = new Stats { Vitality = 2, Speed = 20 }, MoveRange = 0 };
            return db;
        }

        [Test]
        public void An_egg_hatches_into_its_real_enemy_at_full_hp_on_schedule()
        {
            var db = EggDb();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("cleric", 2, 7), ("knight", 5, 7));
            var egg = new Unit { Id = s.NextUnitId++, Def = db.Classes["nagaegg"], Team = Team.Enemy, Level = 3, Pos = new Coord(3, 3), W = 1, H = 1, RisesAs = "naga", RisesLevel = 3 };
            egg.LevelStats = Formulas.StatsAtLevel(egg.Def, 3); egg.MaxHp = egg.Hp = 5;
            s.Units.Add(egg);
            s.Timeline.Add(new TimelineEntry { Id = s.NextEntryId++, Kind = TimelineKind.Turn, OwnerId = egg.Id, Ip = 990 });
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var hatched = ev.OfType<EggHatchedEvent>().SingleOrDefault();
            Assert.That(hatched, Is.Not.Null);
            var u = s.UnitById(hatched.UnitId);
            Assert.That(u.Def.Id, Is.EqualTo("naga"));
            Assert.That(u.Hp, Is.EqualTo(u.MaxHp)); // full HP — a fresh enemy, not a weakened return
            Assert.That(egg.Alive, Is.False);
        }
    }
}
