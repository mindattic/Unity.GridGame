using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T60: the endless spectrum coverage scheduler.</summary>
    public class EncounterScheduleTests
    {
        [Test]
        public void Same_base_seed_gives_the_same_schedule()
        {
            var a = new EncounterSchedule(Real, 4242);
            var b = new EncounterSchedule(Real, 4242);
            for (int i = 0; i < 40; i++)
            {
                var x = a.Next();
                var y = b.Next();
                Assert.That(y.Seed, Is.EqualTo(x.Seed));
                Assert.That(y.StageId, Is.EqualTo(x.StageId));
                Assert.That(y.Level, Is.EqualTo(x.Level));
                Assert.That(y.Families, Is.EqualTo(x.Families));
                Assert.That(y.HeroClasses, Is.EqualTo(x.HeroClasses));
            }
            Assert.That(b.CoverageTable(), Is.EqualTo(a.CoverageTable()));
        }

        [Test]
        public void A_few_hundred_encounters_cover_every_value_evenly()
        {
            var schedule = new EncounterSchedule(Real, 7);
            for (int i = 0; i < 300; i++) schedule.Next();
            var cov = schedule.Coverage;

            void Even(string name, System.Collections.Generic.Dictionary<string, int> counts, int slack)
            {
                Assert.That(counts.Values.Min(), Is.GreaterThan(0), $"{name}: a value was never drawn\n{schedule.CoverageTable()}");
                Assert.That(counts.Values.Max() - counts.Values.Min(), Is.LessThanOrEqualTo(slack), $"{name} uneven\n{schedule.CoverageTable()}");
            }
            Even("biome", cov.Biomes, 1);
            Even("band", cov.Bands, 1);
            Even("family", cov.Families, 2);
            Even("hero class", cov.HeroClasses, 2);
            Assert.That(cov.Encounters, Is.EqualTo(300));
        }

        [Test]
        public void Plans_have_distinct_families_and_heroes_and_levels_inside_their_band()
        {
            var schedule = new EncounterSchedule(Real, 99);
            for (int i = 0; i < 60; i++)
            {
                var p = schedule.Next();
                Assert.That(p.Families.Distinct().Count(), Is.EqualTo(p.Families.Count));
                Assert.That(p.HeroClasses.Distinct().Count(), Is.EqualTo(p.HeroClasses.Count));
                Assert.That(p.Level, Is.InRange(p.BandStart, p.BandEnd));
                Assert.That(p.Seed, Is.EqualTo(99 + i));
            }
        }

        [Test]
        public void Planned_encounter_follows_the_plan_and_the_party_uses_its_classes()
        {
            var db = Real;
            var plan = new EncounterSchedule(db, 31).Next();
            var stage = EncounterGenerator.Generate(db, plan);

            Assert.That(stage.Waves.Count, Is.EqualTo(EncounterGenerator.WaveCount));
            Assert.That(stage.LevelOverride, Is.EqualTo(plan.Level));
            Assert.That(stage.Theme, Is.EqualTo(db.Stage(plan.StageId).Theme));
            for (int i = 0; i < stage.Waves.Count; i++)
            {
                var fams = stage.Waves[i].Enemies.Select(e => db.Class(e.Class).DropTable).Distinct().ToList();
                Assert.That(fams.Count, Is.EqualTo(1));
                Assert.That(fams[0], Is.EqualTo(plan.Families[i % plan.Families.Count]).IgnoreCase);
            }

            var party = EncounterParty.Generate(db, plan.Seed, plan.Level, plan.HeroClasses);
            Assert.That(party.Select(h => h.ClassId), Is.EqualTo(plan.HeroClasses));
            Assert.That(party.All(h => h.Level == plan.Level), Is.True);
        }
    }
}
