using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// GG-T271 / GG-T305 — the one marquee every single-line text shares (buttons, tabs, menu rows, names, chips, badges,
    /// headers): a TextElement whose one line is wider than its box is never clipped or cut. <see cref="TextFit"/> finds
    /// every such element each pass and calls <see cref="Check"/>; nothing needs to opt in. The element keeps its text and
    /// its layout exactly (its own glyphs are drawn invisibly via a leading &lt;alpha=#01&gt;), and an absolute, clipping
    /// overlay laid over its content box (a sibling: a TextElement with a child loses its text measure) draws the same
    /// text in the same font, size, colour and shadow, following its visibility and opacity. That copy
    /// waits at the left, scrolls smoothly until the end is in view, flashes three times, snaps back to the left-most
    /// position and starts again — only while the text overflows; text that fits is static and untouched. Pause-aware
    /// (Tween.Dt) but not sped up by Battle speed, so it always reads at the same pace. Reduce Motion: an ellipsis instead.
    /// </summary>
    public static class Marquee
    {
        /// <summary>The clipping overlay box (the layout audit treats text inside it as handled).</summary>
        public const string BoxClass = "marquee";
        /// <summary>An element whose overflowing line is being handled by a marquee (or, with Reduce Motion, an ellipsis).</summary>
        public const string HostClass = "marquee-host";

        /// <summary>Design px per second: about the story crawl's reading speed.</summary>
        public const float ScrollSpeed = 90f;
        public const float HoldStart = 1.2f, HoldEnd = 0.3f, FlashHalf = 0.16f, FlashLow = 0.2f, AfterFlash = 0.2f;
        public const int Flashes = 3;

        // Alpha 1/255, not 0: fully transparent glyphs are dropped from the text's measured size, which collapsed the
        // element; at 0.4% they still lay out exactly and are invisible on any background.
        private const string Hide = "<alpha=#01>";
        private static readonly Regex ColorTags = new Regex("</?(color|alpha)[^>]*>", RegexOptions.IgnoreCase);

        private sealed class State
        {
            public VisualElement Clip;
            public Label Inner;
            public string Source;
            public string Hidden;
            public bool Ellipsis;
            public bool Running;
            public bool ShadowMoved;
            public TextShadow Shadow;
        }

        private static readonly ConditionalWeakTable<TextElement, State> states = new ConditionalWeakTable<TextElement, State>();

        private static bool ReduceMotion => GameApp.I?.Settings?.ReduceMotion == true;

        /// <summary>
        /// Marks <paramref name="label"/> as a one-line text that may be narrower than its words: no wrap, free to shrink
        /// below its text (and to grow into the row with <paramref name="grow"/>). Overflow then becomes a marquee.
        /// </summary>
        public static Label Apply(Label label, bool grow = true)
        {
            if (label == null) return null;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.minWidth = Space.S0;
            label.style.flexShrink = 1f;
            if (grow) label.style.flexGrow = 1f;
            return label;
        }

        /// <summary>The text the element shows (its own text minus the marquee's hiding tag).</summary>
        public static string SourceOf(TextElement e)
        {
            if (e == null) return null;
            return states.TryGetValue(e, out var st) && st.Hidden != null && e.text == st.Hidden ? st.Source : e.text;
        }

        private static float Measure(TextElement e, string s) =>
            e.MeasureTextSize(s ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

        /// <summary>One pass over a single-line element whose content box is <paramref name="width"/> wide.</summary>
        public static void Check(TextElement e, float width)
        {
            states.TryGetValue(e, out var st);
            if (st != null && st.Clip != null && (e.parent == null || st.Clip.parent != e.parent))
            {
                // The element was cleared or rebuilt under us: start over from what it shows now.
                Drop(e, st, restoreText: e.text == st.Hidden);
                st = null;
            }
            string source = st != null && st.Hidden != null && e.text == st.Hidden ? st.Source : e.text;
            if (string.IsNullOrEmpty(source))
            {
                if (st != null) Drop(e, st, false);
                return;
            }
            bool over = Measure(e, source) > width + 1f;
            if (!over)
            {
                if (st != null) Drop(e, st, e.text == st.Hidden);
                return;
            }
            if (st == null)
            {
                st = new State();
                states.Add(e, st);
                e.AddToClassList(HostClass);
            }
            if (ReduceMotion) { ToEllipsis(e, st, source); return; }
            if (st.Ellipsis) { UndoEllipsis(e); st.Ellipsis = false; }
            Show(e, st, source);
            Sync(e, st);
            if (!st.Running && GameApp.I != null) GameApp.I.StartCoroutine(Loop(e, st));
        }

        private static void Show(TextElement e, State st, string source)
        {
            if (st.Clip == null)
            {
                st.Clip = new VisualElement { name = "marquee", pickingMode = PickingMode.Ignore };
                st.Clip.AddToClassList(BoxClass);
                st.Clip.style.position = Position.Absolute;
                st.Clip.style.flexDirection = FlexDirection.Row;
                st.Clip.style.justifyContent = Justify.FlexStart;
                st.Clip.style.overflow = Overflow.Hidden;
                st.Inner = new Label { pickingMode = PickingMode.Ignore, enableRichText = e.enableRichText };
                var s = st.Inner.style;
                s.whiteSpace = WhiteSpace.NoWrap;
                s.flexShrink = 0f;
                s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = Space.S0;
                s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = Space.S0;
                s.unityTextAlign = TextAnchor.MiddleLeft;
                st.Clip.Add(st.Inner);
                // A sibling, not a child: a TextElement with any child loses its text measure (Yoga) and collapses.
                // Last in the parent, so it draws over the element and shifts no other sibling's index.
                e.parent.Add(st.Clip);
                // The element's text was set from code: show the new text at once, not on the next pass.
                e.RegisterCallback<ChangeEvent<string>>(OnTextChanged);
                e.RegisterCallback<GeometryChangedEvent>(OnMoved);
            }
            if (st.Source != source || st.Inner.text != source)
            {
                st.Source = source;
                st.Inner.text = source;
                st.Inner.style.translate = new Translate(0, 0);
            }
            string hidden = Hide + ColorTags.Replace(source, "");
            st.Hidden = hidden;
            if (!e.enableRichText) e.enableRichText = true;
            if (e.text != hidden) SetQuietly(e, hidden);
        }

        private static void OnTextChanged(ChangeEvent<string> evt)
        {
            if (!(evt.target is TextElement e) || evt.currentTarget != e) return;
            if (!states.TryGetValue(e, out var st) || st.Clip == null || evt.newValue == st.Hidden) return;
            Show(e, st, evt.newValue ?? "");
            Sync(e, st);
        }

        private static void OnMoved(GeometryChangedEvent evt)
        {
            if (evt.target is TextElement e && states.TryGetValue(e, out var st) && st.Clip != null) Place(e, st);
        }

        /// <summary>Lays the overlay exactly over the element's content box, mirroring its visibility and opacity.
        /// Absolute offsets start inside the parent's border; layout.x already includes it.</summary>
        private static void Place(TextElement e, State st)
        {
            var p = e.parent;
            if (p == null || st.Clip.parent != p) return;
            var r = e.resolvedStyle;
            var pr = p.resolvedStyle;
            var c = st.Clip.style;
            var lay = e.layout;
            c.left = lay.x - pr.borderLeftWidth + r.borderLeftWidth + r.paddingLeft;
            c.top = lay.y - pr.borderTopWidth + r.borderTopWidth + r.paddingTop;
            c.width = Mathf.Max(0f, e.contentRect.width);
            c.height = Mathf.Max(0f, e.contentRect.height);
            if (st.Clip.resolvedStyle.display != r.display) c.display = r.display;
            if (st.Clip.resolvedStyle.visibility != r.visibility) c.visibility = r.visibility;
            if (Mathf.Abs(st.Clip.resolvedStyle.opacity - r.opacity) > 0.01f) c.opacity = r.opacity;
        }

        /// <summary>Keeps the overlay on the element's content box and in its exact type (class/state changes included).</summary>
        private static void Sync(TextElement e, State st)
        {
            var r = e.resolvedStyle;
            var c = st.Clip.style;
            Place(e, st);
            var a = r.unityTextAlign;
            c.alignItems = a == TextAnchor.UpperLeft || a == TextAnchor.UpperCenter || a == TextAnchor.UpperRight ? Align.FlexStart
                : a == TextAnchor.LowerLeft || a == TextAnchor.LowerCenter || a == TextAnchor.LowerRight ? Align.FlexEnd : Align.Center;
            var s = st.Inner.style;
            if (st.Inner.resolvedStyle.color != r.color) s.color = r.color;
            if (Mathf.Abs(st.Inner.resolvedStyle.fontSize - r.fontSize) > 0.1f) s.fontSize = r.fontSize;
            if (!st.Inner.resolvedStyle.unityFontDefinition.Equals(r.unityFontDefinition)) s.unityFontDefinition = r.unityFontDefinition;
            if (st.Inner.resolvedStyle.unityFontStyleAndWeight != r.unityFontStyleAndWeight) s.unityFontStyleAndWeight = r.unityFontStyleAndWeight;
            if (Mathf.Abs(st.Inner.resolvedStyle.letterSpacing - r.letterSpacing) > 0.01f) s.letterSpacing = r.letterSpacing;
            // Its full one-line width, explicitly: a text node measured inside a narrower box is clamped to the box.
            float full = Mathf.Ceil(Measure(e, st.Source)) + 1f;
            if (Mathf.Abs(st.Inner.resolvedStyle.width - full) > 0.5f) s.width = full;
            // The element's own (invisible) glyphs must not cast a shadow; the overlay carries it instead. Text shadow
            // isn't in resolvedStyle: the one USS shadow (.shadow, theme-scoped) moves as a class, an inline one as a value.
            if (!st.ShadowMoved)
            {
                st.ShadowMoved = true;
                st.Shadow = e.style.textShadow.keyword == StyleKeyword.Undefined ? e.style.textShadow.value : default;
                if (e.ClassListContains("shadow")) st.Inner.AddToClassList("shadow");
                if (st.Shadow.color.a > 0f) s.textShadow = st.Shadow;
                e.style.textShadow = new TextShadow { color = Color.clear };
            }
        }

        private static void ToEllipsis(TextElement e, State st, string source)
        {
            if (st.Clip != null) RemoveOverlay(e, st, true);
            if (st.Ellipsis) return;
            st.Ellipsis = true;
            st.Source = source;
            e.style.overflow = Overflow.Hidden;
            e.style.textOverflow = TextOverflow.Ellipsis;
        }

        private static void UndoEllipsis(TextElement e)
        {
            e.style.overflow = StyleKeyword.Null;
            e.style.textOverflow = StyleKeyword.Null;
        }

        private static void RemoveOverlay(TextElement e, State st, bool restoreText)
        {
            e.UnregisterCallback<ChangeEvent<string>>(OnTextChanged);
            e.UnregisterCallback<GeometryChangedEvent>(OnMoved);
            st.Clip?.RemoveFromHierarchy();
            st.Clip = null;
            st.Inner = null;
            if (st.Source != null && (restoreText || (st.Hidden != null && e.text == st.Hidden))) SetQuietly(e, st.Source);
            st.Hidden = null;
            if (st.ShadowMoved) { e.style.textShadow = st.Shadow.color.a > 0f ? new StyleTextShadow(st.Shadow) : StyleKeyword.Null; st.ShadowMoved = false; }
        }

        private static void Drop(TextElement e, State st, bool restoreText)
        {
            RemoveOverlay(e, st, restoreText);
            if (st.Ellipsis) UndoEllipsis(e);
            st.Ellipsis = false;
            e.RemoveFromClassList(HostClass);
            states.Remove(e);
        }

        private static void SetQuietly(TextElement e, string text) => ((INotifyValueChanged<string>)e).SetValueWithoutNotify(text);

        private static bool Alive(TextElement e, State st, Label inner) =>
            e.panel != null && st.Clip != null && st.Inner == inner && !st.Ellipsis && st.Clip.parent == e.parent;

        private static IEnumerator Hold(TextElement e, State st, float seconds)
        {
            for (float t = 0f; t < seconds && st.Clip != null; t += Tween.Dt) { Place(e, st); yield return null; }
        }

        /// <summary>Wait at the left → scroll to the end → flash ×3 → snap back to the left → again.</summary>
        private static IEnumerator Loop(TextElement e, State st)
        {
            st.Running = true;
            var inner = st.Inner;
            while (Alive(e, st, inner))
            {
                inner.style.translate = new Translate(0, 0);
                inner.style.opacity = 1f;
                yield return Hold(e, st, HoldStart);
                if (!Alive(e, st, inner)) break;
                float over = inner.layout.width - st.Clip.layout.width;
                if (float.IsNaN(over) || over <= 1f) continue;
                float dur = Mathf.Max(Timing.Dramatic, over / ScrollSpeed);
                for (float t = 0f; t < dur && Alive(e, st, inner); t += Tween.Dt)
                {
                    inner.style.translate = new Translate(-over * Tween.EaseInOut(t / dur), 0);
                    Place(e, st);
                    yield return null;
                }
                if (!Alive(e, st, inner)) break;
                inner.style.translate = new Translate(-over, 0);
                yield return Hold(e, st, HoldEnd);
                for (int i = 0; i < Flashes && Alive(e, st, inner); i++)
                {
                    for (float t = 0f; t < FlashHalf; t += Tween.Dt) { inner.style.opacity = Mathf.Lerp(1f, FlashLow, t / FlashHalf); yield return null; }
                    for (float t = 0f; t < FlashHalf; t += Tween.Dt) { inner.style.opacity = Mathf.Lerp(FlashLow, 1f, t / FlashHalf); yield return null; }
                    inner.style.opacity = 1f;
                }
                yield return Hold(e, st, AfterFlash);
            }
            if (inner != null) { inner.style.translate = new Translate(0, 0); inner.style.opacity = 1f; }
            // The element left its parent (or the panel) without the overlay: take the overlay away too.
            if (st.Clip != null && st.Inner == inner && (e.panel == null || st.Clip.parent != e.parent)) RemoveOverlay(e, st, false);
            st.Running = false;
        }
    }
}
