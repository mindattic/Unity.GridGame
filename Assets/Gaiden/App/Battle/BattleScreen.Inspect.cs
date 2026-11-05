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
    /// GG-T252 (POL-D1/D2): hold-to-inspect on the battle's units, slots, statuses and timeline icons, and the outcome
    /// preview on the confirm card and the drag preview. Presentation only — every number comes from
    /// <see cref="OutcomePreviewer"/>, which runs on clones and never touches the battle.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private int inspectUnitId = -1;
        private float inspectSince;
        private bool inspectOpen;
        private readonly List<VisualElement> outcomeTags = new List<VisualElement>();

        private bool Revealed(Unit u) =>
            u.IsHero || State.Results.Scanned.Contains(u.Def.Id) || (App.Save?.Bestiary.TryGetValue(u.Def.Id, out var be) == true && be.Scanned);

        private void AttachSlotInspect(VisualElement slot, int idx) =>
            InspectCard.Attach(slot, body =>
            {
                var hero = State.UnitById(SelectedHeroId);
                var ab = hero == null ? null : BattleEngine.SlotAbility(State, hero, idx);
                return InspectCard.Ability(body, ab, ab == null ? null : SlotNote(hero, ab));
            });

        private string SlotNote(Unit hero, AbilityDef a)
        {
            if (hero.Cooldowns.TryGetValue(a.Id, out var cd) && cd > 0) return $"{hero.Name} can use it again in {cd} turns.";
            if (a.Cost > State.Bank.Count) return $"Needs {a.Cost} orbs — you have {State.Bank.Count}.";
            return $"Tap to aim with {hero.Name}.";
        }

        private void AttachTimelineInspect(VisualElement icon, TimelineEntry e)
        {
            icon.pickingMode = PickingMode.Position;
            InspectCard.Attach(icon, body =>
            {
                var owner = State.UnitById(e.OwnerId);
                if (e.Kind == TimelineKind.Cast)
                    return InspectCard.Ability(body, App.Content.TryAbility(e.AbilityId),
                        owner == null ? "Lands when it reaches ACT." : $"{owner.Name}'s spell — lands when it reaches ACT. Hit {owner.Name} to push it back.");
                return owner != null && InspectCard.Unit(body, State, owner, Revealed(owner));
            });
        }

        private void AttachStatusInspect(VisualElement pip, BuffDef d, int remaining)
        {
            pip.pickingMode = PickingMode.Position;
            InspectCard.Attach(pip, body => InspectCard.Buff(body, d, remaining));
        }

        // ── Holding a unit on the board ─────────────────────────────────────────

        private void BeginBoardInspect(Unit u)
        {
            inspectUnitId = u.Id;
            inspectSince = Time.unscaledTime;
            inspectOpen = false;
        }

        private void TickBoardInspect()
        {
            if (inspectUnitId < 0 || inspectOpen || dragging) return;
            if (Time.unscaledTime - inspectSince < InspectCard.HoldSeconds) return;
            var u = State.UnitById(inspectUnitId);
            if (u == null || !u.Alive || !board.Units.TryGetValue(u.Id, out var v)) { inspectUnitId = -1; return; }
            inspectOpen = InspectCard.Show(v.Root, body => InspectCard.Unit(body, State, u, Revealed(u)));
        }

        /// <summary>Ends a board hold; true when it had opened a card (the release then does nothing else).</summary>
        private bool EndBoardInspect()
        {
            bool was = inspectOpen;
            inspectUnitId = -1;
            inspectOpen = false;
            if (was) InspectCard.Hide();
            return was;
        }

        // ── Outcome preview ─────────────────────────────────────────────────────

        private static string Range(int a, int b) => a == b ? a.ToString() : $"{a}–{b}";

        /// <summary>One line per unit the pending pick would touch: damage or heal range, KO chance, statuses, orbs.</summary>
        private void OutcomeLines(VisualElement parent, AbilityDef a)
        {
            var hero = State.UnitById(SelectedHeroId);
            if (hero == null || !PendingTarget.HasValue) return;
            var p = OutcomePreviewer.Preview(State, new UseSlotCommand { HeroId = hero.Id, Slot = TargetingSlot, Target = PendingTarget.Value }, 6);
            if (p.Refused) return;
            int shown = 0;
            foreach (var t in p.Targets.OrderByDescending(t => t.DamageMax + t.HealMax))
            {
                var u = State.UnitById(t.UnitId);
                if (u == null || (t.DamageMax == 0 && t.HealMax == 0 && t.StatusChance.Count == 0)) continue;
                if (++shown > 3) break;
                var parts = new List<string>();
                if (t.DamageMax > 0) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Palette.Bad)}>−{Range(t.DamageMin, t.DamageMax)}</color>");
                if (t.HealMax > 0) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Palette.Good)}>+{Range(t.HealMin, t.HealMax)}</color>");
                if (t.KillChance > 0f) parts.Add(t.KillChance >= 1f ? "KO" : $"KO {Mathf.RoundToInt(t.KillChance * 100)}%");
                foreach (var kv in t.StatusChance)
                    parts.Add($"{App.Content.TryBuff(kv.Key)?.Name ?? kv.Key}{(kv.Value < 1f ? $" {Mathf.RoundToInt(kv.Value * 100)}%" : "")}");
                var l = Ui.Text($"{u.Name}: {string.Join(" · ", parts)}", "t-caption", parent);
                l.name = "confirm-outcome";
                l.style.whiteSpace = WhiteSpace.Normal;
            }
            if (p.OrbsMax > 0)
                Ui.Text($"Mana +{Range(p.OrbsMin, p.OrbsMax)} orb{(p.OrbsMax == 1 ? "" : "s")}", "t-caption", parent).style.color = Palette.Mana;
        }

        /// <summary>Small damage tags over the foes a drop would pincer (and its KO odds), refreshed per drag step.</summary>
        private void ShowDragOutcome(MovePreview pv)
        {
            ClearOutcomeTags();
            if (pv.Pincers.Count == 0 || dragPath.Count == 0) return;
            var p = OutcomePreviewer.Preview(State, new MoveCommand { HeroId = dragHeroId, Path = new List<Coord>(dragPath) }, 3);
            if (p.Refused) return;
            foreach (var t in p.Targets)
            {
                var u = State.UnitById(t.UnitId);
                if (u == null || u.IsHero || t.DamageMax <= 0) continue;
                var at = pv.Positions.TryGetValue(u.Id, out var pos) ? pos : u.Pos;
                var c = BoardView.CenterOf(at);
                string text = $"−{Range(t.DamageMin, t.DamageMax)}" + (t.KillChance > 0f ? (t.KillChance >= 1f ? " KO" : $" KO {Mathf.RoundToInt(t.KillChance * 100)}%") : "");
                var tag = new Label(text) { name = "outcome-tag", pickingMode = PickingMode.Ignore };
                tag.AddToClassList("shadow");
                tag.style.position = Position.Absolute;
                tag.style.width = BoardView.Tile * 1.6f;
                tag.style.left = c.x - BoardView.Tile * 0.8f;
                tag.style.top = c.y - BoardView.Tile * 0.46f; // inside the foe's own tile, along its top edge
                tag.style.unityTextAlign = TextAnchor.MiddleCenter;
                tag.style.fontSize = TypeScale.Caption;
                tag.style.color = t.KillChance > 0f ? Palette.Gold : Palette.Text;
                Fonts.UseHeavy(tag);
                board.FxLayer.Add(tag);
                outcomeTags.Add(tag);
            }
        }

        private void ClearOutcomeTags()
        {
            foreach (var t in outcomeTags) t.RemoveFromHierarchy();
            outcomeTags.Clear();
        }

        // ── Cancelling a drag ───────────────────────────────────────────────────

        /// <summary>A drop off the board, or back on the start tile with nothing shoved, is a cancel: no command, no clock.</summary>
        private bool DropCancels(Vector2 local)
        {
            bool offBoard = local.x < 0 || local.y < 0 || local.x > BoardView.Tile * BattleConfig.Cols || local.y > BoardView.Tile * BattleConfig.Rows;
            var hero = State.UnitById(dragHeroId);
            if (hero == null) return false;
            bool home = dragTile == hero.Pos && (preview == null || ShovePreview.Of(State, dragHeroId, preview).Displaced.Count == 0);
            return offBoard || home;
        }

        private void CancelDrag()
        {
            if (!dragging) return;
            float held = holdWindow > 0f ? Mathf.Min(holdElapsed, holdWindow) : holdElapsed;
            if (BattleEngine.IsTimeUp(State, held)) { CommitDrag(); return; } // a forced drop still ends the turn (§2.0)
            dragging = false;
            pressing = false;
            board.Lines.ClearLines();
            board.ClearMarks();
            ClearOutcomeTags();
            var v = board.Units[dragHeroId];
            board.Trail.Release(v.Root);
            v.Root.style.scale = new Scale(Vector2.one);
            EndHold();
            board.Place(v, State.UnitById(dragHeroId).Pos);
            dragPath.Clear();
            preview = null;
            App.Audio.Sfx("ui.cancel");
            ShowBanner("Move cancelled");
            SyncAll();
        }
    }
}
