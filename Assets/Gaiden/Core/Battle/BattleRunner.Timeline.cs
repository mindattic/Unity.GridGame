using System;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        private const int MaxShieldTicks = 2000;
        private int ticksSinceReport;

        /// <summary>Runs the shared clock forward <paramref name="ticks"/> ticks, resolving every arrival in order.</summary>
        private void AdvanceClock(int ticks)
        {
            for (int i = 0; i < ticks && !S.IsOver; i++) Tick(out _);
            ReportAdvance();
        }

        /// <summary>Shield: run the clock until an enemy turn has resolved (or nothing can ever arrive).</summary>
        private void AdvanceUntilEnemyTurn()
        {
            for (int i = 0; i < MaxShieldTicks && !S.IsOver; i++)
            {
                Tick(out bool turnResolved);
                if (turnResolved) break;
            }
            ReportAdvance();
        }

        private void ReportAdvance()
        {
            if (ticksSinceReport <= 0) return;
            var e = new TimelineAdvancedEvent { Ticks = ticksSinceReport };
            foreach (var t in S.Timeline) e.U[t.Id] = t.U;
            Emit(e);
            ticksSinceReport = 0;
        }

        private int ZoneRatePerTick => Math.Max(1, (BattleConfig.BarLength - BattleConfig.ZoneStart) / BattleConfig.ZoneCrossingTicks);

        private int RateFor(Unit owner, TimelineEntry e)
        {
            float mult = BattleEngine.TimelineScale(S, owner);
            float rate = e.Ip >= BattleConfig.ZoneStart
                ? ZoneRatePerTick
                : owner.Effective.Speed * BattleConfig.OpenRatePerSpeed;
            return Math.Max(1, (int)Math.Round(rate * mult));
        }

        private void Tick(out bool enemyTurnResolved)
        {
            enemyTurnResolved = false;
            S.Clock++;
            ticksSinceReport++;

            if (S.PendingSpawns.Count > 0) { ResolvePendingSpawns(); CheckTilesStrict("reinforcements"); }
            if (S.IsOver) return;

            foreach (var e in S.Timeline)
            {
                var owner = S.UnitById(e.OwnerId);
                if (owner == null || !owner.Alive) continue;
                if (e.Kind == TimelineKind.Turn)
                {
                    if (owner.IsTimelineHalted) continue;
                    if (e.StunTicks > 0) { e.StunTicks--; continue; }
                    if (e.Ip < BattleConfig.BarLength) e.Ip = Math.Min(BattleConfig.BarLength, e.Ip + RateFor(owner, e));
                }
                else
                {
                    if (owner.IsTimelineHalted) continue;
                    if (e.RemainingTicks > 0) e.RemainingTicks--;
                }
            }

            TickTimedBuffs(1);
            if (S.IsOver) return;

            // Arrivals this tick, resolved in arrival order: casts that hit 0, then full turn icons; ties by id.
            for (int guard = 0; guard < 64 && !S.IsOver; guard++)
            {
                TimelineEntry next = null;
                foreach (var e in S.Timeline)
                {
                    bool arrived = e.Kind == TimelineKind.Cast ? e.RemainingTicks <= 0 : e.Ip >= BattleConfig.BarLength;
                    if (!arrived) continue;
                    if (next == null || Earlier(e, next)) next = e;
                }
                if (next == null) break;
                ReportAdvance();
                if (next.Kind == TimelineKind.Cast) { ResolveCast(next); CheckTilesStrict("cast " + next.AbilityId); }
                else
                {
                    next.Ip = 0;
                    var owner = S.UnitById(next.OwnerId);
                    if (owner != null && owner.Alive) { RunEnemyTurn(owner); RuleBreakAfterTurn(owner); enemyTurnResolved = true; CheckTilesStrict("turn of " + owner); }
                }
            }
        }

        private static bool Earlier(TimelineEntry a, TimelineEntry b)
        {
            if (a.Kind != b.Kind) return a.Kind == TimelineKind.Cast;
            return a.Id < b.Id;
        }

        /// <summary>Time-banked orbs (§3.1.8): drag-timer seconds left at the last drop convert to orbs at the enemy handoff.</summary>
        private void MintTimeBank()
        {
            if (S.PendingTimeBankSeconds <= 0f) return;
            int n = Math.Min(BattleConfig.MaxTimeBankOrbs, (int)(S.PendingTimeBankSeconds / BattleConfig.SecondsPerTimeBankOrb));
            S.PendingTimeBankSeconds = 0f;
            if (n <= 0) return;
            for (int i = 0; i < n; i++) MintOrb(-1, "time bank");
            Announce($"Banked {n} mana");
        }
    }
}
