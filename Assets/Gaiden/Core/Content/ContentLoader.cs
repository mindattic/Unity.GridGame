using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GridGame.Core.Content
{
    /// <summary>
    /// Builds a <see cref="ContentDb"/> from the JSON documents. The text source is injected
    /// (Unity: Resources TextAssets; tests: files on disk) so the loader stays engine-free.
    /// </summary>
    public static class ContentLoader
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            // Replace (not append to) collections that have initializers, e.g. HeroSave.Bar's five empty slots.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <param name="readText">Returns the JSON text for a file name (no extension), or null if absent.</param>
        public static ContentDb Load(Func<string, string> readText)
        {
            var db = new ContentDb();
            AddAll(db.Classes, ReadList<ClassDef>(readText, "classes"), c => c.Id);
            AddAll(db.Abilities, ReadList<AbilityDef>(readText, "abilities"), c => c.Id);
            AddAll(db.Buffs, ReadList<BuffDef>(readText, "buffs"), c => c.Id);
            AddAll(db.Items, ReadList<ItemDef>(readText, "items"), c => c.Id);
            AddAll(db.Recipes, ReadList<RecipeDef>(readText, "recipes"), c => c.Id);
            AddAll(db.DropTables, ReadList<DropTableDef>(readText, "droptables"), c => c.Id);
            var encounters = ReadList<EncounterGroupDef>(readText, "encounters");
            AddAll(db.Encounters, encounters, x => x.Id);
            db.EncounterOrder.AddRange(encounters);
            var stages = ReadList<StageDef>(readText, "stages");
            // A wave that names a group (GG-T184) gets the group's foes, formation and level; Validate reports a bad id.
            foreach (var st in stages)
                foreach (var w in st.Waves)
                    if (w.Group != null && w.Enemies.Count == 0 && db.TryEncounter(w.Group) is EncounterGroupDef g)
                        w.Enemies = EncounterCatalog.Spawns(g, g.Level);
            AddAll(db.Stages, stages, c => c.Id);
            db.StageOrder.AddRange(stages);
            AddAll(db.Traps, ReadList<TrapDef>(readText, "traps"), c => c.Id);
            AddAll(db.Bounties, ReadList<BountyDef>(readText, "bounties"), c => c.Id);
            AddAll(db.Enchants, ReadList<EnchantDef>(readText, "enchants"), c => c.Id);
            var armorEnchants = ReadList<ArmorEnchantDef>(readText, "armorenchants");
            AddAll(db.ArmorEnchants, armorEnchants, x => x.Id);
            db.ArmorEnchantOrder.AddRange(armorEnchants);
            db.Campaign = ReadOne<CampaignDef>(readText, "campaign") ?? new CampaignDef();
            db.Summon = ReadOne<SummonDef>(readText, "summon") ?? new SummonDef();
            AddAll(db.Caravan, ReadList<CaravanUpgradeDef>(readText, "caravan"), x => x.Id);
            AddAll(db.Pickups, ReadList<PickupDef>(readText, "pickups"), x => x.Id);
            AddAll(db.SkillTrees, ReadList<SkillTreeDef>(readText, "skilltrees"), x => x.Id);
            var dialogues = ReadList<DialogueDef>(readText, "dialogues");
            AddAll(db.Dialogues, dialogues, x => x.Id);
            db.DialogueOrder.AddRange(dialogues);
            var sets = ReadList<ArmorSetDef>(readText, "armorsets");
            AddAll(db.ArmorSets, sets, x => x.Id);
            db.ArmorSetOrder.AddRange(sets);
            // First set wins a piece listed twice; Validate reports the clash.
            foreach (var set in sets)
                foreach (var piece in set.Pieces)
                    if (piece != null && !db.SetByItem.ContainsKey(piece)) db.SetByItem[piece] = set;
            return db;
        }

        private static List<T> ReadList<T>(Func<string, string> readText, string name)
        {
            var text = readText(name);
            if (string.IsNullOrWhiteSpace(text)) return new List<T>();
            try { return JsonConvert.DeserializeObject<List<T>>(text, Settings) ?? new List<T>(); }
            catch (Exception e) { throw new FormatException($"Content file '{name}.json' is invalid: {e.Message}", e); }
        }

        private static T ReadOne<T>(Func<string, string> readText, string name) where T : class
        {
            var text = readText(name);
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonConvert.DeserializeObject<T>(text, Settings); }
            catch (Exception e) { throw new FormatException($"Content file '{name}.json' is invalid: {e.Message}", e); }
        }

        private static void AddAll<T>(Dictionary<string, T> map, List<T> items, Func<T, string> key)
        {
            foreach (var item in items)
            {
                if (item == null) throw new FormatException($"{typeof(T).Name}: null entry");
                var k = key(item);
                if (string.IsNullOrEmpty(k)) throw new FormatException($"{typeof(T).Name} with empty Id");
                if (map.ContainsKey(k)) throw new FormatException($"Duplicate {typeof(T).Name} id '{k}'");
                map[k] = item;
            }
        }

        /// <summary>Cross-reference check; returns human-readable problems (empty = valid).</summary>
        /// <param name="portraitExists">Whether Resources/Portraits/&lt;id&gt; exists (dialogue speakers); null skips that
        /// art check (Core can't see the art — the app and the dotnet tests pass their own lookup).</param>
        public static List<string> Validate(ContentDb db, Func<string, bool> portraitExists = null)
        {
            var p = new List<string>();
            void NeedClass(string id, string where) { if (id != null && !db.Classes.ContainsKey(id)) p.Add($"{where}: unknown class '{id}'"); }
            void NeedItem(string id, string where) { if (id != null && !db.Items.ContainsKey(id)) p.Add($"{where}: unknown item '{id}'"); }
            void NeedAbility(string id, string where) { if (id != null && !db.Abilities.ContainsKey(id)) p.Add($"{where}: unknown ability '{id}'"); }
            void NeedBuff(string id, string where) { if (id != null && !db.Buffs.ContainsKey(id)) p.Add($"{where}: unknown buff '{id}'"); }

            foreach (var c in db.Classes.Values)
            {
                if (c.DropTable != null && !db.DropTables.ContainsKey(c.DropTable)) p.Add($"class {c.Id}: unknown drop table '{c.DropTable}'");
                foreach (var a in c.Abilities) NeedAbility(a, $"class {c.Id} abilities");
                foreach (var s in c.DefaultLoadout)
                {
                    if (s == null) { p.Add($"class {c.Id} loadout: null entry"); continue; }
                    if (s.StartsWith("item:")) NeedItem(s.Substring(5), $"class {c.Id} loadout");
                    else NeedAbility(s, $"class {c.Id} loadout");
                }
                NeedAbility(c.ChargeAbility, $"class {c.Id} charge");
                if (c.Trap != null && !db.Traps.ContainsKey(c.Trap)) p.Add($"class {c.Id}: unknown trap '{c.Trap}'");
                if (c.Trail != null && !db.Traps.ContainsKey(c.Trail)) p.Add($"class {c.Id}: unknown trail '{c.Trail}'");
                NeedBuff(c.OnHitBuff, $"class {c.Id} on-hit buff");
                NeedClass(c.Remains, $"class {c.Id} remains");
                NeedClass(c.LaysEgg, $"class {c.Id} egg");
                foreach (var ph in c.BossPhases) NeedAbility(ph.OnEnterAbility, $"class {c.Id} boss phase");
            }
            foreach (var a in db.Abilities.Values) { NeedBuff(a.ApplyBuff, $"ability {a.Id}"); NeedBuff(a.RemovesBuff, $"ability {a.Id} removes"); }
            foreach (var b in db.Buffs.Values) NeedBuff(b.OnExpireApply, $"buff {b.Id}");
            foreach (var i in db.Items.Values)
            {
                NeedAbility(i.OnUseAbility, $"item {i.Id}");
                NeedBuff(i.CuresBuff, $"item {i.Id} cures");
                NeedBuff(i.PincerApplyBuff, $"item {i.Id} pincer buff");
                if (i.Type == ItemType.RecipeScroll)
                {
                    if (i.TeachesRecipe == null) p.Add($"item {i.Id}: RecipeScroll needs TeachesRecipe");
                    else if (!db.Recipes.ContainsKey(i.TeachesRecipe)) p.Add($"item {i.Id}: unknown recipe '{i.TeachesRecipe}'");
                }
            }
            foreach (var r in db.Recipes.Values)
            {
                NeedItem(r.Result, $"recipe {r.Id}");
                NeedItem(r.BaseItem, $"recipe {r.Id} base");
                foreach (var ic in r.Inputs) NeedItem(ic.Item, $"recipe {r.Id}");
                NeedItem(r.PotencyResult, $"recipe {r.Id} potency");
                foreach (var ic in r.PotencyInputs) NeedItem(ic.Item, $"recipe {r.Id} potency");
                // GG-T177 (§25.0.6): a starter recipe is the guided first forge — always affordable, so no gold.
                if (r.Starter && (r.Station != CraftStation.Forge || r.BaseItem != null || r.Tier > 1 || r.Gold != 0 || r.ResultCount != 1
                                  || (db.Items.TryGetValue(r.Result ?? "", out var made) && made.Slot != EquipmentSlot.Weapon)))
                    p.Add($"recipe {r.Id}: a Starter recipe is an Apprentice Forge recipe for one weapon that costs no gold");
            }
            foreach (var d in db.DropTables.Values)
                foreach (var e in d.Entries) NeedItem(e.Item, $"droptable {d.Id}");
            foreach (var s in db.Stages.Values)
                foreach (var w in s.Waves)
                {
                    foreach (var e in w.Enemies) NeedClass(e.Class, $"stage {s.Id}");
                    foreach (var g in w.Guests) NeedClass(g.Class, $"stage {s.Id} guest");
                    NeedClass(w.SurpriseSpeaker, $"stage {s.Id} surprise speaker");
                }
            // ContentDb.ThemeOf resolves a stage id to the *first* theme that lists it — a stage accidentally
            // listed in two themes would silently belong to the wrong one there (expedition tracking,
            // carried orbs) with no error here to catch it, so every stage id must be theme-unique.
            var stageThemeOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in db.Campaign.Themes)
                foreach (var s in t.Stages)
                {
                    if (!db.Stages.ContainsKey(s)) p.Add($"theme {t.Id}: unknown stage '{s}'");
                    else if (stageThemeOwner.TryGetValue(s, out var owner)) p.Add($"theme {t.Id}: stage '{s}' already belongs to theme '{owner}'");
                    else stageThemeOwner[s] = t.Id;
                }
            foreach (var t in db.Traps.Values) NeedBuff(t.ApplyBuff, $"trap {t.Id}");
            foreach (var pk in db.Pickups.Values)
            {
                NeedBuff(pk.ApplyBuff, $"pickup {pk.Id}");
                if (pk.Weight <= 0) p.Add($"pickup {pk.Id}: weight must be positive");
            }
            foreach (var c in db.Classes.Values)
                if (c.Team == Team.Hero && (c.Role == null || !HeroRoles.Contains(c.Role))) p.Add($"class {c.Id}: hero needs a Role (Tank/Healer/Mage/Striker/Support/Ranged), has '{c.Role}'");
            foreach (var b in db.Bounties.Values) { NeedClass(b.TargetClass, $"bounty {b.Id}"); NeedItem(b.RewardItem, $"bounty {b.Id}"); }
            foreach (var c in db.Summon.Pool) NeedClass(c, "summon pool");
            foreach (var e in db.Enchants.Values) NeedItem(e.Essence, $"enchant {e.Id}");
            // GG-T179: an armor enchant grants a real Passive ability and costs real materials.
            foreach (var e in db.ArmorEnchantOrder)
            {
                string w = $"armor enchant {e.Id}";
                if (string.IsNullOrWhiteSpace(e.Name)) p.Add($"{w}: needs a Name");
                if (!db.Abilities.TryGetValue(e.Passive ?? "", out var passive)) p.Add($"{w}: unknown passive '{e.Passive}'");
                else if (passive.Kind != AbilityKind.Passive) p.Add($"{w}: '{e.Passive}' isn't a Passive ability");
                if (e.Inputs.Count == 0) p.Add($"{w}: needs inputs");
                foreach (var ic in e.Inputs) { NeedItem(ic.Item, w); if (ic.Count <= 0) p.Add($"{w}: input counts must be positive"); }
                if (e.Gold < 0) p.Add($"{w}: gold can't be negative");
                if (e.Tier < 1 || e.Tier > 3) p.Add($"{w}: tier must be 1-3");
            }

            // Skill trees (§25.6.1, GG-T50): every node's grant and prerequisites must resolve, and every
            // hero class must name a home tree that exists.
            foreach (var t in db.SkillTrees.Values)
            {
                var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in t.Nodes)
                {
                    if (string.IsNullOrEmpty(n.Id)) { p.Add($"tree {t.Id}: node with empty Id"); continue; }
                    if (!nodeIds.Add(n.Id)) p.Add($"tree {t.Id}: duplicate node id '{n.Id}'");
                }
                foreach (var n in t.Nodes)
                {
                    NeedAbility(n.Grants, $"tree {t.Id} node {n.Id}");
                    if (n.Cost <= 0) p.Add($"tree {t.Id} node {n.Id}: cost must be positive");
                    if (n.RequiredPointsInTree < 0) p.Add($"tree {t.Id} node {n.Id}: required points can't be negative");
                    foreach (var req in n.Prerequisites)
                    {
                        if (!nodeIds.Contains(req)) p.Add($"tree {t.Id} node {n.Id}: unknown prerequisite '{req}'");
                        else if (req == n.Id) p.Add($"tree {t.Id} node {n.Id}: can't require itself");
                    }
                }
                // Cycle check: walk each node's prerequisite chain and fail only if it revisits a node
                // already on the *current* path. Two branches sharing a common ancestor (e.g. a finale
                // node requiring two capstones that both trace back to the same root) is a valid diamond,
                // not a cycle — tracking "seen anywhere" instead of "seen on this path" flagged those too.
                foreach (var n in t.Nodes)
                {
                    var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (HasCycle(n)) p.Add($"tree {t.Id} node {n.Id}: prerequisite cycle");

                    bool HasCycle(SkillNodeDef cur)
                    {
                        if (!onPath.Add(cur.Id)) return true;
                        foreach (var req in cur.Prerequisites)
                        {
                            var reqNode = t.Nodes.Find(x => string.Equals(x.Id, req, StringComparison.OrdinalIgnoreCase));
                            if (reqNode != null && HasCycle(reqNode)) return true;
                        }
                        onPath.Remove(cur.Id);
                        return false;
                    }
                }
            }
            foreach (var c in db.Classes.Values)
            {
                if (c.Team != Team.Hero) continue;
                if (string.IsNullOrEmpty(c.HomeTree)) p.Add($"class {c.Id}: hero needs a HomeTree");
                else if (!db.SkillTrees.ContainsKey(c.HomeTree)) p.Add($"class {c.Id}: unknown home tree '{c.HomeTree}'");
            }
            foreach (var d in db.DialogueOrder) ValidateDialogue(db, d, portraitExists, p);
            foreach (var set in db.ArmorSetOrder) ValidateArmorSet(db, set, p);
            EncounterCatalog.Validate(db, p);
            return p;
        }

        /// <summary>Armor sets (GG-T182): five real armor pieces, one per armor slot, all the set's weight, no piece in
        /// two sets, and bonuses at distinct piece counts between 2 and 5.</summary>
        private static void ValidateArmorSet(ContentDb db, ArmorSetDef set, List<string> p)
        {
            string w = $"armor set {set.Id}";
            if (string.IsNullOrWhiteSpace(set.Name)) p.Add($"{w}: needs a Name");
            if (set.Pieces.Count != 5) p.Add($"{w}: needs exactly 5 pieces, has {set.Pieces.Count}");
            var slots = new HashSet<EquipmentSlot>();
            foreach (var id in set.Pieces)
            {
                var item = db.TryItem(id);
                if (item == null) { p.Add($"{w}: unknown item '{id}'"); continue; }
                if (item.Type != ItemType.Equipment || !IsArmorSlot(item.Slot)) { p.Add($"{w}: '{id}' isn't a Head/Torso/Legs/Gloves/Boots piece"); continue; }
                if (!slots.Add(item.Slot)) p.Add($"{w}: two pieces for {item.Slot}");
                if (item.ArmorWeight != set.Weight) p.Add($"{w}: '{id}' is {item.ArmorWeight}, set is {set.Weight}");
                if (db.TryArmorSetOf(id) != set) p.Add($"{w}: '{id}' already belongs to set '{db.TryArmorSetOf(id)?.Id}'");
            }
            var counts = new HashSet<int>();
            foreach (var b in set.Bonuses)
            {
                if (b == null) { p.Add($"{w}: null bonus"); continue; }
                if (b.Pieces < 2 || b.Pieces > 5) p.Add($"{w}: bonus at {b.Pieces} pieces (must be 2-5)");
                if (!counts.Add(b.Pieces)) p.Add($"{w}: two bonuses at {b.Pieces} pieces");
            }
            if (set.Bonuses.Count == 0) p.Add($"{w}: has no bonuses");
        }

        private static bool IsArmorSlot(EquipmentSlot s) =>
            s == EquipmentSlot.Head || s == EquipmentSlot.Torso || s == EquipmentSlot.Legs || s == EquipmentSlot.Gloves || s == EquipmentSlot.Boots;

        public const string NpcFlagPrefix = "npc:";

        /// <summary>The Merchant's cheap Empty Bottle (GG-T176): an Alchemy recipe that takes one is a "bottle recipe",
        /// brewed from it plus looted monster trophies.</summary>
        public const string EmptyBottle = "empty_bottle";

        /// <summary>Every hero Role a class (or a dialogue's JoinHero) may name.</summary>
        public static readonly HashSet<string> HeroRoles = new HashSet<string> { "Tank", "Healer", "Mage", "Striker", "Support", "Ranged" };

        /// <summary>One dialogue (§27.1, GG-T117): every reference resolves, every node is reachable from the start,
        /// and every choice list has a way out that no condition can hide — same severity as a dangling class id.</summary>
        private static void ValidateDialogue(ContentDb db, DialogueDef d, Func<string, bool> portraitExists, List<string> p)
        {
            string w = $"dialogue {d.Id}";
            bool IsNpc(string id) => id != null && Array.IndexOf(DialogueDef.Npcs, id) >= 0;
            bool IsHero(string id) => db.TryClass(id)?.Team == Team.Hero;
            var trigger = d.Trigger ?? new DialogueTrigger();
            switch (trigger.Kind)
            {
                case DialogueTriggerKind.ThemeStart:
                    if (!db.Campaign.Themes.Exists(t => string.Equals(t.Id, trigger.Id, StringComparison.OrdinalIgnoreCase))) p.Add($"{w}: unknown trigger theme '{trigger.Id}'");
                    break;
                case DialogueTriggerKind.StageCleared:
                    if (trigger.Id == null || !db.Stages.ContainsKey(trigger.Id)) p.Add($"{w}: unknown trigger stage '{trigger.Id}'");
                    break;
                case DialogueTriggerKind.GearWorn:
                    if (trigger.Percent < 1 || trigger.Percent > 99) p.Add($"{w}: GearWorn needs a Percent of 1-99, has {trigger.Percent}");
                    if (trigger.Id != null && !db.Campaign.Themes.Exists(t => string.Equals(t.Id, trigger.Id, StringComparison.OrdinalIgnoreCase))) p.Add($"{w}: unknown GearWorn fallback theme '{trigger.Id}'");
                    break;
                case DialogueTriggerKind.CanBrew:
                    if (!new List<RecipeDef>(db.Recipes.Values).Exists(r => r.Station == CraftStation.Alchemy && r.Tier <= 1 && r.Inputs.Exists(i => i.Item == EmptyBottle)))
                        p.Add($"{w}: CanBrew needs an Apprentice-tier Alchemy recipe that takes an {EmptyBottle}");
                    if (trigger.Id != null && !db.Campaign.Themes.Exists(t => string.Equals(t.Id, trigger.Id, StringComparison.OrdinalIgnoreCase))) p.Add($"{w}: unknown CanBrew fallback theme '{trigger.Id}'");
                    break;
                case DialogueTriggerKind.FirstArmor:
                    if (trigger.Id != null && !db.Campaign.Themes.Exists(t => string.Equals(t.Id, trigger.Id, StringComparison.OrdinalIgnoreCase))) p.Add($"{w}: unknown FirstArmor fallback theme '{trigger.Id}'");
                    break;
                case DialogueTriggerKind.Talk:
                    if (trigger.Id == "party") break;
                    if (trigger.Id != null && trigger.Id.StartsWith(NpcFlagPrefix)) { if (!IsNpc(trigger.Id.Substring(NpcFlagPrefix.Length))) p.Add($"{w}: unknown Talk NPC '{trigger.Id}'"); }
                    else if (!IsHero(trigger.Id)) p.Add($"{w}: Talk needs \"party\", \"npc:<id>\" or a hero class, has '{trigger.Id}'");
                    break;
            }
            bool talkWithHero = trigger.Kind == DialogueTriggerKind.Talk && trigger.Id != null && !trigger.Id.StartsWith(NpcFlagPrefix);
            bool joinsHero = false;
            foreach (var e in d.AllEffects()) joinsHero |= e?.Type == DialogueEffectType.JoinHero;

            void Cond(DialogueCondition c, string where)
            {
                if (c == null) { p.Add($"{where}: null condition"); return; }
                switch (c.Type)
                {
                    case DialogueConditionType.FlagSet:
                    case DialogueConditionType.FlagNotSet:
                        if (string.IsNullOrEmpty(c.Id)) p.Add($"{where}: {c.Type} needs a flag Id"); break;
                    case DialogueConditionType.HasItem:
                        if (!db.Items.ContainsKey(c.Id ?? "")) p.Add($"{where}: unknown item '{c.Id}'"); break;
                    case DialogueConditionType.GoldAtLeast:
                        if (c.Amount <= 0) p.Add($"{where}: GoldAtLeast needs a positive Amount"); break;
                    case DialogueConditionType.PartyHas:
                    case DialogueConditionType.RosterHas:
                    case DialogueConditionType.RosterLacks:
                        if (!IsHero(c.Id)) p.Add($"{where}: {c.Type} needs a hero class, has '{c.Id}'"); break;
                    case DialogueConditionType.NpcJoined:
                        if (!IsNpc(c.Id)) p.Add($"{where}: unknown NPC '{c.Id}'"); break;
                }
            }
            void Effect(DialogueEffect e, string where)
            {
                if (e == null) { p.Add($"{where}: null effect"); return; }
                switch (e.Type)
                {
                    case DialogueEffectType.SetFlag:
                    case DialogueEffectType.ClearFlag:
                        if (string.IsNullOrEmpty(e.Id)) p.Add($"{where}: {e.Type} needs a flag Id");
                        else if (e.Id.StartsWith(NpcFlagPrefix)) p.Add($"{where}: flag '{e.Id}' is reserved — use JoinNpc");
                        break;
                    case DialogueEffectType.GrantGold:
                        if (e.Amount == 0) p.Add($"{where}: GrantGold needs a non-zero Amount"); break;
                    case DialogueEffectType.GrantItem:
                        if (!db.Items.ContainsKey(e.Id ?? "")) p.Add($"{where}: unknown item '{e.Id}'");
                        if (e.Amount < 0) p.Add($"{where}: GrantItem can't take items away"); break;
                    case DialogueEffectType.RecruitHero:
                        if (!IsHero(e.Id)) p.Add($"{where}: RecruitHero needs a hero class, has '{e.Id}'"); break;
                    case DialogueEffectType.JoinNpc:
                        if (!IsNpc(e.Id)) p.Add($"{where}: unknown NPC '{e.Id}'"); break;
                    case DialogueEffectType.JoinHero:
                        if (e.Id == null && e.Role == null) p.Add($"{where}: JoinHero needs a hero class Id, a Role, or both");
                        if (e.Id != null && !IsHero(e.Id)) p.Add($"{where}: JoinHero needs a hero class, has '{e.Id}'");
                        if (e.Role != null && !HeroRoles.Contains(e.Role)) p.Add($"{where}: JoinHero has unknown Role '{e.Role}'");
                        break;
                }
            }
            foreach (var c in d.Conditions) Cond(c, w);

            var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in d.Speakers)
            {
                if (s == null || string.IsNullOrEmpty(s.Id)) { p.Add($"{w}: speaker with empty Id"); continue; }
                if (!speakers.Add(s.Id)) p.Add($"{w}: duplicate speaker '{s.Id}'");
                if (s.Class == "$lead") { }
                else if (s.Class == "$with") { if (!talkWithHero) p.Add($"{w} speaker {s.Id}: \"$with\" only fits a Talk with a party member"); }
                else if (s.Class == "$recruit") { if (!joinsHero) p.Add($"{w} speaker {s.Id}: \"$recruit\" needs a JoinHero effect in this dialogue"); }
                else if (s.Class != null) { if (db.TryClass(s.Class) == null) p.Add($"{w} speaker {s.Id}: unknown class '{s.Class}'"); }
                else if (string.IsNullOrEmpty(s.Portrait) || string.IsNullOrEmpty(s.Name)) p.Add($"{w} speaker {s.Id}: needs a Class, or a Name and a Portrait");
                var portrait = s.Portrait ?? (s.Class != null && !s.Class.StartsWith("$") ? s.Class : null);
                if (portrait != null && portraitExists != null && !portraitExists(portrait)) p.Add($"{w} speaker {s.Id}: missing portrait '{portrait}'");
            }

            if (d.Nodes.Count == 0) { p.Add($"{w}: has no nodes"); return; }
            var nodes = new Dictionary<string, DialogueNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in d.Nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) { p.Add($"{w}: node with empty Id"); continue; }
                if (nodes.ContainsKey(n.Id)) p.Add($"{w}: duplicate node '{n.Id}'"); else nodes[n.Id] = n;
            }
            string start = d.Start ?? d.Nodes[0]?.Id;
            if (start == null || !nodes.ContainsKey(start)) p.Add($"{w}: unknown start node '{start}'");
            foreach (var n in nodes.Values)
            {
                string nw = $"{w} node {n.Id}";
                if (n.Speaker == null || !speakers.Contains(n.Speaker)) p.Add($"{nw}: unknown speaker '{n.Speaker}'");
                if (string.IsNullOrWhiteSpace(n.Text)) p.Add($"{nw}: empty text");
                if (n.Next != null && !nodes.ContainsKey(n.Next)) p.Add($"{nw}: unknown next node '{n.Next}'");
                if (n.Next != null && n.Choices.Count > 0) p.Add($"{nw}: has both Next and Choices");
                foreach (var e in n.Effects) Effect(e, nw);
                for (int i = 0; i < n.Choices.Count; i++)
                {
                    var ch = n.Choices[i];
                    string cw = $"{nw} choice {i + 1}";
                    if (ch == null) { p.Add($"{cw}: null"); continue; }
                    if (string.IsNullOrWhiteSpace(ch.Label)) p.Add($"{cw}: empty label");
                    if (ch.Next != null && !nodes.ContainsKey(ch.Next)) p.Add($"{cw}: unknown next node '{ch.Next}'");
                    foreach (var c in ch.Conditions) Cond(c, cw);
                    foreach (var e in ch.Effects) Effect(e, cw);
                }
                // A choice list where every option is conditional could offer nothing and strand the player.
                if (n.Choices.Count > 0 && !n.Choices.Exists(ch => ch != null && ch.Conditions.Count == 0)) p.Add($"{nw}: every choice is conditional — add one with no conditions");
            }
            if (start == null || !nodes.ContainsKey(start)) return;
            var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var open = new Stack<string>();
            open.Push(start);
            while (open.Count > 0)
            {
                var id = open.Pop();
                if (!reached.Add(id) || !nodes.TryGetValue(id, out var n)) continue;
                if (n.Next != null) open.Push(n.Next);
                foreach (var ch in n.Choices) if (ch?.Next != null) open.Push(ch.Next);
            }
            foreach (var n in nodes.Values) if (!reached.Contains(n.Id)) p.Add($"{w}: node '{n.Id}' is unreachable");
        }
    }
}
