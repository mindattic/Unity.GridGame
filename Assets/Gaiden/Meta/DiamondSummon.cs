using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// The Summon Circle's random draw for Diamonds (§25.10.2, GG-T343). Owner, 2026-11-07: "another way is to simply
    /// summon them randomly with Diamonds monetized currency". It spends <see cref="Price"/> Diamonds (<c>mat_diamond</c>,
    /// which still comes only from the LuckyDiamond paywall hook — no store) and draws one unowned hero from the same pool
    /// wanderers come from and go back to (<see cref="Wanderers.Pool"/>). The pick is seeded from the save and its recruit
    /// count, fixed when the summoning starts, and lands through the ordinary timed Summon job (<see cref="Paywall.Finish"/>),
    /// so it plays out over time with a reveal. The gold, deliberate summon is unchanged; bots never draw.
    /// </summary>
    public static class DiamondSummon
    {
        /// <summary>Diamonds per draw — the one tuning spot; 1 for now, in line with the 1 g · 1 s placeholder paywalls.</summary>
        public const int Price = 1;

        /// <summary>"1 Diamond · 1s" for the button.</summary>
        public static string Tag(SaveData s) => $"{Price} Diamond{(Price == 1 ? "" : "s")} · {Paywall.Duration(PaywallKind.Summon, s):0.#}s";

        /// <summary>Who could come: the wanderer pool (unowned heroes with no other way in).</summary>
        public static List<string> Pool(ContentDb c, SaveData s) => Wanderers.Pool(c, s);

        public static Result CanDraw(ContentDb c, SaveData s)
        {
            if (s.TestMode) return Result.Fail("Not in Test Mode");
            if (Pool(c, s).Count == 0) return Result.Fail("No one is left to answer");
            if (Paywall.Pending(s, PaywallKind.Summon) != null) return Result.Fail("A summoning is already under way");
            if (s.Count(Crafting.DiamondId) < Price) return Result.Fail("Not enough Diamonds");
            return Result.Success();
        }

        /// <summary>The hero the next draw brings for this save (deterministic: the save's creation time and its recruit count).</summary>
        public static string Pick(ContentDb c, SaveData s)
        {
            var pool = Pool(c, s);
            if (pool.Count == 0) return null;
            return pool[new Random(Wanderers.SeedFor(s, "diamond-summon", s.Recruits)).Next(pool.Count)];
        }

        /// <summary>Spends the Diamonds and starts the summoning; the hero arrives when <see cref="Paywall.Finish"/> lands it.</summary>
        public static Result Begin(ContentDb c, SaveData s, DateTime now, out PaywallJob job)
        {
            job = null;
            var ok = CanDraw(c, s);
            if (!ok.Ok) return ok;
            var pick = Pick(c, s);
            s.AddItem(Crafting.DiamondId, -Price);
            job = new PaywallJob { Kind = PaywallKind.Summon, Arg = pick, StartedUtc = now, Seconds = Paywall.Duration(PaywallKind.Summon, s), Diamonds = Price };
            s.Jobs.Add(job);
            return Result.Success();
        }
    }
}
