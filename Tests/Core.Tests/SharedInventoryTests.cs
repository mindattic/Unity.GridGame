using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T285: the party's item slots draw from one shared inventory, never a full copy per hero.</summary>
    public class SharedInventoryTests
    {
        [Test]
        public void Five_owned_potions_are_five_charges_across_the_whole_party()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Pool");
            Assert.That(save.Party.Count, Is.GreaterThan(1));
            save.Inventory["x_potion"] = 5;
            var stage = c.CampaignStages().First();
            var s = BattleEngine.Create(c, BattleFactory.Build(c, save, stage.Id, 7), new List<BattleEvent>());
            int charges = s.Units.Where(u => u.IsHero).SelectMany(u => u.Bar).Where(b => b != null && b.ItemId == "x_potion").Sum(b => b.Charges);
            Assert.That(charges, Is.EqualTo(5), "five owned potions, five charges in total");
        }

        [Test]
        public void A_fresh_party_still_starts_with_a_full_stack_each()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Fresh");
            int owned = save.Inventory.TryGetValue("x_potion", out var n) ? n : 0;
            var stage = c.CampaignStages().First();
            var s = BattleEngine.Create(c, BattleFactory.Build(c, save, stage.Id, 7), new List<BattleEvent>());
            int charges = s.Units.Where(u => u.IsHero).SelectMany(u => u.Bar).Where(b => b != null && b.ItemId == "x_potion").Sum(b => b.Charges);
            Assert.That(charges, Is.EqualTo(owned), "the starting potions are all on the bars, no more and no fewer");
        }
    }
}
