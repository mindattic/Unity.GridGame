using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// GG-T271: a single-line label that can't wrap or shrink (a name, a plate, a list-row title) never overflows. It sits
    /// in a clipping box; when the text is wider than the box it holds, scrolls left at a readable speed until the end is
    /// in view, holds, fades, snaps back to the start and repeats. Pause-aware (Tween time). Reduce Motion: an ellipsis.
    /// </summary>
    public static class Marquee
    {
        public const string BoxClass = "marquee";
        /// <summary>Design px per second: about the story crawl's reading speed.</summary>
        private const float ScrollSpeed = 90f;

        private static readonly System.Collections.Generic.HashSet<Label> running = new System.Collections.Generic.HashSet<Label>();

        /// <summary>Wraps <paramref name="label"/> in a clipping marquee box that takes its place in the layout. The box
        /// grows into the row's free space (<paramref name="grow"/>) and may shrink below the text's width.</summary>
        public static Label Apply(Label label, bool grow = true)
        {
            var parent = label?.parent;
            if (parent == null || parent.ClassListContains(BoxClass)) return label;
            var box = new VisualElement { name = "marquee", pickingMode = PickingMode.Ignore };
            box.AddToClassList(BoxClass);
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.overflow = Overflow.Hidden;
            box.style.flexGrow = grow ? 1f : 0f;
            box.style.flexShrink = 1f;
            box.style.minWidth = Space.S0;
            bool still = GameApp.I?.Settings?.ReduceMotion == true;
            // Registered before the move: re-parenting an attached label fires its attach event right away.
            if (!still) label.RegisterCallback<AttachToPanelEvent>(_ => Start(box, label));
            parent.Insert(parent.IndexOf(label), box);
            box.Add(label);
            if (label.style.width.keyword == StyleKeyword.Undefined) { box.style.width = label.style.width; label.style.width = StyleKeyword.Auto; }
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.flexShrink = 0;

            if (still)
            {
                label.style.flexShrink = 1;
                label.style.minWidth = Space.S0;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                return label;
            }
            // Text that fits fills the box, so the label's own alignment (left, centered) still applies.
            box.RegisterCallback<GeometryChangedEvent>(_ => label.style.flexGrow = Excess(box, label) > 1f ? 0f : 1f);
            if (label.panel != null) Start(box, label);
            return label;
        }

        private static void Start(VisualElement box, Label label)
        {
            if (GameApp.I == null || !running.Add(label)) return;
            GameApp.I.StartCoroutine(Loop(box, label));
        }

        private static float Excess(VisualElement box, Label label)
        {
            float need = label.MeasureTextSize(label.text ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
            return need - box.contentRect.width;
        }

        private static IEnumerator Loop(VisualElement box, Label label)
        {
            while (label.panel != null)
            {
                float over = Excess(box, label);
                if (over <= 1f || float.IsNaN(over))
                {
                    label.style.translate = new Translate(0, 0);
                    yield return Tween.Wait(Timing.Cinematic);
                    continue;
                }
                yield return Tween.Wait(Timing.Cinematic);
                if (label.panel == null) yield break;
                yield return Tween.Run(Mathf.Max(Timing.Dramatic, over / ScrollSpeed), k => label.style.translate = new Translate(-over * k, 0), Ease.InOut);
                yield return Tween.Wait(Timing.Cinematic);
                yield return Tween.Fade(label, 1f, 0f, Timing.Quick);
                label.style.translate = new Translate(0, 0);
                yield return Tween.Fade(label, 0f, 1f, Timing.Quick);
            }
            running.Remove(label);
        }
    }
}
