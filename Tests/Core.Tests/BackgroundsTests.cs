using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Backgrounds at the art's own resolution, named after their story region (GG-T353, BIBLE §12.1). Owner
    /// 2026-11-07: "make sure the source files for the Backgrounds all get proper naming to identify them to their world
    /// lore counterparts". Every GridGame2026 biome backdrop is imported at 1536×3072 as <c>&lt;region&gt;_NN</c>, and
    /// each main campaign theme names its set in campaign.json.</summary>
    public class BackgroundsTests
    {
        private static string Folder => Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Backgrounds");

        private static bool OnDisk(string resourceKey) =>
            File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".jpg"))
            || File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", resourceKey.Replace('/', Path.DirectorySeparatorChar) + ".png"));

        /// <summary>Theme id → (set, image count, source size). The first five are the GridGame2026 biomes (Moors,
        /// RedThorns, ElectricWasteland, UnderTheBridge, CyberNecropolis less its two byte-identical duplicates).</summary>
        private static readonly Dictionary<string, (string set, int count, int w, int h)> Expected = new Dictionary<string, (string, int, int, int)>
        {
            ["greenvalley"] = ("tithemoors", 5, 1536, 3072),
            ["lanternroad"] = ("lanternroad", 4, 768, 1536), // GG-T345: the owner's four lantern-road landscapes, native size
            ["desert"] = ("shardfall", 5, 1536, 3072),
            ["swamp"] = ("arkgrave", 5, 1536, 3072),
            ["cave"] = ("anchorwastes", 4, 1536, 3072),
            ["still"] = ("still", 6, 768, 1536),
            ["cityruins"] = ("halcyon", 14, 1536, 3072),
            ["cathedral"] = ("cathedral", 4, 768, 1536), // GG-T350: the owner's four cathedral landscapes, native size
            ["bloom"] = ("bloom", 8, 768, 1536), // GG-T347: a new postgame chapter (no trailing digit, so it is its own base theme)
            ["morrowsshadow"] = ("morrowsshadow", 10, 768, 1536), // GG-T348: the final postgame chapter, its own base theme too
        };

        /// <summary>Postgame themes (GG-T346/T349) that show a region set of their own instead of borrowing their base
        /// theme's: theme id → (set, image count, source size).</summary>
        private static readonly Dictionary<string, (string set, int count, int w, int h)> OwnPostgame = new Dictionary<string, (string, int, int, int)>
        {
            ["greenvalley2"] = ("freeholds", 7, 768, 1536), // GG-T346: the owner's seven Free Holds landscapes, native size
            ["cityruins2"] = ("underspire", 9, 768, 1536), // GG-T349: the owner's nine underspire night-market landscapes, native size
        };

        private static IEnumerable<(string set, int count, int w, int h)> AllSets => Expected.Values.Concat(OwnPostgame.Values);

        private static IEnumerable<ThemeDef> MainThemes => Real.Campaign.Themes.Where(t => Backdrops.BaseTheme(t.Id) == t.Id);

        private static void AssertSet(ThemeDef t, (string set, int count, int w, int h) e)
        {
            var (set, count, w, h) = e;
            Assert.That(t.Backgrounds, Is.EqualTo(set), $"{t.Id} names its region's set");
            var s = Backdrops.Probe(t.Backgrounds, OnDisk);
            Assert.That(s.Padded, Is.True, $"{set}: numbered from 01");
            Assert.That(s.Count, Is.EqualTo(count).And.AtLeast(4), $"{set}: every image the art gives it");
            for (int i = 0; i < s.Count; i++)
            {
                var file = Path.Combine(Folder, $"{set}_{i + 1:00}.jpg");
                Assert.That(JpegSize(file), Is.EqualTo((w, h)), $"{Path.GetFileName(file)}: the original resolution, not downscaled");
            }
            Assert.That(OnDisk(Backdrops.Key(set, count, true)), Is.False, $"{set} has exactly {count}");
        }

        [Test]
        public void Every_main_theme_names_a_region_set_of_at_least_four_backgrounds_at_full_size()
        {
            Assert.That(MainThemes.Select(t => t.Id), Is.EquivalentTo(Expected.Keys));
            foreach (var t in MainThemes) AssertSet(t, Expected[t.Id]);
        }

        [Test]
        public void A_postgame_theme_with_its_own_landscapes_names_its_region_set()
        {
            foreach (var kv in OwnPostgame)
            {
                var t = Real.Campaign.Themes.Single(x => x.Id == kv.Key);
                Assert.That(Backdrops.BaseTheme(t.Id), Is.Not.EqualTo(t.Id), $"{t.Id} is a postgame theme");
                AssertSet(t, kv.Value);
                foreach (var id in t.Stages)
                    Assert.That(Real.Stages[id].Theme, Is.EqualTo(t.Id), $"{id}: the battle backdrop reads the stage's theme, so it names the postgame theme");
                Assert.That(Backdrops.Resolve(t.Id, kv.Value.count, s => Backdrops.Probe(s, OnDisk), x => Backdrops.SetOf(Real, x)),
                    Is.EqualTo($"Backgrounds/{kv.Value.set}_01"), $"{t.Id}: wraps over its own set");
            }
        }

        [Test]
        public void A_postgame_theme_shows_its_base_themes_set()
        {
            BackdropSet Probe(string set) => Backdrops.Probe(set, OnDisk);
            string SetOf(string theme) => Backdrops.SetOf(Real, theme);
            foreach (var t in Real.Campaign.Themes.Where(t => Backdrops.BaseTheme(t.Id) != t.Id && !OwnPostgame.ContainsKey(t.Id)))
            {
                var set = Expected[Backdrops.BaseTheme(t.Id)].set;
                for (int i = 0; i < 6; i++)
                    Assert.That(Backdrops.Resolve(t.Id, i, Probe, SetOf), Does.StartWith($"Backgrounds/{set}_"), t.Id);
            }
            Assert.That(Backdrops.Resolve("greenvalley", 4, Probe, SetOf), Is.EqualTo("Backgrounds/tithemoors_05"), "all five, not four");
            Assert.That(Backdrops.Resolve("cityruins", 13, Probe, SetOf), Is.EqualTo("Backgrounds/halcyon_14"));
        }

        [Test]
        public void Every_background_file_is_region_named_with_a_unique_meta_and_no_legacy_names_remain()
        {
            var themeIds = new HashSet<string>(Real.Campaign.Themes.Select(t => t.Id).Where(id => !AllSets.Any(e => e.set == id)));
            var files = Directory.GetFiles(Folder).Where(f => !f.EndsWith(".meta")).Select(Path.GetFileName).ToList();
            Assert.That(files, Has.Count.EqualTo(AllSets.Sum(e => e.count)), "nothing but the region sets");
            var guids = new HashSet<string>();
            foreach (var f in files)
            {
                var m = Regex.Match(f, @"^([a-z]+)_(\d\d)\.(jpg|png)$");
                Assert.That(m.Success, $"{f}: named <region>_NN");
                Assert.That(themeIds, Does.Not.Contain(m.Groups[1].Value), $"{f}: named after its region, not a theme id");
                var meta = Path.Combine(Folder, f + ".meta");
                Assert.That(File.Exists(meta), $"{f} has its .meta");
                var guid = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([0-9a-f]{32})").Groups[1].Value;
                Assert.That(guids.Add(guid), $"{f}: its GUID is unique");
            }
            foreach (var legacy in new[] { "greenvalley", "desert", "swamp", "cave", "cityruins" })
                Assert.That(files.Where(f => f.StartsWith(legacy + "_")), Is.Empty, $"no legacy {legacy}_<n> files");
            Assert.That(Directory.GetFiles(Folder, "*.meta").Length, Is.EqualTo(files.Count), "no orphaned .meta");
        }

        /// <summary>A baseline or progressive JPEG's pixel size, from its first SOF marker.</summary>
        private static (int w, int h) JpegSize(string path)
        {
            var b = File.ReadAllBytes(path);
            Assert.That(b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8, $"{Path.GetFileName(path)} is a JPEG");
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                byte marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                int len = (b[i + 2] << 8) | b[i + 3];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + len;
            }
            return (0, 0);
        }
    }
}
