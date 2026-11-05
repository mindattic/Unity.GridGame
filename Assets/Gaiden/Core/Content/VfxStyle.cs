using System;
using System.Collections.Generic;
using System.Linq;

namespace GridGame.Core.Content
{
    /// <summary>How hard an effect lands (POL-E5): scales particle count, size, screen shake and hit-stop.</summary>
    public enum VfxTier { Minor, Major, Ultimate }

    /// <summary>
    /// One look in vfxstyle.json (GG-T253, POL-E4): an element (a <see cref="DamageType"/> name), a non-damage family
    /// ("Heal", "Water") or a status (a buff id). A status that names an <see cref="Element"/> inherits every field it
    /// leaves empty from that element, so Burning reads as Fire and Frozen as Ice.
    /// </summary>
    public sealed class VfxStyleDef
    {
        public string Id;
        public string Element;
        /// <summary>#RRGGBB: the particle body, its bloom, and the hot accent mixed into it.</summary>
        public string Core, Glow, Accent;
        /// <summary>The particle silhouette: one of <see cref="VfxStyleSheet.Shapes"/>.</summary>
        public string Shape;
        /// <summary>How the particles move: one of <see cref="VfxStyleSheet.Motions"/>.</summary>
        public string Motion;
        /// <summary>The sound family (an element./status./battle. cue, POL-F1).</summary>
        public string Cue;
        public int? Count;
        public float? Size, Reach;
        /// <summary>A timing token name (POL-E1): Instant, Quick, Standard, Emphasis, Dramatic, Cinematic.</summary>
        public string Time;
        /// <summary>Draws a ring with the burst (frost ring, arcane sigil, ward).</summary>
        public bool? Ring;
        /// <summary>Text drawn by the "glyph" shape (a sleeper's "z").</summary>
        public string Glyph;
        /// <summary>Statuses: #RRGGBB tint on the unit's art while it takes hold; <see cref="TintHolds"/> keeps it on.</summary>
        public string Tint;
        public bool? TintHolds;
        /// <summary>Statuses: the unit swells once as it takes hold (Empowered).</summary>
        public bool? Swell;
        /// <summary>Statuses: one lingering particle every N beats while the status lasts (0 = none).</summary>
        public int? LingerEvery;
        /// <summary>A vfx.json effect id (GG-T269) that replaces this style's code-drawn burst; null keeps the code-drawn
        /// effect. A status's prefab plays as it takes hold, and a Loop prefab stays on the unit while the status lasts.</summary>
        public string Prefab;
        /// <summary>Per-tier effect ids ("Minor"/"Major"/"Ultimate"), overriding <see cref="Prefab"/> at that tier.</summary>
        public Dictionary<string, string> TierPrefabs;
    }

    public sealed class VfxTierDef
    {
        public VfxTier Tier;
        public float Count = 1f, Size = 1f, Shake;
        /// <summary>Seconds the impact freezes before follow-through (POL-E6).</summary>
        public float HitStop;
        /// <summary>A tier-wide vfx.json effect id, used when a style names none for this tier (GG-T269).</summary>
        public string Prefab;
    }

    /// <summary>vfxstyle.json: the one table every burst, status visual, projectile trail and impact reads (POL-E4).</summary>
    public sealed class VfxStyleSheet
    {
        public static readonly string[] Shapes = { "spark", "shard", "ember", "bubble", "mote", "smoke", "rune", "ray", "glyph", "droplet" };
        public static readonly string[] Motions = { "burst", "rise", "fall", "implode", "spiral", "pulse", "contract", "bolt", "rays", "drift", "billow" };
        public static readonly string[] Times = { "Instant", "Quick", "Standard", "Emphasis", "Dramatic", "Cinematic" };

        public List<VfxStyleDef> Elements = new List<VfxStyleDef>();
        public List<VfxStyleDef> Statuses = new List<VfxStyleDef>();
        public List<VfxTierDef> Tiers = new List<VfxTierDef>();

        private Dictionary<string, VfxStyleDef> resolved;

        public VfxStyleDef Element(DamageType t) => Find(t.ToString());

        /// <summary>A family ("Heal", "Water") or element by name.</summary>
        public VfxStyleDef Family(string id) => Find(id);

        /// <summary>A status's look, with every field it leaves empty taken from its element.</summary>
        public VfxStyleDef Status(string buffId) => buffId == null ? null : Find("status:" + buffId);

        public VfxTierDef TierDef(VfxTier t) => Tiers.FirstOrDefault(x => x.Tier == t) ?? new VfxTierDef { Tier = t };

        private VfxStyleDef Find(string key)
        {
            if (resolved == null) Resolve();
            return resolved.TryGetValue(key, out var s) ? s : null;
        }

