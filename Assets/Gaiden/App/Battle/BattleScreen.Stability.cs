using GridGame.Meta;
using MindAttic.Capture;
using MindAttic.Capture.Unity;
using MindAttic.Telemetry;
using MindAttic.Telemetry.Unity;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// GG-T61: the hooks <see cref="EndlessStability"/> uses to keep a `-demo -endless` run alive for hundreds of
    /// encounters in one process — ending an encounter that ran past its turn / real-time cap as "stalled" instead of
    /// letting it hang the loop, and tearing a finished battle down completely once the next one is on screen.
    /// </summary>
    public sealed partial class BattleScreen
    {
        /// <summary>A beat is animating or the bot's simulated drag is in flight (the battle is busy, not frozen).</summary>
        internal bool StabilityBusy => playing || simulatingDrag;

        /// <summary>This battle has finished (or been ended) and accepts no more commands.</summary>
        internal bool StabilityEnded => ended;

        /// <summary>
        /// Ends a battle that never resolved as <c>"stalled"</c>: no further command is accepted (the same `ended`
        /// gate EndBattle/Retreat use, so an in-flight drag or playback can't commit or route afterwards), the battle
        /// record is saved, and the telemetry session closes with a "stalled" event and outcome. The caller routes on
        /// (the endless loop goes straight to the next encounter). Returns false if the battle had already ended.
        /// </summary>
        internal bool EndStalled(string reason, float realSeconds)
        {
            if (ended || State == null) return false;
            ended = true;
            AutoPlay = false;
            int heroes = 0, foes = 0;
            foreach (var u in State.Units)
                if (u.Alive) { if (u.IsHero) heroes++; else foes++; }
            CaptureUnity.Raise("battle-end", new CaptureData { ["stage"] = State.Stage.Id, ["result"] = "stalled" });
            SaveRecord();
            TelemetryUnity.RecordEvent("stalled", new TelemetryData
            {
                ["reason"] = reason, ["seconds"] = Mathf.Round(realSeconds),
                ["actions"] = State.Results.ActionsTaken, ["ticks"] = State.Results.TicksElapsed,
            });
            TelemetryUnity.Finish(new
            {
                stage = State.Stage.Id,
                result = "stalled",
                reason,
                seconds = Mathf.Round(realSeconds),
                waveReached = State.Wave + 1,
                actions = State.Results.ActionsTaken,
                ticks = State.Results.TicksElapsed,
                coins = State.Results.Coins,
                xp = State.Results.Xp,
                heroesStanding = heroes,
                enemiesStanding = foes,
            });
            return true;
        }

        /// <summary>The report the endless loop records for a stalled encounter (nothing is committed or awarded).</summary>
        internal BattleReport StalledReport() => new BattleReport
        {
            StageId = State.Stage.Id, StageName = State.Stage.Name, StageTheme = State.Stage.Theme,
        };

        /// <summary>
        /// Called once this battle is off screen for good (the next encounter is showing): drops everything it could
        /// still be holding — its bots, its visual tree (board, FX layer, cut-in cards, timeline icons and their
        /// scheduled pulses) — and clears the global pause/hold flags in case it was torn down mid-beat. Any coroutine
        /// still running for it works on detached elements and finishes harmlessly; with `ended` set it can no
        /// longer commit or route.
        /// </summary>
        internal void TearDownAfterEncounter()
        {
            ended = true;
            AutoPlay = false;
            autoBot = null;
            perfectBot = null;
            if (pauseVeil != null) { pauseVeil.RemoveFromHierarchy(); pauseVeil = null; }
            Tween.Paused = false;
            if (Root != null)
            {
                Root.RemoveFromHierarchy();
                Root.Clear();
            }
        }
    }
}
