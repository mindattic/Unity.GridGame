using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The outcome preview behind the confirm card and the drag preview (GG-T252, POL-D1).</summary>
    public class OutcomePreviewTests
    {
        private static MoveCommand Pincer(BattleState s) =>
            new MoveCommand { HeroId = s.Units.First(u => u.Def.Id == "cleric").Id, Path = new List<Coord> { new Coord(2, 1) } };

        [Test]
        public void A_pincer_preview_shows_damage_on_the_flanked_foe()
        {
            var (s, _) = Battle(Mini(), Stage(("goblin", 2, 2)), ("knight", 2, 3), ("cleric", 2, 0));
            var gob = s.Units.First(u => u.Def.Id == "goblin");
            var p = OutcomePreviewer.Preview(s, Pincer(s));
            Assert.That(p.Refused, Is.False);
            var t = p.Of(gob.Id);
            Assert.That(t, Is.Not.Null);
            Assert.That(t.DamageMax, Is.GreaterThan(0));
            Assert.That(t.DamageMin, Is.LessThanOrEqualTo(t.DamageMax));
            Assert.That(t.KillChance, Is.InRange(0f, 1f));
        }

        [Test]
        public void Previewing_never_changes_the_real_battle()
        {
            var (s, _) = Battle(Mini(), Stage(("goblin", 2, 2)), ("knight", 2, 3), ("cleric", 2, 0));
            var before = s.Units.Select(u => (u.Id, u.Hp, u.Pos)).ToList();
            long clock = s.Clock;
            int orbs = s.Bank.Count, charge = s.ManaCharge;
            var rng = s.Rng.Clone();
            OutcomePreviewer.Preview(s, Pincer(s));
            Assert.That(s.Units.Select(u => (u.Id, u.Hp, u.Pos)).ToList(), Is.EqualTo(before));
            Assert.That(s.Clock, Is.EqualTo(clock));
            Assert.That(s.Bank.Count, Is.EqualTo(orbs));
            Assert.That(s.ManaCharge, Is.EqualTo(charge));
            Assert.That(s.Rng.NextUInt(), Is.EqualTo(rng.NextUInt()), "the real dice are untouched");
        }

        [Test]
        public void The_preview_matches_what_the_command_then_does()
        {
            var (s, _) = Battle(Mini(), Stage(("goblin", 2, 2)), ("knight", 2, 3), ("cleric", 2, 0));
            var gob = s.Units.First(u => u.Def.Id == "goblin");
            var p = OutcomePreviewer.Preview(s, Pincer(s));
            int hp = gob.Hp;
            var ev = BattleEngine.Apply(s, Pincer(s));
            int dealt = ev.TakeWhile(e => !(e is TurnStartedEvent)).OfType<DamagedEvent>().Where(d => d.TargetId == gob.Id).Sum(d => d.Amount);
            var t = p.Of(gob.Id);
            Assert.That(dealt, Is.InRange(t.DamageMin, t.DamageMax), "sample 0 replays the real dice, so the real hit is inside the range");
        }
    }
}
