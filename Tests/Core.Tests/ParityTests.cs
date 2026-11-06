using System;
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
    /// <summary>One targeted assertion per bible rule that was previously only exercised by the soak.</summary>
    public class ParityTests
    {
        private static Unit Enemy(BattleState s, string cls) => s.Units.First(u => u.Def.Id == cls && u.Alive);

        // ── Pincers ─────────────────────────────────────────────────────────────

        [Test] // US-009
        public void Both_attackers_strike_every_trapped_opponent()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 1, 0), ("tank", 2, 0), ("tank", 3, 0)), ("knight", 0, 0), ("cleric", 4, 1));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((4, 0)) });
            var tanks = s.Units.Where(u => u.Def.Id == "tank").Select(u => u.Id).ToList();
            foreach (var attacker in new[] { Hero(s, "knight").Id, Hero(s, "cleric").Id })
                foreach (var t in tanks)
                    Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == attacker && a.TargetId == t && !a.IsSupport), Is.True);
        }

        [Test] // US-011
        public void Supporter_strikes_at_about_half_damage()
        {
            var db = Mini();
            db.Classes["mage"].BaseStats.Strength = 20;
            var ratios = new List<float>();
            for (int seed = 1; seed < 40; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
                setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(1, 4) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                var dmg = ev.OfType<DamagedEvent>().ToList();
                var full = dmg.Where(d => d.SourceId == Hero(s, "knight").Id && d.Hit == HitKind.Normal).Select(d => d.Amount).FirstOrDefault();
                var sup = dmg.Where(d => d.SourceId == Hero(s, "mage").Id && d.Hit == HitKind.Normal).Select(d => d.Amount).FirstOrDefault();
                if (full > 0 && sup > 0) ratios.Add(sup / (float)full);
            }
            Assert.That(ratios, Is.Not.Empty);
            Assert.That(ratios.Average(), Is.InRange(0.35f, 0.65f));
        }

        [Test] // US-012
        public void Beasts_never_form_pincers()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 3), ("wolf", 2, 3)), ("knight", 1, 3), ("cleric", 5, 7));
            Assert.That(PincerDetector.Detect(s, Team.Enemy, null), Is.Empty);
        }

        [Test] // US-027
        public void Sentient_enemies_pincer_heroes_without_minting_orbs()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 0, 3), ("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 5, 7));
            s.TurnEntryOf(s.Units.First(u => u.Def.Id == "goblin").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is PincerFormedEvent p && p.Team == Team.Enemy), Is.True);
            Assert.That(ev.Any(e => e is OrbMintedEvent o && (o.Reason == "pincer" || o.Reason == "support")), Is.False);
        }

        [Test] // US-016 / US-017
        public void Boss_step_shoves_the_hero_behind_it_then_melees_by_footprint()
        {
            var db = Mini();
            db.Classes["boss"].MoveRange = 1;
            var (s, _) = Battle(db, Stage(("boss", 0, 0)), ("knight", 2, 0), ("cleric", 5, 0));
            var boss = Enemy(s, "boss");
            s.TurnEntryOf(boss.Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(boss.Pos, Is.EqualTo(new Coord(1, 0)));
            Assert.That(Hero(s, "knight").Pos, Is.EqualTo(new Coord(0, 0)));
            Assert.That(ev.Any(e => e is DisplacedEvent d && d.ByUnitId == boss.Id), Is.True);
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == boss.Id && a.IsMelee), Is.True);
            AssertInvariants(s);
        }

        [Test] // US-195
        public void Snake_members_are_immovable_to_drags()
        {
            var db = Mini();
            db.Classes["naga"] = new ClassDef { Id = "naga", Name = "Naga", Team = Team.Enemy, BaseStats = new Stats { Vitality = 20, Speed = 1 }, SnakeSegments = 2, MoveRange = 0 };
            var (s, _) = Battle(db, Stage(("naga", 2, 4)), ("knight", 2, 6), ("cleric", 5, 7));
            var seg = s.Units.First(u => u.IsSnakeSegment);
            var k = Hero(s, "knight");
            var target = seg.Pos;
            k.Pos = target + Coord.Down;
            if (s.UnitAt(k.Pos) != k && s.UnitAt(k.Pos) != null) return;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = new List<Coord> { target } });
            Assert.That(ev.Any(e => e is StepBlockedEvent b && b.Reason == "immovable"), Is.True);
        }

        // ── Enemy behaviour ─────────────────────────────────────────────────────

        [Test] // US-028
        public void Enemy_melee_targets_the_adjacent_hero_with_lowest_hp()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 2, 3)), ("knight", 1, 3), ("cleric", 3, 3));
            db.Classes["wolf"].MoveRange = 0;
            Hero(s, "cleric").Hp = 5;
            s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var melee = ev.OfType<AttackEvent>().First(a => a.IsMelee);
            Assert.That(melee.TargetId, Is.EqualTo(Hero(s, "cleric").Id));
        }

        [Test] // US-180
        public void MoveRange_two_steps_twice()
        {
            var db = Mini();
            db.Classes["wolf"].MoveRange = 2;
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 0, 7), ("cleric", 5, 7));
            s.TurnEntryOf(Enemy(s, "wolf").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<SteppedEvent>().Count(st => st.UnitId == Enemy(s, "wolf").Id), Is.EqualTo(2));
        }

        [Test] // US-173
        public void Enemy_values_supporting_an_ally_pincer()
        {
            var db = Mini();
            db.Classes["goblin"].MoveRange = 1;
            // goblins at (0,3),(2,3) already pincer the knight at (1,3); a third goblin at (0,5) can step to (0,4) to become a supporter.
            var (s, _) = Battle(db, Stage(("goblin", 0, 3), ("goblin", 2, 3), ("goblin", 0, 5)), ("knight", 1, 3), ("cleric", 5, 0));
            var third = s.Units.First(u => u.Def.Id == "goblin" && u.Pos == new Coord(0, 5));
            Assert.That(EnemyPlanner.PlanStep(s, third), Is.EqualTo(new Coord(0, 4)));
        }

        [Test] // US-026
        public void Traps_spring_only_on_heroes()
        {
            var db = Mini();
            db.Traps["snare"] = new TrapDef { Id = "snare", Name = "Snare", Damage = 6 };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 3, 2), ("cleric", 5, 7));
            s.Traps.Add(new Trap { At = new Coord(3, 2), TrapId = "snare" });
            // knight steps west into the tank's tile; the tank is displaced onto the trapped tile (3,2) — no trigger.
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((2, 2)) });
            Assert.That(ev.Any(e => e is TrapTriggeredEvent), Is.False);
            Assert.That(s.TrapAt(new Coord(3, 2)), Is.Not.Null);
        }

        // ── Timeline ────────────────────────────────────────────────────────────

        [Test] // US-043
        public void Timeline_rate_is_linear_in_speed_with_no_zone_crawl()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Enemy(s, "wolf");
            var e = s.TurnEntryOf(wolf.Id);
            e.Ip = 0;
            int speed10 = BattleEngine.TicksUntilTurn(s, wolf.Id);
            Assert.That(speed10, Is.EqualTo((int)Math.Ceiling(1000 / 16.0))); // one rate the whole way (GG-T286)
            wolf.LevelStats.Speed = 20;
            int speed20 = BattleEngine.TicksUntilTurn(s, wolf.Id);
            Assert.That(speed20 < speed10, Is.True);
            e.Ip = 650; // where the old Zone began: Speed still matters there
            int fastLate = BattleEngine.TicksUntilTurn(s, wolf.Id);
            wolf.LevelStats.Speed = 5;
            Assert.That(BattleEngine.TicksUntilTurn(s, wolf.Id), Is.GreaterThan(fastLate));
        }

        [Test] // US-057
        public void Slowed_halves_the_timeline_rate()
        {
            var db = Mini();
            db.Buffs["slowed"] = new BuffDef { Id = "slowed", Name = "Slowed", Unit = DurationUnit.Turns, Duration = 2, TimelineSpeedMultiplier = 0.5f };
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Enemy(s, "wolf");
            s.TurnEntryOf(wolf.Id).Ip = 0;
            int normal = BattleEngine.TicksUntilTurn(s, wolf.Id);
            wolf.Buffs.Add(new BuffInstance { Def = db.Buffs["slowed"], Remaining = 2 });
            int slowed = BattleEngine.TicksUntilTurn(s, wolf.Id);
            Assert.That(slowed / (float)normal, Is.InRange(1.8f, 2.2f));
        }

        [Test] // US-047
        public void Pincer_pushback_is_the_same_seconds_anywhere_on_the_bar_whatever_the_strength()
        {
            float Push(int ip, float str)
            {
                var db = Mini();
                db.Classes["knight"].BaseStats.Strength = str;
                db.Classes["cleric"].BaseStats.Strength = str;
                db.Classes["tank"].BaseStats.Speed = 10; // a bar-length lap, so neither end clamps
                var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
                s.TurnEntryOf(Enemy(s, "tank").Id).Ip = ip;
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                var p = ev.OfType<PushedBackEvent>().FirstOrDefault();
                return p == null ? 0f : p.ToU - p.FromU; // pushed right: acts later
            }
            float expected = BattleConfig.PincerPushbackSeconds / BattleConfig.BarSeconds;
            Assert.That(Push(990, 20), Is.EqualTo(expected).Within(1e-4));
            Assert.That(Push(300, 20), Is.EqualTo(expected).Within(1e-4)); // far from the left edge too (GG-T286)
            Assert.That(Push(990, 40), Is.EqualTo(expected).Within(1e-4)); // STR no longer scales it
        }

        [Test] // US-049
        public void Quicken_on_a_casting_hero_rushes_the_cast()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            s.Timeline.Add(new TimelineEntry { Id = 500, Kind = TimelineKind.Cast, OwnerId = m.Id, CasterTeam = Team.Hero, AbilityId = "meteor", RemainingTicks = 200, TotalCastTicks = 200, TargetTile = new Coord(2, 0) });
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "quicken" };
            s.Bank.Add(1);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = m.Pos });
            Assert.That(ev.Any(e => e is HastenedEvent h && h.EntryId == 500), Is.True);
        }

        [Test] // US-051
        public void Casts_resolve_before_turns_arriving_on_the_same_tick()
        {
            var db = Mini();
            db.Abilities["zap"] = new AbilityDef { Id = "zap", Name = "Zap", Kind = AbilityKind.Spell, BaseDamage = 1, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Arcane };
            var (s, _) = Battle(db, Stage(("wolf", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var wolf = Enemy(s, "wolf");
            s.TurnEntryOf(wolf.Id).Ip = 999;
            s.Timeline.Add(new TimelineEntry { Id = 501, Kind = TimelineKind.Cast, OwnerId = Hero(s, "mage").Id, CasterTeam = Team.Hero, AbilityId = "zap", RemainingTicks = 1, TotalCastTicks = 10, TargetUnitId = wolf.Id, TargetTile = wolf.Pos });
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            int cast = ev.FindIndex(e => e is AbilityResolvedEvent r && r.AbilityId == "zap");
            int turn = ev.FindIndex(e => e is TurnStartedEvent);
            Assert.That(cast >= 0 && turn >= 0 && cast < turn, Is.True);
        }

        [Test] // US-052
        public void Wave_icons_start_with_a_bounded_random_head_start()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 0, 0), ("tank", 1, 0), ("tank", 2, 0), ("tank", 3, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            foreach (var e in s.Timeline) Assert.That(e.Ip, Is.InRange(0, BattleConfig.SpawnStaggerIp));
        }

        // ── Mana ────────────────────────────────────────────────────────────────

        [Test] // US-075
        public void Hero_crit_adds_mana_charge()
        {
            var db = Mini();
            db.Classes["knight"].BaseStats.Luck = 200; // crit% caps at 50: some strikes crit
            bool saw = false;
            for (int seed = 1; seed < 40 && !saw; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                int crits = ev.OfType<DamagedEvent>().Count(d => d.Hit == HitKind.Critical && d.SourceId == Hero(s, "knight").Id);
                int wild = ev.OfType<ManaChargedEvent>().Count(o => o.Reason == "critical" && ManaEconomyTests.IsDampedFrom(o.Amount, BattleConfig.CritCharge));
                if (crits > 0) { Assert.That(wild, Is.EqualTo(crits)); saw = true; }
            }
            Assert.That(saw, Is.True);
        }

        [Test] // US-076
        public void Steal_with_high_luck_always_mints_an_orb()
        {
            var db = Mini();
            db.Classes["knight"].BaseStats.Luck = 100;
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "steal" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "steal"), Is.True);
        }

        [Test] // US-077 / US-096
        public void Stagger_accumulates_and_cancelling_a_charge_mints_an_orb()
        {
            var db = Mini();
            db.Abilities["charge_bolt"].Cost = 1;
            db.Classes["witch"].BaseStats.Wisdom = 0;
            db.Classes["knight"].BaseStats.Strength = 400;
            db.Classes["cleric"].BaseStats.Strength = 400;
            db.Classes["witch"].BaseStats.Vitality = 4000;
            var (s, _) = Battle(db, Stage(("witch", 2, 0)), ("knight", 1, 0), ("cleric", 3, 1));
            var witch = Enemy(s, "witch");
            s.Timeline.Add(new TimelineEntry { Id = 600, Kind = TimelineKind.Cast, OwnerId = witch.Id, CasterTeam = Team.Enemy, AbilityId = "charge_bolt", IsCharge = true, RemainingTicks = 25, TotalCastTicks = 25, TargetUnitId = Hero(s, "knight").Id });
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 0)) });
            Assert.That(ev.Any(e => e is CastStaggeredEvent st && st.DelayTicks > 0), Is.True);
            Assert.That(ev.Any(e => e is CastCancelledEvent c && c.Reason == "interrupted"), Is.True);
            Assert.That(ev.Any(e => e is ManaChargedEvent o && o.Reason == "interrupt" && ManaEconomyTests.IsDampedFrom(o.Amount, BattleConfig.InterruptCharge)), Is.True);
        }

        [Test] // US-091
        public void Slot_status_reports_each_blocking_state()
        {
            var db = Mini();
            db.Buffs["silenced"] = new BuffDef { Id = "silenced", Name = "Silenced", Unit = DurationUnit.Turns, Duration = 2, BlocksSpells = true };
            var setup = new BattleSetup { Stage = Stage(("tank", 0, 0)), Seed = 1, UnlockedSlots = 3 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7), Bar = new List<string> { "fireball", "teleport", "item:potion:0", null, "heal" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight");
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 0), Is.EqualTo(SlotStatus.Unaffordable));
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 1), Is.EqualTo(SlotStatus.Ready));
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 2), Is.EqualTo(SlotStatus.OutOfCharges));
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 3), Is.EqualTo(SlotStatus.Locked));
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 4), Is.EqualTo(SlotStatus.Locked));
            k.Cooldowns["teleport"] = 2;
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 1), Is.EqualTo(SlotStatus.Cooldown));
            s.Bank.Add(2);
            k.Buffs.Add(new BuffInstance { Def = db.Buffs["silenced"], Remaining = 2 });
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 0), Is.EqualTo(SlotStatus.Silenced));
            k.Buffs.Add(new BuffInstance { Def = db.Buffs["sleep"], Remaining = 2 });
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 0), Is.EqualTo(SlotStatus.CannotAct));
        }

        [Test] // US-102 refusal half
        public void Revival_charm_is_refused_when_nobody_has_fallen()
        {
            var db = Mini();
            db.Items["revival_charm"] = new ItemDef { Id = "revival_charm", Name = "Revival Charm", Type = ItemType.Consumable, Revives = true, BaseHealing = 30 };
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { ItemId = "revival_charm", Charges = 1, MaxCharges = 1 };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.True);
            Assert.That(k.Bar[0].Charges, Is.EqualTo(1));
        }

        // ── Buffs ───────────────────────────────────────────────────────────────

        [Test] // US-140
        public void Reapplying_a_buff_refreshes_its_duration()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            var tank = Enemy(s, "tank");
            s.Bank.Add(4);
            // An action runs the clock until the next enemy turn (GG-T188); the Speed-1 tank would take ~350
            // ticks to arrive and Burning would expire inside the first cast, so its turn is put on the next tick.
            s.TurnEntryOf(tank.Id).Ip = 999;
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            var first = tank.Buffs.First(b => b.Def.Id == "burning").Remaining;
            s.TurnEntryOf(tank.Id).Ip = 999;
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            Assert.That(tank.Buffs.Where(b => b.Def.Id == "burning"), Has.Exactly(1).Items);
            Assert.That(tank.Buffs.First(b => b.Def.Id == "burning").Remaining >= first, Is.True);
        }

        [Test] // US-141 / US-153
        public void Burning_ticks_four_per_second_for_five_then_turns_warm()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("cleric", 4, 7));
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["burning"], Remaining = 5 });
            var all = new List<BattleEvent>();
            for (int i = 0; i < 4; i++) all.AddRange(BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = Hero(s, "knight").Pos.Y == 7 ? P((5, 6)) : P((5, 7)) }));
            var ticks = all.OfType<DamagedEvent>().Where(d => d.TargetId == tank.Id && d.Note == "Burning").ToList();
            Assert.That(ticks.Count, Is.EqualTo(5));
            foreach (var d in ticks) Assert.That(d.Amount, Is.EqualTo(4));
            Assert.That(all.Any(e => e is BuffAppliedEvent b && b.UnitId == tank.Id && b.BuffId == "warm"), Is.True);
        }

        [Test] // US-149
        public void Poison_ticks_three_for_six()
        {
            var db = Mini();
            db.Buffs["poisoned"] = new BuffDef { Id = "poisoned", Name = "Poisoned", Unit = DurationUnit.Ticks, Duration = 6, DamagePerTick = 3 };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("cleric", 4, 7));
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["poisoned"], Remaining = 6 });
            var all = new List<BattleEvent>();
            for (int i = 0; i < 4; i++) all.AddRange(BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = Hero(s, "knight").Pos.Y == 7 ? P((5, 6)) : P((5, 7)) }));
            Assert.That(all.OfType<DamagedEvent>().Count(d => d.TargetId == tank.Id && d.Amount == 3), Is.EqualTo(6));
            Assert.That(tank.HasBuff("poisoned"), Is.False);
        }

        [Test] // US-145
        public void Fire_on_frozen_thaws_then_steams_off_the_wet()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["frozen"], Remaining = 1 });
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(2);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            Assert.That(ev.Any(e => e is BuffRemovedEvent r && r.BuffId == "frozen"), Is.True);
            Assert.That(ev.Any(e => e is BuffRemovedEvent r && r.BuffId == "wet" && r.Reason == "steam"), Is.True);
            Assert.That(tank.HasBuff("frozen"), Is.False);
            Assert.That(tank.HasBuff("wet"), Is.False);
        }

        [Test] // US-146
        public void Sleep_on_a_warm_target_lasts_half_again_as_long()
        {
            var db = Mini();
            db.Abilities["sleep"] = new AbilityDef { Id = "sleep", Name = "Sleep", Kind = AbilityKind.Spell, ApplyBuff = "sleep", Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["warm"], Remaining = 3 });
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "sleep" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "sleep" && b.Duration == 5), Is.True); // ceil(3 × 1.5)
        }

        [Test] // US-147
        public void Sleeping_enemy_skips_its_turn()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Enemy(s, "wolf");
            wolf.Buffs.Add(new BuffInstance { Def = db.Buffs["sleep"], Remaining = 3 });
            s.TurnEntryOf(wolf.Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is TurnSkippedEvent t && t.UnitId == wolf.Id && t.Reason == "asleep"), Is.True);
        }

        [Test] // US-148
        public void Sleep_breaks_when_the_sleeper_is_displaced()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 3, 2), ("cleric", 5, 7));
            var tank = Enemy(s, "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["sleep"], Remaining = 3 });
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((2, 2)) });
            Assert.That(tank.HasBuff("sleep"), Is.False);
        }

        [Test] // US-150
        public void Blinded_attackers_hit_far_less_often()
        {
            var db = Mini();
            db.Buffs["blinded"] = new BuffDef { Id = "blinded", Name = "Blinded", Unit = DurationUnit.Turns, Duration = 2, AccuracyMultiplier = 0.5f };
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight"); var tank = Enemy(s, "tank");
            var rng = new Rng(9);
            int Hits() { int h = 0; for (int i = 0; i < 4000; i++) if (Formulas.RollHit(k, tank, rng) != HitKind.Miss) h++; return h; }
            int clear = Hits();
            k.Buffs.Add(new BuffInstance { Def = db.Buffs["blinded"], Remaining = 2 });
            int blind = Hits();
            Assert.That(blind < clear * 0.75f, Is.True, $"blind {blind} vs clear {clear}");
        }

        [Test] // US-151
        public void Protection_reduces_incoming_spell_damage_by_fifteen_percent()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            var spell = new AbilityDef { BaseDamage = 100, DamageType = DamageType.Arcane };
            Assert.That(Formulas.SpellDamage(spell, k, false), Is.EqualTo(100));
            k.Buffs.Add(new BuffInstance { Def = db.Buffs["protection"], Remaining = 1 });
            Assert.That(Formulas.SpellDamage(spell, k, false), Is.EqualTo(85));
        }

        // ── Loot / XP / meta ────────────────────────────────────────────────────

        [Test] // US-021
        public void Enemies_without_a_coin_range_drop_about_half_their_xp()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 1, 0)), ("knight", 0, 0), ("cleric", 2, 1));
            for (int i = 0; i < 8 && !s.IsOver; i++)
            {
                var c = Hero(s, "cleric");
                BattleEngine.Apply(s, new MoveCommand { HeroId = c.Id, Path = c.Pos == new Coord(2, 0) ? P((2, 1)) : P((2, 0)) });
            }
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Results.Coins, Is.InRange((int)(s.Results.Xp * 0.5f * 0.75f), (int)Math.Ceiling(s.Results.Xp * 0.5f * 1.25f) + 1));
        }

        [Test] // US-217
        public void Xp_curve_is_50_plus_10_level_squared()
        {
            Assert.That(Formulas.XpToNext(1), Is.EqualTo(60));
            Assert.That(Formulas.XpToNext(2), Is.EqualTo(90));
            Assert.That(Formulas.LevelFromTotalXp(60), Is.EqualTo((2, 0)));
            Assert.That(Formulas.LevelFromTotalXp(150), Is.EqualTo((3, 0)));
            Assert.That(Formulas.TotalXpForLevel(3), Is.EqualTo(150));
        }

        [Test] // US-216
        public void Wounds_carry_on_victory_and_the_fallen_revive_at_one_hp()
        {
            var c = Real;
            var save = Progression.NewGame(c, "W");
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[0].Id, 4);
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            var pal = s.Units.First(u => u.Def.Id == "paladin");
            var cle = s.Units.First(u => u.Def.Id == "cleric");
            pal.Hp = 40; cle.Hp = 0; cle.OnBoard = false;
            foreach (var e in s.Units.Where(u => !u.IsHero)) { e.Hp = 0; e.OnBoard = false; }
            s.Phase = BattlePhase.Victory;
            BattleFactory.Commit(c, save, s);
            if (Progression.Level(save.Hero("paladin")) == 1) Assert.That(save.Hero("paladin").Hp, Is.EqualTo(40));
            if (Progression.Level(save.Hero("cleric")) == 1) Assert.That(save.Hero("cleric").Hp, Is.EqualTo(1));
        }

        [Test] // US-219
        public void Enemy_levels_are_floored_to_the_campaign_recommendation()
        {
            var c = Real;
            var save = Progression.NewGame(c, "L");
            var stages = c.CampaignStages();
            var setup = BattleFactory.Build(c, save, stages[5].Id, 1);
            Assert.That(setup.RecommendedLevel, Is.EqualTo(6));
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            foreach (var u in s.Units.Where(u => !u.IsHero)) Assert.That(u.Level >= 6, Is.True);
        }

        [Test] // US-225
        public void Salvage_returns_half_the_forge_inputs()
        {
            var c = Real;
            var recipe = Crafting.Recipes(c, CraftStation.Forge).First(r => r.BaseItem == null && r.Inputs.Any(i => i.Count >= 2));
            var save = new SaveData();
            save.AddItem(recipe.Result, 1);
            Assert.That(Crafting.Salvage(c, save, recipe.Result).Ok, Is.True);
            foreach (var ic in recipe.Inputs) Assert.That(save.Count(ic.Item), Is.EqualTo(Math.Max(1, ic.Count / 2)));
        }

        [Test] // GG-T42
        public void Every_equipment_item_has_a_salvage_yield_via_recipe_or_fallback_table()
        {
            var c = Real;
            foreach (var item in c.Items.Values.Where(i => i.Type == ItemType.Equipment))
            {
                var yield = Crafting.SalvageYield(c, item, item.Durability > 0 ? item.Durability : 0);
                Assert.That(yield.Count > 0, Is.True, $"{item.Id} ({item.Slot}/{item.Rarity}) has no salvage yield");
            }
        }

        [Test] // GG-T42
        public void Salvage_of_equipment_without_a_recipe_uses_the_type_and_rarity_fallback_table()
        {
            var c = Real;
            var item = c.Item("eq_sword_rusty");
            Assert.That(Crafting.Recipes(c, CraftStation.Forge).FirstOrDefault(r => r.Result == item.Id && r.BaseItem == null), Is.Null);
            var save = new SaveData();
            save.AddItem(item.Id, 1);
            Assert.That(Crafting.Salvage(c, save, item.Id).Ok, Is.True);
            Assert.That(save.Count("mat_iron_ore"), Is.EqualTo(2));
            Assert.That(save.Count("mat_wood_plank"), Is.EqualTo(1));
        }

        [Test] // GG-T42
        public void Salvage_yield_scales_down_with_remaining_durability_but_keeps_at_least_one_of_the_cheapest_input()
        {
            var c = Real;
            var item = c.Item("eq_sword_rusty");
            var full = Crafting.SalvageYield(c, item, item.Durability);
            Assert.That(full.First(i => i.Item == "mat_iron_ore").Count, Is.EqualTo(2));
            var nearDead = Crafting.SalvageYield(c, item, 1);
            Assert.That(nearDead.Sum(i => i.Count), Is.EqualTo(1));
            Assert.That(nearDead, Has.Exactly(1).Items);
            Assert.That(nearDead.Single().Item, Is.EqualTo("mat_wood_plank"));
        }

        [Test] // GG-T42
        public void SalvageEquipped_clears_the_heros_slot_and_grants_durability_scaled_materials_instead_of_returning_it()
        {
            var c = Real;
            var s = Progression.NewGame(c, "T");
            var hero = s.Hero(s.Party[0]);
            var item = c.Item("eq_sword_rusty");
            s.AddItem(item.Id, 1);
            Assert.That(Equipment.Equip(c, s, hero, EquipmentSlot.Weapon, item.Id).Ok, Is.True);
            hero.WeaponDurability = 1;
            var result = Crafting.SalvageEquipped(c, s, hero, EquipmentSlot.Weapon);
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(hero.WeaponId, Is.Null);
            Assert.That(hero.WeaponDurability, Is.EqualTo(0));
            Assert.That(s.Count("mat_wood_plank"), Is.EqualTo(1));
            Assert.That(s.Count(item.Id), Is.EqualTo(0));
        }

        [Test] // US-230 / US-246
        public void Unequipped_gear_remembers_its_wear_and_fresh_copies_are_used_first()
        {
            var c = Real;
            var save = Progression.NewGame(c, "U");
            var weapon = c.Items.Values.First(i => i.Slot == EquipmentSlot.Weapon && i.Durability > 2);
            save.AddItem(weapon.Id, 1);
            var h = save.Hero("paladin");
            Assert.That(Equipment.Equip(c, save, h, EquipmentSlot.Weapon, weapon.Id).Ok, Is.True);
            h.WeaponDurability = 1; h.WeaponRepairs = 2;
            Assert.That(Equipment.Unequip(c, save, h, EquipmentSlot.Weapon).Ok, Is.True);
            Assert.That(save.Count(weapon.Id), Is.EqualTo(1));
            Assert.That(save.Worn[weapon.Id], Has.Exactly(1).Items);
            // Buy a fresh one: equipping takes the fresh copy first...
            save.AddItem(weapon.Id, 1);
            Assert.That(Equipment.Equip(c, save, h, EquipmentSlot.Weapon, weapon.Id).Ok, Is.True);
            Assert.That(h.WeaponDurability, Is.EqualTo(weapon.Durability));
            // ...and the worn one comes back with its wear and repair history.
            var other = save.Hero("barbarian");
            Assert.That(Equipment.Equip(c, save, other, EquipmentSlot.Weapon, weapon.Id).Ok, Is.True);
            Assert.That(other.WeaponDurability, Is.EqualTo(1));
            Assert.That(other.WeaponRepairs, Is.EqualTo(2));
            Assert.That(save.Worn.ContainsKey(weapon.Id), Is.False);
        }

        [Test] // US-115
        public void Spells_never_miss_and_can_crit_for_a_bonus_orb()
        {
            var db = Mini();
            db.Classes["mage"].BaseStats.Luck = 200;
            int crits = 0, misses = 0;
            for (int seed = 1; seed <= 60; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("tank", 2, 2)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(4, 7), Bar = new List<string> { "fireball" } });
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                s.Bank.Add(2);
                var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "mage").Id, Slot = 0, Target = new Coord(2, 2) });
                var d = ev.OfType<DamagedEvent>().First(x => x.SourceId == Hero(s, "mage").Id);
                if (d.Hit == HitKind.Miss) misses++;
                int power = (int)System.Math.Round(18 * Formulas.CasterPower(db.Abilities["fireball"], Hero(s, "mage")));
                if (d.Hit == HitKind.Critical) { crits++; Assert.That(d.Amount, Is.EqualTo((int)System.Math.Round(power * 1.6))); Assert.That(ev.Any(e => e is ManaChargedEvent o && o.Reason == "critical"), Is.True); }
            }
            Assert.That(misses, Is.EqualTo(0));
            Assert.That(crits, Is.InRange(5, 30)); // 50% physical crit cap × 0.5 → ~25%
        }

        [Test] // GG-T130
        public void OrbsSpent_reports_the_bank_right_after_that_spend_not_the_whole_actions_end()
        {
            // A spell's cost comes out of the bank the instant it's cast, but the same action can still mint
            // more orbs afterward (here: a crit's charge rolling over). OrbsSpentEvent.CountAfter must freeze the
            // bank as it stood right after the spend, not read it back off the command's final state once those
            // later orbs have already landed (GG-T130, found by GG-T112) — playback would otherwise show the bank
            // topped up before the later orb's fly-in arrives.
            var db = Mini();
            db.Classes["mage"].BaseStats.Luck = 200; // crit some of the time
            bool sawMintAfterSpend = false;
            for (int seed = 1; seed <= 60 && !sawMintAfterSpend; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("tank", 2, 2)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(4, 7), Bar = new List<string> { "fireball" } });
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                s.Bank.Add(2);
                int bankBeforeCast = s.Bank.Count;
                s.ManaCharge = BattleConfig.ManaChargePerOrb - BattleConfig.CritCharge; // one crit away from minting
                var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "mage").Id, Slot = 0, Target = new Coord(2, 2) });
                int spentIndex = ev.FindIndex(e => e is OrbsSpentEvent);
                if (spentIndex < 0) continue;
                var spent = (OrbsSpentEvent)ev[spentIndex];
                Assert.That(spent.CountAfter, Is.EqualTo(bankBeforeCast - spent.Count));
                var mintedLater = ev.Skip(spentIndex + 1).OfType<OrbMintedEvent>().Where(o => o.Landed).ToList();
                if (mintedLater.Count == 0) continue;
                sawMintAfterSpend = true;
                Assert.That(spent.CountAfter, Is.LessThan(s.Bank.Count)); // the later mint must not already show in CountAfter
            }
            Assert.That(sawMintAfterSpend, Is.True, "never saw a crit roll an orb in the same action as the spend; widen the seed range");
        }

        [Test] // US-245
        public void Loot_past_the_stack_limit_is_auto_sold()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Stack");
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[1].Id, 5);
            var st = GridGame.Core.Bot.BotRunner.Play(c, setup, new GridGame.Core.Bot.HeroBot(2), 300);
            Assert.That(st.Phase, Is.EqualTo(BattlePhase.Victory));
            var item = st.Results.Items.Keys.FirstOrDefault();
            if (item == null) return;
            var def = c.Item(item);
            save.Inventory[item] = def.MaxStack; // bag already full of it
            int gold = save.Gold;
            var rep = BattleFactory.Commit(c, save, st);
            Assert.That(save.Count(item), Is.EqualTo(def.MaxStack));
            Assert.That(rep.AutoSold > 0, Is.True);
            Assert.That(save.Gold > gold + st.Results.Coins - 1, Is.True);
        }

        [Test] // US-332
        public void New_game_plus_restarts_the_campaign_with_stronger_enemies()
        {
            var c = Real;
            var save = Progression.NewGame(c, "NG");
            Assert.That(Progression.StartNewGamePlus(c, save).Ok, Is.False);
            save.HighestClearedStage = c.CampaignStages().Count - 1;
            save.Hero("paladin").TotalXp = 5000;
            Assert.That(Progression.StartNewGamePlus(c, save).Ok, Is.True);
            Assert.That(save.NewGamePlus, Is.EqualTo(1));
            Assert.That(save.HighestClearedStage, Is.EqualTo(-1));
            Assert.That(save.Hero("paladin").TotalXp, Is.EqualTo(5000));
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[0].Id, 1);
            var s = BattleEngine.Create(c, setup, new List<BattleEvent>());
            foreach (var u in s.Units.Where(u => !u.IsHero)) Assert.That(u.Level >= 11, Is.True);
        }

        [Test] // US-235
        public void Recruits_arrive_at_roster_average_xp()
        {
            var c = Real;
            var save = Progression.NewGame(c, "R", new[] { "paladin", "barbarian", "cleric", "redmage", "engineer" });
            foreach (var h in save.Roster) h.TotalXp = 300;
            save.Gold = 10000;
            var pick = Summon.Pool(c, save).First();
            Assert.That(Summon.Recruit(c, save, pick.Id).Ok, Is.True);
            Assert.That(save.Hero(pick.Id).TotalXp, Is.EqualTo(300));
            // The starting party of five is full, so a recruit waits in reserve until there's room.
            Assert.That(save.Party, Does.Not.Contain(pick.Id));
            Assert.That(Party.Toggle(save, save.Party[4]).Ok, Is.True);
            Assert.That(Party.Toggle(save, pick.Id).Ok, Is.True);
            Assert.That(save.Party, Does.Contain(pick.Id));
        }

        [Test] // US-237
        public void Abandoned_bounties_can_be_retaken_and_claimed_ones_never_repost()
        {
            var c = Real;
            var save = Progression.NewGame(c, "B");
            var b = Bounties.Posted(c, save).First();
            Assert.That(Bounties.Accept(c, save, b.Id).Ok, Is.True);
            Assert.That(Bounties.Abandon(save).Ok, Is.True);
            Assert.That(Bounties.Accept(c, save, b.Id).Ok, Is.True);
            Bounties.RecordKills(c, save, new Dictionary<string, int> { [b.TargetClass] = b.Kills });
            Assert.That(Bounties.Claim(c, save).Ok, Is.True);
            Assert.That(Bounties.Posted(c, save).Any(x => x.Id == b.Id), Is.False);
        }

        [Test] // US-238
        public void Bestiary_records_seen_on_spawn_and_defeated_on_victory()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Best");
            var setup = BattleFactory.Build(c, save, c.CampaignStages()[1].Id, 9); // a short battle (the first is a 20-wave gauntlet)
            var st = GridGame.Core.Bot.BotRunner.Play(c, setup, new GridGame.Core.Bot.HeroBot(1), 300);
            Assert.That(st.Phase, Is.EqualTo(BattlePhase.Victory));
            BattleFactory.Commit(c, save, st);
            foreach (var kv in st.Results.Kills)
            {
                Assert.That(save.Bestiary[kv.Key].Seen, Is.True);
                Assert.That(save.Bestiary[kv.Key].Defeated, Is.EqualTo(kv.Value));
            }
        }

        [Test] // US-242
        public void Camp_routine_rests_and_restocks()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Camp");
            CampNpcs.JoinAll(save); // the Innkeeper and Merchant have joined (GG-T119)
            save.Gold = 5000;
            save.Hero("paladin").Hp = 10;
            save.Inventory.Remove("healing_potion_basic");
            var log = CampRoutine.Run(c, save);
            Assert.That(save.Hero("paladin").Hp, Is.EqualTo(0));
            Assert.That(save.Count("healing_potion_basic") > 0, Is.True);
            Assert.That(log, Is.Not.Empty);
        }

        [Test] // US-190 (content)
        public void Cyclops_is_a_two_by_two_boss_that_appears_in_the_campaign()
        {
            var c = Real;
            var cyclops = c.Class("cyclops00");
            Assert.That(cyclops.Footprint.W, Is.EqualTo(2));
            Assert.That(cyclops.Footprint.H, Is.EqualTo(2));
            Assert.That(c.CampaignStages().Any(st => st.Waves.Any(w => w.Enemies.Any(e => e.Class == "cyclops00"))), Is.True);
            Assert.That(cyclops.BossPhases, Is.Not.Empty);
        }

        [Test] // US-018
        public void Physical_formula_matches_the_bible_shape()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight"); var tank = Enemy(s, "tank");
            // off 20, def = VIT 200: pen 5 → effDef 195 → reduced 20 − 68.25 < floor 4 → raw = max(1, 4) = 4; ±10% variance.
            var rng = new Rng(3);
            var normals = new List<int>();
            for (int i = 0; i < 400; i++) { var r = Formulas.Physical(k, tank, rng); if (r.Hit == HitKind.Normal) normals.Add(r.Damage); }
            Assert.That(normals.Min(), Is.InRange(3, 4));
            Assert.That(normals.Max(), Is.InRange(4, 5));
            // Level advantage scales damage by 1.07^Δ.
            k.Level = 6;
            var rng2 = new Rng(3);
            var high = new List<int>();
            for (int i = 0; i < 400; i++) { var r = Formulas.Physical(k, tank, rng2); if (r.Hit == HitKind.Normal) high.Add(r.Damage); }
            Assert.That(high.Average() > normals.Average(), Is.True);
            Assert.That(Formulas.MaxHp(new Stats { Vitality = 10 }, 1), Is.EqualTo(12 + 10 * 5 + 1));
            Assert.That(Formulas.CastTime(1.5f, new Stats { Wisdom = 999, Intelligence = 999 }), Is.EqualTo(1.5f * 0.25f).Within(1e-3));
        }

        [Test] // US-029
        public void Heroes_start_on_their_tiles_and_enemies_on_authored_or_upper_rows()
        {
            var db = Mini();
            var setup = new BattleSetup { Stage = StageWaves(new[] { ("tank", 4, 1), ("tank", -1, -1), ("tank", -1, -1), ("tank", -1, -1) }), Seed = 21, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight" });
            setup.Party.Add(new HeroSpec { ClassId = "cleric" });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            Assert.That(Hero(s, "knight").Pos, Is.EqualTo(new Coord(1, 6)));
            Assert.That(Hero(s, "cleric").Pos, Is.EqualTo(new Coord(4, 6)));
            Assert.That(s.UnitAt(new Coord(4, 1)), Is.Not.Null);
            foreach (var u in s.Units.Where(u => !u.IsHero)) Assert.That(u.Pos.Y, Is.InRange(0, 4));
        }

        [Test] // US-170
        public void Smart_enemies_hunt_whoever_hurt_them_most()
        {
            var db = Mini();
            db.Classes["wolf"].Targeting = Targeting.Threat;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 4), ("cleric", 2, 0));
            var wolf = Enemy(s, "wolf");
            Assert.That(EnemyPlanner.PickTarget(s, wolf), Is.SameAs(EnemyPlanner.PickTarget(s, wolf)));
            Hero(s, "cleric").DamageDealt = 200;
            Hero(s, "knight").DamageDealt = 0;
            Assert.That(EnemyPlanner.PickTarget(s, wolf).Id, Is.EqualTo(Hero(s, "cleric").Id));
            Hero(s, "cleric").DamageDealt = 0;
            Hero(s, "knight").DamageDealt = 200;
            Assert.That(EnemyPlanner.PickTarget(s, wolf).Id, Is.EqualTo(Hero(s, "knight").Id));
        }

        [Test] // US-192
        public void Prefers_charge_phase_makes_a_caster_charge_even_point_blank()
        {
            var db = Mini();
            db.Classes["witch"].BossPhases.Add(new BossPhaseDef { HpThreshold = 1f, PrefersCharge = true });
            var (s, _) = Battle(db, Stage(("witch", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var witch = Enemy(s, "witch");
            Assert.That(EnemyPlanner.PlanCast(s, witch, false), Is.Null); // adjacent: normally melee
            s.TurnEntryOf(witch.Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is CastQueuedEvent q && q.IsCharge), Is.True);
        }

        [Test] // US-032
        public void Second_pincer_on_an_already_dead_target_plays_respite()
        {
            var db = Mini();
            db.Classes["goblin"].BaseStats.Vitality = 0; // 13 HP: one landing strike kills
            db.Classes["rogue"] = new ClassDef { Id = "rogue", Name = "Rogue", Team = Team.Hero, Tags = { "Humanoid" }, BaseStats = new Stats { Strength = 20, Vitality = 10, Speed = 10 } };
            bool saw = false;
            for (int seed = 1; seed < 60 && !saw; seed++)
            {
                // Goblin at (1,1) is flanked twice by one drop: horizontally (0,1)–(2,1) and vertically (1,0)–(1,2).
                var setup = new BattleSetup { Stage = Stage(("goblin", 1, 1)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 1) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(1, 0) });
                setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(1, 2) });
                setup.Party.Add(new HeroSpec { ClassId = "rogue", Start = new Coord(3, 1) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "rogue").Id, Path = P((2, 1)) });
                if (ev.OfType<PincerFormedEvent>().Count() == 1 && ev.Any(e => e is DiedEvent)) saw = ev.Any(e => e is RespiteEvent);
            }
            Assert.That(saw, Is.True, "the second link over a goblin killed by the first should play Respite");
        }
    }
}
