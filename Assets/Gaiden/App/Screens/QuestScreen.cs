using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>The quest journal (§22.11, GG-T237): quests ready to hand in, under way, heard of, still to ask about,
    /// and done. Optional side quests — separate from the bounty board and never in the campaign's way.</summary>
    public sealed class QuestScreen : HubPage
    {
        private string handedIn;
        public override string Id => "quests";
        protected override string Title => "Quests";

        protected override void BuildBody(VisualElement body)
        {
            if (handedIn != null && C.TryQuest(handedIn) is QuestDef done) TurnInCard(done, body);
            var w = Ui.Window(null, body);
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            bool any = false;

            void Section(string title, System.Collections.Generic.IEnumerable<QuestDef> quests, System.Action<QuestDef, VisualElement> extra)
            {
                var qs = quests.ToList();
                if (qs.Count == 0) return;
                any = true;
                Ui.Text(title, "h2 gold", list).style.marginTop = 12;
                foreach (var q in qs) Card(q, list, extra);
            }

            var all = C.QuestOrder;
            Section("Ready to hand in!", all.Where(q => Quests.State(C, S, q) == QuestState.Ready), (q, card) =>
            {
                var b = Ui.Button("Hand in", () => TurnIn(q), "btn--primary btn--small", card);
                b.name = "quest-turnin-" + q.Id;
            });
            Section("Under way", all.Where(q => Quests.State(C, S, q) == QuestState.Active), (q, card) =>
                Ui.Button("Set aside", () => Confirm.Show($"Set \"{q.Name}\" aside? Progress is lost, but you can take it up again.", "Set aside", () => Done(Quests.Abandon(C, S, q.Id))), "btn--danger btn--small", card));
            var heard = Quests.Heard(C, S);
            Section("Heard of", heard, (q, card) =>
            {
                var b = Ui.Button("Accept", () => Accept(q), "btn--small", card);
                b.name = "quest-accept-" + q.Id;
            });
            Section("Ask around", all.Where(q => Quests.State(C, S, q) == QuestState.Available && !heard.Contains(q)), null);
            Section("Done", all.Where(q => Quests.State(C, S, q) == QuestState.Done), null);
            if (!any) Ui.Text("No quests yet. Talk to your companions and the caravan's people between battles: they'll have things they need doing.", "small dim", list).style.whiteSpace = WhiteSpace.Normal;
        }

        private void Card(QuestDef q, VisualElement parent, System.Action<QuestDef, VisualElement> extra)
        {
            var st = Quests.State(C, S, q);
            var card = Ui.El("card", parent);
            card.name = "quest-" + q.Id;
            if (q.Objective.Kind == QuestObjectiveKind.Defeat && C.TryClass(q.Objective.Id) != null) Ui.Thumb(q.Objective.Id, card, 110);
            var col = Ui.El("col grow", card);
            Ui.Text(q.Name, "h2", col);
            string theme = C.Campaign.Themes.FirstOrDefault(t => t.Id == q.Theme)?.Name;
            Ui.Text($"{q.Giver}{(theme != null ? " · " + theme : "")}", "tiny dim", col);
            if (st == QuestState.Available && !Quests.Heard(C, S).Contains(q))
            {
                Ui.Text(q.Giver == "Party" ? "One of your companions has something on their mind. Talk to the party." : $"The {q.Giver} has something on their mind. Talk to them.", "small", col).style.whiteSpace = WhiteSpace.Normal;
                return;
            }
            Ui.Text(q.Summary, "small", col).style.whiteSpace = WhiteSpace.Normal;
            var goal = Ui.Text(st == QuestState.Done ? Quests.Describe(C, q) : $"{Quests.Describe(C, q)}  {Quests.Progress(S, q)}/{Quests.Goal(q)}",
                st == QuestState.Ready ? "small good" : "small gold", col);
            goal.style.whiteSpace = WhiteSpace.Normal;
            Ui.Text("Reward: " + Quests.RewardText(C, q), "tiny dim", col).style.whiteSpace = WhiteSpace.Normal;
            extra?.Invoke(q, card);
        }

        private void Accept(QuestDef q)
        {
            var r = Quests.Start(C, S, q.Id);
            App.Toast(r.Message);
            App.Audio.Sfx(r.Ok ? "story.quest_accept" : "ui.error");
            if (r.Ok) App.Persist();
            Refresh();
        }

        private void TurnIn(QuestDef q)
        {
            var r = Quests.TurnIn(C, S, q.Id);
            App.Toast(r.Message, 2.6f);
            App.Audio.Sfx(r.Ok ? "story.quest_complete" : "ui.error");
            if (!r.Ok) { Refresh(); return; }
            App.Persist();
            handedIn = q.Id;
            Refresh();
        }

        /// <summary>The giver's thanks, sliding in at the top of the journal after a hand-in (no single-frame swap).</summary>
        private void TurnInCard(QuestDef q, VisualElement body)
        {
            var w = Ui.Window($"{q.Name} — complete!", body, "quest-turnin is-hidden");
            if (!string.IsNullOrEmpty(q.TurnIn)) Ui.Text($"\"{q.TurnIn}\"", "small", w).style.whiteSpace = WhiteSpace.Normal;
            Ui.Text($"— {q.Giver}", "tiny dim", w);
            Ui.Text("Received: " + Quests.RewardText(C, q), "small gold", w).style.whiteSpace = WhiteSpace.Normal;
            w.schedule.Execute(() => w.RemoveFromClassList("is-hidden")).ExecuteLater(30);
        }
    }
}
