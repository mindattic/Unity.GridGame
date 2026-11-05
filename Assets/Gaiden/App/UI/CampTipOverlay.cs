using System.Linq;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The first-use prompt for a Camp station (GG-T173): a see-through veil, a gold outline and a bouncing pointing
    /// hand on the station's key control, and a one-sentence card with "Got it". Shown once per station per save;
    /// lives in the screen's own root so it leaves with the screen.
    /// </summary>
    public static class CampTipOverlay
    {
        /// <summary>Bots and harness runs see no tips unless the run asks for them (`-campTips`).</summary>
        private static bool Suppressed =>
            DemoMode.Active || TestMode.Active || CommandLine.Has("-autobattle") ||
            (CommandLine.Has("-automation") && !CommandLine.Has("-campTips"));

        public static void ShowIfPending(GameScreen page, string station)
        {
            var save = GameApp.I.Save;
            var tip = CampTips.Resolve(GameApp.I.Content, save, CampTips.Pending(save, station));
            if (tip == null || Suppressed || page.Root == null) return;
            // Wait one layout pass so the target control has a size and position to point at.
            page.Root.schedule.Execute(() => Show(page.Root, tip)).ExecuteLater(60);
        }

        private static VisualElement FindTarget(VisualElement root, string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            bool prefix = target.EndsWith("-");
            return root.Query<VisualElement>().Where(e => e.name != null && (prefix ? e.name.StartsWith(target) : e.name == target)
                                                         && e.resolvedStyle.display != DisplayStyle.None && e.worldBound.height > 0).First();
        }

        private static void Show(VisualElement root, CampTip tip, bool scrolled = false)
        {
            if (root.Q("camp-tip") != null) return;
            // A target below the fold of a list is scrolled into view first, then pointed at once the list settles.
            var inList = FindTarget(root, tip.Target)?.GetFirstAncestorOfType<ScrollView>();
            if (inList != null && !scrolled)
            {
                inList.ScrollTo(FindTarget(root, tip.Target));
                root.schedule.Execute(() => Show(root, tip, true)).ExecuteLater(80);
                return;
            }
            var veil = Ui.El("fill", root);
            veil.name = "camp-tip";
            veil.style.backgroundColor = new Color(0f, 0f, 0.04f, 0.55f);
            veil.pickingMode = PickingMode.Position;

            var target = FindTarget(root, tip.Target);
            bool targetLow = false;
            if (target != null)
            {
                var r = target.worldBound;
                var origin = root.worldBound.position;
                var rect = new Rect(r.position - origin, r.size);
                targetLow = rect.center.y > root.worldBound.height * 0.55f;

                var ring = Ui.El("", veil);
                ring.name = "camp-tip-ring";
                ring.pickingMode = PickingMode.Ignore;
                ring.style.position = Position.Absolute;
                ring.style.left = rect.xMin - 8; ring.style.top = rect.yMin - 8;
                ring.style.width = rect.width + 16; ring.style.height = rect.height + 16;
                ring.style.borderTopWidth = ring.style.borderBottomWidth = ring.style.borderLeftWidth = ring.style.borderRightWidth = 6;
                ring.style.borderTopColor = ring.style.borderBottomColor = ring.style.borderLeftColor = ring.style.borderRightColor = Palette.Gold;
                ring.style.borderTopLeftRadius = ring.style.borderTopRightRadius = ring.style.borderBottomLeftRadius = ring.style.borderBottomRightRadius = 16;

                var hand = Ui.El("", veil);
                hand.name = "camp-tip-hand";
                hand.pickingMode = PickingMode.Ignore;
                hand.style.position = Position.Absolute;
                hand.style.width = hand.style.height = Size.Glyph;
                hand.style.backgroundImage = new StyleBackground(Art.Cursor);
                float hx = Mathf.Max(6f, rect.xMin - 100f), hy = rect.center.y - 44f;
                hand.style.left = hx; hand.style.top = hy;
                float t0 = Time.unscaledTime;
                hand.schedule.Execute(() => hand.style.translate = new Translate(Mathf.Sin((Time.unscaledTime - t0) * 7f) * 12f, 0)).Every(16);
            }

            var card = Ui.Window(null, veil);
            card.name = "camp-tip-card";
            card.style.position = Position.Absolute;
            card.style.left = Fit.Edge; card.style.right = Fit.Edge;
            if (targetLow) card.style.top = Fit.H(7.9f); else card.style.bottom = Space.S64;
            Ui.Text("Camp tip · " + tip.Title, "small gold", card);
            var text = Ui.Text(tip.Text, "", card);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.marginTop = Space.S8;
            var row = Ui.El("row", card);
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = Space.S16;
            var ok = Ui.Button("Got it", () => Dismiss(veil, tip), "btn--primary", row);
            ok.name = "camp-tip-ok";

            GameApp.I.Audio.Sfx("ui.select", 0.7f);
            veil.style.opacity = 0f;
            veil.schedule.Execute(() => veil.style.opacity = 1f).ExecuteLater(16);
        }

        private static void Dismiss(VisualElement veil, CampTip tip)
        {
            CampTips.MarkSeen(GameApp.I.Save, tip.Station);
            GameApp.I.Persist();
            veil.RemoveFromHierarchy();
        }
    }
}
