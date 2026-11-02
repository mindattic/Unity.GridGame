using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>Where a skill-tree node stands for one hero (§25.6.3, GG-T53).</summary>
    public enum TalentState
    {
        /// <summary>Already in the hero's kit without a point spent on it (starting loadout or gold-Learned):
        /// counts toward the tree's points like any unlocked node, but Respec can't refund it.</summary>
        Innate,
        /// <summary>Bought with skill points (<see cref="HeroSave.TreeNodes"/>); Respec refunds it.</summary>
        Purchased,
        /// <summary>Every gate met; affordable or not, it can be bought once the hero has the points.</summary>
        Available,
        /// <summary>A prerequisite node or the tree's point requirement is still missing.</summary>
        Locked,
    }

    /// <summary>
    /// Spending skill points on tree nodes, and the Inn's Respec (§25.6.3, GG-T53). A node is unlocked if it was bought
    /// (<see cref="HeroSave.TreeNodes"/>) or its grant is already Known — the GG-T50 rule that a hero starts its home tree
    /// with its default abilities pre-unlocked — so the points a tree counts (<see cref="PointsIn"/>) match the Build
    /// screen's <see cref="Progression.TreeInvestment"/> reading. The trees a hero sees are its home tree first, then any
    /// other tree it already has a node in (an "opened" off-tree); how a new off-tree gets opened is GG-T51/T83's call.
    /// </summary>
    public static class Talents
    {
        public const int RespecBaseGold = 100;
        public const int RespecGoldPerLevel = 50;

        public static string Key(SkillTreeDef t, SkillNodeDef n) => t.Id + "/" + n.Id;

        public static bool Bought(HeroSave h, SkillTreeDef t, SkillNodeDef n) => h.TreeNodes != null && h.TreeNodes.Contains(Key(t, n));

        public static bool Unlocked(HeroSave h, SkillTreeDef t, SkillNodeDef n) => Bought(h, t, n) || Innate(h, n);

        private static bool Innate(HeroSave h, SkillNodeDef n) => !string.IsNullOrEmpty(n.Grants) && h.Known.Contains(n.Grants);

        /// <summary>Points the tree counts toward a node's <see cref="SkillNodeDef.RequiredPointsInTree"/>: every unlocked node's cost.</summary>
        public static int PointsIn(HeroSave h, SkillTreeDef t) => t.Nodes.Where(n => Unlocked(h, t, n)).Sum(n => n.Cost);

        /// <summary>Points actually spent (refundable by Respec) across every tree.</summary>
        public static int PointsSpent(ContentDb c, HeroSave h)
        {
            int total = 0;
            foreach (var t in c.SkillTrees.Values)
                foreach (var n in t.Nodes)
                    if (Bought(h, t, n)) total += n.Cost;
            return total;
        }

        public static SkillTreeDef HomeTree(ContentDb c, HeroSave h) => c.TrySkillTree(c.TryClass(h.ClassId)?.HomeTree);

        /// <summary>Home tree first, then every other tree the hero already holds a node in, by name.</summary>
        public static List<SkillTreeDef> Trees(ContentDb c, HeroSave h)
        {
            var home = HomeTree(c, h);
            var list = new List<SkillTreeDef>();
            if (home != null) list.Add(home);
            list.AddRange(c.SkillTrees.Values
                .Where(t => t != home && t.Nodes.Any(n => Unlocked(h, t, n)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
            return list;
        }

        public static TalentState State(HeroSave h, SkillTreeDef t, SkillNodeDef n)
        {
            if (Bought(h, t, n)) return TalentState.Purchased;
            if (Innate(h, n)) return TalentState.Innate;
            return WhyLocked(h, t, n) == null ? TalentState.Available : TalentState.Locked;
        }

        /// <summary>What still blocks a node (missing prerequisites, then points in the tree), or null when its gates are met.</summary>
        public static string WhyLocked(HeroSave h, SkillTreeDef t, SkillNodeDef n)
        {
            var missing = n.Prerequisites
                .Select(id => t.Nodes.FirstOrDefault(x => x.Id == id))
                .Where(p => p != null && !Unlocked(h, t, p))
                .Select(p => p.Name ?? p.Id).ToList();
            if (missing.Count > 0) return "Needs " + string.Join(" + ", missing);
            int have = PointsIn(h, t);
            if (have < n.RequiredPointsInTree) return $"Needs {n.RequiredPointsInTree} pts in {t.Name} ({have} now)";
            return null;
        }

        /// <summary>Spends <see cref="SkillNodeDef.Cost"/> skill points on a node: its grant becomes Known.</summary>
        public static Result Unlock(ContentDb c, SaveData s, HeroSave h, string treeId, string nodeId)
        {
            var t = c.TrySkillTree(treeId);
            var n = t?.Nodes.FirstOrDefault(x => x.Id == nodeId);
            if (h == null || n == null || !Trees(c, h).Contains(t)) return Result.Fail("No such talent");
            var state = State(h, t, n);
            if (state == TalentState.Purchased || state == TalentState.Innate) return Result.Fail("Already learned");
            if (state == TalentState.Locked) return Result.Fail(WhyLocked(h, t, n));
            if (h.SkillPoints < n.Cost) return Result.Fail("Not enough skill points");
            h.SkillPoints -= n.Cost;
            if (h.TreeNodes == null) h.TreeNodes = new List<string>();
            h.TreeNodes.Add(Key(t, n));
            if (!string.IsNullOrEmpty(n.Grants) && !h.Known.Contains(n.Grants)) h.Known.Add(n.Grants);
            var a = c.TryAbility(n.Grants);
            return Result.Success($"Learned {a?.Name ?? n.Name ?? n.Id}");
        }

        /// <summary>Respec price at the Inn: grows with the hero's level.</summary>
        public static int RespecCost(HeroSave h) => RespecBaseGold + RespecGoldPerLevel * Progression.Level(h);

        /// <summary>Why the Inn can't respec right now (no Innkeeper yet / on the road without a Medic Wagon), or null.</summary>
        public static string RespecClosed(SaveData s)
        {
            if (!CampUnlocks.NpcOpen(s, CampNpcs.Innkeeper)) return "The Innkeeper hasn't joined yet";
            if (!Expeditions.InnOpen(s)) return "On the road — the Inn opens when the expedition ends";
            return null;
        }

        /// <summary>The Inn's Respec: pays <see cref="RespecCost"/> gold, refunds every bought node's points and forgets what
        /// those nodes granted (clearing them off the battle bar). Innate nodes — the starting kit, gold-Learned abilities — stay.</summary>
        public static Result Respec(ContentDb c, SaveData s, HeroSave h)
        {
            if (h == null) return Result.Fail("No hero");
            var closed = RespecClosed(s);
            if (closed != null) return Result.Fail(closed);
            int refund = PointsSpent(c, h);
            if (refund == 0) return Result.Fail("No talent points spent yet");
            int cost = RespecCost(h);
            if (s.Gold < cost) return Result.Fail("Not enough gold");
            s.Gold -= cost;
            var forget = new HashSet<string>();
            foreach (var t in c.SkillTrees.Values)
                foreach (var n in t.Nodes)
                    if (Bought(h, t, n) && !string.IsNullOrEmpty(n.Grants)) forget.Add(n.Grants);
            h.TreeNodes.Clear();
            h.Known.RemoveAll(forget.Contains);
            for (int i = 0; i < h.Bar.Count; i++)
                if (h.Bar[i] != null && forget.Contains(h.Bar[i])) h.Bar[i] = null;
            h.SkillPoints += refund;
            string hero = c.TryClass(h.ClassId)?.Name ?? h.ClassId;
            return Result.Success($"{hero}: {refund} skill point{(refund == 1 ? "" : "s")} refunded");
        }
    }
}
