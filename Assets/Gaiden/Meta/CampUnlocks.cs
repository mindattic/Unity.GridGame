using System.Linq;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// Which Camp stations are open, and if not, when they will be (GG-T174, bible §25.0.3). Keeper stations open when
    /// their NPC joins at a story beat (§25.0.1); the four keeperless ones open at campaign milestones, so a new player
    /// meets one system at a time instead of every menu at once. A station the save has already used never locks
    /// again, and bot-played saves see everything.
    /// </summary>
    public static class CampUnlocks
    {
        /// <summary>Campaign stages that must be cleared before each keeperless station opens.</summary>
        public const int BountiesAfter = 2, SummonAfter = 5, CrucibleAfter = 5, CaravanAfter = 10;

        private static int Cleared(SaveData s) => s.HighestClearedStage + 1;
        private static bool Veteran(SaveData s) => s.TestMode || s.NewGamePlus > 0;

        public static bool NpcOpen(SaveData s, string npc) => s != null && (CampNpcs.Joined(s, npc) || s.TestMode);

        public static bool BountiesOpen(SaveData s) => s != null && (Veteran(s) || Cleared(s) >= BountiesAfter || s.Bounty?.ActiveId != null);
        public static bool SummonOpen(SaveData s) => s != null && (Veteran(s) || Cleared(s) >= SummonAfter || s.Recruits > 0);
        public static bool CrucibleOpen(SaveData s) => s != null && (Veteran(s) || Cleared(s) >= CrucibleAfter || (s.Crucible?.Runs ?? 0) > 0);
        public static bool CaravanOpen(SaveData s) => s != null && (Veteran(s) || Cleared(s) >= CaravanAfter || (s.Caravan?.Values.Sum() ?? 0) > 0);

        /// <summary>The Blacksmith's Forge, Upgrade and Salvage tabs (GG-T175, §25.0.4). He joins to mend worn gear, so
        /// Repair is his only tab at first; the workshop opens a step later in the onboarding chain — once the Alchemist
        /// has joined (GG-T177 hands it the first forge). Old saves joined everyone (v4 migration), so they keep it.</summary>
        public static bool SmithWorkshopOpen(SaveData s) => s != null && (Veteran(s) || CampNpcs.Joined(s, CampNpcs.Alchemist));

        /// <summary>The locked workshop tab's hint.</summary>
        public const string SmithWorkshopHint = "Opens once the Alchemist joins";

        /// <summary>"After Green Valley II" — the stage whose clear opens a milestone station.</summary>
        public static string MilestoneHint(ContentDb c, int clears)
        {
            var stages = c.CampaignStages();
            if (clears <= 0 || stages.Count == 0) return "";
            return "After " + stages[System.Math.Min(clears, stages.Count) - 1].Name;
        }

        /// <summary>"Joins at Sandsea Reaches" / "Joins after Green Valley II" / "Joins when gear wears below 60%" / "Joins once you've looted enough to brew" — read from the story beat whose
        /// JoinNpc recruits this keeper, so the hint follows the content.</summary>
        public static string JoinHint(ContentDb c, string npc)
        {
            var beat = c.Dialogues.Values.FirstOrDefault(d => d.Trigger != null && d.Trigger.Kind != DialogueTriggerKind.Talk
                                                              && d.AllEffects().Any(e => e.Type == DialogueEffectType.JoinNpc && e.Id == npc));
            if (beat == null) return "Joins later on the road";
            if (beat.Trigger.Kind == DialogueTriggerKind.GearWorn)
                return $"Joins when gear wears below {beat.Trigger.Percent}%";
            if (beat.Trigger.Kind == DialogueTriggerKind.CanBrew)
                return "Joins once you've looted enough to brew";
            if (beat.Trigger.Kind == DialogueTriggerKind.FirstArmor)
                return "Joins once you've forged your first armor";
            if (beat.Trigger.Kind == DialogueTriggerKind.ThemeStart)
                return "Joins at " + (c.Campaign.Themes.FirstOrDefault(t => t.Id == beat.Trigger.Id)?.Name ?? beat.Trigger.Id);
            return "Joins after " + (c.Stages.TryGetValue(beat.Trigger.Id ?? "", out var st) ? st.Name : beat.Trigger.Id);
        }
    }
}
