using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The timeline (GG-T287, Resonance-style, BIBLE §2): one track with a small diamond every second, icons flowing
    /// LEFT at <see cref="BattleEngine.BarU"/> (0 = acting now), and a large prominence diamond at the left end showing
    /// whoever acts next (a unit's portrait for a turn, the ability icon for a cast). Every icon is a diamond: a 45°
    /// bordered square that clips (overflow hidden — UI Toolkit stencil-masks a rotated clip) a counter-rotated portrait,
    /// so no art shows outside the diamond. Enemy turns ring red, bosses purple, hero casts teal, enemy charges amber;
    /// the next actor rings gold on the bar and on the board. Inside <see cref="ApproachSeconds"/> an icon's glow and its
    /// owner's tile glow ramp up together in the icon's colour, so icon and tile read as one.
    /// </summary>
    public sealed partial class BattleScreen
    {
        // ── Geometry (design px). The frame spans the stage; the slot sits on the left gutter, GUARD on the right. ──
        private const float TimelineY = 250f;
        private const float TimelineFrameHeight = 150f;
        /// <summary>The track line's y inside the timeline frame; every icon centres on it.</summary>
        private const float TrackY = TimelineFrameHeight / 2f;
        private const float Sqrt2 = 1.41421356f;
        private const float IconSide = 60f, IconSpan = IconSide * Sqrt2, IconBorder = 4f;
        private const float SlotSide = 96f, SlotSpan = SlotSide * Sqrt2, SlotBorder = 6f;
        private const float NextScale = 1.15f;
        /// <summary>Seconds before acting when an icon (and its owner's tile) starts to glow; the glow ramps to full at 0.</summary>
        public const float ApproachSeconds = 3f;

        /// <summary>x of the "acts now" end of the track (u = 0): an icon there touches the prominence slot.</summary>
        private static float TrackX0 => Fit.Edge + SlotSpan + Space.S16 + IconSpan / 2f;
        /// <summary>x of u = 1 (BarSeconds away), clear of the GUARD button.</summary>
        private static float TrackX1 => GameApp.StageWidth - Fit.Edge - Size.Target - Space.S16 - IconSpan / 2f;
        private static float XOfU(float u) => TrackX0 + Mathf.Clamp01(u) * (TrackX1 - TrackX0);

        private VisualElement timelineFrame, timelineBar, timelineIcons, timelineSlot, slotFrame, slotHalo;
        private Label slotTime;
        private readonly List<(VisualElement d, float side, float seconds)> delimiters = new List<(VisualElement, float, float)>();
        private readonly Dictionary<int, VisualElement> timelineIconViews = new Dictionary<int, VisualElement>();
        /// <summary>What the bar draws right now: the state's entries plus in-flight cast ghosts, minus casts whose
        /// CastQueued beat hasn't played yet and icons whose owner isn't on the board (yet / any more).</summary>
        private readonly Dictionary<int, TimelineEntry> drawn = new Dictionary<int, TimelineEntry>();
        private readonly Dictionary<int, (Color color, float k)> approach = new Dictionary<int, (Color, float)>();
        private readonly HashSet<int> glowing = new HashSet<int>();
        private readonly HashSet<int> unplayedCasts = new HashSet<int>();
        private readonly HashSet<int> cueKeep = new HashSet<int>();
        private readonly Dictionary<int, (Color c, float k, bool next)> unitCue = new Dictionary<int, (Color, float, bool)>();
        private int nextActorEntry = -1, slotEntry = int.MinValue, actingEntry = -1;
        /// <summary>Off for the very first sync of a build, so the opening layout doesn't fire arrival/glow sounds.</summary>
        private bool timelineSeeded;

        private void BuildTimeline(VisualElement root)
        {
            timelineIconViews.Clear(); drawn.Clear(); approach.Clear(); glowing.Clear(); delimiters.Clear();
            nextActorEntry = -1; slotEntry = int.MinValue; actingEntry = -1; timelineSeeded = false;

            var frame = Ui.El("", root);
            frame.name = "timeline";
            frame.pickingMode = PickingMode.Ignore;
            timelineFrame = frame;
            frame.style.position = Position.Absolute;
            frame.style.left = Space.S0; frame.style.right = Space.S0;
            frame.style.top = TimelineY - TimelineFrameHeight / 2f; frame.style.height = TimelineFrameHeight;

            timelineBar = Ui.El("", frame);
            timelineBar.name = "timeline-track";
            timelineBar.pickingMode = PickingMode.Ignore;
            timelineBar.style.position = Position.Absolute;
            timelineBar.style.top = TrackY - Size.Track / 2f; timelineBar.style.height = Size.Track;
            timelineBar.style.backgroundColor = Palette.Text.WithA(0.85f);
            timelineBar.style.borderTopLeftRadius = timelineBar.style.borderTopRightRadius = timelineBar.style.borderBottomLeftRadius = timelineBar.style.borderBottomRightRadius = 3;

            // A small diamond every second, a bigger one every five (no Zone, no "ACT": the left end is acting).
            int n = Mathf.Max(1, Mathf.RoundToInt(BattleConfig.BarSeconds));
            for (int s = 0; s <= n; s++)
            {
                bool major = s % 5 == 0;
                float side = major ? 16f : 10f;
                var d = new VisualElement { name = "tl-tick", pickingMode = PickingMode.Ignore };
                d.style.position = Position.Absolute;
                d.style.width = d.style.height = side;
                d.style.top = TrackY - side / 2f;
                d.style.rotate = new Rotate(45f);
                d.style.backgroundColor = major ? Palette.Text : Palette.TextDim;
                SetBorder(d, 2f, 2f, Palette.Ink);
                frame.Add(d);
                delimiters.Add((d, side, s));
            }

            timelineIcons = Ui.El("", frame);
            timelineIcons.name = "timeline-icons";
            timelineIcons.pickingMode = PickingMode.Ignore;
            timelineIcons.style.position = Position.Absolute;
            timelineIcons.style.left = timelineIcons.style.top = timelineIcons.style.right = timelineIcons.style.bottom = Space.S0;
            timelineIcons.schedule.Execute(CycleCastRings).Every(1200);
            timelineIcons.schedule.Execute(PulseTimeline).Every(33);

            BuildSlot(frame);

            var shield = Ui.Button("GUARD", () => { if (CanCommand()) Commit(new ShieldCommand()); }, "btn--icon", root);
            shield.name = "shield";
            shield.style.position = Position.Absolute;
            shield.style.right = Fit.Edge; shield.style.top = TimelineY - Size.Target / 2f; shield.style.width = shield.style.height = Size.Target;
            shield.style.fontSize = 30;
            LayoutTimelineFrame();
        }

        /// <summary>The prominence slot: a gold socket holding a large diamond of the next (or acting) actor, its seconds under it.</summary>
        private void BuildSlot(VisualElement frame)
        {
            timelineSlot = new VisualElement { name = "tl-slot", pickingMode = PickingMode.Position };
            timelineSlot.style.position = Position.Absolute;
            timelineSlot.style.width = timelineSlot.style.height = SlotSpan;
            timelineSlot.style.left = Fit.Edge; timelineSlot.style.top = TrackY - SlotSpan / 2f;
            frame.Add(timelineSlot);
            slotHalo = Halo(timelineSlot, SlotSpan);
            var socket = DiamondPart(timelineSlot, "tl-socket", SlotSide + 12f, SlotSpan);
            socket.style.backgroundColor = Palette.Ink.WithA(0.7f);
            SetBorder(socket, 8f, 3f, Palette.Gold.WithA(0.9f));
            slotFrame = DiamondPart(timelineSlot, "tl-frame", SlotSide, SlotSpan);
            slotFrame.style.backgroundColor = Palette.Ink;
            slotFrame.style.overflow = Overflow.Hidden;
            SetBorder(slotFrame, 8f, SlotBorder, Palette.Enemy);
            slotFrame.style.display = DisplayStyle.None;
            slotTime = Ui.Text("", "tiny shadow", timelineSlot);
            slotTime.name = "tl-slot-time";
            slotTime.pickingMode = PickingMode.Ignore;
            slotTime.style.position = Position.Absolute;
            slotTime.style.left = slotTime.style.right = Space.S0; slotTime.style.bottom = -Space.S8;
            slotTime.style.unityTextAlign = TextAnchor.MiddleCenter;
            Fonts.UseHeavy(slotTime);
            InspectCard.Attach(timelineSlot, body => drawn.TryGetValue(slotEntry, out var e) && BuildTimelineCard(body, e));
        }

        /// <summary>Re-applies the stage-width-dependent x positions (build and resize).</summary>
        private void LayoutTimelineFrame()
        {
            if (timelineFrame == null) return;
            float start = Fit.Edge + SlotSpan / 2f;
            timelineBar.style.left = start;
            timelineBar.style.width = Mathf.Max(0f, XOfU(1f) + IconSpan / 2f - start);
            float bar = Mathf.Max(0.0001f, BattleConfig.BarSeconds);
            foreach (var (d, side, s) in delimiters) d.style.left = XOfU(s / bar) - side / 2f;
            if (lastAt != null) LayoutIcons(lastAt);
        }

        // ── Diamonds ────────────────────────────────────────────────────────────

        /// <summary>A <paramref name="side"/>-px square turned 45°, centred in a <paramref name="span"/>-px host.</summary>
        private static VisualElement DiamondPart(VisualElement host, string name, float side, float span)
        {
            var d = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            d.style.position = Position.Absolute;
            d.style.width = d.style.height = side;
            d.style.left = d.style.top = (span - side) / 2f;
            d.style.rotate = new Rotate(45f);
            host.Add(d);
            return d;
        }

        /// <summary>The soft glow behind a diamond (hidden until the approach window).</summary>
        private static VisualElement Halo(VisualElement host, float span)
        {
            float size = span * 1.8f;
            var h = new VisualElement { name = "tl-halo", pickingMode = PickingMode.Ignore };
            h.style.position = Position.Absolute;
            h.style.width = h.style.height = size;
            h.style.left = h.style.top = (span - size) / 2f;
            h.style.backgroundImage = new StyleBackground(Art.Glow);
            h.style.opacity = 0f;
            host.Add(h);
            return h;
        }

        /// <summary>
        /// Fills a diamond frame with its art, counter-rotated −45° so it stands upright. The art square is the frame's
        /// inner side × √2 so it covers every corner of the diamond; the frame's overflow:hidden (a rotated, so stencil,
        /// clip) cuts it back to the diamond. A turn shows the owner's portrait; a cast its ability icon, inset so the
        /// whole icon sits inside the diamond.
        /// </summary>
        private void SetDiamondArt(VisualElement frame, float side, float border, TimelineEntry e)
        {
            frame.Q("tl-art")?.RemoveFromHierarchy();
            if (e == null) return;
            float inner = side - 2f * border, s = inner * Sqrt2;
            var art = new VisualElement { name = "tl-art", pickingMode = PickingMode.Ignore };
            art.style.position = Position.Absolute;
            art.style.width = art.style.height = s;
            art.style.left = art.style.top = (inner - s) / 2f;
            art.style.rotate = new Rotate(-45f);
            frame.Add(art);
            if (e.Kind == TimelineKind.Turn) { Ui.PortraitThumb(art, State.UnitById(e.OwnerId)?.Def.Id); return; }
            art.style.backgroundColor = EntryColor(e).WithA(0.22f);
            var tex = Art.AbilityIcon(App.Content.TryAbility(e.AbilityId));
            if (tex == null) return;
            var ic = new VisualElement { name = "tl-ability", pickingMode = PickingMode.Ignore };
            ic.style.position = Position.Absolute;
            ic.style.left = ic.style.top = ic.style.right = ic.style.bottom = Length.Percent(24);
            ic.style.backgroundImage = new StyleBackground(tex);
            ic.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            art.Add(ic);
        }

        private static Color TurnRing(Unit owner) =>
            owner != null && (owner.Def.HasTag("Boss") || owner.IsMultiTile) ? Palette.Boss : Palette.Enemy;

        /// <summary>An icon's own colour: enemy red, boss purple, hero cast teal, enemy charge amber.</summary>
        private Color EntryColor(TimelineEntry e) =>
            e.Kind == TimelineKind.Cast ? (e.CasterTeam == Team.Hero ? Palette.CastHero : Palette.CastEnemy) : TurnRing(State.UnitById(e.OwnerId));

        private VisualElement MakeTimelineIcon(TimelineEntry e)
        {
            var icon = new VisualElement { name = "tl-" + e.Id, pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.flexShrink = 0;
            icon.style.width = icon.style.height = IconSpan;
            icon.style.top = TrackY - IconSpan / 2f; // centred on the line
            Halo(icon, IconSpan);
            var frame = DiamondPart(icon, "tl-frame", IconSide, IconSpan);
            frame.style.backgroundColor = Palette.Ink; // opaque, so the track never shows through
            frame.style.overflow = Overflow.Hidden;
            SetBorder(frame, 5f, IconBorder, EntryColor(e));
            SetDiamondArt(frame, IconSide, IconBorder, e);
            AttachTimelineInspect(icon, e);
            return icon;
        }

        // ── Sync ────────────────────────────────────────────────────────────────

        private void SyncTimeline(Dictionary<int, float> uOverride)
        {
            if (timelineIcons == null || State == null) return;
            drawn.Clear();
            var at = new Dictionary<int, float>();
            foreach (var e in State.Timeline)
            {
                if (e.Kind == TimelineKind.Cast && unplayedCasts.Contains(e.Id)) continue; // its CastQueued beat hasn't played
                if (!board.Units.TryGetValue(e.OwnerId, out var ov) || ov.Dead) continue;
                drawn[e.Id] = e;
                at[e.Id] = Mathf.Clamp01(uOverride != null && uOverride.TryGetValue(e.Id, out var ou) ? ou : BattleEngine.BarU(State, e));
            }
            // A cast that queued and landed inside this action is gone from the state; its ghost rides the bar until it lands.
            foreach (var g in castGhosts.Values)
            {
                if (drawn.ContainsKey(g.EntryId) || State.Timeline.Any(x => x.Id == g.EntryId)) continue;
                if (!board.Units.TryGetValue(g.CasterId, out var cv) || cv.Dead) continue;
                var caster = State.UnitById(g.CasterId);
                drawn[g.EntryId] = new TimelineEntry
                {
                    Id = g.EntryId, Kind = TimelineKind.Cast, OwnerId = g.CasterId, AbilityId = g.AbilityId,
                    CasterTeam = caster != null && caster.IsHero ? Team.Hero : Team.Enemy, TargetUnitId = g.TargetUnitId, TargetTile = g.TargetTile,
                };
                at[g.EntryId] = Mathf.Clamp01(uOverride != null && uOverride.TryGetValue(g.EntryId, out var gu) ? gu : g.U);
            }
            foreach (var kv in drawn)
                if (!timelineIconViews.ContainsKey(kv.Key))
                {
                    var icon = MakeTimelineIcon(kv.Value);
                    timelineIconViews[kv.Key] = icon;
                    timelineIcons.Add(icon);
                }
            foreach (var kv in timelineIconViews.ToList())
                if (!drawn.ContainsKey(kv.Key)) { kv.Value.RemoveFromHierarchy(); timelineIconViews.Remove(kv.Key); }
            lastAt = at;
            LayoutIcons(at);
            MarkNextAttacker(at);
            SyncTimelineStatus();
            SyncCastRings(at);
            timelineSeeded = true;
        }

        /// <summary>Arrival order on a tie (BattleRunner.Tick): casts before turns, then by id.</summary>
        private static bool Earlier(TimelineEntry a, TimelineEntry b) => a.Kind != b.Kind ? a.Kind == TimelineKind.Cast : a.Id < b.Id;

        private bool Halted(TimelineEntry e) => State.UnitById(e.OwnerId) is Unit o && o.IsTimelineHalted;

        /// <summary>
        /// Picks the next actor (lowest u that can move; ties as the engine resolves them), rings it gold and lifts it,
        /// fills the prominence slot, and records each icon's approach glow (0 outside <see cref="ApproachSeconds"/>,
        /// 1 at the left edge). New arrivals into the slot and new glows each make a sound.
        /// </summary>
        private void MarkNextAttacker(Dictionary<int, float> at)
        {
            TimelineEntry next = null;
            foreach (var e in drawn.Values)
            {
                if (Halted(e) || !at.TryGetValue(e.Id, out var u)) continue;
                if (next == null) { next = e; continue; }
                float nu = at[next.Id];
                if (u < nu - 0.0005f || (Mathf.Abs(u - nu) <= 0.0005f && Earlier(e, next))) next = e;
            }
            nextActorEntry = next?.Id ?? -1;
            approach.Clear();
            bool newGlow = false;
            foreach (var e in drawn.Values)
            {
                if (!timelineIconViews.TryGetValue(e.Id, out var icon)) continue;
                bool isNext = e.Id == nextActorEntry;
                var c = EntryColor(e);
                var frame = icon.Q("tl-frame");
                if (frame != null) SetBorder(frame, 5f, isNext ? IconBorder + 1f : IconBorder, isNext ? Palette.Gold : c);
                icon.style.scale = new Scale(isNext ? new Vector2(NextScale, NextScale) : Vector2.one);
                icon.EnableInClassList("tl-next", isNext);
                float k = Halted(e) ? 0f : Mathf.Clamp01(1f - at[e.Id] * BattleConfig.BarSeconds / ApproachSeconds);
                approach[e.Id] = (c, k);
                if (k > 0f) { if (glowing.Add(e.Id)) newGlow = true; }
                else glowing.Remove(e.Id);
            }
            glowing.RemoveWhere(id => !drawn.ContainsKey(id));
            if (newGlow && timelineSeeded) App.Audio.Sfx("battle.channel", 0.4f, 1.5f); // a foe (or spell) enters its last seconds
            SyncSlot(at);
        }

        /// <summary>The prominence slot shows the acting entry during its turn, otherwise the next actor.</summary>
        private void SyncSlot(Dictionary<int, float> at)
        {
            if (timelineSlot == null) return;
            int id = actingEntry >= 0 && drawn.ContainsKey(actingEntry) ? actingEntry : nextActorEntry;
            drawn.TryGetValue(id, out var e);
            if (id != slotEntry)
            {
                slotEntry = id;
                SetDiamondArt(slotFrame, SlotSide, SlotBorder, e);
                slotFrame.style.display = e != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (e != null && timelineSeeded)
                {
                    App.Audio.Sfx("battle.whoosh", 0.5f, 1.25f); // a new actor takes the slot
                    if (!ReduceMotion) App.StartCoroutine(Tween.Pop(timelineSlot, 1.18f, Timing.Standard));
                }
            }
            if (e == null) { slotTime.text = ""; return; }
            SetBorder(slotFrame, 8f, SlotBorder, EntryColor(e));
            float secs = at.TryGetValue(e.Id, out var u) ? u * BattleConfig.BarSeconds : 0f;
            slotTime.text = e.Id == actingEntry || secs < 0.05f ? "NOW" : $"{secs:0.0}s";
            slotTime.style.color = e.Id == actingEntry ? Palette.Gold : Palette.Text;
        }

        /// <summary>
        /// Every icon sits on the same centre line at its u (owner direction 2026-10-10: never lift or bury one). Where
        /// icons overlap they form a cluster, and each member gets its own beat on top: the front one rotates every 1.2 s.
        /// </summary>
        private void LayoutIcons(Dictionary<int, float> at)
        {
            var entries = drawn.Values.Where(e => timelineIconViews.ContainsKey(e.Id) && at.ContainsKey(e.Id))
                .OrderBy(e => at[e.Id]).ThenBy(e => e.Id).ToList();
            foreach (var e in entries)
            {
                var icon = timelineIconViews[e.Id];
                icon.style.left = XOfU(at[e.Id]) - IconSpan / 2f;
                icon.style.top = TrackY - IconSpan / 2f;
            }
            var cluster = new List<TimelineEntry>();
            void Flush()
            {
                if (cluster.Count == 0) return;
                foreach (var e in cluster) timelineIconViews[e.Id].BringToFront();
                if (cluster.Count > 1) timelineIconViews[cluster[castCycle % cluster.Count].Id].BringToFront();
                cluster.Clear();
            }
            float lastX = float.NegativeInfinity;
            foreach (var e in entries)
            {
                float x = XOfU(at[e.Id]);
                if (x - lastX >= IconSpan * 0.85f) Flush();
                cluster.Add(e);
                lastX = x;
            }
            Flush();
        }

        /// <summary>Why an enemy isn't moving: a badge on its timeline diamond for halting/immobilising effects and pushback stun.</summary>
        private void SyncTimelineStatus()
        {
            foreach (var e in drawn.Values)
            {
                if (e.Kind != TimelineKind.Turn || !timelineIconViews.TryGetValue(e.Id, out var icon)) continue;
                var owner = State.UnitById(e.OwnerId);
                var badge = timelineIcons.Q<VisualElement>("tl-status-" + e.Id);
                var status = owner == null ? null : StatusOf(owner, e);
                var art = icon.Q("tl-art");
                if (status == null) { badge?.RemoveFromHierarchy(); if (art != null) art.style.opacity = 1f; continue; }
                if (badge != null && badge.viewDataKey != status.Value.icon) { badge.RemoveFromHierarchy(); badge = null; }
                if (badge == null)
                {
                    badge = Ui.StatusPip(status.Value.icon, status.Value.letter, status.Value.color, 40, timelineIcons);
                    badge.name = "tl-status-" + e.Id;
                    badge.AddToClassList("tl-status");
                    badge.style.position = Position.Absolute;
                }
                badge.style.left = icon.style.left.value.value + IconSpan - 34f;
                badge.style.top = icon.style.top.value.value + IconSpan - 34f;
                badge.BringToFront();
                if (art != null) art.style.opacity = 0.55f; // dimmed: it isn't moving
            }
            foreach (var b in timelineIcons.Query<VisualElement>(className: "tl-status").ToList())
                if (b.name != null && b.name.StartsWith("tl-status-") && !drawn.Values.Any(e => "tl-status-" + e.Id == b.name && State.UnitById(e.OwnerId) is Unit o && o.Alive && StatusOf(o, e) != null))
                    b.RemoveFromHierarchy();
        }

        // ── The glow, animated over time ────────────────────────────────────────

        /// <summary>
        /// 30 Hz: each approaching icon's halo and its owner's tile pulse in the icon's colour, faster and brighter the
        /// nearer it is to acting; the next actor's tile keeps a gold border like its icon. Reduce Motion holds it steady.
        /// </summary>
        private void PulseTimeline()
        {
            if (State == null || board == null || timelineIcons == null || Paused) return;
            float t = Time.unscaledTime;
            unitCue.Clear();
            foreach (var kv in approach)
            {
                if (!timelineIconViews.TryGetValue(kv.Key, out var icon) || !drawn.TryGetValue(kv.Key, out var e)) continue;
                var (c, k) = kv.Value;
                float pulse = ReduceMotion ? 1f : 0.65f + 0.35f * Mathf.Sin(t * (3f + 7f * k));
                var halo = icon.Q("tl-halo");
                if (halo != null)
                {
                    halo.style.unityBackgroundImageTintColor = c;
                    halo.style.opacity = k * pulse;
                }
                bool isNext = kv.Key == nextActorEntry;
                if (k <= 0f && !isNext) continue;
                // One cue per unit (a foe can have its turn and a charge on the bar): the nearer entry's colour wins.
                if (!unitCue.TryGetValue(e.OwnerId, out var have)) have = (c, -1f, false);
                unitCue[e.OwnerId] = k > have.k ? (c, k, have.next || isNext) : (have.c, have.k, have.next || isNext);
            }
            cueKeep.Clear();
            foreach (var kv in unitCue)
            {
                if (!board.Units.TryGetValue(kv.Key, out var v) || v.Dead) continue;
                var (c, k, next) = kv.Value;
                k = Mathf.Max(0f, k);
                float pulse = ReduceMotion ? 1f : 0.65f + 0.35f * Mathf.Sin(t * (3f + 7f * k));
                var fill = c.WithA(0.3f * k * pulse);
                var border = next ? Palette.Gold : c.WithA(0.35f + 0.65f * k * pulse);
                board.SetCue(kv.Key, v.Tile, v.W, v.H, fill, border, next ? 7f : 3f + 4f * k);
                cueKeep.Add(kv.Key);
            }
            board.HideCues(cueKeep);
            if (slotHalo != null)
            {
                // The slot glows in its occupant's colour as it closes in, fully while it acts; an empty socket stays dim gold.
                bool lit = drawn.TryGetValue(slotEntry, out var se);
                float k = !lit ? 0f : Mathf.Max(approach.TryGetValue(slotEntry, out var sa) ? sa.k : 0f, slotEntry == actingEntry ? 1f : 0f);
                float pulse = ReduceMotion ? 1f : 0.65f + 0.35f * Mathf.Sin(t * (3f + 7f * k));
                slotHalo.style.unityBackgroundImageTintColor = lit ? EntryColor(se) : Palette.Gold;
                slotHalo.style.opacity = 0.25f + 0.75f * k * pulse;
            }
        }
    }
}
