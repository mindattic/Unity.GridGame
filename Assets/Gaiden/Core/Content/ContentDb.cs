using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>All loaded content, indexed by id. Immutable after load.</summary>
    public sealed class ContentDb
    {
        public readonly Dictionary<string, ClassDef> Classes = new Dictionary<string, ClassDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, BuffDef> Buffs = new Dictionary<string, BuffDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, RecipeDef> Recipes = new Dictionary<string, RecipeDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, DropTableDef> DropTables = new Dictionary<string, DropTableDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, StageDef> Stages = new Dictionary<string, StageDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, TrapDef> Traps = new Dictionary<string, TrapDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, PickupDef> Pickups = new Dictionary<string, PickupDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, BountyDef> Bounties = new Dictionary<string, BountyDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, EnchantDef> Enchants = new Dictionary<string, EnchantDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>armorenchants.json (GG-T179): passive-granting armor enchants, in file order, and by id.</summary>
        public readonly List<ArmorEnchantDef> ArmorEnchantOrder = new List<ArmorEnchantDef>();
        public readonly Dictionary<string, ArmorEnchantDef> ArmorEnchants = new Dictionary<string, ArmorEnchantDef>(StringComparer.OrdinalIgnoreCase);
        public ArmorEnchantDef TryArmorEnchant(string id) => id != null && ArmorEnchants.TryGetValue(id, out var e) ? e : null;
        public readonly List<StageDef> StageOrder = new List<StageDef>();
        public CampaignDef Campaign = new CampaignDef();
        public SummonDef Summon = new SummonDef();
        public readonly Dictionary<string, CaravanUpgradeDef> Caravan = new Dictionary<string, CaravanUpgradeDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SkillTreeDef> SkillTrees = new Dictionary<string, SkillTreeDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>dialogues.json in file order (§27.1, GG-T117) — the first matching beat wins, so order matters.</summary>
        public readonly List<DialogueDef> DialogueOrder = new List<DialogueDef>();
        public readonly Dictionary<string, DialogueDef> Dialogues = new Dictionary<string, DialogueDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>armorsets.json (GG-T182), in file order (campaign level order).</summary>
        public readonly List<ArmorSetDef> ArmorSetOrder = new List<ArmorSetDef>();
        public readonly Dictionary<string, ArmorSetDef> ArmorSets = new Dictionary<string, ArmorSetDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Armor piece item id → the set it belongs to (built at load; a piece is in at most one set).</summary>
        public readonly Dictionary<string, ArmorSetDef> SetByItem = new Dictionary<string, ArmorSetDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>encounters.json (GG-T184), in file order, and by id.</summary>
        public readonly List<EncounterGroupDef> EncounterOrder = new List<EncounterGroupDef>();
        public readonly Dictionary<string, EncounterGroupDef> Encounters = new Dictionary<string, EncounterGroupDef>(StringComparer.OrdinalIgnoreCase);
        public EncounterGroupDef TryEncounter(string id) => id != null && Encounters.TryGetValue(id, out var g) ? g : null;

        public ArmorSetDef TryArmorSetOf(string itemId) => itemId != null && SetByItem.TryGetValue(itemId, out var a) ? a : null;

        public ClassDef Class(string id) => Get(Classes, id, "class");
        public AbilityDef Ability(string id) => Get(Abilities, id, "ability");
        public BuffDef Buff(string id) => Get(Buffs, id, "buff");
        public ItemDef Item(string id) => Get(Items, id, "item");
        public StageDef Stage(string id) => Get(Stages, id, "stage");
        public RecipeDef Recipe(string id) => Get(Recipes, id, "recipe");

        public AbilityDef TryAbility(string id) => id != null && Abilities.TryGetValue(id, out var a) ? a : null;
        public ItemDef TryItem(string id) => id != null && Items.TryGetValue(id, out var a) ? a : null;
        public BuffDef TryBuff(string id) => id != null && Buffs.TryGetValue(id, out var a) ? a : null;
        public ClassDef TryClass(string id) => id != null && Classes.TryGetValue(id, out var a) ? a : null;
        public RecipeDef TryRecipe(string id) => id != null && Recipes.TryGetValue(id, out var a) ? a : null;
        public SkillTreeDef TrySkillTree(string id) => id != null && SkillTrees.TryGetValue(id, out var a) ? a : null;

        private static T Get<T>(Dictionary<string, T> map, string id, string kind)
        {
            if (id != null && map.TryGetValue(id, out var v)) return v;
            throw new KeyNotFoundException($"Unknown {kind} '{id}'");
        }

        /// <summary>Campaign stages in play order (themes in order, stages in order).</summary>
        public List<StageDef> CampaignStages()
        {
            var list = new List<StageDef>();
            foreach (var t in Campaign.Themes)
                foreach (var s in t.Stages)
                    if (Stages.TryGetValue(s, out var def)) list.Add(def);
            return list;
        }

        public StageDef TryStage(string id) => id != null && Stages.TryGetValue(id, out var st) ? st : null;

        public ThemeDef ThemeOf(string stageId)
        {
            // Stages/Validate both key stage ids case-insensitively (StringComparer.OrdinalIgnoreCase) —
            // List<string>.Contains here was ordinal-exact, so a theme's stage id differing only in case
            // from its StageDef.Id passed Validate but silently resolved to no theme (test/standalone stage
            // tracking: no expedition progress, no carried orbs, no post-battle bookkeeping for that stage).
            if (stageId == null) return null;
            foreach (var t in Campaign.Themes)
                foreach (var s in t.Stages)
                    if (string.Equals(s, stageId, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }
    }
}
