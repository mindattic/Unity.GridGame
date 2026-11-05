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
    /// A conversation (§27.1, GG-T117, GG-T225): the speakers stand on the left and right as their whole, uncropped
    /// portraits, each with a name plate on the one shared text box below. The lines trade back and forth in that
    /// box — each replaces the last and types out — and whoever is talking has their name plate lit (theme accent,
    /// glow) and their portrait full-bright and nudged forward, while the listener's plate and portrait dim. The
    /// player's choices appear in the same box. It only draws what <see cref="DialogueRunner"/> says; every save
    /// change happens there.
    /// </summary>
    public sealed class DialogueScreen : GameScreen
    {
        private const float CharsPerSecond = 70f;
        /// <summary>How long a talking/listening swap (and a speaker stepping on or off) takes.</summary>
        private const float SwapSeconds = 0.2f;
        private readonly DialogueDef def;
        private readonly string with;
        private DialogueRunner run;
        private readonly GameScreen next;
        private readonly Func<GameScreen> nextFactory;
        private readonly string backdrop;
        private readonly Side[] sides = { new Side(0), new Side(1) };
        private int talking = -1;
        private VisualElement controls;
        private Label typing, typingLabel;
        private string typingText;
        private float typed;
        private int shownLines, shownLog;
        private bool done;

        /// <summary>One side of the conversation: who stands there, their whole portrait and their name plate.
        /// <see cref="Lit"/> (talking 1 / listening 0) and <see cref="Present"/> (on stage 1 / off 0) ease toward
        /// their targets every frame, so no change is ever a single-frame swap.</summary>
        private sealed class Side
        {
            public readonly int Index;
            public string Speaker, Portrait, Name;
            /// <summary>A new occupant waiting for the old one to step off (Present reaching 0) before stepping on.</summary>
            public bool Pending;
            public string PendingSpeaker, PendingPortrait, PendingName;
            public float Lit, LitTarget, Present, PresentTarget;
            public VisualElement Art, Plate, GlowNear, GlowFar;
            public Label NameLabel;
            public Side(int index) { Index = index; }
            public float Toward => Index == 0 ? 1f : -1f; // +x points toward the middle of the screen
        }

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

        /// <summary>A conversation with <paramref name="with"/> as "$with" that then goes to whatever <paramref name="next"/> builds.</summary>
        public static DialogueScreen WithThen(DialogueDef def, string with, Func<GameScreen> next) => new DialogueScreen(next, def, with);

        private DialogueScreen(Func<GameScreen> next, DialogueDef def, string with) : this(def, with, (GameScreen)null)
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
            talking = -1;
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(backdrop, 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.5f);

            // The stage: a whole portrait standing on each side, feet behind the text box.
            var stage = Ui.El(null, root);
            stage.name = "dlg-stage";
            stage.style.position = Position.Absolute;
            stage.style.left = 0; stage.style.right = 0; stage.style.top = 150; stage.style.bottom = 640;
            stage.RegisterCallback<ClickEvent>(_ => TapLine());

            var title = Ui.Text(def.Title ?? "", "small dim shadow", root);
            title.style.position = Position.Absolute; title.style.left = 30; title.style.top = 40;

            var skip = Ui.Button("Skip ›", SkipAhead, "btn--small", root);
            skip.name = "dlg-skip";
            skip.style.position = Position.Absolute; skip.style.right = 24; skip.style.top = 24;

            // The one shared text box: each line replaces the last in it; the choices sit in it too.
            var box = Ui.Window(null, root, "window--plain");
            box.name = "dlg-box";
            box.style.position = Position.Absolute;
            box.style.left = 20; box.style.right = 20; box.style.bottom = 50;
            box.style.minHeight = 560;
            box.style.paddingTop = 60;
            box.style.overflow = Overflow.Visible;
            box.RegisterCallback<ClickEvent>(e =>
            {
                // A choice/Next button's click bubbles up here too; only a tap on the box itself is "next line".
                if (e.target is VisualElement t && controls != null && (t == controls || controls.Contains(t))) return;
                TapLine();
            });
            var text = Ui.Text("", "h2", box);
            text.name = "dlg-text";
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityFontStyleAndWeight = FontStyle.Normal;
            text.style.fontSize = 44;
            text.style.flexGrow = 1;
            text.style.minHeight = 230;
            typing = null;

            controls = Ui.El("col", box);
            controls.name = "dlg-controls";
            controls.style.justifyContent = Justify.FlexEnd;

            foreach (var s in sides)
            {
                s.Speaker = s.Portrait = s.Name = null;
                s.Pending = false;
                s.Lit = s.LitTarget = s.Present = s.PresentTarget = 0f;
                BuildSide(s, stage, box);
            }

            typingLabel = text;
            ShowNew();
            if (DemoMode.Active) App.StartCoroutine(AutoPlay());
        }

        /// <summary>Builds a side's whole-portrait frame (contain-fit, bottom-aligned, never a thumbnail framing) and
        /// its name plate, which sits on the text box's top edge with two soft glow layers behind it.</summary>
        private const float FigureHeight = 1200f;

        private static void BuildSide(Side s, VisualElement stage, VisualElement box)
        {
            string tag = s.Index == 0 ? "left" : "right";
            var p = Ui.El(null, stage);
            p.name = "dlg-portrait-" + tag;
            p.pickingMode = PickingMode.Ignore;
            p.style.position = Position.Absolute;
            p.style.bottom = 0; p.style.height = FigureHeight; p.style.width = FigureHeight;
            if (s.Index == 0) p.style.left = -70; else p.style.right = -70;
            p.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            p.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom);
            p.style.opacity = 0f;
            s.Art = p;

            var plate = Ui.El(null, box);
            plate.name = "dlg-plate-" + tag;
            plate.pickingMode = PickingMode.Ignore;
            plate.style.position = Position.Absolute;
            plate.style.top = -58;
            if (s.Index == 0) plate.style.left = 36; else plate.style.right = 36;
            plate.style.height = 92;
            plate.style.minWidth = 260;
            plate.style.paddingLeft = plate.style.paddingRight = 34;
            plate.style.justifyContent = Justify.Center;
            plate.style.alignItems = Align.Center;
            SetRadius(plate, 22);
            SetBorder(plate, 4);
            plate.style.opacity = 0f;
            s.GlowFar = Glow(plate, 22, 0.18f);
            s.GlowNear = Glow(plate, 10, 0.4f);
            s.NameLabel = Ui.Text("", null, plate);
            s.NameLabel.name = "dlg-name-" + tag;
            s.NameLabel.style.fontSize = 44;
            Fonts.UseHeavy(s.NameLabel);
            s.NameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            s.Plate = plate;
        }

        private static VisualElement Glow(VisualElement plate, float spread, float alpha)
        {
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            g.style.position = Position.Absolute;
            g.style.left = g.style.right = g.style.top = g.style.bottom = -spread;
            SetRadius(g, 22 + spread);
            g.style.opacity = 0f;
            g.userData = alpha;
            plate.Add(g);
            return g;
        }

        private static void SetRadius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        private static void SetBorder(VisualElement e, float w) =>
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;

        public override void Tick(float dt)
        {
            foreach (var s in sides) Animate(s, dt);
            if (typing == null || typed >= typingText.Length) return;
            int before = (int)typed;
            typed = Mathf.Min(typingText.Length, typed + dt * CharsPerSecond);
            if ((int)typed != before) typing.text = typingText.Substring(0, (int)typed);
            if (typed >= typingText.Length) LineDone();
        }

        /// <summary>Eases a side toward its targets and draws it: the talker's portrait full-bright, full-size and
        /// nudged toward the middle, its plate lit in the theme accent with a glow; the listener's dimmed, shrunk
        /// back, plate greyed. A side being handed to a new speaker steps the old one off first.</summary>
        private void Animate(Side s, float dt)
        {
            float step = dt / SwapSeconds;
            s.Lit = Mathf.MoveTowards(s.Lit, s.LitTarget, step);
            float presentTarget = s.Pending ? 0f : s.PresentTarget;
            s.Present = Mathf.MoveTowards(s.Present, presentTarget, step);
            if (s.Pending && s.Present <= 0f)
            {
                s.Pending = false;
                Occupy(s, s.PendingSpeaker, s.PendingPortrait, s.PendingName);
            }
            if (s.Art == null) return;

            float lit = Tween.EaseOut(s.Lit), present = Tween.EaseOut(s.Present);
            var accent = Art.ThemeAccent;

            s.Art.style.opacity = s.Portrait != null ? present : 0f;
            float tint = Mathf.Lerp(0.42f, 1f, lit);
            s.Art.style.unityBackgroundImageTintColor = new Color(tint, tint, tint);
            s.Art.style.scale = new Scale(Vector2.one * Mathf.Lerp(0.93f, 1f, lit));
            s.Art.style.translate = new Translate(s.Toward * (Mathf.Lerp(0f, 40f, lit) - (1f - present) * 140f), 0);

            s.Plate.style.opacity = present * Mathf.Lerp(0.6f, 1f, lit);
            s.Plate.style.scale = new Scale(Vector2.one * Mathf.Lerp(0.9f, 1.06f, lit));
            s.Plate.style.translate = new Translate(0, Mathf.Lerp(8f, 0f, lit));
            var dimBg = Palette.Ink.WithA(0.92f);
            var litBg = Color.Lerp(accent, Color.black, 0.3f); litBg.a = 0.98f;
            s.Plate.style.backgroundColor = Color.Lerp(dimBg, litBg, lit);
            var border = Color.Lerp(new Color(0.42f, 0.46f, 0.62f), Color.Lerp(accent, Color.white, 0.55f), lit);
            s.Plate.style.borderTopColor = s.Plate.style.borderBottomColor = s.Plate.style.borderLeftColor = s.Plate.style.borderRightColor = border;
            s.NameLabel.style.color = Color.Lerp(Palette.TextDim, Color.white, lit);
            foreach (var g in new[] { s.GlowNear, s.GlowFar })
            {
                var c = accent; c.a = (float)g.userData;
                g.style.backgroundColor = c;
                g.style.opacity = lit;
            }
        }

        private static void Occupy(Side s, string speaker, string portrait, string name)
        {
            s.Speaker = speaker;
            s.Portrait = HasPortrait(portrait) ? portrait : null;
            s.Name = name;
            if (s.Art == null) return;
            var tex = s.Portrait != null ? Art.Portrait(s.Portrait) : null;
            s.Art.style.backgroundImage = new StyleBackground(tex);
            // Same on-screen height for every speaker; canvases vary from 336x640 to 640x640, so width follows the art.
            if (tex != null) s.Art.style.width = FigureHeight * tex.width / (float)tex.height;
            s.NameLabel.text = name ?? "";
            s.Plate.style.display = string.IsNullOrEmpty(name) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private bool Typing => typing != null && typed < typingText.Length;

        /// <summary>Puts the newest transcript line in the box (one per Advance/Choose; lines rushed past by Skip
        /// still claim their sides) and toasts what effects did.</summary>
        private void ShowNew()
        {
            while (shownLog < run.Log.Count)
            {
                App.Toast(run.Log[shownLog++]);
                App.Audio.Sfx("ui.buy");
            }
            if (run.Finished) { Finish(); return; }
            if (shownLines >= run.Transcript.Count) return;
            int before = talking;
            DialogueLine line = null;
            while (shownLines < run.Transcript.Count)
            {
                line = run.Transcript[shownLines++];
                Speak(line);
            }
            if (talking != before && before >= 0) App.Audio.Sfx("slide", 0.45f);
            ShowLine(line);
        }

        /// <summary>Hands the line's speaker a side and lights it: the talker's plate and portrait come up, the
        /// other side's go down.</summary>
        private void Speak(DialogueLine line)
        {
            int side = SideFor(line.SpeakerId);
            var s = sides[side];
            string speaker = line.SpeakerId ?? "";
            string portrait = HasPortrait(line.Portrait) ? line.Portrait : null;
            bool same = s.Pending ? s.PendingSpeaker == speaker : s.Speaker == speaker;
            if (!same || (s.Pending ? s.PendingPortrait : s.Portrait) != portrait)
            {
                if (s.Speaker == null || s.Present <= 0f) { s.Pending = false; Occupy(s, speaker, portrait, line.Name); }
                else { s.Pending = true; s.PendingSpeaker = speaker; s.PendingPortrait = portrait; s.PendingName = line.Name; }
            }
            s.PresentTarget = 1f;
            s.LitTarget = 1f;
            sides[1 - side].LitTarget = 0f;
            if (talking != side) s.Art?.BringToFront();
            talking = side;
        }

        private void ShowLine(DialogueLine line)
        {
            typing = typingLabel;
            typingText = line.Text ?? "";
            typed = 0;
            typing.text = "";
            controls.Clear();
            App.Audio.Sfx("ui.select");
        }

        /// <summary>The side a speaker stands on: theirs if they already have one, else a free one, else (a third
        /// speaker) the side of whoever is not talking right now, so the one who just spoke stays put.</summary>
        private int SideFor(string speakerId)
        {
            speakerId ??= "";
            for (int i = 0; i < 2; i++)
                if ((sides[i].Pending ? sides[i].PendingSpeaker : sides[i].Speaker) == speakerId) return i;
            for (int i = 0; i < 2; i++)
                if (sides[i].Speaker == null && !sides[i].Pending) return i;
            return talking < 0 ? 0 : 1 - talking;
        }

        /// <summary>True when there is portrait art for <paramref name="id"/> (Resources/Portraits/&lt;id&gt;): a class,
        /// or an NPC speaker's own <c>Portrait</c>. Dialogue always shows it whole (GG-T225).</summary>
        internal static bool HasPortrait(string id) => !string.IsNullOrEmpty(id) && !id.StartsWith("$") && Art.Exists("Portraits/" + id);

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
                var b = Ui.Button(run.Node.Next == null ? "Done ›" : "Next ›", Advance, "btn--primary btn--small", controls);
                b.name = "dlg-next";
                b.style.alignSelf = Align.FlexEnd; b.style.width = 300;
                b.style.opacity = 0f;
                App.StartCoroutine(FadeUp(b, 0f));
            }
        }

        private IEnumerator FadeUp(VisualElement e, float delay)
        {
            if (delay > 0) yield return Tween.Wait(delay);
            yield return Tween.Run(Timing.Quick, k => { e.style.opacity = k; e.style.translate = new Translate(0, Mathf.Lerp(30f, 0f, k)); }, Tween.EaseOut);
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
                var row = Ui.MenuRow(name, value, () => App.Router.Push(new DialogueScreen(topic, npc ? null : who, null)),
                    list.contentContainer, null, topic != null);
                var ic = Ui.El("menu-icon");
                if (npc)
                {
                    // An NPC is a person, not a party hero: their own whole portrait (the topic's first speaker with art),
                    // contain-fit — the same picture that stands in their conversation (GG-T225).
                    string art = topic?.Speakers.Select(sp => sp.Portrait ?? sp.Class).FirstOrDefault(DialogueScreen.HasPortrait);
                    if (art != null)
                    {
                        ic.style.width = 80; ic.style.height = 96;
                        ic.style.backgroundImage = new StyleBackground(Art.Portrait(art));
                        ic.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom);
                        row.Insert(1, ic); // after the cursor, where MenuRow puts an icon
                    }
                }
                else if (DialogueScreen.HasPortrait(who))
                {
                    // A hero keeps the face framing every other hero list uses (Thumbnail, GG-T221).
                    row.Insert(1, ic);
                    Ui.PortraitThumb(ic, who);
                }
                row.name = "talk-" + (npc ? npcId : who);
                if (topic != null && !S.SeenDialogues.Contains(topic.Id)) row.Q<Label>("value").AddToClassList("gold");
            }
            Ui.SelectFirst(list.contentContainer);
        }
    }
}
