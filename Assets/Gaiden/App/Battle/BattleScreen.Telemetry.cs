using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Meta;
using MindAttic.Telemetry.Unity;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// GG-T59: per-encounter telemetry. While a telemetry session is open (editor / development builds), every
    /// command's events feed an <see cref="EncounterTelemetry"/>, and when the battle ends one structured
    /// "encounter" record is appended to the session (one JSONL line, flushed as it's written, so a crash loses
    /// at most the encounter in progress) just before the session's closing record.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private EncounterTelemetry encounterTelemetry;
        private bool encounterRecorded;

        private void BeginEncounterTelemetry(BattleSetup setup, List<BattleEvent> initial, DifficultyRow diff)
        {
            encounterRecorded = false;
            if (!TelemetryUnity.Current.Enabled) { encounterTelemetry = null; return; }
            var tags = new Dictionary<string, object>
            {
                ["level"] = setup.Difficulty,
                ["enemyHp"] = diff.EnemyHp, ["enemyDamage"] = diff.EnemyDamage, ["enemyLevel"] = diff.EnemyLevelOffset,
                ["rewardGold"] = diff.RewardGold, ["rewardXp"] = diff.RewardXp, ["itemDropRate"] = diff.DropChance,
            };
            encounterTelemetry = new EncounterTelemetry(App.Content, setup, State, initial, tags, Time.realtimeSinceStartupAsDouble);
        }

        private void ObserveEncounterTelemetry(List<BattleEvent> events) =>
            encounterTelemetry?.Observe(events, State, Time.realtimeSinceStartupAsDouble);

        /// <summary>Appends this encounter's record to the open session (once per battle).</summary>
        private void RecordEncounterTelemetry()
        {
            if (encounterTelemetry == null || encounterRecorded) return;
            encounterRecorded = true;
            TelemetryUnity.Current.RecordEvent("encounter", encounterTelemetry.Build(State, Time.realtimeSinceStartupAsDouble));
        }
    }
}
