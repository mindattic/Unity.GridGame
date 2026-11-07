using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Choir (GG-T335, BIBLE §14.17): the Pale Concord's engineer-priests. A Cantor whose Tithe Hymn reaches the
    /// whole party and can Silence it, a Psalmist that Empowers the Concord, and a Confessor that heals, cleanses and wards
    /// its most wounded ally.</summary>
    public class ChoirTests
    {
        private static readonly string[] Family = { "cultist01", "cultist02", "cultist03" };

        [Test]
        public void The_choir_validates_plays_three_roles_and_sings_in_halcyon()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("cultist") || p.Contains("choir") || p.Contains("tithe_hymn")
                                                                 || p.Contains("psalm_of_certainty") || p.Contains("absolution")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var halcyon = db.Campaign.Themes.Where(t => t.Id == "cityruins").SelectMany(t => t.Stages).ToList();
            foreach (var id in Family)
            {
                var c = db.Classes[id];
                Assert.That(c.HasTag("Sentient"), Is.True, $"{id}: the Concord's own");
                Assert.That(c.DropTable, Is.EqualTo("choir"), id);
                Assert.That(c.Resistances[DamageType.Arcane], Is.LessThan(1f), $"{id}: the hymn is aether");
                Assert.That(c.ChargeAbility, Does.StartWith("charge_"), $"{id}: every Choir role is a song");
                Assert.That(halcyon.Any(s => db.Stages[s].Waves.Any(w => w.Enemies.Any(e => e.Class == id))), $"{id} sings in Halcyon");
            }
            Assert.That(Family.Select(id => db.Classes[id].Brain).Distinct().Count(), Is.EqualTo(3), "three roles");
        }

        [Test]
        public void The_tithe_hymn_reaches_every_hero()
        {
            var (s, _) = Battle(Real, Stage(("cultist01", 2, 0)), ("paladin", 0, 7), ("cleric", 5, 7), ("redmage", 3, 6));
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_tithe_hymn") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var hymn = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_tithe_hymn");
            Assert.That(hymn, Is.Not.Null, "the cantor charges its hymn");
            foreach (var h in s.Units.Where(u => u.IsHero)) Assert.That(hymn.Targets, Does.Contain(h.Id), h.Def.Id);
            Assert.That(Real.Abilities["charge_tithe_hymn"].ApplyBuff, Is.EqualTo("silenced"));
        }

        [Test]
        public void The_confessor_absolves_its_most_wounded_ally()
        {
            var (s, _) = Battle(Real, Stage(("cultist03", 1, 0), ("cultist01", 4, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var cantor = s.Units.Single(u => u.Def.Id == "cultist01");
            cantor.Hp = cantor.MaxHp / 3;
            cantor.Buffs.Add(new BuffInstance { Def = Real.Buffs["blinded"], Remaining = 3 });
            int before = cantor.Hp;
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_absolution") && !s.IsOver; i++)
                seen.AddRange(Turn(s, new ShieldCommand()));
            var rite = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_absolution");
            Assert.That(rite, Is.Not.Null, "the confessor charges absolution on a wounded ally");
            Assert.That(rite.Targets, Does.Contain(cantor.Id));
            Assert.That(cantor.Hp, Is.GreaterThan(before));
            Assert.That(cantor.HasBuff("blinded"), Is.False, "its debuffs are cleansed");
            Assert.That(cantor.HasBuff("ward"), Is.True);
        }

        [Test]
        public void The_psalm_empowers_the_concord_near_the_psalmist()
        {
            var (s, _) = Battle(Real, Stage(("cultist02", 2, 0), ("cultist03", 4, 0)), ("paladin", 0, 7), ("cleric", 5, 7));
            var confessor = s.Units.Single(u => u.Def.Id == "cultist03");
            var psalmist = s.Units.Single(u => u.Def.Id == "cultist02");
            var seen = new List<BattleEvent>();
            for (int i = 0; i < 16 && !seen.OfType<AbilityResolvedEvent>().Any(e => e.AbilityId == "charge_psalm_of_certainty") && !s.IsOver; i++)
            {
                confessor.Pos = new GridGame.Core.Coord(psalmist.Pos.X + (psalmist.Pos.X < 5 ? 1 : -1), psalmist.Pos.Y); // keep it beside the singer
                seen.AddRange(Turn(s, new ShieldCommand()));
            }
            var psalm = seen.OfType<AbilityResolvedEvent>().FirstOrDefault(e => e.AbilityId == "charge_psalm_of_certainty");
            Assert.That(psalm, Is.Not.Null, "the psalmist charges its psalm");
            Assert.That(seen.OfType<BuffAppliedEvent>().Any(b => b.UnitId == confessor.Id && b.BuffId == "empowered"), Is.True);
        }
    }
}
