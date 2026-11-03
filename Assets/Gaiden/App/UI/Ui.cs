using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The widget kit. Every screen composes these; nothing positions itself with magic pixel offsets
    /// except the battle board. All styling is in Theme.uss classes.
    /// </summary>
    public static class Ui
    {
        public static VisualElement El(string classes = null, VisualElement parent = null)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        public static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        }

        public static Label Text(string text, string classes = null, VisualElement parent = null)
        {
            var l = new Label(text) { enableRichText = true };
            l.pickingMode = PickingMode.Ignore;
            AddClasses(l, classes);
            parent?.Add(l);
            return l;
        }

        public static VisualElement Window(string title = null, VisualElement parent = null, string classes = null)
        {
            var w = El("window " + (classes ?? ""), parent);
            w.style.backgroundImage = new StyleBackground(Art.Window);
            TintWindow(w);
            if (title != null) Text(title, "window-title", w);
            return w;
        }

        /// <summary>A gradient-only theme tints the window frame inline with its derived accent; a theme with a USS
        /// class (Art.ThemeUssClass) owns the frame in Theme.uss, so the inline colors are cleared for it.</summary>
        private static void TintWindow(VisualElement w)
        {
            StyleColor c = Art.ThemeUssClass == null ? new StyleColor(Art.ThemeAccent) : new StyleColor(StyleKeyword.Null);
            w.style.borderTopColor = w.style.borderBottomColor = w.style.borderLeftColor = w.style.borderRightColor = c;
        }

        /// <summary>
        /// Applies the active menu theme (Art.ApplyTheme) live to everything already on screen (GG-T185): puts the
        /// theme's root class on <paramref name="root"/> (removing every other theme's) so Theme.uss's scoped
        /// <c>.theme-* .window/.tab/.btn</c> rules take over, and re-tints every open window's frame. The window
        /// gradient texture is repainted in place by Art.ApplyTheme itself.
        /// </summary>
        public static void ApplyMenuTheme(VisualElement root)
        {
            if (root == null) return;
            foreach (var t in Art.Themes)
                if (t.UssClass != null) root.EnableInClassList(t.UssClass, t.UssClass == Art.ThemeUssClass);
            root.Query(className: "window").ForEach(TintWindow);
        }

        public static Button Button(string text, Action onClick, string classes = null, VisualElement parent = null)
        {
            var b = new Button(() =>
            {
                GameApp.I?.Audio?.Sfx("ui.click");
                onClick?.Invoke();
            }) { text = text };
            b.AddToClassList("btn");
            AddClasses(b, classes);
            Pressable(b);
            parent?.Add(b);
            return b;
        }

        /// <summary>
        /// The one press feel every tappable thing shares (owner direction 2026-10-11): on touch it sinks — drops a few
        /// pixels onto its lip, shrinks, darkens — with a soft press tick; on release it springs back. Disabled things
        /// don't sink. Styling lives in Theme.uss (.pressable / .is-pressed); this only toggles the state.
        /// </summary>
        public static T Pressable<T>(T e, bool sound = true) where T : VisualElement
        {
            if (e.ClassListContains("pressable")) return e;
            e.AddToClassList("pressable");
            void Release()
            {
                if (!e.ClassListContains("is-pressed")) return;
                e.RemoveFromClassList("is-pressed");
                e.AddToClassList("is-released");
                e.schedule.Execute(() => e.RemoveFromClassList("is-released")).StartingIn(140);
            }
            e.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (!e.enabledInHierarchy || e.ClassListContains("btn--disabled") || e.ClassListContains("menu-row--disabled")) return;
                e.RemoveFromClassList("is-released");
                e.AddToClassList("is-pressed");
                if (sound) GameApp.I?.Audio?.Sfx("ui.press", 0.55f);
            }, TrickleDown.TrickleDown);
            e.RegisterCallback<PointerUpEvent>(_ => Release(), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerLeaveEvent>(_ => Release());
            e.RegisterCallback<PointerCancelEvent>(_ => Release());
            e.RegisterCallback<PointerCaptureOutEvent>(_ => Release());
            return e;
        }

        /// <summary>A tappable card/cell (party card, bestiary thumb, bar cell): the shared press feel plus a click handler.</summary>
        public static T Tappable<T>(T e, Action onClick, string sfx = "ui.click") where T : VisualElement
        {
            Pressable(e);
            e.RegisterCallback<ClickEvent>(_ =>
            {
                try
                {
                    if (sfx != null) GameApp.I?.Audio?.Sfx(sfx);
                    onClick?.Invoke();
                }
                catch (Exception ex) { Debug.LogException(ex); }
            });
            return e;
        }

        public static void SetEnabled(Button b, bool enabled)
        {
            b.SetEnabled(enabled);
            b.EnableInClassList("btn--disabled", !enabled);
        }

        /// <summary>Top bar: back button, title, live gold.</summary>
        public static VisualElement Header(string title, Action onBack, VisualElement parent, out Label gold)
        {
            var h = El("header", parent);
            if (onBack != null) Button("‹", onBack, "btn--small btn--icon", h).name = "back";
            Text(title, "header-title shadow", h);
            gold = Text("", "header-gold shadow", h);
            return h;
        }

        public static string Gold(int g) => $"{g:N0} G";

        /// <summary>FF-style menu row: cursor hand, optional icon, label, right-aligned value.</summary>
        public static VisualElement MenuRow(string label, string value, Action onClick, VisualElement parent, Texture2D icon = null, bool enabled = true)
        {
            var row = El("menu-row", parent);
            var cur = El("menu-cursor", row);
            cur.style.backgroundImage = new StyleBackground(Art.Cursor);
            if (icon != null)
            {
                var ic = El("menu-icon", row);
                ic.style.backgroundImage = new StyleBackground(icon);
            }
            var l = Text(label, "menu-label", row);
            l.name = "label";
            var v = Text(value ?? "", "menu-value", row);
            v.name = "value";
            row.EnableInClassList("menu-row--disabled", !enabled);
            Pressable(row);
            row.RegisterCallback<PointerEnterEvent>(_ => Select(row));
            row.RegisterCallback<ClickEvent>(_ =>
            {
                try
                {
                    Select(row);
                    if (!enabled) { GameApp.I?.Audio?.Sfx("ui.error"); return; }
                    GameApp.I?.Audio?.Sfx("ui.select");
                    onClick?.Invoke();
                }
                catch (Exception e) { Debug.LogException(e); }
            });
            return row;
        }

        public static void Select(VisualElement row)
        {
            var p = row.parent;
            if (p != null) foreach (var sib in p.Children()) sib.RemoveFromClassList("menu-row--selected");
            row.AddToClassList("menu-row--selected");
        }

        /// <summary>Put the cursor on the first enabled row of a menu container.</summary>
        public static void SelectFirst(VisualElement menu)
        {
            foreach (var c in menu.Children())
                if (c.ClassListContains("menu-row") && !c.ClassListContains("menu-row--disabled")) { Select(c); return; }
        }

        public static ScrollView List(VisualElement parent)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.AddToClassList("list");
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            parent?.Add(sv);
            // The window holding a list takes only the space left over and scrolls; it is the one that gives way
            // when a screen runs long, never the tabs or footer around it.
            if (parent != null && parent.ClassListContains("window")) parent.AddToClassList("window--scroller");
            return sv;
        }

        public static VisualElement Tabs(string[] names, int active, Action<int> onPick, VisualElement parent)
        {
            var bar = El("tabs", parent);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var t = Text(names[i], "tab" + (i == active ? " tab--active" : ""), bar);
                t.name = "tab-" + names[i].ToLowerInvariant().Replace(' ', '-');
                t.pickingMode = PickingMode.Position;
                Tappable(t, () => onPick(idx));
            }
            return bar;
        }

        public static VisualElement Gauge(float frac, VisualElement parent, float width = -1)
        {
            var g = El("gauge", parent);
            if (width > 0) g.style.width = width;
            var f = El("gauge-fill", g);
            f.name = "fill";
            SetGauge(g, frac);
            return g;
        }

        public static void SetGauge(VisualElement gauge, float frac)
        {
            var f = gauge.Q("fill");
            frac = Mathf.Clamp01(frac);
            f.style.width = Length.Percent(frac * 100f);
            f.EnableInClassList("gauge-fill--mid", frac <= 0.5f && frac > 0.25f);
            f.EnableInClassList("gauge-fill--low", frac <= 0.25f);
        }

        /// <summary>A framed card thumbnail: the class's real portrait seen through its Thumbnail window (<see cref="PortraitThumb"/>).
        /// Tint the art (e.g. the bestiary's unseen silhouette) on <c>t.Q(ThumbArt)</c>, not on the frame.</summary>
        public static VisualElement Thumb(string classId, VisualElement parent, float size = 150)
        {
            var t = El("thumb", parent);
            t.style.width = size;
            t.style.height = size;
            PortraitThumb(t, classId);
            return t;
        }

        /// <summary>Name of the portrait child <see cref="PortraitThumb"/> puts in a host (tint/opacity it, not the host).</summary>
        public const string ThumbArt = "thumb-art";

        /// <summary>
        /// Fills a square host with the class's real portrait, zoomed and offset by its <see cref="Thumbnail"/>
        /// (BIBLE §12.1.1, GG-T221): no thumbnail images exist, only the portrait already in memory. The host
        /// clips (overflow hidden); inside it an inset-0 clip box holds the portrait child, sized and placed in
        /// percent of that box (width = Zoom×100%, height = Zoom×aspect×100%, left = 50% − X·width,
        /// top = 50% − Y·height), so the framing is right at any size with no layout pass. A class with no
        /// Thumbnail (or an id that isn't a class) shows the whole portrait contained. Returns the portrait
        /// child (named <see cref="ThumbArt"/>), which callers tint.
        /// </summary>
        public static VisualElement PortraitThumb(VisualElement host, string classId)
        {
            host.style.overflow = Overflow.Hidden;
            var clip = host.Q("thumb-clip");
            if (clip == null)
            {
                clip = new VisualElement { name = "thumb-clip", pickingMode = PickingMode.Ignore };
                clip.style.position = Position.Absolute;
                clip.style.left = clip.style.top = clip.style.right = clip.style.bottom = 0;
                clip.style.overflow = Overflow.Hidden;
                host.Insert(0, clip);
            }
            var art = clip.Q(ThumbArt);
            if (art == null)
            {
                art = new VisualElement { name = ThumbArt, pickingMode = PickingMode.Ignore };
                art.style.position = Position.Absolute;
                clip.Add(art);
            }
            var tex = string.IsNullOrEmpty(classId) ? null : Art.Portrait(classId);
            art.style.backgroundImage = new StyleBackground(tex);
            var th = GameApp.I?.Content?.TryClass(classId)?.Thumbnail;
            if (th == null || tex == null || tex.width <= 0)
            {
                art.style.left = art.style.top = 0;
                art.style.width = art.style.height = Length.Percent(100);
                art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                return art;
            }
            float w = th.Zoom * 100f, h = w * tex.height / tex.width;
            art.style.width = Length.Percent(w);
            art.style.height = Length.Percent(h);
            art.style.left = Length.Percent(50f - th.X * w);
            art.style.top = Length.Percent(50f - th.Y * h);
            art.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            return art;
        }

        /// <summary>Mana cost as orb pips: 3 → three orbs.</summary>
        public static VisualElement CostPips(int cost, VisualElement parent, float size = 34)
        {
            var row = El("row", parent);
            row.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < cost; i++)
            {
                var p = Text("", "pip pip--mana", row);
                p.style.width = size; p.style.height = size; p.style.borderTopLeftRadius = p.style.borderTopRightRadius = p.style.borderBottomLeftRadius = p.style.borderBottomRightRadius = size / 2;
                p.style.fontSize = size * 0.6f;
            }
            return row;
        }

        public static string RarityClass(ItemRarity r) => "rarity-" + r;

        /// <summary>Class blurbs mark their keywords as [word]; show them as highlighted words instead of raw brackets.</summary>
        public static string Keywords(string text) =>
            string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]", "<color=#9CC8FF>$1</color>");

        public static Color RoleColor(string role)
        {
            switch (role)
            {
                case "Tank": return new Color(0.95f, 0.36f, 0.3f);
                case "Healer": return new Color(0.4f, 0.9f, 0.5f);
                case "Mage": return new Color(0.55f, 0.6f, 1f);
                case "Striker": return new Color(1f, 0.66f, 0.25f);
                case "Support": return new Color(0.35f, 0.85f, 0.85f);
                case "Ranged": return new Color(0.68f, 0.85f, 0.35f);
                default: return new Color(0.8f, 0.8f, 0.85f);
            }
        }

        /// <summary>What each role brings to the party (§25.13), one line.</summary>
        public static string RoleBlurb(string role)
        {
            switch (role)
            {
                case "Tank": return "Taunts enemies onto itself and takes the hits, so casters can finish their spells.";
                case "Healer": return "Mends and cleanses. Heals grow with WIS.";
                case "Mage": return "Ranged elemental spells. Damage grows with INT.";
                case "Striker": return "Pincer damage, Cleave and War Cry. Grows with STR.";
                case "Support": return "Bends the timeline: Quicken allies, Slow foes, rally the party.";
                case "Ranged": return "Precision shots from distance — never needs to be adjacent to strike.";
                default: return "";
            }
        }

        /// <summary>A small coloured pill naming a hero's role.</summary>
        public static Label RoleBadge(string role, VisualElement parent, float size = 26)
        {
            var c = RoleColor(role);
            var l = Text((role ?? "").ToUpperInvariant(), "role-badge", parent);
            l.style.fontSize = size;
            l.style.color = c;
            l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = c;
            return l;
        }

        public static string StatLine(Stats s) =>
            $"STR {s.Strength:0}  VIT {s.Vitality:0}  AGI {s.Agility:0}  SPD {s.Speed:0}\nINT {s.Intelligence:0}  WIS {s.Wisdom:0}  LCK {s.Luck:0}";

        public static string Delta(float now, float then)
        {
            float d = then - now;
            if (Mathf.Abs(d) < 0.5f) return $"{then:0}";
            return d > 0 ? $"{then:0} <color=#78EC96>▲{d:0}</color>" : $"{then:0} <color=#FF6E6E>▼{-d:0}</color>";
        }

        /// <summary>What a buff does, in words, built from its mechanics so it never drifts from the numbers.</summary>
        public static string BuffEffect(BuffDef d)
        {
            var parts = new List<string>();
            if (d.DamagePerTick > 0) parts.Add($"{d.DamagePerTick} damage each tick");
            if (d.HealPerTick > 0) parts.Add($"heals {d.HealPerTick} each tick");
            if (d.Immobile) parts.Add("can't move or act");
            if (d.HaltsTimeline) parts.Add("timeline halted");
            if (d.BlocksSpells) parts.Add("can't cast spells");
            if (d.IncomingDamageMultiplier < 1f) parts.Add($"takes {Mathf.RoundToInt((1f - d.IncomingDamageMultiplier) * 100)}% less damage");
            if (d.IncomingDamageMultiplier > 1f) parts.Add($"takes {Mathf.RoundToInt((d.IncomingDamageMultiplier - 1f) * 100)}% more damage");
            if (d.TimelineSpeedMultiplier < 1f) parts.Add("acts slower");
            if (d.TimelineSpeedMultiplier > 1f) parts.Add("acts faster");
            if (d.AccuracyMultiplier < 1f) parts.Add("attacks miss more");
            if (d.BreaksOnDamage) parts.Add("ends when hit");
            if (d.BreaksOnMove) parts.Add("ends when moved");
            return parts.Count == 0 ? (d.Kind == BuffKind.Buff ? "beneficial" : "harmful") : string.Join(", ", parts);
        }

        /// <summary>An enemy's kit in a few words (§14.4) — shown on its card and in the bestiary.</summary>
        public static string KitTraits(ContentDb c, ClassDef d)
        {
            var t = new List<string>();
            if (d.MoveRange > 1) t.Add($"moves {d.MoveRange} tiles");
            if (d.AttackRange > 1) t.Add($"shoots {d.AttackRange} tiles");
            if (!string.IsNullOrEmpty(d.ChargeAbility)) t.Add("casts " + (c.TryAbility(d.ChargeAbility)?.Name ?? d.ChargeAbility));
            if (!string.IsNullOrEmpty(d.Trap) && c.Traps.TryGetValue(d.Trap, out var tr)) t.Add("lays " + tr.Name);
            if (!string.IsNullOrEmpty(d.Trail) && c.Traps.TryGetValue(d.Trail, out var tl)) t.Add("leaves a " + tl.Name);
            if (!string.IsNullOrEmpty(d.OnHitBuff)) t.Add($"hits may inflict {c.TryBuff(d.OnHitBuff)?.Name ?? d.OnHitBuff} ({Mathf.RoundToInt(d.OnHitChance * 100)}%)");
            if (d.DeathBurst > 0) t.Add($"explodes when killed ({d.DeathBurst})");
            foreach (var id in d.Abilities)
                if (c.TryAbility(id) is AbilityDef a && a.Kind == AbilityKind.Passive && !string.IsNullOrEmpty(a.Name)) t.Add(a.Name.ToLowerInvariant());
            if (d.ImmuneBuffs != null && d.ImmuneBuffs.Count > 0) t.Add("immune to " + string.Join("/", d.ImmuneBuffs));
            return t.Count == 0 ? "" : char.ToUpper(t[0][0]) + string.Join(" · ", t).Substring(1);
        }

        /// <summary>
        /// A status pip: the pixel-art icon (flame, snowflake, poison bottle …) on a dark disc ringed in the status colour.
        /// Falls back to the letter only when an effect has no icon yet.
        /// </summary>
        public static VisualElement StatusPip(string iconId, string letter, Color color, float size, VisualElement parent = null)
        {
            var pip = new VisualElement { pickingMode = PickingMode.Ignore, viewDataKey = iconId };
            pip.style.width = pip.style.height = size;
            pip.style.flexShrink = 0;
            pip.style.borderTopLeftRadius = pip.style.borderTopRightRadius = pip.style.borderBottomLeftRadius = pip.style.borderBottomRightRadius = size / 2f;
            pip.style.borderTopWidth = pip.style.borderBottomWidth = pip.style.borderLeftWidth = pip.style.borderRightWidth = 3;
            pip.style.borderTopColor = pip.style.borderBottomColor = pip.style.borderLeftColor = pip.style.borderRightColor = color;
            pip.style.backgroundColor = new Color(0.05f, 0.06f, 0.14f, 0.95f);
            var tex = Art.StatusIcon(iconId);
            if (tex != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.style.position = Position.Absolute;
                img.style.left = img.style.top = img.style.right = img.style.bottom = 1;
                img.style.backgroundImage = new StyleBackground(tex);
                img.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                pip.Add(img);
            }
            else
            {
                var l = new Label(letter) { pickingMode = PickingMode.Ignore };
                l.style.flexGrow = 1; l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.fontSize = size * 0.55f; l.style.color = color; l.style.unityFontStyleAndWeight = FontStyle.Bold;
                pip.Add(l);
            }
            parent?.Add(pip);
            return pip;
        }
    }
}
