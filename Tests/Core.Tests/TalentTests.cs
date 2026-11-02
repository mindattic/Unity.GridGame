using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Spending skill points on tree nodes and the Inn's Respec (§25.6.3, GG-T53).</summary>
    public class TalentTests
    {
        private static (ContentDb c, SaveData s, HeroSave h, SkillTreeDef t) Setup()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Talents");
            CampNpcs.JoinAll(s);
            var h = s.Hero(s.Party[0]);
            return (c, s, h, Talents.HomeTree(c, h));
        }

        [Test]
        public void Home_tree_comes_first_and_starting_kit_nodes_are_innate()
        {
            var (c, _, h, t) = Setup();
            Assert.That(Talents.Trees(c, h).First(), Is.SameAs(t));
            foreach (var n in t.Nodes.Where(n => h.Known.Contains(n.Grants)))
                Assert.That(Talents.State(h, t, n), Is.EqualTo(TalentState.Innate), n.Id);
            Assert.That(Talents.PointsSpent(c, h), Is.Zero);
        }

        [Test]
        public void Unlock_spends_points_grants_the_ability_and_respects_gates()
        {
            var (c, s, h, t) = Setup();
            var open = t.Nodes.First(n => Talents.State(h, t, n) == TalentState.Available);
            h.SkillPoints = 0;
            Assert.That(Talents.Unlock(c, s, h, t.Id, open.Id).Ok, Is.False, "no points");
            h.SkillPoints = 50;
            Assert.That(Talents.Unlock(c, s, h, t.Id, open.Id).Ok, Is.True);
            Assert.That(h.SkillPoints, Is.EqualTo(50 - open.Cost));
            Assert.That(h.Known, Does.Contain(open.Grants));
            Assert.That(Talents.State(h, t, open), Is.EqualTo(TalentState.Purchased));
            Assert.That(Talents.Unlock(c, s, h, t.Id, open.Id).Ok, Is.False, "twice");

            var locked = t.Nodes.FirstOrDefault(n => Talents.State(h, t, n) == TalentState.Locked);
            if (locked != null)
            {
                int before = h.SkillPoints;
                Assert.That(Talents.Unlock(c, s, h, t.Id, locked.Id).Ok, Is.False);
                Assert.That(h.SkillPoints, Is.EqualTo(before));
            }
        }

        [Test]
        public void Respec_refunds_bought_points_for_gold_that_scales_with_level_and_keeps_the_starting_kit()
        {
            var (c, s, h, t) = Setup();
            var kit = h.Known.ToList();
            h.SkillPoints = 50;
            var open = t.Nodes.First(n => Talents.State(h, t, n) == TalentState.Available);
            Assert.That(Talents.Unlock(c, s, h, t.Id, open.Id).Ok, Is.True);
            h.Bar[0] = open.Grants;

            s.Gold = 0;
            Assert.That(Talents.Respec(c, s, h).Ok, Is.False, "can't afford");
            int cost = Talents.RespecCost(h);
            s.Gold = cost;
            Assert.That(Talents.Respec(c, s, h).Ok, Is.True);
            Assert.That(s.Gold, Is.Zero);
            Assert.That(h.SkillPoints, Is.EqualTo(50));
            Assert.That(h.TreeNodes, Is.Empty);
            Assert.That(h.Known, Is.EquivalentTo(kit));
            Assert.That(h.Bar[0], Is.Null);
            Assert.That(Talents.Respec(c, s, h).Ok, Is.False, "nothing to refund");

            int low = Talents.RespecCost(h);
            h.TotalXp += 100000;
            Assert.That(Talents.RespecCost(h), Is.GreaterThan(low));
        }

        [Test]
        public void Respec_needs_the_Innkeeper()
        {
            var c = Real;
            var s = Progression.NewGame(c, "NoInn");
            var h = s.Hero(s.Party[0]);
            var t = Talents.HomeTree(c, h);
            h.SkillPoints = 50;
            var open = t.Nodes.First(n => Talents.State(h, t, n) == TalentState.Available);
            Assert.That(Talents.Unlock(c, s, h, t.Id, open.Id).Ok, Is.True);
            s.Gold = 100000;
            if (!CampUnlocks.NpcOpen(s, CampNpcs.Innkeeper))
                Assert.That(Talents.Respec(c, s, h).Ok, Is.False);
        }
    }
}
