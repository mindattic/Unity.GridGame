using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T253: vfxstyle.json is the one look table (POL-E4) and abilities land at a tier (POL-E5).</summary>
    public class VfxStyleTests
    {
        private static ContentDb FreshReal() => ContentLoader.Load(name =>
        {
            var p = Path.Combine(ContentDir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        });

        [Test]
        public void Every_element_and_status_has_a_valid_look_and_every_tier_is_defined()
        {
            var c = FreshReal();
            foreach (DamageType t in System.Enum.GetValues(typeof(DamageType))) Assert.That(c.Vfx.Element(t), Is.Not.Null, t.ToString());
            foreach (var b in c.Buffs.Values) Assert.That(c.Vfx.Status(b.Id), Is.Not.Null, b.Id);
            var p = new List<string>();
            VfxStyleSheet.Validate(c, p);
            Assert.That(p, Is.Empty, string.Join("\n", p));
        }

        [Test]
        public void A_status_inherits_its_elements_look_and_keeps_its_own_overrides()
        {
            var c = FreshReal();
            var fire = c.Vfx.Element(DamageType.Fire);
            var burning = c.Vfx.Status("burning");
            Assert.That(burning.Core, Is.EqualTo(fire.Core));
            Assert.That(burning.Shape, Is.EqualTo(fire.Shape));
            Assert.That(burning.Cue, Is.EqualTo(fire.Cue));
            var frozen = c.Vfx.Status("frozen");
            Assert.That(frozen.Motion, Is.EqualTo("implode"));
            Assert.That(frozen.Core, Is.EqualTo(c.Vfx.Element(DamageType.Ice).Core));
            Assert.That(frozen.TintHolds, Is.True);
        }

        [Test]
        public void A_status_names_its_own_prefab_rather_than_inheriting_its_elements_impact()
        {
            // GG-T269: a burning unit doesn't replay the Fire impact explosion; a status's prefab is its own.
            var sheet = new VfxStyleSheet
            {
                Elements = { new VfxStyleDef { Id = "Fire", Core = "#FF7326", Prefab = "fx_fire_burst" } },
                Statuses = { new VfxStyleDef { Id = "burning", Element = "Fire" }, new VfxStyleDef { Id = "warm", Element = "Fire", Prefab = "fx_warm" } },
                Tiers = { new VfxTierDef { Tier = VfxTier.Ultimate, Prefab = "fx_big" } },
            };
            Assert.That(sheet.Status("burning").Prefab, Is.Null);
            Assert.That(sheet.Status("warm").Prefab, Is.EqualTo("fx_warm"));
            Assert.That(sheet.TierDef(VfxTier.Ultimate).Prefab, Is.EqualTo("fx_big"));
            Assert.That(FreshReal().Vfx.Element(DamageType.Fire).Prefab, Is.Not.Null, "Fire is wired to an effect prefab");
        }

        [Test]
        public void Tiers_scale_up_and_an_ability_tier_comes_from_its_price_unless_set()
        {
            var c = FreshReal();
            var minor = c.Vfx.TierDef(VfxTier.Minor); var major = c.Vfx.TierDef(VfxTier.Major); var ult = c.Vfx.TierDef(VfxTier.Ultimate);
            Assert.That(minor.Count, Is.LessThan(major.Count)); Assert.That(major.Count, Is.LessThan(ult.Count));
            Assert.That(minor.HitStop, Is.LessThan(major.HitStop)); Assert.That(major.HitStop, Is.LessThan(ult.HitStop));
            Assert.That(VfxStyleSheet.TierOf(new AbilityDef { Cost = 1 }), Is.EqualTo(VfxTier.Minor));
            Assert.That(VfxStyleSheet.TierOf(new AbilityDef { Cost = 2 }), Is.EqualTo(VfxTier.Major));
            Assert.That(VfxStyleSheet.TierOf(new AbilityDef { Cost = 3 }), Is.EqualTo(VfxTier.Ultimate));
            Assert.That(VfxStyleSheet.TierOf(new AbilityDef { CooldownTurns = 5 }), Is.EqualTo(VfxTier.Ultimate));
            Assert.That(VfxStyleSheet.TierOf(new AbilityDef { Cost = 3, Intensity = "Minor" }), Is.EqualTo(VfxTier.Minor));
            Assert.That(c.Abilities.Values.Select(VfxStyleSheet.TierOf).Distinct().Count(), Is.EqualTo(3), "real content spans all three tiers");
        }

        [Test]
        public void A_bad_style_is_reported()
        {
            var c = FreshReal();
            c.Vfx = new VfxStyleSheet
            {
                Elements = { new VfxStyleDef { Id = "Fire", Core = "orange", Shape = "blob", Motion = "zoom", Cue = "fire", Time = "Slow" } },
            };
            var p = new List<string>();
            VfxStyleSheet.Validate(c, p);
            Assert.That(p, Has.Some.Contains("no style for element Ice"));
            Assert.That(p, Has.Some.Contains("not #RRGGBB"));
            Assert.That(p, Has.Some.Contains("unknown Shape"));
            Assert.That(p, Has.Some.Contains("unknown Motion"));
            Assert.That(p, Has.Some.Contains("off the sound taxonomy"));
            Assert.That(p, Has.Some.Contains("not a timing token"));
        }
    }
}
