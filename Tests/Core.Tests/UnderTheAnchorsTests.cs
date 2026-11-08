using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Under the Anchors' region rule (GG-T364, BIBLE §14.20.3): a sunk anchor that heaves every unit near it a
    /// step closer, and the three anchor monsters built around it.</summary>
    public class UnderTheAnchorsTests
    {
        private static readonly string[] Stages = { "cave-04", "cave-05" };
        private static readonly string[] Monsters = { "chainwarden", "anchorhulk", "hookthrower" };

        [Test]
        public void Under_the_anchors_each_wave_sinks_an_anchor_that_heaves_everyone_near_it()
        {
            var db = Real;
            foreach (var id in Stages)
                Assert.That(RegionRuleCatalog.ActiveFor(db, db.Stages[id]).Select(r => r.Id), Is.EqualTo(new[] { "anchor-pull" }), id);

            var spawn = db.TryRegionRule("anchor-pull").Hooks.Single(h => h.On == RuleHook.WaveSpawn).Effects.Select(e => e.Kind);
            Assert.That(spawn, Is.EqualTo(new[] { RuleEffectKind.AdjustEvents, RuleEffectKind.ClearTiles, RuleEffectKind.PlaceAnchor,
                RuleEffectKind.MarkTiles, RuleEffectKind.Schedule }), "the last heave is cancelled before the new anchor's starts");
            Assert.That(db.TryTileState("anchor-post").Blocks, Is.True);

            var heave = db.TryRuleEvent("anchor-heave");
            Assert.That((heave.Repeat, heave.Hazard), Is.EqualTo((true, false)), "it moves, it doesn't hurt");
            var pull = heave.Effects.Single();
            Assert.That((pull.Kind, pull.Move, pull.Amount, pull.Radius, pull.Team), Is.EqualTo((RuleEffectKind.MoveUnits, RuleMove.Toward, 1, 3, RuleTeam.Any)));
        }

        [Test]
        public void The_anchor_monsters_drag_the_anchor_ignore_the_heave_and_hook_heroes()
        {
            var db = Real;
            Assert.That(db.TryAbility(db.Classes["chainwarden"].ChargeAbility).RuleEffects.Select(e => e.Kind),
                Does.Contain(RuleEffectKind.MoveAnchor));
            Assert.That(db.Classes["anchorhulk"].RuleImmune, Does.Contain("anchor-pull"));
            Assert.That(db.TryAbility(db.Classes["hookthrower"].ChargeAbility).Pull, Is.EqualTo(2));
            foreach (var m in Monsters)
                Assert.That(db.Classes[m].Portrait, Is.EqualTo("mannequin"), $"{m}: placeholder art until its portrait lands");

            foreach (var id in Stages)
            {
                var classes = db.Stages[id].Waves.SelectMany(w => w.Enemies).Select(e => e.Class).ToList();
                foreach (var m in Monsters) Assert.That(classes, Does.Contain(m), $"{id} fields the {m}");
            }
        }
    }
}
