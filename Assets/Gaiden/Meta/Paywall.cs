using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    public enum PaywallKind { Summon, LuckyDiamond, RestedRefill }

    /// <summary>A paid, timed job in progress (§25.18): gold is taken at the start, the effect lands when the time is up.
    /// Kept in the save so a long job survives a restart.</summary>
    public sealed class PaywallJob
    {
        public PaywallKind Kind;
        /// <summary>What the job is for (a hero class for Summon; free-form for the other kinds).</summary>
        public string Arg;
        public DateTime StartedUtc;
        public double Seconds;
        public int Paid;
    }

    /// <summary>The paywall hooks (§25.18, GG-T232): every premium action prices and times itself here.</summary>
    public static class Paywall
    {
        // Future monetization paywalls: one table, every hook 1 g / 1 s until the owner says otherwise.
        private static readonly Dictionary<PaywallKind, (int gold, double seconds)> Table = new Dictionary<PaywallKind, (int, double)>
        {
            [PaywallKind.Summon] = (1, 1.0),
            [PaywallKind.LuckyDiamond] = (1, 1.0),
            [PaywallKind.RestedRefill] = (1, 1.0),
        };

        public static int Cost(PaywallKind kind, SaveData s) => Table[kind].gold;

        public static double Duration(PaywallKind kind, SaveData s) => Table[kind].seconds;

        /// <summary>"1g · 1s" — the price tag every paywall button shows.</summary>
        public static string Tag(PaywallKind kind, SaveData s) => $"{Cost(kind, s)}g · {Duration(kind, s):0.#}s";

        public static PaywallJob Pending(SaveData s, PaywallKind kind) => s?.Jobs.FirstOrDefault(j => j.Kind == kind);

        /// <summary>Takes the gold and starts the clock. One job of each kind at a time.</summary>
        public static Result Start(SaveData s, PaywallKind kind, string arg, DateTime now, out PaywallJob job)
        {
            job = null;
            if (Pending(s, kind) != null) return Result.Fail("Already under way");
            int cost = Cost(kind, s);
            if (s.Gold < cost) return Result.Fail("Not enough gold");
            s.Gold -= cost;
            job = new PaywallJob { Kind = kind, Arg = arg, StartedUtc = now, Seconds = Duration(kind, s), Paid = cost };
            s.Jobs.Add(job);
            return Result.Success();
        }

        public static double Progress(PaywallJob job, DateTime now) =>
            job.Seconds <= 0 ? 1 : Math.Max(0, Math.Min(1, (now - job.StartedUtc).TotalSeconds / job.Seconds));

        public static bool Ready(PaywallJob job, DateTime now) => Progress(job, now) >= 1;

        /// <summary>Lands a finished job's effect and removes it; refuses one still running.</summary>
        public static Result Finish(ContentDb c, SaveData s, PaywallJob job, DateTime now)
        {
            if (job == null || !s.Jobs.Contains(job)) return Result.Fail("No such job");
            if (!Ready(job, now)) return Result.Fail("Not done yet");
            s.Jobs.Remove(job);
            switch (job.Kind)
            {
                case PaywallKind.Summon: return Summon.Arrive(c, s, job.Arg);
                case PaywallKind.RestedRefill: RestedPool.Fill(s, now); return Result.Success("The party is rested.");
                case PaywallKind.LuckyDiamond:
                    var diamond = c.TryItem(Crafting.DiamondId);
                    if (diamond == null) return Result.Fail("No such item");
                    if (s.Count(diamond.Id) >= diamond.MaxStack) { s.Gold += job.Paid; return Result.Fail("Can't carry more — refunded"); }
                    s.AddItem(diamond.Id, 1);
                    return Result.Success($"A {diamond.Name} for the forge.");
                default: return Result.Success();
            }
        }

        /// <summary>Finishes every job whose time is up (e.g. ones left running when the app closed).</summary>
        public static List<Result> FinishReady(ContentDb c, SaveData s, DateTime now) =>
            s.Jobs.Where(j => Ready(j, now)).ToList().Select(j => Finish(c, s, j, now)).ToList();
    }
}
