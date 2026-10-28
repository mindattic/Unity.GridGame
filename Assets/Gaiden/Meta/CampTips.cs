using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>One guided first-use prompt for a Camp station (GG-T173).</summary>
    public sealed class CampTip
    {
        /// <summary>The station's screen id (HubPage.Id): "shop", "blacksmith", …</summary>
        public string Station;
        public string Title;
        /// <summary>One sentence: the station's single most important action.</summary>
        public string Text;
        /// <summary>Name (or name prefix ending in '-') of the control the pointing hand aims at; null = none.</summary>
        public string Target;
    }

    /// <summary>
    /// The camp tutorial (GG-T173, bible §25.14): the first time the player opens a Camp station, a one-time prompt
    /// points at the thing to do there. Seen tips are kept in <see cref="SaveData.SeenCampTips"/> so they never
    /// repeat; Settings › How to play can replay them. The onboarding chain (GG-T174…T180) refines the text and the
    /// order stations open in; this table is the single place that wording lives.
    /// </summary>
    public static class CampTips
    {
        public static readonly IReadOnlyList<CampTip> All = new[]
        {
            new CampTip { Station = "shop", Title = "The Merchant",
                Text = "Spend your first battle gold on Healing Potions: tap + to add some, then Buy. The Sell tab turns spare drops back into gold.",
                Target = "cart-plus-healing_potion_basic" },
            new CampTip { Station = "blacksmith", Title = "The Blacksmith",
                Text = "Gear wears down in battle. Tap a worn piece to mend it before it shatters.",
                Target = "repair-" },
            new CampTip { Station = Forging.TipStation, Title = "The Forge",
                Text = "Forge gear from what enemies drop. Pick a recipe, then Craft.",
                Target = "recipe-" },
            new CampTip { Station = Forging.ArmorTipStation, Title = "Forge Armor",
                Text = "Armor recipes are open. Forge a set piece from what enemies drop, then Craft.",
                Target = "recipe-" },
            new CampTip { Station = "alchemist", Title = "The Alchemist",
                Text = "Brew potions from what enemies drop — far cheaper than buying them. Pick a recipe, then Brew.",
                Target = "recipe-" },
            new CampTip { Station = "enchantress", Title = "The Enchantress",
                Text = "Bind a rune into a piece of worn armor to give its wearer a lasting passive. Pick a piece and an enchant, then Enchant.",
                Target = ArmorEnchanting.PieceRowPrefix },
            new CampTip { Station = "abilities", Title = "Training",
                Text = "Learn new skills and spells here, then set them on a hero's bar so they can use them in battle.",
                Target = null },
            new CampTip { Station = "bounties", Title = "Bounties",
                Text = "Take one contract at a time. Defeat the target enemy as you fight, then come back to Claim the reward.",
                Target = "bounty-" },
        };

        public static CampTip For(string station) => All.FirstOrDefault(t => t.Station == station);

        /// <summary>The tip to show on opening <paramref name="station"/>, or null when it has none, it's been seen,
        /// or this is a bot-played save.</summary>
        public static CampTip Pending(SaveData s, string station)
        {
            if (s == null || s.TestMode || station == null) return null;
            var tip = For(station);
            return tip != null && !s.SeenCampTips.Contains(station) ? tip : null;
        }

        /// <summary>The tip as it should read for this save right now. The Blacksmith's (GG-T175) names and points at the
        /// most worn equipped piece — the one whose wear brought him to the caravan — so the first thing done at his
        /// cart is repairing it. Every other tip is returned as written.</summary>
        public static CampTip Resolve(ContentDb c, SaveData s, CampTip tip)
        {
            if (tip == null || c == null) return tip;
            if (tip.Station == "alchemist") return ResolveAlchemist(c, s, tip);
            if (tip.Station == Forging.TipStation) return ResolveForge(c, s, tip);
            if (tip.Station == Forging.ArmorTipStation) return ResolveArmor(c, s, tip);
            if (tip.Station == "enchantress") return ResolveEnchantress(c, s, tip);
            if (tip.Station != "blacksmith") return tip;
            var worn = Repair.MostWorn(c, s);
            if (worn == null || worn.Durability >= worn.Max) return tip;
            string hero = c.TryClass(worn.Hero.ClassId)?.Name ?? worn.Hero.ClassId;
            int pct = (int)System.Math.Round(worn.Fraction * 100);
            return new CampTip
            {
                Station = tip.Station, Title = tip.Title, Target = worn.RowName,
                Text = $"{hero}'s {worn.Item.Name} is worn down to {pct}%. Tap it to repair it before it shatters in battle.",
            };
        }

        /// <summary>The Alchemist's tip (GG-T176, §25.0.5) walks the first brew: with a bottle recipe brewable now
        /// (<see cref="Brewing.FirstBrew"/>, which his screen preselects) it names the potion and what goes in, says what
        /// buying it would cost, and points at Brew; with only the bottle missing it sends the player to the Merchant.</summary>
        private static CampTip ResolveAlchemist(ContentDb c, SaveData s, CampTip tip)
        {
            var r = Brewing.FirstBrew(c, s);
            var result = r != null ? c.TryItem(r.Result) : null;
            if (result == null) return tip;
            string loot = string.Join(" and ", r.Inputs.Where(i => i.Item != ContentLoader.EmptyBottle)
                .Select(i => (i.Count > 1 ? i.Count + " " : "") + (c.TryItem(i.Item)?.Name ?? i.Item)));
            bool ready = Crafting.HasInputs(s, r) && s.Gold >= r.Gold;
            return new CampTip
            {
                Station = tip.Station, Title = tip.Title,
                Target = ready ? CraftButton : RecipeRowPrefix + r.Id,
                Text = ready
                    ? $"An Empty Bottle with your {loot} makes a {result.Name} (the Merchant charges {Brewing.BuyCost(c, r)} G). Tap Brew."
                    : $"Your {loot} make a {result.Name}: buy an Empty Bottle from the Merchant for a few coins, then brew it here.",
            };
        }

        /// <summary>The Forge tab's tip (GG-T177, §25.0.6) walks the first forge: the starter weapon <see cref="Forging.First"/>
        /// picks (the Forge preselects it), what goes in, and the hero it suits, who equips it on forging. With materials
        /// still to find it rings the recipe row and says what to bring back.</summary>
        private static CampTip ResolveForge(ContentDb c, SaveData s, CampTip tip)
        {
            var plan = Forging.First(c, s);
            if (plan == null) return tip;
            string hero = c.TryClass(plan.Hero.ClassId)?.Name ?? plan.Hero.ClassId;
            string Name(ItemCount i) => c.TryItem(i.Item)?.Name ?? i.Item;
            if (plan.Ready)
            {
                string loot = string.Join(" and ", plan.Recipe.Inputs.Select(i => (i.Count > 1 ? i.Count + " " : "") + Name(i)));
                return new CampTip
                {
                    Station = tip.Station, Title = tip.Title, Target = CraftButton,
                    Text = $"Your {loot} make a {plan.Weapon.Name} for the {hero}, and the smith charges nothing for a first weapon. Tap Craft and the {hero} equips it.",
                };
            }
            string missing = string.Join(" and ", plan.Recipe.Inputs.Where(i => s.Count(i.Item) < i.Count)
                .Select(i => (i.Count - s.Count(i.Item)) + " more " + Name(i)));
            return new CampTip
            {
                Station = tip.Station, Title = tip.Title, Target = RecipeRowPrefix + plan.Recipe.Id,
                Text = $"A {plan.Weapon.Name} would suit the {hero}, free to forge from battle drops. Bring back {missing}, then Craft it here.",
            };
        }

        /// <summary>The Forge's armor tip (GG-T178, §25.0.7), shown once the first weapon is made: it walks the first armor
        /// piece <see cref="Forging.FirstArmor"/> picks (the Forge preselects it), its set, what goes in and the hero who
        /// wears it on forging, ringing Craft; with materials or gold still short it rings the recipe row and says what's
        /// missing.</summary>
        private static CampTip ResolveArmor(ContentDb c, SaveData s, CampTip tip)
        {
            var plan = Forging.FirstArmor(c, s);
            if (plan == null) return tip;
            string hero = c.TryClass(plan.Hero.ClassId)?.Name ?? plan.Hero.ClassId;
            string set = c.TryArmorSetOf(plan.Weapon.Id)?.Name;
            string piece = plan.Weapon.Name + (set != null && !plan.Weapon.Name.StartsWith(set) ? $" ({set} set)" : "");
            string Name(ItemCount i) => c.TryItem(i.Item)?.Name ?? i.Item;
            if (plan.Ready)
            {
                string loot = string.Join(" and ", plan.Recipe.Inputs.Select(i => (i.Count > 1 ? i.Count + " " : "") + Name(i)));
                return new CampTip
                {
                    Station = tip.Station, Title = tip.Title, Target = CraftButton,
                    Text = $"Armor recipes are open. Your {loot} make {piece} for the {hero}, the first piece of a set. Tap Craft and the {hero} wears it.",
                };
            }
            var short1 = plan.Recipe.Inputs.Where(i => s.Count(i.Item) < i.Count)
                .Select(i => (i.Count - s.Count(i.Item)) + " more " + Name(i)).ToList();
            if (s.Gold < plan.Recipe.Gold) short1.Add($"{plan.Recipe.Gold - s.Gold} more gold");
            return new CampTip
            {
                Station = tip.Station, Title = tip.Title, Target = RecipeRowPrefix + plan.Recipe.Id,
                Text = $"Armor recipes are open. {piece} would suit the {hero}. Bring back {string.Join(" and ", short1)}, then Craft it here.",
            };
        }

        /// <summary>The Enchantress's tip (GG-T179, §25.0.8) walks the first armor enchant: the piece and enchant
        /// <see cref="ArmorEnchanting.First"/> picks (her Armor tab preselects both), the passive it grants and what goes
        /// in, ringing Enchant; with materials still to find it rings the enchant row and says what to bring back.</summary>
        private static CampTip ResolveEnchantress(ContentDb c, SaveData s, CampTip tip)
        {
            var plan = ArmorEnchanting.First(c, s);
            if (plan == null) return tip;
            string hero = c.TryClass(plan.Target.Hero.ClassId)?.Name ?? plan.Target.Hero.ClassId;
            string passive = c.TryAbility(plan.Enchant.Passive)?.Name ?? plan.Enchant.Name;
            string Name(ItemCount i) => c.TryItem(i.Item)?.Name ?? i.Item;
            if (plan.Ready)
            {
                string loot = string.Join(" and ", plan.Enchant.Inputs.Select(i => (i.Count > 1 ? i.Count + " " : "") + Name(i)));
                return new CampTip
                {
                    Station = tip.Station, Title = tip.Title, Target = CraftButton,
                    Text = $"Your {loot} bind {plan.Enchant.Name} into the {hero}'s {plan.Target.Item.Name}: {passive} while it's worn, and the first rune is free. Tap Enchant.",
                };
            }
            string missing = string.Join(" and ", plan.Enchant.Inputs.Where(i => s.Count(i.Item) < i.Count)
                .Select(i => (i.Count - s.Count(i.Item)) + " more " + Name(i)));
            return new CampTip
            {
                Station = tip.Station, Title = tip.Title, Target = ArmorEnchanting.EnchantRowPrefix + plan.Enchant.Id,
                Text = $"{plan.Enchant.Name} would give the {hero}'s {plan.Target.Item.Name} {passive}, and the first rune is free. Bring back {missing}, then Enchant it here.",
            };
        }

        /// <summary>Recipe stations' Brew/Craft button and recipe-row name prefix.</summary>
        public const string CraftButton = "craft", RecipeRowPrefix = "recipe-";

        public static void MarkSeen(SaveData s, string station) { if (s != null && station != null) s.SeenCampTips.Add(station); }

        /// <summary>Settings › How to play › Replay camp tips.</summary>
        public static void ResetAll(SaveData s) => s?.SeenCampTips.Clear();
    }
}
