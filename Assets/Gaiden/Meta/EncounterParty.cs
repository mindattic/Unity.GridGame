using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// GG-T56: a random, level-appropriate hero party for an endless encounter (the hero side of
    /// GG-T55's <see cref="EncounterGenerator"/>). Without this, an endless battle at the encounter's
    /// level would field whatever bare <c>DefaultLoadout</c> a fresh <see cref="HeroSave"/> starts
    /// with — a level-40 encounter fought by a naked level-1 kit — and the capture telemetry (GG-T58/59)
    /// would only ever measure missing gear, not the fight.
    /// </summary>
    /// <remarks>
    /// Picks 5 of every hero-team class in the content (all of them, not just the campaign's small
    /// <see cref="Progression.StartingRoster"/>) and gears each the way CampRoutine/MetaHeuristics gear a
    /// real save at Camp: the best weapon, armor and relic (jewelry) <see cref="MetaHeuristics.Score"/> can
    /// find, abilities picked by the same "priciest affordable" rule <see cref="CampRoutine.BestLearnable"/>
    /// uses. The difference is that an endless encounter has no save, no gold and no bag to equip from — it
    /// only has a level — so rather than simulate an economy this reads straight off the catalog: for each
    /// slot/ability pool, ranks every candidate by power and takes the one at this level's percentile of the
    /// full 1..campaignMax spectrum (0 = the weakest in the game, 1 = the best). That keeps it a pure
    /// function of (content, level) — no SaveData, nothing but the shared <see cref="Rng"/> seed used to pick
    /// which 5 classes show up — so the same (seed, level) always builds the same party.
    /// Every hero currently proficiency-free (GG-T52 hasn't landed) can use any weapon/armor/relic, so two
    /// heroes at the same level can land on the very same top item; that's an accurate reflection of today's
    /// rules, not a bug here, and GG-T52 is where any per-class differentiation belongs.
    /// </remarks>
    public static class EncounterParty
    {
        public const int PartySize = 5;

        /// <summary>Every equip slot a hero can fill, in the order <see cref="BattleFactory.SpecFor"/> would
        /// read them back out: weapon, the five armor pieces, then the five jewelry (relic) slots.</summary>
        private static IEnumerable<EquipmentSlot> AllSlots() =>
            new[] { EquipmentSlot.Weapon }.Concat(BattleEngine.ArmorSlots).Concat(Equipment.JewelrySlots);

        /// <summary>Builds the party: <see cref="PartySize"/> distinct hero classes drawn deterministically
        /// from <paramref name="seed"/>, each built for <paramref name="level"/> (clamped to at least 1).</summary>
        public static List<HeroSpec> Generate(ContentDb db, int seed, int level) => Generate(db, seed, level, null);

        /// <summary>GG-T60: as above, but with the party's classes fixed by the caller (an
        /// <see cref="EncounterSchedule"/>'s stratified <see cref="EncounterPlan.HeroClasses"/>) instead of drawn
        /// from <paramref name="seed"/>. A null list falls back to the seeded draw.</summary>
        public static List<HeroSpec> Generate(ContentDb db, int seed, int level, IReadOnlyList<string> classIds)
        {
            List<ClassDef> picked;
            if (classIds != null && classIds.Count > 0)
                picked = classIds.Select(db.Class).ToList();
            else
            {
                var rng = new Rng(seed);
                var classes = db.Classes.Values.Where(c => c.Team == Team.Hero)
                    .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
                if (classes.Count == 0) throw new InvalidOperationException("EncounterParty: no hero class to draw from");
                picked = Shuffle(rng, classes).Take(Math.Min(PartySize, classes.Count)).ToList();
            }
            int campaignMax = Math.Max(1, db.CampaignStages().Count);
            int clampedLevel = Math.Max(1, level);
            float band = campaignMax > 1 ? Clamp01((clampedLevel - 1) / (float)(campaignMax - 1)) : 1f;

            return picked.Select(def => Build(db, def, clampedLevel, band)).ToList();
        }

        private static List<ClassDef> Shuffle(Rng rng, List<ClassDef> classes)
        {
            var shuffled = new List<ClassDef>(classes);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return shuffled;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));

        private static HeroSpec Build(ContentDb db, ClassDef def, int level, float band)
        {
            var spec = new HeroSpec { ClassId = def.Id, Level = level };

            foreach (var slot in AllSlots())
            {
                var item = BestForBand(db.Items.Values.Where(i => Equipment.Fits(i, slot)), band);
                if (item == null) continue;
                if (slot == EquipmentSlot.Weapon) { spec.WeaponId = item.Id; spec.WeaponDurability = item.Durability; }
                else if (Array.IndexOf(Equipment.JewelrySlots, slot) < 0) spec.Armor[slot] = new ArmorPieceSpec { ItemId = item.Id, Durability = item.Durability };
                spec.EquippedItemIds.Add(item.Id);
            }

            spec.Bar = BuildBar(db, def, band);
            foreach (var entry in spec.Bar.Where(e => e != null && e.StartsWith("item:")))
                spec.ItemCounts[entry.Substring(5)] = 3;

            return spec;
        }

        /// <summary>The candidate at this band's percentile by <see cref="MetaHeuristics.Score"/> (ascending):
        /// band 0 picks the weakest eligible piece in the content, band 1 the strongest. Ties break on id so
        /// the pick stays stable across content reloads.</summary>
        private static ItemDef BestForBand(IEnumerable<ItemDef> candidates, float band)
        {
            var ranked = candidates.OrderBy(MetaHeuristics.Score).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
            if (ranked.Count == 0) return null;
            int index = (int)Math.Round(band * (ranked.Count - 1), MidpointRounding.AwayFromZero);
            return ranked[Math.Max(0, Math.Min(ranked.Count - 1, index))];
        }

        /// <summary>Fills every bar slot (all five are unlocked from the start, §4.7 — <see cref="Progression.UnlockedSlots"/>
        /// never gates them) with the hero's own class abilities, a contiguous slice of the class list ranked by
        /// <see cref="Abilities.Price"/> that slides from the cheapest/earliest abilities at band 0 to the priciest
        /// at band 1 — the same "priciest affordable" idea <see cref="CampRoutine.BestLearnable"/> applies one slot
        /// at a time at Camp, just resolved for the whole bar at once since a synthetic party has no Known list that
        /// grows battle by battle. A class with fewer than <see cref="Progression.MaxBarSlots"/> usable abilities
        /// (rare) parks the camp's stopgap potion in whatever slots are left, exactly like a real hero's unused slots.</summary>
        private static List<string> BuildBar(ContentDb db, ClassDef def, float band)
        {
            var usable = def.Abilities.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(db.TryAbility).Where(a => a != null && Abilities.IsHeroUsable(a))
                .OrderBy(Abilities.Price).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();

            var bar = new List<string>();
            int windowSize = Math.Min(Progression.MaxBarSlots, usable.Count);
            if (windowSize > 0)
            {
                int start = (int)Math.Round(band * (usable.Count - windowSize), MidpointRounding.AwayFromZero);
                start = Math.Max(0, Math.Min(usable.Count - windowSize, start));
                for (int i = 0; i < windowSize; i++) bar.Add(usable[start + i].Id);
            }
            while (bar.Count < Progression.MaxBarSlots) bar.Add("item:" + CampRoutine.StopgapPotion);
            return bar;
        }
    }
}
