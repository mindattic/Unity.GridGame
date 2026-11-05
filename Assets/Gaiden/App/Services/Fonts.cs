using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Dynamic SDF font assets built at runtime from the bundled TTFs, each with a fallback chain
    /// (Noto Sans → Noto Sans Symbols 2) so every glyph the UI uses renders — no missing-glyph boxes.
    /// </summary>
    public static class Fonts
    {
        public readonly struct FontPreset
        {
            public readonly string Name, Path;
            public FontPreset(string name, string path) { Name = name; Path = path; }
        }

        /// <summary>Body font choices (owner direction 2026-10-11: "allow a selection of different Fonts
        /// to change look and feel"). Calibri/Aptos are Microsoft-proprietary (not bundleable); Inter
        /// fills that "clean modern UI sans" niche instead. "Attic" isn't a real distributable family;
        /// Montserrat fills that slot. The Display font (Cinzel) stays fixed across every preset.</summary>
        public static readonly FontPreset[] Presets =
        {
            new FontPreset("Classic", "Fonts/Outfit"),
            new FontPreset("Lato", "Fonts/Lato"),
            new FontPreset("Roboto", "Fonts/Roboto"),
            new FontPreset("Play", "Fonts/Play"),
            new FontPreset("Montserrat", "Fonts/Montserrat"),
            new FontPreset("Inter", "Fonts/Inter"),
        };

        private static string currentBodyPath = Presets[0].Path;
        private static FontAsset body, display, heavy, noto, symbols, outfit;

        public static FontAsset Body => body ??= Make(currentBodyPath, true);
        public static FontAsset Display => display ??= Make("Fonts/Cinzel", true);
        /// <summary>A real Outfit Bold cut (GG-T245): SDF faux-bold on a Regular file dilates and rounds every glyph, which read as "bubbly".</summary>
        public static FontAsset Heavy => heavy ??= Make("Fonts/OutfitBold", true);

        private static FontAsset Fallback(ref FontAsset slot, string path)
        {
            if (slot != null) return slot;
            var f = Resources.Load<Font>(path);
            slot = f != null ? FontAsset.CreateFontAsset(f) : null;
            return slot;
        }

        private static FontAsset Make(string path, bool withFallbacks)
        {
            var font = Resources.Load<Font>(path);
            if (font == null) { Debug.LogError($"[Fonts] missing {path}"); return null; }
            var fa = FontAsset.CreateFontAsset(font);
            if (fa == null) { Debug.LogError($"[Fonts] could not create font asset for {path}"); return null; }
            if (withFallbacks)
            {
                fa.fallbackFontAssetTable = new List<FontAsset>();
                var n = Fallback(ref noto, "Fonts/NotoSans");
                var s = Fallback(ref symbols, "Fonts/NotoSansSymbols2");
                if (n != null) fa.fallbackFontAssetTable.Add(n);
                if (s != null) fa.fallbackFontAssetTable.Add(s);
                // Outfit as a final, universal fallback: the other five body-font presets (owner direction
                // 2026-10-11) each lack a few glyphs Noto Sans also lacks (e.g. "→" is missing from Roboto
                // and Play) that Outfit happens to cover, so every preset needs it in its chain, not just Classic.
                if (path != "Fonts/Outfit")
                {
                    var o = Fallback(ref outfit, "Fonts/Outfit");
                    if (o != null) fa.fallbackFontAssetTable.Add(o);
                }
            }
            return fa;
        }

        public static void UseBody(VisualElement e) { if (Body != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(Body); }
        public static void UseDisplay(VisualElement e) { if (Display != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(Display); }
        public static void UseHeavy(VisualElement e)
        {
            if (Heavy == null) return;
            e.style.unityFontDefinition = FontDefinition.FromSDFFont(Heavy);
            e.style.unityFontStyleAndWeight = FontStyle.Normal;
        }

        /// <summary>
        /// Switches the body font and re-applies it to the UI root in place. unityFontDefinition is an
        /// inherited style property, so every element that doesn't explicitly override it (everything
        /// except the handful of Display-tagged headlines) updates immediately — no screen rebuild
        /// needed. Unknown names fall back to Classic. Call once at save load and again whenever
        /// Settings changes it (mirrors Art.ApplyTheme).
        /// </summary>
        public static void ApplyFont(string name)
        {
            var found = Presets.FirstOrDefault(p => p.Name == name);
            var path = found.Name != null ? found.Path : Presets[0].Path;
            if (path == currentBodyPath && body != null) return;
            currentBodyPath = path;
            body = null;
            if (GameApp.I != null && GameApp.I.Document != null) UseBody(GameApp.I.Document.rootVisualElement);
        }
    }
}
