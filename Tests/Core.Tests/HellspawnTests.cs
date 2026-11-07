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
    /// <summary>The Hellspawn (GG-T340, BIBLE §14.19): what the Ember makes of a beast or a person who drinks from the red
    /// rivers. Every one carries Bloodscent — its blows land 50% harder on a foe below half HP, the new OtherHpBelow
    /// trigger condition — and an Emberheart floods a hero's whole row with burning blood.</summary>
    public class HellspawnTests
    {
        private static readonly string[] Family = { "hellspawn01", "hellspawn02", "hellspawn03", "hellspawn04" };

        [Test]
        public void The_hellspawn_validate_play_four_roles_and_hunt_the_shardfall()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("hellspawn") || p.Contains("bloodscent") || p.Contains("red_tide")
                                                                 || p.Contains("searing_scales")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var shardfall = db.Campaign.Themes.Where(t => t.Id == "desert").SelectMany(t => t.Stages).ToList();
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.HasTag("Sentient"), Is.False, $"{id}: the Ember's beasts don't plan pincers");
                Assert.That(c.DropTable, Is.EqualTo("hellspawn"), id);
                Assert.That(c.Portrait, Is.EqualTo(id), $"{id}: owner art, never overwritten by import_art");
                Assert.That(new[] { 8f, 11f, 15f, 18f }, Does.Contain(c.BaseStats.Speed), $"{id}: a speed tier");
                Assert.That(c.Abilities, Does.Contain("bloodscent"), $"{id}: every hellspawn smells blood");
                Assert.That(c.Abilities, Does.Contain("infernal_hide"), $"{id}: born of the Ember, it can't burn");
                Assert.That(c.Resistances[DamageType.Fire], Is.LessThan(1f), $"{id}: born of the Ember");
                Assert.That(c.Support?.Label, Is.Not.Null.And.Not.Empty, $"{id}: its own named support");
                Assert.That(shardfall.Any(s => db.Stages[s].Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} hunts the Shardfall");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count(), Is.EqualTo(4), "four roles");
        }

        [Test]
        public void Bloodscent_hits_a_hero_below_half_hp_half_again_as_hard()
        {
            var (s, _) = Battle(Real, Stage(("hellspawn01", 2, 2)), ("paladin", 2, 3), ("cleric", 5, 7));
            var ravager = s.Units.Single(u => u.Def.Id == "hellspawn01");
            var paladin = Hero(s, "paladin");
            Assert.That(Triggers.OutgoingMultiplier(s, ravager, paladin), Is.EqualTo(1f), "a hale hero takes a normal blow");
            paladin.Hp = (paladin.MaxHp + 1) / 2;
            Assert.That(Triggers.OutgoingMultiplier(s, ravager, paladin), Is.EqualTo(1f), "at half HP it is not yet bleeding enough");
            paladin.Hp = paladin.MaxHp / 2 - 1;
            Assert.That(Triggers.OutgoingMultiplier(s, ravager, paladin), Is.EqualTo(1.5f).Within(1e-4), "a bleeding hero takes ×1.5");
            var zealot = new Unit { Def = Real.Classes["infernalist01"], Team = Team.Enemy, LevelStats = Real.Classes["infernalist01"].BaseStats.Clone() };
            Assert.That(Triggers.OutgoingMultiplier(s, zealot, paladin), Is.EqualTo(1f), "only the hellspawn smell it");
        }

        [Test]
        public void Bloodscent_reads_as_a_sentence_and_validation_keeps_the_fraction_in_range()
        {
            var scent = Real.Abilities["bloodscent"];
            Assert.That(TriggerText.Describe(Real, scent), Does.Contain("the foe is below 50% HP"));
            var keep = scent.Trigger.Condition.OtherHpBelow;
            try
            {
                scent.Trigger.Condition.OtherHpBelow = 1.5f;
                Assert.That(ContentLoader.Validate(Real).Any(p => p.Contains("bloodscent") && p.Contains("OtherHpBelow")), Is.True);
                scent.Trigger.Condition.OtherHpBelow = -0.1f;
                Assert.That(ContentLoader.Validate(Real).Any(p => p.Contains("bloodscent") && p.Contains("OtherHpBelow")), Is.True);
            }
            finally { scent.Trigger.Condition.OtherHpBelow = keep; }
        }

        [Test]
        public void The_red_tide_floods_its_targets_row_and_burns_it()
        {
            var (s, _) = Battle(Real, Stage(("hellspawn03", 2, 0)), ("paladin", 1, 6), ("cleric", 4, 6), ("barbarian", 3, 7));
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_red_tide") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var tide = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_red_tide");
            Assert.That(tide, Is.Not.Null, "the emberheart charges and lands its tide");
            var caught = s.Units.Where(u => u.IsHero && tide.Targets.Contains(u.Id)).ToList();
            Assert.That(caught.Select(h => h.Pos.Y).Distinct().Count(), Is.EqualTo(1), "one row");
            Assert.That(caught.Count, Is.GreaterThanOrEqualTo(1));
            var row = caught[0].Pos.Y;
            foreach (var h in s.Units.Where(u => u.IsHero && u.Alive && u.Pos.Y != row)) Assert.That(tide.Targets, Does.Not.Contain(h.Id), $"{h.Def.Id} stands off the row");
            Assert.That(seen.OfType<BuffAppliedEvent>().Any(b => b.BuffId == "burning" && caught.Any(h => h.Id == b.UnitId)), Is.True, "the tide sets them Burning");
        }

        [Test]
        public void Searing_scales_can_burn_whoever_strikes_the_scaleback()
        {
            var scales = Real.Abilities["searing_scales"];
            Assert.That(scales.Trigger.On, Is.EqualTo(TriggerOn.OnAttackedDirect));
            Assert.That(scales.Trigger.Effects.Single().Buff, Is.EqualTo("burning"));
            Assert.That(scales.Trigger.Effects.Single().Who, Is.EqualTo(EffectTarget.Attacker));
            Assert.That(Real.Classes["hellspawn04"].ArmorPlate, Is.GreaterThan(0), "a scaled front");
            Assert.That(Real.Classes["hellspawn02"].Abilities, Does.Contain("lifesteal"), "the drinker drinks");
        }
    }
}
