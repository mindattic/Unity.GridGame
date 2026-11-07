using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Anchor Watch (GG-T334, BIBLE §14.16): three undying knights of the arches. A Sentry that never leaves its
    /// post and answers blows with a chilling riposte, a Reaver whose oath holds once, and a Vigilant that lends the Watch
    /// its old fire (Regen). All of them leave a grave that rises unless smashed.</summary>
    public class AnchorWatchTests
    {
        private static readonly string[] Family = { "wraithknight01", "wraithknight02", "wraithknight03" };

        [Test]
        public void The_watch_validates_plays_three_roles_and_guards_the_anchor_wastes()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("wraithknight") || p.Contains("anchorwatch") || p.Contains("rime_riposte")
                                                                 || p.Contains("oathbound") || p.Contains("ember_vigil")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var wastes = db.Campaign.Themes.Where(t => t.Id == "cave").SelectMany(t => t.Stages).ToList();
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.HasTag("Undead") && c.HasTag("Sentient"), Is.True, $"{id}: dead knights who still keep an oath");
                Assert.That(c.DropTable, Is.EqualTo("anchorwatch"), id);
                Assert.That(c.Remains, Is.EqualTo("gravestone"), $"{id}: the Watch rises unless its grave is smashed");
                Assert.That(c.Resistances[DamageType.Ice], Is.LessThan(1f), $"{id}: cold");
                Assert.That(c.Resistances[DamageType.Fire], Is.GreaterThan(1f), $"{id}: fire undoes them");
                Assert.That(wastes.Any(s => db.Stages[s].Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} guards the Anchor Wastes");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count(), Is.EqualTo(3), "three roles");
            Assert.That(db.Classes["wraithknight01"].Brain, Is.EqualTo(Brain.Sentinel), "the Sentry holds its post");
        }

        [Test]
        public void The_sentry_never_leaves_its_post()
        {
            var (s, _) = Battle(Real, Stage(("wraithknight01", 2, 2)), ("paladin", 2, 7), ("cleric", 5, 7));
            var sentry = s.Units.Single(u => u.Def.Id == "wraithknight01");
            var post = sentry.Pos;
            for (int i = 0; i < 8 && !s.IsOver; i++) Turn(s, new ShieldCommand());
            Assert.That(sentry.Pos, Is.EqualTo(post));
        }

        [Test]
        public void The_rime_riposte_counters_and_slows_the_attacker()
        {
            var riposte = Real.Abilities["rime_riposte"];
            Assert.That(riposte.Kind, Is.EqualTo(AbilityKind.Reactive));
            Assert.That(riposte.Trigger.On, Is.EqualTo(TriggerOn.OnAttackedDirect));
            Assert.That(riposte.Trigger.Effects.Select(e => e.Kind), Is.EquivalentTo(new[] { TriggerEffectKind.Counterstrike, TriggerEffectKind.ApplyBuff }));
            Assert.That(riposte.Trigger.Effects.Single(e => e.Kind == TriggerEffectKind.ApplyBuff).Who, Is.EqualTo(EffectTarget.Attacker));
            Assert.That(TriggerText.Describe(Real, riposte), Does.Contain("Slowed"));
        }

        [Test]
        public void The_reavers_oath_holds_only_once()
        {
            var oath = Real.Abilities["oathbound"];
            Assert.That(oath.Trigger.On, Is.EqualTo(TriggerOn.OnLowHp));
            Assert.That(oath.Trigger.Cooldown, Is.GreaterThanOrEqualTo(99), "once a battle");
            Assert.That(oath.Trigger.Effects.Any(e => e.Kind == TriggerEffectKind.Heal), Is.True);
            Assert.That(oath.Trigger.Effects.Any(e => e.Kind == TriggerEffectKind.ApplyBuff && e.Buff == "ward"), Is.True);
            Assert.That(Real.Classes["wraithknight02"].Abilities, Does.Contain("oathbound"));
        }

        [Test]
        public void The_ember_vigil_gives_regen_to_the_knights_near_the_vigilant()
        {
            var (s, _) = Battle(Real, Stage(("wraithknight03", 2, 0), ("wraithknight01", 4, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var sentry = s.Units.Single(u => u.Def.Id == "wraithknight01");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_ember_vigil") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var vigil = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_ember_vigil");
            Assert.That(vigil, Is.Not.Null, "the vigilant charges its vigil");
            Assert.That(vigil.Targets, Does.Contain(sentry.Id), "the sentry stands within 2 tiles");
            Assert.That(seen.OfType<BuffAppliedEvent>().Any(b => b.UnitId == sentry.Id && b.BuffId == "regen"), Is.True);
        }
    }
}
