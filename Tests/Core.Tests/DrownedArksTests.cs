using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Drowned Arks' region rule (GG-T362, BIBLE §14.20.4): a tide that rises row by row from the party's
    /// side, slows and wets whoever stands in it, and the three drowned monsters built around it.</summary>
    public class DrownedArksTests
    {
        private static readonly string[] Stages = { "swamp-04", "swamp-05" };
        private static readonly string[] Monsters = { "tidestrider", "stormcaller", "bloatdrowner" };

        [Test]
        public void The_drowned_arks_flood_row_by_row_from_the_bottom_and_the_flood_slows_and_wets()
        {
            var db = Real;
            foreach (var id in Stages)
                Assert.That(RegionRuleCatalog.ActiveFor(db, db.Stages[id]).Select(r => r.Id), Is.EqualTo(new[] { "rising-tide" }), id);

            var rule = db.TryRegionRule("rising-tide");
            var rises = rule.Hooks.Single(h => h.On == RuleHook.WaveSpawn).Effects.Where(e => e.Kind == RuleEffectKind.Schedule).ToList();
            Assert.That(rises.Select(e => (e.Area, e.Index)), Is.EqualTo(new[] { (RuleArea.Row, 7), (RuleArea.Row, 6), (RuleArea.Row, 5) }),
                "the bottom row first");
            Assert.That(rises.Select(e => db.TryRuleEvent(e.Event).Seconds), Is.Ordered.Ascending, "one row at a time");

            var inWater = rule.Hooks.Single(h => h.State == "floodwater");
            Assert.That(inWater.Effects.Select(e => e.Buff), Is.EqualTo(new[] { "slowed", "wet" }));
        }

        [Test]
        public void The_drowned_monsters_wade_conduct_and_hurry_the_tide()
        {
            var db = Real;
            Assert.That(db.Classes["tidestrider"].RuleImmune, Does.Contain("rising-tide"));
            var conduct = db.TryAbility(db.Classes["stormcaller"].ChargeAbility).RuleEffects.Single();
            Assert.That((conduct.Area, conduct.State, conduct.DamageType), Is.EqualTo((RuleArea.WithState, "floodwater", DamageType.Lightning)));
            Assert.That(db.Classes["bloatdrowner"].OnDeathRule.Count(e => e.Kind == RuleEffectKind.AdjustEvents && e.Seconds < 0), Is.EqualTo(3));
            foreach (var m in Monsters)
                Assert.That(db.Classes[m].Portrait, Is.EqualTo("mannequin"), $"{m}: placeholder art until its portrait lands");

            foreach (var id in Stages)
            {
                var classes = db.Stages[id].Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
                foreach (var m in Monsters) Assert.That(classes, Does.Contain(m), $"{id} fields the {m}");
            }
            Assert.That(db.Stages["swamp-05"].Waves.Last().Enemies.Select(e => e.Class), Does.Contain("swampmistress00"));
        }
    }
}
