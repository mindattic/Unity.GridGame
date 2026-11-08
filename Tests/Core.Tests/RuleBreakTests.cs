using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Rule-breaking finale bosses (§14.9, GG-T234): each breaks one standard rule, readably, with a counter.</summary>
    public class RuleBreakTests
    {
        private static StageDef Breaker(RuleBreak rule, params (string cls, int x, int y)[] enemies)
        {
            var st = Stage(enemies);
            st.Waves[0].Enemies[0].RuleBreak = rule;
            return st;
        }

        private static Unit Boss(BattleState s) => s.Units.First(u => u.RuleBreak != RuleBreak.None);

        /// <summary>Guard until <paramref name="u"/> has taken <paramref name="turns"/> own turns; returns every event of the
        /// command that held its last one.</summary>
        private static List<BattleEvent> UntilTurn(BattleState s, Unit u, int turns)
        {
            for (int i = 0; i < 60 && !s.IsOver; i++)
            {
                int before = u.TurnsTaken;
                var ev = BattleEngine.Apply(s, new ShieldCommand());
                if (before < turns && u.TurnsTaken >= turns) return ev;
            }
            Assert.Fail($"{u} never reached turn {turns}");
            return null;
        }

        [Test]
        public void A_rule_breaker_names_its_broken_rule_on_arrival()
        {
            var (s, ev) = Battle(Mini(), Breaker(RuleBreak.AxisWard, ("tank", 2, 2), ("goblin", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var arrivals = ev.OfType<RuleBrokenEvent>().Where(e => e.Arrived).ToList();
            Assert.That(arrivals, Has.Exactly(1).Items);
            Assert.That(arrivals[0].UnitId, Is.EqualTo(Boss(s).Id));
            Assert.That(arrivals[0].Text, Does.Contain("pincer rule"));
            Assert.That(s.Units.Single(u => u.Def.Id == "goblin").RuleBreak, Is.EqualTo(RuleBreak.None), "only the flagged spawn breaks the rules");
        }

        [Test]
        public void TwinTurn_takes_a_second_turn_when_no_hero_stands_beside_it()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.TwinTurn, ("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Boss(s);
            var ev = UntilTurn(s, wolf, 1);
            Assert.That(ev.OfType<TurnStartedEvent>().Count(t => t.UnitId == wolf.Id), Is.EqualTo(2));
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => !r.Arrived && r.Rule == RuleBreak.TwinTurn), Is.True);
            Assert.That(wolf.Pos.Manhattan(new Coord(0, 0)), Is.EqualTo(2), "two marches in one arrival");
        }

        [Test]
        public void TwinTurn_is_held_to_one_turn_by_a_hero_beside_it()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.TwinTurn, ("goblin", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var gob = Boss(s);
            var ev = UntilTurn(s, gob, 1);
            Assert.That(ev.OfType<TurnStartedEvent>().Count(t => t.UnitId == gob.Id), Is.EqualTo(1));
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => !r.Arrived), Is.False);
        }

        [Test]
        public void AxisWard_turns_a_row_pincer_aside_and_cracks()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.AxisWard, ("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 5));
            var tank = Boss(s);
            int hp = tank.Hp;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(ev.OfType<PincerFormedEvent>(), Is.Not.Empty);
            Assert.That(tank.Hp, Is.EqualTo(hp), "a side-to-side pincer glances off");
            Assert.That(tank.RuleBreakCount, Is.EqualTo(1));
            Assert.That(ev.OfType<DamagedEvent>().Any(d => d.TargetId == tank.Id && d.Amount == 0 && d.Note == "Warded!"), Is.True);
        }

        [Test]
        public void AxisWard_lets_a_column_pincer_land()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.AxisWard, ("tank", 2, 3)), ("knight", 2, 2), ("cleric", 4, 4));
            var tank = Boss(s);
            int hp = tank.Hp;
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (2, 4)) });
            Assert.That(tank.Hp, Is.LessThan(hp));
            Assert.That(tank.RuleBreakCount, Is.EqualTo(0));
        }

        [Test]
        public void AxisWard_shatters_on_its_last_crack_and_row_pincers_land_after()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.AxisWard, ("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 5));
            var tank = Boss(s);
            tank.RuleBreakCount = RuleBreakRules.AxisWardCracks - 1;
            int hp = tank.Hp;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(tank.Hp, Is.EqualTo(hp));
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => r.Text.Contains("shatters")), Is.True);

            var (s2, _) = Battle(Mini(), Breaker(RuleBreak.AxisWard, ("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 5));
            var broken = Boss(s2);
            broken.RuleBreakCount = RuleBreakRules.AxisWardCracks;
            int hp2 = broken.Hp;
            BattleEngine.Apply(s2, new MoveCommand { HeroId = Hero(s2, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(broken.Hp, Is.LessThan(hp2));
        }

        [Test]
        public void MireSwap_trades_places_with_an_ally_when_a_hero_corners_it()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.MireSwap, ("goblin", 2, 2), ("tank", 0, 0)), ("knight", 2, 3), ("cleric", 5, 7));
            var boss = Boss(s);
            var ally = s.Units.Single(u => u.Def.Id == "tank");
            var ev = UntilTurn(s, boss, 1);
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => r.Rule == RuleBreak.MireSwap && !r.Arrived), Is.True);
            Assert.That(boss.Pos, Is.EqualTo(new Coord(0, 0)));
            Assert.That(ally.Pos, Is.EqualTo(new Coord(2, 2)));
            AssertInvariants(s);
        }

        [Test]
        public void MireSwap_has_nowhere_to_go_once_its_allies_are_gone()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.MireSwap, ("goblin", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var boss = Boss(s);
            var ev = UntilTurn(s, boss, 1);
            Assert.That(boss.Pos, Is.EqualTo(new Coord(2, 2)));
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => !r.Arrived), Is.False);
        }

        [Test]
        public void GravityStamp_warns_then_drags_heroes_in_reach_toward_it()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.GravityStamp, ("goblin", 2, 2)), ("knight", 2, 5), ("cleric", 5, 7));
            var boss = Boss(s);
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            var first = UntilTurn(s, boss, 1);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(2, 5)), "no stamp on the first turn");
            Assert.That(first.OfType<AnnounceEvent>().Any(a => a.Text.Contains("stamps next turn")), Is.True);
            var second = UntilTurn(s, boss, 2);
            Assert.That(second.OfType<RuleBrokenEvent>().Any(r => r.Rule == RuleBreak.GravityStamp), Is.True);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(2, 3)), "pulled two tiles, stopping beside it");
            Assert.That(cleric.Pos, Is.EqualTo(new Coord(5, 7)), "out of reach");
            AssertInvariants(s);
        }

        [Test]
        public void Undying_rises_once_while_a_thrall_stands()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.Undying, ("tank", 2, 3), ("goblin", 0, 0)), ("knight", 1, 3), ("cleric", 3, 5));
            var lord = Boss(s);
            lord.Hp = 1;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(lord.Alive, Is.True);
            Assert.That(lord.Hp, Is.GreaterThanOrEqualTo(1));
            Assert.That(lord.RuleBreakCount, Is.EqualTo(1));
            Assert.That(ev.OfType<RuleBrokenEvent>().Any(r => r.Rule == RuleBreak.Undying && !r.Arrived), Is.True);
            Assert.That(ev.OfType<DiedEvent>().Any(d => d.UnitId == lord.Id), Is.False);
        }

        [Test]
        public void Undying_falls_for_good_with_no_thralls_left_or_once_it_has_risen()
        {
            var (s, _) = Battle(Mini(), Breaker(RuleBreak.Undying, ("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 5));
            var lord = Boss(s);
            lord.Hp = 1;
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(lord.Alive, Is.False, "no thralls: it stays down");

            var (s2, _) = Battle(Mini(), Breaker(RuleBreak.Undying, ("tank", 2, 3), ("goblin", 0, 0)), ("knight", 1, 3), ("cleric", 3, 5));
            var risen = Boss(s2);
            risen.RuleBreakCount = 1;
            risen.Hp = 1;
            BattleEngine.Apply(s2, new MoveCommand { HeroId = Hero(s2, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(risen.Alive, Is.False, "it only rises once");
        }

        [Test]
        public void Every_main_biome_finale_has_its_own_rule_breaking_boss()
        {
            var expected = new Dictionary<string, (string boss, RuleBreak rule)>
            {
                ["greenvalley-03"] = ("captain", RuleBreak.TwinTurn),
                ["lanternroad-03"] = ("oni02", RuleBreak.Undying), // GG-T345: the mask that gets back up
                ["desert-03"] = ("mechaarmor01", RuleBreak.AxisWard),
                ["swamp-03"] = ("swampmistress00", RuleBreak.MireSwap),
                ["cave-03"] = ("cyclops02", RuleBreak.GravityStamp),
                ["still-03"] = ("chronomancer", RuleBreak.TwinTurn), // GG-T344: the keeper of the Still's clocks
                ["cityruins-03"] = ("ceramicknight06", RuleBreak.AxisWard), // GG-T350: a Kiln Guard captain holds the spire stair
                ["cathedral-03"] = ("hierarch", RuleBreak.Undying), // GG-T350: the Hierarch, at the top of the spire
            };
            foreach (var kv in expected)
            {
                var finale = Real.Stages[kv.Key].Waves.Last();
                var breakers = finale.Enemies.Where(e => e.RuleBreak != RuleBreak.None).ToList();
                Assert.That(breakers, Has.Exactly(1).Items, kv.Key);
                Assert.That(breakers[0].Class, Is.EqualTo(kv.Value.boss), kv.Key);
                Assert.That(breakers[0].RuleBreak, Is.EqualTo(kv.Value.rule), kv.Key);
            }
            var others = Real.Stages.Values.Where(st => !expected.ContainsKey(st.Id))
                .SelectMany(st => st.Waves).SelectMany(w => w.Enemies).Where(e => e.RuleBreak != RuleBreak.None);
            Assert.That(others, Is.Empty, "rule-breakers are the main finales only");
            Assert.That(ContentLoader.Validate(Real).Where(p => p.Contains("RuleBreak") || p.Contains("MireSwap")), Is.Empty);
        }

        [Test]
        public void A_rule_break_on_a_guest_or_a_big_body_swap_fails_validation()
        {
            var db = Mini();
            var st = Stage(("boss", 2, 2));
            st.Waves[0].Enemies[0].RuleBreak = RuleBreak.MireSwap;
            st.Waves[0].Guests.Add(new SpawnDef { Class = "mage", RuleBreak = RuleBreak.TwinTurn });
            var problems = new List<string>();
            RuleBreaks.Validate(db, st, st.Waves[0], problems);
            Assert.That(problems, Has.Exactly(2).Items);
        }
    }
}
