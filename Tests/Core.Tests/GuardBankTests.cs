using System.Linq;
using GridGame.Core.Battle;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T189 (Â§2.5, Â§3.1.9): Guard ends the turn early and banks the unused turn time into the mana bubble.</summary>
    public class GuardBankTests
    {
        private static Unit Wolf(BattleState s) => s.Units.First(u => u.Def.Id == "wolf" && u.Alive);

        [Test]
        public void Guard_conversion_is_proportional_to_time_left_and_capped()
        {
            Assert.That(BattleEngine.GuardChargeFor(0f), Is.EqualTo(0));
            Assert.That(BattleEngine.GuardChargeFor(-3f), Is.EqualTo(0));
            Assert.That(BattleEngine.GuardChargeFor(float.NaN), Is.EqualTo(0));
            Assert.That(BattleEngine.GuardChargeFor(1.5f), Is.EqualTo((int)System.Math.Round(1.5f * BattleConfig.GuardChargePerSecond)));
            Assert.That(BattleEngine.GuardChargeFor(2f), Is.EqualTo(2 * BattleEngine.GuardChargeFor(1f)));
            Assert.That(BattleEngine.GuardChargeFor(1000f), Is.EqualTo(BattleConfig.GuardMaxCharge));
            Assert.That(BattleEngine.GuardChargeFor(float.MaxValue), Is.EqualTo(BattleConfig.GuardMaxCharge));
            Assert.That(BattleConfig.GuardMaxCharge, Is.LessThan(BattleConfig.ManaChargePerOrb), "one Guard fills part of a bubble, never a whole orb");
        }

        [Test]
        public void Guard_banks_only_the_turn_time_not_yet_used()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(s).SetIp(s, 999); // the wolf is a tick from ACT: GG-T311, the window is that 0.1 s (no real-time floor)
            float w = BattleEngine.TurnWindowSeconds(s);
            Assert.That(w, Is.EqualTo(0.1f).Within(1e-4));
            Assert.That(BattleEngine.GuardCharge(s), Is.EqualTo(BattleEngine.GuardChargeFor(w)));
            Assert.That(BattleEngine.GuardCharge(s, w / 2f), Is.EqualTo(BattleEngine.GuardChargeFor(w / 2f)));
            Assert.That(BattleEngine.GuardCharge(s, w), Is.EqualTo(0));
            Assert.That(BattleEngine.GuardCharge(s, w + 5f), Is.EqualTo(0));
        }

        [Test]
        public void Guard_fills_the_mana_bubble_by_the_time_it_gives_up()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(s).SetIp(s, 999);
            s.ManaCharge = 0;
            int expected = BattleEngine.GuardCharge(s);
            Assert.That(expected, Is.GreaterThan(0));
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var charge = ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "guard");
            Assert.That(charge.Amount, Is.EqualTo(expected));
            Assert.That(charge.Total, Is.EqualTo(expected));
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "guard"), Is.False, "a partial bubble mints no orb");

            // A Guard with more of the turn ahead (the wolf far from ACT) banks more, up to the cap.
            var (far, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(far).SetIp(far, 0);
            int farCharge = BattleEngine.GuardCharge(far);
            Assert.That(farCharge, Is.GreaterThanOrEqualTo(expected));
            Assert.That(farCharge, Is.LessThanOrEqualTo(BattleConfig.GuardMaxCharge));
        }

        [Test]
        public void Guard_bubble_rolls_into_an_orb_and_fades_when_the_bank_holds_12()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(s).SetIp(s, 999);
            s.Bank.Add(s.Bank.Capacity);
            Assert.That(s.Bank.Count, Is.EqualTo(ManaBank.DefaultCapacity));
            s.ManaCharge = BattleConfig.ManaChargePerOrb - 1;
            int amount = BattleEngine.DampedCharge(s, BattleEngine.GuardCharge(s)); // a full bank damps it (§3.1.9, GG-T247)
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            var minted = ev.OfType<OrbMintedEvent>().Where(o => o.Reason == "guard").ToList();
            Assert.That(minted.Count, Is.EqualTo(1));
            Assert.That(minted[0].Landed, Is.False, "a full bank lets the rolled-over orb fade (GG-LAW-6)");
            Assert.That(s.Bank.Count, Is.EqualTo(ManaBank.DefaultCapacity));
            Assert.That(ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "guard").Total,
                Is.EqualTo((BattleConfig.ManaChargePerOrb - 1 + amount) % BattleConfig.ManaChargePerOrb));
        }

        [Test]
        public void Guard_with_room_in_the_bank_lands_the_rolled_over_orb()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(s).SetIp(s, 999);
            int before = s.Bank.Count;
            s.ManaCharge = BattleConfig.ManaChargePerOrb - 1;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "guard" && o.Landed), Is.True);
            Assert.That(s.Bank.Count, Is.GreaterThanOrEqualTo(before + 1));
        }

        [Test]
        public void Guard_ends_the_turn_and_banks_before_the_enemy_acts()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Wolf(s).SetIp(s, 999);
            long clock = s.Clock;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            int banked = ev.FindIndex(e => e is ManaChargedEvent c && c.Reason == "guard");
            int enemy = ev.FindIndex(e => e is TurnStartedEvent);
            Assert.That(banked, Is.GreaterThanOrEqualTo(0));
            Assert.That(enemy, Is.GreaterThan(banked), "the turn time is banked when Guard is pressed, then the enemy acts");
            Assert.That(s.Clock, Is.GreaterThan(clock));
            Assert.That(ev.Last() is PlayerControlEvent, Is.True);
        }

        [Test]
        public void Guard_drops_a_pending_drag_timer_bank_instead_of_stacking_it()
        {
            var (s, _) = Battle(Mini(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            s.PendingTimeBankSeconds = BattleConfig.DragTimerSeconds;
            Wolf(s).SetIp(s, 999);
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "time bank"), Is.False);
            Assert.That(ev.Any(e => e is ManaChargedEvent c && c.Reason == "guard"), Is.True);
        }
    }

    internal static class GuardBankTestExt
    {
        public static void SetIp(this Unit u, BattleState s, int ip) => s.TurnEntryOf(u.Id).Ip = ip;
    }
}
