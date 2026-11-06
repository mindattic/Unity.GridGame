using System;
using GridGame.Core.Battle;

namespace GridGame.Tests
{
    /// <summary>The difficulty dial the measurement soaks fight at (GG-T309): GAIDEN_DIFFICULTY=1..10, default 10 — the
    /// designed game every balance pass targets — so `polish.py --balance --difficulty 5` measures a gentler dial.</summary>
    internal static class BotDifficulty
    {
        public static int FromEnv() =>
            int.TryParse(Environment.GetEnvironmentVariable("GAIDEN_DIFFICULTY"), out var d) ? DifficultyTable.Clamp(d) : DifficultyTable.Default;
    }
}
