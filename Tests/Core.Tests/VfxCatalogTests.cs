using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T269: vfx.json lists the effect prefabs and vfxstyle.json points elements, tiers and statuses at them.</summary>
    public class VfxCatalogTests
    {
        private static ContentDb FreshReal() => ContentLoader.Load(name =>
        {
            var p = Path.Combine(ContentDir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        });

        [Test]
        public void Every_prefab_reference_resolves_and_every_effect_is_normalized_and_sourced()
        {
            var c = FreshReal();
            Assert.That(c.VfxPrefabs.Effects, Is.Not.Empty);
            var p = new List<string>();
            VfxCatalog.Validate(c, p);
            Assert.That(p, Is.Empty, string.Join("\n", p));
            foreach (var e in c.VfxPrefabs.Effects)
                Assert.That(File.Exists(Path.Combine(ContentDir, "..", e.ResourcePath + ".prefab")), e.Id + " -> " + e.ResourcePath);
        }

        [Test]
        public void A_style_that_lists_tiers_leaves_unlisted_tiers_code_drawn()
        {
            var sheet = new VfxStyleSheet();
            var s = new VfxStyleDef { Id = "X", Prefab = "Big", TierPrefabs = new Dictionary<string, string> { ["Major"] = "Big" } };
            Assert.That(VfxCatalog.PrefabFor(sheet, s, VfxTier.Major), Is.EqualTo("Big"));
            Assert.That(VfxCatalog.PrefabFor(sheet, s, VfxTier.Minor), Is.Null);
            var plain = new VfxStyleDef { Id = "Y", Prefab = "Puff" };
            Assert.That(VfxCatalog.PrefabFor(sheet, plain, VfxTier.Ultimate), Is.EqualTo("Puff"));
            Assert.That(VfxCatalog.PrefabFor(sheet, new VfxStyleDef { Id = "Z" }, VfxTier.Minor), Is.Null);
        }

        [Test]
        public void Validation_catches_a_dangling_prefab_and_an_unbounded_once_effect()
        {
            var c = FreshReal();
            c.Vfx.Element(DamageType.Fire).Prefab = "NoSuchEffect";
            c.VfxPrefabs.Effects.Add(new VfxPrefabDef { Id = "Endless", Mode = VfxMode.Once, Duration = 0f, Extent = 1f, Source = "test" });
            var p = new List<string>();
            VfxCatalog.Validate(c, p);
            Assert.That(p.Any(x => x.Contains("NoSuchEffect")), string.Join("\n", p));
            Assert.That(p.Any(x => x.Contains("Endless") && x.Contains("Duration")), string.Join("\n", p));
        }

        [Test]
        public void Statuses_do_not_inherit_their_elements_impact_prefab()
        {
            var c = FreshReal();
            Assert.That(c.Vfx.Element(DamageType.Fire).Prefab, Is.Not.Null);
            Assert.That(c.Vfx.Status("burning").Prefab, Is.EqualTo("Flame.Loop"));
            foreach (var b in c.Buffs.Values)
            {
                var st = c.Vfx.Status(b.Id);
                if (st?.Prefab != null) Assert.That(c.VfxPrefabs.Find(st.Prefab), Is.Not.Null, b.Id);
            }
        }
    }
}
