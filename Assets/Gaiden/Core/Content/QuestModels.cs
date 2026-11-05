using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>What an optional side quest asks for (§22.11, GG-T237) — only things the save already tracks.</summary>
    public enum QuestObjectiveKind { ClearStage, Defeat, Bring, Flag }

    public sealed class QuestObjective
    {
        public QuestObjectiveKind Kind;
        /// <summary>ClearStage: the stage id. Defeat: the enemy class id. Bring: the item id. Flag: the save flag.</summary>
        public string Id;
        /// <summary>Defeat: foes to fell (after accepting). Bring: items handed over (consumed on turn-in). Default 1.</summary>
        public int Count = 1;
        /// <summary>ClearStage only: win with at most this many heroes of your own in the fight (0 = any).</summary>
        public int MaxHeroes;
        /// <summary>ClearStage only: every hero of your own must be standing at the victory.</summary>
        public bool NoneFallen;
    }

    /// <summary>One optional side quest (quests.json, §22.11, GG-T237): offered by a Camp conversation (a dialogue
    /// <c>StartQuest</c> effect), tracked in the journal, turned in from Camp for its rewards. Never gates the campaign.</summary>
    public sealed class QuestDef
    {
        public string Id;
        public string Name;
        /// <summary>Who hands it out, as the journal shows it ("Innkeeper").</summary>
        public string Giver;
        /// <summary>The campaign theme it belongs to (journal grouping and backdrop).</summary>
        public string Theme;
        /// <summary>Offered only once this stage is cleared; null = from the start.</summary>
        public string Unlock;
        /// <summary>The journal's one-paragraph ask, in the giver's voice.</summary>
        public string Summary;
        /// <summary>What the giver says when it's handed in (the turn-in toast).</summary>
        public string TurnIn;
        public QuestObjective Objective = new QuestObjective();
        public int RewardGold;
        public string RewardItem;
        public int RewardItemCount = 1;
        /// <summary>Skill points each hero in the active party gains on turn-in.</summary>
        public int RewardSkillPoints;
        /// <summary>A save flag set on turn-in — opens a follow-up Talk dialogue gated on it.</summary>
        public string RewardFlag;
    }

    /// <summary>quests.json validation (§22.11): run from <see cref="ContentLoader.Validate"/>.</summary>
    public static class QuestCatalog
    {
        public static void Validate(ContentDb db, List<string> p)
        {
            var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in db.DialogueOrder)
                foreach (var e in d.AllEffects())
                    if (e?.Type == DialogueEffectType.StartQuest && e.Id != null) given.Add(e.Id);
            foreach (var q in db.QuestOrder)
            {
                string w = $"quest {q.Id}";
                if (string.IsNullOrWhiteSpace(q.Name)) p.Add($"{w}: needs a Name");
                if (string.IsNullOrWhiteSpace(q.Summary)) p.Add($"{w}: needs a Summary");
                if (q.Theme != null && !db.Campaign.Themes.Exists(t => string.Equals(t.Id, q.Theme, StringComparison.OrdinalIgnoreCase))) p.Add($"{w}: unknown theme '{q.Theme}'");
                if (q.Unlock != null && !db.Stages.ContainsKey(q.Unlock)) p.Add($"{w}: unknown unlock stage '{q.Unlock}'");
                var o = q.Objective;
                if (o == null) { p.Add($"{w}: needs an Objective"); continue; }
                if (o.Count <= 0) p.Add($"{w}: objective Count must be positive");
                switch (o.Kind)
                {
                    case QuestObjectiveKind.ClearStage:
                        if (o.Id == null || !db.Stages.ContainsKey(o.Id)) p.Add($"{w}: unknown stage '{o.Id}'");
                        if (o.MaxHeroes < 0) p.Add($"{w}: MaxHeroes can't be negative");
                        break;
                    case QuestObjectiveKind.Defeat:
                        if (db.TryClass(o.Id)?.Team != Team.Enemy) p.Add($"{w}: Defeat needs an enemy class, has '{o.Id}'");
                        break;
                    case QuestObjectiveKind.Bring:
                        if (db.TryItem(o.Id) == null) p.Add($"{w}: unknown item '{o.Id}'");
                        break;
                    case QuestObjectiveKind.Flag:
                        if (string.IsNullOrEmpty(o.Id)) p.Add($"{w}: Flag needs a flag Id");
                        break;
                }
                if (o.Kind != QuestObjectiveKind.ClearStage && (o.MaxHeroes != 0 || o.NoneFallen)) p.Add($"{w}: MaxHeroes/NoneFallen only fit a ClearStage objective");
                if (q.RewardItem != null && db.TryItem(q.RewardItem) == null) p.Add($"{w}: unknown reward item '{q.RewardItem}'");
                if (q.RewardGold < 0 || q.RewardSkillPoints < 0 || q.RewardItemCount <= 0) p.Add($"{w}: rewards can't be negative");
                if (q.RewardGold == 0 && q.RewardItem == null && q.RewardSkillPoints == 0 && q.RewardFlag == null) p.Add($"{w}: needs a reward");
                if (q.RewardFlag != null && (q.RewardFlag.StartsWith(ContentLoader.NpcFlagPrefix) || q.RewardFlag.StartsWith("sp:"))) p.Add($"{w}: reward flag '{q.RewardFlag}' is reserved");
                if (!given.Contains(q.Id)) p.Add($"{w}: no dialogue offers it (needs a StartQuest effect)");
            }
        }
    }
}
