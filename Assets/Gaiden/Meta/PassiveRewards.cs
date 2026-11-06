using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Core;

namespace GridGame.Meta
{
    /// <summary>
    /// Which learned passives a hero fights with (§25.6.4, GG-T301). A hero's class-kit passives (<see cref="ClassDef.Abilities"/>)
    /// are innate and always on. Every other Passive/Reactive in <see cref="HeroSave.Known"/> — skill-tree grants and Tomes —
    /// is learned, and only <see cref="Slots"/> of them are on at once: 2, a 3rd at level 10, a 4th at level 20. Until the
    /// player picks (<see cref="HeroSave.PassivesChosen"/>) the first learned ones are on, in the order learned; after that
    /// <see cref="HeroSave.ActivePassives"/> is the list. Meta-only passives (the proficiencies, no trigger) aren't counted.
    /// </summary>
    public static class PassiveLoadout
    {
        public const int BaseSlots = 2;
        public static readonly int[] SlotLevels = { 10, 20 };

        public static int Slots(HeroSave h)
        {
            int lvl = Progression.Level(h), n = BaseSlots;
            foreach (var at in SlotLevels) if (lvl >= at) n++;
            return n;
        }

        /// <summary>The level the next learned-passive slot opens at, or 0 when every slot is open.</summary>
        public static int NextSlotLevel(HeroSave h)
        {
            int lvl = Progression.Level(h);
            foreach (var at in SlotLevels) if (lvl < at) return at;
            return 0;
        }

        /// <summary>Class-kit passives: always on, never take a slot.</summary>
        public static List<AbilityDef> Innate(ContentDb c, HeroSave h) =>
            (c.TryClass(h.ClassId)?.Abilities ?? new List<string>()).Select(c.TryAbility).Where(RewardCatalog.Teachable).Distinct().ToList();

        public static bool IsInnate(ContentDb c, HeroSave h, string abilityId) =>
            c.TryClass(h.ClassId)?.Abilities?.Any(id => string.Equals(id, abilityId, StringComparison.OrdinalIgnoreCase)) == true;

        /// <summary>Learned battle passives, in the order the hero learned them.</summary>
        public static List<AbilityDef> Learned(ContentDb c, HeroSave h) =>
            h.Known.Where(id => !IsInnate(c, h, id)).Select(c.TryAbility).Where(RewardCatalog.Teachable).Distinct().ToList();

        /// <summary>The learned passives that fight, at most <see cref="Slots"/>.</summary>
        public static List<string> Active(ContentDb c, HeroSave h)
        {
            var learned = Learned(c, h).Select(a => a.Id).ToList();
            var on = h.PassivesChosen ? learned.Where(id => h.ActivePassives != null && h.ActivePassives.Contains(id)) : learned;
            return on.Take(Slots(h)).ToList();
        }

        public static bool IsActive(ContentDb c, HeroSave h, string abilityId) => Active(c, h).Contains(abilityId);

        /// <summary>Switches a learned passive on or off. Switching one on with every slot full fails (turn one off first).</summary>
        public static Result Toggle(ContentDb c, HeroSave h, string abilityId)
        {
            var a = c.TryAbility(abilityId);
            if (h == null || a == null || !Learned(c, h).Contains(a)) return Result.Fail("Not a learned passive");
            var on = Active(c, h);
            h.PassivesChosen = true;
            h.ActivePassives = on;
            if (on.Contains(a.Id)) { on.Remove(a.Id); return Result.Success($"{a.Name} off"); }
            if (on.Count >= Slots(h))
            {
                int next = NextSlotLevel(h);
                return Result.Fail($"All {Slots(h)} passive slots in use. Turn one off first" + (next > 0 ? $" (another opens at Lv {next})." : "."));
            }
            on.Add(a.Id);
            return Result.Success($"{a.Name} on");
        }

