using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-LAW-13 / Unity.Multiplayer multiplayer-ready rule 8 (GG-T192): a LocalSession with two seats, each commanding
    /// its own hero through the one command pipeline; a wrong-seat command is refused; commands round-trip through
    /// JSON; and seed + setup + command log replays the battle exactly.
    /// </summary>
    public class MultiplayerReadyTests
    {
        private static BattleSetup TwoHeroSetup()
        {
            var setup = new BattleSetup { Stage = Stage(("tank", 5, 7)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 0) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(0, 4) });
            return setup;
        }

        [Test]
        public void Two_players_command_their_own_heroes_and_a_wrong_player_command_is_refused()
        {
            var db = Mini();
            var session = LocalSession.Create(db, TwoHeroSetup(), playerCount: 2);
            var ann = session.Players[0];
            var bob = session.Players[1];
            var knight = Hero(session.State, "knight");
            var cleric = Hero(session.State, "cleric");
            Assert.That(knight.Owner, Is.EqualTo(ann));
            Assert.That(cleric.Owner, Is.EqualTo(bob));

            var raised = new List<BattleEvent>();
            session.EventRaised += raised.Add;

            // Submitted commands wait for the tick: nothing changes until the session steps.
            session.Submit(new MoveCommand { Player = ann, HeroId = knight.Id, Path = P((1, 0)) });
            session.Submit(new MoveCommand { Player = bob, HeroId = cleric.Id, Path = P((1, 4)) });
            Assert.That(knight.Pos, Is.EqualTo(new Coord(0, 0)));
            session.Step(2);

            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 0)), "ann's command moved ann's hero");
            Assert.That(cleric.Pos, Is.EqualTo(new Coord(1, 4)), "bob's command moved bob's hero");
            Assert.That(session.State.Results.ActionsTaken, Is.EqualTo(2));
            Assert.That(raised.OfType<SteppedEvent>().Any(), Is.True, "presentation hears the authority's events");

            // Bob tries to move ann's knight: the authority refuses it and nothing of ann's changes.
            raised.Clear();
            session.Submit(new MoveCommand { Player = bob, HeroId = knight.Id, Path = P((2, 0)) });
            session.Step();
            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 0)));
            Assert.That(session.State.Results.ActionsTaken, Is.EqualTo(2));
            Assert.That(raised.OfType<AbilityRefusedEvent>().Any(r => r.CasterId == knight.Id), Is.True);

            // A seat nobody holds cannot even enter the pipeline.
            session.Submit(new ShieldCommand { Player = new PlayerId(5) });
            session.Step();
            Assert.That(session.State.Results.ActionsTaken, Is.EqualTo(2));

            // Party-wide Guard is open to either seat.
            session.Submit(new ShieldCommand { Player = bob });
            session.Step();
            Assert.That(session.State.Results.ActionsTaken, Is.EqualTo(3));
        }

        [Test]
        public void Commands_round_trip_through_json_and_the_log_replays_the_battle_exactly()
        {
            var db = Mini();
            var session = LocalSession.Create(db, TwoHeroSetup(), playerCount: 2);
            var ann = session.Players[0];
            var bob = session.Players[1];
            int knight = Hero(session.State, "knight").Id, cleric = Hero(session.State, "cleric").Id;

            var commands = new BattleCommand[]
            {
                new MoveCommand { Player = ann, HeroId = knight, Path = P((1, 0), (2, 0)), TimeLeftSeconds = 1.5f },
                new MoveCommand { Player = bob, HeroId = cleric, Path = P((1, 4)) },
                new UseSlotCommand { Player = bob, HeroId = cleric, Slot = 0, Target = new Coord(1, 4) },
                new MoveCommand { Player = ann, HeroId = cleric, Path = P((2, 4)) }, // refused: not ann's hero
                new ShieldCommand { Player = ann },
            };
            foreach (var c in commands)
            {
                var json = CommandCodec.ToJson(c);
                var back = CommandCodec.FromJson(json);
                Assert.That(back.GetType(), Is.EqualTo(c.GetType()));
                Assert.That(back.Player, Is.EqualTo(c.Player));
                Assert.That(CommandCodec.ToJson(back), Is.EqualTo(json), "command JSON round-trips equal");
                session.Submit(c);
                session.Step();
            }

            var log = session.CommandLog.Select(CommandCodec.ToJson).ToList();
            Assert.That(log, Has.Count.EqualTo(commands.Length));
            var replay = LocalSession.Replay(db, TwoHeroSetup(), 2, log);
            Assert.That(BattleRecorder.Snapshot(replay.State).ToString(), Is.EqualTo(BattleRecorder.Snapshot(session.State).ToString()),
                "same seed + setup + commands → same state (stable ids, seeded rules)");
        }

        [Test]
        public void Single_seat_session_matches_the_engine_exactly()
        {
            // The shipped game runs as one seat (seat 0): the session must change nothing about the rules.
            var db = Mini();
            var session = LocalSession.Create(db, TwoHeroSetup());
            var direct = BattleEngine.Create(db, TwoHeroSetup(), new List<BattleEvent>());
            var knight = Hero(session.State, "knight").Id;
            var a = session.SubmitAndStep(new MoveCommand { HeroId = knight, Path = P((1, 0)) });
            var b = BattleEngine.Apply(direct, new MoveCommand { HeroId = knight, Path = P((1, 0)) });
            Assert.That(a.Select(e => e.ToString()), Is.EqualTo(b.Select(e => e.ToString())));
            Assert.That(BattleRecorder.Snapshot(session.State).ToString(), Is.EqualTo(BattleRecorder.Snapshot(direct).ToString()));
        }
    }
}
