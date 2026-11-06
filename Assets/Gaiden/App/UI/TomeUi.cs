using System;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Learnable-passive rewards on screen (§25.6.4, GG-T301): a Tome's taught passive as its generated sentence (terms
    /// tappable through GlossaryText) with a "Teach to…" row of hero buttons, and a Chest's "Open" button. Used by the
    /// results screen, the Items bag, the quest hand-in card and the bounty board, so every reward source reads the same.
    /// </summary>
    internal static class TomeUi
    {
        private static GameApp App => GameApp.I;

        /// <summary>"[Reactive] Counter Attack: When [attacked directly]: …" — the kind, the name and the trigger sentence.</summary>
        public static string Sentence(ContentDb c, AbilityDef a) =>
            a == null ? "" : $"[{TriggerText.KindLabel(a)}] {a.Name}: {TriggerText.Describe(c, a)}";

        /// <summary>The taught passive's sentence alone (no buttons), for a reward that isn't in the bag yet.</summary>
        public static void Preview(VisualElement parent, ItemDef tome)
        {
            var a = RewardCatalog.TaughtBy(App.Content, tome);
            if (a == null) return;
            var l = GlossaryText.Create("Teaches " + Sentence(App.Content, a), "tiny dim", parent);
            l.style.whiteSpace = WhiteSpace.Normal;
        }

        /// <summary>The sentence plus "Teach to…": one button per roster hero (party first), disabled for a hero who already
        /// knows it or whose class can't learn it. Teaching uses up one Tome from the bag; the block redraws itself.</summary>
        /// <param name="sentence">False when the caller already shows the sentence (the Items detail panel).</param>
        public static VisualElement Offer(VisualElement parent, ItemDef tome, Action<Result> after = null, bool sentence = true)
        {
            var box = Ui.El("col", parent);
            box.name = "tome-" + tome.Id;
            box.style.marginLeft = Space.S16; box.style.marginBottom = Space.S8;
            Build(box, tome, after, null, sentence);
            return box;
        }

        private static void Build(VisualElement box, ItemDef tome, Action<Result> after, string taughtTo, bool sentence)
        {
            box.Clear();
            var c = App.Content; var s = App.Save;
            var a = RewardCatalog.TaughtBy(c, tome);
            if (a == null) return;
            if (sentence) GlossaryText.Create(Sentence(c, a), "tiny", box).style.whiteSpace = WhiteSpace.Normal;
            if (taughtTo != null)
            {
                var done = Ui.Text($"{taughtTo} learned {a.Name}.", "tiny good", box);
                App.StartCoroutine(Tween.Pop(done, 1.12f, 0.18f));
            }
            if (s.Count(tome.Id) <= 0) return;
            Ui.Text(s.Count(tome.Id) > 1 ? $"Teach to… ({s.Count(tome.Id)} tomes)" : "Teach to…", "tiny dim", box);
            var row = Ui.El("row", box);
            row.style.flexWrap = Wrap.Wrap;
            foreach (var h in s.Roster.OrderByDescending(x => s.Party.Contains(x.ClassId)).ToList())
            {
                string id = h.ClassId, name = c.TryClass(id)?.Name ?? id;
                string why = Tomes.WhyNot(c, s, tome.Id, id);
                var b = Ui.Button(name, () =>
                {
                    var r = Tomes.Teach(c, s, tome.Id, id);
                    App.Toast(r.Message, 2.4f);
                    App.Audio.Sfx(r.Ok ? "battle.powerup" : "ui.error");
                    if (r.Ok) App.Persist();
                    Build(box, tome, after, r.Ok ? name : null, sentence);
                    after?.Invoke(r);
                }, "btn--small", row);
                b.name = "teach-" + id;
                b.style.flexGrow = 1; b.style.flexShrink = 1; b.style.flexBasis = Length.Percent(30); b.style.minWidth = 0;
                Ui.SetEnabled(b, why == null);
                if (why != null) b.tooltip = why;
            }
        }

        /// <summary>A Chest's "Open" button: rolls it, toasts the haul, and calls <paramref name="opened"/> with what came out.</summary>
        public static Button OpenButton(VisualElement parent, ItemDef chest, Action<Result, Chests.Haul> opened)
        {
            var b = Ui.Button(App.Save.Count(chest.Id) > 1 ? $"Open ({App.Save.Count(chest.Id)})" : "Open", () =>
            {
                var r = Chests.Open(App.Content, App.Save, chest.Id, out var haul);
                App.Toast(r.Message, 2.8f);
                App.Audio.Sfx(r.Ok ? "battle.pickup" : "ui.error");
                if (r.Ok) App.Persist();
                opened?.Invoke(r, haul);
            }, "btn--small btn--primary", parent);
            b.name = "open-" + chest.Id;
            Ui.SetEnabled(b, App.Save.Count(chest.Id) > 0);
            return b;
        }
    }
}
