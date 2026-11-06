using System;
using System.Collections.Generic;
using System.Text;

namespace GridGame.Meta
{
    /// <summary>
    /// GG-T305: deterministic English hyphenation, engine-free so the dotnet tests pin it. A compact rule set in the spirit
    /// of Liang/TeX (left 2 / right 3 letters, words of 6+ letters) that only proposes breaks it is sure of: known suffixes
    /// (-tion, -ment, -ness, -ing …, splitting a doubled consonant before -ing/-able), a few prefixes (un-, dis-, over-,
    /// under-, inter-, counter-, super-), and the split of a consonant cluster between vowels that keeps the longest legal
    /// onset with the next syllable (con-trol, mon-ster, black-smith, lit-tle) and never splits a digraph (ch, ck, gh, ph,
    /// sh, th, wh). A lone consonant between vowels (le-mon / lem-on) is ambiguous in English, so it is never split.
    /// Only plain lowercase words are hyphenated: never a capitalised word (proper nouns), a number, or anything inside a
    /// [bracketed] keyword or a rich-text span.
    /// </summary>
    public static class Hyphenation
    {
        public const int MinWord = 6, LeftMin = 2, RightMin = 3;
        /// <summary>U+00AD SOFT HYPHEN (written as a number so no invisible character sits in the source).</summary>
        public const char SoftHyphen = (char)0xAD;

        private static readonly string[] Suffixes =
            { "things", "thing", "tious", "cious", "ments", "ment", "tion", "sion", "cian", "tial", "cial", "ness", "less", "ful", "able", "ible", "ings", "ing" };
        private static readonly string[] Prefixes = { "counter", "under", "inter", "super", "over", "dis", "un" };
        /// <summary>Prefixes that only count before a consonant (un-able but uni-form, dis-tance but disa-ster).</summary>
        private static readonly HashSet<string> ConsonantPrefixes = new HashSet<string> { "un", "dis", "inter" };
        private static readonly HashSet<string> Onsets = new HashSet<string>
        {
            "bl", "br", "cl", "cr", "dr", "fl", "fr", "gl", "gr", "pl", "pr", "tr", "ch", "sh", "th", "ph", "wh",
            "sc", "sk", "sm", "sn", "sp", "st", "sw", "scr", "spr", "str", "spl", "thr", "chr", "phr", "shr",
        };
        private static readonly HashSet<string> Digraphs = new HashSet<string> { "ch", "ck", "gh", "ph", "sh", "th", "wh" };
        /// <summary>The handful of words the rules get wrong, spelled out (TeX keeps an exception list too).</summary>
        private static readonly Dictionary<string, int[]> Exceptions = new Dictionary<string, int[]>
        {
            ["lightning"] = new[] { 5 },
        };

        private static bool IsVowel(string w, int i)
        {
            char c = w[i];
            if (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u') return true;
            // y is a vowel after a consonant (every, cyber, lawyer), a consonant at the start or after a vowel (yes, player).
            return c == 'y' && i > 0 && !IsVowel(w, i - 1);
        }

        private static bool HasVowel(string w, int from, int to)
        {
            for (int i = from; i < to; i++) if (IsVowel(w, i)) return true;
            return false;
        }

        private static bool AllLower(string w)
        {
            foreach (char c in w) if (c < 'a' || c > 'z') return false;
            return true;
        }

        /// <summary>
        /// Break points of one word: index i means "word[..i]-" / "word[i..]". Case-insensitive; empty for anything that
        /// isn't all letters or is shorter than <see cref="MinWord"/>.
        /// </summary>
        public static IReadOnlyList<int> Points(string word)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(word) || word.Length < MinWord) return result;
            string w = word.ToLowerInvariant();
            if (!AllLower(w)) return result;
            if (Exceptions.TryGetValue(w, out var ex)) { result.AddRange(ex); return result; }
            int n = w.Length;
            var pts = new SortedSet<int>();

