using System.Linq;
using System.Collections.Generic;
using GridGame.Core.Content;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// Every texture the game draws: imported art by key (Resources) plus procedurally generated UI
    /// pieces (window gradient, cursor, orbs). Missing art never throws — it falls back to a
    /// generated placeholder and logs once, so a gap is visible instead of fatal.
    /// </summary>
    public static class Art
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> warned = new HashSet<string>();

        public static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return Placeholder;
            if (cache.TryGetValue(path, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(path);
            if (t == null)
            {
                if (warned.Add(path)) Debug.LogWarning($"[Art] missing texture '{path}'");
                t = Placeholder;
            }
            cache[path] = t;
            return t;
        }

        public static bool Exists(string path) => Resources.Load<Texture2D>(path) != null;

        /// <summary>Textures held by path (the `-leakprobe` line, GG-T375): bounded by the art on disk, never per battle.</summary>
        internal static int CachedTextures => cache.Count;

        private static readonly Dictionary<string, int> spellIndex = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Called once content loads: every ability gets an ordinal, so every spell gets its own colour.</summary>
        public static void IndexSpells(IEnumerable<string> ids)
        {
            spellIndex.Clear();
            foreach (var id in ids.OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase)) spellIndex[id] = spellIndex.Count;
        }

        /// <summary>Each spell's own colour (golden-ratio hue walk over the ability list) — its countdown ring and timeline frame.</summary>
        public static Color SpellColor(string abilityId)
        {
            int i = abilityId != null && spellIndex.TryGetValue(abilityId, out var k) ? k : 0;
            float hue = (0.03f + i * 0.61803398f) % 1f;
            return Color.HSVToRGB(hue, 0.72f, 1f);
        }

        /// <summary>A unit's one piece of art (full portrait). Every thumbnail is this texture framed by the class's
        /// Thumbnail (Ui.PortraitThumb, BIBLE §12.1.1); there are no separate thumb images.</summary>
        /// A class with no portrait yet shows the mannequin placeholder art (owner 2026-11-07, GG-T351).
        public static Texture2D Portrait(string classId)
        {
            string path = "Portraits/" + classId;
            if (cache.TryGetValue(path, out var t) && t != null) return t;
            t = string.IsNullOrEmpty(classId) ? null : Resources.Load<Texture2D>(path);
            if (t == null)
            {
                if (warned.Add(path)) Debug.LogWarning($"[Art] missing portrait '{path}', using the mannequin");
                t = Load("Portraits/" + MannequinPortrait);
            }
            return cache[path] = t;
        }

        /// <summary>The placeholder portrait every class without art falls back to.</summary>
        public const string MannequinPortrait = "mannequin";

        // ── The figure on a portrait (GG-T352) ─────────────────────────────────────
        // Owner 2026-11-07: "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the
        // assets are." Thumbnails frame that whole canvas (Ui.PortraitThumb). Whole-figure views draw only the figure's
        // box on it (ClassDef.Figure, the alpha bounds tools/import_art.py writes), so a figure reads at the same size it
        // always did instead of shrinking into its canvas margins.

        /// <summary>The size whole-figure views measure a figure by: its box fitted inside this square, never enlarged
        /// (the size portraits were imported at before GG-T352, so a creature smaller than a person still draws smaller).</summary>
        public const float FigureBox = 640f;

        private static readonly Dictionary<Texture2D, Sprite> figureSprites = new Dictionary<Texture2D, Sprite>();

        /// <summary>The figure's box on a portrait texture, in texture pixels from the bottom-left (Unity's origin). It is
        /// the box of the class the texture belongs to, so the mannequin fallback (GG-T351) uses the mannequin's own box;
        /// a texture with no valid box is used whole.</summary>
        private static Rect FigureRect(Texture2D tex)
        {
            var f = GameApp.I != null ? GameApp.I.Content?.TryClass(tex.name)?.Figure : null;
            if (f == null || !f.IsValid) return new Rect(0, 0, tex.width, tex.height);
            float x0 = Mathf.Round(f.X * tex.width), x1 = Mathf.Min(tex.width, Mathf.Round((f.X + f.W) * tex.width));
            float y0 = Mathf.Round(f.Y * tex.height), y1 = Mathf.Min(tex.height, Mathf.Round((f.Y + f.H) * tex.height));
            if (x1 - x0 < 1f || y1 - y0 < 1f) return new Rect(0, 0, tex.width, tex.height);
            return new Rect(x0, tex.height - y1, x1 - x0, y1 - y0);
        }

        /// <summary>A class's figure (its portrait cut to <see cref="ClassDef.Figure"/>) as a sprite for a
        /// <c>backgroundImage</c>: every whole-portrait view (dialogue, cut-ins, boss intro, level-up, cards, the death
        /// dissolve) draws this instead of the raw texture. Cached per texture; null only when there is no art at all.</summary>
        public static Sprite PortraitFigure(string classId)
        {
            var tex = Portrait(classId);
            if (tex == null) return null;
            if (figureSprites.TryGetValue(tex, out var s) && s != null) return s;
            s = Sprite.Create(tex, FigureRect(tex), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = tex.name + ".figure";
            return figureSprites[tex] = s;
        }

        /// <summary>The figure's layout size in <see cref="FigureBox"/> units: its box fitted inside a 640 square, never
        /// enlarged. Width ÷ height is the figure's aspect; dialogue scales the height (640 = one person).</summary>
        public static Vector2 FigureSize(string classId)
        {
            var tex = Portrait(classId);
            if (tex == null) return new Vector2(FigureBox * 0.7f, FigureBox);
            var r = FigureRect(tex);
            // Measured on the 1024 canvas the box was found on, whatever size the texture was imported at.
            float px = 1024f / Mathf.Max(1, tex.width), w = r.width * px, h = r.height * px;
            float k = Mathf.Min(1f, FigureBox / Mathf.Max(w, h));
            return new Vector2(w * k, h * k);
        }
        /// <summary>The board tile for a campaign theme (tools/make_tiles.py); the original slate tile when a theme has none.</summary>
        private static Texture2D Pixelled(string folder, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string path = folder + "/" + id;
            if (cache.TryGetValue(path, out var t)) return t;
            return cache[path] = Resources.Load<Texture2D>(path);
        }

        /// <summary>An ability's pixel icon (AbilityIcons/&lt;id&gt;), else its legacy icon, else null.</summary>
        public static Texture2D AbilityIcon(AbilityDef a) => a == null ? null : Pixelled("AbilityIcons", a.Id) ?? (string.IsNullOrEmpty(a.Icon) ? null : Icon(a.Icon));

        /// <summary>An item's pixel icon (ItemIcons/&lt;id&gt;), or null.</summary>
        public static Texture2D ItemIcon(string itemId) => Pixelled("ItemIcons", itemId);

        /// <summary>Pixel-art status icon (tools/make_status_icons.py) for a buff id or "stunned"; null when none exists.</summary>
        public static Texture2D StatusIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string path = "StatusIcons/" + id;
            if (cache.TryGetValue(path, out var t)) return t;
            return cache[path] = Resources.Load<Texture2D>(path);
        }

        /// <summary>A theme's board tile: <c>Tiles/&lt;theme&gt;</c>, else its base theme's (a postgame theme with its own
        /// stages, like <c>greenvalley2</c>, keeps the moors' tile, GG-T346), else the plain tile.</summary>
        public static Texture2D BoardTile(string theme)
        {
            foreach (var t in new[] { theme, Backdrops.BaseTheme(theme) })
                if (!string.IsNullOrEmpty(t) && Exists("Tiles/" + t)) return Load("Tiles/" + t);
            return Load("Tiles/tile");
        }
        /// <summary>A UI-group pixel icon (tools/icons.json "ui": cursor, pickups), or null.</summary>
        public static Texture2D UiIcon(string id) => Pixelled("UI", id);
        public static Texture2D Icon(string key) => string.IsNullOrEmpty(key) ? null : Load("Icons/" + key);

        private static readonly Dictionary<string, BackdropSet> backdropSets = new Dictionary<string, BackdropSet>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>A background set as found in Resources (GG-T344: counted once, from <c>_01</c>, or the legacy <c>_0</c>).
        /// GG-T353: each probed image is unloaded again (a full-size backdrop is ~4.5 MB of texture), so counting a set
        /// doesn't keep all of it resident; the one shown is loaded by <see cref="Background"/>.</summary>
        public static BackdropSet BackgroundSet(string set)
        {
            if (string.IsNullOrEmpty(set)) return new BackdropSet(set, 0, true);
            if (!backdropSets.TryGetValue(set, out var s))
                backdropSets[set] = s = Backdrops.Probe(set, ProbeBackground);
            return s;
        }

        private static bool ProbeBackground(string key)
        {
            var t = Resources.Load<Texture2D>(key);
            if (t == null) return false;
            if (!cache.ContainsKey(key)) Resources.UnloadAsset(t);
            return true;
        }

        /// <summary>A theme's <paramref name="index"/>th backdrop from its campaign <c>Backgrounds</c> set (else the theme id),
        /// wrapped over the set's own count; a postgame theme with no art of its own borrows its base theme's
        /// (<see cref="Backdrops.Resolve"/>).</summary>
        public static Texture2D Background(string theme, int index = 0)
        {
            var content = GameApp.I != null ? GameApp.I.Content : null;
            var t = Resources.Load<Texture2D>(Backdrops.Resolve(theme, index, BackgroundSet, x => Backdrops.SetOf(content, x)));
            return t != null ? t : Load(Backdrops.Fallback);
        }

        // ── Generated pieces ────────────────────────────────────────────────────────

        private static Texture2D placeholder, window, windowDark, cursor, glow, orb;

        public static Texture2D Placeholder => placeholder ??= Solid(new Color(0.35f, 0.2f, 0.45f), "placeholder");

        /// <summary>The Final Fantasy window body: a deep vertical gradient from the active menu theme (border
        /// tint also follows the theme — see <see cref="ThemeAccent"/>, applied in <c>Ui.Window</c>).</summary>
        public static Texture2D Window => window ??= Gradient(CurrentTheme.ColorA, CurrentTheme.ColorB, "window");
        public static Texture2D WindowDark => windowDark ??= Gradient(Darken(CurrentTheme.ColorA, 0.3f), Darken(CurrentTheme.ColorB, 0.55f), "window-dark");

        // ── Menu themes (owner request 2026-10-11: "the gradient on all the menus is generated... make
        // multiple Themes... updated in Settings and remembers it") ──────────────────────────────────

        /// <summary>A theme is two chosen base colors; the accent (window border, highlights) is derived from
        /// them by color theory, never hand-picked, so every theme — including future ones — looks coherent
        /// with zero extra tuning: hue-average the two bases (shortest arc around the wheel), then take its
        /// complement (+180°) for a third hue guaranteed to stand apart from both, pushed toward full
        /// saturation/brightness since it's used for thin borders and highlights, not large fills.
        /// A theme may also name a <see cref="UssClass"/> (GG-T185): <c>Ui.ApplyMenuTheme</c> puts it on the UI root, and
        /// Theme.uss scopes that theme's window frame, tabs and buttons under it (<c>.theme-slate .tab { … }</c>), so the
        /// whole look switches live with no screen rebuild. A theme with a class owns its window frame in USS (no inline
        /// accent border). Adding a theme = one row in <see cref="Themes"/> plus, if it restyles more than the gradient,
        /// one <c>.theme-&lt;name&gt;</c> block at the end of Theme.uss.</summary>
        public readonly struct MenuTheme
        {
            public readonly string Name;
            public readonly Color32 ColorA, ColorB;
            public readonly Color32 Accent;
            /// <summary>Root USS class that scopes this theme's stylesheet overrides; null for gradient-only themes.</summary>
            public readonly string UssClass;

            public MenuTheme(string name, Color32 a, Color32 b, string ussClass = null)
            {
                Name = name; ColorA = a; ColorB = b; UssClass = ussClass;
                Color.RGBToHSV(a, out var ha, out var sa, out var va);
                Color.RGBToHSV(b, out var hb, out var sb, out var vb);
                float arc = Mathf.DeltaAngle(ha * 360f, hb * 360f) / 360f; // shortest signed hue distance a→b
                float mid = Mathf.Repeat(ha + arc * 0.5f, 1f);             // circular midpoint hue
                float accentHue = Mathf.Repeat(mid + 0.5f, 1f);            // its complement
                float sat = Mathf.Clamp(Mathf.Max(sa, sb), 0.6f, 0.95f);
                float val = Mathf.Clamp(Mathf.Max(va, vb) * 1.05f, 0.8f, 1f);
                Accent = Color.HSVToRGB(accentHue, sat, val);
            }
        }

        public static readonly MenuTheme[] Themes =
        {
            new MenuTheme("Classic", new Color32(44, 70, 170, 255), new Color32(10, 18, 70, 255)),
            new MenuTheme("Cyberpunk", new Color32(255, 0, 200, 255), new Color32(0, 220, 255, 255)),
            new MenuTheme("Ember", new Color32(239, 83, 80, 255), new Color32(38, 166, 154, 255)),
            new MenuTheme("Verdant", new Color32(46, 125, 50, 255), new Color32(7, 20, 8, 255)),
            // Chrono Trigger's default window (GG-T185): a gray-blue slate gradient in a light bevelled frame, white text.
            new MenuTheme("Slate", new Color32(118, 128, 158, 255), new Color32(46, 52, 78, 255), "theme-slate"),
            // GG-T186: the DocFX modern dark docs / mindattic.com look on MindAttic.Components' web/themes/Dark/dark.css
            // tokens: flat near-black windows (both stops --bg2 #13161b); the hairline --border #2a2f3a frame and the one
            // --accent #6ba3e8 live in Theme.uss's .theme-dark block.
            new MenuTheme("Dark", new Color32(0x13, 0x16, 0x1b, 255), new Color32(0x13, 0x16, 0x1b, 255), "theme-dark"),
        };

        private static MenuTheme CurrentTheme = Themes[0];
        public static Color ThemeAccent => CurrentTheme.Accent;
        /// <summary>The active theme's root USS class, or null when it only recolors the window gradient.</summary>
        public static string ThemeUssClass => CurrentTheme.UssClass;
        public static string ThemeName => CurrentTheme.Name;

        /// <summary>Switches the active theme, regenerating any already-created textures in place (same
        /// Texture2D instance, so every <c>VisualElement</c> already pointing at it updates with no rebuild).
        /// Unknown names fall back to Classic. Call once at save load and again whenever Settings changes it.</summary>
        public static void ApplyTheme(string name)
        {
            var found = Themes.FirstOrDefault(t => t.Name == name);
            CurrentTheme = found.Name != null ? found : Themes[0];
            if (window != null) Repaint(window, CurrentTheme.ColorA, CurrentTheme.ColorB);
            if (windowDark != null) Repaint(windowDark, Darken(CurrentTheme.ColorA, 0.3f), Darken(CurrentTheme.ColorB, 0.55f));
        }

        private static Color32 Darken(Color32 c, float amount) => Color32.Lerp(c, new Color32(0, 0, 0, c.a), amount);

        /// <summary>Repaints an existing gradient texture's pixels in place (see <see cref="Gradient"/>).</summary>
        private static void Repaint(Texture2D tex, Color32 top, Color32 bottom)
        {
            int h = tex.height;
            for (int y = 0; y < h; y++)
            {
                var c = Color32.Lerp(bottom, top, y / (float)(h - 1));
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();
        }

        /// <summary>The pointing-glove cursor (tools/make_icons.py, UI/cursor); the generated one if it's missing.</summary>
        public static Texture2D Cursor => cursor ??= (Resources.Load<Texture2D>("UI/cursor") ?? MakeCursor());
        public static Texture2D Glow => glow ??= MakeGlow();
        public static Texture2D Orb => orb ??= MakeOrb();

        private static Texture2D NewTex(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        }

        private static Texture2D Solid(Color c, string name)
        {
            var t = NewTex(4, 4, name);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D Gradient(Color32 top, Color32 bottom, string name)
        {
            const int h = 128;
            var t = NewTex(2, h, name);
            for (int y = 0; y < h; y++)
            {
                var c = Color32.Lerp(bottom, top, y / (float)(h - 1));
                t.SetPixel(0, y, c);
                t.SetPixel(1, y, c);
            }
            t.Apply();
            return t;
        }

        private static Texture2D MakeCursor()
        {
            // A white right-pointing gauntlet-ish arrow with a dark outline, like the classic menu hand.
            const int s = 64;
            var t = NewTex(s, s, "cursor");
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x / (float)s, fy = y / (float)s;
                    // arrow head
                    float head = fx > 0.45f ? (1f - fx) * 1.8f : 0f;
                    bool inHead = fx > 0.45f && Mathf.Abs(fy - 0.5f) < head * 0.5f;
                    bool inShaft = fx >= 0.08f && fx <= 0.5f && Mathf.Abs(fy - 0.5f) < 0.13f;
                    bool inCuff = fx >= 0.04f && fx <= 0.16f && Mathf.Abs(fy - 0.5f) < 0.22f;
                    bool inside = inHead || inShaft || inCuff;
                    bool edge = false;
                    if (!inside)
                    {
                        for (int k = 0; k < 8 && !edge; k++)
                        {
                            float ox = fx + Mathf.Cos(k * Mathf.PI / 4f) * 0.04f, oy = fy + Mathf.Sin(k * Mathf.PI / 4f) * 0.04f;
                            float h2 = ox > 0.45f ? (1f - ox) * 1.8f : 0f;
                            edge = (ox > 0.45f && Mathf.Abs(oy - 0.5f) < h2 * 0.5f) || (ox >= 0.08f && ox <= 0.5f && Mathf.Abs(oy - 0.5f) < 0.13f) || (ox >= 0.04f && ox <= 0.16f && Mathf.Abs(oy - 0.5f) < 0.22f);
                        }
                    }
                    var shade = Mathf.Lerp(1f, 0.78f, fy);
                    t.SetPixel(x, y, inside ? new Color(shade, shade, 1f, 1f) : edge ? new Color(0.05f, 0.05f, 0.15f, 1f) : clear);
                }
            t.Apply();
            return t;
        }

        /// <summary>GLSL-style smoothstep (Unity's Mathf.SmoothStep is an interpolator, not an edge function).</summary>
        private static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Texture2D MakeGlow()
        {
            const int s = 64;
            var t = NewTex(s, s, "glow");
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s / 2f + 0.5f) / (s / 2f), dy = (y - s / 2f + 0.5f) / (s / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft ring that stays bright out to the frame edge (≈0.75) then falls off.
                    float a = d < 0.7f ? 1f : 1f - Edge(0.7f, 1f, d);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        private static Texture2D MakeOrb()
        {
            const int s = 64;
            var t = NewTex(s, s, "orb");
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s / 2f + 0.5f) / (s / 2f), dy = (y - s / 2f + 0.5f) / (s / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 1f) { t.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt((dx + 0.35f) * (dx + 0.35f) + (dy - 0.35f) * (dy - 0.35f)) * 1.6f);
                    float rim = Edge(0.8f, 1f, d);
                    float v = Mathf.Lerp(0.85f, 0.55f, rim) + hl * 0.5f;
                    float a = 1f - Edge(0.94f, 1f, d);
                    t.SetPixel(x, y, new Color(v, v, v, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>The one mana orb color (generic mana, AMENDMENTS 2026-10-10).</summary>
        public static readonly Color Mana = new Color(0.42f, 0.78f, 1f);

        public static Color HexColor(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c)) return c;
            return fallback;
        }
    }
}
