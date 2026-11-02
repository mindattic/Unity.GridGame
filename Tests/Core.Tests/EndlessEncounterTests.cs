using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using Newtonsoft.Json;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T64: `-demo -endless -seed S -count 1` reproduces one flagged encounter, and Core can replay it headless.</summary>
    public class EndlessEncounterTests
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

        private static string Fingerprint(BattleSetup s) =>
            JsonConvert.SerializeObject(new { s.Stage, s.Party, s.Seed, s.UnlockedSlots, s.RecommendedLevel }, Json);

        [TestCase(1)]
        [TestCase(2026)]
        [TestCase(-41)]
        public void Two_generations_from_one_seed_are_identical(int seed)
        {
            var db = Real;
            try
            {
                var a = EndlessEncounter.Build(db, seed);
                // An unrelated encounter in between must not leak into the next build (its stage is pruned first).
                EndlessEncounter.Build(db, seed + 1000);
                var b = EndlessEncounter.Build(db, seed);

                Assert.That(b.Stage.Id, Is.EqualTo(a.Stage.Id));
                Assert.That(b.Party.Select(h => h.ClassId), Is.EqualTo(a.Party.Select(h => h.ClassId)));
                Assert.That(Fingerprint(b), Is.EqualTo(Fingerprint(a)), "same seed built a different location/waves/levels/party/gear");
                Assert.That(db.Stages.Keys.Count(k => k.StartsWith(EncounterGenerator.IdPrefix)), Is.EqualTo(1), "endless stages piled up");
            }
            finally { EndlessEncounter.Prune(db); }
        }

        [TestCase(7)]
        [TestCase(31337)]
        public void Headless_replay_of_a_seed_gives_the_same_outcome_and_turn_count(int seed)
        {
            var db = Real;
            var first = EndlessEncounter.Replay(db, seed);
            var second = EndlessEncounter.Replay(db, seed);

            Assert.That(first.Outcome, Is.Not.EqualTo(BattlePhase.PlayerInput), $"seed {seed} did not resolve within {EndlessEncounter.DefaultMaxActions} actions");
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(second.Turns, Is.EqualTo(first.Turns));
            Assert.That(second.Wave, Is.EqualTo(first.Wave));
            Assert.That(second.Clock, Is.EqualTo(first.Clock));
            Assert.That(db.Stages.Keys.Any(k => k.StartsWith(EncounterGenerator.IdPrefix)), Is.False, "replay left its stage registered");
        }
    }
}
