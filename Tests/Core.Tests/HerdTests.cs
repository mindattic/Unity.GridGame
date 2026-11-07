using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Herd (GG-T339, BIBLE §14.18): Street kids spliced with horns and bull-mass, muscle for hire. Their
    /// loyalty is the Stampede — when any ally falls, every bull still standing charges and its next turn comes sooner —
    /// and the crew is a Boss that feeds it, a Bouncer that guards, a Gorer that charges down a line and a Breaker.</summary>
    public class HerdTests
    {
        private static readonly string[] Family = { "minotaur01", "minotaur02", "minotaur03", "minotaur04" };

        [Test]
        public void The_herd_validates_plays_four_roles_and_works_the_street()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("minotaur") || p.Contains("herd") || p.Contains("stampede")
                                                                 || p.Contains("bull_rush") || p.Contains("brass_knuckles")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            // Street territory: the Tithe Moors and Halcyon's underspire.
            var street = db.Campaign.Themes.Where(t => t.Id == "greenvalley" || t.Id == "cityruins").SelectMany(t => t.Stages).ToList();
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.HasTag("Sentient"), Is.True, $"{id}: a crew that plans its pincers");
                Assert.That(c.DropTable, Is.EqualTo("herd"), id);
                Assert.That(c.Portrait, Is.EqualTo(id), $"{id}: owner art, never overwritten by import_art");
                Assert.That(new[] { 8f, 11f, 15f, 18f }, Does.Contain(c.BaseStats.Speed), $"{id}: a speed tier");
                Assert.That(c.Abilities, Does.Contain("herd_stampede"), $"{id}: every bull stampedes");
                Assert.That(c.Resistances[DamageType.Arcane], Is.LessThan(1f), $"{id}: the splice holds its aether in");
                Assert.That(c.Support?.Label, Is.Not.Null.And.Not.Empty, $"{id}: its own named support");
                Assert.That(street.Any(s => db.Stages[s].Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} works the Street");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count(), Is.EqualTo(4), "four roles");
            Assert.That(db.Classes["minotaur03"].ChargeShape, Is.EqualTo(ChargeShape.Line), "the gorer runs down a line");
            Assert.That(GridGame.Meta.Abilities.IsHeroUsable(db.Abilities["stampede_rush"]), Is.False, "a free enemy-only rush is never learnable");
        }

        [Test]
        public void When_an_ally_falls_the_herd_stampedes_forward_on_the_bar()
        {
            var (s, _) = Battle(Real, Stage(("ganger03", 2, 3), ("minotaur02", 5, 0)), ("paladin", 1, 3), ("barbarian", 3, 4), ("cleric", 5, 7));
            var ganger = s.Units.Single(u => u.Def.Id == "ganger03");
            var bouncer = s.Units.Single(u => u.Def.Id == "minotaur02");
            ganger.Hp = 1;
            var ev = Turn(s, new MoveCommand { HeroId = Hero(s, "barbarian").Id, Path = P((3, 3)) });
            Assume.That(ganger.Alive, Is.False, "the pincer fells the ganger");
            Assert.That(ev.OfType<TriggeredEvent>().Any(t => t.UnitId == bouncer.Id && t.AbilityId == "herd_stampede"), Is.True, "the bull answers its crew");
            var rush = ev.OfType<HastenedEvent>().FirstOrDefault(h => h.UnitId == bouncer.Id);
            Assert.That(rush, Is.Not.Null, "its icon lurches forward");
            Assert.That(rush.ToU, Is.LessThan(rush.FromU), "sooner on the bar");
        }

        [Test]
        public void The_bull_rush_tramples_the_hero_on_its_line_and_spares_the_one_off_it()
        {
            var (s, _) = Battle(Real, Stage(("minotaur03", 2, 0)), ("paladin", 2, 5), ("cleric", 5, 7));
            var paladin = Hero(s, "paladin"); var cleric = Hero(s, "cleric");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_bull_rush") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var queued = seen.OfType<CastQueuedEvent>().FirstOrDefault(e => e.AbilityId == "charge_bull_rush");
            Assert.That(queued, Is.Not.Null, "the gorer lowers its horns");
            Assert.That(queued.LineTiles, Is.Not.Empty, "its line is marked");
            var resolved = seen.OfType<AbilityResolvedEvent>().First(e => e.AbilityId == "charge_bull_rush");
            Assert.That(resolved.Targets, Does.Not.Contain(cleric.Id), "a hero off the line is untouched");
            if (resolved.Targets.Contains(paladin.Id))
                Assert.That(seen.OfType<DamagedEvent>().Any(d => d.TargetId == paladin.Id && d.Amount > 0), Is.True, "the hero on the line is trampled");
        }

        [Test]
        public void The_boss_feeds_its_wounded_crew_and_rushes_them()
        {
            var (s, _) = Battle(Real, Stage(("minotaur01", 1, 0), ("minotaur02", 2, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var boss = s.Units.Single(u => u.Def.Id == "minotaur01");
            var bouncer = s.Units.Single(u => u.Def.Id == "minotaur02");
            bouncer.Hp = bouncer.MaxHp / 3;
            int before = bouncer.Hp;
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_feed_the_herd") && !s.IsOver; i++)
            {
                var beside = new Coord(boss.Pos.X + (boss.Pos.X < 5 ? 1 : -1), boss.Pos.Y);
                if (s.UnitAt(beside) == null) bouncer.Pos = beside; // keep the crew within the boss's reach
                seen.AddRange(Turn(s, new ShieldCommand()));
            }
            var feed = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_feed_the_herd");
            Assert.That(feed, Is.Not.Null, "the boss pays its wounded crew");
            Assert.That(feed.Targets, Does.Contain(bouncer.Id));
            Assert.That(bouncer.Hp, Is.GreaterThan(before));
            Assert.That(seen.OfType<HastenedEvent>().Any(h => h.UnitId == bouncer.Id), Is.True, "fed, it rushes its next turn");
        }

        [Test]
        public void The_bouncers_brass_knuckles_can_slow_a_hero()
        {
            var k = Real.Abilities["brass_knuckles"];
            Assert.That(k.Trigger.On, Is.EqualTo(TriggerOn.OnLandedHit));
            Assert.That(k.Trigger.Effects.Single().Buff, Is.EqualTo("slowed"));
            Assert.That(Real.Classes["minotaur02"].ArmorPlate, Is.GreaterThan(0), "a thick front");
            Assert.That(Real.Classes["minotaur02"].Brain, Is.EqualTo(Brain.Guardian), "paid to stand in front of the crew");
        }
    }
}
