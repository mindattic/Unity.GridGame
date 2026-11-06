using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T300: the [term] grammar, glossary.json and its validation.</summary>
    public class GlossaryTests
    {
        private static string Show(IEnumerable<GlossaryRun> runs) => string.Join("|", runs.Select(r => r.ToString()));

        [Test]
        public void Glossary_loads_and_resolves_terms_ids_and_aliases_case_insensitively()
        {
            var db = TestKit.Real;
            Assert.That(db.GlossaryOrder.Count, Is.GreaterThan(40));
            Assert.That(db.TryTerm("Counterstrike")?.Id, Is.EqualTo("counterstrike"));
            Assert.That(db.TryTerm("counter-strike")?.Id, Is.EqualTo("counterstrike"));
            Assert.That(db.TryTerm("BURN")?.Id, Is.EqualTo("burning"));
            Assert.That(db.TryTerm(" slight ")?.Id, Is.EqualTo("slight"));
            Assert.That(db.TryTerm("no-such-term"), Is.Null);
            foreach (var b in db.Buffs.Values) Assert.That(db.TryTerm(b.Name), Is.Not.Null, b.Name);
            foreach (var band in Glossary.ChanceBands) Assert.That(db.TryTerm(band.Word), Is.Not.Null, band.Word);
        }

        [Test]
        public void Real_content_has_no_unknown_terms()
        {
            var problems = ContentLoader.Validate(TestKit.Real).Where(p => p.Contains("glossary")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Validator_reports_an_unknown_term_and_accepts_text_without_brackets()
        {
            var db = new ContentDb();
            db.GlossaryOrder.Add(new GlossaryEntry { Id = "counterstrike", Term = "Counterstrike", Aliases = { "Counter" }, Text = "Hits back." });
            Glossary.Index(db);
            db.Abilities["plain"] = new AbilityDef { Id = "plain", Name = "Plain", Description = "No keywords at all." };
            db.Abilities["ok"] = new AbilityDef { Id = "ok", Name = "Ok", Description = "Strikes back with a [counter]." };
            db.Abilities["bad"] = new AbilityDef { Id = "bad", Name = "Bad", Description = "A [wibble] chance." };
            var p = new List<string>();
            Glossary.Validate(db, p);
            Assert.That(p, Is.EqualTo(new[] { "ability bad: unknown glossary term [wibble]" }));
        }

        [Test]
        public void Validator_reports_key_clashes_and_bad_links()
        {
            var db = new ContentDb();
            db.GlossaryOrder.Add(new GlossaryEntry { Id = "a", Term = "Alpha", Text = "x" });
            db.GlossaryOrder.Add(new GlossaryEntry { Id = "b", Term = "Beta", Aliases = { "alpha" }, Text = "y", LinkKind = GlossaryLinkKind.Buff, LinkId = "nope" });
            Glossary.Index(db);
            var p = new List<string>();
            Glossary.Validate(db, p);
            Assert.That(p, Has.Some.Contains("'alpha' is already a key of glossary a"));
            Assert.That(p, Has.Some.Contains("unknown buff 'nope'"));
        }

        [Test]
        public void Parse_splits_terms_punctuation_adjacent_and_nested_brackets()
        {
            Assert.That(Show(Glossary.Parse("when attacked [directly] you have a [slight] chance of a [Counterstrike].")),
                Is.EqualTo("when attacked |[directly]| you have a |[slight]| chance of a |[Counterstrike]|."));
            Assert.That(Show(Glossary.Parse("[healer][buffer]")), Is.EqualTo("[healer]|[buffer]"));
            Assert.That(Show(Glossary.Parse("[a [b] c]")), Is.EqualTo("[a |[b]| c]"));
            Assert.That(Show(Glossary.Parse("[[b]]")), Is.EqualTo("[|[b]|]"));
            Assert.That(Show(Glossary.Parse("[] and [ ] and [open")), Is.EqualTo("[] and [ ] and [open"));
            Assert.That(Show(Glossary.Parse("no terms")), Is.EqualTo("no terms"));
            Assert.That(Glossary.Plain("Gain [Protection]."), Is.EqualTo("Gain Protection."));
            Assert.That(Glossary.Terms("[combo finishes] and [Wet]"), Is.EqualTo(new[] { "combo finishes", "Wet" }));
        }

        [Test]
        public void Words_keep_punctuation_on_its_term_and_break_on_newlines()
        {
            var w = Glossary.Words("Strikes a [Counterstrike]. Then\n[healer][buffer] go");
            Assert.That(w.Select(Show), Is.EqualTo(new[] { "Strikes", "a", "[Counterstrike]|.", "Then", "", "[healer]|[buffer]", "go" }));
        }

        [Test]
        public void Words_rebalance_rich_text_tags_across_words()
        {
            var w = Glossary.Words("<color=#FF0000>two words</color> [Wet]");
            Assert.That(w.Select(Show), Is.EqualTo(new[] { "<color=#FF0000>two</color>", "<color=#FF0000>words</color>", "[Wet]" }));
        }

        [Test]
        public void Chance_words_use_the_one_band_table()
        {
            Assert.That(Glossary.ChanceWord(0.10f), Is.EqualTo("[slight]"));
            Assert.That(Glossary.ChanceWord(0.15f), Is.EqualTo("[slight]"));
            Assert.That(Glossary.ChanceWord(0.20f), Is.EqualTo("[small]"));
            Assert.That(Glossary.ChanceWord(0.35f), Is.EqualTo("[fair]"));
            Assert.That(Glossary.ChanceWord(0.50f), Is.EqualTo("[good]"));
            Assert.That(Glossary.ChanceWord(0.75f), Is.EqualTo("[likely]"));
            Assert.That(Glossary.ChanceWord(1f), Is.EqualTo("[likely]"));
            Assert.That(Glossary.ChanceWord(0f), Is.Null);
        }
    }
}
