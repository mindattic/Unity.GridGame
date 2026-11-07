using System;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using Newtonsoft.Json;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>§25.10.2 (GG-T343): the Summon Circle's random draw for Diamonds, beside the gold, deliberate summon.</summary>
    public class SummonTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        private static SaveData WithDiamonds(string name, int diamonds)
        {
            var s = Progression.NewGame(Real, name);
            if (diamonds > 0) s.AddItem(Crafting.DiamondId, diamonds);
            return s;
        }

        [Test]
        public void A_draw_costs_one_diamond_and_no_gold()
        {
            Assert.That(DiamondSummon.Price, Is.EqualTo(1));
            var s = WithDiamonds("D1", 2);
            s.Gold = 500;
            Assert.That(DiamondSummon.Tag(s), Is.EqualTo("1 Diamond · 1s"));
            Assert.That(DiamondSummon.Begin(Real, s, T0, out var job).Ok, Is.True);
            Assert.That(s.Count(Crafting.DiamondId), Is.EqualTo(1));
            Assert.That(s.Gold, Is.EqualTo(500), "Diamonds, not gold");
            Assert.That(job.Kind, Is.EqualTo(PaywallKind.Summon));
            Assert.That(job.Diamonds, Is.EqualTo(1));
            Assert.That(job.Paid, Is.EqualTo(0));
        }

        [Test]
        public void It_draws_from_the_wanderer_pool_and_the_hero_arrives_when_the_summoning_lands()
        {
            var s = WithDiamonds("D2", 1);
            var pool = Wanderers.Pool(Real, s);
            int roster = s.Roster.Count;
            Assert.That(DiamondSummon.Begin(Real, s, T0, out var job).Ok, Is.True);
            Assert.That(pool, Does.Contain(job.Arg));
            Assert.That(Real.Summon.Pool, Does.Not.Contain(job.Arg), "not one of the gold summons");
            Assert.That(Paywall.Finish(Real, s, job, T0.AddSeconds(0.5)).Ok, Is.False, "plays out over time");
            Assert.That(s.Hero(job.Arg), Is.Null);
            var r = Paywall.Finish(Real, s, job, T0.AddSeconds(1));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Hero(job.Arg), Is.Not.Null);
            Assert.That(s.Roster.Count, Is.EqualTo(roster + 1));
            Assert.That(Wanderers.Pool(Real, s), Does.Not.Contain(job.Arg), "owned heroes leave the pool");
        }

        [Test]
        public void The_pick_is_seeded_from_the_save()
        {
            var a = WithDiamonds("D3", 1); var b = WithDiamonds("D3", 1);
            b.CreatedUtc = a.CreatedUtc;
            var pick = DiamondSummon.Pick(Real, a);
            Assert.That(pick, Is.Not.Null);
            Assert.That(DiamondSummon.Pick(Real, b), Is.EqualTo(pick));
            Assert.That(DiamondSummon.Pick(Real, a), Is.EqualTo(pick), "asking again doesn't change it");
            Assert.That(DiamondSummon.Begin(Real, a, T0, out var job).Ok, Is.True);
            Assert.That(job.Arg, Is.EqualTo(pick), "the summoning brings the hero picked");
        }

        [Test]
        public void No_diamond_no_draw_and_one_summoning_at_a_time()
        {
            var none = WithDiamonds("D4", 0);
            none.Gold = 999;
            Assert.That(DiamondSummon.Begin(Real, none, T0, out _).Message, Does.Contain("Diamonds"));
            Assert.That(none.Jobs, Is.Empty);

            var s = WithDiamonds("D5", 3);
            Assert.That(DiamondSummon.Begin(Real, s, T0, out _).Ok, Is.True);
            Assert.That(DiamondSummon.Begin(Real, s, T0, out _).Ok, Is.False, "a summoning is under way");
            Assert.That(s.Count(Crafting.DiamondId), Is.EqualTo(2));
            var gold = Summon.Pool(Real, s).First(d => s.Hero(d.Id) == null);
            s.Gold = 10;
            Assert.That(Summon.BeginRecruit(Real, s, gold.Id, T0, out _).Ok, Is.False, "the gold summon shares the circle");
        }

        [Test]
        public void A_fizzled_draw_gives_its_diamond_back()
        {
            var s = WithDiamonds("D6", 1);
            Assert.That(DiamondSummon.Begin(Real, s, T0, out var job).Ok, Is.True);
            Summon.Join(Real, s, job.Arg); // they turned up some other way in the meantime
            Assert.That(Paywall.Finish(Real, s, job, T0.AddSeconds(1)).Ok, Is.False);
            Assert.That(s.Count(Crafting.DiamondId), Is.EqualTo(1));
        }

        [Test]
        public void A_draw_left_running_survives_a_save_round_trip()
        {
            var s = WithDiamonds("D7", 1);
            Assert.That(DiamondSummon.Begin(Real, s, T0, out var job).Ok, Is.True);
            var json = JsonConvert.SerializeObject(s, ContentLoader.Settings);
            var back = SaveMigrations.Migrate(JsonConvert.DeserializeObject<SaveData>(json, ContentLoader.Settings));
            Assert.That(back.Jobs.Single().Diamonds, Is.EqualTo(1));
            Assert.That(Paywall.FinishReady(Real, back, T0.AddMinutes(1)).Single().Ok, Is.True);
            Assert.That(back.Hero(job.Arg), Is.Not.Null);
        }

        [Test]
        public void Test_mode_and_the_camp_routine_never_spend_diamonds()
        {
            var t = WithDiamonds("D8", 2); t.TestMode = true;
            Assert.That(DiamondSummon.Begin(Real, t, T0, out _).Ok, Is.False);
            var s = WithDiamonds("D9", 5);
            s.Gold = 100000;
            for (int i = 0; i < 5; i++) CampRoutine.Run(Real, s);
            Assert.That(s.Count(Crafting.DiamondId), Is.EqualTo(5));
        }

        [Test]
        public void The_gold_summon_is_unchanged()
        {
            var s = WithDiamonds("D10", 1);
            var pick = Summon.Pool(Real, s).First(d => s.Hero(d.Id) == null);
            s.Gold = 5;
            Assert.That(Summon.Recruit(Real, s, pick.Id).Ok, Is.True);
            Assert.That(s.Gold, Is.EqualTo(5 - Paywall.Cost(PaywallKind.Summon, s)));
            Assert.That(s.Count(Crafting.DiamondId), Is.EqualTo(1));
        }
    }
}
