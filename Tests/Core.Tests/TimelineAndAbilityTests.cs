using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    public class TimelineAndAbilityTests
    {
        private static void Wiggle(BattleState s, string cls)
        {
            var h = Hero(s, cls);
            var t = new Coord(h.Pos.X, h.Pos.Y == 7 ? 6 : h.Pos.Y + 1);
            if (s.UnitAt(t) != null) t = new Coord(h.Pos.X, h.Pos.Y - 1);
            BattleEngine.Apply(s, new MoveCommand { HeroId = h.Id, Path = new() { t } });
        }

        [Test]
        public void Clock_only_advances_on_committed_actions()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            long c0 = s.Clock;
            int ip0 = s.Timeline[0].Ip;
            int turn = BattleEngine.TurnTicks(s);
            Assert.That(s.Clock, Is.EqualTo(c0)); // creating/inspecting costs nothing
            Wiggle(s, "knight");
            Assert.That(s.Clock, Is.EqualTo(c0 + turn)); // GG-T188: the turn runs to the next enemy arrival
            Assert.That(s.Timeline[0].Ip, Is.Not.EqualTo(ip0));
        }

        // ── GG-T188 (§2.0): the held turn ──────────────────────────────────────────────────────────────

        [Test]
        public void Turn_length_is_the_time_until_the_next_enemy_icon_reaches_act()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0), ("tank", 2, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            int expected = System.Math.Min(BattleEngine.TicksUntilTurn(s, wolf.Id), BattleEngine.TicksUntilTurn(s, tank.Id));
            Assert.That(BattleEngine.TurnTicks(s), Is.EqualTo(expected));
            Assert.That(BattleEngine.TurnWindowSeconds(s), Is.EqualTo(System.Math.Max(BattleConfig.MinTurnSeconds, expected / (float)BattleConfig.TicksPerSecond)).Within(1e-4));

            // A short turn still gets the minimum real-time window.
            s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - 1;
            Assert.That(BattleEngine.TurnTicks(s), Is.EqualTo(1));
            Assert.That(BattleEngine.TurnWindowSeconds(s), Is.EqualTo(BattleConfig.MinTurnSeconds).Within(1e-4));

            // The held timeline: the next icon reaches the left edge (u 0, acts) exactly at the end of the turn, never before.
            int t = BattleEngine.TurnTicks(s);
            var wolfEntry = s.TurnEntryOf(wolf.Id);
            Assert.That(BattleEngine.TimelineUAfter(s, t - 1)[wolfEntry.Id], Is.GreaterThan(0f));
            Assert.That(BattleEngine.TimelineUAfter(s, t)[wolfEntry.Id], Is.EqualTo(0f));

            // Nothing can arrive (every enemy Frozen): no limit on the hold.
            foreach (var e in s.LivingList(Team.Enemy)) e.Buffs.Add(new BuffInstance { Def = db.Buffs["frozen"], Remaining = 1 });
            Assert.That(BattleEngine.TurnTicks(s), Is.EqualTo(-1));
            Assert.That(BattleEngine.TurnWindowSeconds(s), Is.LessThan(0f));
            Assert.That(BattleEngine.IsTimeUp(s, 999f), Is.False);
        }

        [Test]
        public void Forced_drop_ends_the_turn_where_the_hero_stands()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            var start = k.Pos;
            long c0 = s.Clock;
            float window = BattleEngine.TurnWindowSeconds(s);

            // Let go before time is up without moving: a cancel, not a turn.
            var cancel = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, HeldSeconds = window * 0.5f });
            Assert.That(cancel.Any(e => e is AbilityRefusedEvent), Is.True);
            Assert.That(s.Clock, Is.EqualTo(c0));
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(0));

            // Held the whole window: dropped where it stands, the turn is spent and the enemy acts.
            int turn = BattleEngine.TurnTicks(s);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, HeldSeconds = window });
            Assert.That(ev.OfType<TurnTimeUpEvent>().Single().HeroId, Is.EqualTo(k.Id));
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.False);
            Assert.That(k.Pos, Is.EqualTo(start));
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(1));
            Assert.That(s.Clock, Is.EqualTo(c0 + turn));
            Assert.That(ev.OfType<TurnStartedEvent>().Any(), Is.True);
            Assert.That(ev.Last(), Is.InstanceOf<PlayerControlEvent>());
        }

        [Test]
        public void Early_drop_ends_the_turn_and_the_next_enemy_acts_on_arrival()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0), ("tank", 2, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            for (int turn = 0; turn < 6; turn++)
            {
                // Whoever's icon is nearest ACT is the one that acts at the end of this turn.
                var next = s.LivingList(Team.Enemy).OrderBy(e => BattleEngine.TicksUntilTurn(s, e.Id)).ThenBy(e => s.TurnEntryOf(e.Id).Id).First();
                int ticks = BattleEngine.TurnTicks(s);
                long c0 = s.Clock;
                var k = Hero(s, "knight");
                var t = new Coord(k.Pos.X, k.Pos.Y == 7 ? 6 : 7);
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = new() { t }, HeldSeconds = 0.3f });
                Assert.That(ev.OfType<TurnTimeUpEvent>(), Is.Empty);
                Assert.That(s.Clock, Is.EqualTo(c0 + ticks), $"turn {turn}");
                Assert.That(ev.OfType<TurnStartedEvent>().First().UnitId, Is.EqualTo(next.Id), $"turn {turn}");
                Assert.That(s.TurnEntryOf(next.Id).Ip, Is.LessThan(BattleConfig.BarLength)); // re-loads after acting
                AssertInvariants(s);
            }
        }

        [Test]
        public void Countdown_badge_counts_turns_not_ticks()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0), ("tank", 2, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            s.TurnEntryOf(wolf.Id).Ip = 900;
            s.TurnEntryOf(tank.Id).Ip = 0;
            Assert.That(BattleEngine.ActionsUntilTurn(s, wolf.Id), Is.EqualTo(0)); // acts at the end of this turn
            Assert.That(BattleEngine.ActionsUntilTurn(s, tank.Id), Is.GreaterThanOrEqualTo(1)); // the fast wolf goes first, maybe more than once
        }

        [Test]
        public void Bot_drags_fit_inside_the_turn_window()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            s.TurnEntryOf(s.Units.Single(u => u.Def.Id == "wolf").Id).Ip = BattleConfig.BarLength - 1; // a 1-tick turn
            float window = BattleEngine.TurnWindowSeconds(s);
            int maxSteps = BattleEngine.MaxDragSteps(s);
            Assert.That(maxSteps, Is.EqualTo((int)(BattleConfig.MinTurnSeconds / BattleConfig.BotSecondsPerStep)));
            var bot = new GridGame.Core.Bot.HeroBot(3);
            for (int i = 0; i < 4 && !s.IsOver; i++)
            {
                var cmd = bot.Decide(s);
                if (cmd is MoveCommand m)
                {
                    Assert.That(m.Path.Count, Is.LessThanOrEqualTo(BattleEngine.MaxDragSteps(s)));
                    Assert.That(m.HeldSeconds, Is.GreaterThan(0f));
                    if (BattleEngine.TurnWindowSeconds(s) > 0f) Assert.That(m.HeldSeconds, Is.LessThanOrEqualTo(BattleEngine.TurnWindowSeconds(s) + 1e-4f));
                }
                BattleEngine.Apply(s, cmd);
            }
            _ = window;
        }

        [Test]
        public void Enemy_acts_when_its_icon_reaches_the_trigger()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            int turns = 0;
            for (int i = 0; i < 10; i++)
            {
                Wiggle(s, "knight");
                AssertInvariants(s);
            }
            // every action is 2s; speed-10 wolf takes ~7s per turn → several turns in 20s
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            Assert.That(wolf.Pos, Is.Not.EqualTo(new Coord(0, 0)));
            _ = turns;
        }

        [Test]
        public void Shield_protects_heroes_and_skips_to_next_enemy_turn()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "protection"), Is.True);
            Assert.That(ev.Any(e => e is TurnStartedEvent), Is.True);
            Assert.That(ev.Last() is PlayerControlEvent, Is.True);
        }

        [Test]
        public void Pincer_pushes_its_foe_back_near_the_left_edge()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            var entry = s.TurnEntryOf(tank.Id);
            entry.Ip = 920;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var push = ev.OfType<PushedBackEvent>().Single();
            Assert.That(push.ToU > push.FromU, Is.True); // further right = acts later (GG-T286)
        }

        [Test]
        public void Pincer_pushback_works_far_from_the_left_edge_too()
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Speed = 10; // a 6.3 s lap: on the drawn bar even from IP 0
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            s.TurnEntryOf(tank.Id).Ip = 0; // just acted: no IP left to lose, so it stands still for the push instead
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var push = ev.OfType<PushedBackEvent>().Single(p => p.UnitId == tank.Id);
            Assert.That(push.ToU - push.FromU, Is.EqualTo(BattleConfig.PincerPushbackSeconds / BattleConfig.BarSeconds).Within(1e-4));
            Assert.That(push.StunTicks, Is.EqualTo(Formulas.SecondsToTicks(BattleConfig.PincerPushbackSeconds)));
        }

        [Test]
        public void Instant_spell_spends_exact_orbs_damages_and_applies_buff()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(3);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = new Coord(2, 0) });
            Assert.That(s.Bank.Count, Is.EqualTo(1 + ev.OfType<OrbMintedEvent>().Count(o => o.Landed)));
            int expected = (int)System.Math.Round(18 * Formulas.CasterPower(db.Abilities["fireball"], k)); // §13.1.5: INT scales it
            Assert.That(ev.Any(e => e is DamagedEvent d && d.Amount == expected), Is.True);
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "burning"), Is.True);
        }

        [Test]
        public void Unaffordable_or_silenced_spell_is_refused_without_cost()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(1);
            long clock = s.Clock;
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = new Coord(2, 0) });
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.True);
            Assert.That(s.Bank.Count, Is.EqualTo(1));
            Assert.That(s.Clock, Is.EqualTo(clock));
        }

        [Test]
        public void Any_orbs_pay_for_any_spell()
        {
            var bank = new ManaBank();
            bank.Add(3);
            Assert.That(bank.CanAfford(2), Is.True);
            Assert.That(bank.Spend(2), Is.True);
            Assert.That(bank.Count, Is.EqualTo(1));
            Assert.That(bank.CanAfford(2), Is.False);
            Assert.That(bank.Spend(2), Is.False);
            Assert.That(bank.Count, Is.EqualTo(1));
        }

        [Test]
        public void Bank_overflow_fades_extra_orbs()
        {
            var bank = new ManaBank();
            Assert.That(bank.Add(15), Is.EqualTo(12));
            Assert.That(bank.Add(1), Is.EqualTo(0));
            Assert.That(bank.Count, Is.EqualTo(12));
        }

        [Test]
        public void Cast_time_spell_rides_timeline_then_resolves()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "meteor" };
            s.Bank.Add(2);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(2, 0) });
            Assert.That(ev.Any(e => e is CastQueuedEvent), Is.True);
            Assert.That(s.Bank.Count, Is.EqualTo(0)); // orbs spent at cast start
            bool resolved = ev.Any(e => e is AbilityResolvedEvent r && r.AbilityId == "meteor");
            for (int i = 0; i < 5 && !resolved; i++)
            {
                ev = BattleEngine.Apply(s, new ShieldCommand());
                resolved = ev.Any(e => e is AbilityResolvedEvent r && r.AbilityId == "meteor");
            }
            Assert.That(resolved, Is.True);
            Assert.That(s.CastEntryOf(m.Id), Is.Null);
        }

        [Test]
        public void Fifth_hero_cast_is_refused_by_cast_cap()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            for (int i = 0; i < 4; i++)
                s.Timeline.Add(new TimelineEntry { Id = 900 + i, Kind = TimelineKind.Cast, OwnerId = m.Id, CasterTeam = Team.Hero, AbilityId = "meteor", RemainingTicks = 999, TotalCastTicks = 999 });
            m.Bar[0] = new AbilitySlot { AbilityId = "meteor" };
            s.Bank.Add(2);
            Assert.That(BattleEngine.GetSlotStatus(s, m.Id, 0), Is.EqualTo(SlotStatus.CastCapReached));
        }

        [Test]
        public void Frozen_halts_the_enemy_icon_and_expires_into_wet()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "frost" };
            s.Bank.Add(2);
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(2, 2) });
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "frozen"), Is.True);
            // Frozen survives the action that applied it and thaws into Wet at the next turn boundary.
            ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((5, 6)) });
            Assert.That(ev.Any(e => e is BuffRemovedEvent r && r.BuffId == "frozen"), Is.True);
            Assert.That(tank.HasBuff("wet"), Is.True);
        }

        [Test]
        public void Lightning_on_wet_hits_harder_and_fire_on_wet_makes_steam()
        {
            var db = Mini();
            db.Abilities["bolt"] = new AbilityDef { Id = "bolt", Name = "Bolt", Kind = AbilityKind.Spell, Cost = 0, BaseDamage = 14, DamageType = DamageType.Lightning, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor };
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["wet"], Remaining = 6 });
            m.Bar[0] = new AbilitySlot { AbilityId = "bolt" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(2, 2) });
            int shock = (int)System.Math.Round(21 * Formulas.CasterPower(db.Abilities["bolt"], m)); // 14 × 1.5 wet, × INT power
            Assert.That(ev.Any(e => e is DamagedEvent d && d.Amount == shock), Is.True);

            m.Bar[1] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(2);
            tank.Buffs.RemoveAll(b => b.Def.Id == "wet");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["wet"], Remaining = 6 });
            ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 1, Target = tank.Pos });
            Assert.That(ev.Any(e => e is BuffRemovedEvent r && r.Reason == "steam"), Is.True);
        }

        [Test]
        public void Potion_item_heals_and_consumes_a_charge()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var k = Hero(s, "knight");
            k.Hp = 10;
            k.Bar[0] = new AbilitySlot { ItemId = "potion", Charges = 2, MaxCharges = 9, FromInventory = true };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            Assert.That(ev.Any(e => e is HealedEvent h && h.Amount == 30), Is.True);
            Assert.That(k.Bar[0].Charges, Is.EqualTo(1));
            Assert.That(s.Results.ItemsConsumed["potion"], Is.EqualTo(1));
        }

        [Test]
        public void Teleport_relocates_and_fires_pincer_then_goes_on_cooldown()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 1, 0), ("mage", 5, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "teleport" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = new Coord(3, 0) });
            Assert.That(ev.Any(e => e is TeleportedEvent), Is.True);
            Assert.That(ev.Any(e => e is PincerFormedEvent), Is.True);
            Assert.That(BattleEngine.GetSlotStatus(s, m.Id, 0), Is.EqualTo(SlotStatus.Cooldown));
        }

        [Test]
        public void Quicken_hastens_an_enemy_icon()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "quicken" };
            s.Bank.Add(1);
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            s.TurnEntryOf(tank.Id).Ip = 850; // Speed 1: 75 ticks out, on the drawn bar
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = tank.Pos });
            var h = ev.OfType<HastenedEvent>().Single();
            Assert.That(h.ToU, Is.LessThan(h.FromU)); // toward the left edge (GG-T286)
        }

        [Test]
        public void Enemy_charge_is_telegraphed_and_can_be_interrupted()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("witch", 2, 0)), ("knight", 5, 7), ("cleric", 0, 7));
            var witch = s.Units.Single(u => u.Def.Id == "witch");
            s.TurnEntryOf(witch.Id).Ip = 990;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is CastQueuedEvent q && q.IsCharge), Is.True);
            var cast = s.CastEntryOf(witch.Id);
            Assert.That(cast, Is.Not.Null);
            // Pincer the witch repeatedly; hits stagger and eventually cancel the charge (minting an orb).
            s.Units.Single(u => u.Def.Id == "knight").Pos = new Coord(1, 0);
            var cleric = Hero(s, "cleric");
            cleric.Pos = new Coord(3, 1);
            ev = BattleEngine.Apply(s, new MoveCommand { HeroId = cleric.Id, Path = P((3, 0)) });
            Assert.That(ev.Any(e => e is CastStaggeredEvent || e is CastCancelledEvent || e is DiedEvent), Is.True);
        }

        [Test]
        public void Sleep_breaks_on_damage()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            tank.Buffs.Add(new BuffInstance { Def = db.Buffs["sleep"], Remaining = 3 });
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            if (ev.OfType<DamagedEvent>().Any(d => d.TargetId == tank.Id && d.Hit != HitKind.Miss))
                Assert.That(tank.HasBuff("sleep"), Is.False);
        }

        [Test]
        public void Weapon_wears_per_pincer_and_shatters_at_zero()
        {
            var db = Mini();
            var stage = Stage(("tank", 2, 3));
            var setup = new BattleSetup { Stage = stage, Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), WeaponId = "sword", WeaponDurability = 2 });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new());
            var cleric = Hero(s, "cleric");
            var all = new System.Collections.Generic.List<BattleEvent>();
            for (int i = 0; i < 4; i++)
                all.AddRange(BattleEngine.Apply(s, new MoveCommand { HeroId = cleric.Id, Path = cleric.Pos.Y == 4 ? P((3, 3)) : P((3, 4)) }));
            Assert.That(all.Any(e => e is WeaponWornEvent), Is.True);
            Assert.That(all.Any(e => e is WeaponShatteredEvent), Is.True);
            Assert.That(Hero(s, "knight").WeaponId, Is.Null);
        }

        [Test] // GG-T41
        public void A_shattered_weapon_is_recorded_in_results_so_commit_can_turn_it_into_a_broken_fragment()
        {
            var db = Mini();
            var stage = Stage(("tank", 2, 3));
            var setup = new BattleSetup { Stage = stage, Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), WeaponId = "sword", WeaponDurability = 2 });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new());
            var cleric = Hero(s, "cleric");
            for (int i = 0; i < 4; i++)
                BattleEngine.Apply(s, new MoveCommand { HeroId = cleric.Id, Path = cleric.Pos.Y == 4 ? P((3, 3)) : P((3, 4)) });
            Assert.That(s.Results.BrokenWeapons.TryGetValue("knight", out var broken), Is.True);
            Assert.That(broken, Does.Contain("sword"));
        }

        [Test] // GG-T40
        public void Shattered_weapon_auto_equips_the_bar_backup_on_the_hero_s_next_action()
        {
            var db = Mini();
            db.Items["dagger"] = new ItemDef { Id = "dagger", Name = "Dagger", Type = ItemType.Equipment, Slot = EquipmentSlot.Weapon, Durability = 9, BaseDamage = 1, Stats = new Stats { Strength = 1 } };
            var stage = Stage(("tank", 2, 3));
            var setup = new BattleSetup { Stage = stage, Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), WeaponId = "sword", WeaponDurability = 2, Bar = { "weapon:dagger" }, ItemCounts = { ["dagger"] = 1 } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new());
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            Assert.That(knight.Bar[0].WeaponId, Is.EqualTo("dagger"));

            var all = new System.Collections.Generic.List<BattleEvent>();
            for (int i = 0; i < 4; i++)
                all.AddRange(BattleEngine.Apply(s, new MoveCommand { HeroId = cleric.Id, Path = cleric.Pos.Y == 4 ? P((3, 3)) : P((3, 4)) }));
            Assert.That(all.Any(e => e is WeaponShatteredEvent), Is.True);
            Assert.That(knight.WeaponId, Is.Null);
            Assert.That(knight.Bar[0].WeaponId, Is.EqualTo("dagger")); // backup still stowed, waiting for the knight's own next action

            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((0, 3)) });
            Assert.That(ev.Any(e => e is WeaponSwappedEvent w && w.ToWeapon == "dagger" && w.FromWeapon == null), Is.True);
            Assert.That(knight.WeaponId, Is.EqualTo("dagger"));
            Assert.That(knight.Bar[0].IsEmpty, Is.True);
        }

        [Test] // GG-T47
        public void Flamebrand_applies_burning_on_a_landed_pincer_strike()
        {
            // bible §1.4 geometry reused: C(2,3) g(3,3) g(4,3) K(5,3); K drags west → C+K pincer g(3,3).
            var db = Mini();
            db.Classes["goblin"].BaseStats.Vitality = 200; // survive the pincer so the post-strike buff check still sees it alive
            db.Items["flame_sword"] = new ItemDef { Id = "flame_sword", Name = "Flame Sword", Type = ItemType.Equipment, Slot = EquipmentSlot.Weapon, Durability = 0, Stats = new Stats { Strength = 20 }, PincerApplyBuff = "burning", PincerApplyBuffChance = 1f };
            var setup = new BattleSetup { Stage = Stage(("goblin", 3, 3), ("goblin", 4, 3)), Seed = 1, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 3), WeaponId = "flame_sword" });
            var s = BattleEngine.Create(db, setup, new());
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((4, 3)) });
            Assert.That(ev.Any(e => e is PincerFormedEvent), Is.True);
            var burned = s.Units.Single(u => u.Def.Id == "goblin" && u.Pos == new Coord(3, 3));
            Assert.That(burned.HasBuff("burning"), Is.True);
        }

        [Test] // GG-T47
        public void Vampiric_edge_heals_the_wielder_a_fraction_of_damage_dealt()
        {
            var db = Mini();
            db.Items["vamp_dagger"] = new ItemDef { Id = "vamp_dagger", Name = "Vamp Dagger", Type = ItemType.Equipment, Slot = EquipmentSlot.Weapon, Durability = 0, Stats = new Stats { Strength = 20 }, LifestealFraction = 0.5f };
            var setup = new BattleSetup { Stage = Stage(("goblin", 3, 3), ("goblin", 4, 3)), Seed = 1, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 3), WeaponId = "vamp_dagger", Hp = 1 });
            var s = BattleEngine.Create(db, setup, new());
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((4, 3)) });
            Assert.That(ev.Any(e => e is HealedEvent h && h.TargetId == knight.Id), Is.True);
            Assert.That(knight.Hp > 1, Is.True);
        }

        [Test] // GG-T47
        public void Guardian_passive_reduces_damage_to_an_adjacent_ally()
        {
            var db = Mini();
            db.Items["guard_hammer"] = new ItemDef { Id = "guard_hammer", Name = "Guard Hammer", Type = ItemType.Equipment, Slot = EquipmentSlot.Weapon, Durability = 0, AdjacentAllyDamageReduction = 0.5f };
            db.Classes["wolf"].MoveRange = 0;
            db.Classes["wolf"].BaseStats.Luck = 0;
            db.Classes["wolf"].BaseStats.Strength = 60;

            int DamageTaken(bool guarded)
            {
                var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = 3, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(2, 4), WeaponId = guarded ? "guard_hammer" : null });
                var s = BattleEngine.Create(db, setup, new());
                var knight = Hero(s, "knight");
                var wolf = s.Units.Single(u => u.Def.Id == "wolf");
                s.TurnEntryOf(wolf.Id).Ip = 999;
                var ev = BattleEngine.Apply(s, new ShieldCommand());
                return ev.OfType<DamagedEvent>().Where(d => d.TargetId == knight.Id).Sum(d => d.Amount);
            }

            int unguarded = DamageTaken(false);
            int guarded = DamageTaken(true);
            Assert.That(unguarded > 0, Is.True);
            Assert.That(guarded < unguarded, Is.True);
        }

        [Test]
        public void Double_attack_passive_strikes_twice_and_counter_strikes_back()
        {
            var db = Mini();
            db.Abilities["double_attack"] = Real.Ability("double_attack");
            db.Abilities["counter_attack"] = Real.Ability("counter_attack");
            db.Classes["knight"].Abilities.Add("double_attack");
            db.Classes["tank"].Abilities.Add("counter_attack");
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            int knightHits = ev.OfType<AttackEvent>().Count(a => a.AttackerId == Hero(s, "knight").Id);
            Assert.That(knightHits >= 2, Is.True, "knight should strike twice");
            if (ev.OfType<DamagedEvent>().Any(d => d.TargetId == s.Units.Single(u => u.Def.Id == "tank").Id && d.Hit != HitKind.Miss))
                Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == s.Units.Single(u => u.Def.Id == "tank").Id), Is.True);
        }
    }
}
