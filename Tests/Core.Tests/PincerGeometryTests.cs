using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T98: the pincer outline was drawn from end-of-command State, so a member that moved later in the same
    /// command (here: the trapped foe's own turn darting it away) stretched the outline into a multi-tile box.
    /// PincerFormedEvent now carries the formation-time footprints, and PincerGeometry guards the shape.
    /// </summary>
    public class PincerGeometryTests
    {
        private static UnitFootprint F(int x, int y, int w = 1, int h = 1) => new UnitFootprint { At = new Coord(x, y), W = w, H = h };

        [Test]
        public void Pincer_reports_formation_time_tiles_when_the_foe_darts_away_in_the_same_command()
        {
            var db = Mini();
            // A sturdy skirmisher: survives the pincer, then on its own turn (same command) strikes and darts a tile away.
            db.Classes["darter"] = new ClassDef
            {
                Id = "darter", Name = "Darter", Team = Team.Enemy, Tags = { "Beast" }, Brain = Brain.Skirmisher,
                BaseStats = new Stats { Strength = 1, Vitality = 200, Agility = 10, Speed = 10 }, MoveRange = 1,
            };
            var (s, _) = Battle(db, Stage(("darter", 1, 0)), ("knight", 0, 1), ("cleric", 2, 0));
            var darter = s.Units.Single(u => u.Def.Id == "darter");
            ActsAfterAction(s, darter); // its turn arrives inside this very command (GG-T311: as close as a drag fits)
            var knight = Hero(s, "knight");

            var ev = Turn(s, new MoveCommand { HeroId = knight.Id, Path = P((0, 0)) });

            var p = ev.OfType<PincerFormedEvent>().Single();
            int at = ev.IndexOf(p);
            Assert.That(darter.Alive, Is.True, "the foe must survive the pincer to act after it");
            Assert.That(ev.Skip(at).Any(e => e is SteppedEvent st && st.UnitId == darter.Id), Is.True, "the foe moved after the pincer in the same command");
            Assert.That(darter.Pos, Is.Not.EqualTo(new Coord(1, 0)), "end-of-command State has the foe elsewhere");

            // The event carries where everyone stood when the pincer formed — not where State has them now.
            Assert.That(p.OpponentsAt.Select(o => o.UnitId), Is.EqualTo(p.Opponents));
            Assert.That(p.OpponentsAt.Single().At, Is.EqualTo(new Coord(1, 0)));
            var atk = new[] { p.Attacker1At, p.Attacker2At }.ToDictionary(f => f.UnitId, f => f.At);
            Assert.That(atk[knight.Id], Is.EqualTo(new Coord(0, 0)));
            Assert.That(atk[Hero(s, "cleric").Id], Is.EqualTo(new Coord(2, 0)));
            Assert.That(p.GeometryError(), Is.Null);

            // What the old playback drew (positions read from final State) is exactly the malformed box the owner saw.
            var stale = PincerGeometry.Error(UnitFootprint.Of(s.UnitById(p.Attacker1)), UnitFootprint.Of(s.UnitById(p.Attacker2)), new[] { UnitFootprint.Of(darter) });
            Assert.That(stale, Is.Not.Null);
            AssertEventInvariants(ev);
        }

        [Test]
        public void Chain_links_each_carry_their_own_formation_tiles()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 1, 0), ("tank", 3, 0)), ("knight", 0, 0), ("cleric", 2, 1), ("mage", 4, 0));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 0)) });
            var ps = ev.OfType<PincerFormedEvent>().ToList();
            Assert.That(ps.Count, Is.EqualTo(2));
            foreach (var p in ps)
            {
                Assert.That(p.GeometryError(), Is.Null, p.ToString());
                Assert.That(new[] { p.Attacker1At.At, p.Attacker2At.At }.Concat(p.OpponentsAt.Select(o => o.At)).All(c => c.Y == 0), Is.True);
            }
            AssertEventInvariants(ev);
        }

        [Test]
        public void Two_by_two_foe_may_widen_the_outline_to_its_own_size()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("boss", 1, 1)), ("knight", 0, 1), ("cleric", 4, 1));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 1)) });
            var p = ev.OfType<PincerFormedEvent>().Single();
            Assert.That(p.OpponentsAt.Single().W, Is.EqualTo(2));
            Assert.That(p.OpponentsAt.Single().H, Is.EqualTo(2));
            Assert.That(p.GeometryError(), Is.Null);
        }

        [Test]
        public void Guard_rejects_every_shape_a_pincer_cannot_take()
        {
            Assert.That(PincerGeometry.Error(F(0, 0), F(2, 0), new[] { F(1, 0) }), Is.Null);
            Assert.That(PincerGeometry.Error(F(3, 2), F(3, 6), new[] { F(3, 3), F(3, 4), F(3, 5) }), Is.Null);
            // The owner's screenshot: one member reported off the line turns the outline into a ~3×3 box.
            Assert.That(PincerGeometry.Error(F(0, 0), F(2, 0), new[] { F(1, 2) }), Is.Not.Null);
            Assert.That(PincerGeometry.Error(F(0, 0), F(2, 2), new[] { F(1, 1) }), Is.Not.Null, "not one row or column");
            Assert.That(PincerGeometry.Error(F(0, 0), F(3, 0), new[] { F(4, 0) }), Is.Not.Null, "opponent outside the attackers");
            Assert.That(PincerGeometry.Error(F(0, 0), F(2, 0), new UnitFootprint[0]), Is.Not.Null);
            // 2×2 foe: two tiles thick is its own width and allowed; a 1×1 foe can't make it thicker than one.
            Assert.That(PincerGeometry.Error(F(0, 1), F(3, 1), new[] { F(1, 1, 2, 2) }), Is.Null);
            Assert.That(PincerGeometry.Error(F(0, 1), F(3, 1), new[] { F(1, 0, 2, 2) }), Is.Null);
            Assert.That(PincerGeometry.Error(F(0, 1), F(3, 1), new[] { F(1, 2, 2, 2) }), Is.Not.Null, "2×2 that misses the line");
        }
    }
}
