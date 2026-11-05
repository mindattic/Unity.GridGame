using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>A finale boss's signature break from standard play (§14.9, GG-T234), set per spawn on a stage wave
    /// (<see cref="SpawnDef.RuleBreak"/>) so the same class fights by the normal rules everywhere else.</summary>
    public enum RuleBreak
    {
        None,
        /// <summary>The turn order: it takes a second turn whenever no hero stands beside it at the end of its first.</summary>
        TwinTurn,
        /// <summary>The pincer rule: pincers along its row glance off (each cracks the ward; the third shatters it). Column pincers land.</summary>
        AxisWard,
        /// <summary>One step at a time: with a hero beside it at its turn, it trades places with an ally anywhere on the board.</summary>
        MireSwap,
        /// <summary>Only you move your heroes: every second turn it stamps and drags every hero within reach toward itself.</summary>
        GravityStamp,
        /// <summary>A fallen foe stays down: the first time it falls while any ally still stands, it rises at half HP.</summary>
        Undying,
    }

    public static class RuleBreaks
    {
        /// <summary>The entrance banner: which rule it breaks and the counterplay.</summary>
        public static string Banner(string name, RuleBreak r)
        {
            switch (r)
            {
                case RuleBreak.TwinTurn: return $"{name} ignores the turn order: two turns unless a hero stands beside it!";
                case RuleBreak.AxisWard: return $"{name} ignores the pincer rule: side-to-side pincers glance off. Pincer it top to bottom!";
                case RuleBreak.MireSwap: return $"{name} ignores the board: corner it and it swaps with an ally. Cut down its allies first!";
                case RuleBreak.GravityStamp: return $"{name} moves your heroes: every other turn it stamps and drags you in. Keep your distance!";
                case RuleBreak.Undying: return $"{name} refuses to fall while an ally stands. Slay its thralls first!";
                default: return name;
            }
        }

        /// <summary>The short float shown over the boss each time its rule bites.</summary>
        public static string Label(RuleBreak r)
        {
            switch (r)
            {
                case RuleBreak.TwinTurn: return "DOUBLE TIME!";
                case RuleBreak.AxisWard: return "WARDED!";
                case RuleBreak.MireSwap: return "SWAP!";
                case RuleBreak.GravityStamp: return "STAMP!";
                case RuleBreak.Undying: return "UNDYING!";
                default: return "";
            }
        }

        /// <summary>A rule-break belongs on an enemy spawn only, and the board-swap needs a one-tile body.</summary>
        public static void Validate(ContentDb db, StageDef s, WaveDef w, List<string> problems)
        {
            foreach (var e in w.Enemies)
            {
                if (e.RuleBreak != RuleBreak.MireSwap) continue;
                var c = db.TryClass(e.Class);
                if (c != null && ((c.Footprint?.W ?? 1) > 1 || (c.Footprint?.H ?? 1) > 1 || c.SnakeSegments > 0))
                    problems.Add($"stage {s.Id}: {e.Class} can't MireSwap (it needs a one-tile, non-snake body)");
            }
            foreach (var g in w.Guests)
                if (g.RuleBreak != RuleBreak.None) problems.Add($"stage {s.Id} guest {g.Class}: a RuleBreak belongs on an enemy spawn only");
        }
    }
}
