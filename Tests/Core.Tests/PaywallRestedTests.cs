using System;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using Newtonsoft.Json;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>§25.18 (GG-T232): the paywall hooks and the party's Rested pool.</summary>
    public class PaywallRestedTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        private static SaveData Fresh()
        {
            var s = Progression.NewGame(Real, "Paywall");
            s.Rested = new RestedSave { Points = RestedPool.Max, RegenUtc = T0 };
            return s;
        }

        [Test]
        public void Every_paywall_hook_costs_one_gold_and_one_second_for_now()
        {
            var s = Fresh();
            foreach (PaywallKind k in Enum.GetValues(typeof(PaywallKind)))
            {
                Assert.That(Paywall.Cost(k, s), Is.EqualTo(1), k.ToString());
                Assert.That(Paywall.Duration(k, s), Is.EqualTo(1.0), k.ToString());
            }
            Assert.That(Paywall.Tag(PaywallKind.RestedRefill, s), Is.EqualTo("1g · 1s"));
        }

        [Test]
        public void A_launch_spends_rested_and_it_regenerates_one_point_per_interval()
        {
            var s = Fresh();
            Assert.That(RestedPool.Spend(s, T0).Ok, Is.True);
            Assert.That(RestedPool.Spend(s, T0).Ok, Is.True);
            Assert.That(RestedPool.Points(s, T0), Is.EqualTo(RestedPool.Max - 2));
            Assert.That(RestedPool.Points(s, T0 + RestedPool.RegenEvery - TimeSpan.FromSeconds(1)), Is.EqualTo(RestedPool.Max - 2));
            Assert.That(RestedPool.Points(s, T0 + RestedPool.RegenEvery), Is.EqualTo(RestedPool.Max - 1));
            Assert.That(RestedPool.Points(s, T0 + RestedPool.RegenEvery + RestedPool.RegenEvery), Is.EqualTo(RestedPool.Max));
            Assert.That(RestedPool.Points(s, T0 + TimeSpan.FromDays(3)), Is.EqualTo(RestedPool.Max), "never above max");
        }

        [Test]
        public void An_exhausted_party_is_refused_a_launch_and_told_when_the_next_point_comes()
        {
            var s = Fresh();
            for (int i = 0; i < RestedPool.Max; i++) Assert.That(RestedPool.Spend(s, T0).Ok, Is.True);
            Assert.That(RestedPool.CanLaunch(s, T0, out var why), Is.False);
            Assert.That(why, Does.Contain("Rested").And.Contain("10:00"));
            Assert.That(RestedPool.Spend(s, T0).Ok, Is.False);
            Assert.That(s.Rested.Points, Is.EqualTo(0));
        }

        [Test]
        public void Test_mode_and_unlimited_saves_never_spend_rested()
        {
            var s = Fresh();
            s.TestMode = true;
            for (int i = 0; i < RestedPool.Max * 3; i++) Assert.That(RestedPool.Spend(s, T0).Ok, Is.True);
            Assert.That(s.Rested.Points, Is.EqualTo(RestedPool.Max));
        }

        [Test]
        public void The_refill_job_takes_its_gold_up_front_and_fills_the_pool_when_its_time_is_up()
        {
            var s = Fresh();
            for (int i = 0; i < RestedPool.Max; i++) RestedPool.Spend(s, T0);
            s.Gold = 5;
            Assert.That(RestedPool.BeginRefill(s, T0, out var job).Ok, Is.True);
            Assert.That(s.Gold, Is.EqualTo(4));
            Assert.That(RestedPool.BeginRefill(s, T0, out _).Ok, Is.False, "one job of a kind at a time");
            Assert.That(Paywall.Finish(Real, s, job, T0.AddSeconds(0.5)).Ok, Is.False, "not before its time");
            Assert.That(Paywall.Progress(job, T0.AddSeconds(0.5)), Is.EqualTo(0.5).Within(1e-6));
            Assert.That(Paywall.Finish(Real, s, job, T0.AddSeconds(1)).Ok, Is.True);
            Assert.That(s.Rested.Points, Is.EqualTo(RestedPool.Max));
            Assert.That(s.Jobs, Is.Empty);
            Assert.That(RestedPool.BeginRefill(s, T0, out _).Ok, Is.False, "nothing to refill when full");
        }

        [Test]
        public void A_refill_with_no_gold_is_refused()
        {
            var s = Fresh();
            RestedPool.Spend(s, T0);
            s.Gold = 0;
            Assert.That(RestedPool.BeginRefill(s, T0, out _).Ok, Is.False);
            Assert.That(s.Jobs, Is.Empty);
        }

        [Test]
        public void A_job_left_running_survives_a_save_round_trip_and_finishes_later()
        {
            var s = Fresh();
            for (int i = 0; i < 3; i++) RestedPool.Spend(s, T0);
            s.Gold = 1;
            Assert.That(RestedPool.BeginRefill(s, T0, out _).Ok, Is.True);
            var json = JsonConvert.SerializeObject(s, ContentLoader.Settings);
            var back = SaveMigrations.Migrate(JsonConvert.DeserializeObject<SaveData>(json, ContentLoader.Settings));
            Assert.That(back.Jobs.Count, Is.EqualTo(1));
            Assert.That(back.Rested.Points, Is.EqualTo(RestedPool.Max - 3));
            var done = Paywall.FinishReady(Real, back, T0.AddMinutes(1));
            Assert.That(done.Single().Ok, Is.True);
            Assert.That(back.Rested.Points, Is.EqualTo(RestedPool.Max));
        }

        [Test]
        public void Summoning_costs_the_paywall_price_and_the_hero_arrives_when_the_job_finishes()
        {
            var s = Fresh();
            s.Flags.Clear();
            var pick = Summon.Pool(Real, s).First(d => s.Hero(d.Id) == null);
            s.Gold = 10;
            Assert.That(Summon.Price(Real, s), Is.EqualTo(Paywall.Cost(PaywallKind.Summon, s)));
            Assert.That(Summon.BeginRecruit(Real, s, pick.Id, T0, out var job).Ok, Is.True);
            Assert.That(s.Gold, Is.EqualTo(9));
            Assert.That(s.Hero(pick.Id), Is.Null, "not until the summoning finishes");
            Assert.That(Paywall.Finish(Real, s, job, T0.AddSeconds(1)).Ok, Is.True);
            Assert.That(s.Hero(pick.Id), Is.Not.Null);
            Assert.That(s.Recruits, Is.EqualTo(1));
            Assert.That(Summon.Price(Real, s), Is.EqualTo(1), "flat — no longer rising per recruit");
            Assert.That(Summon.BeginRecruit(Real, s, pick.Id, T0, out _).Ok, Is.False, "already owned");
        }
    }
}
