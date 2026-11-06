using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GridGame.Core.Content
{
    /// <summary>What a glossary entry can point at for richer detail on its term card (GG-T300).</summary>
    public enum GlossaryLinkKind { None, Buff, Ability, Stat, Item }

    /// <summary>
    /// One glossary.json entry (GG-T300, owner: "anything in brackets is a tap tooltip"): a description marks a keyword
    /// as <c>[term]</c>; the term resolves (case-insensitive) to the entry whose <see cref="Id"/>, <see cref="Term"/> or
    /// one of its <see cref="Aliases"/> matches, and tapping it shows <see cref="Text"/>.
    /// </summary>
    public sealed class GlossaryEntry
    {
        public string Id;
        /// <summary>The headword shown on the term card.</summary>
        public string Term;
        /// <summary>Other spellings a description may bracket (plurals, verbs: "Burn" for Burning).</summary>
        public List<string> Aliases = new List<string>();
        /// <summary>The definition, one or two short sentences. May itself bracket other terms.</summary>
        public string Text;
        public GlossaryLinkKind LinkKind = GlossaryLinkKind.None;
        /// <summary>The buff/ability/item id, or the <see cref="StatGrantDef.Names"/> stat, <see cref="LinkKind"/> names.</summary>
        public string LinkId;
    }

    /// <summary>A piece of parsed text: plain text, or a bracketed term (brackets removed).</summary>
    public readonly struct GlossaryRun
    {
        public readonly string Text;
        public readonly bool IsTerm;
        public GlossaryRun(string text, bool isTerm) { Text = text; IsTerm = isTerm; }
        public override string ToString() => IsTerm ? "[" + Text + "]" : Text;
    }

    /// <summary>The bracket grammar, the chance bands and the glossary's validation (GG-T300). Pure C#.</summary>
    public static class Glossary
    {
        /// <summary>
        /// The one definition of the chance words (BIBLE §9): a description that doesn't print a number says how likely
        /// with one of these, and a number maps back to its word with <see cref="ChanceWord"/>. Upper bounds are
        /// inclusive percentages; each band's typical value is what glossary.json quotes.
        /// </summary>
        public static readonly (string Word, int Typical, int Max)[] ChanceBands =
        {
            ("slight", 10, 15),
            ("small", 20, 27),
            ("fair", 35, 42),
            ("good", 50, 62),
            ("likely", 75, 100),
        };

        /// <summary>The chance word for a 0..1 probability, bracketed ("[slight]"), or null for 0.</summary>
        public static string ChanceWord(float chance)
        {
            int pct = (int)Math.Round(chance * 100f);
            if (pct <= 0) return null;
            foreach (var b in ChanceBands) if (pct <= b.Max) return "[" + b.Word + "]";
            return "[" + ChanceBands[ChanceBands.Length - 1].Word + "]";
        }

        /// <summary>
        /// Splits <paramref name="text"/> into plain and term runs. A term is <c>[</c>, then at least one character that
        /// is neither bracket nor newline, then <c>]</c>; anything else (an unmatched or nested bracket, <c>[]</c>) stays
        /// literal, so "[a [b] c]" is "[a ", term "b", " c]" and "[[b]]" is "[", term "b", "]". Adjacent plain pieces merge.
        /// </summary>
        public static List<GlossaryRun> Parse(string text)
        {
            var runs = new List<GlossaryRun>();
            if (string.IsNullOrEmpty(text)) return runs;
            var plain = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '[')
                {
                    int j = i + 1;
                    while (j < text.Length && text[j] != ']' && text[j] != '[' && text[j] != '\n') j++;
                    if (j < text.Length && text[j] == ']' && j > i + 1 && text.Substring(i + 1, j - i - 1).Trim().Length > 0)
                    {
                        if (plain.Length > 0) { runs.Add(new GlossaryRun(plain.ToString(), false)); plain.Clear(); }
                        runs.Add(new GlossaryRun(text.Substring(i + 1, j - i - 1).Trim(), true));
                        i = j + 1;
                        continue;
                    }
                }
                plain.Append(c);
                i++;
            }
            if (plain.Length > 0) runs.Add(new GlossaryRun(plain.ToString(), false));
            return runs;
        }

        public static bool HasTerms(string text) => !string.IsNullOrEmpty(text) && text.IndexOf('[') >= 0 && Parse(text).Exists(r => r.IsTerm);

        /// <summary>Every bracketed term in <paramref name="text"/>, in order.</summary>
        public static IEnumerable<string> Terms(string text)
        {
            foreach (var r in Parse(text)) if (r.IsTerm) yield return r.Text;
        }

        /// <summary>The text without its brackets — what the player reads.</summary>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return text ?? "";
            var sb = new StringBuilder();
            foreach (var r in Parse(text)) sb.Append(r.Text);
            return sb.ToString();
        }

        private static readonly Regex Tag = new Regex(@"<(/?)([a-zA-Z]+)(=[^>]*)?>");

        /// <summary>
        /// Layout words for a wrapping row of labels: each inner list is one unbreakable word — its plain and term pieces
        /// in order ("[Counterstrike]." is a term then "."; "[healer][buffer]" is two terms) — and an empty list is a
        /// forced line break (a newline). Whitespace separates words and is dropped. Rich-text tags open in a plain piece
        /// are closed at the end of every word and reopened on the next plain piece, so each piece is balanced on its own.
        /// </summary>
        public static List<List<GlossaryRun>> Words(string text)
        {
            var words = new List<List<GlossaryRun>>();
            var open = new List<(string Name, string Tag)>();
            List<GlossaryRun> cur = null;
            var piece = new StringBuilder();

            void FlushPiece()
            {
                if (piece.Length == 0) return;
                string body = piece.ToString();
                piece.Clear();
                var prefix = new StringBuilder();
                foreach (var t in open) prefix.Append(t.Tag);
                foreach (Match m in Tag.Matches(body))
                {
                    if (m.Groups[1].Value == "/")
                    {
                        int k = open.FindLastIndex(t => t.Name.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase));
                        if (k >= 0) open.RemoveAt(k);
                    }
                    else open.Add((m.Groups[2].Value, m.Value));
                }
                var suffix = new StringBuilder();
                for (int k = open.Count - 1; k >= 0; k--) suffix.Append("</").Append(open[k].Name).Append('>');
                (cur ??= new List<GlossaryRun>()).Add(new GlossaryRun(prefix + body + suffix, false));
            }
            void EndWord()
            {
                FlushPiece();
                if (cur != null && cur.Count > 0) words.Add(cur);
                cur = null;
            }

            foreach (var r in Parse(text))
            {
                if (r.IsTerm)
                {
                    FlushPiece();
                    (cur ??= new List<GlossaryRun>()).Add(r);
                    continue;
                }
                foreach (char c in r.Text)
                {
                    if (c == '\n') { EndWord(); words.Add(new List<GlossaryRun>()); }
                    else if (char.IsWhiteSpace(c)) EndWord();
                    else piece.Append(c);
                }
            }
            EndWord();
            return words;
        }

        /// <summary>Every key an entry answers to: its Id, Term and Aliases.</summary>
        public static IEnumerable<string> Keys(GlossaryEntry e)
        {
            if (!string.IsNullOrWhiteSpace(e.Id)) yield return e.Id.Trim();
            if (!string.IsNullOrWhiteSpace(e.Term)) yield return e.Term.Trim();
            if (e.Aliases != null) foreach (var a in e.Aliases) if (!string.IsNullOrWhiteSpace(a)) yield return a.Trim();
        }

        /// <summary>Builds <see cref="ContentDb.GlossaryIndex"/> (first entry wins a clashing key; Validate reports it).</summary>
        public static void Index(ContentDb db)
        {
            db.GlossaryIndex.Clear();
            foreach (var e in db.GlossaryOrder)
                foreach (var k in Keys(e))
                    if (!db.GlossaryIndex.ContainsKey(k)) db.GlossaryIndex[k] = e;
        }

        /// <summary>
        /// glossary.json is well formed, every link resolves, every buff name has an entry, and every [term] in a
        /// player-facing description (abilities, items incl. relic passives, classes, skill trees, the glossary itself)
        /// resolves to an entry or alias. Text with no brackets is always fine.
        /// </summary>
        public static void Validate(ContentDb db, List<string> p)
        {
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in db.GlossaryOrder)
            {
                string w = $"glossary {e.Id}";
                if (string.IsNullOrWhiteSpace(e.Id)) { p.Add("glossary: entry with empty Id"); continue; }
                if (string.IsNullOrWhiteSpace(e.Term)) p.Add($"{w}: empty Term");
                if (string.IsNullOrWhiteSpace(e.Text)) p.Add($"{w}: empty Text");
                foreach (var k in Keys(e))
                {
                    if (k.IndexOf('[') >= 0 || k.IndexOf(']') >= 0) p.Add($"{w}: key '{k}' has a bracket");
                    if (owner.TryGetValue(k, out var other) && !string.Equals(other, e.Id, StringComparison.OrdinalIgnoreCase))
                        p.Add($"{w}: '{k}' is already a key of glossary {other}");
                    else owner[k] = e.Id;
                }
                switch (e.LinkKind)
                {
                    case GlossaryLinkKind.Buff: if (db.TryBuff(e.LinkId) == null) p.Add($"{w}: unknown buff '{e.LinkId}'"); break;
                    case GlossaryLinkKind.Ability: if (db.TryAbility(e.LinkId) == null) p.Add($"{w}: unknown ability '{e.LinkId}'"); break;
                    case GlossaryLinkKind.Item: if (db.TryItem(e.LinkId) == null) p.Add($"{w}: unknown item '{e.LinkId}'"); break;
                    case GlossaryLinkKind.Stat: if (!StatGrantDef.IsStatName(e.LinkId)) p.Add($"{w}: unknown stat '{e.LinkId}'"); break;
                    default: if (e.LinkId != null) p.Add($"{w}: LinkId without a LinkKind"); break;
                }
                Need(db, e.Text, w, p);
            }
            // A content set with a glossary covers every status by name (fixtures without one are exempt).
            if (db.GlossaryOrder.Count > 0)
                foreach (var b in db.Buffs.Values)
                    if (!string.IsNullOrEmpty(b.Name) && db.TryTerm(b.Name) == null) p.Add($"buff {b.Id}: no glossary entry for '{b.Name}'");

            foreach (var a in db.Abilities.Values) Need(db, a.Description, $"ability {a.Id}", p);
            foreach (var i in db.Items.Values)
            {
                Need(db, i.Description, $"item {i.Id}", p);
                Need(db, i.PassiveDescription, $"item {i.Id} passive", p);
            }
            foreach (var c in db.Classes.Values)
            {
                Need(db, c.Description, $"class {c.Id}", p);
                Need(db, c.Lore, $"class {c.Id} lore", p);
                Need(db, c.Card, $"class {c.Id} card", p);
            }
            foreach (var t in db.SkillTrees.Values)
            {
                Need(db, t.Description, $"skilltree {t.Id}", p);
                foreach (var n in t.Nodes) if (n != null) Need(db, n.Description, $"skilltree {t.Id} node {n.Id}", p);
            }
        }

        private static void Need(ContentDb db, string text, string where, List<string> p)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return;
            foreach (var t in Terms(text))
                if (db.TryTerm(t) == null) p.Add($"{where}: unknown glossary term [{t}]");
        }
    }
}
