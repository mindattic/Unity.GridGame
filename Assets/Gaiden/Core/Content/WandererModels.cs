using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>A STORY.md faction (factions.json, GG-T342) and the hero classes who belong to it. A wanderer talked to by a
    /// companion of their own faction is easier to win over (Meta <c>Wanderers.Judge</c>), and a faction's Wanderer talk
    /// (<see cref="DialogueTriggerKind.Wanderer"/> with this <see cref="Id"/>) speaks for every hero in it without a scene
    /// of their own.</summary>
    public sealed class FactionDef
    {
        public string Id;
        /// <summary>As the game says it: "the Lantern Road".</summary>
        public string Name;
        public List<string> Heroes = new List<string>();
    }

    public static class WandererContent
    {
        /// <summary>factions.json: known hero classes, each in at most one faction, no duplicate ids.</summary>
        public static void ValidateFactions(ContentDb db, List<string> p)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var member = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in db.Factions)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Id)) { p.Add("faction with empty Id"); continue; }
                string w = $"faction {f.Id}";
                if (!ids.Add(f.Id)) p.Add($"{w}: duplicate id");
                if (string.IsNullOrWhiteSpace(f.Name)) p.Add($"{w}: needs a Name");
                if (db.TryClass(f.Id) != null) p.Add($"{w}: id clashes with a class id");
                foreach (var h in f.Heroes)
                {
                    if (db.TryClass(h)?.Team != Team.Hero) { p.Add($"{w}: '{h}' is not a hero class"); continue; }
                    if (member.TryGetValue(h, out var other)) p.Add($"{w}: '{h}' is already in faction {other}");
                    else member[h] = f.Id;
                }
            }
        }

        /// <summary>A Wanderer talk's shape (GG-T342): Id is a hero class, a faction or none; no effects (the rule, not the
        /// script, decides); exactly one question of three unconditional answers, one to <see cref="DialogueDef.HeardNode"/>
        /// and two to <see cref="DialogueDef.UnheardNode"/>; both replies go on to <see cref="DialogueDef.OutcomeNext"/>;
        /// and a <see cref="DialogueDef.JoinNode"/> and a <see cref="DialogueDef.PartNode"/> that end it.</summary>
        public static void ValidateDialogue(ContentDb db, DialogueDef d, List<string> p)
        {
            string w = $"dialogue {d.Id}";
            var id = d.Trigger?.Id;
            if (id != null && db.TryClass(id)?.Team != Team.Hero && !db.Factions.Exists(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)))
                p.Add($"{w}: Wanderer needs a hero class, a faction or no Id, has '{id}'");
            if (d.AllEffects().GetEnumerator().MoveNext()) p.Add($"{w}: a Wanderer talk can't have effects (the understanding rule decides)");
            var asks = d.Nodes.FindAll(n => n != null && n.Choices.Count > 0);
            if (asks.Count != 1) p.Add($"{w}: a Wanderer talk needs exactly one question, has {asks.Count}");
            else
            {
                var ask = asks[0];
                if (ask.Choices.Count != 3) p.Add($"{w}: the question needs three answers, has {ask.Choices.Count}");
                int heard = ask.Choices.FindAll(ch => Is(ch?.Next, DialogueDef.HeardNode)).Count;
                int unheard = ask.Choices.FindAll(ch => Is(ch?.Next, DialogueDef.UnheardNode)).Count;
                if (heard != 1) p.Add($"{w}: exactly one answer must go to '{DialogueDef.HeardNode}', has {heard}");
                if (heard + unheard != ask.Choices.Count) p.Add($"{w}: every other answer must go to '{DialogueDef.UnheardNode}'");
                if (ask.Choices.Exists(ch => ch != null && ch.Conditions.Count > 0)) p.Add($"{w}: the answers can't be conditional");
            }
            foreach (var reply in new[] { DialogueDef.HeardNode, DialogueDef.UnheardNode })
            {
                var n = d.Nodes.Find(x => Is(x?.Id, reply));
                if (n == null) p.Add($"{w}: needs a '{reply}' node");
                else if (!Is(n.Next, DialogueDef.OutcomeNext)) p.Add($"{w}: '{reply}' must go on to \"{DialogueDef.OutcomeNext}\"");
            }
            foreach (var end in new[] { DialogueDef.JoinNode, DialogueDef.PartNode })
            {
                var n = d.Nodes.Find(x => Is(x?.Id, end));
                if (n == null) p.Add($"{w}: needs a '{end}' node");
                else if (n.Next != null || n.Choices.Count > 0) p.Add($"{w}: '{end}' must end the talk");
            }
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
