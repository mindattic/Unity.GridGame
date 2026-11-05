using System.Collections;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// One hero at a time: portrait slam, an XP bar that ticks through every level crossed, then stat
    /// deltas — fanfare between a battle and PostBattleScreen (owner direction 2026-10-11: "pomp and
    /// circumstance... the same tempo and pause that makes video games enjoyable"). Reuses
    /// CharacterSelectScreen's SlamIn reveal and CutIns.ChargeAnim's tween-driven bar-fill idiom.
    /// Only ever reached via <see cref="PostBattleScreen.RouteFrom"/>, which skips it entirely when
    /// nobody leveled up.
    /// </summary>
    public sealed class LevelUpScreen : GameScreen
    {
        private readonly BattleReport report;
        private readonly string stageId;
        private int index;
        private Coroutine running;
        private readonly List<Coroutine> reveals = new List<Coroutine>();
        private VisualElement portraitRow, xpGauge, statColumn;
        private Label levelLabel;
        private bool skipHandlerBound;
        /// <summary>Set once the router pops this screen. The fanfare coroutine runs on GameApp, not on this screen, so
        /// without this it played on after a ResetTo (the harness's randombattle calls one mid-fanfare): it rebuilt a
        /// popped screen against a generated stage already pruned ("Unknown stage 'skirmish-…'") and its Advance()
        /// Replace()d whatever screen had just been mounted (GG-T127).</summary>
        private bool hidden;

        public LevelUpScreen(BattleReport report, string stageId) { this.report = report; this.stageId = stageId; }
        public override string Id => "levelup";
        public override string Music => "HeroicAge";

        public override void OnHidden()
        {
            hidden = true;
            if (running != null) App.StopCoroutine(running);
            running = null;
            foreach (var co in reveals) if (co != null) App.StopCoroutine(co);
            reveals.Clear();
        }

        protected override void Build(VisualElement root)
        {
            var c = App.Content;
            var (classId, from, _) = report.LevelUps[index];
            var def = c.Class(classId);

            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(PostBattleScreen.ThemeOf(c, report, stageId), 1));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.5f);
            var col = Ui.El("fill col center", root);

            var head = Ui.Text("LEVEL UP!", "title shadow", col);
            Fonts.UseDisplay(head);
            head.style.color = Palette.Gold;
            head.style.marginBottom = 30;

            portraitRow = Ui.El("row", col);
            portraitRow.style.opacity = 0f;
            var frame = Ui.El(null, portraitRow);
            frame.style.width = 360; frame.style.height = 400;
            frame.style.borderTopWidth = frame.style.borderBottomWidth = frame.style.borderLeftWidth = frame.style.borderRightWidth = 4;
            var roleColor = Ui.RoleColor(def.Role);
            frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = roleColor;
            frame.style.backgroundColor = Palette.Ink.WithA(0.6f);
            frame.style.backgroundImage = new StyleBackground(Art.Portrait(classId));
            frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);

            var info = Ui.El("col", portraitRow);
            info.style.marginLeft = 24; info.style.width = 320;
            var nm = Ui.Text(def.Name, "h1 shadow", info);
            Fonts.UseDisplay(nm);
            Ui.RoleBadge(def.Role, info).style.marginBottom = 10;
            levelLabel = Ui.Text($"Lv {from}", "h1 shadow gold", info);
            xpGauge = Ui.Gauge(0f, info, 300);
            xpGauge.style.marginTop = 10;

            statColumn = Ui.El("col", col);
            statColumn.style.marginTop = 24;

            // Root is reused across Refresh() calls (one per hero that leveled up) — Root.Clear() drops
            // child elements but not callbacks registered on Root itself, so this must only ever bind once
            // or each additional hero stacks another handler and a single tap fires Skip() that many times.
            // A tap on the Skip button's own ClickEvent also bubbles up to root — without the Button filter one
            // tap on "Skip ›" ran Skip() twice and jumped past the next hero's fanfare too (GG-T70).
            if (!skipHandlerBound) { root.RegisterCallback<ClickEvent>(e => { if (!(e.target is Button)) Skip(); }); skipHandlerBound = true; }
            var skip = Ui.Button("Skip ›", Skip, "btn--small", root);
            skip.style.position = Position.Absolute; skip.style.right = 30; skip.style.bottom = 40;

            running = App.StartCoroutine(PlayHero());
        }

        private void Skip()
        {
            if (hidden) return;
            if (running != null) App.StopCoroutine(running);
            running = null;
            Advance();
        }

        private void Advance()
        {
            if (hidden) return;
            index++;
            if (index >= report.LevelUps.Count) { App.Router.Replace(new PostBattleScreen(report, stageId)); return; }
            Refresh();
        }

        private IEnumerator PlayHero()
        {
            var (classId, from, to) = report.LevelUps[index];
            var def = App.Content.Class(classId);
            var hero = App.Save.Hero(classId);

            App.Audio.DuckFor(Timing.Cinematic * 3f); // POL-F3: the fanfare plays over a ducked track
            yield return SlamIn(portraitRow);
            yield return Tween.Wait(Timing.Standard);

            // A battle can carry a hero past more than one level; tick fully through each one crossed
            // (bar fills, "Lv N" pops, resets) before a true partial fill for the level landed on.
            for (int lvl = from; lvl < to; lvl++)
            {
                App.Audio.Sfx("battle.crit", 0.6f, 0.8f + 0.1f * (lvl - from));
                yield return Tween.Run(Timing.Standard, k => Ui.SetGauge(xpGauge, k));
                levelLabel.text = $"Lv {lvl + 1}";
                reveals.Add(App.StartCoroutine(Tween.Pop(levelLabel, 1.3f, 0.2f)));
                yield return Tween.Wait(Timing.Quick);
                Ui.SetGauge(xpGauge, 0f);
            }
            int intoFinal = hero != null ? hero.TotalXp - Formulas.TotalXpForLevel(to) : 0;
            float finalFrac = Mathf.Clamp01(intoFinal / (float)Formulas.XpToNext(to));
            App.Audio.Sfx("battle.orb", 0.5f);
            yield return Tween.Run(Timing.Emphasis, k => Ui.SetGauge(xpGauge, Mathf.Lerp(0f, finalFrac, k)));
            yield return Tween.Wait(Timing.Standard);

            var before = Formulas.StatsAtLevel(def, from);
            var after = Formulas.StatsAtLevel(def, to);
            foreach (var (label, b, a) in StatRows(before, after))
            {
                var row = Ui.El("row", statColumn);
                Ui.Text(label, "small dim", row).style.width = 60;
                Ui.Text($"{b:0} → {a:0}  (+{a - b:0})", "small good", row);
                reveals.Add(App.StartCoroutine(Tween.Pop(row, 1.15f, 0.15f)));
                yield return Tween.Wait(Timing.Instant);
            }
            yield return Tween.Wait(1.1f); // the fanfare hold
            running = null;
            Advance();
        }

        private static IEnumerable<(string label, float before, float after)> StatRows(Stats before, Stats after)
        {
            if (after.Strength > before.Strength) yield return ("STR", before.Strength, after.Strength);
            if (after.Vitality > before.Vitality) yield return ("VIT", before.Vitality, after.Vitality);
            if (after.Agility > before.Agility) yield return ("AGI", before.Agility, after.Agility);
            if (after.Speed > before.Speed) yield return ("SPD", before.Speed, after.Speed);
            if (after.Stamina > before.Stamina) yield return ("STA", before.Stamina, after.Stamina);
            if (after.Intelligence > before.Intelligence) yield return ("INT", before.Intelligence, after.Intelligence);
            if (after.Wisdom > before.Wisdom) yield return ("WIS", before.Wisdom, after.Wisdom);
            if (after.Luck > before.Luck) yield return ("LCK", before.Luck, after.Luck);
        }

        /// <summary>Bombastic reveal, verbatim from CharacterSelectScreen.SlamIn: oversized and tilted,
        /// punches down past full size with a back-ease overshoot, then a small settle shake.</summary>
        private IEnumerator SlamIn(VisualElement target)
        {
            target.style.scale = new Scale(Vector2.one * 1.5f);
            target.style.rotate = new Rotate(-6f);
            App.Audio.Sfx("battle.shatter", 0.55f);
            yield return Tween.Run(Timing.Standard, k =>
            {
                float s = Mathf.LerpUnclamped(1.5f, 1f, Tween.Back(k));
                target.style.scale = new Scale(Vector2.one * s);
                target.style.opacity = Mathf.Clamp01(k * 2.2f);
                target.style.rotate = new Rotate(Mathf.LerpUnclamped(-6f, 0f, k));
            });
            target.style.scale = new Scale(Vector2.one);
            target.style.opacity = 1f;
            target.style.rotate = new Rotate(0f);
            yield return Tween.Shake(target, 6f, 0.14f);
        }
    }
}
