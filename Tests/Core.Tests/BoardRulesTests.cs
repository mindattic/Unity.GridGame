using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    public class BoardRulesTests
    {
        [Test]
        public void Slide_displaces_occupant_into_tile_just_left()
        {
            // bible §1.1: H A E E → (drag H east) A H E E → (again) A E H E
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0), ("tank", 3, 0)), ("knight", 0, 0), ("cleric", 1, 0));
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 0), (2, 0)) });
            Assert.That(At(s, 0, 0).Def.Id, Is.EqualTo("cleric"));
            Assert.That(At(s, 1, 0).Def.Id, Is.EqualTo("tank"));
            Assert.That(At(s, 2, 0), Is.SameAs(knight));
            Assert.That(At(s, 3, 0).Def.Id, Is.EqualTo("tank"));
            Assert.That(ev.OfType<DisplacedEvent>().Count(), Is.EqualTo(2));
            AssertInvariants(s);
        }

        [Test]
        public void Movement_never_deals_damage()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 1, 0)), ("knight", 0, 0), ("cleric", 5, 7));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((1, 0), (2, 0)) });
            Assert.That(ev.Any(e => e is DamagedEvent d && d.TargetId == At(s, 0, 0).Id), Is.False);
            Assert.That(ev.TakeWhile(e => !(e is TimelineAdvancedEvent)).Any(e => e is DamagedEvent), Is.False);
        }

        [Test]
        public void Worked_example_1_4_slide_then_pincer()
        {
            // bible §1.4: [C](2,3) [g](3,3) [g](4,3) [K](5,3); K drags west → g shoved to (5,3); C+K pincer g(3,3).
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 3, 3), ("goblin", 4, 3)), ("cleric", 2, 3), ("knight", 5, 3));
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((4, 3)) });
            var pincer = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(new[] { knight.Id, Hero(s, "cleric").Id }, Does.Contain(pincer.Attacker1));
            Assert.That(pincer.Opponents, Has.Exactly(1).Items);
            var charge = ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "pincer");
            Assert.That(charge.Participants, Is.EqualTo(2)); // §3.1.9: a lean two-hero pincer charges at full rate
            Assert.That(charge.Amount > 0, Is.True);
            Assert.That(ev.Any(e => e is DisplacedEvent d && d.To == new Coord(5, 3)), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void Dragging_out_and_back_to_the_start_tile_still_pincers_if_it_lands_back_in_position()
        {
            // K(0,0) g(1,0) C(2,0) from the start (no pincer has fired yet — nothing moved there). Dragging K
            // out to (0,1) and straight back to (0,0) ends on the same tile it started, but that tile still
            // completes a pincer on g — it must fire, not be refused as a no-op cancel.
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 1, 0)), ("knight", 0, 0), ("cleric", 2, 0));
            var knight = Hero(s, "knight");
            var goblin = At(s, 1, 0);
            var hpBefore = goblin.Hp;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((0, 1), (0, 0)) });
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.False);
            var pincer = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(new[] { knight.Id, Hero(s, "cleric").Id }, Does.Contain(pincer.Attacker1));
            Assert.That(goblin.Hp < hpBefore, Is.True);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(0, 0)));
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(1));
            AssertInvariants(s);
        }

        [Test]
        public void Gap_or_ally_in_line_breaks_pincer()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 1, 0), ("tank", 3, 0)), ("knight", 0, 2), ("cleric", 4, 0), ("mage", 5, 5));
            // knight to (0,0): line (0,0)…(4,0) has a gap at (2,0) → no pincer
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((0, 1), (0, 0)) });
            Assert.That(ev.OfType<PincerFormedEvent>(), Is.Empty);
        }

        [Test]
        public void Diagonal_is_not_a_pincer_and_diagonal_step_is_refused_when_slip_is_disabled()
        {
            bool was = BattleConfig.DiagonalSlipEnabled;
            BattleConfig.DiagonalSlipEnabled = false;
            try
            {
                var db = Mini();
                var (s, _) = Battle(db, Stage(("tank", 1, 1)), ("knight", 0, 0), ("cleric", 3, 3));
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 2)) });
                Assert.That(ev.Any(e => e is StepBlockedEvent), Is.True);
                Assert.That(ev.OfType<PincerFormedEvent>(), Is.Empty);
                Assert.That(Hero(s, "cleric").Pos, Is.EqualTo(new Coord(3, 3)));
            }
            finally { BattleConfig.DiagonalSlipEnabled = was; }
        }

        [Test]
        public void Diagonal_slip_glides_onto_a_free_tile_when_enabled()
        {
            bool was = BattleConfig.DiagonalSlipEnabled;
            BattleConfig.DiagonalSlipEnabled = true;
            try
            {
                var db = Mini();
                var (s, _) = Battle(db, Stage(("tank", 5, 5)), ("cleric", 3, 3));
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 2)) });
                Assert.That(ev.Any(e => e is SteppedEvent st && st.From == new Coord(3, 3) && st.To == new Coord(2, 2)), Is.True);
                Assert.That(ev.Any(e => e is StepBlockedEvent), Is.False);
                Assert.That(Hero(s, "cleric").Pos, Is.EqualTo(new Coord(2, 2)));
                AssertInvariants(s);
            }
            finally { BattleConfig.DiagonalSlipEnabled = was; }
        }

        [Test]
        public void Diagonal_slip_is_still_refused_onto_an_occupied_tile_even_when_enabled()
        {
            bool was = BattleConfig.DiagonalSlipEnabled;
            BattleConfig.DiagonalSlipEnabled = true;
            try
            {
                var db = Mini();
                var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("cleric", 3, 3));
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 2)) });
                Assert.That(ev.Any(e => e is StepBlockedEvent b && b.Reason == "occupied"), Is.True);
                Assert.That(Hero(s, "cleric").Pos, Is.EqualTo(new Coord(3, 3)));
                AssertInvariants(s);
            }
            finally { BattleConfig.DiagonalSlipEnabled = was; }
        }

        [Test]
        public void Off_board_step_stops_the_drag()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 5, 0)), ("knight", 0, 7), ("cleric", 3, 3));
            var k = Hero(s, "knight");
            BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((0, 6), (-1, 6), (-1, 5)) });
            Assert.That(k.Pos, Is.EqualTo(new Coord(0, 6)));
        }

        [Test]
        public void Noop_drag_costs_no_time()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 5, 0)), ("knight", 0, 7), ("cleric", 3, 3));
            long clock = s.Clock;
            var k = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((0, 6), (0, 7)) });
            Assert.That(s.Clock, Is.EqualTo(clock));
            Assert.That(ev.Any(e => e is PlayerControlEvent), Is.False);
        }

        [Test]
        public void Two_by_two_boss_blocks_slides_and_is_pincered_across_its_width()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("boss", 1, 1)), ("knight", 0, 1), ("cleric", 4, 1));
            var boss = s.Units.Single(u => u.Def.Id == "boss");
            // slide into the boss is blocked
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((1, 1)) });
            Assert.That(ev.Any(e => e is StepBlockedEvent b && b.Reason == "immovable"), Is.True);
            Assert.That(boss.Pos, Is.EqualTo(new Coord(1, 1)));
            // cleric steps west to (3,1): knight(0,1) … boss covers (1,1),(2,1) … cleric(3,1) → one opponent
            ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 1)) });
            var p = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(p.Opponents, Is.EqualTo(new[] { boss.Id }));
            AssertInvariants(s);
        }

        [Test]
        public void Supporters_join_but_dilute_the_mana_charge()
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30; // a foe the pincer can dent but not kill
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4));
            // cleric (3,4) → (3,3): knight(1,3) tank(2,3) cleric(3,3); mage (1,4) adjacent to knight = supporter
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var p = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(p.Supporters, Does.Contain(Hero(s, "mage").Id));
            var charge = ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "pincer");
            Assert.That(charge.Participants, Is.EqualTo(3));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            float share = (tank.MaxHp - tank.Hp) / (float)tank.MaxHp;
            Assert.That(charge.Amount, Is.EqualTo(System.Math.Max(BattleConfig.MinPincerCharge, (int)System.Math.Round(BattleConfig.PincerChargePerHp * share / 1.5f)))); // ×1.5 divisor for 3 heroes
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "support"), Is.False);
            Assert.That(ev.Any(e => e is AttackEvent a && a.IsSupport), Is.True);
        }

        // ── Support actions along a chain of allies (§1.2.3, GG-T187) ──

        private static ContentDb SupportDb()
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30; // a foe the pincer can dent but not kill
            db.Classes["medic"] = new ClassDef { Id = "medic", Name = "Medic", Team = Team.Hero, Tags = { "Humanoid" }, BaseStats = new Stats { Strength = 10, Vitality = 10, Agility = 10, Speed = 10 },
                Support = new SupportDef { Effect = SupportEffect.Heal, Magnitude = 0.25f, Target = SupportTarget.Attacker, Range = 3 } };
            db.Classes["squire"] = new ClassDef { Id = "squire", Name = "Squire", Team = Team.Hero, Tags = { "Humanoid" }, BaseStats = new Stats { Strength = 10, Vitality = 10, Agility = 10, Speed = 10 } };
            return db;
        }

        [Test]
        public void Supporters_join_through_an_unbroken_chain_of_allies_within_their_range()
        {
            // knight(1,3) tank(2,3) cleric→(3,3). mage(1,4) touches the knight; medic(1,5) touches the mage (2 links, range 3);
            // squire(0,4) touches the mage too (2 links) but has the default range of 1.
            var db = SupportDb();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4), ("medic", 1, 5), ("squire", 0, 4));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var p = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(p.Supporters, Does.Contain(Hero(s, "mage").Id));
            Assert.That(p.Supporters, Does.Contain(Hero(s, "medic").Id), "two links down the chain, within the medic's range");
            Assert.That(p.Supporters, Does.Not.Contain(Hero(s, "squire").Id), "two links away but a striker only supports from beside the attacker");
            int medicAt = p.Supporters.IndexOf(Hero(s, "medic").Id);
            Assert.That(p.SupporterLinks[medicAt], Is.EqualTo(Hero(s, "mage").Id), "the medic links through the mage");
            var act = ev.OfType<SupportActionEvent>().Single(a => a.SupporterId == Hero(s, "medic").Id);
            Assert.That(act.Effect, Is.EqualTo(SupportEffect.Heal));
            Assert.That(act.AttackerId, Is.EqualTo(Hero(s, "knight").Id));
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == Hero(s, "medic").Id), Is.False, "a healer heals instead of striking");
            AssertInvariants(s);
        }

        [Test]
        public void A_gap_in_the_chain_breaks_support()
        {
            // medic(1,6) with an empty tile at (1,5) between it and the mage: no chain, no support.
            var db = SupportDb();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4), ("medic", 1, 6));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var p = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(p.Supporters, Does.Contain(Hero(s, "mage").Id));
            Assert.That(p.Supporters, Does.Not.Contain(Hero(s, "medic").Id));
            Assert.That(ev.OfType<SupportActionEvent>().Any(a => a.SupporterId == Hero(s, "medic").Id), Is.False);
        }

        [Test]
        public void A_healer_supporter_heals_the_attacker_it_is_chained_to()
        {
            var db = SupportDb();
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4), ("medic", 1, 5));
            var knight = Hero(s, "knight");
            knight.Hp = knight.MaxHp / 2;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var heal = ev.OfType<HealedEvent>().First(h => h.SourceId == Hero(s, "medic").Id);
            Assert.That(heal.TargetId, Is.EqualTo(knight.Id));
            Assert.That(heal.Amount, Is.EqualTo(System.Math.Min(knight.MaxHp - knight.MaxHp / 2, (int)System.Math.Round(knight.MaxHp * 0.25f))));
        }

        [Test]
        public void Every_hero_class_performs_its_own_support_action()
        {
            // Each real hero class's SupportDef, worn by a supporter beside the knight, does what it says.
            var real = Real;
            int checkedClasses = 0;
            foreach (var cls in real.Classes.Values.Where(c => c.Team == Team.Hero).OrderBy(c => c.Id))
            {
                Assert.That(cls.Support, Is.Not.Null, $"{cls.Id} has no support action");
                var def = cls.Support;
                var db = SupportDb();
                foreach (var b in real.Buffs) db.Buffs[b.Key] = b.Value;
                db.Classes["mage"].Support = def;
                if (def.Effect == SupportEffect.Cast) // a Cast supporter echoes a spell from its own class kit (GG-T298)
                {
                    foreach (var a in real.Abilities) db.Abilities[a.Key] = a.Value;
                    db.Classes["mage"].Abilities = new List<string>(cls.Abilities);
                }
                var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4));
                var knight = Hero(s, "knight"); var mage = Hero(s, "mage");
                knight.Hp = knight.MaxHp / 2;
                knight.Buffs.Add(new BuffInstance { Def = db.Buffs["wet"], Remaining = 5 });
                var hadTurn = s.Units.Where(u => s.TurnEntryOf(u.Id) != null || s.CastEntryOf(u.Id) != null).Select(u => u.Id).ToList();
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                var act = ev.OfType<SupportActionEvent>().SingleOrDefault(a => a.SupporterId == mage.Id);
                Assert.That(act, Is.Not.Null, $"{cls.Id}: no support action");
                Assert.That(act.Effect, Is.EqualTo(def.Effect), cls.Id);
                Assert.That(act.Targets, Is.Not.Empty, $"{cls.Id}: support lands on nobody");
                string why = $"{cls.Id} ({def.Effect} → {def.Target})";
                switch (def.Effect)
                {
                    case SupportEffect.Strike:
                        Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == mage.Id && a.IsSupport), Is.True, why); break;
                    case SupportEffect.Heal:
                        Assert.That(ev.Any(e => e is HealedEvent h && h.SourceId == mage.Id && act.Targets.Contains(h.TargetId)), Is.True, why); break;
                    case SupportEffect.Buff:
                        foreach (var t in act.Targets) Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.UnitId == t && b.BuffId == def.Buff), Is.True, why); break;
                    case SupportEffect.Haste:
                        if (act.Targets.Any(hadTurn.Contains)) Assert.That(ev.Any(e => e is HastenedEvent h && act.Targets.Contains(h.UnitId)), Is.True, why); break;
                    case SupportEffect.Mana:
                        Assert.That(ev.Any(e => e is ManaChargedEvent m && m.Reason == "support" && m.SourceId == mage.Id), Is.True, why); break;
                    case SupportEffect.Cleanse:
                        if (act.Targets.Contains(knight.Id)) Assert.That(ev.Any(e => e is BuffRemovedEvent r && r.UnitId == knight.Id && r.Reason == "cleansed"), Is.True, why); break;
                    case SupportEffect.Crit:
                        Assert.That(act.Targets, Is.EqualTo(new[] { knight.Id }), why);
                        Assert.That(ev.IndexOf(act), Is.LessThan(ev.FindIndex(e => e is AttackEvent)), $"{why}: the edge is lent before the blows"); break;
                    case SupportEffect.Cast:
                        Assert.That(ev.Any(e => e is DamagedEvent d && d.SourceId == mage.Id && act.Targets.Contains(d.TargetId)), Is.True, why); break;
                    case SupportEffect.Status:
                    case SupportEffect.Pushback:
                        Assert.That(act.Targets.All(t => s.UnitById(t).Team == Team.Enemy), Is.True, $"{why}: lands on the trapped foes"); break;
                }
                if (def.Effect != SupportEffect.Strike)
                    Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == mage.Id), Is.False, $"{why}: only strikers strike in support");
                checkedClasses++;
            }
            Assert.That(checkedClasses, Is.GreaterThan(0));
        }

        [Test]
        public void Chain_pincers_resolve_in_order()
        {
            var db = Mini();
            // knight(0,0) t(1,0) [cleric drops at (2,0)] t(3,0) mage(4,0): two pairs sharing cleric.
            var (s, _) = Battle(db, Stage(("tank", 1, 0), ("tank", 3, 0)), ("knight", 0, 0), ("cleric", 2, 1), ("mage", 4, 0));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 0)) });
            var ps = ev.OfType<PincerFormedEvent>().ToList();
            Assert.That(ps.Count, Is.EqualTo(2));
            Assert.That(ps[0].ChainIndex, Is.EqualTo(0));
            Assert.That(ps[1].ChainIndex, Is.EqualTo(1));
            var cleric = Hero(s, "cleric").Id;
            Assert.That(ps[0].Attacker1 == cleric || ps[0].Attacker2 == cleric, Is.True);
        }

        [Test]
        public void Killing_last_enemy_of_last_wave_is_victory_with_loot()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 1, 0)), ("knight", 0, 0), ("cleric", 2, 1));
            for (int i = 0; i < 6 && !s.IsOver; i++)
            {
                var c = Hero(s, "cleric");
                var path = c.Pos == new Coord(2, 0) ? P((2, 1)) : P((2, 0));
                BattleEngine.Apply(s, new MoveCommand { HeroId = c.Id, Path = path });
                AssertInvariants(s);
            }
            Assert.That(s.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(s.Results.Coins, Is.EqualTo(3));
            Assert.That(s.Results.Items["fang"], Is.EqualTo(1));
            Assert.That(s.Results.Kills["goblin"], Is.EqualTo(1));
            Assert.That(s.Results.Xp > 0, Is.True);
        }

        [Test]
        public void Waves_spawn_in_sequence()
        {
            var db = Mini();
            var stage = StageWaves(new[] { ("goblin", 1, 0) }, new[] { ("goblin", 4, 4) });
            var (s, _) = Battle(db, stage, ("knight", 0, 0), ("cleric", 2, 1));
            for (int i = 0; i < 6 && s.Wave == 0; i++)
            {
                var c = Hero(s, "cleric");
                BattleEngine.Apply(s, new MoveCommand { HeroId = c.Id, Path = c.Pos == new Coord(2, 0) ? P((2, 1)) : P((2, 0)) });
            }
            Assert.That(s.Wave, Is.EqualTo(1));
            Assert.That(s.IsOver, Is.False);
            Assert.That(At(s, 4, 4), Is.Not.Null);
        }

        // ── Delayed mid-wave reinforcements (§14.8) ─────────────────────────────

        [Test]
        public void A_delayed_spawn_arrives_partway_through_its_wave_not_at_wave_start()
        {
            var db = Mini();
            var stage = new StageDef { Id = "t", Name = "T", IsTest = true };
            var wave = new WaveDef();
            wave.Enemies.Add(new SpawnDef { Class = "goblin", Level = 1, X = 4, Y = 4 });
            wave.Enemies.Add(new SpawnDef { Class = "wolf", Level = 1, X = 5, Y = 5, DelayTicks = 15 });
            stage.Waves.Add(wave);
            var (s, _) = Battle(db, stage, ("knight", 0, 0), ("cleric", 2, 1));
            Assert.That(At(s, 5, 5), Is.Null);
            Assert.That(s.PendingSpawns, Has.Exactly(1).Items);
            var c = Hero(s, "cleric");
            Turn(s, new MoveCommand { HeroId = c.Id, Path = P((2, 0)) }); // GG-T308: the window runs on to the next enemy turn
            var wolf = At(s, 5, 5);
            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolf.Def.Id, Is.EqualTo("wolf"));
            Assert.That(s.PendingSpawns, Is.Empty);
        }

        [Test]
        public void A_waves_reinforcement_dialogue_fires_once_even_with_multiple_delayed_entries()
        {
            var db = Mini();
            var stage = new StageDef { Id = "t", Name = "T", IsTest = true };
            var wave = new WaveDef { SurpriseSpeaker = "goblin", SurpriseLine = "Reinforcements!" };
            wave.Enemies.Add(new SpawnDef { Class = "goblin", Level = 1, X = 4, Y = 4 });
            wave.Enemies.Add(new SpawnDef { Class = "wolf", Level = 1, X = 5, Y = 5, DelayTicks = 15 });
            wave.Enemies.Add(new SpawnDef { Class = "tank", Level = 1, X = 3, Y = 5, DelayTicks = 15 });
            stage.Waves.Add(wave);
            var (s, _) = Battle(db, stage, ("knight", 0, 0), ("cleric", 2, 1));
            var c = Hero(s, "cleric");
            var ev = Turn(s, new MoveCommand { HeroId = c.Id, Path = P((2, 0)) }); // GG-T308: the window runs on to the next enemy turn
            var arrivals = ev.OfType<ReinforcementsArrivedEvent>().ToList();
            Assert.That(arrivals, Has.Exactly(1).Items);
            Assert.That(arrivals[0].Spawned.Count, Is.EqualTo(2));
            Assert.That(arrivals[0].SpeakerClassId, Is.EqualTo("goblin"));
            Assert.That(arrivals[0].Line, Is.EqualTo("Reinforcements!"));
        }

        // ── Physical blocks (§1.1.1) ────────────────────────────────────────────

        [Test]
        public void A_hero_cannot_step_onto_a_block()
        {
            var db = Mini();
            var stage = Stage(("tank", 5, 0));
            stage.Blocks.Add(new Coord(1, 3));
            var (s, _) = Battle(db, stage, ("knight", 0, 3), ("cleric", 5, 7));
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 3)) });
            Assert.That(ev.Any(e => e is StepBlockedEvent b && b.Reason == "block"), Is.True);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(0, 3)));
        }

        [Test]
        public void A_block_in_the_line_breaks_the_pincer()
        {
            var db = Mini();
            var stage = Stage(("goblin", 3, 3));
            stage.Blocks.Add(new Coord(4, 3));
            var (s, _) = Battle(db, stage, ("cleric", 2, 3), ("knight", 5, 2));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((5, 3)) });
            Assert.That(ev.Any(e => e is PincerFormedEvent), Is.False);
        }

        [Test]
        public void Nobody_spawns_or_walks_onto_blocks()
        {
            var db = Mini();
            for (int seed = 0; seed < 40; seed++)
            {
                var stage = Stage(("wolf", -1, -1), ("wolf", -1, -1), ("wolf", -1, -1), ("wolf", -1, -1));
                for (int x = 0; x < 6; x++) if (x != 2) stage.Blocks.Add(new Coord(x, 2));
                var setup = new BattleSetup { Stage = stage, Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight" });
                setup.Party.Add(new HeroSpec { ClassId = "cleric" });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                for (int i = 0; i < 6 && !s.IsOver; i++)
                {
                    BattleEngine.Apply(s, new ShieldCommand());
                    foreach (var u in s.Units.Where(u => u.Alive)) Assert.That(s.IsBlock(u.Pos), Is.False, $"{u.Name} on a block at {u.Pos}");
                }
            }
        }

        [Test]
        public void Real_stages_carry_their_blocks()
        {
            Assert.That(Real.Stages["cave-03"].Blocks.Count >= 2, Is.True);
            foreach (var b in Real.Stages.Values.SelectMany(st => st.Blocks)) Assert.That(b.Y <= 5, Is.True, "blocks stay clear of the hero start rows");
        }
    }
}
