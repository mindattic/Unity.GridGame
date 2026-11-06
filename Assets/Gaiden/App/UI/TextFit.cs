using System.Runtime.CompilerServices;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// GG-T305 (owner: "perfect text wrapping on all things (hyphenated or word wrap as an option), and one-line things
    /// like buttons have a restarting marquee"). One pass over every TextElement on the panel, a few times a second:
    /// <list type="bullet">
    /// <item>Wrapped text (white-space: normal) is laid out by <see cref="LineBreaker"/> against its real width and font:
    /// no lone word on the last line, no single letter ending a line, an overlong word hyphenated instead of cut
    /// mid-glyph, and with Settings → Text wrapping → Hyphenate a word that would leave a ragged line breaks at a
    /// <see cref="Hyphenation"/> point. When the engine's own greedy wrap is already clean the text is left untouched;
    /// otherwise the label gets explicit line breaks, re-laid when its width, font size, the setting or its text changes
    /// (a text must hold still for one pass first, so a typewriter is never re-flowed mid-line).</item>
    /// <item>Single-line text (white-space: nowrap) that is wider than its box becomes a <see cref="Marquee"/>.</item>
    /// <item>A word in a GlossaryText row (GG-T300) wider than the row becomes a wrapped label, so it hyphenates too.</item>
    /// </list>
    /// </summary>
    public static class TextFit
    {
        /// <summary>Pass interval, ms.</summary>
        public const long Interval = 120;
        /// <summary>Lines are fitted this much narrower than the box, so the engine never re-wraps one of ours by a hair.</summary>
        private const float Slack = 2f;

        public static bool Hyphenate { get; private set; }
        private static int generation;

        private sealed class WrapState
        {
            public string Source, Output, Seen;
            public float Width, MaxLine, FontSize, ParentWidth;
            public int Generation = -1;
            public bool Pinned, ParentKnown;
            public StyleLength PrevMinWidth;
        }

        /// <summary>
        /// Our lines are measured to fit; but a label sized to its own text would then be exactly as wide as its longest
        /// line, and the engine re-wraps a line at exactly its own width by a rounding hair. So a re-laid label keeps a
        /// minimum width of its longest line plus <see cref="Slack"/> (never more than the width it was laid out in).
        /// </summary>
        private static void Unpin(TextElement t, WrapState st)
        {
            if (!st.Pinned) return;
            t.style.minWidth = st.PrevMinWidth;
            st.Pinned = false;
        }

        private static readonly ConditionalWeakTable<TextElement, WrapState> wraps = new ConditionalWeakTable<TextElement, WrapState>();

        public static void Install(VisualElement root)
        {
            if (root == null) return;
            root.schedule.Execute(() => Pass(root)).Every(Interval);
        }

        /// <summary>Applies the Text wrapping setting live (and a font change): every wrapped label is re-laid on the next pass.</summary>
        public static void Configure(bool hyphenate)
        {
            Hyphenate = hyphenate;
            generation++;
        }

        /// <summary>The words an element was given (before line breaks or a marquee's hiding tag), for automation and audits.</summary>
        public static string SourceOf(TextElement e)
        {
            if (e == null) return null;
            if (wraps.TryGetValue(e, out var st) && st.Output != null && e.text == st.Output) return st.Source;
            return Marquee.SourceOf(e);
        }

        public static void Pass(VisualElement root)
        {
            root.Query<TextElement>().ForEach(Fit);
        }

        private static void Fit(TextElement t)
        {
            if (t.panel == null) return;
            var p = t.parent;
            if (p == null || p.ClassListContains(Marquee.BoxClass) || t.ClassListContains("fit-off") || p is TextField || p.parent is TextField) return;
            var rs = t.resolvedStyle;
            if (rs.display == DisplayStyle.None) return;
            float w = t.contentRect.width;
            if (float.IsNaN(w) || w <= 1f) return;
            if (rs.whiteSpace == WhiteSpace.Normal) Wrap(t, w, rs.fontSize);
            else if (!GlossaryWord(t)) Marquee.Check(t, w);
        }

        /// <summary>A GlossaryText word wider than its whole row: let it wrap (and hyphenate) like any paragraph.</summary>
        private static bool GlossaryWord(TextElement t)
        {
            var box = t.parent;
            if (box != null && !box.ClassListContains(GlossaryText.UssClass)) box = box.parent;
            if (box == null || !box.ClassListContains(GlossaryText.UssClass)) return false;
            float row = box.contentRect.width;
            if (!float.IsNaN(row) && row > 1f && t.layout.width > row + 1f)
            {
                t.style.whiteSpace = WhiteSpace.Normal;
                t.style.flexShrink = 1f;
                t.style.maxWidth = Length.Percent(100);
            }
            return true;
        }

        private static float Measure(TextElement t, string s) =>
            t.MeasureTextSize(s ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

        private static void Wrap(TextElement t, float w, float fontSize)
        {
            var st = wraps.GetValue(t, _ => new WrapState());
            string text = t.text ?? "";
            if (st.Output == null || text != st.Output)
            {
                // New text (first sight, or set from code): wait until it holds still for one pass.
                if (text != st.Seen) { st.Seen = text; return; }
                st.Source = text;
                st.Output = null;
            }
            float pw = t.parent.contentRect.width;
            // The parent's width is read one pass after a re-lay, so a parent sized by this label (which our pin just
            // narrowed) is the baseline rather than a change that would re-lay it again.
            if (st.Output != null && !st.ParentKnown) { st.ParentWidth = pw; st.ParentKnown = true; }
            bool stale = st.Output == null || st.Generation != generation || Mathf.Abs(st.FontSize - fontSize) > 0.1f
                         || w < st.MaxLine - 0.5f || w > st.Width + 1f || Mathf.Abs(pw - st.ParentWidth) > 1f;
            if (!stale) return;
            if (st.Pinned)
            {
                // Let it find its natural width again first; re-lay on the next pass.
                Unpin(t, st);
                st.Generation = -1;
                st.ParentKnown = true;
                st.ParentWidth = pw;
                return;
            }

            string output = st.Source;
            float maxLine = w; // untouched text: any narrowing re-checks it
            if (st.Source.Length > 0 && (st.Source.IndexOf(' ') >= 0 || Measure(t, st.Source) > w))
            {
                var r = LineBreaker.Break(st.Source, w - Slack, s => Measure(t, s), Hyphenate);
                if (r.Changed)
                {
                    output = string.Join("\n", r.Lines);
                    maxLine = 0f;
                    foreach (var line in r.Lines) maxLine = Mathf.Max(maxLine, Measure(t, line));
                }
            }
            st.Width = w;
            st.FontSize = fontSize;
            st.Generation = generation;
            st.MaxLine = maxLine;
            st.Output = output;
            st.Seen = output;
            st.ParentKnown = false;
            if (!ReferenceEquals(output, st.Source))
            {
                st.PrevMinWidth = t.style.minWidth;
                st.Pinned = true;
                t.style.minWidth = Mathf.Min(w, Mathf.Ceil(maxLine) + Slack);
            }
            if (t.text != output) ((INotifyValueChanged<string>)t).SetValueWithoutNotify(output);
        }
    }
}
