using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Archetype skill trees (§25.6.1, GG-T50): one tree per hero archetype, ContentLoader validates
    /// every node's grant and prerequisites, and every hero class names a home tree that exists.</summary>
    public class SkillTreeTests
    {
        [Test]
        public void Every_hero_class_names_a_real_home_tree()
        {
            var c = Real;
            foreach (var cls in c.Classes.Values.Where(x => x.Team == Team.Hero))
            {
                Assert.That(string.IsNullOrEmpty(cls.HomeTree), Is.False, $"{cls.Id}: no HomeTree");
                Assert.That(c.SkillTrees.ContainsKey(cls.HomeTree), Is.True, $"{cls.Id}: unknown home tree '{cls.HomeTree}'");
            }
        }

        [Test]
        public void Every_hero_archetype_in_play_has_a_tree()
        {
            var c = Real;
            var archetypes = c.Classes.Values.Where(x => x.Team == Team.Hero).Select(x => x.HomeTree).Distinct().ToList();
            Assert.That(archetypes, Is.Not.Empty);
            foreach (var a in archetypes) Assert.That(c.SkillTrees[a].Nodes, Is.Not.Empty);
        }

        [Test]
        public void Every_node_grant_resolves_to_a_real_ability_or_passive()
        {
            var c = Real;
            foreach (var t in c.SkillTrees.Values)
                foreach (var n in t.Nodes)
                {
                    if (n.IsStatNode)
                    {
                        Assert.That(n.Grants, Is.Null.Or.Empty, $"{t.Id}/{n.Id}: a stat node can't also grant an ability");
                        Assert.That(StatGrantDef.IsStatName(n.GrantsStat.Stat), Is.True, $"{t.Id}/{n.Id}: unknown stat '{n.GrantsStat.Stat}'");
                        Assert.That(n.GrantsStat.Amount > 0f, Is.True, $"{t.Id}/{n.Id}: stat bonus must be positive");
                    }
                    else Assert.That(c.Abilities.ContainsKey(n.Grants), Is.True, $"{t.Id}/{n.Id}: unknown grant '{n.Grants}'");
                    Assert.That(n.Cost > 0, Is.True, $"{t.Id}/{n.Id}: cost must be positive");
                    Assert.That(n.RequiredPointsInTree >= 0, Is.True, $"{t.Id}/{n.Id}: required points can't be negative");
                }
        }

        [Test]
        public void Every_prerequisite_points_at_a_node_in_the_same_tree()
        {
            var c = Real;
            foreach (var t in c.SkillTrees.Values)
            {
                var ids = new HashSet<string>(t.Nodes.Select(n => n.Id));
                foreach (var n in t.Nodes)
                    foreach (var req in n.Prerequisites)
                        Assert.That(ids, Does.Contain(req));
            }
        }

        /// <summary>GG-T50: "every hero starts there with its existing default abilities pre-unlocked" — the
        /// hero's starting bar (<see cref="ClassDef.DefaultLoadout"/>) is Known from level 1 regardless of
        /// what's still locked in its home tree (<see cref="MetaServices.Progression.NewHero"/>).</summary>
        [Test]
        public void A_new_hero_already_knows_its_default_loadout_in_its_home_tree_archetype()
        {
            var c = Real;
            foreach (var cls in c.Classes.Values.Where(x => x.Team == Team.Hero))
            {
                Assert.That(string.IsNullOrEmpty(cls.HomeTree), Is.False, $"{cls.Id}: no home tree");
                var h = Progression.NewHero(c, cls.Id);
                foreach (var entry in cls.DefaultLoadout)
                {
                    if (entry.StartsWith("item:")) continue;
                    Assert.That(h.Known, Does.Contain(entry));
                }
            }
        }

        private static ContentDb BrokenTreeDb()
        {
            var db = Mini();
            db.Classes["knight"].Role = "Tank";
            db.Classes["knight"].HomeTree = "guardian";
            db.Abilities["shield_bash"] = new AbilityDef { Id = "shield_bash", Name = "Shield Bash", Kind = AbilityKind.Skill };
            db.SkillTrees["guardian"] = new SkillTreeDef
            {
                Id = "guardian",
                Name = "Guardian",
                Nodes =
                {
                    new SkillNodeDef { Id = "root", Grants = "shield_bash", Cost = 1, RequiredPointsInTree = 0 },
                    new SkillNodeDef { Id = "ghost_grant", Grants = "does_not_exist", Cost = 1 },
                    new SkillNodeDef { Id = "ghost_prereq", Grants = "shield_bash", Cost = 1, Prerequisites = { "nowhere" } },
                    new SkillNodeDef { Id = "root", Grants = "shield_bash", Cost = 1 }, // duplicate id
                    new SkillNodeDef { Id = "bad_cost", Grants = "shield_bash", Cost = 0 },
                    new SkillNodeDef { Id = "cycle_a", Grants = "shield_bash", Cost = 1, Prerequisites = { "cycle_b" } },
                    new SkillNodeDef { Id = "cycle_b", Grants = "shield_bash", Cost = 1, Prerequisites = { "cycle_a" } },
                },
            };
            return db;
        }

        [Test]
        public void Validate_catches_every_kind_of_broken_tree_reference()
        {
            var problems = ContentLoader.Validate(BrokenTreeDb());
            Assert.That(problems.Any(p => p.Contains("unknown ability 'does_not_exist'")), Is.True);
            Assert.That(problems.Any(p => p.Contains("unknown prerequisite 'nowhere'")), Is.True);
            Assert.That(problems.Any(p => p.Contains("duplicate node id 'root'")), Is.True);
            Assert.That(problems.Any(p => p.Contains("cost must be positive")), Is.True);
            Assert.That(problems.Any(p => p.Contains("prerequisite cycle")), Is.True);
        }

        /// <summary>GG-T77: every branch (an Opener node) carries 2 interleaved attribute nodes — its tree's primary stat
        /// after the Opener, its secondary stat after the Core.</summary>
        [Test]
        public void Every_branch_interleaves_its_trees_primary_and_secondary_stat_nodes()
        {
            var c = Real;
            var expected = new Dictionary<string, (string Primary, string Secondary)>
            {
                ["guardian"] = ("Vitality", "Stamina"), ["warrior"] = ("Strength", "Speed"), ["ranger"] = ("Agility", "Luck"),
                ["mage"] = ("Intelligence", "Wisdom"), ["cleric"] = ("Wisdom", "Vitality"), ["alchemist"] = ("Luck", "Stamina"),
            };
            foreach (var t in c.SkillTrees.Values)
            {
                Assert.That(expected.ContainsKey(t.Id), Is.True, $"tree {t.Id} has no stat mapping");
                var (primary, secondary) = expected[t.Id];
                foreach (var opener in t.Nodes.Where(n => n.Id.EndsWith("_opener")))
                {
                    string branch = opener.Id.Substring(0, opener.Id.Length - "_opener".Length);
                    var s1 = t.Nodes.Single(n => n.Id == branch + "_stat1");
                    var s2 = t.Nodes.Single(n => n.Id == branch + "_stat2");
                    Assert.That(s1.GrantsStat.Stat, Is.EqualTo(primary), s1.Id);
                    Assert.That(s2.GrantsStat.Stat, Is.EqualTo(secondary), s2.Id);
                    Assert.That(s1.Prerequisites, Does.Contain(opener.Id), s1.Id);
                    Assert.That(s2.Prerequisites, Does.Contain(branch + "_core"), s2.Id);
                }
            }
            Assert.That(c.SkillTrees.Values.Sum(t => t.Nodes.Count(n => n.IsStatNode)), Is.GreaterThanOrEqualTo(36));
        }

        [Test]
        public void Validate_rejects_a_bad_stat_node()
        {
            var db = Mini();
            db.Classes["knight"].Role = "Tank";
            db.Classes["knight"].HomeTree = "guardian";
            db.Abilities["shield_bash"] = new AbilityDef { Id = "shield_bash", Name = "Shield Bash", Kind = AbilityKind.Skill };
            db.SkillTrees["guardian"] = new SkillTreeDef
            {
                Id = "guardian",
                Name = "Guardian",
                Nodes =
                {
                    new SkillNodeDef { Id = "ok", GrantsStat = new StatGrantDef { Stat = "Vitality", Amount = 3 } },
                    new SkillNodeDef { Id = "both", Grants = "shield_bash", GrantsStat = new StatGrantDef { Stat = "Vitality", Amount = 3 } },
                    new SkillNodeDef { Id = "shorthand", GrantsStat = new StatGrantDef { Stat = "CON", Amount = 3 } },
                    new SkillNodeDef { Id = "zero", GrantsStat = new StatGrantDef { Stat = "Luck", Amount = 0 } },
                    new SkillNodeDef { Id = "empty" },
                },
            };
            var problems = ContentLoader.Validate(db);
            Assert.That(problems.Any(p => p.Contains("node ok")), Is.False);
            Assert.That(problems.Any(p => p.Contains("node both") && p.Contains("mutually exclusive")), Is.True);
            Assert.That(problems.Any(p => p.Contains("node shorthand") && p.Contains("unknown stat 'CON'")), Is.True);
            Assert.That(problems.Any(p => p.Contains("node zero") && p.Contains("must be positive")), Is.True);
            Assert.That(problems.Any(p => p.Contains("node empty") && p.Contains("needs Grants")), Is.True);
            Assert.That(new StatGrantDef { Stat = "Wisdom", Amount = 2 }.ToStats().Wisdom, Is.EqualTo(2f));
        }

        [Test]
        public void Validate_rejects_a_hero_class_missing_or_unknown_home_tree()
        {
            var db = Mini();
            db.Classes["knight"].Role = "Tank";
            db.Classes["knight"].HomeTree = null;
            var problems = ContentLoader.Validate(db);
            Assert.That(problems.Any(p => p.Contains("knight") && p.Contains("HomeTree")), Is.True);

            db.Classes["knight"].HomeTree = "nope";
            problems = ContentLoader.Validate(db);
            Assert.That(problems.Any(p => p.Contains("unknown home tree 'nope'")), Is.True);
        }
    }
}
