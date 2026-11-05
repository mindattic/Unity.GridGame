using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>One mercenary under contract (GG-T233): paid upfront for <see cref="Term"/> battles at
    /// <see cref="Rate"/> gold each; <see cref="BattlesLeft"/> counts down on every battle fought, used or not.</summary>
    public sealed class MercContract
    {
        public string ClassId;
        public int BattlesLeft;
        public int Term;
        public int Rate;
        /// <summary>The "nearly up" reoffer has been answered for this term (renewing resets it).</summary>
        public bool Reoffered;
        public int Renewals;
    }

    /// <summary>The save's Mercenaries board (GG-T233): contracts, the board's refresh counter (it seeds the offers and
    /// turns over once per battle), this refresh's haggles and walk-offs, and departures not yet announced at Camp.</summary>
    public sealed class MercenarySave
    {
        public List<MercContract> Contracts = new List<MercContract>();
        public int Refresh;
        public int Hires;
        /// <summary>Class id → price adjustment in percent from this refresh's haggle (−20 dropped, 0 held, +15 raised).</summary>
        public Dictionary<string, int> Haggles = new Dictionary<string, int>();
        /// <summary>Mercs who walked off the board after a haggle; back on the next refresh.</summary>
        public HashSet<string> Walked = new HashSet<string>();
        /// <summary>Class ids whose contract ran out, waiting to be announced at Camp.</summary>
        public List<string> Departed = new List<string>();
    }

    public enum HaggleOutcome { Dropped, Held, Raised, Walked }

    /// <summary>
    /// Mercenaries for hire (GG-T233, BIBLE §25.10.1). A hero class hired for a negotiated number of battles, paid in
    /// full upfront (ordinary gold, not a paywall hook). The contract counts down on every battle fought — stage or
    /// Crucible — whether the merc fought in it or sat in reserve. With one battle left the merc reoffers at Camp:
    /// renew (a loyalty discount), finish the contract, or part ways. At zero they leave the roster, handing back any
    /// gear they were given, and can be hired again later. This is a contract system, separate from the dialogue
    /// vocabulary, which still can never remove a hero (AMENDMENTS 2026-11-05).
    /// </summary>
    public static class Mercenaries
    {
        public const int BoardSize = 3;
        /// <summary>Each longer contract length offered is this much cheaper per battle than the one before it.</summary>
        public const float TermStepDiscount = 0.10f;
        public const int HaggleDropPercent = -20, HaggleRaisePercent = 15;
        public const float LoyaltyStep = 0.10f, LoyaltyCap = 0.30f;

        public static bool Open(SaveData s) => s != null && (CampUnlocks.BountiesOpen(s) || s.Mercenaries.Contracts.Count > 0);

        public static MercContract Contract(SaveData s, string classId) =>
            s?.Mercenaries?.Contracts.Find(m => string.Equals(m.ClassId, classId, StringComparison.OrdinalIgnoreCase));

        public static bool IsMerc(SaveData s, string classId) => Contract(s, classId) != null;

        /// <summary>This refresh's offers: every merc in a seeded order, skipping heroes already in the roster.
        /// Stable until the next battle (<see cref="CountBattle"/>); a merc who walked off still holds their spot.</summary>
        public static List<MercenaryDef> Board(ContentDb c, SaveData s)
        {
            var rng = new Random(Seed(s, 0));
            var order = c.Mercenaries.Where(m => c.TryClass(m.Class) != null).ToList();
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            return order.Where(m => s.Hero(m.Class) == null && !Permadeath.IsLost(s, m.Class)).Take(BoardSize).ToList();
        }

        public static bool Walked(SaveData s, string classId) => s.Mercenaries.Walked.Contains(classId);
        public static bool Haggled(SaveData s, string classId) => s.Mercenaries.Haggles.ContainsKey(classId) || Walked(s, classId);
        public static int HaggleAdjust(SaveData s, string classId) => s.Mercenaries.Haggles.TryGetValue(classId, out var a) ? a : 0;

        /// <summary>Gold per battle for <paramref name="term"/>: the base rate, cheaper for each longer term offered,
        /// moved by a haggle (percent) and a loyalty discount (fraction).</summary>
        public static int PerBattle(MercenaryDef m, int term, int adjustPercent = 0, float loyalty = 0f)
        {
            var terms = m.Terms.OrderBy(t => t).ToList();
            int step = Math.Max(0, terms.IndexOf(term));
            double rate = m.Rate * (1.0 - TermStepDiscount * step) * (1.0 + adjustPercent / 100.0) * (1.0 - loyalty);
            return Math.Max(1, (int)Math.Round(rate));
        }

        /// <summary>The full upfront price to hire <paramref name="classId"/> for <paramref name="term"/> battles now.</summary>
        public static int Price(ContentDb c, SaveData s, string classId, int term)
        {
            var m = c.TryMercenary(classId);
            return m == null ? 0 : PerBattle(m, term, HaggleAdjust(s, classId)) * term;
        }

        public static Result Hire(ContentDb c, SaveData s, string classId, int term)
        {
            var m = c.TryMercenary(classId);
            if (m == null || !Board(c, s).Contains(m)) return Result.Fail("Not on the board");
            if (Walked(s, classId)) return Result.Fail("They've walked off for now");
            if (!m.Terms.Contains(term)) return Result.Fail("Not a term they offer");
            int price = Price(c, s, classId, term);
            if (s.Gold < price) return Result.Fail("Not enough gold");
            s.Gold -= price;
            s.Mercenaries.Hires++;
            Summon.Join(c, s, classId);
            s.Mercenaries.Contracts.Add(new MercContract { ClassId = m.Class, BattlesLeft = term, Term = term, Rate = PerBattle(m, term, HaggleAdjust(s, classId)) });
            return Result.Success($"{c.Class(classId).Name} signs on for {term} battles");
        }

        /// <summary>One haggle per merc per refresh. Seeded by the save, the refresh and the merc, so it replays the same
        /// way; the odds follow the merc's temperament.</summary>
        public static HaggleOutcome Haggle(ContentDb c, SaveData s, string classId, out string message)
        {
            var m = c.TryMercenary(classId);
            string name = c.TryClass(classId)?.Name ?? classId;
            if (m == null || Haggled(s, classId)) { message = $"{name} won't haggle twice"; return HaggleOutcome.Held; }
            var (drop, hold, raise) = Odds(m.Temperament);
            double roll = new Random(Seed(s, StableHash(m.Class))).NextDouble();
            HaggleOutcome o = roll < drop ? HaggleOutcome.Dropped : roll < drop + hold ? HaggleOutcome.Held : roll < drop + hold + raise ? HaggleOutcome.Raised : HaggleOutcome.Walked;
            switch (o)
            {
                case HaggleOutcome.Dropped: s.Mercenaries.Haggles[m.Class] = HaggleDropPercent; message = $"{name} knocks {-HaggleDropPercent}% off"; break;
                case HaggleOutcome.Held: s.Mercenaries.Haggles[m.Class] = 0; message = $"{name} holds firm"; break;
                case HaggleOutcome.Raised: s.Mercenaries.Haggles[m.Class] = HaggleRaisePercent; message = $"Insulted, {name} raises the price {HaggleRaisePercent}%"; break;
                default: s.Mercenaries.Walked.Add(m.Class); message = $"{name} walks off. Maybe after the next battle"; break;
            }
            return o;
        }

        /// <summary>Chance to drop, hold, raise; the rest walks off.</summary>
        public static (double drop, double hold, double raise) Odds(MercTemperament t) => t switch
        {
            MercTemperament.Easygoing => (0.55, 0.30, 0.10),
            MercTemperament.Greedy => (0.25, 0.25, 0.30),
            _ => (0.40, 0.30, 0.20),
        };

        /// <summary>Called once per committed battle (<see cref="BattleFactory.Commit"/>): every contract loses a battle,
        /// used or not, and the board turns over. Departures settle before the next battle or at Camp
        /// (<see cref="Settle"/>), so the last battle's XP and gear wear are written first.</summary>
        public static void CountBattle(SaveData s)
        {
            var b = s.Mercenaries;
            b.Refresh++;
            b.Haggles.Clear();
            b.Walked.Clear();
            foreach (var k in b.Contracts) k.BattlesLeft = Math.Max(0, k.BattlesLeft - 1);
        }

        /// <summary>Mercs whose contract has run out leave: gear back to the bag, out of the roster and party. Idempotent.</summary>
        public static List<string> Settle(ContentDb c, SaveData s)
        {
            var gone = new List<string>();
            foreach (var k in s.Mercenaries.Contracts.Where(k => k.BattlesLeft <= 0).ToList())
            {
                Depart(c, s, k);
                gone.Add(k.ClassId);
            }
            return gone;
        }

        /// <summary>The next contract down to its last battle whose reoffer hasn't been answered, or null.</summary>
        public static MercContract PendingRenewal(SaveData s) =>
            s?.Mercenaries?.Contracts.FirstOrDefault(k => k.BattlesLeft == 1 && !k.Reoffered && s.Hero(k.ClassId) != null);

        public static float Loyalty(MercContract k) => Math.Min(LoyaltyCap, LoyaltyStep * (k.Renewals + 1));

        public static int RenewalPrice(ContentDb c, SaveData s, string classId, int term)
        {
            var m = c.TryMercenary(classId); var k = Contract(s, classId);
            return m == null || k == null ? 0 : PerBattle(m, term, 0, Loyalty(k)) * term;
        }

        /// <summary>Renegotiate: <paramref name="term"/> more battles on top of the one left, at the loyalty rate.</summary>
        public static Result Renew(ContentDb c, SaveData s, string classId, int term)
        {
            var m = c.TryMercenary(classId); var k = Contract(s, classId);
            if (m == null || k == null) return Result.Fail("No such contract");
            if (!m.Terms.Contains(term)) return Result.Fail("Not a term they offer");
            int price = RenewalPrice(c, s, classId, term);
            if (s.Gold < price) return Result.Fail("Not enough gold");
            s.Gold -= price;
            k.Rate = PerBattle(m, term, 0, Loyalty(k));
            k.Renewals++;
            k.BattlesLeft += term;
            k.Term = term;
            k.Reoffered = false;
            return Result.Success($"{c.Class(classId).Name} renews for {term} more battles");
        }

        /// <summary>Keep the merc for the last battle already paid for, then let them go.</summary>
        public static void FinishContract(SaveData s, string classId) { var k = Contract(s, classId); if (k != null) k.Reoffered = true; }

        public static Result PartWays(ContentDb c, SaveData s, string classId)
        {
            var k = Contract(s, classId);
            if (k == null) return Result.Fail("No such contract");
            Depart(c, s, k);
            return Result.Success($"{c.Class(classId).Name} parts ways with the caravan");
        }

        /// <summary>Departures not yet announced; announcing clears them.</summary>
        public static List<string> TakeDepartureNotices(SaveData s)
        {
            var list = new List<string>(s.Mercenaries.Departed);
            s.Mercenaries.Departed.Clear();
            return list;
        }

        private static void Depart(ContentDb c, SaveData s, MercContract k)
        {
            s.Mercenaries.Contracts.Remove(k);
            var h = s.Hero(k.ClassId);
            if (h == null) return;
            // Gear the caravan handed them comes back to the bag with its wear (a fresh hire arrives with none).
            var slots = new List<EquipmentSlot> { EquipmentSlot.Weapon };
            slots.AddRange(BattleEngine.ArmorSlots);
            slots.AddRange(Equipment.JewelrySlots);
            foreach (var slot in slots)
                if (Equipment.Equipped(h, slot) != null) Equipment.Unequip(c, s, h, slot);
            s.Roster.Remove(h);
            s.Party.RemoveAll(id => string.Equals(id, k.ClassId, StringComparison.OrdinalIgnoreCase));
            if (s.Party.Count == 0 && s.Roster.Count > 0) s.Party.Add(s.Roster[0].ClassId);
            s.Mercenaries.Departed.Add(h.ClassId);
        }

        private static int Seed(SaveData s, int salt) =>
            unchecked((int)((uint)s.CreatedUtc.Ticks * 2654435761u ^ (uint)s.Mercenaries.Refresh * 747796405u ^ (uint)salt * 2891336453u));

        private static int StableHash(string text)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in text.ToLowerInvariant()) h = h * 31 + ch;
                return h;
            }
        }
    }
}
