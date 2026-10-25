using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// "What a sensible player does at camp": scrap worn-out gear, rest, repair, forge and equip better
    /// gear (owned bag, then Forge, then the Merchant's Bargain shelf), learn abilities into dead bar
    /// slots (GG-T100), keep potions stocked, work the Bounty Board and recruit at the Summon Circle into an open
    /// party slot (GG-T87). Used by the balance simulation and the in-game auto-manage helper;
    /// every step goes through the same services the screens call, and only the services whose Camp NPC has
    /// joined (GG-T119): no Blacksmith means no repair/salvage/forge, no Innkeeper no rest, no Merchant no
    /// buying or Bargains, no Alchemist no brewing.
    /// </summary>
    public static class CampRoutine
    {
        /// <summary>A Bargain offer below this fraction of max durability is skipped (GG-T54): the shelf's
        /// §25.1 gamble (10-40% durability left, a real risk of shattering before it can be repaired) is a
        /// deliberate player choice — an automated buyer can't judge that risk, so it only takes the safer
        /// rolls of the range.</summary>
        public const float MinSafeBargainDurabilityFraction = 0.25f;

        public static List<string> Run(ContentDb c, SaveData s)
        {
            var log = new List<string>();
            void Note(Result r) { if (r.Ok && r.Message != null) log.Add(r.Message); }
            // Only the Camp NPCs who have joined (GG-T119) — a service whose keeper hasn't been met yet is hidden
            // from the player, so the routine doesn't use it either. Bounties, the Summon Circle and Learn have no keeper.
            bool smith = CampNpcs.Joined(s, CampNpcs.Blacksmith), merchant = CampNpcs.Joined(s, CampNpcs.Merchant);
            bool alchemist = CampNpcs.Joined(s, CampNpcs.Alchemist), inn = CampNpcs.Joined(s, CampNpcs.Innkeeper);
            // The Blacksmith only repairs until his workshop opens (GG-T175): no forging or salvage before then.
            bool workshop = smith && CampUnlocks.SmithWorkshopOpen(s);

            // Broken fragments (GG-T41) can't be equipped or repaired — only scrapped — so there's never a
            // reason to let them sit in the bag; salvage every copy of every one.
            foreach (var itemId in workshop ? s.Broken.Keys.ToList() : new List<string>())
            {
                // Stops on a failed call, or one that didn't use up a fragment, rather than spinning (GG-T111).
                Result r;
                int left;
                do { left = s.BrokenCount(itemId); r = Crafting.SalvageBroken(c, s, itemId); Note(r); }
                while (r.Ok && s.BrokenCount(itemId) > 0 && s.BrokenCount(itemId) < left);
            }

            // A repair that costs as much as the item is worth (GG-T42's "Salvage instead" offer) is a
            // losing trade — scrap it for materials instead of pouring gold into a piece about to shatter anyway.
            foreach (var h in smith ? s.Roster : new List<HeroSave>())
            {
                if (h.WeaponId != null)
                {
                    var w = c.TryItem(h.WeaponId);
                    if (workshop && w != null && Repair.IsUneconomical(w, h.WeaponDurability, h.WeaponRepairs, h.WeaponMastery)) Note(Crafting.SalvageEquipped(c, s, h, EquipmentSlot.Weapon));
                    else Note(Repair.RepairWeapon(c, s, h));
                }
                foreach (var slot in BattleEngine.ArmorSlots)
                {
                    if (!h.Armor.TryGetValue(slot, out var piece)) continue;
                    var a = c.TryItem(piece.ItemId);
                    if (workshop && a != null && Repair.IsUneconomical(a, piece.Durability, piece.Repairs, piece.Mastery)) Note(Crafting.SalvageEquipped(c, s, h, slot));
                    else Note(Repair.RepairArmor(c, s, h, slot));
                }
            }
            if (inn && Inn.Cost(c, s) > 0 && Inn.Cost(c, s) <= s.Gold) Note(Inn.Rest(c, s));

            // Bounty Board (GG-T87): cash in a finished contract, drop one whose target no longer appears
            // anywhere ahead, then take the one whose target shows up soonest on the road ahead.
            ManageBounty(c, s, Note);

            // Summon Circle (GG-T87): fill an open party slot with a recruit when the gold is there — done
            // before the gear pass so the newcomer gets equipped on this same visit.
            if (s.Party.Count < Progression.MaxPartySize && Summon.Price(c, s) <= s.Gold)
            {
                var recruit = Summon.Pool(c, s).FirstOrDefault(d => s.Hero(d.Id) == null);
                if (recruit != null) Note(Summon.Recruit(c, s, recruit.Id));
            }

            // Fill the ability bar (GG-T100) before the discretionary gear pass, so a full gear shopping list
            // can't starve it every visit.
            FillBars(c, s, log);

            // The best affordable upgrade for each party member's weapon and armor: an owned bag copy
            // first, then a Forge recipe, then the Merchant's Bargain shelf (skipping near-shatter rolls).
            foreach (var id in s.Party)
            {
                var h = s.Hero(id);
                foreach (var slot in new[] { EquipmentSlot.Weapon }.Concat(BattleEngine.ArmorSlots))
                {
                    float current = MetaHeuristics.Score(c.TryItem(Equipment.Equipped(h, slot)));
                    var owned = MetaHeuristics.BestFirst(Equipment.Candidates(c, s, slot, h)).FirstOrDefault();
                    if (workshop && (owned == null || MetaHeuristics.Score(owned) <= current))
                    {
                        var recipe = Crafting.Recipes(c, CraftStation.Forge)
                            .Where(r => r.BaseItem == null && Forging.ArmorOpen(c, s, r) && Equipment.Fits(c, h, c.Item(r.Result), slot) && Crafting.HasInputs(s, r) && r.Gold <= s.Gold / 2)
                            .OrderByDescending(r => MetaHeuristics.Score(c.Item(r.Result))).FirstOrDefault();
                        if (recipe != null && MetaHeuristics.Score(c.Item(recipe.Result)) > current) Note(Crafting.Craft(c, s, recipe));
                        owned = MetaHeuristics.BestFirst(Equipment.Candidates(c, s, slot, h)).FirstOrDefault();
                    }
                    if (merchant && (owned == null || MetaHeuristics.Score(owned) <= current))
                    {
                        var bargain = Bargains.Shelf(c, s)
                            .Where(o => Equipment.Fits(c, h, o.Item, slot) && o.Price <= s.Gold / 2 && o.Durability >= o.Item.Durability * MinSafeBargainDurabilityFraction)
                            .OrderByDescending(o => MetaHeuristics.Score(o.Item)).FirstOrDefault();
                        if (bargain != null && MetaHeuristics.Score(bargain.Item) > current)
                        {
                            var bought = Bargains.Buy(c, s, bargain.Item.Id);
                            Note(bought);
                            if (bought.Ok) owned = bargain.Item;
                        }
                    }
                    if (owned != null && MetaHeuristics.Score(owned) > current) Note(Equipment.Equip(c, s, h, slot, owned.Id));
                }
            }

            // Keep a potion stock — brewing it (reagents from field drops) undercuts the shop price
            // (GG-T44), so brew first and only fall back to buying once the reagents run dry. Every pass
            // must add stock: a failed brew falls back to buying, and a pass where neither worked (or one
            // that "succeeded" without adding a potion) ends the loop instead of spinning forever (GG-T111).
            var potion = c.TryItem(StopgapPotion);
            // Any recipe that makes it will do — the slime-gel brew or the Empty Bottle + wolf pelt one (GG-T176); a bottle
            // recipe short only of its bottle buys one from the Merchant first, as a player would.
            var brews = c.Recipes.Values.Where(r => r.Station == CraftStation.Alchemy && r.Result == StopgapPotion).ToList();
            var bottle = c.TryItem(ContentLoader.EmptyBottle);
            while (potion != null && s.Count(potion.Id) < System.Math.Min(6, potion.MaxStack))
            {
                int before = s.Count(potion.Id);
                bool ok = false;
                var brew = alchemist ? brews.FirstOrDefault(r => Crafting.IsUnlocked(s, r) && Brewing.HasLoot(s, r) && s.Gold >= r.Gold) : null;
                if (brew != null && !Crafting.HasInputs(s, brew) && merchant && bottle != null && Brewing.IsBottleRecipe(brew) && s.Gold >= bottle.BaseCost + brew.Gold)
                    Note(Shop.Buy(c, s, new Dictionary<string, int> { [bottle.Id] = 1 }));
                if (brew != null && Crafting.HasInputs(s, brew) && s.Gold >= brew.Gold)
                {
                    var r = Crafting.Craft(c, s, brew); Note(r); ok = r.Ok;
                }
                if (!ok && merchant && s.Gold >= potion.BaseCost * 3)
                {
                    var r = Shop.Buy(c, s, new Dictionary<string, int> { [potion.Id] = System.Math.Min(3, Shop.MaxBuy(s, potion)) }); Note(r); ok = r.Ok;
                }
                if (!ok || s.Count(potion.Id) <= before) break;
            }
            return log;
        }

        /// <summary>Text in a camp log line that records learning an ability ("Cleric learned Rejuvenate (280g)");
        /// the Test Mode check looks for it.</summary>
        public const string LearnedMarker = " learned ";

        /// <summary>The cheap shop potion the routine stocks and parks in a bar slot when it has nothing better.</summary>
        public const string StopgapPotion = "healing_potion_basic";

        /// <summary>A bar slot that does nothing in battle: empty, or an item/backup-weapon entry whose bag stack
        /// has run dry (the starting X-Potion slot is spent within the first few battles and stays dead).</summary>
        public static bool IsDeadSlot(SaveData s, string entry)
        {
            if (entry == null) return true;
            if (entry.StartsWith("item:")) return s.Count(entry.Substring(5)) <= 0;
            if (entry.StartsWith("weapon:")) return s.Count(entry.Substring(7)) <= 0;
            return false;
        }

        /// <summary>The ability a sensible player buys for an open slot (GG-T100): the priciest (highest-tier)
        /// affordable one from the hero's own class list, in list order on ties; only once the class list is
        /// fully learned does it reach into the general catalog.</summary>
        public static AbilityDef BestLearnable(ContentDb c, HeroSave h, int gold)
        {
            var learnable = Abilities.Learnable(c, h);
            var classList = c.TryClass(h.ClassId)?.Abilities ?? new List<string>();
            var own = learnable.Where(a => classList.Contains(a.Id)).ToList();
            var pool = own.Count > 0 ? own : learnable;
            return pool.Where(a => Abilities.Price(a) <= gold)
                .OrderByDescending(Abilities.Price).ThenBy(a => classList.IndexOf(a.Id))
                .FirstOrDefault();
        }

        /// <summary>For every party hero, put something useful in each dead bar slot — and upgrade a slot
        /// holding only the stopgap potion: an already-known but unslotted ability first, else learn the
        /// <see cref="BestLearnable"/> one the gold allows, else (dead slots only) the stopgap potion if any is
        /// stocked. Runs before discretionary gear so learning isn't permanently outbid by gear (GG-T100).</summary>
        private static void FillBars(ContentDb c, SaveData s, List<string> log)
        {
            int unlocked = System.Math.Min(Progression.UnlockedSlots(s), Progression.MaxBarSlots);
            string stopgap = "item:" + StopgapPotion;
            foreach (var id in s.Party)
            {
                var h = s.Hero(id);
                if (h == null) continue;
                while (h.Bar.Count < Progression.MaxBarSlots) h.Bar.Add(null);
                string heroName = c.TryClass(h.ClassId)?.Name ?? h.ClassId;
                for (int i = 0; i < unlocked; i++)
                {
                    bool dead = IsDeadSlot(s, h.Bar[i]);
                    if (!dead && h.Bar[i] != stopgap) continue;
                    string entry = h.Known.FirstOrDefault(k => !h.Bar.Contains(k));
                    if (entry == null)
                    {
                        var learn = BestLearnable(c, h, s.Gold);
                        if (learn != null)
                        {
                            var r = Abilities.Learn(c, s, h, learn.Id);
                            if (r.Ok) { log.Add($"{heroName}{LearnedMarker}{learn.Name} ({Abilities.Price(learn)}g)"); entry = learn.Id; }
                        }
                    }
                    if (entry == null && dead && s.Count(StopgapPotion) > 0 && !h.Bar.Contains(stopgap)) entry = stopgap;
                    if (entry == null) continue;
                    var assigned = Abilities.Assign(c, s, h, i, entry);
                    if (!assigned.Ok) break;
                    string label = entry.StartsWith("item:") ? c.TryItem(entry.Substring(5))?.Name ?? entry : c.TryAbility(entry)?.Name ?? entry;
                    log.Add($"{heroName} slotted {label} (slot {i + 1})");
                }
            }
        }

        /// <summary>Claim a completed bounty; abandon an active one the stages still ahead can no longer finish;
        /// with none active, accept the posted contract the road ahead completes soonest (cheapest reward
        /// breaks ties). A playthrough meets each stage once, so a contract is only "achievable" if the
        /// uncleared campaign stages spawn enough of its target — e.g. Slime Culling wants 5 slime00 kills
        /// but the whole campaign spawns 3, so this player (who doesn't grind replays) never takes it.</summary>
        private static void ManageBounty(ContentDb c, SaveData s, System.Action<Result> note)
        {
            if (Bounties.IsComplete(c, s)) note(Bounties.Claim(c, s));
            var ahead = c.CampaignStages().Skip(s.HighestClearedStage + 1).ToList();
            // Index (into `ahead`) of the stage that would bring the count to `needed`, or -1 if it never gets there.
            int CompletesAt(string classId, int needed)
            {
                int seen = 0;
                for (int i = 0; i < ahead.Count; i++)
                {
                    seen += ahead[i].Waves.Sum(w => w.Enemies.Count(e => e.Class == classId));
                    if (seen >= needed) return i;
                }
                return -1;
            }
            var active = Bounties.Active(c, s);
            if (active != null && CompletesAt(active.TargetClass, active.Kills - s.Bounty.Progress) < 0) note(Bounties.Abandon(s));
            if (Bounties.Active(c, s) != null) return;
            var pick = Bounties.Posted(c, s)
                .Select(b => (b, at: CompletesAt(b.TargetClass, b.Kills)))
                .Where(x => x.at >= 0)
                .OrderBy(x => x.at).ThenBy(x => x.b.RewardGold)
                .Select(x => x.b).FirstOrDefault();
            if (pick != null) note(Bounties.Accept(c, s, pick.Id));
        }
    }
}
