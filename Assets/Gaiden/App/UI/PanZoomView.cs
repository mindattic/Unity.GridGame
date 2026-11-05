using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Pan/zoom canvas (GG-T78, amends GG-T53's Talents node graph)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pan/zoom that outlives a screen rebuild: a page that re-runs BuildBody on Refresh (the Talents screen does on every
    /// tap) keeps one of these as a field and hands it to each new <see cref="PanZoomView"/>, so the hero's view of the
    /// tree doesn't snap back to the corner every time a node is selected or learned.
    /// </summary>
    public sealed class PanZoomState
    {
        public Vector2 Pan;
        public float Scale = 1f;
        /// <summary>False until the view has fitted the content once; a fresh state fits-to-width on first layout.</summary>
        public bool Initialized;
        public void Reset() { Pan = Vector2.zero; Scale = 1f; Initialized = false; }
    }

    /// <summary>
    /// The pure arithmetic behind <see cref="PanZoomView"/> — zoom about a fixed screen point, clamping, fling decay —
    /// kept free of VisualElement state so it reads (and could be tested) on its own. Coordinates: a content point c is
    /// drawn at viewport point <c>pan + c * scale</c>.
    /// </summary>
    public static class PanZoomMath
    {
        /// <summary>The pan that keeps the content point under <paramref name="screen"/> fixed while the scale changes
        /// from <paramref name="oldScale"/> to <paramref name="newScale"/> (pinch midpoint, wheel cursor).</summary>
        public static Vector2 ZoomAbout(Vector2 pan, float oldScale, float newScale, Vector2 screen)
        {
            if (oldScale <= 0f) return pan;
            var content = (screen - pan) / oldScale;
            return screen - content * newScale;
        }

        /// <summary>Keeps the content on screen: an axis that fits inside the viewport is centred on it, an axis that
        /// overflows may pan only until its edge meets the viewport's edge (plus <paramref name="slack"/> px of give).</summary>
        public static Vector2 Clamp(Vector2 pan, Vector2 contentSize, float scale, Vector2 viewport, float slack = 0f)
        {
            return new Vector2(Axis(pan.x, contentSize.x * scale, viewport.x, slack), Axis(pan.y, contentSize.y * scale, viewport.y, slack));

            static float Axis(float p, float content, float view, float give)
            {
                if (content <= view) return (view - content) / 2f;
                return Mathf.Clamp(p, view - content - give, give);
            }
        }

        /// <summary>The scale that fits <paramref name="contentWidth"/> into <paramref name="viewportWidth"/>, never
        /// zooming in past 1 and never out past <paramref name="minScale"/>.</summary>
        public static float FitWidth(float contentWidth, float viewportWidth, float minScale)
        {
            if (contentWidth <= 0f || viewportWidth <= 0f) return 1f;
            return Mathf.Clamp(viewportWidth / contentWidth, minScale, 1f);
        }

        /// <summary>Exponential fling decay: the velocity left after <paramref name="dt"/> seconds of friction.</summary>
        public static Vector2 Decay(Vector2 velocity, float dt, float friction = 5f) => velocity * Mathf.Exp(-friction * dt);
    }

    /// <summary>
    /// A clipped viewport over a free-floating <see cref="Content"/> layer the player moves with their fingers: one-finger
    /// (or mouse) drag pans, a quick release keeps it gliding (swipe/fling with friction), two fingers pinch-zoom about
    /// their midpoint (and pan together), the mouse wheel zooms about the cursor. A press that never travels past
    /// <see cref="DragThreshold"/> stays a tap, so Tappable children (talent nodes) still select through it; once it does
    /// travel the gesture is the canvas's, the child's pressed look is cleared and its click is swallowed.
    /// Pure UI Toolkit pointer events (each touch arrives with its own pointerId) — no Input System polling, no
    /// EventSystem — the same path BattleScreen's drag input already rides.
    /// </summary>
    public sealed class PanZoomView : VisualElement
    {
        public const float DragThreshold = 14f;

        public VisualElement Content { get; }
        public PanZoomState State { get; }
        public float MinScale = 0.35f;
        public float MaxScale = 2.5f;
        /// <summary>Called whenever pan or scale changes (zoom readouts, minimaps).</summary>
        public System.Action Changed;

        private readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        private Vector2 pressStart;
        private bool dragging;
        private bool swallowClick;
        private float pinchStartDistance;
        private float pinchStartScale;
        private Vector2 pinchAnchor; // content point under the pinch midpoint when the pinch began
        private Vector2 velocity;
        private float lastMoveTime;
        private IVisualElementScheduledItem fling;

        public PanZoomView(PanZoomState state = null)
        {
            State = state ?? new PanZoomState();
            name = "pan-zoom";
            style.overflow = Overflow.Hidden;
            style.flexGrow = 1;
            style.minHeight = 200;

            Content = new VisualElement { name = "pan-zoom-content", pickingMode = PickingMode.Position };
            Content.style.position = Position.Absolute;
            Content.style.left = Space.S0; Content.style.top = Space.S0;
            Content.style.minWidth = Length.Percent(100);
            Content.style.transformOrigin = new TransformOrigin(0, 0);
            hierarchy.Add(Content);

            // TrickleDown: the canvas sees every press before the node under the finger does, so it can decide tap vs pan.
            RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(e => End(e.pointerId, false), TrickleDown.TrickleDown);
            // Only the canvas's own capture loss ends a gesture — a node losing its capture to the canvas mid-drag mustn't.
            RegisterCallback<PointerCaptureOutEvent>(e => { if (e.target == this) End(e.pointerId, false); });
            RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            Content.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            RegisterCallback<DetachFromPanelEvent>(_ => StopFling());
        }

        public override VisualElement contentContainer => Content;

        public float Scale => State.Scale;
        public Vector2 Pan => State.Pan;

        /// <summary>Sets the zoom about the viewport centre (zoom buttons, double-tap), clamped to the range.</summary>
        public void ZoomTo(float scale) => ZoomAbout(scale, layout.size / 2f);

        /// <summary>Re-fits the content to the viewport width and scrolls it to the top.</summary>
        public void Fit()
        {
            State.Initialized = false;
            Layout();
        }

        /// <summary>Pans (without zooming) so <paramref name="e"/>, a descendant of <see cref="Content"/>, sits at the
        /// centre of the viewport — e.g. the selected or next-available talent after a rebuild.</summary>
        public void CenterOn(VisualElement e)
        {
            if (e == null || float.IsNaN(layout.width) || float.IsNaN(e.layout.width)) return;
            var c = Content.WorldToLocal(e.worldBound.center); // unscaled content coordinates
            Apply(layout.size / 2f - c * State.Scale);
        }

        private Vector2 ContentSize => new Vector2(
            float.IsNaN(Content.layout.width) ? 0f : Content.layout.width,
            float.IsNaN(Content.layout.height) ? 0f : Content.layout.height);

        private void Layout()
        {
            if (float.IsNaN(layout.width) || layout.width <= 0f || ContentSize.x <= 0f) return;
            if (!State.Initialized)
            {
                State.Initialized = true;
                State.Scale = PanZoomMath.FitWidth(ContentSize.x, layout.width, MinScale);
                State.Pan = new Vector2(0f, 0f);
            }
            Apply(State.Pan);
        }

        private void Apply(Vector2 pan)
        {
            State.Scale = Mathf.Clamp(State.Scale, MinScale, MaxScale);
            State.Pan = PanZoomMath.Clamp(pan, ContentSize, State.Scale, layout.size);
            Content.style.translate = new Translate(State.Pan.x, State.Pan.y);
            Content.style.scale = new Scale(new Vector3(State.Scale, State.Scale, 1f));
            Changed?.Invoke();
        }

        private void ZoomAbout(float scale, Vector2 screen)
        {
            float next = Mathf.Clamp(scale, MinScale, MaxScale);
            var pan = PanZoomMath.ZoomAbout(State.Pan, State.Scale, next, screen);
            State.Scale = next;
            Apply(pan);
        }

        // ── Gestures ────────────────────────────────────────────────────────────

        private Vector2 Local(Vector2 panelPosition) => this.WorldToLocal(panelPosition);

        private void OnDown(PointerDownEvent e)
        {
            var p = Local(e.position);
            pointers[e.pointerId] = p;
            StopFling();
            velocity = Vector2.zero;
            lastMoveTime = Time.realtimeSinceStartup;
            if (pointers.Count == 1)
            {
                pressStart = p;
                dragging = false;
                swallowClick = false;
            }
            else if (pointers.Count == 2)
            {
                // Second finger down: the gesture is a pinch now, never a tap, and both fingers belong to the canvas.
                BeginDrag();
                BeginPinch();
                foreach (var id in pointers.Keys) if (!this.HasPointerCapture(id)) this.CapturePointer(id);
            }
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!pointers.TryGetValue(e.pointerId, out var last)) return;
            var p = Local(e.position);
            pointers[e.pointerId] = p;

            if (pointers.Count >= 2)
            {
                Pinch();
                e.StopPropagation();
                return;
            }

            if (!dragging)
            {
                if ((p - pressStart).sqrMagnitude < DragThreshold * DragThreshold) return;
                BeginDrag();
                this.CapturePointer(e.pointerId);
                last = pressStart; // count the slop too, so the content doesn't lag the finger by the threshold
            }

            var delta = p - last;
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Max(now - lastMoveTime, 1f / 240f);
            lastMoveTime = now;
            velocity = Vector2.Lerp(velocity, delta / dt, 0.5f);
            Apply(State.Pan + delta);
            e.StopPropagation();
        }

        private void OnUp(PointerUpEvent e) => End(e.pointerId, true);

        private void End(int pointerId, bool released)
        {
            if (!pointers.Remove(pointerId)) return;
            if (this.HasPointerCapture(pointerId)) this.ReleasePointer(pointerId);

            if (pointers.Count == 1)
            {
                // Pinch → pan: re-baseline on the remaining finger so the content doesn't jump to it.
                foreach (var kv in pointers) pressStart = kv.Value;
                velocity = Vector2.zero;
                lastMoveTime = Time.realtimeSinceStartup;
            }
            else if (pointers.Count == 0)
            {
                bool wasDragging = dragging;
                dragging = false;
                // A swipe that was still moving when the finger lifted keeps gliding.
                if (released && wasDragging && Time.realtimeSinceStartup - lastMoveTime < 0.08f && velocity.sqrMagnitude > 200f * 200f)
                    StartFling();
            }
        }

        private void OnClick(ClickEvent e)
        {
            if (!swallowClick) return;
            swallowClick = false;
            e.StopImmediatePropagation();
        }

        private void OnWheel(WheelEvent e)
        {
            // One notch ≈ 10% (UI Toolkit reports wheel in lines; negative y is "away from you" → zoom in).
            ZoomAbout(State.Scale * Mathf.Pow(1.1f, -e.delta.y / 3f), Local(e.mousePosition));
            e.StopPropagation();
        }

        private void BeginDrag()
        {
            if (dragging) return;
            dragging = true;
            swallowClick = true;
            // The press started on a node: it sank (Ui.Pressable) — this is a pan, not a tap, so spring it back.
            Content.Query(className: "is-pressed").ForEach(x => x.RemoveFromClassList("is-pressed"));
        }

        private void BeginPinch()
        {
            var (a, b) = TwoPointers();
            pinchStartDistance = Mathf.Max(Vector2.Distance(a, b), 1f);
            pinchStartScale = State.Scale;
            pinchAnchor = ((a + b) / 2f - State.Pan) / State.Scale;
        }

        private void Pinch()
        {
            var (a, b) = TwoPointers();
            float next = Mathf.Clamp(pinchStartScale * Vector2.Distance(a, b) / pinchStartDistance, MinScale, MaxScale);
            var mid = (a + b) / 2f;
            State.Scale = next;
            // The content point that began under the fingers' midpoint stays under it: pinch zooms and two-finger drags.
            Apply(mid - pinchAnchor * next);
        }

        private (Vector2, Vector2) TwoPointers()
        {
            Vector2 a = default, b = default;
            int i = 0;
            foreach (var kv in pointers)
            {
                if (i == 0) a = kv.Value; else if (i == 1) b = kv.Value; else break;
                i++;
            }
            return (a, b);
        }

        private void StartFling()
        {
            StopFling();
            float last = Time.realtimeSinceStartup;
            fling = schedule.Execute(() =>
            {
                float now = Time.realtimeSinceStartup;
                float dt = Mathf.Min(now - last, 0.05f);
                last = now;
                var before = State.Pan;
                Apply(State.Pan + velocity * dt);
                velocity = PanZoomMath.Decay(velocity, dt);
                // Stop once it has slowed to a crawl or run into an edge.
                if (velocity.sqrMagnitude < 20f * 20f || (State.Pan - before).sqrMagnitude < 0.01f) StopFling();
            }).Every(16);
        }

        private void StopFling()
        {
            fling?.Pause();
            fling = null;
        }
    }
}