        /// <summary>A newly learned passive goes straight on when a slot is free (the automatic order already does this).</summary>
        public static void OnLearned(ContentDb c, HeroSave h, string abilityId)
        {
            if (h == null || !h.PassivesChosen || !RewardCatalog.Teachable(c.TryAbility(abilityId)) || IsInnate(c, h, abilityId)) return;
            if (h.ActivePassives == null) h.ActivePassives = new List<string>();
            if (!h.ActivePassives.Contains(abilityId) && Active(c, h).Count < Slots(h)) h.ActivePassives.Add(abilityId);
        }
    }

    /// <summary>Reading a Tome (§25.6.4, GG-T301): the hero learns its Passive/Reactive and the Tome is used up.</summary>
    public static class Tomes
    {
        /// <summary>Why <paramref name="heroId"/> can't learn this Tome, or null when it can.</summary>
        public static string WhyNot(ContentDb c, SaveData s, string itemId, string heroId)
        {
            var item = c.TryItem(itemId);
            var a = RewardCatalog.TaughtBy(c, item);
            if (a == null) return "Not a tome";
            var h = s.Hero(heroId);
            var cls = c.TryClass(heroId);
            if (h == null || cls == null) return "No such hero";
            if (h.Domesticated) return "A tamed monster can't read"; // GG-T303
            if (PassiveLoadout.IsInnate(c, h, a.Id) || h.Known.Contains(a.Id)) return "Already knows it";
            if (!RewardCatalog.AllowedFor(a, cls)) return $"Only {string.Join("/", a.LearnableBy)} can learn it";
            return null;
        }

        public static Result Teach(ContentDb c, SaveData s, string itemId, string heroId)
        {
            if (s.Count(itemId) <= 0) return Result.Fail("You don't have that tome");
            var why = WhyNot(c, s, itemId, heroId);
            if (why != null) return Result.Fail(why);
            var a = RewardCatalog.TaughtBy(c, c.TryItem(itemId));
            var h = s.Hero(heroId);
            s.AddItem(itemId, -1);
            h.Known.Add(a.Id);
            PassiveLoadout.OnLearned(c, h, a.Id);
            string hero = c.TryClass(heroId)?.Name ?? heroId;
            bool on = PassiveLoadout.IsActive(c, h, a.Id);
            return Result.Success($"{hero} learned {a.Name}" + (on ? "" : " (passive slots full: switch it on at Abilities)"));
        }

        /// <summary>The heroes a Tome could teach right now, party first.</summary>
        public static List<HeroSave> Candidates(ContentDb c, SaveData s, string itemId) =>
            s.Roster.OrderByDescending(h => s.Party.Contains(h.ClassId)).Where(h => WhyNot(c, s, itemId, h.ClassId) == null).ToList();

        /// <summary>The bots' camp (CampRoutine): read every Tome in the bag to the first party hero with a free passive slot.</summary>
        public static List<Result> AutoTeach(ContentDb c, SaveData s)
        {
            var done = new List<Result>();
            foreach (var id in s.Inventory.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList())
            {
                if (RewardCatalog.TaughtBy(c, c.TryItem(id)) == null) continue;
                while (s.Count(id) > 0)
                {
                    var h = Candidates(c, s, id).FirstOrDefault(x => s.Party.Contains(x.ClassId) && PassiveLoadout.Active(c, x).Count < PassiveLoadout.Slots(x));
                    if (h == null) break;
                    done.Add(Teach(c, s, id, h.ClassId));
                }
            }
            return done;
        }
    }

    /// <summary>
    /// Random chests (§25.6.4, GG-T301): a Chest is an item that drops from enemy drop tables like any loot (so the
    /// difficulty dial's drop multiplier scales it), and opening it rolls its own droptables.json row: every entry rolls
    /// its chance, coins roll CoinsMin..CoinsMax, and a chest never opens empty (nothing rolled = its likeliest entry).
    /// Seeded by <see cref="SaveData.ChestsOpened"/>, so a save replays its chests identically.
    /// </summary>
    public static class Chests
    {
        public sealed class Haul
        {
            public int Gold;
            public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
        }

