using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T233: mercenaries hired for a negotiated number of battles, counted down whether used or not,
    /// reoffering when nearly up, and leaving (gear handed back) when the contract runs out.</summary>
    public class MercenaryTests
    {
        private static SaveData NewSave(string name = "Merc")
        {
            var s = Progression.NewGame(Real, name);
            s.CreatedUtc = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
            s.Gold = 100000;
            return s;
        }

        private static void FightOne(ContentDb c, SaveData s, int seed)
        {
            var st = BattleEngine.Create(c, BattleFactory.Build(c, s, c.CampaignStages()[0].Id, seed), new List<BattleEvent>());
            st.Phase = BattlePhase.Victory;
            BattleFactory.Commit(c, s, st);
        }

        [Test]
        public void Mercenary_content_is_valid_heroes_kept_apart_from_the_summon_pool()
        {
            var c = Real;
            Assert.That(c.Mercenaries, Is.Not.Empty);
            var problems = new List<string>();
            MercenaryContent.Validate(c, problems);
            Assert.That(problems, Is.Empty);
            foreach (var m in c.Mercenaries)
            {
                Assert.That(c.Class(m.Class).Team, Is.EqualTo(Team.Hero));
                Assert.That(c.Summon.Pool, Does.Not.Contain(m.Class), m.Class);
                var odds = Mercenaries.Odds(m.Temperament);
                Assert.That(odds.drop + odds.hold + odds.raise, Is.LessThanOrEqualTo(1.0));
            }
        }

        [Test]
        public void Longer_contracts_are_cheaper_per_battle()
        {
            foreach (var m in Real.Mercenaries)
            {
                var terms = m.Terms.OrderBy(t => t).ToList();
                for (int i = 1; i < terms.Count; i++)
                    Assert.That(Mercenaries.PerBattle(m, terms[i]), Is.LessThan(Mercenaries.PerBattle(m, terms[i - 1])), m.Class);
            }
        }

        [Test]
        public void The_board_is_seeded_skips_owned_heroes_and_turns_over_each_battle()
        {
            var c = Real; var s = NewSave();
            var a = Mercenaries.Board(c, s).Select(m => m.Class).ToList();
            Assert.That(a.Count, Is.EqualTo(Math.Min(Mercenaries.BoardSize, c.Mercenaries.Count)));
            Assert.That(Mercenaries.Board(c, s).Select(m => m.Class), Is.EqualTo(a), "reopening never rerolls");
            Assert.That(Mercenaries.Hire(c, s, a[0], c.TryMercenary(a[0]).Terms.Min()).Ok, Is.True);
            Assert.That(Mercenaries.Board(c, s).Select(m => m.Class), Does.Not.Contain(a[0]));

            var seen = new HashSet<string>(a);
            for (int i = 0; i < 12; i++) { Mercenaries.CountBattle(s); foreach (var m in Mercenaries.Board(c, s)) seen.Add(m.Class); }
            Assert.That(seen.Count, Is.GreaterThan(Mercenaries.BoardSize), "the board turns over");
        }

        [Test]
        public void Hiring_pays_the_whole_contract_upfront_and_joins_at_the_rosters_experience()
        {
            var c = Real; var s = NewSave();
            foreach (var h in s.Roster) h.TotalXp = 5000;
            var m = Mercenaries.Board(c, s)[0];
            int term = m.Terms.Max();
            int price = Mercenaries.Price(c, s, m.Class, term);
            Assert.That(price, Is.EqualTo(Mercenaries.PerBattle(m, term) * term));
            int gold = s.Gold;
            var r = Mercenaries.Hire(c, s, m.Class, term);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(s.Gold, Is.EqualTo(gold - price));
            Assert.That(s.Hero(m.Class).TotalXp, Is.EqualTo(5000));
            Assert.That(s.Party, Does.Contain(m.Class));
            var k = Mercenaries.Contract(s, m.Class);
            Assert.That(k.BattlesLeft, Is.EqualTo(term));
            Assert.That(Mercenaries.IsMerc(s, m.Class), Is.True);
            Assert.That(Mercenaries.IsMerc(s, s.Roster[0].ClassId), Is.False);

            var poor = NewSave("Poor"); poor.Gold = 0;
            Assert.That(Mercenaries.Hire(c, poor, Mercenaries.Board(c, poor)[0].Class, 3).Ok, Is.False);
            Assert.That(Mercenaries.Hire(c, s, Mercenaries.Board(c, s)[0].Class, 4).Ok, Is.False, "only the offered terms");
        }

        [Test]
        public void A_contract_counts_down_every_battle_even_in_reserve_then_the_merc_leaves_with_gear_returned()
        {
            var c = Real; var s = NewSave();
            var m = Mercenaries.Board(c, s)[0];
            int term = m.Terms.Min();
            Assert.That(Mercenaries.Hire(c, s, m.Class, term).Ok, Is.True);
            Assert.That(Party.Toggle(s, m.Class).Ok, Is.True, "benched");
            Assert.That(s.Party, Does.Not.Contain(m.Class));
            var weapon = c.Items.Values.First(i => i.Type == ItemType.Equipment && i.Slot == EquipmentSlot.Weapon);
            var h = s.Hero(m.Class);
            h.WeaponId = weapon.Id; h.WeaponDurability = weapon.Durability;
            int bagBefore = s.Count(weapon.Id);

            for (int i = 0; i < term - 1; i++) FightOne(c, s, 100 + i);
            Assert.That(Mercenaries.Contract(s, m.Class).BattlesLeft, Is.EqualTo(1));
            Assert.That(Mercenaries.PendingRenewal(s)?.ClassId, Is.EqualTo(m.Class), "reoffer when about to leave");
            Mercenaries.FinishContract(s, m.Class);
            Assert.That(Mercenaries.PendingRenewal(s), Is.Null);

            FightOne(c, s, 200);
            Assert.That(s.Hero(m.Class), Is.Not.Null, "the last battle settles before the next one, not mid-commit");
            var gone = Mercenaries.Settle(c, s);
            Assert.That(gone, Is.EqualTo(new[] { m.Class }));
            Assert.That(s.Hero(m.Class), Is.Null);
            Assert.That(s.Party, Does.Not.Contain(m.Class));
            Assert.That(s.Count(weapon.Id), Is.EqualTo(bagBefore + 1), "gear handed back");
            Assert.That(Mercenaries.TakeDepartureNotices(s), Is.EqualTo(new[] { m.Class }));
            Assert.That(Mercenaries.TakeDepartureNotices(s), Is.Empty);
            Assert.That(Mercenaries.Settle(c, s), Is.Empty, "idempotent");

            // Gone for now, not for good: they come back to the board and can be hired again.
            bool back = false;
            for (int i = 0; i < 30 && !back; i++) { Mercenaries.CountBattle(s); back = Mercenaries.Board(c, s).Any(x => x.Class == m.Class); }
            Assert.That(back, Is.True);
            Assert.That(Mercenaries.Hire(c, s, m.Class, term).Ok, Is.True);
        }

        [Test]
        public void A_spent_contract_never_fights_another_battle()
        {
            var c = Real; var s = NewSave();
            var m = Mercenaries.Board(c, s)[0];
            Assert.That(Mercenaries.Hire(c, s, m.Class, m.Terms.Min()).Ok, Is.True);
            for (int i = 0; i < m.Terms.Min(); i++) FightOne(c, s, 300 + i);
            var setup = BattleFactory.Build(c, s, c.CampaignStages()[0].Id, 9);
            Assert.That(setup.Party.Select(p => p.ClassId), Does.Not.Contain(m.Class));
            Assert.That(s.Hero(m.Class), Is.Null);
        }

        [Test]
        public void Renewing_extends_at_a_loyalty_discount_and_parting_ways_leaves_now()
        {
            var c = Real; var s = NewSave();
            var m = Mercenaries.Board(c, s)[0];
            int term = m.Terms.Min();
            Assert.That(Mercenaries.Hire(c, s, m.Class, term).Ok, Is.True);
            var k = Mercenaries.Contract(s, m.Class);
            k.BattlesLeft = 1;
            int renewal = Mercenaries.RenewalPrice(c, s, m.Class, term);
            Assert.That(renewal, Is.LessThan(Mercenaries.PerBattle(m, term) * term));
            int gold = s.Gold;
            Assert.That(Mercenaries.Renew(c, s, m.Class, term).Ok, Is.True);
            Assert.That(s.Gold, Is.EqualTo(gold - renewal));
            Assert.That(k.BattlesLeft, Is.EqualTo(1 + term));
            Assert.That(k.Renewals, Is.EqualTo(1));
            Assert.That(Mercenaries.Loyalty(k), Is.GreaterThan(Mercenaries.LoyaltyStep));
            for (int i = 0; i < 10; i++) k.Renewals++;
            Assert.That(Mercenaries.Loyalty(k), Is.EqualTo(Mercenaries.LoyaltyCap));

            Assert.That(Mercenaries.PartWays(c, s, m.Class).Ok, Is.True);
            Assert.That(s.Hero(m.Class), Is.Null);
            Assert.That(Mercenaries.Contract(s, m.Class), Is.Null);
        }

        [Test]
        public void Haggling_is_once_a_refresh_seeded_and_a_walk_off_blocks_the_hire_until_the_next_battle()
        {
            var c = Real;
            var outcomes = new HashSet<HaggleOutcome>();
            for (int day = 0; day < 40; day++)
            {
                var s = NewSave(); s.CreatedUtc = s.CreatedUtc.AddDays(day);
                var twin = NewSave(); twin.CreatedUtc = s.CreatedUtc;
                var m = Mercenaries.Board(c, s)[0];
                int before = Mercenaries.Price(c, s, m.Class, m.Terms.Min());
                var o = Mercenaries.Haggle(c, s, m.Class, out _);
                Assert.That(Mercenaries.Haggle(c, twin, m.Class, out _), Is.EqualTo(o), "same save, same answer");
                outcomes.Add(o);
                Assert.That(Mercenaries.Haggled(s, m.Class), Is.True);
                int after = Mercenaries.Price(c, s, m.Class, m.Terms.Min());
                if (o == HaggleOutcome.Dropped) Assert.That(after, Is.LessThan(before));
                if (o == HaggleOutcome.Raised) Assert.That(after, Is.GreaterThan(before));
                if (o == HaggleOutcome.Held) Assert.That(after, Is.EqualTo(before));
                if (o == HaggleOutcome.Walked) Assert.That(Mercenaries.Hire(c, s, m.Class, m.Terms.Min()).Ok, Is.False);
                int adj = Mercenaries.HaggleAdjust(s, m.Class);
                Mercenaries.Haggle(c, s, m.Class, out _);
                Assert.That(Mercenaries.HaggleAdjust(s, m.Class), Is.EqualTo(adj), "no second haggle");
                Mercenaries.CountBattle(s);
                Assert.That(Mercenaries.Haggled(s, m.Class), Is.False, "the next battle clears it");
            }
            Assert.That(outcomes, Does.Contain(HaggleOutcome.Dropped));
            Assert.That(outcomes.Count, Is.GreaterThan(1));
        }

        [Test]
        public void An_older_save_without_a_board_migrates()
        {
            var s = Progression.NewGame(Real, "Old");
            s.Mercenaries = null;
            SaveMigrations.Migrate(s, Real);
            Assert.That(s.Mercenaries, Is.Not.Null);
            Assert.That(s.Mercenaries.Contracts, Is.Empty);
        }
    }
}
