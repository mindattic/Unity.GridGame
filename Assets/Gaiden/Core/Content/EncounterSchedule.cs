using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GridGame.Core.Content
{
    /// <summary>One scheduled endless encounter: everything <see cref="EncounterSchedule"/> stratified for it.
    /// Feed it to <see cref="EncounterGenerator.Generate(ContentDb, EncounterPlan)"/> for the enemy side and to
    /// <c>EncounterParty.Generate(db, plan.Seed, plan.Level, plan.HeroClasses)</c> for the hero side.</summary>
    public sealed class EncounterPlan
    {
        /// <summary>0-based position in the run.</summary>
        public int Index;
        /// <summary>The encounter's own seed (base seed + index, the GG-T57 convention): everything the schedule
        /// does not fix (wave sizes, groups, foes) is still drawn from it.</summary>
        public int Seed;
        /// <summary>The donor stage's <see cref="StageDef.Theme"/> (biome).</summary>
        public string Biome;
        /// <summary>The donor stage (location: board blocks, background, music).</summary>
        public string StageId;
        /// <summary>One enemy family (<see cref="ClassDef.DropTable"/>) per wave, distinct within the encounter.</summary>
        public List<string> Families = new List<string>();
        /// <summary>The hero classes of the party, distinct within the encounter.</summary>
        public List<string> HeroClasses = new List<string>();
        /// <summary>The encounter's level: the stage's <see cref="StageDef.LevelOverride"/>, the middle wave's level
        /// and the level the party is built at.</summary>
        public int Level;
        public int BandStart;
        public int BandEnd;
        public string Band => EncounterSchedule.BandLabel(BandStart, BandEnd);
    }

    /// <summary>
    /// GG-T60: spectrum coverage scheduler for endless mode. Rather than pure random picks per encounter, every
    /// dimension — enemy family, hero class, biome (and location within it) and level band (1–5, 6–10, …) — is
    /// drawn round-robin without replacement from its own seeded shuffled deck: each value comes up once before
    /// any comes up twice, then the deck reshuffles. A run of a few hundred encounters therefore covers the whole
    /// spectrum evenly (per dimension, any two values' counts differ by at most one draw-cycle's worth), and the
    /// same base seed always yields the same schedule. <see cref="Coverage"/> tallies what was drawn and
    /// <see cref="CoverageTable"/> formats the end-of-run log table.
    /// </summary>
    public sealed class EncounterSchedule
    {
        public const int DefaultBandSize = 5;
        /// <summary>Heroes per scheduled party (matches <c>EncounterParty.PartySize</c>; Core can't see Meta).</summary>
        public const int HeroesPerEncounter = 5;

        private readonly int baseSeed;
        private readonly int heroesPerEncounter;
        private readonly Deck<string> families;
        private readonly Deck<string> heroes;
        private readonly Deck<string> biomes;
        private readonly Dictionary<string, Deck<string>> stagesByBiome = new Dictionary<string, Deck<string>>(StringComparer.Ordinal);
        private readonly Deck<int> bands;
        private readonly List<(int Start, int End)> bandRanges = new List<(int, int)>();
        private readonly Rng levelRng;
        private int next;

        public EncounterCoverage Coverage { get; }

        public EncounterSchedule(ContentDb db, int baseSeed, int bandSize = DefaultBandSize, int heroesPerEncounter = HeroesPerEncounter)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            this.baseSeed = baseSeed;
            this.heroesPerEncounter = Math.Max(1, heroesPerEncounter);
            bandSize = Math.Max(1, bandSize);

            // One stream per dimension so adding/removing content in one dimension never reshuffles the others.
            var familyIds = db.Classes.Values
                .Where(c => c.Team == Team.Enemy && !string.IsNullOrEmpty(c.DropTable))
                .Select(c => c.DropTable).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (familyIds.Count == 0) throw new InvalidOperationException("EncounterSchedule: no enemy family (ClassDef.DropTable) to draw from");

            var heroIds = db.Classes.Values.Where(c => c.Team == Team.Hero)
                .Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (heroIds.Count == 0) throw new InvalidOperationException("EncounterSchedule: no hero class to draw from");

            var donors = db.Stages.Values.Where(s => !s.IsTest && s.Waves.Count > 0)
                .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            if (donors.Count == 0) throw new InvalidOperationException("EncounterSchedule: no real stage to borrow location/blocks from");
            var byBiome = donors.GroupBy(s => BiomeOf(s), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

            int campaignMax = Math.Max(1, db.CampaignStages().Count);
            for (int start = 1; start <= campaignMax; start += bandSize)
                bandRanges.Add((start, Math.Min(campaignMax, start + bandSize - 1)));

            families = new Deck<string>(familyIds, new Rng(unchecked(baseSeed ^ 0x0F4A11E5)), StringComparer.OrdinalIgnoreCase);
            heroes = new Deck<string>(heroIds, new Rng(unchecked(baseSeed ^ 0x4E50E5)), StringComparer.Ordinal);
            biomes = new Deck<string>(byBiome.Select(g => g.Key).ToList(), new Rng(unchecked(baseSeed ^ 0x0B10AE5)), StringComparer.Ordinal);
            int salt = 0;
            foreach (var g in byBiome)
                stagesByBiome[g.Key] = new Deck<string>(g.Select(s => s.Id).ToList(), new Rng(unchecked(baseSeed ^ (0x57A6E000 + salt++))), StringComparer.Ordinal);
            bands = new Deck<int>(Enumerable.Range(0, bandRanges.Count).ToList(), new Rng(unchecked(baseSeed ^ 0x0BA4D5)), EqualityComparer<int>.Default);
            levelRng = new Rng(unchecked(baseSeed ^ 0x1E7E1));

            Coverage = new EncounterCoverage(familyIds, heroIds,
                byBiome.Select(g => g.Key), donors.Select(s => s.Id),
                bandRanges.Select(b => BandLabel(b.Start, b.End)));
        }

        /// <summary>The next encounter of the run (and records it in <see cref="Coverage"/>).</summary>
        public EncounterPlan Next()
        {
            int index = next++;
            string biome = biomes.Draw();
            var band = bandRanges[bands.Draw()];
            var plan = new EncounterPlan
            {
                Index = index,
                Seed = unchecked(baseSeed + index),
                Biome = biome,
                StageId = stagesByBiome[biome].Draw(),
                Families = families.DrawDistinct(EncounterGenerator.WaveCount),
                HeroClasses = heroes.DrawDistinct(heroesPerEncounter),
                BandStart = band.Start,
                BandEnd = band.End,
                Level = levelRng.Range(band.Start, band.End + 1),
            };
            Coverage.Record(plan);
            return plan;
        }

        /// <summary>The end-of-run coverage table (count per family / class / biome / location / band).</summary>
        public string CoverageTable() => Coverage.Format();

        internal static string BiomeOf(StageDef s) => string.IsNullOrEmpty(s.Theme) ? "(none)" : s.Theme;

        public static string BandLabel(int start, int end) => start == end ? start.ToString() : $"{start}-{end}";

        /// <summary>A seeded shuffled deck drawn without replacement; reshuffles when exhausted (round-robin).</summary>
        private sealed class Deck<T>
        {
            private readonly List<T> all;
            private readonly Rng rng;
            private readonly IEqualityComparer<T> cmp;
            private readonly List<T> pile = new List<T>();

            public Deck(List<T> items, Rng rng, IEqualityComparer<T> cmp) { all = items; this.rng = rng; this.cmp = cmp; }

            private void Refill()
            {
                pile.Clear();
                pile.AddRange(all);
                for (int i = pile.Count - 1; i > 0; i--)
                {
                    int j = rng.Range(0, i + 1);
                    (pile[i], pile[j]) = (pile[j], pile[i]);
                }
            }

            private T Pop()
            {
                if (pile.Count == 0) Refill();
                var x = pile[pile.Count - 1];
                pile.RemoveAt(pile.Count - 1);
                return x;
            }

            public T Draw() => Pop();

            /// <summary><paramref name="count"/> distinct values (capped at the deck size). When a draw crosses a
            /// reshuffle and hits a value already taken this time, that value is set aside and put back on top,
            /// so it is the first one drawn next time — no value loses its turn in the cycle.</summary>
            public List<T> DrawDistinct(int count)
            {
                count = Math.Min(count, all.Count);
                var picked = new List<T>(count);
                var deferred = new List<T>();
                while (picked.Count < count)
                {
                    var x = Pop();
                    if (picked.Contains(x, cmp)) deferred.Add(x); else picked.Add(x);
                }
                for (int i = deferred.Count - 1; i >= 0; i--) pile.Add(deferred[i]);
                return picked;
            }
        }
    }

    /// <summary>GG-T60: tallies of what an <see cref="EncounterSchedule"/> drew, per dimension. Every value the
    /// content offers is listed from the start, so a value never drawn shows as 0 in the table.</summary>
    public sealed class EncounterCoverage
    {
        public int Encounters { get; private set; }
        public Dictionary<string, int> Families { get; }
        public Dictionary<string, int> HeroClasses { get; }
        public Dictionary<string, int> Biomes { get; }
        public Dictionary<string, int> Locations { get; }
        public Dictionary<string, int> Bands { get; }

        public EncounterCoverage(IEnumerable<string> families, IEnumerable<string> heroClasses, IEnumerable<string> biomes,
            IEnumerable<string> locations, IEnumerable<string> bands)
        {
            Families = Zeroed(families, StringComparer.OrdinalIgnoreCase);
            HeroClasses = Zeroed(heroClasses, StringComparer.Ordinal);
            Biomes = Zeroed(biomes, StringComparer.Ordinal);
            Locations = Zeroed(locations, StringComparer.Ordinal);
            Bands = Zeroed(bands, StringComparer.Ordinal);
        }

        private static Dictionary<string, int> Zeroed(IEnumerable<string> keys, StringComparer cmp)
        {
            var d = new Dictionary<string, int>(cmp);
            foreach (var k in keys) d[k] = 0;
            return d;
        }

        private static void Bump(Dictionary<string, int> d, string key)
        {
            if (key == null) return;
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }

        public void Record(EncounterPlan plan)
        {
            Encounters++;
            foreach (var f in plan.Families) Bump(Families, f);
            foreach (var h in plan.HeroClasses) Bump(HeroClasses, h);
            Bump(Biomes, plan.Biome);
            Bump(Locations, plan.StageId);
            Bump(Bands, plan.Band);
        }

        /// <summary>The end-of-run log table: one section per dimension with each value's count, plus the
        /// min/max spread and how many values were never drawn.</summary>
        public string Format()
        {
            var sb = new StringBuilder();
            sb.Append("Endless coverage: ").Append(Encounters).Append(" encounter").Append(Encounters == 1 ? "" : "s").AppendLine();
            Section(sb, "Enemy family (per wave)", Families, false);
            Section(sb, "Hero class (per party slot)", HeroClasses, false);
            Section(sb, "Biome", Biomes, false);
            Section(sb, "Location", Locations, false);
            Section(sb, "Level band", Bands, true);
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, Dictionary<string, int> counts, bool keepOrder)
        {
            var rows = keepOrder ? counts.ToList() : counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            int total = rows.Sum(r => r.Value);
            int min = rows.Count > 0 ? rows.Min(r => r.Value) : 0;
            int max = rows.Count > 0 ? rows.Max(r => r.Value) : 0;
            int missing = rows.Count(r => r.Value == 0);
            sb.Append(title).Append(": ").Append(rows.Count).Append(" values, ").Append(total).Append(" draws, min ")
              .Append(min).Append(" / max ").Append(max);
            if (missing > 0) sb.Append(", ").Append(missing).Append(" never drawn");
            sb.AppendLine();
            int width = rows.Count > 0 ? rows.Max(r => r.Key.Length) : 0;
            foreach (var r in rows) sb.Append("  ").Append(r.Key.PadRight(width)).Append("  ").Append(r.Value.ToString().PadLeft(5)).AppendLine();
        }
    }
}
