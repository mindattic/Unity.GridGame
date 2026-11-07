using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Infernalists (GG-T338, BIBLE §14.14): four Ember zealots that set heroes Burning and an Executioner
    /// that hits Burning heroes 50% harder — the new OtherHasStatus trigger condition.</summary>
    public class InfernalistTests
    {
        private static readonly string[] Family = { "infernalist01", "infernalist02", "infernalist03", "infernalist04" };

        [Test]
        public void The_infernalists_validate_play_four_roles_and_march_in_the_shardfall()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("infernalist") || p.Contains("hellfire") || p.Contains("blood_rite")
                                                                 || p.Contains("cinder_claws") || p.Contains("executioners_verdict")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var shardfall = db.Campaign.Themes.Where(t => t.Id == "desert").SelectMany(t => t.Stages).ToList();
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.HasTag("Sentient"), Is.True, $"{id}: an order that plans its pincers");
                Assert.That(c.DropTable, Is.EqualTo("infernalist"), id);
                Assert.That(c.Resistances[DamageType.Fire], Is.LessThan(1f), $"{id}: born of the Ember");
                Assert.That(shardfall.Any(s => db.Stages[s].Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} marches in the Shardfall");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count(), Is.EqualTo(4), "four roles");
        }

        [Test]
        public void The_executioner_hits_a_burning_hero_half_again_as_hard()
        {
            var (s, _) = Battle(Real, Stage(("infernalist04", 2, 2)), ("paladin", 2, 3), ("cleric", 5, 7));
            var exec = s.Units.Single(u => u.Def.Id == "infernalist04");
            var paladin = Hero(s, "paladin");
            Assert.That(Triggers.OutgoingMultiplier(s, exec, paladin), Is.EqualTo(1f), "an unburnt hero takes a normal blow");
            paladin.Buffs.Add(new BuffInstance { Def = Real.Buffs["burning"], Remaining = 3 });
            Assert.That(Triggers.OutgoingMultiplier(s, exec, paladin), Is.EqualTo(1.5f).Within(1e-4), "a Burning hero takes ×1.5");
            var zealot = new Unit { Def = Real.Classes["infernalist01"], Team = Team.Enemy, LevelStats = Real.Classes["infernalist01"].BaseStats.Clone() };
            Assert.That(Triggers.OutgoingMultiplier(s, zealot, paladin), Is.EqualTo(1f), "only the Executioner carries the verdict");
        }

        [Test]
        public void The_verdict_reads_as_a_sentence_and_validation_rejects_an_unknown_status()
        {
            var verdict = Real.Abilities["executioners_verdict"];
            Assert.That(TriggerText.Describe(Real, verdict), Does.Contain("the foe has [Burning]"));
            var keep = verdict.Trigger.Condition.OtherHasStatus;
            verdict.Trigger.Condition.OtherHasStatus = "no_such_status";
            try { Assert.That(ContentLoader.Validate(Real).Any(p => p.Contains("executioners_verdict") && p.Contains("no_such_status")), Is.True); }
            finally { verdict.Trigger.Condition.OtherHasStatus = keep; }
        }

        [Test]
        public void Hellfire_sets_the_heroes_in_its_cross_burning()
        {
            var (s, _) = Battle(Real, Stage(("infernalist02", 2, 0)), ("paladin", 2, 6), ("cleric", 3, 6), ("barbarian", 5, 3));
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_hellfire") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var fire = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_hellfire");
            Assert.That(fire, Is.Not.Null, "the pyromancer charges and lands its Hellfire");
            var caught = s.Units.Where(u => u.IsHero && fire.Targets.Contains(u.Id)).ToList();
            Assert.That(caught.Count, Is.GreaterThanOrEqualTo(2), "the paladin and cleric stand side by side");
            Assert.That(caught.Any(h => h.Def.Id == "barbarian"), Is.False);
            Assert.That(caught.Any(h => h.HasBuff("burning")), Is.True, "Hellfire sets them Burning");
        }

        [Test]
        public void The_bloodbinder_mends_and_empowers_its_most_wounded_kin()
        {
            var (s, _) = Battle(Real, Stage(("infernalist03", 1, 0), ("infernalist04", 4, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var exec = s.Units.Single(u => u.Def.Id == "infernalist04");
            exec.Hp = exec.MaxHp / 3;
            int before = exec.Hp;
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_blood_rite") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var rite = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_blood_rite");
            Assert.That(rite, Is.Not.Null, "the binder charges its rite on a wounded ally");
            Assert.That(rite.Targets, Does.Contain(exec.Id));
            Assert.That(exec.Hp, Is.GreaterThan(before));
            Assert.That(exec.HasBuff("empowered"), Is.True);
        }
    }
}
