using System;
using System.Collections.Generic;
using GridGame.Core.Content;

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
                    if (e.RecoverTicks > 0) { e.RecoverTicks--; continue; } // off the bar, recovering (GG-T318)
                    if (e.StunTicks > 0) { e.StunTicks--; continue; }
                    if (e.Ip < BattleConfig.BarLength) e.Ip = Math.Min(BattleConfig.BarLength, e.Ip + BattleEngine.TurnRatePerTick(S, owner)); // one rate for everyone (GG-T318)
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
                    recoverActorId = owner?.Id ?? -1;
                    turnRecoverSeconds = 0f;
                    // A tamed or domesticated monster (GG-T303) acts on its own icon but doesn't end the party's window.
                    if (owner != null && owner.Alive && owner.IsIndependent) { RunAllyTurn(owner); CheckTilesStrict("ally turn of " + owner); }
                    else if (owner != null && owner.Alive) { RunEnemyTurn(owner); RuleBreakAfterTurn(owner); enemyTurnResolved = true; CheckTilesStrict("turn of " + owner); }
                    recoverActorId = -1;
                    // GG-T318: the turn is done, so its recovery cost is known. Off the bar, then back on at its Speed's entry.
                    if (owner != null && owner.Alive && S.Timeline.Contains(next))
                        ScheduleReentry(next, owner, BattleConfig.RecoverBaseSeconds + turnRecoverSeconds, space: !owner.IsIndependent);
                }
            }
        }

        /// <summary>GG-T318: the unit whose turn is resolving, and the largest RecoverSeconds keyword it used this turn.</summary>
        private int recoverActorId = -1;
        private float turnRecoverSeconds;

        /// <summary>GG-T318: a RecoverSeconds keyword used by the unit whose turn is resolving adds to that turn's recovery.</summary>
        private void NoteRecover(Unit caster, AbilityDef a)
        {
            if (caster != null && a != null && caster.Id == recoverActorId && a.RecoverSeconds > turnRecoverSeconds)
                turnRecoverSeconds = a.RecoverSeconds;
        }

        private static int SecondsAsTicks(float seconds) => (int)Math.Round(Math.Max(0f, seconds) * BattleConfig.TicksPerSecond);

        /// <summary>
        /// GG-T318 (§2.12): puts a turn icon back on its cycle. It sits off the bar for <paramref name="recoverSeconds"/>
        /// (± the difficulty row's jitter, never acting sooner than <paramref name="floorTicks"/> from now), then re-enters
        /// at its Speed's entry (<see cref="BattleEngine.EntryIp"/>). With <paramref name="space"/>, the spacer
        /// (<see cref="TimelineSpacing"/>) may hold it longer so its turn lands clear of every other foe's. Entries in
        /// <paramref name="unscheduled"/> are not counted as others yet.
        /// </summary>
        private void ScheduleReentry(TimelineEntry e, Unit u, float recoverSeconds, bool space, int floorTicks = 0, ISet<TimelineEntry> unscheduled = null)
        {
            e.Ip = BattleEngine.EntryIp(u);
            e.StunTicks = 0;
            e.RecoverTicks = 0;
            int travel = Math.Max(0, BattleEngine.TicksUntilAct(S, e, ignoreHalt: true));
            var row = BattleEngine.SpacingRow(S);
            int recover = SecondsAsTicks(recoverSeconds);
            int jitter = SecondsAsTicks(row.JitterSeconds);
            if (jitter > 0) recover += S.Rng.Range(-jitter, jitter + 1);
            recover = Math.Max(0, Math.Max(recover, floorTicks - travel));
            if (space && row.MinGapSeconds > 0f)
            {
                var others = new List<int>();
                foreach (var o in S.Timeline)
                {
                    if (o == e || (unscheduled != null && unscheduled.Contains(o))) continue;
                    var ou = S.UnitById(o.OwnerId);
                    if (ou == null || !ou.Alive || ou.Team != Team.Enemy || ou.IsIndependent) continue;
                    if (o.Kind == TimelineKind.Cast && !o.IsCharge) continue;
                    others.Add(BattleEngine.TicksUntilAct(S, o));
                }
                recover += TimelineSpacing.Delay(recover + travel, others, SecondsAsTicks(row.MinGapSeconds), SecondsAsTicks(row.MaxSpreadSeconds));
            }
            e.RecoverTicks = recover;
            e.RecoverTotalTicks = recover;
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
