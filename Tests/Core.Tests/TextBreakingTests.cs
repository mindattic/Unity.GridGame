using System.Linq;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T305: the pure hyphenation helper and the line breaker every wrapped label goes through.</summary>
    public class TextBreakingTests
    {
        private static string Show(string word) => Hyphenation.Hyphenate(word, '-');

        [TestCase("protection", "protec-tion")]
        [TestCase("description", "des-crip-tion")]
        [TestCase("summoning", "sum-mon-ing")]
        [TestCase("running", "run-ning")]
        [TestCase("falling", "fall-ing")]
        [TestCase("teaching", "teach-ing")]
        [TestCase("everything", "every-thing")]
        [TestCase("blacksmith", "black-smith")]
        [TestCase("monster", "mon-ster")]
        [TestCase("control", "con-trol")]
        [TestCase("little", "lit-tle")]
        [TestCase("simple", "sim-ple")]
        [TestCase("terrible", "ter-rible")]
        [TestCase("experience", "ex-perience")]
        [TestCase("resistance", "resis-tance")]
        [TestCase("darkness", "dark-ness")]
        [TestCase("unknown", "un-known")]
        [TestCase("question", "ques-tion")]
        [TestCase("lightning", "light-ning")]
        [TestCase("counterattack", "coun-ter-at-tack")]
        public void Hyphenates_common_words_at_safe_points(string word, string expected) =>
            Assert.That(Show(word), Is.EqualTo(expected));

        [TestCase("lemon")]      // too short (6+ letters only)
        [TestCase("damage")]     // a lone consonant between vowels is ambiguous: never split
        [TestCase("strength")]   // no vowel on one side of any split
        [TestCase("string")]     // "str-ing" would leave a vowelless head
        [TestCase("uniform")]    // un- only before a consonant
        public void Leaves_words_it_cannot_split_safely_alone(string word) =>
            Assert.That(Show(word), Is.EqualTo(word));

        [Test]
        public void Points_respect_left_and_right_minimums()
        {
            foreach (var w in new[] { "protection", "summoning", "blacksmith", "counterattack", "experience", "everything", "unknown" })
                foreach (int p in Hyphenation.Points(w))
                {
                    Assert.That(p, Is.GreaterThanOrEqualTo(Hyphenation.LeftMin), w);
                    Assert.That(w.Length - p, Is.GreaterThanOrEqualTo(Hyphenation.RightMin), w);
                }
        }

        [Test]
        public void Never_hyphenates_proper_nouns_keywords_numbers_or_rich_text()
        {
            string s = "Counterstrike [counterstrike] 1234567890 <color=#9CC8FF>summoning protection</color> Longmother summoning";
            Assert.That(Hyphenation.Hyphenate(s, '-'),
                Is.EqualTo("Counterstrike [counterstrike] 1234567890 <color=#9CC8FF>summoning protection</color> Longmother sum-mon-ing"));
        }

        [Test]
        public void Hyphenate_inserts_soft_hyphens_and_keeps_spacing_and_punctuation()
        {
            string h = Hyphenation.Hyphenate("A protection,\n  summoning.");
            string shy = ((char)0xAD).ToString();
            Assert.That(h, Is.EqualTo("A protec" + shy + "tion,\n  sum" + shy + "mon" + shy + "ing."));
            Assert.That(h.Replace(shy, ""), Is.EqualTo("A protection,\n  summoning."));
        }

        [Test]
        public void Is_deterministic()
        {
            var a = Hyphenation.Hyphenate("the enchantress teaches everything about resistance");
            for (int i = 0; i < 5; i++) Assert.That(Hyphenation.Hyphenate("the enchantress teaches everything about resistance"), Is.EqualTo(a));
        }

        // A monospace "font": every character is 10 units wide.
        private static float Mono(string s) => s.Length * 10f;

        [Test]
        public void Untouched_when_the_greedy_wrap_is_already_clean()
        {
            var r = LineBreaker.Break("one two three four", 100f, Mono, false);
            Assert.That(r.Lines, Is.EqualTo(new[] { "one two", "three four" }));
            Assert.That(r.Changed, Is.False);
        }

        [Test]
        public void A_lone_last_word_takes_the_word_before_it_down()
        {
            var r = LineBreaker.Break("alpha beta gamma delta", 170f, Mono, false);
            Assert.That(r.Lines, Is.EqualTo(new[] { "alpha beta", "gamma delta" }));
            Assert.That(r.Changed, Is.True);
        }

        [Test]
        public void The_widow_fix_never_strands_a_lone_dash_above()
        {
            var r = LineBreaker.Break("— Osk, quartermaster", 140f, Mono, false);
            Assert.That(r.Lines, Is.EqualTo(new[] { "— Osk,", "quartermaster" }));
        }

        [Test]
        public void A_single_letter_word_never_ends_a_line()
        {
            var r = LineBreaker.Break("cast a spell on them all now", 70f, Mono, false);
            Assert.That(r.Lines.Any(l => l.EndsWith(" a")), Is.False, string.Join("|", r.Lines));
            Assert.That(r.Lines.All(l => Mono(l) <= 70f), Is.True, string.Join("|", r.Lines));
        }

        [Test]
        public void An_overlong_word_is_hyphenated_never_cut_mid_word()
        {
            var r = LineBreaker.Break("the counterattack", 90f, Mono, false);
            Assert.That(r.Changed, Is.True);
            Assert.That(r.Lines.All(l => Mono(l) <= 90f), Is.True, string.Join("|", r.Lines));
            Assert.That(string.Join("|", r.Lines), Is.EqualTo("the|counter-|attack"));
        }

        [Test]
        public void Word_wrap_mode_moves_a_word_down_hyphenate_mode_fills_the_ragged_gap()
        {
            string text = "we are summoning protection";
            var word = LineBreaker.Break(text, 140f, Mono, false);
            Assert.That(word.Lines[0], Is.EqualTo("we are"));
            var hyph = LineBreaker.Break(text, 140f, Mono, true);
            Assert.That(hyph.Lines, Is.EqualTo(new[] { "we are summon-", "ing protection" }));
            Assert.That(hyph.Lines.All(l => Mono(l) <= 140f), Is.True, string.Join("|", hyph.Lines));
            Assert.That(hyph.Changed, Is.True);
        }

        [Test]
        public void Text_wrapping_setting_defaults_to_word_wrap_and_round_trips_through_settings_json()
        {
            Assert.That(new SettingsSave().Hyphenate, Is.False);
            var ps = Unity.Shared.Core.ProfileSettings.CreateDefault("t");
            Assert.That(GaidenSettings.Read(ps).Hyphenate, Is.False);
            GaidenSettings.Write(ps, new SettingsSave { Hyphenate = true });
            Assert.That(ps.Game.GetBool(GaidenSettings.Keys.Hyphenate), Is.True);
            Assert.That(GaidenSettings.Read(ps).Hyphenate, Is.True);
        }

        [Test]
        public void Explicit_newlines_and_blank_lines_survive()
        {
            var r = LineBreaker.Break("first\n\nsecond", 200f, Mono, false);
            Assert.That(r.Lines, Is.EqualTo(new[] { "first", "", "second" }));
            Assert.That(r.Changed, Is.False);
        }
    }
}
