using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Murder harpies (GG-T329, BIBLE §14.11): three swift Street flyers — a raider whose talons Slow (party
    /// tempo), a shrieker whose Gale Shriek drags heroes out of formation, and a spotter whose ranged darts Silence.</summary>
    public class HarpyTests
    {
        private static readonly string[] Family = { "harpy01", "harpy02", "harpy03" };

        [Test]
        public void The_harpies_validate_are_swift_sentient_flyers_and_are_placed()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("harpy") || p.Contains("gale_shriek") || p.Contains("talon_rake") || p.Contains("jammer_darts")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.Team, Is.EqualTo(Team.Enemy), id);
                Assert.That(c.BaseStats.Speed, Is.EqualTo(18f), $"{id}: a swift flyer");
                Assert.That(c.HasTag("Flying") && c.HasTag("Sentient"), Is.True, $"{id}: a Street crew that plans its pincers");
                Assert.That(c.DropTable, Is.EqualTo("harpy"), id);
                Assert.That(c.Support, Is.Not.Null, id);
                Assert.That(db.Stages.Values.Any(s => !s.IsTest && s.Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} is placed in the campaign");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count() + Family.Select(id => db.Classes[id].AttackRange).Distinct().Count(), Is.GreaterThanOrEqualTo(4),
                "three roles, not one harpy three times");
        }

        [Test]
        public void A_raiders_talons_slow_its_prey_and_cost_the_party_tempo()
        {
            var rake = Real.Abilities["talon_rake"];
            float keep = rake.Trigger.Chance;
            rake.Trigger.Chance = 1f;
            try
            {
                var (s, _) = Battle(Real, Stage(("harpy01", 2, 5)), ("paladin", 2, 6), ("cleric", 5, 7));
                var paladin = Hero(s, "paladin");
                for (int i = 0; i < 12 && !paladin.HasBuff("slowed") && !s.IsOver; i++) Turn(s, new ShieldCommand());
                Assert.That(s.Units.Any(u => u.IsHero && u.HasBuff("slowed")), Is.True, "a landed rake Slows the hero it hits");
                Assert.That(s.NetTempo, Is.LessThan(0), "a Slowed hero costs the party tempo, so every foe acts sooner");
            }
            finally { rake.Trigger.Chance = keep; }
        }

        [Test]
        public void The_gale_shriek_drags_heroes_toward_the_shrieker()
        {
            var (s, _) = Battle(Real, Stage(("harpy02", 2, 0)), ("paladin", 2, 6), ("cleric", 3, 6));
            var harpy = s.Units.Single(u => u.Def.Id == "harpy02");
            var gale = Real.Abilities[harpy.Def.ChargeAbility];
            Assert.That(gale.Pull, Is.GreaterThan(0));
            Assert.That(gale.Shape, Is.EqualTo(TargetShape.Square), "it catches the heroes beside its target too");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == gale.Id) && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var resolved = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == gale.Id);
            Assert.That(resolved, Is.Not.Null, "the shrieker charges and lands its gale");
            var drags = seen.OfType<DisplacedEvent>().Where(d => d.ByUnitId == harpy.Id).ToList();
            Assert.That(drags, Is.Not.Empty, "the gale moves heroes");
            foreach (var d in drags)
                Assert.That(d.To.Manhattan(harpy.Pos), Is.LessThan(d.From.Manhattan(harpy.Pos)), "dragged toward the shrieker, out of formation");
        }

        [Test]
        public void A_spotters_ranged_dart_can_silence_a_caster()
        {
            var darts = Real.Abilities["jammer_darts"];
            float keep = darts.Trigger.Chance;
            darts.Trigger.Chance = 1f;
            try
            {
                var (s, _) = Battle(Real, Stage(("harpy03", 3, 3)), ("paladin", 0, 7), ("cleric", 3, 6));
                var spotter = s.Units.Single(u => u.Def.Id == "harpy03");
                Assert.That(spotter.IsRanged, Is.True);
                Assert.That(spotter.Def.Targeting, Is.EqualTo(Targeting.Caster));
                for (int i = 0; i < 12 && !s.Units.Any(u => u.IsHero && u.HasBuff("silenced")) && !s.IsOver; i++) Turn(s, new ShieldCommand());
                Assert.That(s.Units.Any(u => u.IsHero && u.HasBuff("silenced")), Is.True, "a landed dart jams a hero's casting");
            }
            finally { darts.Trigger.Chance = keep; }
        }
    }
}
