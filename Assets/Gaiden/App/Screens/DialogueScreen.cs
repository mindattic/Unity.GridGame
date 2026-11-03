using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// A conversation (§27.1, GG-T117): the speakers' portraits overlap across the top (the one talking full-bright
    /// and in front, the other dimmed behind), the whole conversation so far stacks below as overlapping boxes that
    /// slide in from the speaker's side, and the player's choices sit at the bottom. It only draws what
    /// <see cref="DialogueRunner"/> says; every save change happens there.
    /// </summary>
    public sealed class DialogueScreen : GameScreen
    {
        private const float CharsPerSecond = 70f;
        private readonly DialogueDef def;
        private readonly string with;
        private DialogueRunner run;
        private readonly GameScreen next;
        private readonly Func<GameScreen> nextFactory;
        private readonly string backdrop;
        private readonly Dictionary<string, int> sideOf = new Dictionary<string, int>();
        private readonly VisualElement[] portraits = new VisualElement[2];
        private readonly string[] portraitIds = new string[2];
        private ScrollView log;
        private VisualElement controls;
        private Label typing;
        private string typingText;
        private float typed;
        private int shownLines, shownLog;
        private bool done;

        /// <param name="next">Screen to replace this one with when it ends (a story beat's battle); null = go back (Camp Talk).</param>
        public DialogueScreen(DialogueDef def, string with, GameScreen next, string backdrop = null)
        {
            this.def = def; this.with = with;
            this.next = next;
            this.backdrop = def.Backdrop ?? backdrop ?? HubScreen.ThemeFor(GameApp.I.Content, GameApp.I.Save);
        }

        /// <param name="next">Builds the screen to replace this one with once it ends — decided then, not now, so a
        /// chain of story beats sees what the earlier ones did (GG-T119).</param>
        public DialogueScreen(DialogueDef def, Func<GameScreen> next, string backdrop = null) : this(def, null, (GameScreen)null, backdrop)
        {
            nextFactory = next;
        }

        public override string Id => "dialogue";
        /// <summary>Keeps whatever was playing (the crawl's theme before a beat, camp music for a chat).</summary>
        public override string Music => null;

        /// <summary>Without this, a screen change away from a still-playing dialogue (demo-mode AutoPlay is the
        /// only thing that keeps running on its own) left AutoPlay ticking on a popped screen: it kept tapping
        /// through lines, applying effects and eventually called Finish(), which Replace()/Back()'d whatever
        /// screen had since been mounted — the same failure class as LevelUpScreen's fanfare coroutine (GG-T127).</summary>
        public override void OnHidden() => done = true;

        protected override void Build(VisualElement root)
        {
            // Started on first show, not at construction: a beat queued behind a story crawl mustn't apply its
            // first line's effects before the player sees it.
            run ??= new DialogueRunner(App.Content, App.Save, def, with);
            shownLines = shownLog = 0;
            sideOf.Clear();
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(backdrop, 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.5f);

            // Portrait stage: two overlapping frames, left and right.
            var stage = Ui.El(null, root);
            stage.name = "dlg-stage";
            stage.style.position = Position.Absolute;
            stage.style.left = 0; stage.style.right = 0; stage.style.top = 110; stage.style.height = 1060;
            for (int i = 0; i < 2; i++)
            {
                var p = Ui.El(null, stage);
                p.name = "dlg-portrait-" + (i == 0 ? "left" : "right");
                p.pickingMode = PickingMode.Ignore;
                p.style.position = Position.Absolute;
                p.style.top = 0; p.style.bottom = 0; p.style.width = 700;
                if (i == 0) p.style.left = -20; else p.style.right = -20;
                p.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                p.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom);
                p.style.opacity = 0f;
                portraits[i] = p;
            }
            var title = Ui.Text(def.Title ?? "", "small dim shadow", root);
            title.style.position = Position.Absolute; title.style.left = 30; title.style.top = 40;

            var skip = Ui.Button("Skip ›", SkipAhead, "btn--small", root);
            skip.name = "dlg-skip";
            skip.style.position = Position.Absolute; skip.style.right = 24; skip.style.top = 24;

            // The conversation: overlapping boxes, newest at the bottom, overlapping the portraits' feet.
            log = Ui.List(root);
            log.name = "dlg-log";
            log.style.position = Position.Absolute;
            log.style.left = 16; log.style.right = 16; log.style.top = 980; log.style.bottom = 560;
            log.RegisterCallback<ClickEvent>(_ => TapLine());
            stage.pickingMode = PickingMode.Position;
            stage.RegisterCallback<ClickEvent>(_ => TapLine());

            controls = Ui.El("col", root);
            controls.name = "dlg-controls";
            controls.style.position = Position.Absolute;
            controls.style.left = 40; controls.style.right = 40; controls.style.bottom = 70; controls.style.height = 470;
            controls.style.justifyContent = Justify.FlexEnd;

            ShowNew();
            if (DemoMode.Active) App.StartCoroutine(AutoPlay());
        }

        public override void Tick(float dt)
        {
            if (typing == null || typed >= typingText.Length) return;
            int before = (int)typed;
            typed = Mathf.Min(typingText.Length, typed + dt * CharsPerSecond);
            if ((int)typed != before) typing.text = typingText.Substring(0, (int)typed);
            if (typed >= typingText.Length) LineDone();
        }

        private bool Typing => typing != null && typed < typingText.Length;

        /// <summary>Draws every transcript line not on screen yet (one per Advance/Choose) and toasts what effects did.</summary>
        private void ShowNew()
        {
            while (shownLog < run.Log.Count)
            {
                App.Toast(run.Log[shownLog++]);
                App.Audio.Sfx("ui.buy");
            }
            if (run.Finished) { Finish(); return; }
            while (shownLines < run.Transcript.Count)
            {
                var line = run.Transcript[shownLines++];
                AddBox(line, shownLines == run.Transcript.Count);
            }
            // Every box but the newest steps back.
            var boxes = log.contentContainer.Children().ToList();
            for (int i = 0; i < boxes.Count - 1; i++) boxes[i].style.opacity = 0.62f;
        }

        private void AddBox(DialogueLine line, bool current)
        {
            int side = SideFor(line.SpeakerId);
            ShowPortrait(side, line.Portrait);

            var box = Ui.Window(null, log.contentContainer, "window--plain");
            box.name = "dlg-line-" + (shownLines - 1);
            box.style.marginTop = shownLines > 1 ? -40 : 0; // boxes overlap like a stack of cards
            if (side == 0) box.style.marginRight = 110; else box.style.marginLeft = 110;
            var head = Ui.El("row", box);
            head.style.alignItems = Align.Center;
            if (HasFace(line.Portrait))
            {
                var face = Ui.El("thumb", head);
                face.style.width = face.style.height = 64; face.style.marginRight = 14;
                Ui.PortraitThumb(face, line.Portrait);
            }
            Ui.Text(line.Name, "small gold", head).style.unityFontStyleAndWeight = FontStyle.Bold;
            var text = Ui.Text(current ? "" : line.Text, "small", box);
            text.name = "dlg-text";
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.marginTop = 6;
            if (line.Answer != null)
            {
                var a = Ui.Text("› " + line.Answer, "tiny dim", box);
                a.style.marginTop = 6; a.style.whiteSpace = WhiteSpace.Normal;
            }
            if (!current) return;
            typing = text; typingText = line.Text ?? ""; typed = 0;
            controls.Clear();
            App.Audio.Sfx("ui.select");
            App.StartCoroutine(SlideIn(box, side));
            box.schedule.Execute(() => log.ScrollTo(box)).ExecuteLater(30);
        }

        private int SideFor(string speakerId)
        {
            if (speakerId == null) return 0;
            if (!sideOf.TryGetValue(speakerId, out var side)) sideOf[speakerId] = side = sideOf.Count % 2;
            return side;
        }

        /// <summary>True when <paramref name="id"/> is a class with portrait art, so its Thumbnail can frame a face (GG-T221).
        /// Portrait-only speakers (NPC art that isn't a class) have no framing and, as before, show no face.</summary>
        internal static bool HasFace(string id) => !string.IsNullOrEmpty(id) && !id.StartsWith("$")
            && GameApp.I?.Content?.TryClass(id)?.Thumbnail != null && Art.Exists("Portraits/" + id);

        /// <summary>Brings the talking speaker's frame forward at full brightness and dims the other one behind it.</summary>
        private void ShowPortrait(int side, string portraitId)
        {
            var p = portraits[side];
            bool swap = portraitIds[side] != portraitId;
            portraitIds[side] = portraitId;
            if (swap) p.style.backgroundImage = new StyleBackground(portraitId != null ? Art.Portrait(portraitId) : null);
            p.BringToFront();
            App.StartCoroutine(Spotlight(p, swap));
            var other = portraits[1 - side];
            if (portraitIds[1 - side] != null) App.StartCoroutine(Dim(other));
        }

        private IEnumerator Spotlight(VisualElement p, bool enter)
        {
            float o0 = enter ? 0f : p.resolvedStyle.opacity;
            float x0 = enter ? (p == portraits[0] ? -80f : 80f) : 0f;
            yield return Tween.Run(0.22f, k =>
            {
                p.style.opacity = Mathf.Lerp(o0, 1f, k);
                p.style.translate = new Translate(Mathf.Lerp(x0, 0f, k), 0);
                p.style.scale = new Scale(Vector2.one * Mathf.Lerp(0.93f, 1f, k));
                float t = Mathf.Lerp(0.45f, 1f, k);
                p.style.unityBackgroundImageTintColor = new Color(t, t, t);
            }, Tween.EaseOut);
        }

        private IEnumerator Dim(VisualElement p)
        {
            yield return Tween.Run(0.22f, k =>
            {
                float t = Mathf.Lerp(1f, 0.45f, k);
                p.style.unityBackgroundImageTintColor = new Color(t, t, t);
                p.style.scale = new Scale(Vector2.one * Mathf.Lerp(1f, 0.93f, k));
                p.style.opacity = Mathf.Max(p.resolvedStyle.opacity, 0.9f);
            });
        }

        private IEnumerator SlideIn(VisualElement box, int side)
        {
            float from = side == 0 ? -140f : 140f;
            box.style.opacity = 0f;
            yield return Tween.Run(0.2f, k =>
            {
                box.style.opacity = k;
                box.style.translate = new Translate(Mathf.Lerp(from, 0f, k), 0);
            }, Tween.EaseOut);
        }

        /// <summary>The line has fully typed out: offer its choices, or a Next button.</summary>
        private void LineDone()
        {
            if (typing != null) typing.text = typingText;
            controls.Clear();
            if (run.AwaitingChoice)
            {
                var choices = run.Choices;
                for (int i = 0; i < choices.Count; i++)
                {
                    int idx = i;
                    var b = Ui.Button(choices[i].Label, () => Choose(idx), i == 0 ? "btn--primary" : null, controls);
                    b.name = "choice-" + i;
                    b.style.marginTop = 10;
                    b.style.opacity = 0f;
                    App.StartCoroutine(FadeUp(b, i * 0.06f));
                }
            }
            else
            {
                var b = Ui.Button(run.Node.Next == null ? "Done ›" : "Next ›", Advance, "btn--primary", controls);
                b.name = "dlg-next";
                b.style.alignSelf = Align.FlexEnd; b.style.width = 360;
            }
        }

        private IEnumerator FadeUp(VisualElement e, float delay)
        {
            if (delay > 0) yield return Tween.Wait(delay);
            yield return Tween.Run(0.18f, k => { e.style.opacity = k; e.style.translate = new Translate(0, Mathf.Lerp(30f, 0f, k)); }, Tween.EaseOut);
        }

        private void TapLine()
        {
            if (done) return;
            if (Typing) { typed = typingText.Length; typing.text = typingText; LineDone(); return; }
            if (!run.AwaitingChoice) Advance();
        }

        private void Advance()
        {
            if (done || Typing || !run.Advance()) return;
            ShowNew();
        }

        private void Choose(int index)
        {
            if (done) return;
            var r = run.Choose(index);
            if (!r.Ok) { App.Audio.Sfx("ui.error"); return; }
            App.Audio.Sfx("ui.confirm");
            // Re-draw the answered box with its answer, then carry on.
            var last = log.contentContainer.Children().LastOrDefault();
            if (last != null)
            {
                var a = Ui.Text("› " + run.Transcript[shownLines - 1].Answer, "tiny dim", last);
                a.style.marginTop = 6; a.style.whiteSpace = WhiteSpace.Normal;
            }
            App.Persist();
            ShowNew();
        }

        /// <summary>Skip ›: rushes through lines up to the next choice (choices are never skipped — they can change the
        /// save), or to the end.</summary>
        private void SkipAhead()
        {
            if (done) return;
            if (Typing) { typed = typingText.Length; typing.text = typingText; }
            int guard = 0;
            while (!run.Finished && !run.AwaitingChoice && guard++ < 200) run.Advance();
            ShowNew();
            if (!done && typing != null) { typed = typingText.Length; LineDone(); }
        }

        private IEnumerator AutoPlay()
        {
            while (!done)
            {
                yield return Tween.Wait(1.4f);
                if (done) yield break;
                if (Typing) TapLine();
                else if (run.AwaitingChoice) Choose(run.Choices.Count - 1); // the last choice is the way out by convention
                else Advance();
            }
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            controls?.Clear();
            App.Persist();
            var then = next ?? nextFactory?.Invoke();
            if (then != null) App.Router.Replace(then); else App.Router.Back();
        }
    }

    /// <summary>Camp's Talk row (§27.1, GG-T117): the party and every joined Camp NPC, each with what they'd talk about.</summary>
    public sealed class TalkScreen : HubPage
    {
        public override string Id => "talk";
        protected override string Title => "Talk";

        protected override void BuildBody(VisualElement body)
        {
            var w = Ui.Window(null, body);
            w.style.flexGrow = 1;
            var list = Ui.List(w);
            foreach (var who in DialogueRunner.TalkPartners(S))
            {
                bool npc = who.StartsWith(ContentLoader.NpcFlagPrefix);
                string npcId = npc ? who.Substring(ContentLoader.NpcFlagPrefix.Length) : null;
                var topic = DialogueRunner.TalkTopic(C, S, who);
                string name = npc ? DialogueRunner.Npc(npcId) : C.TryClass(who)?.Name ?? who;
                string value = topic == null ? "Nothing to say" : S.SeenDialogues.Contains(topic.Id) ? topic.Title ?? "" : "New!";
                // An NPC has no class portrait; borrow the face of the topic's first speaker that has one.
                string face = npc ? topic?.Speakers.Select(sp => sp.Portrait ?? sp.Class).FirstOrDefault(DialogueScreen.HasFace) : who;
                var row = Ui.MenuRow(name, value, () => App.Router.Push(new DialogueScreen(topic, npc ? null : who, null)),
                    list.contentContainer, null, topic != null);
                if (DialogueScreen.HasFace(face))
                {
                    var ic = Ui.El("menu-icon");
                    row.Insert(1, ic); // after the cursor, where MenuRow puts an icon
                    Ui.PortraitThumb(ic, face);
                }
                row.name = "talk-" + (npc ? npcId : who);
                if (topic != null && !S.SeenDialogues.Contains(topic.Id)) row.Q<Label>("value").AddToClassList("gold");
            }
            Ui.SelectFirst(list.contentContainer);
        }
    }
}
