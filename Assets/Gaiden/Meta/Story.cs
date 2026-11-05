using System;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// The campaign's bookends (§27, GG-T231): the prologue after a new campaign's party pick, and the ending —
    /// crawl, rolling credits, Title — after the first victory over <c>campaign.json</c>'s <c>Ending.Stage</c>.
    /// The postgame themes stay playable afterwards; New Game+ lets the ending play again.
    /// </summary>
    public static class Story
    {
        /// <summary>Set once the ending has played; New Game+ clears it.</summary>
        public const string EndingFlag = "story:ending";

        /// <summary>True when this result should roll the ending: a victory over the ending stage on a save that
        /// hasn't seen it. Never on a Test Mode save (bots never see story).</summary>
        public static bool EndingDue(ContentDb c, SaveData s, bool victory, string stageId) =>
            victory && s != null && !s.TestMode && c.Campaign.Ending?.Stage != null
            && string.Equals(c.Campaign.Ending.Stage, stageId, StringComparison.OrdinalIgnoreCase)
            && !s.Flags.Contains(EndingFlag);

        public static void MarkEndingSeen(SaveData s) => s.Flags.Add(EndingFlag);
    }
}
