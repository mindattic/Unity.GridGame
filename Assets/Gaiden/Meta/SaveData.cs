using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Meta
{
    /// <summary>
    /// One save slot (bible §15). Plain data, JSON-serialized by the app's save store.
    /// Bump <see cref="CurrentVersion"/> and add a step to <see cref="SaveMigrations"/> when the shape changes.
    /// </summary>
    /// <summary>An expedition in progress: a theme's battles fought in order, wounds and mana carried between them (§22.5).</summary>
    public sealed class ExpeditionSave
    {
        public string ThemeId;
        /// <summary>Index (into the theme's stages) of the next battle.</summary>
        public int Next;
        /// <summary>Orbs left in the bank at the end of the last battle; they start the next one.</summary>
        public int CarriedOrbs;
        /// <summary>Coins earned so far on this expedition (the completion bonus is a share of it).</summary>
        public int Coins;
    }

    public sealed class SaveData
    {
        public const int CurrentVersion = 5;

        public int Version = CurrentVersion;
        public string Name = "Lightbearer";
        public DateTime CreatedUtc = DateTime.UtcNow;
        public DateTime UpdatedUtc = DateTime.UtcNow;
        public double PlaySeconds;

        public int Gold;
        public int LifetimeCoins;
        /// <summary>Campaign index of the furthest cleared stage (-1 = none).</summary>
        public int HighestClearedStage = -1;
        public HashSet<string> ClearedStages = new HashSet<string>();
        public HashSet<string> SeenCrawls = new HashSet<string>();
        /// <summary>Story flags set by dialogue (§27.1, GG-T117) — the same idea as SeenCrawls. A joined Camp NPC
        /// is the flag "npc:&lt;id&gt;" (JoinNpc), which ClearFlag can't touch.</summary>
        public HashSet<string> Flags = new HashSet<string>();
        /// <summary>Dialogue ids played to the end: story beats never replay; Talk dialogues marked Once don't either.</summary>
        public HashSet<string> SeenDialogues = new HashSet<string>();
        public string LastStage;

        public List<HeroSave> Roster = new List<HeroSave>();
        public List<string> Party = new List<string>();
        public Dictionary<string, int> Inventory = new Dictionary<string, int>();
        public int Recruits;
        /// <summary>Caravan upgrade id → level bought (§25.12).</summary>
        public Dictionary<string, int> Caravan = new Dictionary<string, int>();
        /// <summary>Crafting station id ("blacksmith"/"alchemist") → accumulated XP (§24.6.1, GG-T35).</summary>
        public Dictionary<string, int> StationXp = new Dictionary<string, int>();
        /// <summary>Guest allies lost for good (§25.11) — only ever recorded or honored with <see cref="Permadeath"/> on
        /// (a future Ironman mode); in the normal game it stays empty, and entries from older saves are ignored (GG-T114).</summary>
        public HashSet<string> LostGuests = new HashSet<string>();
        /// <summary>Recipe ids discovered early, before the crafting station reached their tier — by reading a
        /// recipe scroll or first-ever pickup of the recipe's rarest input (§24.6.2, GG-T36). A recipe whose
        /// tier the station has already reached is always discovered regardless of this set.</summary>
        public HashSet<string> DiscoveredRecipes = new HashSet<string>();
        /// <summary>Spare equipment that has been used: item id → its worn copies (per-instance durability, US-246).</summary>
        public Dictionary<string, List<WornGear>> Worn = new Dictionary<string, List<WornGear>>();
        /// <summary>Shattered/broken gear kept as salvage fodder instead of lost outright: item id → count of
        /// Broken copies. Broken pieces can't be equipped or repaired — only scrapped at the Blacksmith (GG-T41).</summary>
        public Dictionary<string, int> Broken = new Dictionary<string, int>();
        /// <summary>Advances once per finished battle; seeds the Merchant's "Used / Bargain" shelf so it
        /// turns over on every return to camp but replays identically from the same save (GG-T38).</summary>
        public int BargainRotation;
        /// <summary>Chests opened so far (GG-T301): seeds each chest's roll, so a save replays its chests identically.</summary>
        public int ChestsOpened;
        /// <summary>Themes whose campaign milestone reward has been paid (GG-T301, <see cref="ThemeDef.MilestoneReward"/>).</summary>
        public HashSet<string> Milestones = new HashSet<string>();
        public bool TutorialDone;
        /// <summary>Camp stations whose first-use tip has been shown (GG-T173, <see cref="CampTips"/>).</summary>
        public HashSet<string> SeenCampTips = new HashSet<string>();
        /// <summary>A `-testmode` throwaway save (GG-T93): enemy levels follow the party (Progression.StageRecommendedLevel).</summary>
        public bool TestMode;
        /// <summary>New Game+ cycle (0 = first run). Unlocked by clearing the last campaign stage (US-332).</summary>
        public int NewGamePlus;

        /// <summary>The expedition under way (null = at the caravan between expeditions) — §22.5.</summary>
        public ExpeditionSave Expedition;
        public BountySave Bounty = new BountySave();
        /// <summary>Optional side quests (§22.11, GG-T237): under way (with progress) and handed in.</summary>
        public QuestSave Quests = new QuestSave();
        /// <summary>Crucible record (§22.10): best waves survived and runs fought on this save.</summary>
        public CrucibleSave Crucible = new CrucibleSave();
        /// <summary>Mercenary contracts and the hiring board (GG-T233, <see cref="Mercenaries"/>).</summary>
        public MercenarySave Mercenaries = new MercenarySave();
        /// <summary>Party-wide energy spent per stage launch (§25.18, <see cref="RestedPool"/>).</summary>
        public RestedSave Rested = new RestedSave();
        /// <summary>Paid, timed paywall jobs still running (§25.18, <see cref="Paywall"/>).</summary>
        public List<PaywallJob> Jobs = new List<PaywallJob>();
        public Dictionary<string, BestiaryEntry> Bestiary = new Dictionary<string, BestiaryEntry>();
        // Settings no longer live in a save slot (GG-T99): they belong to the player profile's settings.json
        // (PlayerProfiles.cs). An older slot's "Settings" block is ignored on load; LegacySaveMigration reads it once.

        public HeroSave Hero(string classId) => Roster.Find(h => string.Equals(h.ClassId, classId, StringComparison.OrdinalIgnoreCase));

        public int Count(string itemId) => itemId != null && Inventory.TryGetValue(itemId, out var n) ? n : 0;

        public void AddItem(string itemId, int n)
        {
            if (itemId == null || n == 0) return;
            int v = Count(itemId) + n;
            if (v <= 0) Inventory.Remove(itemId); else Inventory[itemId] = v;
            // Worn copies can't outnumber owned copies; when copies leave the bag the most-worn go first (an enchanted
            // copy, GG-T179, last).
            if (Worn.TryGetValue(itemId, out var worn))
            {
                worn.Sort((a, b) => { int e = (b.Enchant != null).CompareTo(a.Enchant != null); return e != 0 ? e : b.Durability.CompareTo(a.Durability); });
                while (worn.Count > Count(itemId)) worn.RemoveAt(worn.Count - 1);
                if (worn.Count == 0) Worn.Remove(itemId);
            }
        }

        public int BrokenCount(string itemId) => itemId != null && Broken.TryGetValue(itemId, out var n) ? n : 0;

        public void AddBroken(string itemId, int n)
        {
            if (itemId == null || n == 0) return;
            int v = BrokenCount(itemId) + n;
            if (v <= 0) Broken.Remove(itemId); else Broken[itemId] = v;
        }
    }

    public sealed class HeroSave
    {
        public string ClassId;
        public int TotalXp;
        /// <summary>Current HP; 0 = full (wounds carry between battles, §15.1).</summary>
        public int Hp;
        public List<string> Known = new List<string>();
        /// <summary>Five ability-bar entries: ability id, "item:id", or null.</summary>
        public List<string> Bar = new List<string> { null, null, null, null, null };
        /// <summary>Unspent skill points (§15.1, GG-T49), earned per level plus milestone bonuses
        /// (<see cref="Formulas.SkillPointsForLevelUp"/>). Spent on skill-tree nodes (GG-T50).</summary>
        public int SkillPoints;
        /// <summary>Skill-tree nodes bought with points, as "treeId/nodeId" (§25.6.3, GG-T53, <see cref="Talents"/>);
        /// the Inn's Respec clears it and refunds their cost. An older save simply has none.</summary>
        public List<string> TreeNodes = new List<string>();
        /// <summary>Learned passives the player has switched on (GG-T301, <see cref="PassiveLoadout"/>), read only once
        /// <see cref="PassivesChosen"/> is set; until then the first <see cref="PassiveLoadout.Slots"/> learned are on.</summary>
        public List<string> ActivePassives = new List<string>();
        /// <summary>The player has picked which learned passives are on (GG-T301). An older save has not.</summary>
        public bool PassivesChosen;
        /// <summary>A domesticated monster (GG-T303, <see cref="Domestication"/>): <see cref="ClassId"/> is an enemy class. It
        /// fights on its own AI, wears no gear, has no skill tree, and grows slowly up to <see cref="Domestication.LevelCap"/>.</summary>
        public bool Domesticated;

        public string WeaponId;
        public int WeaponDurability;
        public int WeaponRepairs;
        /// <summary>Masterwork stat multiplier (§24.6.2, GG-T46); 1 = normal gear.</summary>
        public float WeaponMastery = 1f;
        /// <summary>Armor equipped in Head/Torso/Legs/Gloves/Boots, each with its own durability/repairs/mastery —
        /// the same per-piece tracking Weapon already had (§24.2, GG-T89).</summary>
        public Dictionary<EquipmentSlot, EquippedArmor> Armor = new Dictionary<EquipmentSlot, EquippedArmor>();
        /// <summary>Jewelry equipped in Necklace/Earring1/Earring2/Ring1/Ring2 — plain item ids, no durability
        /// (the same no-wear rule the old Relic slots had, GG-T89).</summary>
        public Dictionary<EquipmentSlot, string> Jewelry = new Dictionary<EquipmentSlot, string>();

        // ── v2-and-earlier save shape, kept only so SaveMigrations can drain them into Armor/Jewelry above.
        // Never read anywhere else — a v3+ save always has these empty/default. ──
        public string ArmorId;
        public int ArmorDurability;
        public int ArmorRepairs;
        public float ArmorMastery = 1f;
        public List<string> Relics = new List<string> { null, null, null };
    }

    /// <summary>One equipped armor piece (§24.2, GG-T89): mirrors <see cref="WornGear"/>'s shape plus the item id,
    /// since (unlike the bag's Worn list, keyed by item id already) a hero's Armor dict is keyed by slot.</summary>
    public sealed class EquippedArmor
    {
        public string ItemId;
        public int Durability;
        public int Repairs;
        public float Mastery = 1f;
        /// <summary>Armor enchant id (armorenchants.json, GG-T179) bound into this piece by the Enchantress; null = none.
        /// It grants its passive while the piece is worn and intact, and is lost if the piece breaks.</summary>
        public string Enchant;
    }

    public sealed class WornGear
    {
        public int Durability;
        public int Repairs;
        /// <summary>Masterwork stat multiplier this specific copy rolled at craft time (§24.6.2, GG-T46); 1 = normal.</summary>
        public float Mastery = 1f;
        /// <summary>Armor enchant this copy carries (GG-T179), kept while it sits in the bag; null = none.</summary>
        public string Enchant;
    }

    public sealed class BountySave
    {
        public string ActiveId;
        public int Progress;
        public HashSet<string> Claimed = new HashSet<string>();
    }

    public sealed class QuestSave
    {
        /// <summary>Quest id → progress (Defeat kills so far; ClearStage 1 once met).</summary>
        public Dictionary<string, int> Active = new Dictionary<string, int>();
        public HashSet<string> Done = new HashSet<string>();
    }

    /// <summary>The save's Crucible record (§22.10). <see cref="Runs"/> also seeds the next run, so a save replays
    /// the same gauntlet until it fights it.</summary>
    public sealed class CrucibleSave
    {
        public int Best;
        public int Runs;
        public int TotalWaves;
    }

    /// <summary>What the save knows about one enemy class (§25.11). How much the Bestiary and the battle inspect
    /// reveal is <see cref="BestiaryKnowledge.Tier"/>, earned by <see cref="Defeated"/>.</summary>
    public sealed class BestiaryEntry
    {
        public bool Seen;
        /// <summary>Retired with the Scan ability (GG-T290). Never set any more; an older save that scanned this enemy
        /// keeps at least the stats tier through <see cref="BestiaryKnowledge.Tier"/>.</summary>
        public bool Scanned;
        public int Defeated;
    }

    /// <summary>The Bestiary's knowledge tiers (§25.11, GG-T290): the more of an enemy the party defeats, the more it
    /// learns. Seen gives its name and description; then Speed, then HP and every base stat, then its elemental
    /// weaknesses and resistances, and last its abilities and how it picks targets.</summary>
    public static class BestiaryKnowledge
    {
        public const int None = 0, Description = 1, Speed = 2, Stats = 3, Elements = 4, Abilities = 5;
        public const int DefeatsForSpeed = 1, DefeatsForStats = 3, DefeatsForElements = 6, DefeatsForAbilities = 10;

        /// <summary>The highest tier <paramref name="e"/> has reached. A pre-GG-T290 Scanned entry counts as at least Stats.</summary>
        public static int Tier(BestiaryEntry e)
        {
            if (e == null) return None;
            int d = Math.Max(0, e.Defeated);
            int t = d >= DefeatsForAbilities ? Abilities : d >= DefeatsForElements ? Elements : d >= DefeatsForStats ? Stats
                : d >= DefeatsForSpeed ? Speed : e.Seen ? Description : None;
            if (e.Scanned && t < Stats) t = Stats;
            return t;
        }

        public static int Tier(SaveData s, string classId) =>
            s?.Bestiary != null && classId != null && s.Bestiary.TryGetValue(classId, out var e) ? Tier(e) : None;

        public static bool Knows(BestiaryEntry e, int tier) => Tier(e) >= tier;
        public static bool Knows(SaveData s, string classId, int tier) => Tier(s, classId) >= tier;

        /// <summary>The defeat count that unlocks <paramref name="tier"/> (0 for Description and below).</summary>
        public static int DefeatsFor(int tier) =>
            tier >= Abilities ? DefeatsForAbilities : tier == Elements ? DefeatsForElements : tier == Stats ? DefeatsForStats : tier == Speed ? DefeatsForSpeed : 0;

        /// <summary>The next tier still to learn, or -1 when everything is known.</summary>
        public static int NextTier(BestiaryEntry e)
        {
            int t = Tier(e);
            return t >= Abilities ? -1 : Math.Max(Speed, t + 1);
        }

        /// <summary>Defeats still needed for the next tier (0 when everything is known).</summary>
        public static int DefeatsToNext(BestiaryEntry e)
        {
            int next = NextTier(e);
            return next < 0 ? 0 : Math.Max(0, DefeatsFor(next) - Math.Max(0, e?.Defeated ?? 0));
        }

        /// <summary>What a tier reveals, as the Bestiary's "Defeat N more to learn …" phrase.</summary>
        public static string Label(int tier) => tier switch
        {
            Speed => "its speed",
            Stats => "its HP and stats",
            Elements => "its weaknesses",
            Abilities => "its abilities",
            _ => "about it",
        };

        /// <summary>Its elemental weaknesses and resistances from the class's multipliers, e.g. "Immune to Poison ·
        /// weak to Fire (×1.5)", or "No elemental weakness or resistance".</summary>
        public static string ElementLine(ClassDef d)
        {
            var parts = new List<string>();
            if (d?.Resistances != null)
                foreach (var kv in d.Resistances)
                {
                    if (Math.Abs(kv.Value - 1f) < 0.001f) continue;
                    parts.Add(kv.Value <= 0f ? $"immune to {kv.Key}"
                        : kv.Value > 1f ? $"weak to {kv.Key} (×{kv.Value:0.##})" : $"resists {kv.Key} (×{kv.Value:0.##})");
                }
            if (parts.Count == 0) return "No elemental weakness or resistance";
            var s = string.Join(" · ", parts);
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>"Defeat 2 more to learn its HP and stats", or null when everything is known.</summary>
        public static string Hint(BestiaryEntry e)
        {
            int next = NextTier(e);
            if (next < 0) return null;
            int n = DefeatsToNext(e);
            return $"Defeat {n} more to learn {Label(next)}.";
        }
    }

    /// <summary>The player's settings — one set per profile, stored in Profiles/&lt;Name&gt;/settings.json through
    /// <see cref="GaidenSettings"/> (GG-T99), not in a save slot.</summary>
    public sealed class SettingsSave
    {
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.85f;
        public bool MuteMusic;
        public bool MuteSfx;
        public bool ColorblindMode;
        public bool ReduceMotion;
        /// <summary>Terra Battle-style drag timer (§2, act-advanced amendment). Off by default.</summary>
        public bool DragTimer;
        /// <summary>Battle animation speed multiplier (1 = normal).</summary>
        public float BattleSpeed = 1f;
        /// <summary>Close-up portrait cut-in on pincers (ported from GridGame2026).</summary>
        public bool PincerCutIns = true;
        /// <summary>Pick a target, then confirm (owner direction 2026-10-11). Off = commit on the pick, for veterans.</summary>
        public bool ConfirmActions = true;
        /// <summary>Menu theme by name (App.Art.Themes: Classic, Cyberpunk, Ember, Verdant, Slate, Dark); unknown/missing names fall back to Classic.</summary>
        public string MenuTheme = "Classic";
        /// <summary>Body font by name (App.Fonts.Presets); unknown/missing names fall back to Classic.</summary>
        public string FontName = "Classic";
        /// <summary>Settings → Text wrapping (GG-T305): false = Word wrap (default), true = Hyphenate (ragged lines and
        /// overlong words break at <see cref="Hyphenation"/> points).</summary>
        public bool Hyphenate;
        /// <summary>The difficulty dial 1..10 (§22.9, GG-T309), 10 by default; applies from the next battle.</summary>
        public DifficultySettings Difficulty = new DifficultySettings();
    }

    /// <summary>
    /// The difficulty dial (owner, GG-T309: "On a 1-10 scale you would start on 10 and let players adjust the game if
    /// they feel it is too easy or hard … the higher the difficulty the greater the rewards"). One number replaces the
    /// §22.9 scaffold's Easy/Normal/Hard/Custom preset and its six inert multipliers; what each level does is
    /// <see cref="DifficultyTable"/>. It is a profile setting (settings.json), so every save slot of the profile plays it.
    /// </summary>
    public sealed class DifficultySettings
    {
        public int Level = DifficultyTable.Default;

        /// <summary>The level an old §22.9 preset becomes. Its multipliers were pinned at 1.0 (inert), so every player
        /// actually fought the designed game — level 10 — except one who chose a lower setting on purpose: Easy → 5, and
        /// a Custom whose enemy HP/damage averaged below ×1 → the level whose enemy side is nearest that average.
        /// Normal, Hard, a Custom at ×1 or above, and anything unknown → 10.</summary>
        public static int FromLegacyPreset(string preset, float enemyHp = 1f, float enemyDamage = 1f)
        {
            switch ((preset ?? "").Trim().ToLowerInvariant())
            {
                case "easy": return 5;
                case "custom":
                    float want = (enemyHp + enemyDamage) / 2f;
                    if (!(want < 1f)) return DifficultyTable.Default;
                    int best = DifficultyTable.Min;
                    float bestGap = float.MaxValue;
                    foreach (var r in DifficultyTable.Rows)
                    {
                        float gap = Math.Abs((r.EnemyHp + r.EnemyDamage) / 2f - want);
                        if (gap < bestGap) { bestGap = gap; best = r.Level; }
                    }
                    return best;
                default: return DifficultyTable.Default;
            }
        }
    }

    public static class SaveMigrations
    {
        /// <summary>Ability ids removed from content; a save still holding one drops it on load.</summary>
        /// <summary>GG-T344: where The Still went into the campaign order, and how many stages it brought (save v5).</summary>
        public const int StillInsertedAt = 12, StillStageCount = 3;

        public static readonly HashSet<string> RetiredAbilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "scan" };

        /// <summary>Upgrades an older save in place. Unknown future versions are rejected. <paramref name="c"/>
        /// is optional (version-shape fixups below don't need content) but drives the GG-T52 proficiency
        /// grandfather clause at the end: without it that step is simply skipped.</summary>
        public static SaveData Migrate(SaveData s, ContentDb c = null)
        {
            if (s == null) return null;
            if (s.Version > SaveData.CurrentVersion) throw new InvalidOperationException($"Save version {s.Version} is newer than this build");
            // v1 is the first shape.
            if (s.Version < 2)
            {
                // GG-T49: skill points didn't exist before v2, so a hero already past level 1 is
                // backfilled once with the points they'd have earned leveling up to here — otherwise
                // every existing save would start the new economy at a permanent deficit.
                foreach (var h in s.Roster)
                    h.SkillPoints += Formulas.SkillPointsForLevelUp(1, Formulas.LevelFromTotalXp(h.TotalXp).level);
                s.Version = 2;
            }
            s.Bounty ??= new BountySave();
            s.Quests ??= new QuestSave();
            s.Quests.Active ??= new Dictionary<string, int>();
            s.Quests.Done ??= new HashSet<string>();
            s.Crucible ??= new CrucibleSave();
            s.Mercenaries ??= new MercenarySave();
            s.Worn ??= new Dictionary<string, List<WornGear>>();
            s.Broken ??= new Dictionary<string, int>();
            s.StationXp ??= new Dictionary<string, int>();
            s.DiscoveredRecipes ??= new HashSet<string>();
            s.Flags ??= new HashSet<string>();
            s.SeenDialogues ??= new HashSet<string>();
            s.SeenCampTips ??= new HashSet<string>();
            s.Rested ??= new RestedSave();
            s.Jobs ??= new List<PaywallJob>();
            foreach (var h in s.Roster) while (h.Bar.Count < 5) h.Bar.Add(null);
            // GG-T290: the Scan ability was retired (the Bestiary now learns from defeats), so it leaves every
            // hero's known list and ability bar. Idempotent.
            foreach (var h in s.Roster)
            {
                h.Known?.RemoveAll(id => RetiredAbilities.Contains(id));
                if (h.Bar != null)
                    for (int i = 0; i < h.Bar.Count; i++)
                        if (h.Bar[i] != null && RetiredAbilities.Contains(h.Bar[i])) h.Bar[i] = null;
            }
            if (s.Version < 3)
            {
                // GG-T89: one Armor slot split into five (Head/Torso/Legs/Gloves/Boots) and Relic1-3 became
                // named jewelry (Necklace/Earring1/Earring2). Best-effort carry-over so nobody's gear vanishes:
                // the old Armor piece becomes Torso (the closest match), and relics fill in order — a relic's
                // own category may not match the slot it lands in, but it still contributes its stats there.
                var jewelrySlots = new[] { EquipmentSlot.Necklace, EquipmentSlot.Earring1, EquipmentSlot.Earring2 };
                foreach (var h in s.Roster)
                {
                    h.Armor ??= new Dictionary<EquipmentSlot, EquippedArmor>();
                    h.Jewelry ??= new Dictionary<EquipmentSlot, string>();
                    if (h.ArmorId != null)
                        h.Armor[EquipmentSlot.Torso] = new EquippedArmor { ItemId = h.ArmorId, Durability = h.ArmorDurability, Repairs = h.ArmorRepairs, Mastery = h.ArmorMastery };
                    for (int i = 0; i < jewelrySlots.Length && i < (h.Relics?.Count ?? 0); i++)
                        if (h.Relics[i] != null) h.Jewelry[jewelrySlots[i]] = h.Relics[i];
                    h.ArmorId = null; h.ArmorDurability = 0; h.ArmorRepairs = 0; h.ArmorMastery = 1f;
                    h.Relics = new List<string>();
                }
                s.Version = 3;
            }
            if (s.Version < 4)
            {
                // GG-T119: Camp NPCs are now met on the journey and their services stay hidden until they join.
                // A save from before that has been using every service all along, so everyone has already joined.
                CampNpcs.JoinAll(s);
                s.Version = 4;
            }
            if (s.Version < 5)
            {
                // GG-T344: The Still (three stages) was inserted between the Anchor Wastes and Halcyon, at campaign index
                // StillInsertedAt. HighestClearedStage is a campaign index, so a save already past the Anchor Wastes moves
                // up by the Still's stage count and keeps the same frontier (its Halcyon/postgame clears stay cleared;
                // the Still opens behind it, replayable like any earlier stage).
                if (s.HighestClearedStage >= StillInsertedAt) s.HighestClearedStage += StillStageCount;
                s.Version = 5;
            }
            foreach (var h in s.Roster)
            {
                h.Armor ??= new Dictionary<EquipmentSlot, EquippedArmor>();
                h.Jewelry ??= new Dictionary<EquipmentSlot, string>();
            }
            // GG-T52 (gear-proficiency gating): a save written before this shipped may already have a hero
            // wearing gear their class isn't nominally trained for (there was no gate to stop them). Rather
            // than strip currently-equipped gear out from under a returning player, auto-grant the matching
            // proficiency-unlock passive so Equipment.Fits keeps considering them proficient. Re-running this
            // on an already-fine save is a no-op (every check is already satisfied). No-op entirely without a
            // ContentDb (c == null), e.g. the version-only unit tests above that never touch gear/content.
            if (c != null)
                foreach (var h in s.Roster)
                {
                    GrandfatherGearProficiency(c, h, h.WeaponId);
                    foreach (var piece in h.Armor.Values) GrandfatherGearProficiency(c, h, piece.ItemId);
                }
            return s;
        }

        /// <summary>GG-T52: if <paramref name="itemId"/> is equipped but <paramref name="h"/> isn't nominally
        /// proficient in it, grant whichever proficiency-unlock passive would explain that (idempotent — a
        /// hero who's already proficient, or has nothing in that slot, is untouched).</summary>
        private static void GrandfatherGearProficiency(ContentDb c, HeroSave h, string itemId)
        {
            var item = itemId != null ? c.TryItem(itemId) : null;
            if (item == null) return;
            if (item.Slot == EquipmentSlot.Weapon && item.WeaponClass != WeaponClass.Simple &&
                !Progression.EffectiveWeaponProficiencies(c, h).Contains(item.WeaponClass))
            {
                var passive = item.WeaponClass == WeaponClass.Arcane ? "arcane_focus_proficiency" : "martial_weapons_proficiency";
                if (!h.Known.Contains(passive)) h.Known.Add(passive);
            }
            else if (Array.IndexOf(BattleEngine.ArmorSlots, item.Slot) >= 0 &&
                     item.ArmorWeight > Progression.EffectiveArmorProficiency(c, h))
            {
                if (!h.Known.Contains("heavy_armor_proficiency")) h.Known.Add("heavy_armor_proficiency");
            }
        }
    }
}
