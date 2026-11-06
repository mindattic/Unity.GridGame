using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>Where a quest stands for this save (§22.11, GG-T237).</summary>
    public enum QuestState { Locked, Available, Active, Ready, Done }

    /// <summary>
    /// Optional side quests (§22.11, GG-T237): offered by Camp conversations (dialogue <c>StartQuest</c>), tracked in the
    /// journal, progressed by battles (<see cref="RecordBattle"/>, next to the bounty in <c>BattleFactory.Commit</c>) and
    /// handed in from Camp. Separate from the bounty board: any number may run at once and none ever gates the campaign.
    /// </summary>
    public static class Quests
    {
        public static bool Unlocked(SaveData s, QuestDef q) => q.Unlock == null || s.ClearedStages.Contains(q.Unlock);

        public static QuestState State(ContentDb c, SaveData s, QuestDef q)
        {
            if (s.Quests.Done.Contains(q.Id)) return QuestState.Done;
            if (s.Quests.Active.ContainsKey(q.Id)) return IsReady(c, s, q) ? QuestState.Ready : QuestState.Active;
            return Unlocked(s, q) ? QuestState.Available : QuestState.Locked;
        }

        public static bool Available(ContentDb c, SaveData s, string id) { var q = c.TryQuest(id); return q != null && State(c, s, q) == QuestState.Available; }
        public static bool Active(SaveData s, string id) => id != null && s.Quests.Active.ContainsKey(id);
        public static bool Done(SaveData s, string id) => id != null && s.Quests.Done.Contains(id);

        /// <summary>How far along it is, out of <see cref="Goal"/>. Bring and Flag read the save live.</summary>
        public static int Progress(SaveData s, QuestDef q)
        {
            if (s.Quests.Done.Contains(q.Id)) return Goal(q);
            var o = q.Objective;
            switch (o.Kind)
            {
                case QuestObjectiveKind.Bring: return Math.Min(Goal(q), s.Count(o.Id));
                case QuestObjectiveKind.Flag: return s.Flags.Contains(o.Id) ? 1 : 0;
                default: return s.Quests.Active.TryGetValue(q.Id, out var n) ? Math.Min(Goal(q), n) : 0;
            }
        }

        public static int Goal(QuestDef q) =>
            q.Objective.Kind == QuestObjectiveKind.Defeat || q.Objective.Kind == QuestObjectiveKind.Bring ? Math.Max(1, q.Objective.Count) : 1;

        public static bool IsReady(ContentDb c, SaveData s, QuestDef q) => s.Quests.Active.ContainsKey(q.Id) && Progress(s, q) >= Goal(q);

        /// <summary>Quests ready to hand in — Camp's "!" marker.</summary>
        public static int ReadyCount(ContentDb c, SaveData s) => c.QuestOrder.Count(q => State(c, s, q) == QuestState.Ready);

        /// <summary>Quests whose offer the player has heard (the giver's conversation is seen) but didn't take up, so the
        /// journal can still accept them — a "Not now" in the conversation never loses a quest.</summary>
        public static List<QuestDef> Heard(ContentDb c, SaveData s) =>
            c.QuestOrder.Where(q => State(c, s, q) == QuestState.Available && c.DialogueOrder.Any(d => s.SeenDialogues.Contains(d.Id)
                && d.AllEffects().Any(e => e?.Type == DialogueEffectType.StartQuest && string.Equals(e.Id, q.Id, StringComparison.OrdinalIgnoreCase)))).ToList();

        public static Result Start(ContentDb c, SaveData s, string id)
        {
            var q = c.TryQuest(id);
            if (q == null) return Result.Fail("No such quest");
            var st = State(c, s, q);
            if (st == QuestState.Done) return Result.Fail("Already done");
            if (st == QuestState.Active || st == QuestState.Ready) return Result.Fail("Already under way");
            s.Quests.Active[q.Id] = 0;
            return Result.Success($"Quest: {q.Name}");
        }

        public static Result Abandon(ContentDb c, SaveData s, string id)
        {
            if (id == null || !s.Quests.Active.Remove(id)) return Result.Fail("Not under way");
            return Result.Success($"Set aside: {c.TryQuest(id)?.Name ?? id}");
        }

        /// <summary>Progress from one finished stage battle (victories only, like the bounty board). Returns a note per
        /// quest it moved ("Quest — Wolves at the Wells: 4/6") for the results screen.</summary>
        public static List<string> RecordBattle(ContentDb c, SaveData s, BattleState battle)
        {
            var notes = new List<string>();
            if (battle == null || battle.Phase != BattlePhase.Victory) return notes;
            var own = battle.Units.Where(u => u.IsHero && !u.IsGuest && !u.IsWildTamed).ToList();
            foreach (var q in c.QuestOrder)
            {
                if (!s.Quests.Active.TryGetValue(q.Id, out var have) || IsReady(c, s, q)) continue;
                var o = q.Objective;
                int next = have;
                if (o.Kind == QuestObjectiveKind.Defeat && battle.Results.Kills.TryGetValue(o.Id, out var n)) next = Math.Min(Goal(q), have + n);
                else if (o.Kind == QuestObjectiveKind.ClearStage && string.Equals(battle.Stage.Id, o.Id, StringComparison.OrdinalIgnoreCase)
                         && (o.MaxHeroes <= 0 || own.Count <= o.MaxHeroes) && (!o.NoneFallen || own.All(u => u.Alive)))
                    next = 1;
                if (next == have) continue;
                s.Quests.Active[q.Id] = next;
                notes.Add(next >= Goal(q) ? $"{q.Name}: ready to hand in!" : $"{q.Name}: {next}/{Goal(q)}");
            }
            return notes;
        }

        /// <summary>Hands a finished quest in: Bring items are given over, then gold, item, skill points (each active-party
        /// hero) and the follow-up flag are paid.</summary>
        public static Result TurnIn(ContentDb c, SaveData s, string id)
        {
            var q = c.TryQuest(id);
            if (q == null || !s.Quests.Active.ContainsKey(q.Id)) return Result.Fail("Not under way");
            if (!IsReady(c, s, q)) return Result.Fail("Not finished yet");
            if (q.Objective.Kind == QuestObjectiveKind.Bring) s.AddItem(q.Objective.Id, -Goal(q));
            var paid = new List<string>();
            if (q.RewardGold > 0) { s.Gold += q.RewardGold; s.LifetimeCoins += q.RewardGold; paid.Add($"{q.RewardGold}g"); }
            if (q.RewardItem != null)
            {
                int n = Math.Max(1, q.RewardItemCount);
                s.AddItem(q.RewardItem, n);
                paid.Add((c.TryItem(q.RewardItem)?.Name ?? q.RewardItem) + (n > 1 ? $" ×{n}" : ""));
            }
            if (q.RewardSkillPoints > 0)
            {
                foreach (var hid in s.Party) { var h = s.Hero(hid); if (h != null) h.SkillPoints += q.RewardSkillPoints; }
                paid.Add($"{q.RewardSkillPoints} SP each");
            }
            if (q.RewardFlag != null) s.Flags.Add(q.RewardFlag);
            s.Quests.Active.Remove(q.Id);
            s.Quests.Done.Add(q.Id);
            return Result.Success($"{q.Name} complete!" + (paid.Count > 0 ? " " + string.Join(" + ", paid) : ""));
        }

        /// <summary>The objective in one line ("Defeat 6× Wolf", "Clear Green Valley III with 3 heroes or fewer").</summary>
        public static string Describe(ContentDb c, QuestDef q)
        {
            var o = q.Objective;
            switch (o.Kind)
            {
                case QuestObjectiveKind.Defeat: return $"Defeat {Goal(q)}× {c.TryClass(o.Id)?.Name ?? o.Id}";
                case QuestObjectiveKind.Bring: return $"Bring {Goal(q)}× {c.TryItem(o.Id)?.Name ?? o.Id}";
                case QuestObjectiveKind.Flag: return "See it through";
                default:
                {
                    var line = $"Clear {c.TryStage(o.Id)?.Name ?? o.Id}";
                    var terms = new List<string>();
                    if (o.MaxHeroes > 0) terms.Add($"with {o.MaxHeroes} hero{(o.MaxHeroes == 1 ? "" : "es")} or fewer");
                    if (o.NoneFallen) terms.Add("without anyone falling");
                    return terms.Count > 0 ? line + " " + string.Join(" and ", terms) : line;
                }
            }
        }

        /// <summary>The reward in one line ("300g + Hi-Potion + 1 SP each").</summary>
        public static string RewardText(ContentDb c, QuestDef q)
        {
            var parts = new List<string>();
            if (q.RewardGold > 0) parts.Add($"{q.RewardGold}g");
            if (q.RewardItem != null) parts.Add((c.TryItem(q.RewardItem)?.Name ?? q.RewardItem) + (q.RewardItemCount > 1 ? $" ×{q.RewardItemCount}" : ""));
            if (q.RewardSkillPoints > 0) parts.Add($"{q.RewardSkillPoints} SP each");
            if (q.RewardFlag != null) parts.Add("a new conversation");
            return string.Join(" + ", parts);
        }
    }
}
