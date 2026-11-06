using System.Collections.Generic;
using System.Text.RegularExpressions;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Text whose bracketed keywords are tap tooltips (GG-T300, owner: "anything in brackets is a tap tooltip"):
    /// "[Counterstrike]" renders as a highlighted, underlined "Counterstrike" (brackets hidden) and a tap on it opens a
    /// small definition card from glossary.json; holding the element around it still opens its InspectCard.
    /// UI Toolkit can't hit-test words inside one Label, so text with terms becomes a wrapping row of word labels in the
    /// same classes (same font, size and line height); a term's label is padded out to a finger-sized hit area and
    /// pulled back by the same negative margin, so the layout doesn't move. Text without terms stays one plain Label.
    /// </summary>
    public static class GlossaryText
    {
        public const string UssClass = "glossary-text";
        /// <summary>Hit-area bleed around a term (POL-C2): a ~40 px line becomes ~88 px tall, a word ~32 px wider.</summary>
        public const float HitPadV = 24f, HitPadH = 16f;

        /// <summary>Space advance as a fraction of font size, measured once from the real font (estimate until then).</summary>
        private static float spaceEm = 0.27f;
        private static bool spaceMeasured;

        public static string TermHex => ColorUtility.ToHtmlStringRGB(Palette.Term);

        /// <summary>
        /// Renders <paramref name="text"/> in <paramref name="classes"/>. <paramref name="tappable"/> false draws the terms
        /// highlighted but inert (inside a hold card, which closes on release anyway).
        /// </summary>
        public static VisualElement Create(string text, string classes, VisualElement parent = null, bool tappable = true, Color? color = null)
        {
            text ??= "";
            if (!Glossary.HasTerms(text))
            {
                var plain = Ui.Text(text, classes, parent);
                plain.style.whiteSpace = WhiteSpace.Normal;
                if (color.HasValue) plain.style.color = color.Value;
                return plain;
            }

            var db = GameApp.I?.Content;
            var box = new VisualElement { name = UssClass, pickingMode = PickingMode.Ignore };
            box.AddToClassList(UssClass);
            box.style.flexDirection = FlexDirection.Row;
            box.style.flexWrap = Wrap.Wrap;
            box.style.alignItems = Align.FlexStart;
            box.style.flexShrink = 1;
            box.userData = Regex.Replace(Glossary.Plain(text), "<[^>]*>", "").Length;

            float gap0 = EstimateSize(classes) * spaceEm;
            foreach (var word in Glossary.Words(text))
            {
                if (word.Count == 0)
                {
                    var br = new VisualElement { pickingMode = PickingMode.Ignore };
                    br.style.flexBasis = Length.Percent(100);
                    br.style.height = 0;
                    box.Add(br);
                    continue;
                }
                VisualElement host = box;
                if (word.Count > 1)
                {
                    host = new VisualElement { pickingMode = PickingMode.Ignore };
                    host.style.flexDirection = FlexDirection.Row;
                    host.style.flexShrink = 0;
                    box.Add(host);
                }
                VisualElement last = null;
                foreach (var run in word)
                {
                    if (run.IsTerm)
                    {
                        var entry = db?.TryTerm(run.Text);
                        last = Term(run.Text, entry, classes, host, tappable && entry != null);
                    }
                    else
                    {
                        var l = Word(run.Text, classes, host);
                        if (color.HasValue) l.style.color = color.Value;
                        last = l;
                    }
                }
                var tail = word.Count > 1 ? host : last;
                tail?.AddToClassList("glossary-word");
                // First-frame gap from the class's type size; ApplyGaps corrects it to the real font once laid out.
                if (last != null) last.style.marginRight = gap0 - (last.pickingMode == PickingMode.Position ? HitPadH : 0f);
            }
            box.RegisterCallback<GeometryChangedEvent>(_ => ApplyGaps(box));
            parent?.Add(box);
            return box;
        }

        private static Label Word(string text, string classes, VisualElement parent)
        {
            var l = Ui.Text(text, classes, parent);
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.flexShrink = 0;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            return l;
        }

        /// <summary>A term: a host (named <c>term-&lt;id&gt;</c>, the hit area) holding the word's label and an underline
        /// overlay. The label stays a leaf so it measures its own text; the overlay never changes the line height.</summary>
        private static VisualElement Term(string shown, GlossaryEntry entry, string classes, VisualElement parent, bool tappable)
        {
            var t = new VisualElement { name = "term-" + (entry?.Id ?? shown), pickingMode = PickingMode.Ignore };
            t.AddToClassList("glossary-term");
            t.style.flexShrink = 0;
            parent.Add(t);
            var l = Word(shown, classes, t);
            l.style.color = Palette.Term;
            var u = new VisualElement { pickingMode = PickingMode.Ignore };
            u.style.position = Position.Absolute;
            u.style.height = 2;
            u.style.backgroundColor = Palette.Term.WithA(0.45f);
            t.Add(u);
            float padV = tappable ? HitPadV : 0f, padH = tappable ? HitPadH : 0f;
            // Finger-sized hit area: padding out, the same negative margin back, so the layout doesn't move (POL-C2).
            t.style.paddingTop = t.style.paddingBottom = padV;
            t.style.marginTop = t.style.marginBottom = -padV;
            t.style.paddingLeft = t.style.paddingRight = padH;
            t.style.marginLeft = t.style.marginRight = -padH;
            u.style.left = u.style.right = padH;
            u.style.bottom = padV + 1;
            if (!tappable) return t;
            t.pickingMode = PickingMode.Position;
            t.RegisterCallback<ClickEvent>(ev =>
            {
                // The tap reads the word; it never also acts on the row/button around it.
                ev.StopPropagation();
                InspectCard.ShowTerm(t, entry);
            });
            return t;
        }

        /// <summary>Word gaps from the font's real space advance; re-run whenever the row lays out (font size may change).</summary>
        private static void ApplyGaps(VisualElement box)
        {
            foreach (var child in box.Children())
            {
                if (!child.ClassListContains("glossary-word")) continue;
                var label = child as Label ?? child.Q<Label>();
                if (label == null) continue;
                float size = label.resolvedStyle.fontSize;
                if (float.IsNaN(size) || size <= 0) continue;
                if (!spaceMeasured && label.panel != null)
                {
                    float two = label.MeasureTextSize("a a", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    float one = label.MeasureTextSize("aa", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    if (two > one) { spaceEm = (two - one) / size; spaceMeasured = true; }
                }
                float gap = size * spaceEm;
                // The word's last piece carries the gap; a tappable term's own margin already pulls back its hit padding.
                bool multi = !(child is Label) && !child.ClassListContains("glossary-term");
                var tail = multi ? (child.childCount > 0 ? child[child.childCount - 1] : null) : child;
                if (tail == null) continue;
                float want = tail.pickingMode == PickingMode.Position ? gap - HitPadH : gap;
                if (Mathf.Abs(tail.resolvedStyle.marginRight - want) > 0.25f) tail.style.marginRight = want;
            }
        }

        private static float EstimateSize(string classes)
        {
            string c = " " + (classes ?? "") + " ";
            if (c.Contains(" tiny ")) return TypeScale.Badge;
            if (c.Contains(" small ") || c.Contains(" t-caption ")) return TypeScale.Caption;
            if (c.Contains(" t-h3 ")) return TypeScale.H3;
            if (c.Contains(" h2 ") || c.Contains(" t-h2 ")) return TypeScale.H2;
            return TypeScale.Body;
        }

        /// <summary>For text that stays a plain Label: terms tinted (brackets hidden) but not tappable.</summary>
        public static string Highlight(string text)
        {
            if (!Glossary.HasTerms(text)) return text;
            var sb = new System.Text.StringBuilder();
            string hex = TermHex;
            foreach (var r in Glossary.Parse(text))
                sb.Append(r.IsTerm ? $"<color=#{hex}>{r.Text}</color>" : r.Text);
            return sb.ToString();
        }
    }
}