        public static Haul Roll(ContentDb c, DropTableDef t, Rng rng)
        {
            var haul = new Haul();
            if (t == null) return haul;
            void Add(string id, int n) { haul.Items.TryGetValue(id, out var v); haul.Items[id] = v + n; }
            foreach (var e in t.Entries)
                if (rng.Chance(e.Chance)) Add(e.Item, rng.Range(e.Min, e.Max + 1));
            if (haul.Items.Count == 0 && t.Entries.Count > 0)
            {
                var best = t.Entries.OrderByDescending(e => e.Chance).First();
                Add(best.Item, best.Min);
            }
            if (t.CoinsMax > 0) haul.Gold = rng.Range(t.CoinsMin, t.CoinsMax + 1);
            return haul;
        }

        /// <summary>Opens one chest from the bag: its haul goes into the save (stack caps auto-sell like battle loot).</summary>
        public static Result Open(ContentDb c, SaveData s, string itemId, out Haul haul)
        {
            haul = null;
            var item = c.TryItem(itemId);
            if (item == null || item.Type != ItemType.Chest || !c.DropTables.TryGetValue(item.OpensTable ?? "", out var table)) return Result.Fail("Can't be opened");
            if (s.Count(itemId) <= 0) return Result.Fail("You don't have one");
            s.AddItem(itemId, -1);
            var rng = new Rng(unchecked(s.ChestsOpened * 7919 + 104729 + StableHash(itemId)));
            s.ChestsOpened++;
            haul = Roll(c, table, rng);
            s.Gold += haul.Gold;
            var rep = new BattleReport();
            BattleFactory.GrantItems(c, s, rep, haul.Items);
            var names = haul.Items.Select(kv => (c.TryItem(kv.Key)?.Name ?? kv.Key) + (kv.Value > 1 ? $" ×{kv.Value}" : "")).ToList();
            if (haul.Gold > 0) names.Insert(0, $"{haul.Gold} G");
            return Result.Success($"{item.Name}: " + string.Join(", ", names));
        }

        /// <summary>The bots' camp (CampRoutine): open every chest in the bag.</summary>
        public static List<Result> OpenAll(ContentDb c, SaveData s)
        {
            var done = new List<Result>();
            foreach (var id in s.Inventory.Where(kv => kv.Value > 0 && c.TryItem(kv.Key)?.Type == ItemType.Chest).Select(kv => kv.Key).ToList())
                while (s.Count(id) > 0) { var r = Open(c, s, id, out _); done.Add(r); if (!r.Ok) break; }
            return done;
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (var ch in s ?? "") h = h * 31 + ch;
                return h;
            }
        }
    }

    /// <summary>Campaign milestones (§25.6.4, GG-T301): a theme's <see cref="ThemeDef.MilestoneReward"/> is paid once, the
    /// first time its last stage is won.</summary>
    public static class Milestones
    {
        /// <summary>Pays the milestone a victory on <paramref name="stageId"/> earns, into <paramref name="rep"/>; returns the theme or null.</summary>
        public static ThemeDef Pay(ContentDb c, SaveData s, BattleReport rep, string stageId)
        {
            var t = c.ThemeOf(stageId);
            if (t == null || t.MilestoneReward == null || t.Stages.Count == 0) return null;
            if (!string.Equals(t.Stages[t.Stages.Count - 1], stageId, StringComparison.OrdinalIgnoreCase)) return null;
            if (s.Milestones == null) s.Milestones = new HashSet<string>();
            if (!s.Milestones.Add(t.Id)) return null;
            var paid = new BattleReport();
            BattleFactory.GrantItems(c, s, paid, new Dictionary<string, int> { [t.MilestoneReward] = 1 });
            foreach (var kv in paid.Items) { rep.Items.TryGetValue(kv.Key, out var had); rep.Items[kv.Key] = had + kv.Value; }
            rep.Gold += paid.Gold;
            rep.AutoSold += paid.AutoSold;
            rep.Milestone = t.Name;
            rep.MilestoneItem = t.MilestoneReward;
            return t;
        }
    }
}
