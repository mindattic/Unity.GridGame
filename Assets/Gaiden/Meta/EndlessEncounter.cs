using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// GG-T64: one endless encounter as a pure function of its seed — the single place `-demo -endless`
    /// (GameApp.EndlessDemo) and a headless Core replay both build it from, so the encounter a telemetry record
    /// or report flagged (its <c>seed</c>) can be reproduced exactly: the same location, waves and levels
    /// (<see cref="EncounterGenerator"/>, GG-T55), the same geared party (<see cref="EncounterParty"/>, GG-T56),
    /// the same battle RNG (<see cref="BattleSetup.Seed"/> = the encounter seed) and, with the bot seeded from the
    /// encounter seed too (<see cref="Bot"/>), the same battle.
    /// </summary>
    public static class EndlessEncounter
    {
        /// <summary>Action cap for a headless replay — a 5-wave encounter resolves well inside it.</summary>
        public const int DefaultMaxActions = 3000; // GG-T308/GG-T311: a command is one hero action or a forced Guard (was 900)

        /// <summary>
        /// Removes every synthetic endless stage from <paramref name="db"/> (the GG-T23 rule: a throwaway stage
        /// must never pile up in, or be borrowed back out of, the content). <see cref="EncounterGenerator"/> borrows
        /// its location from any non-test stage, so a leftover endless stage would change what a seed generates.
        /// </summary>
        public static void Prune(ContentDb db)
        {
            var stale = new List<string>();
            foreach (var id in db.Stages.Keys) if (id.StartsWith(EncounterGenerator.IdPrefix, StringComparison.Ordinal)) stale.Add(id);
            foreach (var id in stale) db.Stages.Remove(id);
        }

        /// <summary>
        /// Builds encounter <paramref name="seed"/>: prunes any earlier endless stage, generates this one and
        /// registers it in <paramref name="db"/> (the engine looks stages up by id; the caller prunes it when done),
        /// and returns its setup with a level-appropriate geared party. Same content + same seed = same setup.
        /// </summary>
        public static BattleSetup Build(ContentDb db, int seed)
        {
            Prune(db);
            var stage = EncounterGenerator.Generate(db, seed);
            db.Stages[stage.Id] = stage;
            int level = stage.LevelOverride ?? 1;
            return new BattleSetup
            {
                Stage = stage,
                Seed = seed,
                // A fresh game's bar: what the endless demo's throwaway per-encounter save unlocks.
                UnlockedSlots = Progression.UnlockedSlots(Progression.NewGame(db, "Endless")),
                RecommendedLevel = 1, // waves keep their own rising levels (GG-T55); no campaign floor
                Party = EncounterParty.Generate(db, seed, level),
            };
        }

        /// <summary>The autoplay bot for encounter <paramref name="seed"/>: its RNG is seeded from the encounter seed.</summary>
        public static HeroBot Bot(int seed) => new HeroBot(seed);

        /// <summary>What a replay came to: the outcome, the commands played (turns) and how far it got.</summary>
        public readonly struct Result
        {
            public readonly BattlePhase Outcome;
            public readonly int Turns;
            public readonly int Wave;
            public readonly long Clock;
            public Result(BattlePhase outcome, int turns, int wave, long clock) { Outcome = outcome; Turns = turns; Wave = wave; Clock = clock; }
            public override string ToString() => $"{Outcome} in {Turns} turns (wave {Wave + 1}, clock {Clock})";
        }

        /// <summary>
        /// Headless replay of encounter <paramref name="seed"/> in Core (no player): builds it, plays it to the end
        /// with <see cref="Bot"/>, prunes its stage again and reports the result. Deterministic per seed.
        /// </summary>
        public static Result Replay(ContentDb db, int seed, int maxActions = DefaultMaxActions,
            Action<BattleCommand, List<BattleEvent>, BattleState> observe = null)
        {
            var setup = Build(db, seed);
            try
            {
                int turns = 0;
                var s = BotRunner.Play(db, setup, Bot(seed), maxActions, (cmd, ev, st) =>
                {
                    if (cmd != null) turns++;
                    observe?.Invoke(cmd, ev, st);
                });
                return new Result(s.Phase, turns, s.Wave, s.Clock);
            }
            finally { Prune(db); }
        }
    }
}