            // Suffixes: break before them (a doubled consonant before -ing/-able/-ible splits between the doubles instead:
            // run-ning, ter-rible; but a stem's own ll/ss/ff/zz keeps it: fall-ing, pass-ing).
            int suffixAt = -1;
            bool vowelSuffix = false;
            foreach (var s in Suffixes)
            {
                if (!w.EndsWith(s, StringComparison.Ordinal)) continue;
                int at = n - s.Length;
                if (s.StartsWith("thing", StringComparison.Ordinal) && at < 3) continue; // no-thing → noth-ing below
                bool vs = s[0] == 'i' || s[0] == 'a';
                if (vs && at >= 2 && w[at - 1] == w[at - 2] && !IsVowel(w, at - 1)
                    && (s[0] != 'i' || s.StartsWith("ible", StringComparison.Ordinal) || "lsfz".IndexOf(w[at - 1]) < 0))
                    at--;
                if (at < LeftMin || !HasVowel(w, 0, at)) continue;
                pts.Add(at);
                suffixAt = n - s.Length;
                vowelSuffix = vs;
                break;
            }

            foreach (var p in Prefixes)
            {
                if (!w.StartsWith(p, StringComparison.Ordinal) || n - p.Length < RightMin) continue;
                if (ConsonantPrefixes.Contains(p) && IsVowel(w, p.Length)) continue;
                pts.Add(p.Length);
                break;
            }

            // Consonant clusters between two vowels: the smallest split whose right part is a legal onset.
            for (int i = 1; i < n; i++)
            {
                if (IsVowel(w, i) || !IsVowel(w, i - 1)) continue;
                int s = i, e = i;
                while (e < n && !IsVowel(w, e)) e++;
                i = e;
                if (e >= n) break;
                int k = e - s;
                if (k == 1)
                {
                    if (w[s] == 'x') pts.Add(s + 1); // ex-ample, tax-ing
                    continue;
                }
                if (vowelSuffix && e == suffixAt) continue; // the suffix rule already placed this break (teach-ing, sing-ing)
                if (pts.GetViewBetween(s + 1, e - 1).Count > 0) continue; // a prefix/suffix break inside it wins (un-known, dark-ness)
                // A final consonant + "le" is its own syllable: lit-tle, can-dle, sim-ple.
                bool finalLe = e == n - 1 && w[e] == 'e' && w[e - 1] == 'l';
                for (int p = s + 1; p < e; p++)
                {
                    if (Digraphs.Contains(w.Substring(p - 1, 2))) continue;
                    string right = w.Substring(p, e - p);
                    bool onset = right.Length == 1 || Onsets.Contains(right) || (finalLe && right.Length == 2 && right[1] == 'l');
                    if (!onset) continue;
                    pts.Add(p);
                    break;
                }
            }

            foreach (int p in pts)
                if (p >= LeftMin && p <= n - RightMin && HasVowel(w, 0, p) && HasVowel(w, p, n)) result.Add(p);
            return result;
        }

