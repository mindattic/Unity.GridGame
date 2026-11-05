using System;
using System.Collections.Generic;
using System.Linq;

namespace GridGame.Core.Content
{
    /// <summary>Once plays to its duration and is gone; Loop keeps going until its owner (a status, a unit) ends it.</summary>
    public enum VfxMode { Once, Loop }

    /// <summary>
    /// One effect prefab in vfx.json (GG-T269): a premade particle prefab under Resources/Vfx, normalized so every
    /// effect lands at a known size in board tiles and a known length, whatever scale it was authored at.
    /// </summary>
    public sealed class VfxPrefabDef
    {
        public string Id;
        /// <summary>Resources path; defaults to "Vfx/&lt;Id&gt;".</summary>
        public string Path;
        public VfxMode Mode;
        /// <summary>On-screen size, in board tiles, of the effect's widest extent.</summary>
        public float Tiles = 1f;
        /// <summary>Once: seconds until emission stops (the authored prefab may run far longer). Loop: unused.</summary>
        public float Duration = 1f;
        /// <summary>The prefab's authored extent in world units (tools/vfx-measure.json): Tiles is mapped onto it.</summary>
        public float Extent = 1f;
        /// <summary>Tint the particles with the element style's Core color (for neutral/white prefabs).</summary>
        public bool Tint;
        public string Cue;
        /// <summary>Where it came from (licence record): "GridGame2026 (owned)" or a CC0 pack.</summary>
        public string Source;

        public string ResourcePath => string.IsNullOrEmpty(Path) ? "Vfx/" + Id : Path;
    }

    /// <summary>vfx.json: every effect prefab the battle can spawn; vfxstyle.json points elements, tiers and statuses at them.</summary>
    public sealed class VfxCatalog
    {
        public List<VfxPrefabDef> Effects = new List<VfxPrefabDef>();

        private Dictionary<string, VfxPrefabDef> byId;

        public VfxPrefabDef Find(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, VfxPrefabDef>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in Effects) if (e?.Id != null) byId[e.Id] = e;
            }
            return byId.TryGetValue(id, out var d) ? d : null;
        }

        /// <summary>The prefab for an element/family/status look at a tier: the style's own tier prefab when it lists
        /// tiers, else the tier's shared prefab, else the style's default; null means the code-drawn effect plays.</summary>
        public static string PrefabFor(VfxStyleSheet sheet, VfxStyleDef style, VfxTier tier)
        {
            if (style == null) return null;
            // A style that lists tiers owns that choice: a tier it leaves out stays code-drawn.
            if (style.TierPrefabs != null)
                return style.TierPrefabs.TryGetValue(tier.ToString(), out var id) && !string.IsNullOrEmpty(id) ? id : null;
            return sheet?.TierDef(tier)?.Prefab ?? style.Prefab;
        }

        public static void Validate(ContentDb db, List<string> p)
        {
            var cat = db.VfxPrefabs;
            if (cat == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in cat.Effects)
            {
                string w = $"vfx {e?.Id}";
                if (e == null || string.IsNullOrWhiteSpace(e.Id)) { p.Add("vfx: an effect has no Id"); continue; }
                if (!seen.Add(e.Id)) p.Add($"{w}: duplicate Id");
                if (e.Tiles <= 0f || e.Tiles > 6f) p.Add($"{w}: Tiles {e.Tiles} out of range (0, 6]");
                if (e.Extent <= 0f) p.Add($"{w}: Extent must be positive");
                if (e.Mode == VfxMode.Once && (e.Duration <= 0f || e.Duration > 4f)) p.Add($"{w}: a Once effect needs a Duration in (0, 4] s");
                if (e.Cue != null && !(e.Cue.StartsWith("element.") || e.Cue.StartsWith("status.") || e.Cue.StartsWith("battle.")))
                    p.Add($"{w}: Cue '{e.Cue}' is off the sound taxonomy");
                if (string.IsNullOrWhiteSpace(e.Source)) p.Add($"{w}: needs a Source (licence record)");
            }
            var sheet = db.Vfx;
            if (sheet == null) return;
            void Check(string owner, string id)
            {
                if (id != null && cat.Find(id) == null) p.Add($"vfxstyle {owner}: Prefab '{id}' is not in vfx.json");
            }
            foreach (var s in sheet.Elements.Concat(sheet.Statuses))
            {
                if (s == null) continue;
                Check(s.Id, s.Prefab);
                if (s.TierPrefabs == null) continue;
                foreach (var kv in s.TierPrefabs)
                {
                    if (!Enum.TryParse(kv.Key, true, out VfxTier _)) p.Add($"vfxstyle {s.Id}: TierPrefabs key '{kv.Key}' is not Minor, Major or Ultimate");
                    Check(s.Id, kv.Value);
                }
            }
            foreach (var t in sheet.Tiers) Check("tier " + t.Tier, t.Prefab);
        }
    }
}
