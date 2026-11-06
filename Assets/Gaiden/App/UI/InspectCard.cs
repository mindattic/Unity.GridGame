using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Hold any icon to read it (GG-T252, POL-D1 recognition over recall): one floating card anchored to the held
    /// element, clamped inside the stage and the safe area, shown while the finger stays down and gone on release.
    /// A press that opened the card never also fires the element's tap.
    /// </summary>
    public static class InspectCard
    {
        public const float HoldSeconds = 0.45f;

        private static VisualElement card;
        private static VisualElement shownFor;
        private static EventCallback<PointerUpEvent> globalUp;
        private static EventCallback<PointerDownEvent> globalDown;
        private static VisualElement globalRoot;
        // A tapped term card (GG-T300) closed by the press that re-taps its own word must not reopen on that tap's click.
        private static VisualElement closedFor;
        private static float closedAt;

        public static bool IsOpen => card != null;
        public static VisualElement ShownFor => shownFor;

        /// <summary>Makes <paramref name="e"/> hold-to-inspect. <paramref name="build"/> fills the card's body and returns
        /// false when there is nothing to show (e.g. an empty slot).</summary>
        public static void Attach(VisualElement e, Func<VisualElement, bool> build)
        {
            bool pressed = false, opened = false, swallowClick = false;
            IVisualElementScheduledItem timer = null;
            e.RegisterCallback<PointerDownEvent>(_ =>
            {
                pressed = true; opened = false;
                timer?.Pause();
                timer = e.schedule.Execute(() =>
                {
                    if (!pressed || e.panel == null) return;
                    opened = Show(e, build);
                }).StartingIn((long)(HoldSeconds * 1000));
            }, TrickleDown.TrickleDown);
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                pressed = false;
                timer?.Pause();
                if (!opened) return;
                opened = false;
                swallowClick = true;
                e.schedule.Execute(() => swallowClick = false).StartingIn(80);
                Hide();
                // The press read the card; it must not also act (a slot, a button, a menu row).
                if (e.HasPointerCapture(ev.pointerId)) e.ReleasePointer(ev.pointerId);
                ev.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            e.RegisterCallback<PointerLeaveEvent>(_ => { if (!opened) { pressed = false; timer?.Pause(); } });
            e.RegisterCallback<PointerCancelEvent>(_ => { pressed = false; timer?.Pause(); if (opened) { opened = false; Hide(); } });
            e.RegisterCallback<ClickEvent>(ev =>
            {
                if (!swallowClick) return;
                swallowClick = false;
                ev.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Opens the card at <paramref name="anchor"/> now. Returns false if <paramref name="build"/> had nothing.</summary>
        public static bool Show(VisualElement anchor, Func<VisualElement, bool> build) => Show(anchor, build, false);

        /// <summary><paramref name="sticky"/>: the card stays after release and the next press anywhere closes it (a
        /// tapped glossary term, GG-T300); otherwise it is a hold card, gone on release.</summary>
        private static bool Show(VisualElement anchor, Func<VisualElement, bool> build, bool sticky)
        {
            var app = GameApp.I;
            var layer = app?.Overlay;
            if (layer == null || anchor == null) return false;
            HideNow();

            var c = new VisualElement { name = "inspect-card", pickingMode = PickingMode.Ignore };
            c.style.position = Position.Absolute;
            c.style.width = Mathf.Min(GameApp.StageWidth - Space.S32 * 2f, 860f);
            c.style.paddingLeft = c.style.paddingRight = Space.S24;
            c.style.paddingTop = c.style.paddingBottom = Space.S24;
            c.style.backgroundColor = Palette.Panel;
            c.style.borderTopWidth = c.style.borderBottomWidth = c.style.borderLeftWidth = c.style.borderRightWidth = 4;
            var frame = Art.ThemeAccent;
            c.style.borderTopColor = c.style.borderBottomColor = c.style.borderLeftColor = c.style.borderRightColor = frame;
            c.style.borderTopLeftRadius = c.style.borderTopRightRadius = c.style.borderBottomLeftRadius = c.style.borderBottomRightRadius = Space.S24;
            c.style.opacity = 0f;
            c.style.visibility = Visibility.Hidden;
            if (!build(c)) return false;

            layer.Add(c);
            card = c;
            shownFor = anchor;
            c.RegisterCallback<GeometryChangedEvent>(Place);

            // A release anywhere closes it, even if the finger drifted off the held element.
            globalRoot = anchor.panel?.visualTree;
            if (globalRoot != null && !sticky)
            {
                globalUp = _ => Hide();
                globalRoot.RegisterCallback(globalUp, TrickleDown.TrickleDown);
            }
            else if (globalRoot != null)
            {
                globalDown = _ => { closedFor = shownFor; closedAt = Time.realtimeSinceStartup; Hide(); };
                globalRoot.RegisterCallback(globalDown, TrickleDown.TrickleDown);
            }
            app.Audio?.Sfx("ui.select", 0.5f);
            return true;
        }

        private static void Place(GeometryChangedEvent _)
        {
            var c = card;
            if (c == null || shownFor == null || c.parent == null) return;
            c.UnregisterCallback<GeometryChangedEvent>(Place);
            var layer = c.parent;
            var a = layer.WorldToLocal(shownFor.worldBound);
            float w = c.resolvedStyle.width, h = c.resolvedStyle.height;
            var screens = layer.parent?.Q("screens");
            float safeTop = (screens?.resolvedStyle.top ?? 0f) + Space.S16;
            float safeBottom = layer.resolvedStyle.height - (screens?.resolvedStyle.bottom ?? 0f) - Space.S16;
            float left = Mathf.Clamp(a.center.x - w / 2f, Space.S32, Mathf.Max(Space.S32, layer.resolvedStyle.width - Space.S32 - w));
            // Above the finger when it fits (the finger hides what is under it), else below.
            float top = a.yMin - h - Space.S16;
            if (top < safeTop) top = a.yMax + Space.S16;
            top = Mathf.Clamp(top, safeTop, Mathf.Max(safeTop, safeBottom - h));
            c.style.left = left;
            c.style.top = top;
            c.style.visibility = Visibility.Visible;
            GameApp.I.StartCoroutine(Appear(c));
        }

        private static IEnumerator Appear(VisualElement c)
        {
            if (Tween.Paused) { c.style.opacity = 1f; c.style.scale = new Scale(Vector2.one); yield break; }
            yield return Tween.Run(Timing.Quick, k =>
            {
                c.style.opacity = k;
                c.style.scale = new Scale(Vector2.one * Mathf.Lerp(0.92f, 1f, k));
            }, Ease.Back);
        }

        public static void Hide()
        {
            var c = card;
            DetachGlobal();
            card = null;
            shownFor = null;
            if (c == null) return;
            if (Tween.Paused || GameApp.I == null) { c.RemoveFromHierarchy(); return; }
            GameApp.I.StartCoroutine(Disappear(c));
        }

        private static void HideNow()
        {
            DetachGlobal();
            card?.RemoveFromHierarchy();
            card = null;
            shownFor = null;
        }

        private static void DetachGlobal()
        {
            if (globalRoot != null && globalUp != null) globalRoot.UnregisterCallback(globalUp, TrickleDown.TrickleDown);
            if (globalRoot != null && globalDown != null) globalRoot.UnregisterCallback(globalDown, TrickleDown.TrickleDown);
            globalRoot = null;
            globalUp = null;
            globalDown = null;
        }

        private static IEnumerator Disappear(VisualElement c)
        {
            float from = c.resolvedStyle.opacity;
            yield return Tween.Run(Timing.Instant, k => c.style.opacity = from * (1f - k));
            c.RemoveFromHierarchy();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Card bodies
        // ─────────────────────────────────────────────────────────────────────────

        private static Label Line(VisualElement parent, string text, string classes, Color? color = null)
        {
            var l = Ui.Text(text, classes, parent);
            l.style.whiteSpace = WhiteSpace.Normal;
            if (color.HasValue) l.style.color = color.Value;
            return l;
        }

        private static string Words(string camel) => Regex.Replace(camel ?? "", "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

        private static VisualElement Header(VisualElement body, Texture2D icon, string name, string sub, Color subColor)
        {
            var row = Ui.El("row", body);
            row.style.alignItems = Align.Center;
            if (icon != null)
            {
                var ic = Ui.El("menu-icon", row);
                ic.style.width = ic.style.height = Size.Avatar;
                ic.style.marginRight = Space.S16;
                ic.style.flexShrink = 0;
                ic.style.backgroundImage = new StyleBackground(icon);
            }
            var col = Ui.El("col grow", row);
            col.style.flexShrink = 1;
            Line(col, name, "t-h3 t-heavy");
            if (!string.IsNullOrEmpty(sub)) Line(col, sub, "t-caption", subColor);
            return col;
        }

        /// <summary>An ability, skill or battle item: kind, cost, cooldown, reach, numbers and its description.</summary>
        public static bool Ability(VisualElement body, AbilityDef a, string note = null)
        {
            if (a == null) return false;
            var itemTex = a.Id != null && a.Id.StartsWith("item:") ? Art.ItemIcon(a.Id.Substring(5)) : null;
            string kind = a.Kind == AbilityKind.Item ? "Item · acts at once"
                : a.Kind == AbilityKind.Passive ? "Passive · always on"
                : a.Kind == AbilityKind.Skill ? (a.CooldownTurns > 0 ? $"Skill · recharges in {a.CooldownTurns} turns" : "Skill · free")
                : a.CastTime > 0 ? $"Spell · charges {a.CastTime:0.#}s on the timeline" : "Spell · instant";
            var head = Header(body, itemTex ?? Art.AbilityIcon(a), a.Name, kind, Palette.Gold);
            if (a.Cost > 0)
            {
                var cost = Ui.El("row", head);
                cost.style.alignItems = Align.Center;
                cost.style.marginTop = Space.S8;
                Ui.CostPips(a.Cost, cost, 32);
                Line(cost, $"  {a.Cost} mana", "t-caption", Palette.Mana);
            }

            var facts = new List<string>();
            if (a.Mode == TargetMode.Auto) facts.Add("No aim needed");
            else
            {
                string reach = a.AdjacentOnly ? "an adjacent tile" : a.Radius > 0 ? $"{Words(a.Shape.ToString())}, radius {a.Radius}" : Words(a.Shape.ToString());
                facts.Add($"Hits {reach} · {Words(a.Filter.ToString())}");
            }
            if (a.BaseDamage > 0) facts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Palette.Element(a.DamageType))}>{a.BaseDamage} {a.DamageType} damage</color> (before stats)");
            if (a.BaseHeal > 0) facts.Add(a.Revives ? $"Revives a fallen ally with {a.BaseHeal} HP" : $"Heals {a.BaseHeal} (before stats)");
            if (!string.IsNullOrEmpty(a.ApplyBuff) && GameApp.I?.Content?.TryBuff(a.ApplyBuff) is BuffDef bd)
                facts.Add($"{(a.ApplyBuffChance < 1f ? $"{Mathf.RoundToInt(a.ApplyBuffChance * 100)}% to apply" : "Applies")} {TermOf(bd.Name)}: {Ui.BuffEffect(bd)}");
            if (a.Knockback > 0) facts.Add($"Knocks back {a.Knockback} tiles");
            if (a.Pull > 0) facts.Add($"Pulls {a.Pull} tiles toward the caster");
            if (a.SwapsPlaces) facts.Add("Swaps places with the target");
            if (a.StepBack > 0) facts.Add($"Caster steps back {a.StepBack}");
            if (a.HastenU > 0)
            {
                int pts = Math.Max(1, (int)Math.Round(a.HastenU / BattleConfig.HastenUPerTempo));
                int turns = a.TempoTurns > 0 ? a.TempoTurns : BattleConfig.DefaultTempoTurns;
                facts.Add($"On an ally: Tempo +{pts} for {turns} turns (every foe's icon slows; stacks). On a foe: hastens its icon");
            }
            if (a.Pushback > 0) facts.Add($"Pushback {a.Pushback:0.##}s: the foe acts later");
            if (a.RemovesDebuffs) facts.Add("Cleanses debuffs");
            if (a.GrantsOrbs > 0) facts.Add($"Grants {a.GrantsOrbs} orbs");
            var factBox = Ui.El("col", body);
            factBox.style.marginTop = Space.S16;
            foreach (var f in facts) GlossaryText.Create("• " + f, "t-caption", factBox, false);
            if (!string.IsNullOrEmpty(a.Description))
            {
                var d = GlossaryText.Create(a.Description, "t-body", body, false);
                d.style.marginTop = Space.S16;
            }
            if (!string.IsNullOrEmpty(note)) { var n = Line(body, note, "t-caption", Palette.TextDim); n.style.marginTop = Space.S8; }
            return true;
        }

        /// <summary>A buff or debuff: what it does, from its own numbers, and how long it has left.</summary>
        public static bool Buff(VisualElement body, BuffDef d, int remaining = -1)
        {
            if (d == null) return false;
            var row = Ui.El("row", body);
            row.style.alignItems = Align.Center;
            var col = Art.HexColor(d.Color, d.Kind == BuffKind.Buff ? Palette.Mana : new Color(0.8f, 0.4f, 0.9f));
            var pip = Ui.StatusPip(d.Id, string.IsNullOrEmpty(d.Letter) ? d.Name.Substring(0, 1) : d.Letter, col, 72, row);
            pip.style.marginRight = Space.S16;
            var c = Ui.El("col grow", row);
            Line(c, d.Name, "t-h3 t-heavy");
            Line(c, d.Kind == BuffKind.Buff ? "Buff" : "Debuff", "t-caption", d.Kind == BuffKind.Buff ? Palette.Good : Palette.Bad);
            var what = Line(body, char.ToUpper(Ui.BuffEffect(d)[0]) + Ui.BuffEffect(d).Substring(1), "t-body");
            what.style.marginTop = Space.S16;
            string unit = d.Unit == GridGame.Core.Content.DurationUnit.Turns ? "turns" : "s";
            Line(body, remaining >= 0 ? $"{remaining} {unit} left" : $"Lasts {d.Duration} {unit}", "t-caption", Palette.TextDim);
            return true;
        }

        /// <summary>A unit on the board: level, HP, what it's about to do (a charging foe's whole spell) and its statuses.
        /// <paramref name="knowledge"/> is the <see cref="BestiaryKnowledge"/> tier (§25.11, GG-T290): HP and stats from
        /// Stats, Speed alone from Speed, elements from Elements, its brain and abilities from Abilities.</summary>
        public static bool Unit(VisualElement body, BattleState s, Unit u, int knowledge)
        {
            bool reveal = knowledge >= BestiaryKnowledge.Stats;
            if (u == null) return false;
            var head = Header(body, null, $"{u.Name}  Lv {u.Level}", u.IsHero ? u.Def.Role : u.IsNeutral ? "Neutral" : "Enemy",
                u.IsHero ? Palette.Hero : u.IsNeutral ? Palette.Neutral : Palette.Enemy);
            Line(head, reveal ? $"HP {u.Hp}/{u.MaxHp}" : "HP ??? — defeat more to learn it", "t-caption");
            if (reveal) Line(head, Ui.StatLine(u.Effective), "t-caption", Palette.TextDim);
            else if (knowledge >= BestiaryKnowledge.Speed) Line(head, $"SPD {u.Effective.Speed:0}", "t-caption", Palette.TextDim);
            if (!u.IsHero && knowledge >= BestiaryKnowledge.Elements) Line(head, BestiaryKnowledge.ElementLine(u.Def), "t-caption", Palette.TextDim);
            if (!u.IsHero && !u.IsNeutral)
            {
                var cast = s.CastEntryOf(u.Id);
                int n = BattleEngine.ActionsUntilTurn(s, u.Id);
                var when = Line(body, cast != null ? "Charging a spell!" : n < 0 ? "Frozen in time" : n == 0 ? "Acts after your next move" : n == 1 ? "Acts in 1 move" : $"Acts in {n} moves", "t-body", Palette.Warn);
                when.style.marginTop = Space.S16;
                if (cast != null && GameApp.I?.Content?.TryAbility(cast.AbilityId) is AbilityDef ca)
                {
                    var box = Ui.El("col", body);
                    box.style.marginTop = Space.S8;
                    box.style.paddingLeft = Space.S16;
                    box.style.borderLeftWidth = 4;
                    box.style.borderLeftColor = Palette.Warn;
                    Ability(box, ca);
                }
                if (knowledge >= BestiaryKnowledge.Abilities)
                {
                    var mind = Line(body, $"{EnemyPlanner.DescribeBrain(u.Def.Brain)} · {EnemyPlanner.Describe(EnemyPlanner.TargetingOf(u)).ToLowerInvariant()}", "t-caption", Palette.TextDim);
                    mind.style.marginTop = Space.S8;
                }
                if (u.HasPlate) Line(body, $"Armor plate {u.PlateHp}/{u.PlateMax} — strike from the sides, or break it head-on", "t-caption", Palette.Info);
            }
            foreach (var b in u.Buffs)
            {
                var r = Ui.El("row", body);
                r.style.marginTop = Space.S8;
                var col = Art.HexColor(b.Def.Color, b.Def.Kind == BuffKind.Buff ? Palette.Mana : new Color(0.8f, 0.4f, 0.9f));
                var pip = Ui.StatusPip(b.Def.Id, string.IsNullOrEmpty(b.Def.Letter) ? b.Def.Name.Substring(0, 1) : b.Def.Letter, col, 40, r);
                pip.style.marginRight = Space.S8;
                var t = GlossaryText.Create($"{TermOf(b.Def.Name)} — {Ui.BuffEffect(b.Def)} ({b.Remaining} left)", "t-caption", r, false);
                t.style.flexShrink = 1;
            }
            return true;
        }

        /// <summary>"[name]" when the glossary knows the name (a status), so it reads as a term; else the bare name.</summary>
        private static string TermOf(string name) => GameApp.I?.Content?.TryTerm(name) != null ? $"[{name}]" : name;

        /// <summary>A tapped glossary term (GG-T300): the same card, opened by a tap, closed by the next press anywhere.</summary>
        public static void ShowTerm(VisualElement anchor, GlossaryEntry e)
        {
            if (e == null || anchor == null) return;
            if (closedFor == anchor && Time.realtimeSinceStartup - closedAt < HoldSeconds + 0.1f) { closedFor = null; return; }
            Show(anchor, b => Term(b, e), true);
        }

        /// <summary>The term card: headword, what kind of word it is, its definition and, for a link, the live numbers.</summary>
        public static bool Term(VisualElement body, GlossaryEntry e)
        {
            if (e == null) return false;
            var db = GameApp.I?.Content;
            var buff = e.LinkKind == GlossaryLinkKind.Buff ? db?.TryBuff(e.LinkId) : null;
            string kind = buff != null ? (buff.Kind == BuffKind.Buff ? "Buff" : "Debuff")
                : e.LinkKind == GlossaryLinkKind.Stat ? "Stat"
                : e.LinkKind == GlossaryLinkKind.Ability ? "Ability"
                : Array.Exists(Glossary.ChanceBands, c => string.Equals(c.Word, e.Id, StringComparison.OrdinalIgnoreCase)) ? "Chance"
                : "Keyword";
            var head = Header(body, null, e.Term, kind, buff == null ? Palette.Term : buff.Kind == BuffKind.Buff ? Palette.Good : Palette.Bad);
            head.parent.style.marginBottom = Space.S8;
            GlossaryText.Create(e.Text, "t-body", body, false);
            if (buff != null)
            {
                string unit = buff.Unit == GridGame.Core.Content.DurationUnit.Turns ? "turns" : "s";
                var n = Line(body, $"{char.ToUpper(Ui.BuffEffect(buff)[0])}{Ui.BuffEffect(buff).Substring(1)} · lasts {buff.Duration} {unit}", "t-caption", Palette.TextDim);
                n.style.marginTop = Space.S8;
            }
            else if (e.LinkKind == GlossaryLinkKind.Ability && db?.TryAbility(e.LinkId) is AbilityDef la)
            {
                var n = Line(body, $"{la.Name}{(la.CastTime > 0 ? $" · charges {la.CastTime:0.#}s" : "")}", "t-caption", Palette.TextDim);
                n.style.marginTop = Space.S8;
            }
            return true;
        }

        /// <summary>Equipment or any inventory item: rarity, slot and what it says about itself.</summary>
        public static bool Item(VisualElement body, ItemDef it)
        {
            if (it == null) return false;
            var head = Header(body, Art.ItemIcon(it.Id), it.Name, $"{it.Rarity} {Words(it.Type.ToString())}", Palette.Gold);
            head.Q<Label>()?.AddToClassList(Ui.RarityClass(it.Rarity));
            if (!string.IsNullOrEmpty(it.Description)) { var d = GlossaryText.Create(it.Description, "t-body", body, false); d.style.marginTop = Space.S16; }
            return true;
        }
    }
}
