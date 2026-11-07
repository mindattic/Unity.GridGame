using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Void Sentinel (GG-T330, BIBLE §14.12): a slow, plated Guardian whose Void Beam is the first authored
    /// Line charge — it hits every hero still on the marked line and Silences them.</summary>
    public class VoidSentinelTests
    {
        [Test]
        public void The_void_sentinel_validates_is_a_slow_plated_line_caster_and_guards_the_anchor_wastes()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("voidsentinel") || p.Contains("void_beam") || p.Contains("void_core")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var c = db.Classes["voidsentinel"];
            Assert.That(c.BaseStats.Speed, Is.EqualTo(8f));
            Assert.That(c.Brain, Is.EqualTo(Brain.Guardian));
            Assert.That(c.ChargeShape, Is.EqualTo(ChargeShape.Line));
            Assert.That(c.ArmorPlate, Is.GreaterThan(0));
            Assert.That(c.HasTag("Sentient"), Is.False, "a construct of smoke: it supports pincers but never makes one");
            var beam = db.Abilities[c.ChargeAbility];
            Assert.That(beam.ApplyBuff, Is.EqualTo("silenced"));
            Assert.That(beam.RecoverSeconds, Is.GreaterThan(0f), "a heavy beam recovers longer");
            var anchorStages = db.Campaign.Themes.Where(t => t.Id == "cave").SelectMany(t => t.Stages).ToList();
            Assert.That(anchorStages.Any(id => db.Stages[id].Waves.Any(w => w.Enemies.Any(e => e.Class == "voidsentinel"))), "placed in the Anchor Wastes");
        }

        [Test]
        public void The_void_beam_hits_the_hero_on_its_line_and_spares_the_one_off_it()
        {
            var (s, _) = Battle(Real, Stage(("voidsentinel", 2, 0)), ("paladin", 2, 5), ("cleric", 5, 7));
            var paladin = Hero(s, "paladin"); var cleric = Hero(s, "cleric");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_void_beam") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var queued = seen.OfType<CastQueuedEvent>().FirstOrDefault(e => e.AbilityId == "charge_void_beam");
            Assert.That(queued, Is.Not.Null, "the sentinel charges its beam");
            Assert.That(queued.LineTiles, Does.Contain(paladin.Pos), "the beam's line is marked through the hero it aims at");
            var resolved = seen.OfType<AbilityResolvedEvent>().First(e => e.AbilityId == "charge_void_beam");
            Assert.That(resolved.Targets, Does.Contain(paladin.Id));
            Assert.That(resolved.Targets, Does.Not.Contain(cleric.Id), "a hero off the line is untouched");
        }

        [Test]
        public void Stepping_off_the_marked_line_dodges_the_beam()
        {
            var (s, _) = Battle(Real, Stage(("voidsentinel", 2, 0)), ("paladin", 2, 5), ("cleric", 5, 7));
            var paladin = Hero(s, "paladin");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<CastQueuedEvent>().Any(e => e.AbilityId == "charge_void_beam") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            Assert.That(seen.OfType<CastQueuedEvent>().Any(e => e.AbilityId == "charge_void_beam"), Is.True);
            paladin.Pos = new Coord(4, 5); // the hero steps off the marked column
            var after = new List<BattleEvent>();
            for (int i = 0; i < 8 && !after.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_void_beam") && !s.IsOver; i++)
                after.AddRange(Turn(s, new ShieldCommand()));
            var resolved = after.OfType<AbilityResolvedEvent>().First(e => e.AbilityId == "charge_void_beam");
            Assert.That(resolved.Targets, Is.Empty, "nobody stands on the line any more");
            Assert.That(paladin.HasBuff("silenced"), Is.False);
        }

        [Test]
        public void Its_core_flares_a_ward_when_it_drops_below_half()
        {
            var core = Real.Abilities["void_core"];
            Assert.That(core.Trigger.On, Is.EqualTo(TriggerOn.OnLowHp));
            Assert.That(core.Trigger.Threshold, Is.EqualTo(0.5f));
            Assert.That(core.Trigger.Effects.Single().Buff, Is.EqualTo("ward"));
            Assert.That(Real.Classes["voidsentinel"].Abilities, Does.Contain("void_core"));
        }
    }
}
