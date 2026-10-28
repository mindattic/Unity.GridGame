using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Meta
{
    public readonly struct Result
    {
        public readonly bool Ok;
        public readonly string Message;
        private Result(bool ok, string message) { Ok = ok; Message = message; }
        public static Result Success(string message = null) => new Result(true, message);
        public static Result Fail(string message) => new Result(false, message);
        public override string ToString() => (Ok ? "OK " : "FAIL ") + Message;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // New game / progression
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Progression
    {
        /// <summary>
        /// The default starting party. Owner 2026-10-11 made it five ("a 5 player team seems about right for max");
        /// owner 2026-10-19 (GG-T118) shrank the start to a small party — Tank, Striker, Healer — with the rest of
        /// the five joining at story beats (dialogue JoinHero), on top of rescued guests and the Summon Circle.
        /// </summary>
        public static readonly string[] StartingRoster = { "paladin", "barbarian", "cleric" };
        /// <summary>Heroes a new game starts with (character select locks in this many; GG-T118).</summary>
        public const int StartingPartySize = 3;
        public const int StartingGold = 200;
        public const int MaxPartySize = 5;
        public const int MaxBarSlots = 5;
        /// <summary>
        /// Slot unlocks (§4.7). Amended 2026-10-11 (owner: "give each character 2 stacks of full life potions and unlock
        /// the slots to put them in"): every slot is open from the start, so the thresholds no longer gate anything.
        /// </summary>
        public static readonly int[] UnlockThresholds = { };
        public const int StartingSlots = MaxBarSlots;

        public static SaveData NewGame(ContentDb c, string name) => NewGame(c, name, StartingRoster);

        /// <summary>A custom party (e.g. from the character-select screen) in place of the default <see cref="StartingRoster"/>.</summary>
        public static SaveData NewGame(ContentDb c, string name, IReadOnlyList<string> party)
        {
            var s = new SaveData { Name = string.IsNullOrWhiteSpace(name) ? "Lightbearer" : name.Trim(), Gold = StartingGold };
            foreach (var id in party)
            {
                if (c.TryClass(id) == null) continue;
                var h = NewHero(c, id);
                s.Roster.Add(h);
                s.Party.Add(id);
            }
            // Every item slot on a starting bar is a full stack (two X-Potion stacks of 5 per hero).
            foreach (var h in s.Roster)
                foreach (var e in h.Bar)
                    if (e != null && e.StartsWith("item:")) s.AddItem(e.Substring(5), Math.Max(1, c.TryItem(e.Substring(5))?.MaxStack ?? 3));
            return s;
        }

        public static HeroSave NewHero(ContentDb c, string classId)
        {
            var def = c.Class(classId);
            var h = new HeroSave { ClassId = def.Id };
            int i = 0;
            foreach (var entry in def.DefaultLoadout)
            {
                // Abilities past the bar are still known (they can be slotted later); items may repeat (two stacks).
                if (!entry.StartsWith("item:") && !h.Known.Contains(entry)) h.Known.Add(entry);
                if (i < MaxBarSlots) h.Bar[i++] = entry;
            }
            return h;
        }

        public static int Level(HeroSave h) => Formulas.LevelFromTotalXp(h.TotalXp).level;

        public static int UnlockedSlots(SaveData s)
        {
            int n = StartingSlots;
            foreach (var t in UnlockThresholds) if (s.HighestClearedStage >= t) n++;
            return Math.Min(MaxBarSlots, n);
        }

        public static bool IsStageUnlocked(SaveData s, int campaignIndex) => campaignIndex <= s.HighestClearedStage + 1;

        public static int RecommendedLevel(int campaignIndex) => campaignIndex + 1;

        /// <summary>The party's level: the average of its heroes' levels, rounded (min 1). Small and shared on
        /// purpose — anything that scales enemies to the party (Test Mode, GG-T93; the Crucible) uses this.</summary>
        public static int PartyLevel(SaveData s)
        {
            var levels = s.Party.Select(s.Hero).Where(h => h != null).Select(Level).ToList();
            return levels.Count == 0 ? 1 : Math.Max(1, (int)Math.Round(levels.Average(), MidpointRounding.AwayFromZero));
        }

        /// <summary>A stage's recommended level for this save (the floor its enemies spawn at): the party's
        /// level on a Test Mode save (GG-T93, so difficulty rises as the party levels), otherwise the
        /// stage's LevelOverride or its flattened campaign index.</summary>
        public static int StageRecommendedLevel(ContentDb c, SaveData s, StageDef stage)
        {
            if (s.TestMode) return PartyLevel(s);
            int index = c.CampaignStages().FindIndex(x => x.Id == stage.Id);
            return stage.LevelOverride ?? (index >= 0 ? RecommendedLevel(index) : 1);
        }

        public static bool CanStartNewGamePlus(ContentDb c, SaveData s)
        {
            var all = c.CampaignStages();
            return all.Count > 0 && s.HighestClearedStage >= all.Count - 1;
        }

        /// <summary>New Game+ (US-332): replay the campaign from the start with everything you own; enemies +10 levels per cycle.</summary>
        public static Result StartNewGamePlus(ContentDb c, SaveData s)
        {
            if (!CanStartNewGamePlus(c, s)) return Result.Fail("Clear the whole campaign first");
            s.NewGamePlus++;
            s.HighestClearedStage = -1;
            s.ClearedStages.Clear();
            s.SeenCrawls.Clear();
            s.SeenDialogues.Clear(); // story beats replay with the crawls; flags (joined NPCs) carry over
            return Result.Success($"New Game+ {s.NewGamePlus} — enemies grow stronger");
        }

        public static Unit Preview(ContentDb c, HeroSave h)
        {
            var spec = BattleFactory.SpecFor(c, h);
            var setup = new BattleSetup
            {
                Stage = new StageDef { Id = "_preview", IsTest = true, Waves = { new WaveDef() } },
                Party = { spec },
            };
            var state = BattleEngine.Create(c, setup, new List<BattleEvent>());
            return state.Units.First(u => u.IsHero);
        }

        // ── Build screen read models (GG-T48): every number here is derived from data the hero already has
        // (Known, its class, the tree content GG-T50 shipped) — none of it invents a spend ledger ahead of the
        // real one (§25.6: spending skill points against a tree is still follow-up work, GG-T49/50).

        /// <summary>Proficiency-unlock passives staged by GG-T74 for GG-T52's gear gating. Granted by a tree
        /// node like any other, but GG-T52 hasn't landed yet, so nothing can unlock one through play today —
        /// <see cref="KnownProficiencies"/> simply reports whichever of these a hero's <c>Known</c> already
        /// has, ready to light up once GG-T52 wires the gate.</summary>
        public static readonly string[] ProficiencyPassiveIds =
            { "heavy_armor_proficiency", "martial_weapons_proficiency", "arcane_focus_proficiency" };

        /// <summary>How many of each skill tree's points a hero's current <c>Known</c> list would represent,
        /// highest first: for every <see cref="SkillTreeDef"/>, the sum of every node's <c>Cost</c> whose
        /// <c>Grants</c> ability the hero already knows (its default loadout, §25.6.1's last paragraph, plus
        /// anything gold-Learned since). Trees with zero matching nodes are left out. This is a read-only
        /// snapshot of "what a hero's kit looks like mapped onto the tree map" — not a spend ledger, since
        /// spending points against a tree doesn't exist yet (§25.6).</summary>
        public static List<(string TreeId, string TreeName, int Points)> TreeInvestment(ContentDb c, HeroSave h)
        {
            var result = new List<(string, string, int)>();
            foreach (var t in c.SkillTrees.Values)
            {
                int points = t.Nodes.Where(n => h.Known.Contains(n.Grants)).Sum(n => n.Cost);
                if (points > 0) result.Add((t.Id, t.Name, points));
            }
            return result.OrderByDescending(x => x.Item3).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Friendly names of every proficiency-unlock passive (<see cref="ProficiencyPassiveIds"/>)
        /// the hero already knows.</summary>
        public static List<string> KnownProficiencies(ContentDb c, HeroSave h) =>
            ProficiencyPassiveIds.Where(h.Known.Contains).Select(id => c.TryAbility(id)?.Name).Where(n => n != null).ToList();

        /// <summary>Weapon classes this hero can currently wield (GG-T52, gear-proficiency gating): every hero
        /// can always use <see cref="WeaponClass.Simple"/>; Martial/Arcane come from the class's own home-tree
        /// training (<see cref="ClassDef.WeaponProficiencies"/>) or from having learned that class's unlock
        /// passive (<c>martial_weapons_proficiency</c> / <c>arcane_focus_proficiency</c> — <see cref="ProficiencyPassiveIds"/>),
        /// e.g. a cleric earning Martial Weapons or a warrior earning Arcane Focus.</summary>
        public static HashSet<WeaponClass> EffectiveWeaponProficiencies(ContentDb c, HeroSave h)
        {
            var cls = c.TryClass(h.ClassId);
            var set = new HashSet<WeaponClass> { WeaponClass.Simple };
            if (cls != null) foreach (var w in cls.WeaponProficiencies) set.Add(w);
            if (h.Known.Contains("martial_weapons_proficiency")) set.Add(WeaponClass.Martial);
            if (h.Known.Contains("arcane_focus_proficiency")) set.Add(WeaponClass.Arcane);
            return set;
        }

        /// <summary>Heaviest armor this hero can currently wear (GG-T52): the class's own training
        /// (<see cref="ClassDef.ArmorProficiency"/>), bumped straight to Heavy if Heavy Armor Proficiency is
        /// known — the passive's own text ("regardless of class") skips Medium on purpose, matching how
        /// Light and Medium are never gated behind any passive today.</summary>
        public static ArmorWeight EffectiveArmorProficiency(ContentDb c, HeroSave h)
        {
            var cls = c.TryClass(h.ClassId);
            var max = cls?.ArmorProficiency ?? ArmorWeight.Medium;
            if (h.Known.Contains("heavy_armor_proficiency") && max < ArmorWeight.Heavy) max = ArmorWeight.Heavy;
            return max;
        }

        /// <summary>A one-line "what this hero is becoming" summary for the Build screen, e.g.
        /// "Knight · Guardian 7 / Cleric 3 — a tank who heals". Built only from the class name, its
        /// <see cref="TreeInvestment"/> and its <see cref="ClassDef.Role"/> — never an authored string that
        /// could drift from the numbers behind it.</summary>
        public static string BuildSummary(ContentDb c, HeroSave h)
        {
            var def = c.Class(h.ClassId);
            var trees = TreeInvestment(c, h);
            string treePart = trees.Count == 0 ? "no tree investment yet" : string.Join(" / ", trees.Take(3).Select(t => $"{t.TreeName} {t.Points}"));
            string tail = RoleWord(def.Role);
            const int crossTreeFlavorThreshold = 3;
            var cross = trees.FirstOrDefault(t => !string.Equals(t.TreeId, def.HomeTree, StringComparison.OrdinalIgnoreCase));
            if (cross.TreeId != null && cross.Points >= crossTreeFlavorThreshold)
            {
                string verb = TreeVerb(cross.TreeId);
                if (!string.IsNullOrEmpty(verb)) tail = $"{tail} who {verb}";
            }
            return $"{def.Name} · {treePart} — {tail}";
        }

        private static string RoleWord(string role)
        {
            switch (role)
            {
                case "Tank": return "a tank";
                case "Healer": return "a healer";
                case "Mage": return "a mage";
                case "Striker": return "a striker";
                case "Support": return "a support";
                case "Ranged": return "a ranged attacker";
                default: return "a hero";
            }
        }

        /// <summary>The role a tree's own branches serve (§25.6.1's one-tree-per-role table), as a verb for
        /// the "…who heals" tail of <see cref="BuildSummary"/>.</summary>
        private static string TreeVerb(string treeId)
        {
            switch ((treeId ?? "").ToLowerInvariant())
            {
                case "guardian": return "tanks";
                case "warrior": return "strikes";
                case "ranger": return "shoots";
                case "mage": return "casts";
                case "cleric": return "heals";
                case "alchemist": return "supports";
                default: return null;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Battle bridge: save → BattleSetup, BattleResults → save (§22.1)
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class BattleReport
    {
        public bool Victory;
        public bool Escaped;
        /// <summary>A voluntary pause-menu Retreat (FFIV-style partial loss, §22.2) — distinct from Defeat.</summary>
        public bool Retreated;
        public string StageId;
        /// <summary>The stage's name and theme as fought, copied at commit so the results flow never has to look the
        /// stage up again: a generated stage (a harness skirmish, a Crucible run) can be pruned from the ContentDb
        /// while the level-up and results screens are still showing it (GG-T127).</summary>
        public string StageName;
        public string StageTheme;
        public int Gold;
        public int XpEach;
        public Dictionary<string, int> Items = new Dictionary<string, int>();
        public List<(string classId, int fromLevel, int toLevel)> LevelUps = new List<(string, int, int)>();
        public bool FirstClear;
        public string UnlockedStage;
        public int NewSlots;
        public string BountyProgress;
        /// <summary>Loot past an item's stack limit is sold on the spot (inventory policy, US-245).</summary>
        public int AutoSold;
        public bool CampaignComplete;
        // Expedition (§22.5)
        public string ExpeditionName;
        public int ExpeditionStep;      // battles won so far on this expedition
        public int ExpeditionLength;
        public bool ExpeditionComplete;
        public bool ExpeditionFailed;
        public int ExpeditionBonus;
        public int CarriedOrbs;
        public string NextStageId;
        // Guests (§25.11)
        public List<string> GuestsJoined = new List<string>();
        /// <summary>Guests down at battle's end — not lost, they'll be met again (GG-T114).</summary>
        public List<string> GuestsFell = new List<string>();
        /// <summary>Guests lost for good — only ever filled with <see cref="Permadeath"/> on (never in the normal game).</summary>
        public List<string> GuestsLost = new List<string>();
        // Crucible (§22.10)
        public bool Crucible;
        /// <summary>Waves fully cleared before the party fell or left.</summary>
        public int CrucibleWaves;
        /// <summary>The save's best waves-survived after this run.</summary>
        public int CrucibleBest;
        public bool CrucibleNewBest;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Expeditions (§22.5): a theme's battles in order; wounds and mana carry; no inn until it ends
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Expeditions
    {
        /// <summary>Share of the expedition's coins paid as a completion bonus.</summary>
        public const float CompletionBonus = 0.5f;

        public static ThemeDef Theme(ContentDb c, string themeId) => c.Campaign.Themes.Find(t => t.Id == themeId);

        /// <summary>The inn is open between expeditions — or on the road once the caravan has a Medic Wagon.</summary>
        public static bool InnOpen(SaveData s) => s.Expedition == null || CaravanUpgrades.Level(s, "medic_wagon") > 0;

        /// <summary>A battle can start if it is the next one of the active expedition, or the first battle of a theme when none is active.</summary>
        public static bool CanLaunch(ContentDb c, SaveData s, string stageId, out string why)
        {
            why = null;
            var theme = c.ThemeOf(stageId);
            if (theme == null) return true; // test/standalone stages
            int idx = theme.Stages.IndexOf(stageId);
            if (s.Expedition != null)
            {
                if (s.Expedition.ThemeId != theme.Id) { why = $"Finish (or abandon) the {Theme(c, s.Expedition.ThemeId)?.Name} expedition first"; return false; }
                if (idx != s.Expedition.Next) { why = $"Next up: battle {s.Expedition.Next + 1} of {theme.Stages.Count}"; return false; }
                return true;
            }
            if (idx != 0) { why = "Expeditions start at battle 1"; return false; }
            return true;
        }

        /// <summary>Called when a battle is launched: starts an expedition on its theme's first battle.</summary>
        public static void Begin(ContentDb c, SaveData s, string stageId)
        {
            var theme = c.ThemeOf(stageId);
            if (theme == null || s.Expedition != null) return;
            if (theme.Stages.IndexOf(stageId) == 0) s.Expedition = new ExpeditionSave { ThemeId = theme.Id };
        }

        public static int StartOrbs(ContentDb c, SaveData s, string stageId)
        {
            var theme = c.ThemeOf(stageId);
            return s.Expedition != null && theme != null && s.Expedition.ThemeId == theme.Id ? s.Expedition.CarriedOrbs : 0;
        }

        public static Result Abandon(SaveData s)
        {
            if (s.Expedition == null) return Result.Fail("No expedition under way");
            s.Expedition = null;
            return Result.Success("Expedition abandoned — the caravan turns back");
        }

        public static void AfterBattle(ContentDb c, SaveData s, BattleReport rep, BattleState battle)
        {
            var theme = c.ThemeOf(battle.Stage.Id);
            if (theme == null || s.Expedition == null || s.Expedition.ThemeId != theme.Id) return;
            rep.ExpeditionName = theme.Name;
            rep.ExpeditionLength = theme.Stages.Count;
            if (!rep.Victory)
            {
                rep.ExpeditionFailed = true;
                rep.ExpeditionStep = s.Expedition.Next;
                s.Expedition = null;
                return;
            }
            s.Expedition.Next++;
            s.Expedition.Coins += rep.Gold;
            s.Expedition.CarriedOrbs = battle.Bank.Count;
            rep.ExpeditionStep = s.Expedition.Next;
            rep.CarriedOrbs = battle.Bank.Count;
            if (s.Expedition.Next >= theme.Stages.Count)
            {
                rep.ExpeditionComplete = true;
                rep.ExpeditionBonus = (int)Math.Round(s.Expedition.Coins * (CompletionBonus + CaravanUpgrades.Value(c, s, "strongbox")));
                s.Gold += rep.ExpeditionBonus;
                rep.Gold += rep.ExpeditionBonus;
                foreach (var id in s.Party) { var h = s.Hero(id); if (h != null) h.Hp = 0; } // the caravan makes camp: everyone rests
                s.Expedition = null;
                rep.CarriedOrbs = 0;
            }
            else rep.NextStageId = theme.Stages[s.Expedition.Next];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Caravan upgrades (§25.12): the long-run gold sink that changes how expeditions play
    // ─────────────────────────────────────────────────────────────────────────────

    public static class CaravanUpgrades
    {
        public static int Level(SaveData s, string id) => s.Caravan.TryGetValue(id, out var n) ? n : 0;

        /// <summary>The current level's value, or <paramref name="none"/> when not bought.</summary>
        public static float Value(ContentDb c, SaveData s, string id, float none = 0f)
        {
            int lv = Level(s, id);
            if (lv <= 0 || !c.Caravan.TryGetValue(id, out var def) || def.Levels.Count == 0) return none;
            return def.Levels[Math.Min(lv, def.Levels.Count) - 1].Value;
        }

        public static CaravanLevel NextLevel(ContentDb c, SaveData s, string id) =>
            c.Caravan.TryGetValue(id, out var def) && Level(s, id) < def.Levels.Count ? def.Levels[Level(s, id)] : null;

        public static Result Buy(ContentDb c, SaveData s, string id)
        {
            if (!c.Caravan.TryGetValue(id, out var def)) return Result.Fail("Unknown upgrade");
            var next = NextLevel(c, s, id);
            if (next == null) return Result.Fail("Fully upgraded");
            if (s.Gold < next.Cost) return Result.Fail("Not enough gold");
            s.Gold -= next.Cost;
            s.Caravan[id] = Level(s, id) + 1;
            return Result.Success($"{def.Name}: {next.Text}");
        }
    }

    public static class BattleFactory
    {
        public static HeroSpec SpecFor(ContentDb c, HeroSave h)
        {
            var spec = new HeroSpec
            {
                ClassId = h.ClassId, Level = Progression.Level(h), Hp = h.Hp,
                WeaponId = h.WeaponId, WeaponDurability = h.WeaponDurability, WeaponMastery = h.WeaponMastery,
            };
            if (h.WeaponId != null) spec.EquippedItemIds.Add(h.WeaponId);
            foreach (var kv in h.Armor)
            {
                if (kv.Value?.ItemId == null) continue;
                spec.Armor[kv.Key] = new ArmorPieceSpec { ItemId = kv.Value.ItemId, Durability = kv.Value.Durability, Mastery = kv.Value.Mastery, Enchant = kv.Value.Enchant };
                spec.EquippedItemIds.Add(kv.Value.ItemId);
            }
            foreach (var id in h.Jewelry.Values) if (id != null) spec.EquippedItemIds.Add(id);
            spec.Bar = new List<string>(h.Bar);
            return spec;
        }

        public static BattleSetup Build(ContentDb c, SaveData s, string stageId, int seed) => Build(c, s, c.Stage(stageId), seed);

        public static BattleSetup Build(ContentDb c, SaveData s, StageDef stage, int seed)
        {
            bool crucible = Crucible.IsStage(stage.Id);
            var setup = new BattleSetup
            {
                Stage = stage,
                Seed = seed,
                UnlockedSlots = Progression.UnlockedSlots(s),
                // A Crucible run authors every wave's level from the party's own (§22.10), so neither the campaign
                // curve nor New Game+'s step may lift it further.
                RecommendedLevel = crucible ? 1 : Progression.StageRecommendedLevel(c, s, stage),
                NewGamePlus = crucible ? 0 : s.NewGamePlus,
                StartOrbs = Expeditions.StartOrbs(c, s, stage.Id) + (int)CaravanUpgrades.Value(c, s, "mana_casks"),
            };
            foreach (var h in s.Roster) setup.ExcludedGuests.Add(h.ClassId);
            foreach (var g in s.LostGuests) if (Permadeath.IsLost(s, g)) setup.ExcludedGuests.Add(g);
            setup.Permadeath = Permadeath.Enabled(s);
            foreach (var id in s.Party)
            {
                var h = s.Hero(id);
                if (h == null) continue;
                var spec = SpecFor(c, h);
                foreach (var kv in s.Inventory) spec.ItemCounts[kv.Key] = kv.Value;
                setup.Party.Add(spec);
            }
            return setup;
        }

        /// <summary>Commits a finished battle into the save (XP, gold, loot, wounds, durability, kills, bestiary, unlocks).</summary>
        public static BattleReport Commit(ContentDb c, SaveData s, BattleState battle)
        {
            if (Crucible.IsStage(battle.Stage.Id)) return Crucible.Commit(c, s, battle);
            var r = battle.Results;
            var rep = new BattleReport
            {
                Victory = battle.Phase == BattlePhase.Victory,
                Escaped = battle.Phase == BattlePhase.Escaped,
                Retreated = battle.Phase == BattlePhase.Retreated,
                StageId = battle.Stage.Id,
                StageName = battle.Stage.Name,
                StageTheme = battle.Stage.Theme,
            };
            int slotsBefore = Progression.UnlockedSlots(s);
            Bargains.Rotate(s);

            foreach (var kv in r.ItemsConsumed) s.AddItem(kv.Key, -kv.Value);
            foreach (var id in r.Seen) Entry(s, id).Seen = true;
            foreach (var id in r.Scanned) Entry(s, id).Scanned = true;

            if (rep.Victory)
            {
                rep.Gold = r.Coins;
                s.Gold += r.Coins;
                s.LifetimeCoins += r.Coins;
                GrantItems(c, s, rep, r.Items);
                foreach (var kv in r.Kills) Entry(s, kv.Key).Defeated += kv.Value;

                var heroes = battle.Units.Where(u => u.IsHero).ToList();
                rep.XpEach = heroes.Count > 0 ? r.Xp : 0;
                foreach (var u in heroes)
                {
                    var h = s.Hero(u.Def.Id);
                    if (h == null) continue;
                    int before = Progression.Level(h);
                    h.TotalXp += rep.XpEach;
                    int after = Progression.Level(h);
                    if (after > before) rep.LevelUps.Add((h.ClassId, before, after));
                    h.SkillPoints += Formulas.SkillPointsForLevelUp(before, after);
                    // Wounds carry; a hero who fell revives at 1 HP; a level-up refreshes to full.
                    h.Hp = after > before ? 0 : (u.Alive ? (u.Hp >= u.MaxHp ? 0 : u.Hp) : 1);
                    SyncGear(s, h, u, r);
                }
                // Shattered weapons and broken armor become salvage-only Broken fragments instead of
                // vanishing outright (GG-T41) — can't be equipped or repaired, only scrapped for materials.
                KeepBrokenFragments(s, r);

                int index = c.CampaignStages().FindIndex(x => x.Id == battle.Stage.Id);
                rep.FirstClear = s.ClearedStages.Add(battle.Stage.Id);
                var all = c.CampaignStages();
                rep.CampaignComplete = all.Count > 0 && index == all.Count - 1;
                if (index > s.HighestClearedStage)
                {
                    s.HighestClearedStage = index;
                    var list = c.CampaignStages();
                    if (index + 1 < list.Count) rep.UnlockedStage = list[index + 1].Name;
                }
                rep.BountyProgress = Bounties.RecordKills(c, s, r.Kills);
            }
            else if (rep.Escaped)
            {
                // Escaping keeps wounds (no defeat reset) and grants nothing.
                foreach (var u in battle.Units.Where(u => u.IsHero))
                {
                    var h = s.Hero(u.Def.Id);
                    if (h != null) h.Hp = u.Alive ? (u.Hp >= u.MaxHp ? 0 : u.Hp) : 1;
                }
            }
            else if (rep.Retreated)
            {
                // FFIV-style partial loss (owner direction 2026-10-11): a voluntary retreat keeps a
                // fraction of this fight's uncommitted gold/loot plus hard-won XP — better than a wipe,
                // but not free. Still resets to full HP like a Defeat (you fall back and recover).
                foreach (var id in s.Party) { var h = s.Hero(id); if (h != null) h.Hp = 0; }
                int keepCoins = (int)Math.Floor(r.Coins * BattleConfig.RetreatKeepFraction);
                rep.Gold = keepCoins;
                s.Gold += keepCoins;
                s.LifetimeCoins += keepCoins;
                var kept = new Dictionary<string, int>();
                foreach (var kv in r.Items)
                {
                    int n = (int)Math.Floor(kv.Value * BattleConfig.RetreatKeepFraction);
                    if (n > 0) kept[kv.Key] = n;
                }
                GrantItems(c, s, rep, kept);
                GrantHardWonXp(s, battle, r, rep);
            }
            else
            {
                foreach (var id in s.Party) { var h = s.Hero(id); if (h != null) h.Hp = 0; } // defeat = retry at full (§22.2)
                GrantHardWonXp(s, battle, r, rep);
            }
            // Guests: standing at a victory (a revived guest counts) → they join the caravan. Down at the end → they're
            // NOT lost (GG-T114): the offer stays open and they turn up again on that stage. Only permadeath — off in the
            // normal game, the hook for a future Ironman mode — makes a fall permanent.
            foreach (var g in battle.Units.Where(u => u.IsGuest))
            {
                if (!g.Alive)
                {
                    if (Permadeath.Enabled(s)) { s.LostGuests.Add(g.Def.Id); rep.GuestsLost.Add(g.Def.Id); }
                    else if (s.Hero(g.Def.Id) == null) rep.GuestsFell.Add(g.Def.Id);
                    continue;
                }
                if (!rep.Victory || s.Hero(g.Def.Id) != null) continue;
                var h = Progression.NewHero(c, g.Def.Id);
                h.TotalXp = Formulas.TotalXpForLevel(g.Level);
                h.SkillPoints += Formulas.SkillPointsForLevelUp(1, g.Level);
                h.Hp = g.Hp >= g.MaxHp ? 0 : g.Hp;
                s.Roster.Add(h);
                if (s.Party.Count < Progression.MaxPartySize) s.Party.Add(h.ClassId);
                rep.GuestsJoined.Add(g.Def.Id);
            }
            Expeditions.AfterBattle(c, s, rep, battle);
            rep.NewSlots = Progression.UnlockedSlots(s) - slotsBefore;
            s.LastStage = battle.Stage.Id;
            s.UpdatedUtc = DateTime.UtcNow;
            return rep;
        }

        /// <summary>Writes one hero's end-of-battle gear back into the save: a bar weapon swap, current weapon/armor
        /// wear, and gear that shattered mid-battle. Shared by Victory and the Crucible (§22.10).</summary>
        internal static void SyncGear(SaveData s, HeroSave h, Unit u, BattleResults r)
        {
            r.BrokenWeapons.TryGetValue(u.Def.Id, out var shatteredWeapons);
            var finalWeapon = r.FinalWeapon.TryGetValue(u.Def.Id, out var fw) ? fw : u.WeaponId;
            // Bag bookkeeping by conservation: hand-before + bar-before = hand-after + bar-after + shattered,
            // and bar weapons live in the bag, so the bag changes by (hand-before − hand-after − shattered).
            // The old pairwise rule (old weapon back unless it shattered, new one out) missed a bar weapon
            // that was swapped in and then shattered while the original was re-equipped from the bar: that
            // copy became a Broken fragment *and* stayed whole in the bag — a duplicate (GG-T70).
            var bagDelta = new Dictionary<string, int>();
            void Delta(string id, int n) { if (id == null) return; bagDelta.TryGetValue(id, out var v); bagDelta[id] = v + n; }
            if (finalWeapon != h.WeaponId) { Delta(h.WeaponId, +1); Delta(finalWeapon, -1); }
            if (shatteredWeapons != null) foreach (var id in shatteredWeapons) Delta(id, -1);
            foreach (var kv in bagDelta) if (kv.Value != 0) s.AddItem(kv.Key, kv.Value);
            if (finalWeapon != h.WeaponId && finalWeapon != null)
            {
                // A bar swap: the hand now holds the bar's weapon (bag already settled above).
                h.WeaponId = finalWeapon;
                h.WeaponRepairs = 0;
                h.WeaponMastery = 1f; // bar backups always enter at factory spec (§24.5), never Masterwork
            }
            if (r.FinalBar.TryGetValue(u.Def.Id, out var bar))
                for (int i = 0; i < bar.Count && i < h.Bar.Count; i++)
                    if (h.Bar[i] != null && h.Bar[i].StartsWith("weapon:")) h.Bar[i] = bar[i];
            if (r.WeaponDurability.TryGetValue(u.Def.Id, out var dur))
            {
                if (dur < 0 && h.WeaponId != null && u.WeaponId == null) { h.WeaponId = null; h.WeaponDurability = 0; h.WeaponRepairs = 0; h.WeaponMastery = 1f; }
                else if (dur >= 0 && h.WeaponId != null) h.WeaponDurability = dur;
            }
            if (r.ArmorDurability.TryGetValue(u.Def.Id, out var armorDur))
                foreach (var slot in BattleEngine.ArmorSlots)
                {
                    if (!armorDur.TryGetValue(slot, out var adur) || !h.Armor.TryGetValue(slot, out var piece)) continue;
                    if (adur < 0 && !u.Gear.ContainsKey(slot)) h.Armor.Remove(slot); // broke mid-battle
                    else if (adur >= 0) piece.Durability = adur; // survived: sync current wear
                }
        }

        internal static void KeepBrokenFragments(SaveData s, BattleResults r)
        {
            foreach (var list in r.BrokenWeapons.Values) foreach (var id in list) s.AddBroken(id, 1);
            foreach (var list in r.BrokenArmors.Values) foreach (var id in list) s.AddBroken(id, 1);
        }

        /// <summary>Grants items into the save, respecting stack caps (overflow auto-sells, US-245). Shared by
        /// Victory's full haul and a voluntary Retreat's partial one.</summary>
        internal static void GrantItems(ContentDb c, SaveData s, BattleReport rep, Dictionary<string, int> items)
        {
            foreach (var kv in items)
            {
                var def = c.TryItem(kv.Key);
                bool firstEver = s.Count(kv.Key) == 0;
                int room = def == null ? kv.Value : Math.Max(0, def.MaxStack - s.Count(kv.Key));
                int keep = Math.Min(room, kv.Value), extra = kv.Value - keep;
                if (keep > 0)
                {
                    s.AddItem(kv.Key, keep);
                    rep.Items[kv.Key] = keep;
                    if (firstEver) RecipeDiscovery.OnItemAcquired(c, s, kv.Key);
                }
                if (extra > 0 && def != null) { int g = extra * Shop.SellPrice(def); s.Gold += g; rep.Gold += g; rep.AutoSold += extra; }
            }
        }

        /// <summary>Hard-won experience (amended 2026-10-11): the XP of every foe felled is kept on a non-victory
        /// end, so each retry of a long gauntlet leaves the party stronger. Shared by Defeat and Retreat.</summary>
        private static void GrantHardWonXp(SaveData s, BattleState battle, BattleResults r, BattleReport rep)
        {
            var heroes = battle.Units.Where(u => u.IsHero && !u.IsGuest).ToList();
            rep.XpEach = heroes.Count > 0 ? r.Xp : 0;
            foreach (var u in heroes)
            {
                var h = s.Hero(u.Def.Id);
                if (h == null || rep.XpEach <= 0) continue;
                int before = Progression.Level(h);
                h.TotalXp += rep.XpEach;
                int after = Progression.Level(h);
                if (after > before) rep.LevelUps.Add((h.ClassId, before, after));
                h.SkillPoints += Formulas.SkillPointsForLevelUp(before, after);
            }
            foreach (var kv in r.Kills) Entry(s, kv.Key).Defeated += kv.Value;
        }

        internal static BestiaryEntry Entry(SaveData s, string id)
        {
            if (!s.Bestiary.TryGetValue(id, out var e)) { e = new BestiaryEntry(); s.Bestiary[id] = e; }
            return e;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Using items at camp (potions on a hero, Tent on everyone)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Camp
    {
        public static bool UsableAtCamp(ItemDef i) => i != null && i.Type == ItemType.Consumable && i.BaseHealing > 0 && !i.Revives;

        public static bool HealsWholeParty(ItemDef i) => i != null && i.OutOfBattleOnly;

        public static Result Use(ContentDb c, SaveData s, string itemId, string heroId)
        {
            var item = c.TryItem(itemId);
            if (!UsableAtCamp(item)) return Result.Fail("Can't use that here");
            if (s.Count(itemId) < 1) return Result.Fail("None left");
            if (HealsWholeParty(item))
            {
                if (Inn.Cost(c, s) == 0) return Result.Fail("Everyone is healthy");
                foreach (var h in s.Roster) h.Hp = 0;
                s.AddItem(itemId, -1);
                return Result.Success("The party is fully rested");
            }
            var hero = s.Hero(heroId);
            if (hero == null) return Result.Fail("Pick a hero");
            string heroName = c.TryClass(hero.ClassId)?.Name ?? hero.ClassId;
            int max = Progression.Preview(c, hero).MaxHp;
            if (hero.Hp <= 0 || hero.Hp >= max) return Result.Fail($"{heroName} is already healthy");
            int healed = Math.Min(max - hero.Hp, item.BaseHealing);
            hero.Hp += healed;
            if (hero.Hp >= max) hero.Hp = 0;
            s.AddItem(itemId, -1);
            return Result.Success($"{heroName} recovers {healed} HP");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Merchant (§25.1)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Shop
    {
        public static List<ItemDef> Stock(ContentDb c) =>
            c.Items.Values.Where(i => i.SoldByVendor && i.BaseCost > 0).OrderBy(i => i.Type).ThenBy(i => i.BaseCost).ThenBy(i => i.Name).ToList();

        public static int SellPrice(ItemDef i) => Math.Max(1, (int)Math.Floor(i.BaseCost * 0.5));

        public static List<ItemDef> Sellable(ContentDb c, SaveData s) =>
            s.Inventory.Where(kv => kv.Value > 0).Select(kv => c.TryItem(kv.Key)).Where(i => i != null && i.BaseCost > 0 && i.Type != ItemType.QuestItem)
                .OrderBy(i => i.Type).ThenBy(i => i.Name).ToList();

        public static int MaxBuy(SaveData s, ItemDef i) => Math.Max(0, i.MaxStack - s.Count(i.Id));

        public static int CartTotal(ContentDb c, Dictionary<string, int> cart, bool selling) =>
            cart.Sum(kv => { var i = c.TryItem(kv.Key); return i == null ? 0 : kv.Value * (selling ? SellPrice(i) : i.BaseCost); });

        public static Result Buy(ContentDb c, SaveData s, Dictionary<string, int> cart)
        {
            int total = CartTotal(c, cart, false);
            if (total <= 0) return Result.Fail("Nothing to buy");
            if (total > s.Gold) return Result.Fail("Not enough gold");
            foreach (var kv in cart)
            {
                var i = c.TryItem(kv.Key);
                if (i == null || kv.Value < 0 || kv.Value > MaxBuy(s, i)) return Result.Fail($"Can't carry more {i?.Name}");
            }
            s.Gold -= total;
            foreach (var kv in cart) s.AddItem(kv.Key, kv.Value);
            return Result.Success($"Bought for {total}g");
        }

        public static Result Sell(ContentDb c, SaveData s, Dictionary<string, int> cart)
        {
            foreach (var kv in cart)
                if (kv.Value < 0 || kv.Value > s.Count(kv.Key)) return Result.Fail("You don't have that many");
            int total = CartTotal(c, cart, true);
            if (total <= 0) return Result.Fail("Nothing to sell");
            foreach (var kv in cart) s.AddItem(kv.Key, -kv.Value);
            s.Gold += total;
            return Result.Success($"Sold for {total}g");
        }
    }

    /// <summary>One rolled offer on the Merchant's "Used / Bargain" shelf (§25.1, US-246).</summary>
    public sealed class BargainOffer
    {
        public ItemDef Item;
        /// <summary>Discounted price (35-60% of <see cref="ItemDef.BaseCost"/>).</summary>
        public int Price;
        /// <summary>Starting durability the piece carries if bought (10-40% of <see cref="ItemDef.Durability"/>).</summary>
        public int Durability;
    }

    /// <summary>
    /// The Merchant's rotating "Used / Bargain" shelf (bible §25.1, US-246): cheap weapons and armor
    /// sold well below new price, but already worn down — a deliberate early-game gamble between
    /// cheap power now and a real chance of shattering mid-combat before it can be repaired.
    /// </summary>
    public static class Bargains
    {
        public const int ShelfSize = 4;
        public const float MinPriceFraction = 0.35f, MaxPriceFraction = 0.60f;
        public const float MinDurabilityFraction = 0.10f, MaxDurabilityFraction = 0.40f;

        private static bool Eligible(ItemDef i) => i.Type == ItemType.Equipment && i.Durability > 0 &&
            (i.Slot == EquipmentSlot.Weapon || Array.IndexOf(BattleEngine.ArmorSlots, i.Slot) >= 0);

        /// <summary>
        /// This rotation's stock. Deterministic from <see cref="SaveData.BargainRotation"/> alone, so it
        /// stays put across repeated calls (reopening the Merchant doesn't reroll it) and replays
        /// identically from the same save, but turns over on <see cref="Rotate"/>.
        /// </summary>
        public static List<BargainOffer> Shelf(ContentDb c, SaveData s)
        {
            var pool = c.Items.Values.Where(Eligible).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
            var offers = new List<BargainOffer>();
            if (pool.Count == 0) return offers;
            var rng = new Random(unchecked((int)((uint)s.BargainRotation * 747796405u + 2891336453u)));
            var remaining = Enumerable.Range(0, pool.Count).ToList();
            int take = Math.Min(ShelfSize, pool.Count);
            for (int i = 0; i < take; i++)
            {
                int pick = rng.Next(remaining.Count);
                var item = pool[remaining[pick]];
                remaining.RemoveAt(pick);
                float priceFrac = MinPriceFraction + (float)rng.NextDouble() * (MaxPriceFraction - MinPriceFraction);
                float durFrac = MinDurabilityFraction + (float)rng.NextDouble() * (MaxDurabilityFraction - MinDurabilityFraction);
                offers.Add(new BargainOffer
                {
                    Item = item,
                    Price = Math.Max(1, (int)Math.Round(item.BaseCost * priceFrac)),
                    Durability = Math.Max(1, (int)Math.Round(item.Durability * durFrac)),
                });
            }
            return offers;
        }

        public static Result Buy(ContentDb c, SaveData s, string itemId)
        {
            var offer = Shelf(c, s).FirstOrDefault(o => o.Item.Id == itemId);
            if (offer == null) return Result.Fail("Not on the shelf");
            if (s.Count(itemId) >= offer.Item.MaxStack) return Result.Fail("Can't carry more");
            if (s.Gold < offer.Price) return Result.Fail("Not enough gold");
            s.Gold -= offer.Price;
            s.AddItem(itemId, 1);
            // Marks the copy as worn on arrival so equipping it pulls this rolled durability, not factory-fresh.
            if (!s.Worn.TryGetValue(itemId, out var list)) s.Worn[itemId] = list = new List<WornGear>();
            list.Add(new WornGear { Durability = offer.Durability, Repairs = 0 });
            return Result.Success($"Bought worn {offer.Item.Name} for {offer.Price}g");
        }

        /// <summary>Turns the shelf over. Called once per finished battle, win or lose (<see cref="BattleFactory.Commit"/>).</summary>
        public static void Rotate(SaveData s) => s.BargainRotation++;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Station progression (§24.6.1, GG-T35): craft/brew/repair/salvage levels up the
    // station that did the work, unlocking the next tier of its own recipe ladder.
    // Deliberately independent of §25.12 caravan upgrades (gold-bought stat bonuses,
    // not recipe gates) so the two never double-gate the same thing.
    // ─────────────────────────────────────────────────────────────────────────────

    public enum StationKind { Blacksmith, Alchemist }

    public static class StationProgress
    {
        public static readonly string[] LevelNames = { "Apprentice", "Journeyman", "Master" };
        /// <summary>Total XP needed to *reach* each level (index 0 = Apprentice, free).</summary>
        private static readonly int[] Thresholds = { 0, 150, 450 };

        public static string Id(StationKind k) => k == StationKind.Blacksmith ? "blacksmith" : "alchemist";

        public static int Xp(SaveData s, StationKind k) => s.StationXp.TryGetValue(Id(k), out var n) ? n : 0;

        public static int Level(SaveData s, StationKind k)
        {
            int xp = Xp(s, k);
            int lvl = 1;
            for (int i = 1; i < Thresholds.Length; i++) if (xp >= Thresholds[i]) lvl = i + 1;
            return lvl;
        }

        public static string LevelName(SaveData s, StationKind k) => LevelNames[Level(s, k) - 1];
        public static string TierName(int tier) => LevelNames[Math.Max(1, Math.Min(tier, LevelNames.Length)) - 1];

        public static void Gain(SaveData s, StationKind k, int amount)
        {
            if (amount <= 0) return;
            s.StationXp[Id(k)] = Xp(s, k) + amount;
        }

        public static bool IsUnlocked(SaveData s, StationKind k, int tier) => Level(s, k) >= tier;

        /// <summary>"Journeyman — 40/450 XP to Master", or "Master — every tier unlocked" when maxed.</summary>
        public static string HeaderText(SaveData s, StationKind k)
        {
            int lvl = Level(s, k);
            if (lvl >= LevelNames.Length) return $"{LevelNames[lvl - 1]} — every recipe tier unlocked";
            return $"{LevelNames[lvl - 1]} — {Xp(s, k)}/{Thresholds[lvl]} XP to {LevelNames[lvl]}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Blacksmith & Alchemist crafting (§24.6, §25.2–25.3)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Crafting
    {
        public const int CraftXp = 15;
        public const int SalvageXp = 8;

        // ── Masterwork variants (§24.6.2, GG-T46) ────────────────────────────────
        /// <summary>Chance a Forge/Upgrade craft rolls Masterwork, before party Luck and station level…</summary>
        public const float MasterworkBaseChance = 0.05f;
        /// <summary>…plus this per point of the active party's combined Luck…</summary>
        public const float MasterworkChancePerLuck = 0.002f;
        /// <summary>…plus this per Blacksmith station level above Apprentice…</summary>
        public const float MasterworkChancePerStationLevel = 0.05f;
        /// <summary>…capped here.</summary>
        public const float MasterworkMaxChance = 0.35f;
        public const float MasterworkStatBonusMin = 0.10f;
        public const float MasterworkStatBonusMax = 0.20f;

        private static int masterworkSeed = Environment.TickCount;

        public static StationKind StationFor(CraftStation station) => station == CraftStation.Forge ? StationKind.Blacksmith : StationKind.Alchemist;

        public static List<RecipeDef> Recipes(ContentDb c, CraftStation station) =>
            c.Recipes.Values.Where(r => r.Station == station && c.TryItem(r.Result) != null)
                .OrderBy(r => r.BaseItem != null).ThenBy(r => r.Gold).ThenBy(r => c.Item(r.Result).Name).ToList();

        public static bool HasInputs(SaveData s, RecipeDef r) => HasInputs(s, r, r.Inputs);

        private static bool HasInputs(SaveData s, RecipeDef r, List<ItemCount> inputs)
        {
            foreach (var ic in inputs) if (s.Count(ic.Item) < ic.Count) return false;
            if (r.BaseItem != null && s.Count(r.BaseItem) < 1) return false;
            return true;
        }

        public static bool IsUnlocked(SaveData s, RecipeDef r) => StationProgress.IsUnlocked(s, StationFor(r.Station), r.Tier);

        private static float PartyLuck(ContentDb c, SaveData s)
        {
            float luck = 0f;
            foreach (var id in s.Party)
            {
                var h = s.Hero(id);
                var def = h != null ? c.TryClass(h.ClassId) : null;
                if (def != null) luck += Formulas.StatsAtLevel(def, Progression.Level(h)).Luck;
            }
            return luck;
        }

        /// <summary>Chance this save's next Forge/Upgrade craft rolls a Masterwork result (party Luck + Blacksmith level).</summary>
        public static float MasterworkChance(ContentDb c, SaveData s)
        {
            int stationLevel = StationProgress.Level(s, StationKind.Blacksmith);
            float chance = MasterworkBaseChance + PartyLuck(c, s) * MasterworkChancePerLuck + (stationLevel - 1) * MasterworkChancePerStationLevel;
            return Math.Max(0f, Math.Min(MasterworkMaxChance, chance));
        }

        private static bool MasterworkEligible(ItemDef item) =>
            item.Type == ItemType.Equipment && item.Durability > 0 && (item.Slot == EquipmentSlot.Weapon || Array.IndexOf(BattleEngine.ArmorSlots, item.Slot) >= 0);

        /// <summary>Whether this recipe has a reagent-quality variant (GG-T45) and the player holds its
        /// higher-rarity inputs — e.g. a Pure Slime Gel in place of a plain one brews a Greater Healing Potion.</summary>
        public static bool HasPotencyInputs(SaveData s, RecipeDef r) => r.PotencyResult != null && HasInputs(s, r, r.PotencyInputs);

        public static Result Craft(ContentDb c, SaveData s, RecipeDef r, bool potency = false, Rng rng = null)
        {
            if (!IsUnlocked(s, r)) return Result.Fail($"Requires {StationProgress.TierName(r.Tier)}");
            if (!Forging.ArmorOpen(c, s, r)) return Result.Fail(Forging.ArmorLockedHint);
            if (potency && r.PotencyResult == null) return Result.Fail("No greater version");
            var inputs = potency ? r.PotencyInputs : r.Inputs;
            var resultId = potency ? r.PotencyResult : r.Result;
            var resultCount = potency ? r.PotencyResultCount : r.ResultCount;
            if (s.Gold < r.Gold) return Result.Fail("Not enough gold");
            if (!HasInputs(s, r, inputs)) return Result.Fail("Missing materials");
            var result = c.Item(resultId);
            if (s.Count(result.Id) + resultCount > result.MaxStack) return Result.Fail("Can't carry more");
            s.Gold -= r.Gold;
            foreach (var ic in inputs) s.AddItem(ic.Item, -ic.Count);
            if (r.BaseItem != null) s.AddItem(r.BaseItem, -1);
            s.AddItem(result.Id, resultCount);
            StationProgress.Gain(s, StationFor(r.Station), CraftXp);
            bool masterwork = false;
            if (!potency && r.Station == CraftStation.Forge && resultCount == 1 && MasterworkEligible(result))
            {
                rng ??= new Rng(masterworkSeed++);
                if (rng.Chance(MasterworkChance(c, s)))
                {
                    masterwork = true;
                    float mastery = rng.Range(1f + MasterworkStatBonusMin, 1f + MasterworkStatBonusMax);
                    int durability = (int)Math.Round(result.Durability * BattleConfig.MasterworkDurabilityMultiplier);
                    if (!s.Worn.TryGetValue(result.Id, out var list)) s.Worn[result.Id] = list = new List<WornGear>();
                    list.Add(new WornGear { Durability = durability, Repairs = 0, Mastery = mastery });
                }
            }
            return Result.Success(masterwork ? $"★ Masterwork {result.Name}!" : $"Made {result.Name}");
        }

        /// <summary>Junk..Legendary materials for any armor piece (Head/Torso/Legs/Gloves/Boots) without its own
        /// Forge recipe (GG-T89, extends GG-T42's fallback table to five slots instead of one).</summary>
        private static readonly (string Item, int Count)[][] ArmorFallbackTiers =
        {
            new[] { ("mat_tattered_cloth", 1) },
            new[] { ("mat_leather", 2), ("mat_cloth", 1) },
            new[] { ("mat_leather", 3), ("mat_arcane_dust", 1) },
            new[] { ("mat_troll_hide", 2), ("mat_leather", 2) },
            new[] { ("mat_demon_shard", 2), ("mat_troll_hide", 2) },
            new[] { ("mat_dragon_scale", 1), ("mat_demon_shard", 1) },
        };

        /// <summary>Junk..Legendary materials for any jewelry piece (Necklace/Earring/Ring) without its own recipe.</summary>
        private static readonly (string Item, int Count)[][] JewelryFallbackTiers =
        {
            new[] { ("mat_cracked_fang", 1) },
            new[] { ("mat_iron_ore", 1), ("mat_undead_bone", 1) },
            new[] { ("mat_arcane_dust", 2) },
            new[] { ("mat_naga_scale", 1), ("mat_arcane_dust", 1) },
            new[] { ("mat_ghost_ectoplasm", 2) },
            new[] { ("mat_dragon_scale", 1) },
        };

        /// <summary>Pieces without a base Forge recipe (upgrades, enchants, jewelry) fall back to a flat
        /// materials-by-type-and-rarity table, so every equipment piece is salvageable (GG-T42).</summary>
        private static readonly Dictionary<(EquipmentSlot Slot, ItemRarity Rarity), (string Item, int Count)[]> FallbackSalvage = BuildFallbackSalvage();

        private static Dictionary<(EquipmentSlot, ItemRarity), (string, int)[]> BuildFallbackSalvage()
        {
            var map = new Dictionary<(EquipmentSlot, ItemRarity), (string, int)[]>
            {
                [(EquipmentSlot.Weapon, ItemRarity.Junk)] = new[] { ("mat_broken_blade", 1) },
                [(EquipmentSlot.Weapon, ItemRarity.Common)] = new[] { ("mat_iron_ore", 2), ("mat_wood_plank", 1) },
                [(EquipmentSlot.Weapon, ItemRarity.Uncommon)] = new[] { ("mat_iron_ore", 3), ("mat_arcane_dust", 1) },
                [(EquipmentSlot.Weapon, ItemRarity.Rare)] = new[] { ("mat_iron_ore", 4), ("mat_arcane_dust", 2) },
                [(EquipmentSlot.Weapon, ItemRarity.Epic)] = new[] { ("mat_demon_shard", 2), ("mat_arcane_dust", 2) },
                [(EquipmentSlot.Weapon, ItemRarity.Legendary)] = new[] { ("mat_dragon_scale", 1), ("mat_demon_shard", 2) },
            };
            var rarities = (ItemRarity[])Enum.GetValues(typeof(ItemRarity)); // declared order: Junk..Legendary
            void Fill(EquipmentSlot slot, (string, int)[][] tiers)
            {
                for (int i = 0; i < rarities.Length && i < tiers.Length; i++) map[(slot, rarities[i])] = tiers[i];
            }
            foreach (var slot in BattleEngine.ArmorSlots) Fill(slot, ArmorFallbackTiers);
            // Only the canonical instance of each multi-slot jewelry category needs an entry (see the
            // bucket redirect in FullSalvageYield below) — Necklace has just the one slot either way.
            foreach (var slot in new[] { EquipmentSlot.Necklace, EquipmentSlot.Earring1, EquipmentSlot.Ring1 }) Fill(slot, JewelryFallbackTiers);
            return map;
        }

        private static List<ItemCount> FullSalvageYield(ContentDb c, ItemDef item)
        {
            var recipe = c.Recipes.Values.FirstOrDefault(r => r.Result == item.Id && r.Station == CraftStation.Forge && r.BaseItem == null);
            if (recipe != null)
                return recipe.Inputs.Select(ic => new ItemCount { Item = ic.Item, Count = Math.Max(1, ic.Count / 2) }).ToList();
            var bucket = item.Slot == EquipmentSlot.Earring2 ? EquipmentSlot.Earring1 : item.Slot == EquipmentSlot.Ring2 ? EquipmentSlot.Ring1 : item.Slot;
            if (!FallbackSalvage.TryGetValue((bucket, item.Rarity), out var fallback)) return new List<ItemCount>();
            return fallback.Select(f => new ItemCount { Item = f.Item, Count = f.Count }).ToList();
        }

        /// <summary>Salvage yield for a piece at <paramref name="durability"/> out of its max: a near-dead or
        /// Broken item returns proportionally less (floor), but never less than 1 of the cheapest input (GG-T42).</summary>
        public static List<ItemCount> SalvageYield(ContentDb c, ItemDef item, int durability)
        {
            var full = FullSalvageYield(c, item);
            if (full.Count == 0 || item.Durability <= 0) return full;
            float frac = Math.Max(0f, Math.Min(1f, (float)durability / item.Durability));
            var scaled = full.Select(ic => new ItemCount { Item = ic.Item, Count = (int)Math.Floor(ic.Count * frac) })
                .Where(ic => ic.Count > 0).ToList();
            if (scaled.Count == 0)
            {
                var cheapest = full.OrderBy(ic => c.TryItem(ic.Item)?.BaseCost ?? int.MaxValue).First();
                scaled.Add(new ItemCount { Item = cheapest.Item, Count = 1 });
            }
            return scaled;
        }

        /// <summary>Preview of what salvaging the bag's worst (most-worn) copy of this item would yield, without consuming it.</summary>
        public static List<ItemCount> SalvagePreview(ContentDb c, SaveData s, string itemId)
        {
            var item = c.TryItem(itemId);
            return item == null ? new List<ItemCount>() : SalvageYield(c, item, PeekWorstDurability(s, item));
        }

        private static int PeekWorstDurability(SaveData s, ItemDef item)
        {
            if (item.Durability <= 0) return item.Durability;
            return s.Worn.TryGetValue(item.Id, out var worn) && worn.Count > 0 ? worn.Min(w => w.Durability) : item.Durability;
        }

        /// <summary>Removes and returns the durability of the bag's single worst tracked copy, or the full
        /// durability if every copy is factory-fresh (so salvaging always eats the junkiest piece first).</summary>
        private static int TakeWorstDurability(SaveData s, ItemDef item)
        {
            if (item.Durability <= 0) return item.Durability;
            if (!s.Worn.TryGetValue(item.Id, out var worn) || worn.Count == 0) return item.Durability;
            int idx = 0;
            for (int i = 1; i < worn.Count; i++) if (worn[i].Durability < worn[idx].Durability) idx = i;
            int dur = worn[idx].Durability;
            worn.RemoveAt(idx);
            if (worn.Count == 0) s.Worn.Remove(item.Id);
            return dur;
        }

        /// <summary>Salvages one bag copy of <paramref name="itemId"/> — the worst (most-worn) tracked copy first.</summary>
        public static Result Salvage(ContentDb c, SaveData s, string itemId)
        {
            var item = c.TryItem(itemId);
            if (item == null || s.Count(itemId) < 1) return Result.Fail("Nothing to salvage");
            int durability = TakeWorstDurability(s, item);
            var yield = SalvageYield(c, item, durability);
            if (yield.Count == 0) return Result.Fail("Can't be salvaged");
            s.AddItem(itemId, -1);
            foreach (var ic in yield) s.AddItem(ic.Item, ic.Count);
            StationProgress.Gain(s, StationKind.Blacksmith, SalvageXp);
            return Result.Success($"Salvaged {item.Name}");
        }

        /// <summary>Salvages a hero's currently equipped weapon/armor directly (the "Salvage instead" offer on
        /// an uneconomical repair, §25.2) — the piece is destroyed, not returned to the bag (GG-T42).</summary>
        public static Result SalvageEquipped(ContentDb c, SaveData s, HeroSave h, EquipmentSlot slot)
        {
            var itemId = Equipment.Equipped(h, slot);
            var item = itemId != null ? c.TryItem(itemId) : null;
            if (item == null) return Result.Fail("Nothing equipped");
            int durability = slot == EquipmentSlot.Weapon ? h.WeaponDurability : h.Armor.TryGetValue(slot, out var ap) ? ap.Durability : 0;
            var yield = SalvageYield(c, item, durability);
            if (yield.Count == 0) return Result.Fail("Can't be salvaged");
            switch (slot)
            {
                case EquipmentSlot.Weapon: h.WeaponId = null; h.WeaponDurability = 0; h.WeaponRepairs = 0; h.WeaponMastery = 1f; break;
                case var armorSlot when Array.IndexOf(BattleEngine.ArmorSlots, armorSlot) >= 0: h.Armor.Remove(armorSlot); break;
                default: return Result.Fail("Can't be salvaged");
            }
            foreach (var ic in yield) s.AddItem(ic.Item, ic.Count);
            StationProgress.Gain(s, StationKind.Blacksmith, SalvageXp);
            return Result.Success($"Salvaged {item.Name}");
        }

        /// <summary>Preview of what salvaging one Broken fragment of <paramref name="itemId"/> would yield
        /// (durability 0 — the floor: 1 of the cheapest input) (GG-T41).</summary>
        public static List<ItemCount> SalvageBrokenPreview(ContentDb c, string itemId)
        {
            var item = c.TryItem(itemId);
            return item == null ? new List<ItemCount>() : SalvageYield(c, item, 0);
        }

        /// <summary>Scraps one Broken fragment of <paramref name="itemId"/> for materials — Broken pieces
        /// can't be equipped or repaired, so salvage is their only use (GG-T41).</summary>
        public static Result SalvageBroken(ContentDb c, SaveData s, string itemId)
        {
            var item = c.TryItem(itemId);
            if (item == null || s.BrokenCount(itemId) < 1) return Result.Fail("Nothing to salvage");
            var yield = SalvageYield(c, item, 0);
            if (yield.Count == 0) return Result.Fail("Can't be salvaged");
            s.AddBroken(itemId, -1);
            foreach (var ic in yield) s.AddItem(ic.Item, ic.Count);
            StationProgress.Gain(s, StationKind.Blacksmith, SalvageXp);
            return Result.Success($"Salvaged Broken {item.Name}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Recipe discovery (§24.6.2, GG-T36): recipes start hidden; a recipe is discovered once its
    // station reaches its tier (§24.6.1), or earlier by reading a recipe scroll or first-ever
    // pickup of the recipe's rarest input. Discovery only gates visibility — Crafting.Craft's own
    // gold/inputs/tier checks are unchanged, since a discovered-but-locked recipe already fails
    // IsUnlocked, and a tier-reached recipe is discovered by definition.
    // ─────────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────────
    // Bottle brewing (GG-T176, §25.0.5): an Empty Bottle from the Merchant plus looted monster trophies brews a
    // potion for less than the Merchant charges for it.
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Brewing
    {
        public static bool IsBottleRecipe(RecipeDef r) =>
            r != null && r.Station == CraftStation.Alchemy && r.Inputs.Any(i => i.Item == ContentLoader.EmptyBottle);

        public static List<RecipeDef> BottleRecipes(ContentDb c) => Crafting.Recipes(c, CraftStation.Alchemy).Where(IsBottleRecipe).ToList();

        /// <summary>The bag holds every looted input of <paramref name="r"/> — everything but its Empty Bottles.</summary>
        public static bool HasLoot(SaveData s, RecipeDef r) =>
            s != null && r != null && r.Inputs.All(i => i.Item == ContentLoader.EmptyBottle || s.Count(i.Item) >= i.Count);

        /// <summary>The CanBrew story trigger (the Alchemist's join beat): the player has looted enough for an
        /// Apprentice-tier bottle recipe — their first brew.</summary>
        public static bool LootedForFirstBrew(ContentDb c, SaveData s) => BottleRecipes(c).Any(r => r.Tier <= 1 && HasLoot(s, r));

        /// <summary>The bottle recipe the Alchemist's camp tip walks through: one this save can brew right now, else
        /// one whose loot is already in the bag (only the bottle missing), else null.</summary>
        public static RecipeDef FirstBrew(ContentDb c, SaveData s)
        {
            if (s == null) return null;
            var open = BottleRecipes(c).Where(r => Crafting.IsUnlocked(s, r)).ToList();
            return open.FirstOrDefault(r => Crafting.HasInputs(s, r) && s.Gold >= r.Gold) ?? open.FirstOrDefault(r => HasLoot(s, r));
        }

        /// <summary>Gold to brew <paramref name="r"/> with every input bought at the Merchant's price, plus its fee —
        /// always less than buying what it makes (<see cref="BuyCost"/>).</summary>
        public static int ShopCost(ContentDb c, RecipeDef r) => r.Gold + r.Inputs.Sum(i => (c.TryItem(i.Item)?.BaseCost ?? 0) * i.Count);

        /// <summary>What the recipe's output would cost bought outright.</summary>
        public static int BuyCost(ContentDb c, RecipeDef r) => (c.TryItem(r.Result)?.BaseCost ?? 0) * r.ResultCount;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The first forge (GG-T177, §25.0.6): once the Blacksmith's workshop opens, the Forge offers starter weapon
    // recipes that cost no gold and take only early drops; the guided forge equips the result on the hero it suits.
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Forging
    {
        /// <summary>Save flag set by the guided first forge.</summary>
        public const string FirstForgeFlag = "onboard:first-forge";
        /// <summary>The camp-tip key of the Blacksmith's Forge tab (his Repair tab's tip is "blacksmith").</summary>
        public const string TipStation = "blacksmith-forge";

        public sealed class Plan
        {
            public RecipeDef Recipe;
            /// <summary>What it makes: the starter weapon (<see cref="First"/>) or the set piece (<see cref="FirstArmor"/>).</summary>
            public ItemDef Weapon;
            /// <summary>The party hero it suits: the one who would equip it on forging.</summary>
            public HeroSave Hero;
            /// <summary>Every input is in the bag (it costs no gold), so Craft works now.</summary>
            public bool Ready;
        }

        public static bool IsStarter(RecipeDef r) => r != null && r.Starter && r.Station == CraftStation.Forge && r.BaseItem == null;

        public static List<RecipeDef> StarterRecipes(ContentDb c) => Crafting.Recipes(c, CraftStation.Forge).Where(IsStarter).ToList();

        /// <summary>The first forge is behind this save: it has been made, or the save is a bot's or a New Game+.</summary>
        public static bool Done(SaveData s) => s == null || s.TestMode || s.NewGamePlus > 0 || s.Flags.Contains(FirstForgeFlag);

        /// <summary>How well a weapon's stats match a hero's class: its stat bonuses weighted by the class's growth,
        /// per point — so a Wisdom lantern suits the Cleric and a Strength/Vitality club the Paladin.</summary>
        public static float Affinity(ContentDb c, HeroSave h, ItemDef item)
        {
            var g = c.TryClass(h.ClassId)?.Growth;
            var st = item?.Stats;
            if (g == null || st == null) return 0f;
            float total = st.Strength + st.Vitality + st.Agility + st.Speed + st.Stamina + st.Intelligence + st.Wisdom + st.Luck;
            if (total <= 0f) return 0f;
            return (st.Strength * g.Strength + st.Vitality * g.Vitality + st.Agility * g.Agility + st.Speed * g.Speed
                    + st.Stamina * g.Stamina + st.Intelligence * g.Intelligence + st.Wisdom * g.Wisdom + st.Luck * g.Luck) / total;
        }

        /// <summary>The party hero a weapon suits: one who can wield it and would be better off with it than with what
        /// they hold now, the best stat match first (then the bigger gain, then party order); null when nobody gains.</summary>
        public static HeroSave SuitedHero(ContentDb c, SaveData s, ItemDef item)
        {
            if (c == null || s == null || item == null || item.Slot != EquipmentSlot.Weapon) return null;
            float score = GridGame.Core.Bot.MetaHeuristics.Score(item);
            return s.Party.Select(s.Hero)
                .Where(h => h != null && Equipment.Fits(c, h, item, EquipmentSlot.Weapon))
                .Select(h => (h, gain: score - GridGame.Core.Bot.MetaHeuristics.Score(c.TryItem(h.WeaponId))))
                .Where(x => x.gain > 0f)
                .OrderByDescending(x => Affinity(c, x.h, item)).ThenByDescending(x => x.gain)
                .Select(x => x.h).FirstOrDefault();
        }

        /// <summary>The starter recipe the Forge's camp tip walks and the Forge preselects, or null once the first forge
        /// is done: one that suits a party hero and can be carried, ready-to-forge ones first, then the best stat match,
        /// then the fewest materials still to find.</summary>
        public static Plan First(ContentDb c, SaveData s)
        {
            if (c == null || Done(s)) return null;
            return StarterRecipes(c)
                .Where(r => Crafting.IsUnlocked(s, r) && c.TryItem(r.Result) is ItemDef w && s.Count(w.Id) + r.ResultCount <= w.MaxStack)
                .Select(r => { var w = c.Item(r.Result); return new Plan { Recipe = r, Weapon = w, Hero = SuitedHero(c, s, w), Ready = Crafting.HasInputs(s, r) && s.Gold >= r.Gold }; })
                .Where(p => p.Hero != null)
                .OrderByDescending(p => p.Ready).ThenByDescending(p => Affinity(c, p.Hero, p.Weapon))
                .ThenBy(p => p.Recipe.Inputs.Sum(i => Math.Max(0, i.Count - s.Count(i.Item))))
                .FirstOrDefault();
        }

        /// <summary>Forging <paramref name="r"/> is the guided first forge: it is a starter recipe and the first forge is still ahead.</summary>
        public static bool IsGuided(SaveData s, RecipeDef r) => IsStarter(r) && !Done(s);

        /// <summary>The guided first forge: makes the starter weapon and equips it on the hero it suits (when one gains
        /// from it), then marks the first forge done. Anything else is an ordinary <see cref="Crafting.Craft"/>.</summary>
        public static Result Forge(ContentDb c, SaveData s, RecipeDef r)
        {
            if (IsGuidedArmor(c, s, r)) return ForgeFirstArmor(c, s, r);
            if (!IsGuided(s, r)) return Crafting.Craft(c, s, r);
            var weapon = c.TryItem(r.Result);
            var hero = SuitedHero(c, s, weapon);
            var made = Crafting.Craft(c, s, r);
            if (!made.Ok) return made;
            s.Flags.Add(FirstForgeFlag);
            if (hero == null) return made;
            var equipped = Equipment.Equip(c, s, hero, EquipmentSlot.Weapon, weapon.Id);
            string name = c.TryClass(hero.ClassId)?.Name ?? hero.ClassId;
            return equipped.Ok ? Result.Success($"{made.Message}: the {name} equips it") : made;
        }

        // ── Onboarding step 5: forge armor (GG-T178, §25.0.7) ─────────────────────────────────────────────
        // The armor-set recipes (GG-T182) open at the Forge once the first weapon is made; the guided first armor
        // forges one set piece and equips it on the hero it suits.

        /// <summary>Save flag set by the guided first armor forge.</summary>
        public const string FirstArmorFlag = "onboard:first-armor";
        /// <summary>The camp-tip key of the Forge tab once the first weapon is made and the first armor is still ahead.</summary>
        public const string ArmorTipStation = "blacksmith-armor";
        /// <summary>Why a set piece can't be forged yet.</summary>
        public const string ArmorLockedHint = "Armor recipes open after your first forge";

        /// <summary>A Forge recipe for a piece of a named armor set (GG-T182).</summary>
        public static bool IsSetArmor(ContentDb c, RecipeDef r) =>
            c != null && r != null && r.Station == CraftStation.Forge && r.BaseItem == null && c.TryArmorSetOf(r.Result) != null;

        /// <summary>The armor-set recipes are open at the Forge: always, except for a set piece before the first forge.</summary>
        public static bool ArmorOpen(ContentDb c, SaveData s, RecipeDef r) => !IsSetArmor(c, r) || Done(s);

        public static List<RecipeDef> SetArmorRecipes(ContentDb c) => Crafting.Recipes(c, CraftStation.Forge).Where(r => IsSetArmor(c, r)).ToList();

        /// <summary>The first armor is behind this save: it has been forged, or the save is a bot's or a New Game+.</summary>
        public static bool ArmorDone(SaveData s) => s == null || s.TestMode || s.NewGamePlus > 0 || s.Flags.Contains(FirstArmorFlag);

        /// <summary>The first armor step is on: the first weapon is made and the first armor isn't.</summary>
        public static bool ArmorStep(SaveData s) => Done(s) && !ArmorDone(s);

        /// <summary>The party hero an armor piece suits: one who is proficient in its weight and better off with it than
        /// with what they wear in that slot, the best stat match first (then the bigger gain, then party order).</summary>
        public static HeroSave SuitedWearer(ContentDb c, SaveData s, ItemDef item)
        {
            if (c == null || s == null || item == null || Array.IndexOf(BattleEngine.ArmorSlots, item.Slot) < 0) return null;
            float score = GridGame.Core.Bot.MetaHeuristics.Score(item);
            return s.Party.Select(s.Hero)
                .Where(h => h != null && Equipment.Fits(c, h, item, item.Slot))
                .Select(h => (h, gain: score - GridGame.Core.Bot.MetaHeuristics.Score(c.TryItem(Equipment.Equipped(h, item.Slot)))))
                .Where(x => x.gain > 0f)
                .OrderByDescending(x => Affinity(c, x.h, item)).ThenByDescending(x => x.gain)
                .Select(x => x.h).FirstOrDefault();
        }

        /// <summary>The set piece the Forge's armor tip walks and the Forge preselects, or null outside the armor step:
        /// an open Apprentice set recipe that suits a party hero and can be carried, ready-to-forge ones first, then the
        /// best stat match, then the fewest materials still to find, then the cheapest.</summary>
        public static Plan FirstArmor(ContentDb c, SaveData s)
        {
            if (c == null || !ArmorStep(s)) return null;
            return SetArmorRecipes(c)
                .Where(r => r.Tier <= 1 && Crafting.IsUnlocked(s, r) && c.TryItem(r.Result) is ItemDef a && s.Count(a.Id) + r.ResultCount <= a.MaxStack)
                .Select(r => { var a = c.Item(r.Result); return new Plan { Recipe = r, Weapon = a, Hero = SuitedWearer(c, s, a), Ready = Crafting.HasInputs(s, r) && s.Gold >= r.Gold }; })
                .Where(p => p.Hero != null)
                .OrderByDescending(p => p.Ready).ThenByDescending(p => Affinity(c, p.Hero, p.Weapon))
                .ThenBy(p => p.Recipe.Inputs.Sum(i => Math.Max(0, i.Count - s.Count(i.Item)))).ThenBy(p => p.Recipe.Gold)
                .FirstOrDefault();
        }

        /// <summary>Forging <paramref name="r"/> is the guided first armor: a set piece, during the armor step.</summary>
        public static bool IsGuidedArmor(ContentDb c, SaveData s, RecipeDef r) => IsSetArmor(c, r) && ArmorStep(s);

        /// <summary>The guided first armor: makes the set piece and equips it on the hero it suits (when one gains from
        /// it), then marks the first armor done.</summary>
        private static Result ForgeFirstArmor(ContentDb c, SaveData s, RecipeDef r)
        {
            var piece = c.TryItem(r.Result);
            var hero = SuitedWearer(c, s, piece);
            var made = Crafting.Craft(c, s, r);
            if (!made.Ok) return made;
            s.Flags.Add(FirstArmorFlag);
            if (hero == null) return made;
            var equipped = Equipment.Equip(c, s, hero, piece.Slot, piece.Id);
            string name = c.TryClass(hero.ClassId)?.Name ?? hero.ClassId;
            return equipped.Ok ? Result.Success($"{made.Message}: the {name} wears it") : made;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Armor enchants (GG-T179, §25.0.8): the Enchantress binds a passive (regeneration, poison ward, thorns…) into a
    // worn armor piece. Onboarding step 6: she joins once the first armor piece is forged, and the guided first
    // enchant (free of gold) binds one into it.
    // ─────────────────────────────────────────────────────────────────────────────

    public static class ArmorEnchanting
    {
        /// <summary>Save flag set by the first armor enchant.</summary>
        public const string FirstEnchantFlag = "onboard:first-enchant";

        /// <summary>One worn armor piece the Enchantress can work on.</summary>
        public sealed class Target
        {
            public HeroSave Hero;
            public EquipmentSlot Slot;
            public EquippedArmor Piece;
            public ItemDef Item;
            public string RowName => PieceRowPrefix + Hero.ClassId + "-" + Slot.ToString().ToLowerInvariant();
        }

        /// <summary>The guided first enchant: which enchant, into which piece, and whether it can be bound right now.</summary>
        public sealed class Plan
        {
            public ArmorEnchantDef Enchant;
            public Target Target;
            public bool Ready;
        }

        /// <summary>Row-name prefixes on the Enchantress's Armor tab (camp-tip and automation targets).</summary>
        public const string PieceRowPrefix = "piece-", EnchantRowPrefix = "aenchant-";

        public static IReadOnlyList<ArmorEnchantDef> All(ContentDb c) => c.ArmorEnchantOrder;

        /// <summary>Like every Enchantress recipe, an armor enchant's tier unlocks with the Alchemist's station level.</summary>
        public static bool IsUnlocked(SaveData s, ArmorEnchantDef e) => e != null && StationProgress.IsUnlocked(s, StationKind.Alchemist, e.Tier);

        public static bool HasInputs(SaveData s, ArmorEnchantDef e) => s != null && e != null && e.Inputs.All(i => s.Count(i.Item) >= i.Count);

        /// <summary>The first enchant is behind this save: it has been bound, or the save is a bot's or a New Game+.</summary>
        public static bool FirstDone(SaveData s) => s == null || s.TestMode || s.NewGamePlus > 0 || s.Flags.Contains(FirstEnchantFlag);

        /// <summary>Gold an enchant costs this save: nothing for the guided first one.</summary>
        public static int Gold(SaveData s, ArmorEnchantDef e) => e == null ? 0 : FirstDone(s) ? e.Gold : 0;

        public static bool CanAfford(SaveData s, ArmorEnchantDef e) => s != null && s.Gold >= Gold(s, e);

        /// <summary>Every intact armor piece the roster wears, party heroes first.</summary>
        public static List<Target> Pieces(ContentDb c, SaveData s)
        {
            var list = new List<Target>();
            if (c == null || s == null) return list;
            var heroes = s.Party.Select(s.Hero).Where(h => h != null).Concat(s.Roster.Where(h => !s.Party.Contains(h.ClassId))).ToList();
            foreach (var h in heroes)
                foreach (var slot in BattleEngine.ArmorSlots)
                    if (h.Armor.TryGetValue(slot, out var piece) && piece?.ItemId != null && c.TryItem(piece.ItemId) is ItemDef item && item.Durability > 0)
                        list.Add(new Target { Hero = h, Slot = slot, Piece = piece, Item = item });
            return list;
        }

        public static Target Find(ContentDb c, SaveData s, string classId, EquipmentSlot slot) =>
            Pieces(c, s).FirstOrDefault(t => t.Hero.ClassId == classId && t.Slot == slot);

        /// <summary>The first enchant the Enchantress's camp tip walks and her Armor tab preselects, or null once it's done
        /// (or nobody wears armor yet): an unenchanted piece (a set piece first, as the guided first armor is), and an open
        /// enchant whose materials are in the bag first, then the fewest materials still to find, then file order.</summary>
        public static Plan First(ContentDb c, SaveData s)
        {
            if (c == null || FirstDone(s)) return null;
            var target = Pieces(c, s).Where(t => t.Piece.Enchant == null)
                .OrderByDescending(t => c.TryArmorSetOf(t.Item.Id) != null).FirstOrDefault();
            if (target == null) return null;
            var e = All(c).Select((x, i) => (x, i)).Where(p => IsUnlocked(s, p.x))
                .OrderByDescending(p => HasInputs(s, p.x))
                .ThenBy(p => p.x.Inputs.Sum(i => Math.Max(0, i.Count - s.Count(i.Item))))
                .ThenBy(p => p.i).Select(p => p.x).FirstOrDefault();
            return e == null ? null : new Plan { Enchant = e, Target = target, Ready = HasInputs(s, e) };
        }

        /// <summary>Binds <paramref name="e"/> into the armor <paramref name="h"/> wears in <paramref name="slot"/>, replacing
        /// any enchant it had. The piece keeps it through unequip and re-equip, and loses it if it breaks.</summary>
        public static Result Enchant(ContentDb c, SaveData s, HeroSave h, EquipmentSlot slot, ArmorEnchantDef e)
        {
            if (c == null || s == null || h == null || e == null) return Result.Fail("Nothing to enchant");
            if (!h.Armor.TryGetValue(slot, out var piece) || piece?.ItemId == null) return Result.Fail("No armor worn there");
            var item = c.TryItem(piece.ItemId);
            if (item == null || item.Durability <= 0) return Result.Fail("That can't hold an enchant");
            if (piece.Enchant == e.Id) return Result.Fail($"Already bound with {e.Name}");
            if (!IsUnlocked(s, e)) return Result.Fail($"Requires {StationProgress.TierName(e.Tier)}");
            if (!HasInputs(s, e)) return Result.Fail("Missing materials");
            int gold = Gold(s, e);
            if (s.Gold < gold) return Result.Fail("Not enough gold");
            s.Gold -= gold;
            foreach (var ic in e.Inputs) s.AddItem(ic.Item, -ic.Count);
            string replaced = c.TryArmorEnchant(piece.Enchant)?.Name;
            piece.Enchant = e.Id;
            StationProgress.Gain(s, StationKind.Alchemist, Crafting.CraftXp);
            s.Flags.Add(FirstEnchantFlag);
            string passive = c.TryAbility(e.Passive)?.Name ?? e.Name;
            string hero = c.TryClass(h.ClassId)?.Name ?? h.ClassId;
            return Result.Success($"{hero}'s {item.Name} now grants {passive}" + (replaced != null ? $" (replacing {replaced})" : ""));
        }

        /// <summary>The passive ability an enchant id grants, or null.</summary>
        public static AbilityDef PassiveOf(ContentDb c, string enchantId) => c?.TryAbility(c.TryArmorEnchant(enchantId)?.Passive);
    }

    public static class RecipeDiscovery
    {
        public static bool IsDiscovered(SaveData s, RecipeDef r) =>
            r != null && (Crafting.IsUnlocked(s, r) || s.DiscoveredRecipes.Contains(r.Id));

        public static void Discover(SaveData s, RecipeDef r) { if (r != null) s.DiscoveredRecipes.Add(r.Id); }

        /// <summary>The input material with the highest rarity (ties broken by cost, then id) — first-ever
        /// pickup of this item discovers the recipe.</summary>
        public static ItemDef RarestInput(ContentDb c, RecipeDef r)
        {
            ItemDef rarest = null;
            foreach (var ic in r.Inputs)
            {
                var item = c.TryItem(ic.Item);
                if (item == null) continue;
                if (rarest == null || item.Rarity > rarest.Rarity ||
                    (item.Rarity == rarest.Rarity && (item.BaseCost > rarest.BaseCost ||
                        (item.BaseCost == rarest.BaseCost && string.CompareOrdinal(item.Id, rarest.Id) < 0))))
                    rarest = item;
            }
            return rarest;
        }

        /// <summary>Call when the player's first-ever copy of an item lands in the bag (battle loot) —
        /// discovers every still-hidden recipe whose rarest input is that item.</summary>
        public static void OnItemAcquired(ContentDb c, SaveData s, string itemId)
        {
            foreach (var r in c.Recipes.Values)
            {
                if (IsDiscovered(s, r)) continue;
                if (RarestInput(c, r)?.Id == itemId) Discover(s, r);
            }
        }

        /// <summary>Reads a recipe scroll: learns its recipe outright (even if still tier-locked) and consumes it.</summary>
        public static Result Learn(ContentDb c, SaveData s, string scrollItemId)
        {
            var item = c.TryItem(scrollItemId);
            if (item == null || item.Type != ItemType.RecipeScroll) return Result.Fail("Can't be read");
            if (s.Count(scrollItemId) < 1) return Result.Fail("None left");
            var recipe = c.TryRecipe(item.TeachesRecipe);
            if (recipe == null) return Result.Fail("This page leads nowhere");
            bool already = IsDiscovered(s, recipe);
            s.AddItem(scrollItemId, -1);
            Discover(s, recipe);
            var result = c.TryItem(recipe.Result);
            return Result.Success(already ? $"You already knew how to make {result?.Name ?? recipe.Result}" : $"Learned: {result?.Name ?? recipe.Result}");
        }

        /// <summary>Not discovered, but missing exactly one item (an input, or the base item an
        /// upgrade/enchant consumes) — the Forge/Brew list's "???" tease. Null when discovered, already
        /// craftable, or still missing more than one thing.</summary>
        public static ItemCount OneAwayMissing(SaveData s, RecipeDef r)
        {
            if (IsDiscovered(s, r)) return null;
            ItemCount missing = null;
            if (r.BaseItem != null && s.Count(r.BaseItem) < 1) missing = new ItemCount { Item = r.BaseItem, Count = 1 };
            foreach (var ic in r.Inputs)
            {
                if (s.Count(ic.Item) < ic.Count)
                {
                    if (missing != null) return null;
                    missing = ic;
                }
            }
            return missing;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Repair (§24.5, §25.2)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Repair
    {
        public const int RepairXp = 8;

        /// <summary>The piece's durability ceiling — raised by <see cref="BattleConfig.MasterworkDurabilityMultiplier"/> for a Masterwork copy (GG-T46).</summary>
        public static int EffectiveMax(ItemDef item, int repairs, float mastery = 1f) =>
            Math.Max(1, (int)Math.Round(item.Durability * (mastery > 1f ? BattleConfig.MasterworkDurabilityMultiplier : 1f)) - repairs);

        /// <summary>Repair price after the caravan's Armory Wagon discount.</summary>
        public static int Cost(ContentDb c, SaveData s, ItemDef item, int current, int repairs, float mastery = 1f)
        {
            int baseCost = Cost(item, current, repairs, mastery);
            if (baseCost <= 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(baseCost * (1f - CaravanUpgrades.Value(c, s, "armory"))));
        }

        public static int Cost(ItemDef item, int current, int repairs, float mastery = 1f)
        {
            if (item == null || item.Durability <= 0) return 0;
            int missing = Math.Max(0, EffectiveMax(item, repairs, mastery) - current);
            if (missing == 0) return 0;
            float perPoint = (float)item.BaseCost / item.Durability;
            float mult = 0.30f * (float)Math.Pow(1.6f, Math.Max(0, repairs));
            return Math.Max(1, (int)Math.Ceiling(missing * perPoint * mult));
        }

        public static bool IsUneconomical(ItemDef item, int current, int repairs, float mastery = 1f) => Cost(item, current, repairs, mastery) >= item.BaseCost;

        /// <summary>One equipped piece that wears (weapon or armor) and how worn it is.</summary>
        public sealed class WornPiece
        {
            public HeroSave Hero;
            public EquipmentSlot Slot;
            public ItemDef Item;
            public int Durability, Max;
            public float Fraction => Max <= 0 ? 1f : (float)Durability / Max;
            /// <summary>The Repair tab row's element name ("repair-knight-Weapon") — what the Blacksmith's camp tip points at.</summary>
            public string RowName => RowNameFor(Hero.ClassId, Slot);
        }

        public static string RowNameFor(string classId, EquipmentSlot slot) => $"repair-{classId}-{slot}";

        /// <summary>Every equipped weapon and armor piece across the roster that has durability.</summary>
        public static IEnumerable<WornPiece> Pieces(ContentDb c, SaveData s)
        {
            if (s == null) yield break;
            foreach (var h in s.Roster)
            {
                var w = c.TryItem(h.WeaponId);
                if (w != null && w.Durability > 0)
                    yield return new WornPiece { Hero = h, Slot = EquipmentSlot.Weapon, Item = w, Durability = h.WeaponDurability, Max = EffectiveMax(w, h.WeaponRepairs, h.WeaponMastery) };
                foreach (var slot in BattleEngine.ArmorSlots)
                {
                    if (!h.Armor.TryGetValue(slot, out var piece)) continue;
                    var a = c.TryItem(piece.ItemId);
                    if (a != null && a.Durability > 0)
                        yield return new WornPiece { Hero = h, Slot = slot, Item = a, Durability = piece.Durability, Max = EffectiveMax(a, piece.Repairs, piece.Mastery) };
                }
            }
        }

        /// <summary>The most worn equipped piece (lowest durability fraction), or null when nobody wears gear that wears.</summary>
        public static WornPiece MostWorn(ContentDb c, SaveData s) => Pieces(c, s).OrderBy(p => p.Fraction).FirstOrDefault();

        /// <summary>True once any equipped piece is below <paramref name="percent"/>% of its max — the GearWorn story trigger (GG-T175).</summary>
        public static bool AnyBelow(ContentDb c, SaveData s, int percent) => Pieces(c, s).Any(p => p.Durability * 100 < p.Max * percent);

        public static Result RepairWeapon(ContentDb c, SaveData s, HeroSave h)
        {
            var item = c.TryItem(h.WeaponId);
            if (item == null || item.Durability <= 0) return Result.Fail("Nothing to repair");
            int cost = Cost(c, s, item, h.WeaponDurability, h.WeaponRepairs, h.WeaponMastery);
            if (cost == 0) return Result.Fail("Already in top shape");
            if (s.Gold < cost) return Result.Fail("Not enough gold");
            s.Gold -= cost;
            h.WeaponRepairs++;
            h.WeaponDurability = EffectiveMax(item, h.WeaponRepairs, h.WeaponMastery);
            StationProgress.Gain(s, StationKind.Blacksmith, RepairXp);
            return Result.Success($"Repaired {item.Name}");
        }

        /// <summary>Repairs one equipped armor piece (Head/Torso/Legs/Gloves/Boots) — each slot has its own
        /// durability/repair count, so this takes which slot instead of assuming the single old Armor slot (GG-T89).</summary>
        public static Result RepairArmor(ContentDb c, SaveData s, HeroSave h, EquipmentSlot slot)
        {
            if (!h.Armor.TryGetValue(slot, out var piece)) return Result.Fail("Nothing equipped");
            var item = c.TryItem(piece.ItemId);
            if (item == null || item.Durability <= 0) return Result.Fail("Nothing to repair");
            int cost = Cost(c, s, item, piece.Durability, piece.Repairs, piece.Mastery);
            if (cost == 0) return Result.Fail("Already in top shape");
            if (s.Gold < cost) return Result.Fail("Not enough gold");
            s.Gold -= cost;
            piece.Repairs++;
            piece.Durability = EffectiveMax(item, piece.Repairs, piece.Mastery);
            StationProgress.Gain(s, StationKind.Blacksmith, RepairXp);
            return Result.Success($"Repaired {item.Name}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Inn / Heal party (§25.3)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Inn
    {
        public const float GoldPerHp = 0.5f;

        public static int MissingHp(ContentDb c, SaveData s, HeroSave h)
        {
            if (h.Hp <= 0) return 0;
            var max = Progression.Preview(c, h).MaxHp;
            return Math.Max(0, max - h.Hp);
        }

        public static int Cost(ContentDb c, SaveData s)
        {
            int missing = 0;
            foreach (var h in s.Roster) missing += MissingHp(c, s, h);
            float premium = s.Expedition != null ? CaravanUpgrades.Value(c, s, "medic_wagon", 1f) : 1f; // on the road: the Medic Wagon's price
            return (int)Math.Ceiling(missing * GoldPerHp * premium);
        }

        public static Result Rest(ContentDb c, SaveData s)
        {
            if (!Expeditions.InnOpen(s)) return Result.Fail("On the road — the caravan's inn opens when the expedition ends");
            int cost = Cost(c, s);
            if (cost == 0) return Result.Fail("Everyone is healthy");
            if (s.Gold < cost) return Result.Fail("Not enough gold");
            s.Gold -= cost;
            foreach (var h in s.Roster) h.Hp = 0;
            return Result.Success("The party is fully rested");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Equip (§24.2, §25.4)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Equipment
    {
        /// <summary>The five jewelry slots, in the order §24.2's equip screen lists them (GG-T89).</summary>
        public static readonly EquipmentSlot[] JewelrySlots =
        {
            EquipmentSlot.Necklace, EquipmentSlot.Earring1, EquipmentSlot.Earring2, EquipmentSlot.Ring1, EquipmentSlot.Ring2,
        };

        private static bool IsArmorSlot(EquipmentSlot slot) => Array.IndexOf(BattleEngine.ArmorSlots, slot) >= 0;
        private static bool IsJewelrySlot(EquipmentSlot slot) => Array.IndexOf(JewelrySlots, slot) >= 0;

        public static string Equipped(HeroSave h, EquipmentSlot slot)
        {
            if (slot == EquipmentSlot.Weapon) return h.WeaponId;
            if (IsArmorSlot(slot)) return h.Armor.TryGetValue(slot, out var a) ? a.ItemId : null;
            if (IsJewelrySlot(slot)) return h.Jewelry.TryGetValue(slot, out var id) ? id : null;
            return null;
        }

        public static bool Fits(ItemDef item, EquipmentSlot slot)
        {
            if (item == null || item.Type != ItemType.Equipment) return false;
            if (slot == EquipmentSlot.Earring1 || slot == EquipmentSlot.Earring2) return item.IsEarring;
            if (slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2) return item.IsRing;
            return item.Slot == slot;
        }

        /// <summary>GG-T52: shape fit (above) plus proficiency — refuses gear <paramref name="h"/> isn't
        /// trained to use. The shape-only overload still drives the Equip screen's bag listing (so a locked
        /// piece the hero owns stays visible with a lock badge instead of disappearing), and bot/shop code
        /// that should never pick gear it can't use calls this one instead.</summary>
        public static bool Fits(ContentDb c, HeroSave h, ItemDef item, EquipmentSlot slot) =>
            Fits(item, slot) && ProficiencyGate(c, h, item) == null;

        /// <summary>Null if <paramref name="h"/> can equip <paramref name="item"/> today; otherwise the name
        /// of the proficiency-unlock passive that would let them (GG-T52) — e.g. "Heavy Armor Proficiency".
        /// Only the Weapon and armor slots are ever gated; jewelry and consumables never are.</summary>
        public static string ProficiencyGate(ContentDb c, HeroSave h, ItemDef item)
        {
            if (item == null || item.Type != ItemType.Equipment) return null;
            if (item.Slot == EquipmentSlot.Weapon)
            {
                if (item.WeaponClass == WeaponClass.Simple) return null;
                if (Progression.EffectiveWeaponProficiencies(c, h).Contains(item.WeaponClass)) return null;
                var passiveId = item.WeaponClass == WeaponClass.Arcane ? "arcane_focus_proficiency" : "martial_weapons_proficiency";
                return c.TryAbility(passiveId)?.Name ?? passiveId;
            }
            if (IsArmorSlot(item.Slot))
            {
                if (item.ArmorWeight <= Progression.EffectiveArmorProficiency(c, h)) return null;
                return c.TryAbility("heavy_armor_proficiency")?.Name ?? "Heavy Armor Proficiency";
            }
            return null;
        }

        /// <summary>Everything in the bag that shape-fits this slot (locked gear included, GG-T52) — the Equip
        /// screen's bag listing. Pass <paramref name="h"/> to additionally drop anything the hero isn't
        /// proficient in (bot/shop shopping lists, which should never pick gear it can't wear).</summary>
        public static List<ItemDef> Candidates(ContentDb c, SaveData s, EquipmentSlot slot, HeroSave h = null) =>
            s.Inventory.Where(kv => kv.Value > 0).Select(kv => c.TryItem(kv.Key))
                .Where(i => Fits(i, slot) && (h == null || ProficiencyGate(c, h, i) == null))
                .OrderByDescending(i => i.Rarity).ThenBy(i => i.Name).ToList();

        /// <summary>True if the one owned (unequipped) copy of this item id is a Masterwork roll (§24.6.2, GG-T46).</summary>
        public static bool IsMasterwork(SaveData s, string itemId) =>
            itemId != null && s.Worn.TryGetValue(itemId, out var list) && list.Any(w => w.Mastery > 1f);

        public static Result Equip(ContentDb c, SaveData s, HeroSave h, EquipmentSlot slot, string itemId)
        {
            var item = c.TryItem(itemId);
            if (!Fits(item, slot)) return Result.Fail("That doesn't go there");
            var gate = ProficiencyGate(c, h, item);
            if (gate != null) return Result.Fail($"Not proficient — needs {gate}");
            if (s.Count(itemId) < 1) return Result.Fail("You don't own that");
            Unequip(c, s, h, slot);
            if (IsJewelrySlot(slot))
            {
                s.AddItem(itemId, -1);
                h.Jewelry[slot] = itemId;
                return Result.Success($"Equipped {item.Name}");
            }
            // Fresh copies first; otherwise the best of the worn ones (per-instance durability, US-246).
            var copy = TakeCopy(s, item);
            s.AddItem(itemId, -1);
            if (slot == EquipmentSlot.Weapon) { h.WeaponId = itemId; h.WeaponDurability = copy.Durability; h.WeaponRepairs = copy.Repairs; h.WeaponMastery = copy.Mastery; }
            else h.Armor[slot] = new EquippedArmor { ItemId = itemId, Durability = copy.Durability, Repairs = copy.Repairs, Mastery = copy.Mastery, Enchant = copy.Enchant };
            return Result.Success(copy.Mastery > 1f ? $"Equipped ★ {item.Name}" : $"Equipped {item.Name}");
        }

        private static WornGear TakeCopy(SaveData s, ItemDef item)
        {
            s.Worn.TryGetValue(item.Id, out var worn);
            int fresh = s.Count(item.Id) - (worn?.Count ?? 0);
            // An enchanted copy (GG-T179) is put on before a fresh one, so taking a piece off and back on keeps its enchant.
            bool enchanted = worn != null && worn.Any(w => w.Enchant != null);
            if (!enchanted && (fresh > 0 || worn == null || worn.Count == 0)) return new WornGear { Durability = item.Durability, Repairs = 0 };
            worn.Sort((a, b) => { int e = (b.Enchant != null).CompareTo(a.Enchant != null); return e != 0 ? e : b.Durability.CompareTo(a.Durability); });
            var best = worn[0];
            worn.RemoveAt(0);
            if (worn.Count == 0) s.Worn.Remove(item.Id);
            return best;
        }

        /// <summary>Returns the piece to the bag, remembering its wear, repairs and Masterwork roll if it isn't factory-fresh.</summary>
        public static Result Unequip(ContentDb c, SaveData s, HeroSave h, EquipmentSlot slot)
        {
            var id = Equipped(h, slot);
            if (id == null) return Result.Fail("Nothing equipped");
            var item = c.TryItem(id);
            if (IsJewelrySlot(slot))
            {
                s.AddItem(id, 1);
                h.Jewelry.Remove(slot);
                return Result.Success("Unequipped");
            }
            int dur = 0, reps = 0; float mastery = 1f; string enchant = null;
            if (slot == EquipmentSlot.Weapon) { dur = h.WeaponDurability; reps = h.WeaponRepairs; mastery = h.WeaponMastery; }
            else if (h.Armor.TryGetValue(slot, out var a)) { dur = a.Durability; reps = a.Repairs; mastery = a.Mastery; enchant = a.Enchant; }
            s.AddItem(id, 1);
            if (item != null && item.Durability > 0 && (dur < item.Durability || reps > 0 || mastery > 1f || enchant != null))
            {
                if (!s.Worn.TryGetValue(id, out var list)) s.Worn[id] = list = new List<WornGear>();
                list.Add(new WornGear { Durability = dur, Repairs = reps, Mastery = mastery, Enchant = enchant });
            }
            if (slot == EquipmentSlot.Weapon) { h.WeaponId = null; h.WeaponDurability = 0; h.WeaponRepairs = 0; h.WeaponMastery = 1f; }
            else h.Armor.Remove(slot);
            return Result.Success("Unequipped");
        }

        /// <summary>Stat preview: the hero's effective stats if <paramref name="itemId"/> replaced the slot.</summary>
        public static (Stats now, Stats then) Compare(ContentDb c, HeroSave h, EquipmentSlot slot, string itemId)
        {
            var now = Progression.Preview(c, h).Effective;
            var copy = new HeroSave
            {
                ClassId = h.ClassId, TotalXp = h.TotalXp,
                WeaponId = h.WeaponId, WeaponMastery = h.WeaponMastery,
                Armor = h.Armor.ToDictionary(kv => kv.Key, kv => new EquippedArmor { ItemId = kv.Value.ItemId, Mastery = kv.Value.Mastery }),
                Jewelry = new Dictionary<EquipmentSlot, string>(h.Jewelry),
            };
            if (slot == EquipmentSlot.Weapon) copy.WeaponId = itemId;
            else if (IsJewelrySlot(slot)) copy.Jewelry[slot] = itemId;
            else copy.Armor[slot] = new EquippedArmor { ItemId = itemId, Mastery = 1f };
            return (now, Progression.Preview(c, copy).Effective);
        }

        /// <summary>The armor sets this hero is wearing pieces of, most pieces first (GG-T182, Status/Equip screens).</summary>
        public static List<SetProgress> WornSets(ContentDb c, HeroSave h) =>
            ArmorSets.Progress(c, h.Armor.Values.Where(a => a?.ItemId != null).Select(a => a.ItemId));

        /// <summary>Set progress if <paramref name="itemId"/> (null = empty) replaced whatever is in <paramref name="slot"/>
        /// — the Equip screen's "3/5 → 4/5" preview.</summary>
        public static List<SetProgress> WornSetsWith(ContentDb c, HeroSave h, EquipmentSlot slot, string itemId)
        {
            var ids = h.Armor.Where(kv => kv.Key != slot && kv.Value?.ItemId != null).Select(kv => kv.Value.ItemId).ToList();
            if (itemId != null && IsArmorSlot(slot)) ids.Add(itemId);
            return ArmorSets.Progress(c, ids);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Abilities: learn + slot (§25.6)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Abilities
    {
        /// <summary>What a hero could learn: its class list plus the general spell catalog (hero spells only).</summary>
        public static List<AbilityDef> Learnable(ContentDb c, HeroSave h)
        {
            var def = c.TryClass(h.ClassId);
            var set = new List<AbilityDef>();
            if (def != null)
                foreach (var id in def.Abilities)
                {
                    var a = c.TryAbility(id);
                    if (a != null && IsHeroUsable(a) && !h.Known.Contains(a.Id) && !set.Contains(a)) set.Add(a);
                }
            foreach (var a in c.Abilities.Values)
                if (IsHeroUsable(a) && !h.Known.Contains(a.Id) && !set.Contains(a) &&
                    (a.Kind == AbilityKind.Spell || a.Universal))
                    set.Add(a);
            return set.OrderBy(a => Price(a)).ThenBy(a => a.Name).ToList();
        }

        public static bool IsHeroUsable(AbilityDef a) => a.Kind != AbilityKind.Passive &&
            (a.Kind == AbilityKind.Spell || a.Kind == AbilityKind.Skill) && !a.Id.StartsWith("charge_") && a.Id != "enrage";

        public static int Price(AbilityDef a) => a.Kind == AbilityKind.Skill ? 300 : 120 + 80 * a.Cost;

        /// <summary>Every hero-usable ability some hero can buy at the Learn screen: any spell, any universal skill, and
        /// every skill on a hero class's list (the union of <see cref="Learnable"/> over the hero classes).</summary>
        public static List<AbilityDef> LearnableByAnyHero(ContentDb c)
        {
            var onHeroLists = new HashSet<string>();
            foreach (var cls in c.Classes.Values)
                if (cls.Team == Team.Hero) foreach (var id in cls.Abilities) onHeroLists.Add(id);
            return c.Abilities.Values
                .Where(a => IsHeroUsable(a) && (a.Kind == AbilityKind.Spell || a.Universal || onHeroLists.Contains(a.Id)))
                .OrderBy(a => a.Id).ToList();
        }

        /// <summary>
        /// GG-T183 reskin audit: the mechanic an ability plays, with its name, numbers (damage, heal, cost, cooldown,
        /// hasten amount, chance) and art stripped away. Two abilities with the same key are the same move with a
        /// different coat of paint. The key is: how it is paid and timed (skill on a cooldown, spell cast on the
        /// timeline, instant spell), its footprint (shape, radius where it changes the footprint, auto/pick, who it
        /// filters, adjacent-only), and each effect it carries — damage (Fire and Lightning separately, since they
        /// thaw/steam and shock/blind), heal or drain, the status it applies or clears, steal, orbs, scan, revive,
        /// bonus-vs-tag, the movement it causes (teleport, knockback, pull, swap, step back) and the timeline change
        /// (hasten, delay). Other elements are resistance-only, so they count as the same mechanic.
        /// </summary>
        public static string MechanicKey(AbilityDef a)
        {
            string pay = a.Kind == AbilityKind.Spell ? (a.CastTime > 0f ? "cast" : "instant") : "skill";
            bool area = a.Shape == TargetShape.Square || a.Shape == TargetShape.Diamond || a.Shape == TargetShape.Cross;
            bool self = a.Shape == TargetShape.Self;
            bool all = a.Shape == TargetShape.AllAllies || a.Shape == TargetShape.AllEnemies;
            var k = new List<string>
            {
                pay,
                a.Shape + (area ? a.Radius.ToString() : ""),
                self || all ? "-" : a.Mode.ToString(),
                self || all ? "-" : a.Filter.ToString(),
                a.AdjacentOnly ? "adj" : "",
            };
            if (a.BaseDamage > 0) k.Add("dmg" + (a.DamageType == DamageType.Fire || a.DamageType == DamageType.Lightning ? ":" + a.DamageType : ""));
            if (a.BaseHeal > 0) k.Add(a.Filter == TargetFilter.EnemyOnly && !self ? "drain" : "heal");
            if (a.ApplyBuff != null) k.Add("buff:" + a.ApplyBuff);
            if (a.RemovesDebuffs) k.Add("cleanse");
            if (a.RemovesBuff != null) k.Add("cure:" + a.RemovesBuff);
            if (a.StealsMana) k.Add("steal");
            if (a.GrantsOrbs > 0) k.Add("orbs");
            if (a.IsScan) k.Add("scan");
            if (a.Revives) k.Add("revive");
            if (a.Escape) k.Add("escape");
            if (a.BonusVsTag != null) k.Add("vs:" + a.BonusVsTag);
            if (a.IsTeleport) k.Add("teleport");
            if (a.Knockback > 0) k.Add("knockback");
            if (a.Pull > 0) k.Add("pull");
            if (a.SwapsPlaces) k.Add("swap");
            if (a.StepBack > 0) k.Add("stepback");
            if (a.HastenU > 0f) k.Add("hasten");
            if (a.DelayU > 0f) k.Add("delay");
            return string.Join("|", k);
        }

        /// <summary>Does the ability do anything at all when it resolves? (GG-T183 found four that did nothing.)</summary>
        public static bool HasEffect(AbilityDef a) =>
            a.BaseDamage > 0 || a.BaseHeal > 0 || a.ApplyBuff != null || a.RemovesDebuffs || a.RemovesBuff != null || a.StealsMana ||
            a.GrantsOrbs > 0 || a.IsScan || a.Revives || a.Escape || a.IsTeleport || a.Knockback > 0 || a.Pull > 0 || a.SwapsPlaces ||
            a.StepBack > 0 || a.HastenU > 0f || a.DelayU > 0f;

        public static Result Learn(ContentDb c, SaveData s, HeroSave h, string abilityId)
        {
            var a = c.TryAbility(abilityId);
            if (a == null || !Learnable(c, h).Contains(a)) return Result.Fail("Can't learn that");
            int price = Price(a);
            if (s.Gold < price) return Result.Fail("Not enough gold");
            s.Gold -= price;
            h.Known.Add(a.Id);
            return Result.Success($"Learned {a.Name}");
        }

        /// <summary>Reorder the bar (owner direction 2026-10-11: organise your abilities): swap two unlocked slots.</summary>
        public static Result Swap(SaveData s, HeroSave h, int a, int b)
        {
            int unlocked = Progression.UnlockedSlots(s);
            if (a < 0 || b < 0 || a >= unlocked || b >= unlocked) return Result.Fail("Slot locked");
            if (a == b) return Result.Fail("Same slot");
            while (h.Bar.Count < Progression.MaxBarSlots) h.Bar.Add(null);
            var t = h.Bar[a]; h.Bar[a] = h.Bar[b]; h.Bar[b] = t;
            return Result.Success("Moved");
        }

        public static Result Assign(ContentDb c, SaveData s, HeroSave h, int slot, string entry)
        {
            if (slot < 0 || slot >= Progression.MaxBarSlots) return Result.Fail("No such slot");
            if (slot >= Progression.UnlockedSlots(s)) return Result.Fail("Slot locked");
            if (entry != null)
            {
                if (entry.StartsWith("item:"))
                {
                    var item = c.TryItem(entry.Substring(5));
                    if (item == null || item.Type != ItemType.Consumable) return Result.Fail("Not usable in battle");
                }
                else if (entry.StartsWith("weapon:"))
                {
                    var w = c.TryItem(entry.Substring(7));
                    if (w == null || w.Slot != EquipmentSlot.Weapon) return Result.Fail("Not a weapon");
                    if (s.Count(w.Id) < 1) return Result.Fail("You need a spare one in your bag");
                }
                else if (!h.Known.Contains(entry)) return Result.Fail("Not learned");
                int dup = h.Bar.IndexOf(entry);
                if (dup >= 0 && dup != slot) h.Bar[dup] = null;
            }
            h.Bar[slot] = entry;
            return Result.Success();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Party & Summon Circle (§23.4, §25.5, §25.10)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Party
    {
        public static Result Toggle(SaveData s, string classId)
        {
            if (s.Hero(classId) == null) return Result.Fail("Not in roster");
            if (s.Party.Contains(classId))
            {
                if (s.Party.Count <= 1) return Result.Fail("Someone has to go");
                s.Party.Remove(classId);
                return Result.Success("Moved to reserve");
            }
            if (s.Party.Count >= Progression.MaxPartySize) return Result.Fail($"Party is full ({Progression.MaxPartySize})");
            s.Party.Add(classId);
            return Result.Success("Joined the party");
        }
    }

    /// <summary>
    /// The one rule for whether a unit can ever be lost for good (GG-T114). Owner rule: "no one dies permanently,
    /// that's lame" — in the normal game nobody is ever lost; heroes, guests, recruits and dialogue-joined heroes fall,
    /// get revived or recover, and come back. A planned opt-in Ironman mode will switch this on; every "unit is lost"
    /// decision (the guest record in <see cref="SaveData.LostGuests"/>, the Summon Circle pool, the guest exclusion in
    /// <c>BattleFactory.Build</c>, the battle's fall announcement via <c>BattleSetup.Permadeath</c>) reads it here,
    /// so Ironman only has to change <see cref="Enabled"/>.
    /// </summary>
    public static class Permadeath
    {
        /// <summary>Always false today — no Ironman mode exists yet (BIBLE §29.1).</summary>
        public static bool Enabled(SaveData s) => false;

        /// <summary>Whether this class has been lost for good. Off → never, even for a save that recorded a loss
        /// under the old "lost forever" guest rule: those guests come back.</summary>
        public static bool IsLost(SaveData s, string classId) => Enabled(s) && s.LostGuests.Contains(classId);
    }

    public static class Summon
    {
        public static int Price(ContentDb c, SaveData s) => c.Summon.BasePrice + c.Summon.PriceStep * s.Recruits;

        public static List<ClassDef> Pool(ContentDb c, SaveData s) =>
            c.Summon.Pool.Where(id => !Permadeath.IsLost(s, id)).Select(c.TryClass).Where(d => d != null).ToList();

        public static Result Recruit(ContentDb c, SaveData s, string classId)
        {
            if (!c.Summon.Pool.Contains(classId)) return Result.Fail("Not summonable");
            if (Permadeath.IsLost(s, classId)) return Result.Fail("Fell in battle for good");
            if (s.Hero(classId) != null) return Result.Fail("Already in your roster");
            int price = Price(c, s);
            if (s.Gold < price) return Result.Fail("Not enough gold");
            s.Gold -= price;
            s.Recruits++;
            Join(c, s, classId);
            return Result.Success($"{c.Class(classId).Name} joins you!");
        }

        /// <summary>Adds a hero to the roster (and the party, if there's room) at the roster's average experience —
        /// shared by the Summon Circle and dialogue's RecruitHero effect (GG-T117). No-op if already owned.</summary>
        public static HeroSave Join(ContentDb c, SaveData s, string classId)
        {
            var existing = s.Hero(classId);
            if (existing != null) return existing;
            var h = Progression.NewHero(c, classId);
            int avg = s.Roster.Count == 0 ? 0 : (int)s.Roster.Average(x => x.TotalXp);
            h.TotalXp = avg; // recruits arrive at the roster's average experience
            h.SkillPoints += Formulas.SkillPointsForLevelUp(1, Progression.Level(h));
            s.Roster.Add(h);
            if (s.Party.Count < Progression.MaxPartySize) s.Party.Add(h.ClassId);
            return h;
        }

        /// <summary>Who a story JoinHero (GG-T118) brings into a save: <paramref name="preferred"/> if it's a hero the
        /// player doesn't have; else an unowned hero of <paramref name="role"/>; else one of a role the roster lacks
        /// (<see cref="StoryRoleOrder"/>); else anyone unowned — or null once every hero is owned. The fallbacks skip
        /// the Summon Circle's pool and the stages' guests so those stay worth having; deterministic (name order) so
        /// a beat's speaker and its joiner always agree. <paramref name="exclude"/> holds heroes already promised to
        /// earlier JoinHero effects of the same dialogue.</summary>
        public static string StoryPick(ContentDb c, SaveData s, string preferred, string role, ICollection<string> exclude = null)
        {
            bool Free(ClassDef d) => d != null && d.Team == Team.Hero && s.Hero(d.Id) == null && !Permadeath.IsLost(s, d.Id) && (exclude == null || !exclude.Contains(d.Id));
            if (preferred != null && Free(c.TryClass(preferred))) return preferred;
            var guests = new HashSet<string>(c.Stages.Values.SelectMany(st => st.Waves).SelectMany(w => w.Guests).Select(g => g.Class));
            var pool = c.Classes.Values.Where(d => Free(d) && !c.Summon.Pool.Contains(d.Id) && !guests.Contains(d.Id))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
            if (role != null && pool.FirstOrDefault(d => d.Role == role) is ClassDef byRole) return byRole.Id;
            var have = new HashSet<string>(s.Roster.Select(h => c.TryClass(h.ClassId)?.Role).Where(r => r != null));
            foreach (var r in StoryRoleOrder)
                if (!have.Contains(r) && pool.FirstOrDefault(d => d.Role == r) is ClassDef missing) return missing.Id;
            return pool.FirstOrDefault()?.Id;
        }

        /// <summary>The order a JoinHero with no usable class or role fills gaps in the roster.</summary>
        public static readonly string[] StoryRoleOrder = { "Healer", "Tank", "Mage", "Support", "Striker", "Ranged" };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bounty board (§22.4)
    // ─────────────────────────────────────────────────────────────────────────────

    public static class Bounties
    {
        public static List<BountyDef> Posted(ContentDb c, SaveData s) =>
            c.Bounties.Values.Where(b => !s.Bounty.Claimed.Contains(b.Id)).OrderBy(b => b.RewardGold).ToList();

        public static BountyDef Active(ContentDb c, SaveData s) => s.Bounty.ActiveId != null && c.Bounties.TryGetValue(s.Bounty.ActiveId, out var b) ? b : null;

        public static bool IsComplete(ContentDb c, SaveData s) { var b = Active(c, s); return b != null && s.Bounty.Progress >= b.Kills; }

        public static Result Accept(ContentDb c, SaveData s, string id)
        {
            if (!c.Bounties.ContainsKey(id) || s.Bounty.Claimed.Contains(id)) return Result.Fail("Not available");
            if (s.Bounty.ActiveId != null) return Result.Fail("Finish or abandon your current bounty first");
            s.Bounty.ActiveId = id;
            s.Bounty.Progress = 0;
            return Result.Success("Bounty accepted");
        }

        public static Result Abandon(SaveData s)
        {
            if (s.Bounty.ActiveId == null) return Result.Fail("No active bounty");
            s.Bounty.ActiveId = null;
            s.Bounty.Progress = 0;
            return Result.Success("Bounty abandoned");
        }

        public static string RecordKills(ContentDb c, SaveData s, Dictionary<string, int> kills)
        {
            var b = Active(c, s);
            if (b == null) return null;
            if (kills.TryGetValue(b.TargetClass, out var n) && n > 0)
            {
                s.Bounty.Progress = Math.Min(b.Kills, s.Bounty.Progress + n);
                return $"{b.Name}: {s.Bounty.Progress}/{b.Kills}";
            }
            return null;
        }

        public static Result Claim(ContentDb c, SaveData s)
        {
            var b = Active(c, s);
            if (b == null) return Result.Fail("No active bounty");
            if (s.Bounty.Progress < b.Kills) return Result.Fail("Not finished yet");
            s.Gold += b.RewardGold;
            if (b.RewardItem != null) s.AddItem(b.RewardItem, 1);
            s.Bounty.Claimed.Add(b.Id);
            s.Bounty.ActiveId = null;
            s.Bounty.Progress = 0;
            return Result.Success($"Claimed {b.RewardGold}g");
        }
    }
}
