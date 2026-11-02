using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    public sealed partial class BattleScreen
    {
        private const float DragThreshold = 18f;
        // Diagonal slip (BattleConfig.DiagonalSlipEnabled): how fast and how close to 45° a drag must be,
        // frame to frame, to offer a diagonal step instead of the safe two-cardinal-step route.
        private const float DiagonalSlipMinSpeed = BoardView.Tile * 6f; // px/s
        private const float DiagonalSlipAngleTolerance = 12f; // degrees off a true 45°

        private void OnPointerDown(PointerDownEvent evt)
        {
            ClearHint();
            if (!CanCommand() || pressing) return;
            var tile = board.TileAt(evt.localPosition);
            var unit = State.UnitAt(tile);

            if (TargetingSlot >= 0)
            {
                // Show the area this pick would hit; commit on release over the same tile.
                pressing = true;
                pressPos = evt.localPosition;
                dragTile = tile;
                PreviewTarget(tile);
                board.Root.CapturePointer(evt.pointerId);
                return;
            }

            if (unit != null && unit.IsHero)
            {
                SelectHero(unit.Id);
                if (!BattleEngine.CanMove(State, unit.Id)) { ShowBanner($"{unit.Name} can't move"); return; }
                pressing = true;
                dragHeroId = unit.Id;
                pressPos = evt.localPosition;
                dragTile = unit.Pos;
                dragPath.Clear();
                preview = null;
                lastPointerPos = evt.localPosition;
                lastPointerTime = Time.unscaledTime;
                board.Root.CapturePointer(evt.pointerId);
            }
            else if (unit != null)
            {
                panelUnitId = unit.Id;
                SyncPanel();
                App.Audio.Sfx("ui.click");
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pressing || Paused) return; // a drag held through Escape-to-pause stays frozen (GG-T126)
            Vector2 local = evt.localPosition;
            if (TargetingSlot >= 0)
            {
                var t = board.TileAt(local);
                if (t != dragTile) { dragTile = t; PreviewTarget(t); }
                return;
            }
            HandlePointerPosition(local);
        }

        /// <summary>
        /// The real drag-following/diagonal-slip-gating logic, factored out of <see cref="OnPointerMove"/> so a
        /// simulated human cursor (<see cref="SimulatedHumanDrag"/>) can drive the exact same code a real
        /// pointer does — a diagonal slip succeeds or fails there for the same kinematic reasons either way,
        /// never by being told the outcome.
        /// </summary>
        private void HandlePointerPosition(Vector2 local)
        {
            if (!dragging)
            {
                if ((local - pressPos).magnitude < DragThreshold) return;
                BeginDrag();
            }
            // Diagonal slip tech: a fast, near-45° flick between polled frames offers a diagonal step;
            // anything slower/straighter falls back to the safe two-cardinal-step route, unchanged.
            var rawDelta = local - lastPointerPos;
            float dt = Mathf.Max(0.0001f, Time.unscaledTime - lastPointerTime);
            float speed = rawDelta.magnitude / dt;
            float angleFromDiagonal = Mathf.Abs(Mathf.Atan2(Mathf.Abs(rawDelta.y), Mathf.Max(0.0001f, Mathf.Abs(rawDelta.x))) * Mathf.Rad2Deg - 45f);
            bool fastDiagonal = BattleConfig.DiagonalSlipEnabled && speed > DiagonalSlipMinSpeed && angleFromDiagonal < DiagonalSlipAngleTolerance;
            lastPointerPos = local;
            lastPointerTime = Time.unscaledTime;
            var v = board.Units[dragHeroId];
            // The hero follows the finger, lifted slightly so it isn't hidden under it.
            v.Root.style.left = Mathf.Clamp(local.x - BoardView.Tile / 2f, -BoardView.Tile * 0.3f, BoardView.Tile * (BattleConfig.Cols - 0.7f));
            v.Root.style.top = Mathf.Clamp(local.y - BoardView.Tile * 0.62f, -BoardView.Tile * 0.3f, BoardView.Tile * (BattleConfig.Rows - 0.7f));
            var over = board.TileAt(new Vector2(local.x, local.y - BoardView.Tile * 0.12f));
            ExtendPath(over, fastDiagonal);
        }

        /// <summary>Starts a drag on <paramref name="heroId"/> with no real pointer to capture — used by the
        /// simulated human cursor (<see cref="SimulatedHumanDrag"/>) instead of <see cref="OnPointerDown"/>.</summary>
        private bool BeginSimulatedDrag(int heroId)
        {
            var hero = State.UnitById(heroId);
            if (hero == null || !hero.IsHero || !BattleEngine.CanMove(State, heroId)) return false;
            ClearHint();
            SelectHero(heroId);
            pressing = true;
            dragHeroId = heroId;
            var startPos = BoardView.CenterOf(hero.Pos);
            pressPos = startPos;
            dragTile = hero.Pos;
            dragPath.Clear();
            preview = null;
            lastPointerPos = startPos;
            lastPointerTime = Time.unscaledTime;
            return true;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            // Release first: when the drag timer auto-commits mid-drag (CommitDrag clears `pressing`), the
            // finger/mouse is still captured — returning before this left board.Root holding the capture, so
            // the next click anywhere (a slot, GUARD, the menu) was routed to the board instead (GG-T70).
            bool wasPressing = pressing;
            if (board.Root.HasPointerCapture(evt.pointerId)) board.Root.ReleasePointer(evt.pointerId);
            if (!wasPressing) return;
            if (TargetingSlot >= 0)
            {
                pressing = false;
                var t = board.TileAt(evt.localPosition);
                var hero = State.UnitById(SelectedHeroId);
                var a = hero == null ? null : BattleEngine.SlotAbility(State, hero, TargetingSlot);
                bool legal = hero != null && a != null && a.Mode != TargetMode.Auto && TargetResolver.IsValidPick(State, a, hero, t);
                if (legal && (!ConfirmActions || AutoPlay)) Commit(new UseSlotCommand { HeroId = hero.Id, Slot = TargetingSlot, Target = t });
                else if (legal && PendingTarget == t) ConfirmPending(); // a second tap on the same pick confirms
                else if (legal) SetPending(t);
                else
                {
                    if (a == null || a.Mode != TargetMode.Auto) App.Audio.Sfx("ui.error");
                    RedrawTargeting();
                }
                return;
            }
            if (dragging) CommitDrag();
            pressing = false;
        }

        private void BeginDrag()
        {
            dragging = true;
            dragTimeLeft = BattleConfig.DragTimerSeconds;
            var v = board.Units[dragHeroId];
            v.Root.BringToFront();
            v.Root.style.scale = new Scale(new Vector2(1.12f, 1.12f));
            v.SetGlow(new Color(1f, 0.95f, 0.6f), 0.9f);
            App.Audio.Sfx("pickup");
            ShowBanner("");
        }

        /// <summary>Turns the tile under the finger into cardinal steps from the last logical tile (one tile at a
        /// time, §1.1) — or, on a fast near-45° flick straight onto a diagonal tile, a single diagonal slip.</summary>
        private void ExtendPath(Coord target, bool fastDiagonal = false)
        {
            int guard = 0;
            while (dragTile != target && guard++ < 16)
            {
                int dx = target.X - dragTile.X, dy = target.Y - dragTile.Y;
                if (fastDiagonal && Math.Abs(dx) == 1 && Math.Abs(dy) == 1)
                {
                    var diag = new Coord(dragTile.X + Math.Sign(dx), dragTile.Y + Math.Sign(dy));
                    var diagCandidate = new List<Coord>(dragPath) { diag };
                    var diagPv = BattleEngine.PreviewMove(State, dragHeroId, diagCandidate);
                    if (diagPv.StepsTaken.Count == diagCandidate.Count)
                    {
                        dragPath.Add(diag);
                        dragTile = diag;
                        preview = diagPv;
                        App.Audio.Sfx("dash", 0.7f);
                        ApplyPreview(diagPv, dragHeroId);
                        continue;
                    }
                }
                Coord step = Math.Abs(dx) >= Math.Abs(dy)
                    ? new Coord(dragTile.X + Math.Sign(dx), dragTile.Y)
                    : new Coord(dragTile.X, dragTile.Y + Math.Sign(dy));
                var candidate = new List<Coord>(dragPath) { step };
                var pv = BattleEngine.PreviewMove(State, dragHeroId, candidate);
                if (pv.StepsTaken.Count < candidate.Count)
                {
                    // Blocked (edge / boss / snake): try the other axis once, else hold position.
                    if (dx != 0 && dy != 0)
                    {
                        Coord alt = Math.Abs(dx) >= Math.Abs(dy)
                            ? new Coord(dragTile.X, dragTile.Y + Math.Sign(dy))
                            : new Coord(dragTile.X + Math.Sign(dx), dragTile.Y);
                        candidate[candidate.Count - 1] = alt;
                        pv = BattleEngine.PreviewMove(State, dragHeroId, candidate);
                        if (pv.StepsTaken.Count < candidate.Count) return;
                        step = alt;
                    }
                    else return;
                }
                dragPath.Add(step);
                dragTile = step;
                preview = pv;
                App.Audio.Sfx("move", 0.5f);
                ApplyPreview(pv, dragHeroId);
            }
        }

        /// <summary>Slide displaced actors to their preview tiles and draw the pincers this drop would make.</summary>
        private void ApplyPreview(MovePreview pv, int heroId)
        {
            foreach (var kv in pv.Positions)
            {
                if (kv.Key == heroId) continue;
                if (!board.Units.TryGetValue(kv.Key, out var v)) continue;
                if (v.Tile != kv.Value)
                {
                    App.Audio.Sfx("slide", 0.6f);
                    var from = BoardView.PosOf(v.Tile);
                    v.Tile = kv.Value;
                    App.StartCoroutine(Tween.Move(v.Root, from, BoardView.PosOf(kv.Value), 0.1f));
                }
            }
            board.Lines.ClearLines();
            board.ClearMarks();
            foreach (var u in State.Units)
                if (board.Units.TryGetValue(u.Id, out var uv) && u.Alive && u.Id != heroId) uv.SetGlow(Color.white, 0f);
            foreach (var p in pv.Pincers)
            {
                pv.Positions.TryGetValue(p.A.Id, out var a);
                pv.Positions.TryGetValue(p.B.Id, out var b);
                // Preview: the group outline and edge links the drop would make (no line across the art).
                // The pincer's units belong to the preview's cloned state, so their positions are the drop's (GG-T98).
                var group = new List<(Coord, int, int)> { (a, p.A.W, p.A.H), (b, p.B.W, p.B.H) };
                var oppAt = p.Opponents.Select(o => new UnitFootprint { UnitId = o.Id, At = pv.Positions.TryGetValue(o.Id, out var op) ? op : o.Pos, W = o.W, H = o.H }).ToList();
                foreach (var o in oppAt) group.Add((o.At, o.W, o.H));
                var geometryError = PincerGeometry.Error(new UnitFootprint { UnitId = p.A.Id, At = a, W = p.A.W, H = p.A.H }, new UnitFootprint { UnitId = p.B.Id, At = b, W = p.B.W, H = p.B.H }, oppAt);
                if (geometryError != null && Debug.isDebugBuild) Debug.LogError($"[GG-T98] malformed preview pincer outline: {geometryError}");
                board.Lines.Outline(BoardView.GroupRect(group), new Color(1f, 0.85f, 0.3f, 0.9f), 8f, 1f);
                foreach (var sup in p.SupportersA.Concat(p.SupportersB).Distinct())
                {
                    // GG-T187: link each supporter to the unit its chain runs through (an attacker or the next ally).
                    var via = p.LinkOf(sup) ?? p.AttackerOf(sup);
                    var sp = pv.Positions.TryGetValue(sup.Id, out var spp) ? spp : sup.Pos;
                    var partner = pv.Positions.TryGetValue(via.Id, out var vp) ? vp : via.Pos;
                    var (la, lb) = BoardView.EdgeLink(sp, partner);
                    board.Lines.Add(la, lb, new Color(0.4f, 0.8f, 1f, 0.9f), 10f);
                }
                foreach (var o in p.Opponents)
                {
                    foreach (var t in o.Tiles()) board.Mark(t, new Color(1f, 0.25f, 0.2f), 0.32f);
                    if (board.Units.TryGetValue(o.Id, out var ov)) ov.SetGlow(new Color(1f, 0.3f, 0.2f), 0.8f);
                }
                foreach (var s in p.SupportersA) if (board.Units.TryGetValue(s.Id, out var sv)) sv.SetGlow(new Color(0.4f, 0.8f, 1f), 0.7f);
                foreach (var s in p.SupportersB) if (board.Units.TryGetValue(s.Id, out var sv)) sv.SetGlow(new Color(0.4f, 0.8f, 1f), 0.7f);
            }
            if (pv.Pincers.Count == 0) { ShowBanner(""); return; }
            string banner = pv.Pincers.Count > 1 ? $"PINCER ×{pv.Pincers.Count}!" : "PINCER!";
            // GG-T39: the attacker (either endpoint, never a supporter) whose weapon is on its last point
            // would wear it to 0 and shatter in this very drop (§24.5) — call that out before it's a surprise.
            Unit shatterer = null;
            foreach (var p in pv.Pincers)
            {
                if (p.A.IsHero && p.A.WeaponId != null && p.A.WeaponDurability == 1) { shatterer = p.A; break; }
                if (p.B.IsHero && p.B.WeaponId != null && p.B.WeaponDurability == 1) { shatterer = p.B; break; }
            }
            if (shatterer != null)
                banner += $"  This pincer will shatter your {App.Content.TryItem(shatterer.WeaponId)?.Name ?? "weapon"}!";
            ShowBanner(banner);
        }

        private void CommitDrag(BattleCommand perfectCmd = null)
        {
            if (!dragging) return;
            dragging = false;
            pressing = false;
            board.Lines.ClearLines();
            board.ClearMarks();
            var v = board.Units[dragHeroId];
            v.Root.style.scale = new Scale(Vector2.one);
            if (dragPath.Count == 0)
            {
                board.Place(v, State.UnitById(dragHeroId).Pos);
                SyncAll();
                return;
            }
            float timeLeft = App.Save != null && App.Save.Settings.DragTimer ? Mathf.Max(0f, dragTimeLeft) : 0f;
            var cmd = new MoveCommand { HeroId = dragHeroId, Path = new List<Coord>(dragPath), TimeLeftSeconds = timeLeft };
            // Snap the dragged hero onto its final tile before playback.
            App.StartCoroutine(Tween.Move(v.Root, new Vector2(v.Root.resolvedStyle.left, v.Root.resolvedStyle.top), BoardView.PosOf(dragTile), 0.06f));
            Commit(cmd, true, perfectCmd);
        }

        private void PreviewTarget(Coord tile)
        {
            var hero = State.UnitById(SelectedHeroId);
            var a = hero == null ? null : BattleEngine.SlotAbility(State, hero, TargetingSlot);
            if (a == null || a.Mode == TargetMode.Auto) return;
            board.ClearMarks();
            foreach (var vt in BattleEngine.ValidTargets(State, hero.Id, TargetingSlot)) board.Mark(vt, new Color(1f, 0.82f, 0.3f), 0.25f);
            if (!TargetResolver.IsValidPick(State, a, hero, tile)) return;
            bool harmful = a.BaseDamage > 0 || a.Filter == TargetFilter.EnemyOnly;
            var color = harmful ? new Color(1f, 0.3f, 0.25f) : new Color(0.4f, 0.9f, 0.5f);
            if (a.IsTeleport) { board.Mark(tile, new Color(0.5f, 0.8f, 1f), 0.6f); return; }
            foreach (var t in TargetResolver.Tiles(tile, a.Shape, a.Radius)) board.Mark(t, color, 0.38f);
        }
    }
}
