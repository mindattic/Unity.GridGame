using System;

namespace GridGame.Meta
{
    public sealed class RestedSave
    {
        public int Points = RestedPool.Max;
        /// <summary>When the next regeneration interval started counting.</summary>
        public DateTime RegenUtc = DateTime.UtcNow;
    }

    /// <summary>Rested (§25.18, GG-T232): one party-wide energy pool. Every stage launch (the Crucible too) spends
    /// <see cref="LaunchCost"/>; it comes back one point per <see cref="RegenEvery"/> of real time, or all at once
    /// through the RestedRefill paywall.</summary>
    public static class RestedPool
    {
        public const int Max = 10;
        public const int LaunchCost = 1;
        public static readonly TimeSpan RegenEvery = TimeSpan.FromMinutes(10);

        /// <summary>Set by the app for Test Mode, -demo and automation runs, so bots are never blocked.</summary>
        public static bool Unlimited;

        public static bool Exempt(SaveData s) => Unlimited || s == null || s.TestMode;

        public static void Regen(SaveData s, DateTime now)
        {
            var r = s.Rested;
            if (now < r.RegenUtc || r.Points >= Max) { r.RegenUtc = now; return; }
            int gained = (int)((now - r.RegenUtc).Ticks / RegenEvery.Ticks);
            if (gained <= 0) return;
            r.Points = Math.Min(Max, r.Points + gained);
            r.RegenUtc = r.Points >= Max ? now : r.RegenUtc + TimeSpan.FromTicks(RegenEvery.Ticks * gained);
        }

        public static int Points(SaveData s, DateTime now) { Regen(s, now); return s.Rested.Points; }

        /// <summary>Time until the next point comes back (zero when full).</summary>
        public static TimeSpan NextIn(SaveData s, DateTime now)
        {
            Regen(s, now);
            return s.Rested.Points >= Max ? TimeSpan.Zero : s.Rested.RegenUtc + RegenEvery - now;
        }

        public static bool CanLaunch(SaveData s, DateTime now, out string why)
        {
            why = null;
            if (Exempt(s) || Points(s, now) >= LaunchCost) return true;
            var wait = NextIn(s, now);
            why = $"The party is exhausted — no Rested left. Next point in {(int)wait.TotalMinutes}:{wait.Seconds:00}.";
            return false;
        }

        /// <summary>Spends a launch's Rested. Exempt saves spend nothing.</summary>
        public static Result Spend(SaveData s, DateTime now)
        {
            if (Exempt(s)) return Result.Success();
            if (!CanLaunch(s, now, out var why)) return Result.Fail(why);
            bool wasFull = s.Rested.Points >= Max;
            s.Rested.Points -= LaunchCost;
            if (wasFull) s.Rested.RegenUtc = now;
            return Result.Success();
        }

        public static Result BeginRefill(SaveData s, DateTime now, out PaywallJob job)
        {
            job = null;
            if (Points(s, now) >= Max) return Result.Fail("The party is already rested");
            return Paywall.Start(s, PaywallKind.RestedRefill, null, now, out job);
        }

        public static void Fill(SaveData s, DateTime now)
        {
            s.Rested.Points = Max;
            s.Rested.RegenUtc = now;
        }
    }
}
