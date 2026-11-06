using System;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        private const int MaxShieldTicks = 2000;
        private int ticksSinceReport;

        /// <summary>GG-T308: an enemy turn resolved (or the window was spent) during this command — the party's window is over.</summary>
        private bool windowOver;

        /// <summary>Runs the shared clock forward <paramref name="ticks"/> ticks, resolving every arrival in order;
        /// <paramref name="stopAtEnemyTurn"/> ends the advance at the first enemy turn (or a new wave) — the window is over.</summary>
        private void AdvanceClock(int ticks, bool stopAtEnemyTurn = false)
        {
            int wave = S.Wave;
            for (int i = 0; i < ticks && !S.IsOver; i++)
            {
                Tick(out bool turnResolved);
                if (turnResolved) windowOver = true;
                if (stopAtEnemyTurn && (turnResolved || S.Wave != wave)) break;
            }
            ReportAdvance();
        }

        /// <summary>Run the clock until an enemy turn has resolved (or nothing can ever arrive).</summary>
        private void AdvanceUntilEnemyTurn()
        {
            int wave = S.Wave;
            for (int i = 0; i < MaxShieldTicks && !S.IsOver; i++)
            {
                Tick(out bool turnResolved);
                if (turnResolved) { windowOver = true; break; }
                if (S.Wave != wave) break;
            }
            ReportAdvance();
        }

        /// <summary>
        /// GG-T308 (§2.0): the party's window is spent (every hero acted, or Guard gave it up) — the clock runs on to the
        /// next enemy turn; with no enemy icon able to arrive at all, the window is the fixed ActionTicks instead.
        /// </summary>
        private void SpendWindow()
        {
            if (BattleEngine.TurnTicks(S) > 0) AdvanceUntilEnemyTurn();
            else { AdvanceClock(BattleConfig.ActionTicks, stopAtEnemyTurn: true); windowOver = true; }
        }

        private void ReportAdvance()
        {
            if (ticksSinceReport <= 0) return;
            var e = new TimelineAdvancedEvent { Ticks = ticksSinceReport };
            foreach (var t in S.Timeline) e.U[t.Id] = BattleEngine.BarU(S, t);
            Emit(e);
            ticksSinceReport = 0;
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
                    if (e.Ip < BattleConfig.BarLength) e.Ip = Math.Min(BattleConfig.BarLength, e.Ip + BattleEngine.TurnRatePerTick(S, owner)); // one linear rate, no Zone (GG-T286)
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
                    // A tamed or domesticated monster (GG-T303) acts on its own icon but doesn't end the party's window.
                    if (owner != null && owner.Alive && owner.IsIndependent) { RunAllyTurn(owner); CheckTilesStrict("ally turn of " + owner); }
                    else if (owner != null && owner.Alive) { RunEnemyTurn(owner); RuleBreakAfterTurn(owner); enemyTurnResolved = true; CheckTilesStrict("turn of " + owner); }
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
