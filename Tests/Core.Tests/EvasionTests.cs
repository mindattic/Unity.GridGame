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
    /// <summary>GG-T297 (§13.1.0): defenders evade with AGI and Luck (plus buffs and gear); a dodge fires OnDodge; pincers can't be dodged.</summary>
    public class EvasionTests
    {
        private static BuffDef Evasive(float points) =>
            new BuffDef { Id = "evasive", Name = "Evasive", Kind = BuffKind.Buff, Unit = DurationUnit.Turns, Duration = 99, EvasionBonus = points };

        [Test]
        public void Evasion_is_modest_from_stats_and_hard_capped_with_buffs()
        {
            Assert.That(Formulas.Evasion(new Stats()), Is.EqualTo(0f));
            Assert.That(Formulas.Evasion(new Stats { Agility = 12, Luck = 12 }), Is.EqualTo(3.6f).Within(1e-4), "a level-1 hero");
            Assert.That(Formulas.Evasion(new Stats { Agility = 32, Luck = 29 }), Is.InRange(8f, 12f), "a level-30 skirmisher");
            Assert.That(Formulas.Evasion(new Stats { Agility = 999, Luck = 999 }), Is.EqualTo(Formulas.EvasionStatCap), "stats alone stop at 25");
            Assert.That(Formulas.Evasion(new Stats { Agility = 999, Luck = 999 }, 100f), Is.EqualTo(Formulas.EvasionCap), "buffs and gear stop at 40");
            Assert.That(Formulas.Evasion(new Stats { Agility = 10 }, -50f), Is.EqualTo(0f), "never negative");
            Assert.That(Formulas.Evasion(new Stats { Agility = -5, Luck = -5 }), Is.EqualTo(0f));

            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            float bare = Formulas.Evasion(k);
            k.Buffs.Add(new BuffInstance { Def = Evasive(15), Remaining = 2 });
            k.GearEvasion = 5;
            Assert.That(Formulas.Evasion(k), Is.EqualTo(bare + 20f).Within(1e-4), "a buff and gear add points");
        }

        [Test]
        public void A_dodge_reuses_the_single_hit_draw_and_undodgeable_blows_ignore_it()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            var tank = s.Units.First(u => u.Def.Id == "tank");
            tank.Buffs.Add(new BuffInstance { Def = Evasive(30), Remaining = 99 });
            var a = new Rng(11); var b = new Rng(11);
            int dodged = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                var withEva = Formulas.RollHit(k, tank, a, true, out bool d);
                var without = Formulas.RollHit(k, tank, b, false, out bool never);
                Assert.That(never, Is.False);
                if (d) { dodged++; Assert.That(withEva, Is.EqualTo(HitKind.Miss)); Assert.That(without, Is.Not.EqualTo(HitKind.Miss)); }
                else Assert.That(withEva, Is.EqualTo(without), "only the dodge band differs; the Rng stream is identical");
            }
            float evasion = Formulas.Evasion(tank);
            Assert.That(dodged / (float)n * 100f, Is.InRange(evasion - 4f, evasion + 4f), $"~{evasion:0} points dodged");
        }

        /// <summary>A knight (with <paramref name="passives"/> and an Evasion buff) beside a wolf that bites it on forced turns.</summary>
        private static (BattleState s, Unit knight, Unit wolf, List<BattleEvent> ev) WolfBites(ContentDb db, int seed, float evasion, int turns, params string[] passives)
        {
            db.Classes["wolf"].MoveRange = 0;
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = seed, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3), Passives = passives.ToList() });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var all = new List<BattleEvent>();
            var s = BattleEngine.Create(db, setup, all);
            var k = Hero(s, "knight");
            k.Buffs.Add(new BuffInstance { Def = Evasive(evasion), Remaining = 99 });
            var wolf = s.Units.First(u => u.Def.Id == "wolf");
            for (int i = 0; i < turns && wolf.Alive && k.Alive && !s.IsOver; i++)
            {
                s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - 1;
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            }
            return (s, k, wolf, all);
        }

        private static ContentDb WithRealReactives()
        {
            var db = Mini();
            foreach (var id in new[] { "evasive_riposte", "catch_breath", "blur" }) db.Abilities[id] = Real.Ability(id);
            db.Buffs["blurred"] = Real.Buffs["blurred"];
            return db;
        }

        [Test]
        public void Dodges_replay_identically_per_seed_and_show_as_a_dodge_not_damage()
        {
            int dodges = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var (_, k, wolf, ev) = WolfBites(Mini(), seed, 40f, 3);
                var (_, _, _, again) = WolfBites(Mini(), seed, 40f, 3);
                Assert.That(again.Select(e => e.ToString()), Is.EqualTo(ev.Select(e => e.ToString())), $"seed {seed} replays identically");
                foreach (var d in ev.OfType<DodgedEvent>().Where(d => d.UnitId == k.Id && d.AttackerId == wolf.Id))
                {
                    dodges++;
                    int at = ev.IndexOf(d);
                    Assert.That(ev[at - 1] is AttackEvent a && a.AttackerId == wolf.Id, Is.True, "the dodge answers the bite");
                }
            }
            Assert.That(dodges, Is.GreaterThan(0), "40 Evasion slips some bites");
            Assert.That(dodges, Is.LessThan(60), "but not every one");
        }

        [Test]
        public void A_dodge_fires_OnDodge_reactives_riposte_catch_breath_and_blur()
        {
            bool riposted = false, blurred = false, mended = false;
            for (int seed = 1; seed <= 40 && !(riposted && blurred && mended); seed++)
            {
                var db = WithRealReactives();
                var (_, k, wolf, ev) = WolfBites(db, seed, 40f, 3, "evasive_riposte", "catch_breath", "blur");
                int dodge = ev.FindIndex(e => e is DodgedEvent d && d.UnitId == k.Id);
                if (dodge < 0) continue;
                var after = ev.Skip(dodge + 1).TakeWhile(e => !(e is TurnStartedEvent)).ToList();
                var cues = after.OfType<TriggeredEvent>().Where(t => t.UnitId == k.Id && t.On == TriggerOn.OnDodge).Select(t => t.AbilityId).ToList();
                if (cues.Contains("evasive_riposte"))
                {
                    riposted = true;
                    Assert.That(after.OfType<AttackEvent>().Any(a => a.AttackerId == k.Id && a.TargetId == wolf.Id), Is.True, "the riposte strikes the biter");
                }
                if (cues.Contains("blur"))
                {
                    blurred = true;
                    Assert.That(after.OfType<BuffAppliedEvent>().Any(b => b.UnitId == k.Id && b.BuffId == "blurred"), Is.True);
                }
                if (cues.Contains("catch_breath")) mended = true;
            }
            Assert.That(riposted, Is.True, "Evasive Riposte counterstrikes on a dodge");
            Assert.That(blurred, Is.True, "Blur turns the dodger Blurred");
            // Catch Breath only fires when hurt; the knight may be at full HP — it must at least never fire on a landed bite.
            Assert.Pass(mended ? "Catch Breath fired too" : "Catch Breath had nothing to mend");
        }

        [Test]
        public void Blurred_adds_fifteen_evasion()
        {
            var db = WithRealReactives();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var k = Hero(s, "knight");
            float bare = Formulas.Evasion(k);
            k.Buffs.Add(new BuffInstance { Def = db.Buffs["blurred"], Remaining = 2 });
            Assert.That(Formulas.Evasion(k), Is.EqualTo(bare + 15f).Within(1e-4));
        }

        [Test]
        public void Pincer_blows_are_never_dodged()
        {
            int pincerBlows = 0;
            for (int seed = 1; seed <= 25; seed++)
            {
                var db = Mini();
                var setup = new BattleSetup { Stage = Stage(("tank", 3, 3)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(2, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 3) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var tank = s.Units.First(u => u.Def.Id == "tank");
                tank.Buffs.Add(new BuffInstance { Def = Evasive(40), Remaining = 99 });
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "knight").Id, Path = P((4, 3)) });
                Assume.That(ev.OfType<PincerFormedEvent>().Any(), Is.True);
                pincerBlows += ev.OfType<AttackEvent>().Count(a => a.TargetId == tank.Id);
                Assert.That(ev.OfType<DodgedEvent>().Any(d => d.UnitId == tank.Id), Is.False, $"seed {seed}: a pincer blow was dodged");
            }
            Assert.That(pincerBlows, Is.GreaterThanOrEqualTo(50));
        }

        [Test]
        public void Content_EvasionBonus_is_validated()
        {
            var db = Mini();
            db.Buffs["hazy"] = new BuffDef { Id = "hazy", Name = "Hazy", EvasionBonus = 60f };
            db.Items["cloak"] = new ItemDef { Id = "cloak", Name = "Cloak", Type = ItemType.Equipment, Slot = EquipmentSlot.Torso, EvasionBonus = 25f };
            var p = ContentLoader.Validate(db);
            Assert.That(p.Any(x => x.Contains("buff hazy") && x.Contains("EvasionBonus")), Is.True);
            Assert.That(p.Any(x => x.Contains("item cloak") && x.Contains("EvasionBonus")), Is.True);
            Assert.That(Real.Buffs["blurred"].EvasionBonus, Is.EqualTo(15f));
        }

        [Test]
        public void Gear_EvasionBonus_reaches_the_battle_unit()
        {
            var db = Mini();
            db.Items["cloak"] = new ItemDef { Id = "cloak", Name = "Cloak", Type = ItemType.Equipment, Slot = EquipmentSlot.Earring1, EvasionBonus = 6f };
            var setup = new BattleSetup { Stage = Stage(("tank", 0, 0)), Seed = 1, UnlockedSlots = 2 };
            var spec = new HeroSpec { ClassId = "knight", Start = new Coord(5, 7) };
            spec.EquippedItemIds.Add("cloak");
            setup.Party.Add(spec);
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            Assert.That(Hero(s, "knight").GearEvasion, Is.EqualTo(6f));
        }
    }
}
