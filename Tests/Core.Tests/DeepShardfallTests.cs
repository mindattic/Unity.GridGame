using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Deep Shardfall's region rule (GG-T363, BIBLE §14.20.2): telegraphed shard strikes that leave crystals, and
    /// the three Shardfall monsters built around them.</summary>
    public class DeepShardfallTests
    {
        private static readonly string[] Stages = { "desert-04", "desert-05" };
        private static readonly string[] Monsters = { "shardcaller", "crystalbrute", "shardskitter" };

        [Test]
        public void Deep_shardfall_plays_under_telegraphed_shard_strikes_that_leave_crystals()
        {
            var db = Real;
            foreach (var id in Stages)
                Assert.That(RegionRuleCatalog.ActiveFor(db, db.Stages[id]).Select(r => r.Id), Is.EqualTo(new[] { "shardfall-strikes" }), id);

            var strike = db.TryRuleEvent("shard-strike");
            Assert.That(strike.Seconds, Is.GreaterThan(0f), "every strike shows on the timeline first");
            Assert.That(strike.Effects.Select(e => e.Kind), Is.EqualTo(new[] { RuleEffectKind.Damage, RuleEffectKind.MarkTiles }));
            Assert.That(strike.Effects[0].Team, Is.EqualTo(RuleTeam.Any), "a foe lured under a strike takes it too");
            Assert.That(strike.Effects[1].State, Is.EqualTo("shard-crystal"));

            var crystal = db.TryRegionRule("shardfall-strikes").Hooks.Single(h => h.State == "shard-crystal");
            Assert.That((crystal.On, crystal.Effects[0].Buff), Is.EqualTo((RuleHook.TurnEnd, "ward")));
            var orb = crystal.Effects.Single(e => e.Kind == RuleEffectKind.GrantOrbs); // GG-T376: the ticket's bonus mana
            Assert.That((orb.Units, orb.Team, orb.Amount), Is.EqualTo((RuleUnits.Subject, RuleTeam.Hero, 1)));
        }

        [Test]
        public void The_shardfall_monsters_call_shrug_off_and_bait_the_strikes()
        {
            var db = Real;
            Assert.That(db.TryAbility(db.Classes["shardcaller"].ChargeAbility).RuleEffects.Single().Event, Is.EqualTo("shard-strike"));
            Assert.That(db.Classes["crystalbrute"].RuleImmune, Does.Contain("shard-strike"));
            Assert.That(db.Classes["crystalbrute"].OnDeathRule.Single().State, Is.EqualTo("shard-crystal"));
            Assert.That(db.Classes["shardskitter"].OnDeathRule.Single().Event, Is.EqualTo("shard-strike"), "its pincer stands under the strike");
            foreach (var m in Monsters)
                Assert.That(db.Classes[m].Portrait, Is.EqualTo("mannequin"), $"{m}: placeholder art until its portrait lands");

            foreach (var id in Stages)
            {
                var classes = db.Stages[id].Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
                foreach (var m in Monsters) Assert.That(classes, Does.Contain(m), $"{id} fields the {m}");
            }
            Assert.That(db.Stages["desert-05"].Waves.Last().Enemies.Select(e => e.Class), Does.Contain("captain"));
        }
    }
}
