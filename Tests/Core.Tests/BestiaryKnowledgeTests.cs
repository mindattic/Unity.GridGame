using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T290 (§25.11): the Bestiary learns from defeats, and the Scan ability is gone.</summary>
    public class BestiaryKnowledgeTests
    {
        private static BestiaryEntry E(int defeated, bool seen = true, bool scanned = false) =>
            new BestiaryEntry { Seen = seen, Defeated = defeated, Scanned = scanned };

        [Test]
        public void Tier_follows_the_defeat_thresholds()
        {
            Assert.That(BestiaryKnowledge.Tier(null), Is.EqualTo(BestiaryKnowledge.None));
            Assert.That(BestiaryKnowledge.Tier(E(0, seen: false)), Is.EqualTo(BestiaryKnowledge.None));
            Assert.That(BestiaryKnowledge.Tier(E(0)), Is.EqualTo(BestiaryKnowledge.Description));
            Assert.That(BestiaryKnowledge.Tier(E(1)), Is.EqualTo(BestiaryKnowledge.Speed));
            Assert.That(BestiaryKnowledge.Tier(E(2)), Is.EqualTo(BestiaryKnowledge.Speed));
            Assert.That(BestiaryKnowledge.Tier(E(3)), Is.EqualTo(BestiaryKnowledge.Stats));
            Assert.That(BestiaryKnowledge.Tier(E(5)), Is.EqualTo(BestiaryKnowledge.Stats));
            Assert.That(BestiaryKnowledge.Tier(E(6)), Is.EqualTo(BestiaryKnowledge.Elements));
            Assert.That(BestiaryKnowledge.Tier(E(9)), Is.EqualTo(BestiaryKnowledge.Elements));
            Assert.That(BestiaryKnowledge.Tier(E(10)), Is.EqualTo(BestiaryKnowledge.Abilities));
            Assert.That(BestiaryKnowledge.Tier(E(250)), Is.EqualTo(BestiaryKnowledge.Abilities));
        }

        [Test]
        public void Thresholds_are_the_named_constants()
        {
            Assert.That(BestiaryKnowledge.DefeatsForSpeed, Is.EqualTo(1));
            Assert.That(BestiaryKnowledge.DefeatsForStats, Is.EqualTo(3));
            Assert.That(BestiaryKnowledge.DefeatsForElements, Is.EqualTo(6));
            Assert.That(BestiaryKnowledge.DefeatsForAbilities, Is.EqualTo(10));
        }

        [Test]
        public void DefeatsToNext_counts_down_to_each_tier_and_stops_at_the_last()
        {
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(0)), Is.EqualTo(1));
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(1)), Is.EqualTo(2));
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(4)), Is.EqualTo(2));
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(7)), Is.EqualTo(3));
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(10)), Is.EqualTo(0));
            Assert.That(BestiaryKnowledge.Hint(E(1)), Is.EqualTo("Defeat 2 more to learn its HP and stats."));
            Assert.That(BestiaryKnowledge.Hint(E(10)), Is.Null);
        }

        [Test]
        public void An_old_scanned_entry_keeps_at_least_the_stats_tier()
        {
            Assert.That(BestiaryKnowledge.Tier(E(0, scanned: true)), Is.EqualTo(BestiaryKnowledge.Stats));
            Assert.That(BestiaryKnowledge.Tier(E(1, scanned: true)), Is.EqualTo(BestiaryKnowledge.Stats));
            Assert.That(BestiaryKnowledge.Tier(E(7, scanned: true)), Is.EqualTo(BestiaryKnowledge.Elements));
            // Next to learn after a scanned Stats tier is Elements, at 6 defeats.
            Assert.That(BestiaryKnowledge.DefeatsToNext(E(0, scanned: true)), Is.EqualTo(6));
        }

        [Test]
        public void Tier_by_save_and_class_id()
        {
            var s = new SaveData();
            s.Bestiary["slime00"] = E(3);
            Assert.That(BestiaryKnowledge.Tier(s, "slime00"), Is.EqualTo(BestiaryKnowledge.Stats));
            Assert.That(BestiaryKnowledge.Tier(s, "nobody"), Is.EqualTo(BestiaryKnowledge.None));
            Assert.That(BestiaryKnowledge.Knows(s, "slime00", BestiaryKnowledge.Speed), Is.True);
            Assert.That(BestiaryKnowledge.Knows(s, "slime00", BestiaryKnowledge.Elements), Is.False);
        }

        [Test]
        public void Element_line_reads_the_class_multipliers()
        {
            var d = new ClassDef { Resistances = { [DamageType.Poison] = 0f, [DamageType.Fire] = 1.5f, [DamageType.Ice] = 0.5f } };
            var line = BestiaryKnowledge.ElementLine(d);
            Assert.That(line, Does.Contain("mmune to Poison"));
            Assert.That(line, Does.Contain("weak to Fire"));
            Assert.That(line, Does.Contain("resists Ice"));
            Assert.That(BestiaryKnowledge.ElementLine(new ClassDef()), Is.EqualTo("No elemental weakness or resistance"));
        }

        [Test]
        public void Migration_drops_the_retired_scan_from_known_and_the_bar()
        {
            var s = new SaveData { Roster = { new HeroSave { ClassId = "knight", Known = { "scan", "taunt" }, Bar = new List<string> { "taunt", "scan", null, null, null } } } };
            s.Bestiary["slime00"] = E(0, scanned: true);
            var back = SaveMigrations.Migrate(s);
            Assert.That(back.Roster[0].Known, Is.EqualTo(new[] { "taunt" }));
            Assert.That(back.Roster[0].Bar, Does.Not.Contain("scan"));
            Assert.That(back.Roster[0].Bar[0], Is.EqualTo("taunt"));
            Assert.That(BestiaryKnowledge.Tier(back.Bestiary["slime00"]), Is.EqualTo(BestiaryKnowledge.Stats));
        }

        [Test]
        public void No_content_references_scan()
        {
            var db = TestKit.Real;
            Assert.That(db.TryAbility("scan"), Is.Null, "the Scan ability is retired");
            foreach (var c in db.Classes.Values)
            {
                Assert.That(c.Abilities, Has.None.EqualTo("scan").IgnoreCase, c.Id);
                Assert.That(c.DefaultLoadout ?? new List<string>(), Has.None.EqualTo("scan").IgnoreCase, c.Id);
            }
            // Any JSON value naming the ability id (a kit, a skill-tree node, a shop) — VFX names like "ScanRays" don't count.
            var id = new Regex("\"scan\"", RegexOptions.IgnoreCase);
            foreach (var f in Directory.GetFiles(TestKit.ContentDir, "*.json"))
                Assert.That(id.IsMatch(File.ReadAllText(f)), Is.False, Path.GetFileName(f));
        }
    }
}
