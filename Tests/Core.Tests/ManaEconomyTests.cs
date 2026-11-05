using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T247: the tempered mana economy (§3.1.9) — fullness damping, explicit grants and the support factor.</summary>
    public class ManaEconomyTests
    {
        /// <summary>An earned charge of <paramref name="baseCharge"/> after any fullness damping (bank empty … full).</summary>
        public static bool IsDampedFrom(int amount, int baseCharge) =>
            amount <= baseCharge && amount >= System.Math.Max(1, (int)System.Math.Round(baseCharge * (1f - BattleConfig.ChargeFullnessDamping)));

        private static int PincerCharge(int bankBefore)
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30; // dented, not killed: the same share both times
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            s.Bank.Add(bankBefore);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            return ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "pincer").Amount;
        }

        [Test]
        public void Damping_runs_from_full_rate_at_an_empty_bank_to_the_floor_at_a_full_one()
        {
            var (s, _) = Battle(Mini(), Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            Assert.That(BattleEngine.ChargeDamping(s), Is.EqualTo(1f));
            s.Bank.Add(s.Bank.Capacity);
            Assert.That(BattleEngine.ChargeDamping(s), Is.EqualTo(1f - BattleConfig.ChargeFullnessDamping).Within(1e-5));
            Assert.That(BattleEngine.DampedCharge(s, 1), Is.EqualTo(1), "a positive charge never rounds to nothing");
        }

        [Test]
        public void A_fuller_bank_charges_slower()
        {
            int empty = PincerCharge(0), nearlyFull = PincerCharge(10);
            Assert.That(nearlyFull, Is.LessThan(empty));
            Assert.That(IsDampedFrom(nearlyFull, empty), Is.True);
        }

        [Test]
        public void Explicit_grants_ignore_fullness_damping()
        {
            var db = Mini();
            db.Classes["knight"].BaseStats.Luck = 100; // steal always lands
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            s.Bank.Add(s.Bank.Capacity - 1);
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "steal" };
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            Assert.That(s.Bank.Count, Is.EqualTo(s.Bank.Capacity), "a stolen orb is a whole orb, however full the bank");
        }

        [Test]
        public void Mana_supporters_charge_at_the_support_factor()
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30;
            db.Classes["mage"].Support = new SupportDef { Effect = SupportEffect.Mana, Magnitude = 40, Target = SupportTarget.Attacker, Range = 2 };
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 1, 4));
            var mage = Hero(s, "mage");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var support = ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "support" && c.SourceId == mage.Id);
            Assert.That(IsDampedFrom(support.Amount, (int)System.Math.Round(40 * BattleConfig.SupportManaFactor)), Is.True);
        }
    }
}