        /// <summary>
        /// <paramref name="text"/> with <paramref name="marker"/> (a soft hyphen by default) at every break point of every
        /// hyphenatable word: plain lowercase words only, never inside [brackets], rich-text spans, numbers or proper nouns.
        /// </summary>
        public static string Hyphenate(string text, char marker = SoftHyphen)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var tokens = Tokenize(text);
            var sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) sb.Append(tokens[i].Separator);
                var t = tokens[i];
                if (!t.Eligible) { sb.Append(t.Text); continue; }
                var pts = WordPoints(t.Text, false);
                int last = 0;
                foreach (int p in pts) { sb.Append(t.Text, last, p - last).Append(marker); last = p; }
                sb.Append(t.Text, last, t.Text.Length - last);
            }
            return sb.ToString();
        }

        internal struct Token
        {
            public string Text;
            /// <summary>Plain word outside any [keyword] or rich-text span.</summary>
            public bool Eligible;
            /// <summary>The separator before this token (" " or "\n"); unused for the first.</summary>
            public string Separator;
        }

        /// <summary>Splits on spaces and newlines, tracking [bracket] and &lt;color&gt;/&lt;link&gt; spans across words.</summary>
        internal static List<Token> Tokenize(string text)
        {
            var list = new List<Token>();
            int bracket = 0, span = 0;
            int start = 0;
            string sep = "";
            for (int i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != ' ' && text[i] != '\n') continue;
                string tok = text.Substring(start, i - start);
                bool inside = bracket > 0 || span > 0;
                Scan(tok, ref bracket, ref span, out bool marked);
                list.Add(new Token { Text = tok, Eligible = !inside && !marked && bracket == 0 && span == 0, Separator = sep });
                if (i < text.Length) sep = text[i] == '\n' ? "\n" : " ";
                start = i + 1;
            }
            return list;
        }

        private static void Scan(string tok, ref int bracket, ref int span, out bool marked)
        {
            marked = false;
            for (int i = 0; i < tok.Length; i++)
            {
                char c = tok[i];
                if (c == '[') { bracket++; marked = true; }
                else if (c == ']') { bracket = Math.Max(0, bracket - 1); marked = true; }
                else if (c == '<')
                {
                    marked = true;
                    int close = tok.IndexOf('>', i);
                    string tag = close > i ? tok.Substring(i + 1, close - i - 1) : "";
                    if (tag.StartsWith("color", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("link", StringComparison.OrdinalIgnoreCase)
                        || tag.StartsWith("b", StringComparison.OrdinalIgnoreCase) && tag.Length == 1) span++;
                    else if (tag.StartsWith("/color", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("/link", StringComparison.OrdinalIgnoreCase)
                             || tag == "/b") span = Math.Max(0, span - 1);
                    if (close > i) i = close;
                }
            }
        }

        /// <summary>Break points of a token in token coordinates: its leading/trailing punctuation is peeled off first.
        /// <paramref name="anyCase"/> lets a capitalised word break too (only an overlong word that must break anyway).</summary>
        internal static List<int> WordPoints(string token, bool anyCase)
        {
            var res = new List<int>();
            if (string.IsNullOrEmpty(token)) return res;
            int a = 0, b = token.Length;
            while (a < b && !char.IsLetterOrDigit(token[a])) a++;
            while (b > a && !char.IsLetterOrDigit(token[b - 1])) b--;
            if (b - a < MinWord) return res;
            string core = token.Substring(a, b - a);
            foreach (char c in core)
                if (c < 'a' || c > 'z')
                {
                    if (!anyCase || !(c >= 'A' && c <= 'Z')) return res;
                }
            foreach (int p in Points(core)) res.Add(a + p);
            return res;
        }
    }

    /// <summary>
    /// GG-T305: the line breaker behind every wrapped label. Greedy like the text engine, then: an overlong word is
    /// hyphenated (or, with no break point, split) instead of being cut mid-glyph; with <c>hyphenate</c> a word that would
    /// leave a ragged line (more than <see cref="RaggedFraction"/> of the width empty) is hyphenated into the gap; a
    /// single-letter word ("a", "I") never ends a line; and a lone word is never left on the last line when the word
    /// before it can come down with it. <see cref="Result.Changed"/> is false when the engine's own greedy wrap would
    /// already draw exactly this, so callers can leave the text untouched.
    /// </summary>
    public static class LineBreaker
    {
        public const float RaggedFraction = 0.2f;

        public sealed class Result
        {
            public readonly List<string> Lines = new List<string>();
            public bool Changed;
        }

        private sealed class Line
        {
            public readonly List<string> Words = new List<string>();
            public bool Hyphen;
            public string Text => string.Join(" ", Words) + (Hyphen ? "-" : "");
        }

        public static Result Break(string text, float width, Func<string, float> measure, bool hyphenate)
        {
            var r = new Result();
            if (text == null) return r;
            foreach (var para in text.Split('\n')) Paragraph(para, width, measure, hyphenate, r);
            return r;
        }

        private static void Paragraph(string para, float width, Func<string, float> measure, bool hyphenate, Result r)
        {
            bool Fits(string s) => measure(s) <= width + 0.01f;
            var tokens = Hyphenation.Tokenize(para);
            var lines = new List<Line>();
            var cur = new Line();
            for (int i = 0; i < tokens.Count; i++)
            {
                string w = tokens[i].Text;
                bool ok = tokens[i].Eligible;
                while (true)
                {
                    if (cur.Words.Count == 0 ? Fits(w) : Fits(cur.Text + " " + w)) { cur.Words.Add(w); break; }
                    if (cur.Words.Count > 0)
                    {
                        // The word doesn't fit: hyphenate it into a ragged gap, or start a new line with it.
                        if (hyphenate && ok && width - measure(cur.Text) > width * RaggedFraction)
                        {
                            string head = cur.Text;
                            int at = BestSplit(w, false, frag => Fits(head + " " + frag + "-"));
                            if (at > 0)
                            {
                                cur.Words.Add(w.Substring(0, at));
                                cur.Hyphen = true;
                                lines.Add(cur);
                                cur = new Line();
                                w = w.Substring(at);
                                r.Changed = true;
                                continue;
                            }
                        }
                        lines.Add(cur);
                        cur = new Line();
                        continue;
                    }
                    // Alone on its line and still too wide: break it rather than let the engine cut it mid-glyph.
                    if (w.Length <= 1 || w.IndexOf('<') >= 0) { cur.Words.Add(w); break; }
                    int cut = BestSplit(w, true, frag => Fits(frag + "-"));
                    bool hyphen = true;
                    if (cut <= 0)
                    {
                        int dash = w.LastIndexOf('-', w.Length - 2);
                        while (dash > 0 && !Fits(w.Substring(0, dash + 1))) dash = w.LastIndexOf('-', dash - 1);
                        if (dash > 0) { cut = dash + 1; hyphen = false; }
                        else
                        {
                            cut = 1;
                            for (int k = w.Length - 1; k > 1; k--) if (Fits(w.Substring(0, k) + "-")) { cut = k; break; }
                        }
                    }
                    if (cut >= w.Length) { cur.Words.Add(w); break; }
                    cur.Words.Add(w.Substring(0, cut));
                    cur.Hyphen = hyphen;
                    lines.Add(cur);
                    cur = new Line();
                    w = w.Substring(cut);
                    r.Changed = true;
                }
            }
            lines.Add(cur);

            if (lines.Count >= 2)
            {
                // A hyphenated fragment alone on the last line: take the whole word down instead.
                var last = lines[lines.Count - 1];
                var prev = lines[lines.Count - 2];
                if (last.Words.Count == 1 && prev.Hyphen)
                {
                    string whole = prev.Words[prev.Words.Count - 1] + last.Words[0];
                    if (prev.Words.Count >= 2 && Fits(whole))
                    {
                        prev.Words.RemoveAt(prev.Words.Count - 1);
                        prev.Hyphen = false;
                        last.Words[0] = whole;
                    }
                }
                // A single-letter word never ends a line ("a" / "I" goes down with the word it belongs to).
                for (int j = 0; j < lines.Count - 1; j++)
                {
                    var l = lines[j];
                    if (l.Hyphen || l.Words.Count < 2 || !SingleLetter(l.Words[l.Words.Count - 1])) continue;
                    string moved = l.Words[l.Words.Count - 1] + " " + lines[j + 1].Text;
                    if (!Fits(moved)) continue;
                    lines[j + 1].Words.Insert(0, l.Words[l.Words.Count - 1]);
                    l.Words.RemoveAt(l.Words.Count - 1);
                    r.Changed = true;
                }
                // No lone word on the last line when the word before it can come down too.
                last = lines[lines.Count - 1];
                prev = lines[lines.Count - 2];
                if (last.Words.Count == 1 && last.Words[0].Length > 0 && !prev.Hyphen && prev.Words.Count >= 2
                    && Letters(prev.Words, prev.Words.Count - 1) >= 2) // never strand a lone "—" or "a" above it instead
                {
                    string moved = prev.Words[prev.Words.Count - 1] + " " + last.Words[0];
                    if (Fits(moved))
                    {
                        last.Words.Insert(0, prev.Words[prev.Words.Count - 1]);
                        prev.Words.RemoveAt(prev.Words.Count - 1);
                        r.Changed = true;
                    }
                }
            }
            foreach (var l in lines) r.Lines.Add(l.Text);
        }

        /// <summary>A word that must not end a line: a single letter ("a", "I"), "&amp;" or a dash.</summary>
        private static bool SingleLetter(string w) => w.Length == 1 && (char.IsLetter(w[0]) || w[0] == '&' || w[0] == '—' || w[0] == '–');

        private static int Letters(List<string> words, int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++) foreach (char c in words[i]) if (char.IsLetterOrDigit(c)) n++;
            return n;
        }

        /// <summary>The largest break point of <paramref name="token"/> whose head passes <paramref name="fits"/>, or 0.</summary>
        private static int BestSplit(string token, bool anyCase, Func<string, bool> fits)
        {
            var pts = Hyphenation.WordPoints(token, anyCase);
            for (int i = pts.Count - 1; i >= 0; i--)
                if (fits(token.Substring(0, pts[i]))) return pts[i];
            return 0;
        }
    }
}
