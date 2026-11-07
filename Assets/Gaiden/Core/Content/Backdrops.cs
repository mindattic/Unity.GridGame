using System;

namespace GridGame.Core.Content
{
    /// <summary>One theme's background set as found on disk: its file name, how many images, and which numbering.</summary>
    public readonly struct BackdropSet
    {
        public readonly string Name;
        public readonly int Count;
        /// <summary>True for the owner's naming (<c>&lt;set&gt;_01</c>, <c>_02</c>, …); false for the legacy <c>&lt;set&gt;_0</c>, <c>_1</c>, ….</summary>
        public readonly bool Padded;

        public BackdropSet(string name, int count, bool padded) { Name = name; Count = count; Padded = padded; }
    }

    /// <summary>
    /// Which backdrop a theme shows (GG-T344). Owner rule: background files are named after their story region, numbered
    /// from 01 with zero padding (<c>Backgrounds/still_01.jpg</c> … <c>still_06.jpg</c>), at the art's original resolution.
    /// A campaign theme may name its set (<c>ThemeDef.Backgrounds</c>, e.g. <c>"still"</c>); otherwise the set is the theme id.
    /// A set has as many images as the art gives it, and an index wraps over that count instead of a fixed four. The
    /// legacy unpadded sets (<c>greenvalley_0</c> … <c>_3</c>) still load until GG-T353 renames them. A theme with no set
    /// borrows its base theme's (a postgame theme like <c>cave2</c> shows <c>cave</c>), then the default theme's, then
    /// <see cref="Fallback"/>. Pure: the App passes a Resources probe, the tests a file probe.
    /// </summary>
    public static class Backdrops
    {
        public const string Folder = "Backgrounds/";
        public const string DefaultTheme = "greenvalley";
        /// <summary>The last resort (legacy name; GG-T353 renames it with the rest).</summary>
        public const string Fallback = Folder + DefaultTheme + "_0";
        /// <summary>The most backgrounds counted per set (a guard on the probe, not a design limit).</summary>
        public const int MaxPerTheme = 32;

        /// <summary>The resource key of image <paramref name="n"/> (0-based) of a set in either numbering.</summary>
        public static string Key(string set, int n, bool padded) => padded ? $"{Folder}{set}_{n + 1:00}" : $"{Folder}{set}_{n}";

        public static string Key(BackdropSet s, int n) => Key(s.Name, n, s.Padded);

        /// <summary>Counts <paramref name="set"/>'s images: the owner's <c>_01</c> numbering first, else the legacy <c>_0</c>.</summary>
        public static BackdropSet Probe(string set, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(set) || exists == null) return new BackdropSet(set, 0, true);
            foreach (bool padded in new[] { true, false })
            {
                int n = 0;
                while (n < MaxPerTheme && exists(Key(set, n, padded))) n++;
                if (n > 0) return new BackdropSet(set, n, padded);
            }
            return new BackdropSet(set, 0, true);
        }

        /// <summary>A postgame theme's art theme: trailing digits dropped ("greenvalley2" → "greenvalley").</summary>
        public static string BaseTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme)) return theme;
            int end = theme.Length;
            while (end > 0 && char.IsDigit(theme[end - 1])) end--;
            return end == 0 ? theme : theme.Substring(0, end);
        }

        /// <summary>The set a campaign theme (or a stage's art theme) uses: its <c>Backgrounds</c> name, else its id.</summary>
        public static string SetOf(ContentDb db, string theme)
        {
            if (string.IsNullOrEmpty(theme)) return theme;
            var t = db?.Campaign?.Themes?.Find(x => string.Equals(x.Id, theme, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(t?.Backgrounds) ? theme : t.Backgrounds;
        }

        /// <summary>The resource key for <paramref name="theme"/>'s <paramref name="index"/>th backdrop, wrapped over its
        /// set's own count (negative indices wrap too). <paramref name="probe"/> answers a set's images (cache it —
        /// <see cref="Probe"/> probes); <paramref name="setOf"/> maps a theme to its set name (null = the theme id).</summary>
        public static string Resolve(string theme, int index, Func<string, BackdropSet> probe, Func<string, string> setOf = null)
        {
            setOf ??= x => x;
            theme = string.IsNullOrEmpty(theme) ? DefaultTheme : theme;
            foreach (var candidate in new[] { theme, BaseTheme(theme), DefaultTheme })
            {
                var s = probe(setOf(candidate) ?? candidate);
                if (s.Count > 0) return Key(s, ((index % s.Count) + s.Count) % s.Count);
            }
            return Fallback;
        }
    }
}
