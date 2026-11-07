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
    /// <summary>
    /// GG-T306 (§14.1.3): units with the Pincer keyword (Sentient foes, tamed monsters, heroes) engineer pincers through the
    /// shared <see cref="PincerPlanner"/>: they swap places with allies and heroes along their walk to complete a pincer, set
    /// up an end an ally can close, or slot a supporter into the chain. Mindless foes keep their simple step.
    /// </summary>
    public class PincerPlannerTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            var st = new Stats { Strength = 5, Vitality = 6, Agility = 10, Speed = 10, Intelligence = 10 };
            db.Classes["soldier"] = new ClassDef { Id = "soldier", Name = "Soldier", Team = Team.Enemy, Tags = { "Humanoid", "Sentient" }, BaseStats = st, MoveRange = 1 };
            db.Classes["yeti"] = new ClassDef { Id = "yeti", Name = "Yeti", Team = Team.Enemy, Tags = { "Beast" }, BaseStats = st, MoveRange = 1 };
            return db;
        }

        /// <summary>Every turn icon at the start of the bar except <paramref name="first"/>, which acts on the next tick.</summary>
        private static void ActsFirst(BattleState s, Unit first)
        {
            foreach (var e in s.Timeline.Where(e => e.Kind == TimelineKind.Turn)) e.Ip = 0;
            s.TurnEntryOf(first.Id).Ip = BattleConfig.BarLength - 1;
        }

        /// <summary>Knight (2,3), cleric far off; a Sentient soldier at (1,3) west of the knight, a wolf at (3,3) east of it,
        /// and <paramref name="mover"/> at (3,2) just above the wolf.</summary>
        private static BattleState SwapBoard(string mover, int seed = 7)
        {
            var setup = new BattleSetup { Stage = Stage(("soldier", 1, 3), ("wolf", 3, 3), (mover, 3, 2)), Seed = seed, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(Db(), setup, new List<BattleEvent>());
            ActsFirst(s, At(s, 3, 2));
            return s;
        }

        [Test]
        public void A_sentient_enemy_swaps_with_an_ally_to_complete_a_pincer()
        {
            var s = SwapBoard("soldier");
            var mover = At(s, 3, 2); var wolf = At(s, 3, 3); var knight = Hero(s, "knight");
            var plan = PincerPlanner.Plan(s, mover, 1);
            Assert.That(plan.Kind, Is.EqualTo(PincerPlanKind.Complete), plan.ToString());
            Assert.That(plan.Path, Is.EqualTo(P((3, 3))));
            Assert.That(plan.Swapped, Is.EqualTo(new[] { wolf.Id }));
            Assert.That(mover.Pos, Is.EqualTo(new Coord(3, 2)), "the planner only looks: every unit is back where it was");
            Assert.That(wolf.Pos, Is.EqualTo(new Coord(3, 3)));

            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<DisplacedEvent>().Any(d => d.UnitId == wolf.Id && d.ByUnitId == mover.Id && d.To == new Coord(3, 2)), Is.True, "the wolf swapped into the tile it left");
            var pincer = ev.OfType<PincerFormedEvent>().FirstOrDefault(p => p.Team == Team.Enemy);
            Assert.That(pincer, Is.Not.Null, "it closed the pincer on its own turn");
            Assert.That(new[] { pincer.Attacker1, pincer.Attacker2 }, Does.Contain(mover.Id));
            Assert.That(pincer.Opponents, Does.Contain(knight.Id));
            Assert.That(pincer.Supporters, Does.Contain(wolf.Id), "the swapped wolf stands behind it as a supporter");
            AssertInvariants(s);
        }

        [TestCase("yeti")]
        [TestCase("wolf")]
        public void A_mindless_enemy_never_swaps_to_pincer(string cls)
        {
            var s = SwapBoard(cls);
            var mover = At(s, 3, 2); var wolf = At(s, 3, 3);
            Assert.That(mover.CanPincer, Is.False);
            Assert.That(EnemyPlanner.PlanPincer(s, mover), Is.Null);
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<DisplacedEvent>().Any(d => d.ByUnitId == mover.Id), Is.False, "no swap");
            Assert.That(ev.OfType<PincerFormedEvent>().Any(p => p.Team == Team.Enemy), Is.False);
            Assert.That(wolf.Pos, Is.EqualTo(new Coord(3, 3)));
        }

        [Test]
        public void A_sentient_enemy_swaps_to_set_up_an_end_an_ally_can_close()
        {
            // Knight (2,3); a wolf beside it at (1,3); the planner's soldier at (1,2); a second soldier at (4,3), one step
            // from the open far end (3,3).
            var setup = new BattleSetup { Stage = Stage(("soldier", 1, 2), ("wolf", 1, 3), ("soldier", 4, 3)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(Db(), setup, new List<BattleEvent>());
            var mover = At(s, 1, 2); var wolf = At(s, 1, 3);
            ActsFirst(s, mover);

            var plan = PincerPlanner.Plan(s, mover, 1);
            Assert.That(plan.Kind, Is.EqualTo(PincerPlanKind.Setup), plan.ToString());
            Assert.That(plan.End, Is.EqualTo(new Coord(1, 3)));

            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(mover.Pos, Is.EqualTo(new Coord(1, 3)), "it took the end of the knight's row");
            Assert.That(wolf.Pos, Is.EqualTo(new Coord(1, 2)), "by swapping the wolf out of it");
            Assert.That(ev.OfType<DisplacedEvent>().Any(d => d.UnitId == wolf.Id && d.ByUnitId == mover.Id), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void A_tamed_monster_moves_to_close_a_hero_pincer()
        {
            // Knight (1,3) | tank foe (2,3) | open (3,3); the tamed wolf at (3,2) already stands beside a second tank at (3,1),
            // so the old charge AI would just bite that one. The planner steps down and closes knight–tank–wolf.
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3), ("wolf", 3, 2), ("tank", 3, 1)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(Db(), setup, new List<BattleEvent>());
            var wolf = At(s, 3, 2); var knight = Hero(s, "knight");
            wolf.Team = Team.Hero; wolf.Tamed = true; wolf.TameTurnsLeft = 3; wolf.BondNeeded = 3;
            ActsFirst(s, wolf);

            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<SteppedEvent>().Any(e => e.UnitId == wolf.Id && e.To == new Coord(3, 3)), Is.True, "it stepped to the open end");
            var pincer = ev.OfType<PincerFormedEvent>().FirstOrDefault(p => p.Team == Team.Hero);
            Assert.That(pincer, Is.Not.Null, "it closed the hero pincer");
            Assert.That(new[] { pincer.Attacker1, pincer.Attacker2 }, Is.EquivalentTo(new[] { knight.Id, wolf.Id }));
        }

        [Test]
        public void The_hero_planner_finds_the_walk_that_closes_a_pincer()
        {
            // The same planner serves the smart bot: knight (2,3) beside a soldier at (3,3); the cleric at (5,2) is two steps
            // from the open far end (4,3).
            var setup = new BattleSetup { Stage = Stage(("soldier", 3, 3)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 2) });
            var s = BattleEngine.Create(Db(), setup, new List<BattleEvent>());
            var cleric = Hero(s, "cleric");
            var plan = PincerPlanner.Plan(s, cleric, 3);
            Assert.That(plan.Kind, Is.EqualTo(PincerPlanKind.Complete), plan.ToString());
            Assert.That(plan.End, Is.EqualTo(new Coord(4, 3)));
            Assert.That(cleric.Pos, Is.EqualTo(new Coord(5, 2)));
        }

        [Test]
        public void Planning_and_the_turns_it_drives_replay_identically()
        {
            string Run(int seed)
            {
                var s = SwapBoard("soldier", seed);
                var log = new List<string> { PincerPlanner.Plan(s, At(s, 3, 2), 1).ToString() };
                for (int i = 0; i < 6 && !s.IsOver; i++)
                    log.AddRange(BattleEngine.Apply(s, new ShieldCommand()).Select(e => e.ToString()));
                return string.Join("\n", log);
            }
            Assert.That(Run(11), Is.EqualTo(Run(11)));
        }

        [Test]
        public void Every_real_enemy_class_brings_its_own_support()
        {
            foreach (var c in Real.Classes.Values.Where(c => c.Team == Team.Enemy))
                Assert.That(c.Support, Is.Not.Null, c.Id);
            var db = Db();
            db.Classes["soldier"].Support = null;
            Assert.That(ContentLoader.Validate(db).Any(p => p.Contains("class soldier: an enemy needs its own Support")), Is.True);
        }
    }
}
