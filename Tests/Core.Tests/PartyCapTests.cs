using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T291 (§25.14): the party cap is six — two attackers, each with two supporters — and a party of six
    /// builds, shares one bag and spawns legally on the 6×8 board.</summary>
    public class PartyCapTests
    {
        private static readonly string[] Six = { "paladin", "barbarian", "cleric", "redmage", "engineer", "knight" };

        [Test]
        public void The_cap_is_six_and_the_start_stays_three()
        {
            Assert.That(Progression.MaxPartySize, Is.EqualTo(6));
            Assert.That(Progression.StartingPartySize, Is.EqualTo(3));
        }

        [Test]
        public void A_seventh_hero_waits_in_reserve()
        {
            var c = Real;
            var s = Progression.NewGame(c, "Cap", Six);
            Assert.That(s.Party.Count, Is.EqualTo(6));
            Summon.Join(c, s, "sage");
            Assert.That(s.Hero("sage"), Is.Not.Null);
            Assert.That(s.Party, Does.Not.Contain("sage"), "a full party sends the joiner to the reserve");
            var toggle = Party.Toggle(s, "sage");
            Assert.That(toggle.Ok, Is.False);
            Assert.That(s.Party.Count, Is.EqualTo(6));
            Assert.That(Party.Toggle(s, "knight").Ok, Is.True, "moving one out frees the slot");
            Assert.That(Party.Toggle(s, "sage").Ok, Is.True);
            Assert.That(s.Party.Count, Is.EqualTo(6));
        }

        [Test]
        public void Six_heroes_spawn_on_free_tiles_below_the_enemy_band_on_every_campaign_stage()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Six", Six);
            foreach (var stage in c.CampaignStages())
            {
                var s = BattleEngine.Create(c, BattleFactory.Build(c, save, stage.Id, 3), new List<BattleEvent>());
                var heroes = s.Units.Where(u => u.IsHero && !u.IsGuest).ToList();
                Assert.That(heroes.Count, Is.EqualTo(6), stage.Id);
                Assert.That(heroes.All(u => u.OnBoard), Is.True, $"{stage.Id}: every hero is on the board");
                var tiles = s.Units.Where(u => u.OnBoard && u.Alive).SelectMany(u => u.Tiles()).ToList();
                Assert.That(tiles.Distinct().Count(), Is.EqualTo(tiles.Count), $"{stage.Id}: no two units share a tile");
                Assert.That(heroes.All(u => u.Pos.Y >= EncounterCatalog.EnemyRows - 1), Is.True,
                    $"{stage.Id}: heroes start on the party's half ({string.Join(", ", heroes.Select(u => $"{u.Def.Id}@{u.Pos.X},{u.Pos.Y}"))})");
            }
        }

        [Test]
        public void Six_heroes_share_one_bag()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Bag", Six);
            save.Inventory["x_potion"] = 5;
            var s = BattleEngine.Create(c, BattleFactory.Build(c, save, c.CampaignStages().First().Id, 7), new List<BattleEvent>());
            int charges = s.Units.Where(u => u.IsHero).SelectMany(u => u.Bar).Where(b => b != null && b.ItemId == "x_potion").Sum(b => b.Charges);
            Assert.That(charges, Is.EqualTo(5), "five owned potions are five charges across all six heroes");
        }
    }
}