        private void Resolve()
        {
            resolved = new Dictionary<string, VfxStyleDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Elements) if (e?.Id != null) resolved[e.Id] = e;
            foreach (var s in Statuses)
            {
                if (s?.Id == null) continue;
                resolved.TryGetValue(s.Element ?? "", out var b);
                resolved["status:" + s.Id] = Merge(s, b);
            }
        }

        private static VfxStyleDef Merge(VfxStyleDef s, VfxStyleDef b)
        {
            if (b == null) return s;
            return new VfxStyleDef
            {
                Id = s.Id, Element = s.Element,
                Core = s.Core ?? b.Core, Glow = s.Glow ?? b.Glow, Accent = s.Accent ?? b.Accent,
                Shape = s.Shape ?? b.Shape, Motion = s.Motion ?? b.Motion, Cue = s.Cue ?? b.Cue,
                Count = s.Count ?? b.Count, Size = s.Size ?? b.Size, Reach = s.Reach ?? b.Reach, Time = s.Time ?? b.Time,
                Ring = s.Ring ?? b.Ring, Glyph = s.Glyph ?? b.Glyph,
                Tint = s.Tint, TintHolds = s.TintHolds, Swell = s.Swell, LingerEvery = s.LingerEvery,
                Prefab = s.Prefab,
                TierPrefabs = s.TierPrefabs,
            };
        }

        /// <summary>An ability's tier: its <see cref="AbilityDef.Intensity"/> if set, else from its price — 3+ orbs or a
        /// 5+ turn cooldown is an ultimate, 2 orbs or a 3+ turn cooldown a major, anything cheaper a minor.</summary>
        public static VfxTier TierOf(AbilityDef a)
        {
            if (a == null) return VfxTier.Minor;
            if (a.Intensity != null && Enum.TryParse(a.Intensity, true, out VfxTier set)) return set;
            if (a.Cost >= 3 || a.CooldownTurns >= 5) return VfxTier.Ultimate;
            if (a.Cost >= 2 || a.CooldownTurns >= 3) return VfxTier.Major;
            return VfxTier.Minor;
        }

        public static void Validate(ContentDb db, List<string> p)
        {
            var sheet = db.Vfx;
            foreach (var a in db.Abilities.Values)
                if (a.Intensity != null && !Enum.TryParse(a.Intensity, true, out VfxTier _))
                    p.Add($"ability {a.Id}: unknown Intensity '{a.Intensity}' (Minor, Major or Ultimate)");
            if (sheet == null || sheet.Elements.Count == 0) return; // fixtures without the style table
            foreach (DamageType t in Enum.GetValues(typeof(DamageType)))
                if (sheet.Element(t) == null) p.Add($"vfxstyle: no style for element {t}");
            foreach (var b in db.Buffs.Values)
                if (sheet.Status(b.Id) == null) p.Add($"vfxstyle: no style for status {b.Id}");
            foreach (VfxTier t in Enum.GetValues(typeof(VfxTier)))
                if (sheet.Tiers.All(x => x.Tier != t)) p.Add($"vfxstyle: no tier {t}");
            var known = new HashSet<string>(sheet.Elements.Select(e => e.Id ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var s in sheet.Statuses)
                if (s.Element != null && !known.Contains(s.Element)) p.Add($"vfxstyle status {s.Id}: unknown Element '{s.Element}'");
            foreach (var s in sheet.Elements.Concat(sheet.Statuses.Select(x => sheet.Status(x.Id))).Where(x => x != null))
            {
                string w = $"vfxstyle {s.Id}";
                foreach (var hex in new[] { s.Core, s.Glow, s.Accent, s.Tint })
                    if (hex != null && !IsHex(hex)) p.Add($"{w}: '{hex}' is not #RRGGBB");
                if (s.Core == null) p.Add($"{w}: needs a Core color");
                if (s.Shape == null || !Shapes.Contains(s.Shape)) p.Add($"{w}: unknown Shape '{s.Shape}'");
                if (s.Motion == null || !Motions.Contains(s.Motion)) p.Add($"{w}: unknown Motion '{s.Motion}'");
                if (s.Time != null && !Times.Contains(s.Time)) p.Add($"{w}: Time '{s.Time}' is not a timing token");
                if (s.Cue == null || !(s.Cue.StartsWith("element.") || s.Cue.StartsWith("status.") || s.Cue.StartsWith("battle.")))
                    p.Add($"{w}: Cue '{s.Cue}' is off the sound taxonomy");
                if (s.Shape == "glyph" && string.IsNullOrEmpty(s.Glyph)) p.Add($"{w}: a glyph needs its Glyph text");
                if (s.Prefab != null && string.IsNullOrWhiteSpace(s.Prefab)) p.Add($"{w}: Prefab is blank (omit it or name a prefab)");
            }
        }

        private static bool IsHex(string s) =>
            s.Length == 7 && s[0] == '#' && s.Skip(1).All(c => Uri.IsHexDigit(c));
    }
}
