using System.Collections;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    // ── Mercenaries board (GG-T233, §25.10.1) ────────────────────────────────────

    public sealed class MercenaryScreen : HubPage
    {
        public override string Id => "mercenaries";
        protected override string Title => "Mercenaries";
        protected override string Backdrop => "desert";
        private bool haggling;

        protected override void BuildBody(VisualElement body)
        {
            var head = Ui.Window(null, body);
            Ui.Text("Swords for hire, paid upfront for a set number of battles. The count runs down every battle, whether they fight or wait in reserve. " +
                    "One haggle each until the next battle.", "small", head).style.whiteSpace = WhiteSpace.Normal;

            if (S.Mercenaries.Contracts.Count > 0)
            {
                var hired = Ui.Window("Under contract", body);
                hired.name = "merc-contracts";
                foreach (var k in S.Mercenaries.Contracts)
                {
                    var def = C.TryClass(k.ClassId);
                    if (def == null) continue;
                    var card = Ui.El("card", hired);
                    Ui.Thumb(k.ClassId, card, 96);
                    var col = Ui.El("col grow", card);
                    var top = Ui.El("row", col);
                    Ui.Text(def.Name, "h2", top);
                    MercenaryCamp.Tag(S, k.ClassId, top);
                    Ui.Text($"{k.BattlesLeft} battle{(k.BattlesLeft == 1 ? "" : "s")} left · {Ui.Gold(k.Rate)} a battle", "small", col);
                }
            }

            var w = Ui.Window("For hire", body);
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            var board = Mercenaries.Board(C, S);
            if (board.Count == 0) Ui.Text("No one's looking for work right now.", "small dim", list);
            foreach (var m in board)
            {
                var merc = m;
                var def = C.Class(m.Class);
                bool walked = Mercenaries.Walked(S, m.Class);
                var card = Ui.El("card", list);
                card.name = "merc-offer-" + m.Class;
                Ui.Thumb(m.Class, card, 140);
                var col = Ui.El("col grow", card);
                col.style.flexShrink = 1; col.style.minWidth = 0;
                var top = Ui.El("row", col);
                Ui.Text(def.Name, "h2", top);
                Ui.RoleBadge(def.Role, top).style.marginLeft = 6;
                Ui.Text(m.Temperament.ToString(), "tiny dim", top).style.marginLeft = 8;
                Ui.Text($"\"{m.Pitch}\"", "tiny", col).style.whiteSpace = WhiteSpace.Normal;
                int adj = Mercenaries.HaggleAdjust(S, m.Class);
                if (adj != 0) Ui.Text(adj < 0 ? $"Haggled down {-adj}%" : $"Price up {adj}%", adj < 0 ? "tiny gold" : "tiny bad", col);
                if (walked) { Ui.Text("Walked off. Back after the next battle.", "small bad", col); continue; }
                var terms = Ui.El("row", col);
                terms.style.flexWrap = Wrap.Wrap;
                terms.style.marginTop = 6;
                foreach (var t in m.Terms.OrderBy(x => x))
                {
                    int term = t, price = Mercenaries.Price(C, S, m.Class, t);
                    var b = Ui.Button($"{term} battles · {Ui.Gold(price)}", () => Confirm.Show(
                        $"Hire {def.Name} for {term} battles at {Ui.Gold(price)}, paid now? The contract runs down every battle, used or not.", "Hire",
                        () => Done(Mercenaries.Hire(C, S, merc.Class, term))), "btn--small", terms);
                    b.name = $"merc-hire-{m.Class}-{term}";
                    b.style.marginRight = 6; b.style.marginBottom = 4;
                    Ui.SetEnabled(b, S.Gold >= price && !haggling);
                }
                var haggle = Ui.Button(Mercenaries.Haggled(S, m.Class) ? "Haggled" : "Haggle", () => App.StartCoroutine(Haggle(merc, card)), "btn--small", terms);
                haggle.name = "merc-haggle-" + m.Class;
                Ui.SetEnabled(haggle, !Mercenaries.Haggled(S, m.Class) && !haggling);
            }
        }

        // The merc weighs the offer for a beat before answering (law 5: nothing lands in a single frame).
        private IEnumerator Haggle(MercenaryDef m, VisualElement card)
        {
            if (haggling) yield break;
            haggling = true;
            App.Audio.Sfx("ui.press");
            var think = Ui.Text("...", "h2 gold", card);
            yield return Tween.Run(Timing.Dramatic, k => { think.style.opacity = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)); });
            var o = Mercenaries.Haggle(C, S, m.Class, out var message);
            haggling = false;
            App.Audio.Sfx(o == HaggleOutcome.Dropped ? "ui.buy" : o == HaggleOutcome.Held ? "ui.select" : "ui.error");
            App.Toast(message);
            App.Persist();
            Refresh();
        }
    }

    /// <summary>Camp's side of the mercenary contracts (GG-T233): announce departures, raise the "nearly up" reoffer,
    /// and the MERC tag on hero cards.</summary>
    public static class MercenaryCamp
    {
        /// <summary>A small "MERC · N left" tag after a contracted hero's name; nothing for anyone else.</summary>
        public static void Tag(SaveData s, string classId, VisualElement parent)
        {
            var k = Mercenaries.Contract(s, classId);
            if (k == null) return;
            var tag = Ui.Text($"MERC · {k.BattlesLeft} left", "tiny gold", parent);
            tag.name = "merc-tag-" + classId;
            tag.style.marginLeft = 6;
        }

        /// <summary>Called from Camp's Build: settle spent contracts, toast who left, then open the reoffer for a merc
        /// on their last battle once the hub has settled.</summary>
        public static void OnCamp(GameScreen hub)
        {
            var app = GameApp.I;
            var c = app.Content; var s = app.Save;
            if (s == null || hub.Root == null) return;
            Mercenaries.Settle(c, s);
            var gone = Mercenaries.TakeDepartureNotices(s);
            var pending = Mercenaries.PendingRenewal(s);
            if (gone.Count == 0 && pending == null) return;
            if (gone.Count > 0) app.Persist();
            hub.Root.schedule.Execute(() =>
            {
                if (app.Router.Current != hub) return;
                if (gone.Count > 0)
                {
                    app.Audio.Sfx("ui.cancel");
                    app.Toast(string.Join(", ", gone.Select(id => c.TryClass(id)?.Name ?? id)) + (gone.Count == 1 ? "'s contract is up. They've moved on." : ": contracts up. They've moved on."), 2.6f);
                }
                if (pending != null && Mercenaries.PendingRenewal(s) == pending) app.Router.Push(new MercenaryRenewalScreen(pending.ClassId));
            }).ExecuteLater(gone.Count > 0 ? 900 : 400);
        }
    }

    /// <summary>The reoffer (GG-T233): a merc on the last battle of their contract asks to renew. Renew at a loyalty
    /// discount, finish the contract, or part ways now.</summary>
    public sealed class MercenaryRenewalScreen : GameScreen
    {
        private readonly string classId;
        private bool answered;
        public MercenaryRenewalScreen(string classId) { this.classId = classId; }
        public override string Id => "merc-renewal";

        private static ContentDb C => GameApp.I.Content;
        private static SaveData S => GameApp.I.Save;

        protected override void Build(VisualElement root)
        {
            var def = C.Class(classId);
            var m = C.TryMercenary(classId);
            var k = Mercenaries.Contract(S, classId);
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(HubScreen.ThemeFor(C, S), 2));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.42f, 0.55f);

            var portrait = Ui.El(null, root);
            portrait.name = "merc-portrait";
            portrait.style.position = Position.Absolute;
            portrait.style.left = 40; portrait.style.right = 40; portrait.style.top = 160; portrait.style.bottom = 980;
            portrait.style.backgroundImage = new StyleBackground(Art.Portrait(classId));
            portrait.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            portrait.style.opacity = 0f;

            var box = Ui.Window(null, root);
            box.name = "merc-renewal-box";
            box.style.position = Position.Absolute;
            box.style.left = 24; box.style.right = 24; box.style.bottom = 60;
            var plate = Ui.El("row", box);
            Ui.Text(def.Name, "h2 gold", plate);
            MercenaryCamp.Tag(S, classId, plate);
            var line = Ui.Text("", "small", box);
            line.style.whiteSpace = WhiteSpace.Normal;
            line.style.minHeight = 120;
            string speech = m?.Renewal ?? "My contract's nearly up. Renew, or do we part ways?";

            var choices = Ui.El("col", box);
            choices.style.marginTop = 12;
            choices.style.opacity = 0f;
            if (m != null && k != null)
            {
                int loyalty = Mathf.RoundToInt(Mercenaries.Loyalty(k) * 100f);
                Ui.Text($"Loyalty discount: {loyalty}%", "tiny gold", choices);
                foreach (var t in m.Terms.OrderBy(x => x))
                {
                    int term = t, price = Mercenaries.RenewalPrice(C, S, classId, t);
                    var b = Ui.Button($"Renew {term} more battles · {Ui.Gold(price)}", () => Answer(Mercenaries.Renew(C, S, classId, term)), null, choices);
                    b.name = $"merc-renew-{term}";
                    Ui.SetEnabled(b, S.Gold >= price);
                }
            }
            var finish = Ui.Button("Finish the contract (1 battle left)", () => { Mercenaries.FinishContract(S, classId); Answer(Result.Success($"{def.Name} will see out the last battle")); }, null, choices);
            finish.name = "merc-finish";
            var part = Ui.Button("Part ways now", () => Answer(Mercenaries.PartWays(C, S, classId)), null, choices);
            part.name = "merc-part";

            // The merc steps in, the line types out, then the answers rise (law 5: every beat plays over time).
            GameApp.I.StartCoroutine(Enter(portrait, line, speech, choices));
        }

        private IEnumerator Enter(VisualElement portrait, Label line, string speech, VisualElement choices)
        {
            App.Audio.Sfx("ui.select");
            yield return Tween.Run(Timing.Emphasis, k => { portrait.style.opacity = k; portrait.style.translate = new Translate(-120f * (1f - k), 0); }, Tween.EaseOut);
            yield return Tween.Run(Mathf.Clamp(speech.Length / 60f, 0.4f, 1.6f), k => line.text = speech.Substring(0, Mathf.RoundToInt(speech.Length * k)));
            App.Audio.Sfx("ui.click");
            yield return Tween.Run(Timing.Standard, k => choices.style.opacity = k);
        }

        private void Answer(Result r)
        {
            if (answered) return;
            if (!r.Ok) { App.Audio.Sfx("ui.error"); App.Toast(r.Message); return; }
            answered = true;
            App.Audio.Sfx("ui.buy");
            App.Toast(r.Message);
            App.Persist();
            App.Router.Back();
        }

        public override bool OnBack()
        {
            // Backing out is an answer too: they see out the contract, so the reoffer doesn't loop.
            if (!answered) { Mercenaries.FinishContract(S, classId); App.Persist(); answered = true; }
            return true;
        }
    }
}
