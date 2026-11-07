using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Lich (GG-T331, BIBLE §14.13): a boss caster that rains Soulfire on a 3×3, harvests every hero once at
    /// half HP, and leaves a grave that raises it again unless the party smashes it.</summary>
    public class LichTests
    {
        [Test]
        public void The_lich_validates_is_a_boss_caster_and_haunts_the_arkgrave()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("lich") || p.Contains("soulfire")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var c = db.Classes["lich"];
            Assert.That(c.HasTag("Boss") && c.HasTag("Undead") && c.HasTag("Sentient"), Is.True);
            Assert.That(c.Support.Range, Is.GreaterThanOrEqualTo(2), "a boss's support reaches further");
            Assert.That(c.BossPhases.Any(p => p.OnEnterAbility == "lich_soul_harvest"), Is.True);
            Assert.That(GridGame.Meta.Abilities.IsHeroUsable(db.Abilities["lich_soul_harvest"]), Is.False, "a boss-phase move no hero can learn");
            Assert.That(GridGame.Meta.Abilities.IsHeroUsable(db.Abilities["charge_soulfire"]), Is.False);
            var arkgrave = db.Campaign.Themes.Where(t => t.Id == "swamp").SelectMany(t => t.Stages).ToList();
            Assert.That(arkgrave.Any(id => db.Stages[id].Waves.Any(w => w.Enemies.Any(e => e.Class == "lich"))), "placed in the Arkgrave");
        }

        [Test]
        public void Soulfire_catches_every_hero_in_the_three_by_three()
        {
            var (s, _) = Battle(Real, Stage(("lich", 2, 0)), ("paladin", 2, 6), ("cleric", 3, 6), ("barbarian", 5, 2));
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_soulfire") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var fire = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_soulfire");
            Assert.That(fire, Is.Not.Null, "the lich charges and lands its Soulfire");
            var caught = s.Units.Where(u => u.IsHero && fire.Targets.Contains(u.Id)).ToList();
            Assert.That(caught.Count, Is.GreaterThanOrEqualTo(2), "the paladin and cleric stand together, so both are caught");
            Assert.That(caught.Any(h => h.Def.Id == "barbarian"), Is.False, "a hero standing apart is spared");
        }

        [Test]
        public void At_half_hp_the_lich_harvests_every_hero_to_heal_itself_once()
        {
            var (s, _) = Battle(Real, Stage(("lich", 2, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var lich = s.Units.Single(u => u.Def.Id == "lich");
            lich.Hp = (int)(lich.MaxHp * 0.45f);
            int before = lich.Hp;
            var heroHp = s.Units.Where(u => u.IsHero).ToDictionary(u => u.Id, u => u.Hp);
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 12 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "lich_soul_harvest") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var harvest = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "lich_soul_harvest");
            Assert.That(harvest, Is.Not.Null, "crossing half HP enters the harvest phase");
            Assert.That(harvest.Targets.Count, Is.EqualTo(2), "every hero is touched");
            Assert.That(lich.Hp, Is.GreaterThan(before), "it heals from what it drains");
            Assert.That(s.Units.Where(u => u.IsHero).All(h => h.Hp < heroHp[h.Id]), Is.True, "every hero loses HP");
            for (int i = 0; i < 6 && !s.IsOver; i++) seen.AddRange(Turn(s, new ShieldCommand()));
            Assert.That(seen.OfType<AbilityResolvedEvent>().Count(e => e.AbilityId == "lich_soul_harvest"), Is.EqualTo(1), "only once");
        }

        [Test]
        public void The_lich_falls_into_a_grave_that_raises_it_again()
        {
            var c = Real.Classes["lich"];
            Assert.That(c.Remains, Is.EqualTo("gravestone"));
            Assert.That(Real.Classes[c.Remains].HasTag("Remains"), Is.True, "smash the grave or it rises (§14.5)");
        }
    }
}
