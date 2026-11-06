#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App.Automation
{
    /// <summary>
    /// The Polish Standard's in-engine checks (GG-T251, docs/POLISH.md): per-rule tallies (subjects checked/failed)
    /// for POL-A2/A3/A5/C2/C3/C5, findings that cite their rule, plus each visible text element's design rect,
    /// color and size so tools/polish.py can measure contrast (POL-B2) against the screenshot behind it.
    /// </summary>
    public sealed partial class AutomationServer
    {
        private const float MinTarget = 132f, MinTargetGap = 24f, CaptionFloor = 30f, MaxMeasure = 60f;

        private object PolishAudit()
        {
            var app = GameApp.I;
            var stage = app.Stage;
            var sb = stage.worldBound;
            if (sb.width < 1) return null;
            float k = GameApp.StageWidth / sb.width;
            var safe = stage.Q("screens")?.worldBound ?? sb;
            var findings = new List<object>();
            var counts = new Dictionary<string, int[]>();

            void Tally(string rule, bool ok, string subject, string detail)
            {
                if (!counts.TryGetValue(rule, out var c)) counts[rule] = c = new int[2];
                c[0]++;
                if (ok) return;
                c[1]++;
                if (findings.Count < 400) findings.Add(new { rule, subject, detail });
            }

            Rect D(Rect wb) => new Rect((wb.x - sb.x) * k, (wb.y - sb.y) * k, wb.width * k, wb.height * k);

            var heavy = Fonts.Heavy;
            var texts = new List<object>();
            foreach (var t in stage.Query<TextElement>().ToList())
            {
                if (string.IsNullOrWhiteSpace(t.text) || !Shown(t)) continue;
                var wb = t.worldBound;
                if (wb.width < 1 || wb.height < 1) continue;
                var r = D(wb);
                string subj = Trim(t.text.Replace('\n', ' '));
                float size = t.resolvedStyle.fontSize;
                var fs = t.resolvedStyle.unityFontStyleAndWeight;
                bool isHeavy = heavy != null && t.resolvedStyle.unityFontDefinition.fontAsset == heavy;
                bool wrap = t.resolvedStyle.whiteSpace == WhiteSpace.Normal;

                Tally("POL-A2", size >= CaptionFloor - 0.5f, subj, $"{size:0}px < {CaptionFloor:0}px caption floor");
                Tally("POL-A3", fs != FontStyle.Bold && fs != FontStyle.BoldAndItalic, subj, "faux bold");
                if (wrap && t.text.Length > MaxMeasure && size > 0)
                {
                    float cpl = t.contentRect.width / (0.5f * size); // ~0.5 em average glyph advance
                    Tally("POL-A5", cpl <= MaxMeasure, subj, $"~{cpl:0} characters per line");
                }

                var why = new List<string>();
                // GG-T271: a marquee label is meant to be wider than its clipping box; the box scrolls it into view.
                // GG-T305: its host draws the same line invisibly (and with Reduce Motion ellipsizes it) — handled too.
                bool marquee = (t.parent != null && t.parent.ClassListContains(Marquee.BoxClass)) || t.ClassListContains(Marquee.HostClass);
                if (t.ClassListContains(Marquee.HostClass) && t.Q(className: Marquee.BoxClass) != null) continue; // its overlay is the visible copy
                bool scrolled = InScroll(t); // scroll content legitimately runs past the stage
                if (!scrolled && !marquee && (wb.xMin < sb.xMin - 2 || wb.xMax > sb.xMax + 2 || wb.yMin < sb.yMin - 2 || wb.yMax > sb.yMax + 2))
                    why.Add($"off-stage {r.xMin:0},{r.yMin:0}..{r.xMax:0},{r.yMax:0}");
                else if (!scrolled && (wb.yMin < safe.yMin - 2 || wb.yMax > safe.yMax + 2))
                    why.Add("inside the unsafe area");
                if (t.contentRect.width > 0 && !marquee)
                {
                    if (wrap)
                    {
                        // +1 px: a label sized to its own text re-measured at exactly that width wraps by a rounding hair (a false "cut off").
                        var need = t.MeasureTextSize(t.text, t.contentRect.width + 1f, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                        if (need.y > t.contentRect.height + 4) why.Add($"wrapped text cut off ({need.y:0}>{t.contentRect.height:0})");
                    }
                    else
                    {
                        var need = t.MeasureTextSize(t.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                        if (need.x > t.contentRect.width + 4) why.Add($"text overflows ({need.x:0}>{t.contentRect.width:0})");
                    }
                }
                if (!marquee && ClippedByAncestor(t, wb)) why.Add("clipped by a parent");
                Tally("POL-C5", why.Count == 0, subj, string.Join("; ", why));

                var c = t.resolvedStyle.color;
                texts.Add(new
                {
                    t = subj,
                    x = Mathf.Round(r.x), y = Mathf.Round(r.y), w = Mathf.Round(r.width), h = Mathf.Round(r.height),
                    size = Mathf.Round(size),
                    color = new[] { Round3(c.r), Round3(c.g), Round3(c.b), Round3(c.a * Opacity(t)) },
                    large = size >= 54f || (isHeavy && size >= 42f),
                });
            }

            var targets = stage.Query<VisualElement>().Where(e => (e is Button || e.ClassListContains("pressable")) && Shown(e)).ToList()
                .Select(e => (e, r: D(e.worldBound))).Where(p => p.r.width >= 1 && p.r.height >= 1).ToList();
            foreach (var (e, r) in targets)
            {
                string subj = TargetName(e);
                Tally("POL-C2", r.width >= MinTarget - 0.5f && r.height >= MinTarget - 0.5f, subj, $"{r.width:0}x{r.height:0} < {MinTarget:0}x{MinTarget:0}");
                float gap = float.MaxValue;
                foreach (var (o, q) in targets)
                {
                    if (o == e || e.Contains(o) || o.Contains(e) || r.Overlaps(q)) continue;
                    float dx = Mathf.Max(0, Mathf.Max(r.xMin, q.xMin) - Mathf.Min(r.xMax, q.xMax));
                    float dy = Mathf.Max(0, Mathf.Max(r.yMin, q.yMin) - Mathf.Min(r.yMax, q.yMax));
                    gap = Mathf.Min(gap, dx > 0 && dy > 0 ? Mathf.Sqrt(dx * dx + dy * dy) : Mathf.Max(dx, dy));
                }
                if (gap < float.MaxValue) Tally("POL-C3", gap >= MinTargetGap - 0.5f, subj, $"{gap:0}px from the next target");
            }

            // Sibling overlap among in-flow children: flex layout never overlaps them unless something is wrong.
            foreach (var parent in stage.Query<VisualElement>().Where(Shown).ToList())
            {
                var kids = parent.Children().Where(c => c.resolvedStyle.position == Position.Relative && Shown(c) && c.worldBound.width >= 1 && c.worldBound.height >= 1).ToList();
                if (kids.Count < 2) continue;
                string hit = null;
                for (int i = 0; i < kids.Count && hit == null; i++)
                    for (int j = i + 1; j < kids.Count && hit == null; j++)
                    {
                        var a = kids[i].worldBound; var b = kids[j].worldBound;
                        float ox = (Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) * k;
                        float oy = (Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin)) * k;
                        if (ox > 8 && oy > 8) hit = $"'{TargetName(kids[i])}' overlaps '{TargetName(kids[j])}' by {ox:0}x{oy:0}";
                    }
                Tally("POL-C5", hit == null, TargetName(parent), hit);
            }

            // POL-C8 gutters: a row or wrapping grid of like-sized items has equal left, right and between gaps, on the
            // spacing scale (owner 2026-11-05: "gutters and spacing are important").
            foreach (var parent in stage.Query<VisualElement>().Where(Shown).ToList())
            {
                var dir = parent.resolvedStyle.flexDirection;
                if (dir != FlexDirection.Row && dir != FlexDirection.RowReverse) continue;
                var kids = parent.Children().Where(c => c.resolvedStyle.position == Position.Relative && Shown(c) && c.worldBound.width >= 1 && c.worldBound.height >= 1).ToList();
                if (kids.Count < 3) continue;
                float mw = Median(kids.Select(c => c.worldBound.width)), mh = Median(kids.Select(c => c.worldBound.height));
                if (kids.Any(c => Mathf.Abs(c.worldBound.width - mw) > mw * 0.1f || Mathf.Abs(c.worldBound.height - mh) > mh * 0.1f)) continue;
                var pb = parent.worldBound;
                var ps = parent.resolvedStyle;
                float innerL = pb.xMin + ps.borderLeftWidth, innerR = pb.xMax - ps.borderRightWidth;
                var lines = kids.GroupBy(c => Mathf.RoundToInt(c.worldBound.yMin / 4f)).Select(g => g.OrderBy(c => c.worldBound.xMin).ToList()).ToList();
                int full = lines.Max(l => l.Count);
                var between = new List<float>(); var lefts = new List<float>(); var rights = new List<float>();
                foreach (var line in lines)
                {
                    lefts.Add((line[0].worldBound.xMin - innerL) * k);
                    for (int i = 1; i < line.Count; i++) between.Add((line[i].worldBound.xMin - line[i - 1].worldBound.xMax) * k);
                    if (line.Count == full) rights.Add((innerR - line[line.Count - 1].worldBound.xMax) * k);
                }
                var issues = new List<string>();
                float gap = between.Count > 0 ? Median(between) : -1;
                if (between.Count > 0 && between.Max() - between.Min() > 2) issues.Add($"uneven gaps {between.Min():0}..{between.Max():0}");
                float left = Median(lefts), right = rights.Count > 0 ? Median(rights) : left;
                if (Mathf.Abs(left - right) > 2) issues.Add($"ragged edges: left {left:0} vs right {right:0}");
                if (gap >= 0 && Mathf.Abs(left - gap) > 2 && Mathf.Abs(right - gap) > 2) issues.Add($"edge {left:0}/{right:0} vs gap {gap:0}");
                if (gap >= 0 && !OnSpaceScale(gap)) issues.Add($"gap {gap:0}px is off the space scale");
                Tally("POL-C8", issues.Count == 0, TargetName(parent), string.Join("; ", issues));
            }

            // POL-C9 fill: no dead bands. Content = visible text, targets and pictures smaller than the backdrop.
            var content = stage.Query<VisualElement>().ToList().Where(e => (e is TextElement te && !string.IsNullOrWhiteSpace(te.text)) || e is Button
                    || e.ClassListContains("pressable") || e.resolvedStyle.backgroundImage.texture != null)
                .Where(Shown).Select(e => D(e.worldBound))
                .Where(r => r.width >= 1 && r.height >= 1 && r.width * r.height < 0.85f * GameApp.StageWidth * GameApp.StageHeight)
                .ToList();
            object fill = null;
            if (content.Count > 0)
            {
                float W = GameApp.StageWidth, H = GameApp.StageHeight;
                float minX = Mathf.Max(0, content.Min(r => r.xMin)), maxX = Mathf.Min(W, content.Max(r => r.xMax));
                float leftM = minX, rightM = W - maxX;
                float band = 0, cursor = 0;
                foreach (var r in content.OrderBy(r => r.yMin))
                {
                    if (r.yMin > cursor) band = Mathf.Max(band, r.yMin - cursor);
                    cursor = Mathf.Max(cursor, r.yMax);
                }
                band = Mathf.Max(band, H - Mathf.Min(H, cursor));
                var issues = new List<string>();
                if (leftM > W * 0.10f || rightM > W * 0.10f) issues.Add($"side margins {leftM:0}/{rightM:0}px > 10% of {W:0}");
                if (band > H * 0.15f && !ArtLed.Contains(app.Router.Current?.Id ?? "")) issues.Add($"empty band {band:0}px > 15% of {H:0}");
                Tally("POL-C9", issues.Count == 0, app.Router.Current?.Id ?? "screen", string.Join("; ", issues));
                // POL-C6 reads the main panel's width (widest window or scroll view): edge-anchored badges would make
                // the content span grow even when the panel itself is a fixed width.
                float main = stage.Query<VisualElement>().ToList().Where(e => (e.ClassListContains("window") || e is ScrollView) && Shown(e))
                    .Select(e => e.worldBound.width * k).DefaultIfEmpty(0f).Max();
                fill = new { minX = Mathf.Round(minX), maxX = Mathf.Round(maxX), band = Mathf.Round(band), main = Mathf.Round(main) };
            }

            return new
            {
                fill,
                screen = app.Router.Current?.Id,
                design = new[] { GameApp.StageWidth, GameApp.StageHeight },
                counts = counts.ToDictionary(kv => kv.Key, kv => new { checkedCount = kv.Value[0], failed = kv.Value[1] }),
                findings,
                texts,
                targets = targets.Count,
            };
        }

        private static float Round3(float v) => Mathf.Round(v * 1000f) / 1000f;

        /// <summary>Screens whose backdrop art is the content: an empty band there is the picture, not dead space.</summary>
        private static readonly HashSet<string> ArtLed = new HashSet<string> { "title", "credits", "storycrawl", "crawl", "dialogue" };

        private static readonly float[] SpaceScale = { 0, 8, 16, 24, 32, 48, 64, 96 };
        private static bool OnSpaceScale(float v) => SpaceScale.Any(s => Mathf.Abs(v - s) <= 1.5f);

        private static float Median(IEnumerable<float> xs)
        {
            var l = xs.OrderBy(x => x).ToList();
            return l.Count == 0 ? 0 : l[l.Count / 2];
        }

        private static bool Shown(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
            {
                var s = p.resolvedStyle;
                if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden || s.opacity <= 0.01f) return false;
            }
            return true;
        }

        private static float Opacity(VisualElement e)
        {
            float o = 1f;
            for (var p = e; p != null; p = p.parent) o *= p.resolvedStyle.opacity;
            return o;
        }

        private static bool InScroll(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
                if (p is ScrollView) return true;
            return false;
        }

        private static bool ClippedByAncestor(VisualElement e, Rect wb)
        {
            if (InScroll(e)) return false; // scrolled out of view is not a defect
            for (var p = e.parent; p != null; p = p.parent)
            {
                // IResolvedStyle has no overflow: inline style, plus the Theme.uss classes that clip (.stage is checked as off-stage).
                bool clips = (p.style.overflow.keyword == StyleKeyword.Undefined && p.style.overflow.value == Overflow.Hidden)
                             || p.ClassListContains("gauge") || p.ClassListContains("unit-frame");
                if (!clips) continue;
                var pb = p.worldBound;
                if (pb.width < 1) continue;
                return wb.xMin < pb.xMin - 2 || wb.xMax > pb.xMax + 2 || wb.yMin < pb.yMin - 2 || wb.yMax > pb.yMax + 2;
            }
            return false;
        }

        private static string TargetName(VisualElement e)
        {
            if (e is TextElement te && !string.IsNullOrWhiteSpace(te.text)) return Trim(te.text.Replace('\n', ' '));
            var label = e.Query<TextElement>().ToList().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.text));
            if (label != null) return Trim(label.text.Replace('\n', ' '));
            return !string.IsNullOrEmpty(e.name) ? "#" + e.name : e.GetType().Name + (e.GetClasses().FirstOrDefault() is string c ? "." + c : "");
        }
    }
}
#endif
