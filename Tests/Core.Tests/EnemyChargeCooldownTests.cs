using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T283: an enemy charge with CooldownTurns rests between charges (templars used to Smite every turn).</summary>
    public class EnemyChargeCooldownTests
    {
        [Test]
        public void Smite_has_a_cooldown_so_a_templar_cannot_charge_it_on_consecutive_turns()
        {
            Assert.That(Real.Abilities["charge_smite"].CooldownTurns, Is.GreaterThan(0));
            var (s, _) = Battle(Real, Stage(("templar00", 5, 0)), ("paladin", 0, 7), ("cleric", 2, 7));
            var templar = s.Units.Single(u => u.Def.Id == "templar00");
            Assert.That(EnemyPlanner.PlanCast(s, templar, false), Is.Not.Null, "fresh: it charges");
            templar.Cooldowns["charge_smite"] = 1;
            Assert.That(EnemyPlanner.PlanCast(s, templar, false), Is.Null, "recharging: it acts normally instead");
            templar.Cooldowns["charge_smite"] = 0;
            Assert.That(EnemyPlanner.PlanCast(s, templar, false), Is.Not.Null, "recharged: it charges again");
        }

        [Test]
        public void Queueing_a_charge_starts_its_cooldown()
        {
            var (s, _) = Battle(Real, Stage(("templar00", 5, 0)), ("paladin", 0, 7), ("cleric", 2, 7));
            var templar = s.Units.Single(u => u.Def.Id == "templar00");
            for (int i = 0; i < 12 && !s.IsOver && !s.Timeline.Any(e => e.IsCharge && e.OwnerId == templar.Id); i++)
                BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(s.Timeline.Any(e => e.IsCharge && e.OwnerId == templar.Id), "the templar charged Smite");
            Assert.That(templar.Cooldowns.TryGetValue("charge_smite", out var cd) && cd > 0, "and Smite is now recharging");
        }
    }
}
