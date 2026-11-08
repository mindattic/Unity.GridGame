using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridGame.Core.Content
{
    /// <summary>
    /// GG-T355 (BIBLE §27.2, docs/PROSE.md): the game handles gameplay and Prose handles narrative. Every story string a
    /// player reads (dialogue lines and choices, crawls, class/item/armor-set/skill-tree/NPC descriptions, quest briefs
    /// and turn-ins, glossary definitions, barks) lives in <c>Resources/Content/Text/&lt;kind&gt;.json</c>, written by
    /// tools/prose_apply.py from published Prose texts (or the one-time seed, tools/prose_text.py). The gameplay JSON keeps
    /// only ids; <see cref="Join"/> resolves each record's key and fills the same model fields consumers always read.
    /// </summary>
    public static class ProseText
    {
        public const string Schema = "gridgame-text/1";
        public const string Seed = "seed";

        /// <summary>Kind → file under Content/Text (tools/prose_apply.py FILES).</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Files = new[]
        {
            new KeyValuePair<string, string>("conversation", "conversations"),
            new KeyValuePair<string, string>("crawl", "crawls"),
            new KeyValuePair<string, string>("description", "descriptions"),
            new KeyValuePair<string, string>("quest", "quests"),
            new KeyValuePair<string, string>("bark", "barks"),
            new KeyValuePair<string, string>("glossary", "glossary"),
        };

        /// <summary>The narrative fields that may no longer appear in gameplay JSON (file → paths, "*" = every element),
        /// mirrored by tools/prose_text.py NARRATIVE and enforced by TextSourcingTests.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> NarrativeFields = new Dictionary<string, string[]>
        {
            ["classes"] = new[] { "*/Description", "*/Lore", "*/BossPhases/*/Announce" },
            ["dialogues"] = new[] { "*/Crawl", "*/Nodes/*/Text", "*/Nodes/*/Choices/*/Label" },
            ["campaign"] = new[] { "Prologue/Crawl", "Ending/Crawl", "Themes/*/Crawl" },
            ["quests"] = new[] { "*/Summary", "*/TurnIn" },
            ["glossary"] = new[] { "*/Text" },
            ["items"] = new[] { "*/Description" },
            ["armorsets"] = new[] { "*/Description" },
            ["skilltrees"] = new[] { "*/Description", "*/Nodes/*/Description" },
            ["caravan"] = new[] { "*/Barks" },
            ["mercenaries"] = new[] { "*/Pitch", "*/Renewal" },
            ["stages"] = new[] { "*/Waves/*/SurpriseLine" },
            ["regionrules"] = new[] { "*/Name", "*/Description" },
        };

        /// <summary>The choice tag a conversation puts on the answer the game's logic rewards: a wanderer's "heard" reply
        /// (<c>Wanderers.RightAnswer</c>) or the Advice-quiz answer that grants a skill point.</summary>
        public const string RightAnswerTag = "right-answer";

        /// <summary>What a key with no text shows: visible on screen, never a crash (the problem is reported too).</summary>
        public static string Placeholder(string key) => "(missing text: " + key + ")";

        private static readonly JsonSerializerSettings Lenient = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore, // Prose may add fields; the game reads what it knows
            NullValueHandling = NullValueHandling.Include,
        };

#pragma warning disable CS0649 // filled by Json.NET
        private sealed class TextFile
        {
            public string Schema;
            public string Kind;
            public Dictionary<string, TextEntry> Texts = new Dictionary<string, TextEntry>();
        }

        private sealed class TextEntry
        {
            public string Key;
            public string Id;
            public string Guid;
            public int Version;
            public string Source;
            public string Hash;
            public JObject Payload;
        }
#pragma warning restore CS0649

        /// <summary>Every text key the gameplay data defines → its kind (tools/prose_keys.py game_keys): a key exists
        /// exactly when the record it names does.</summary>
        public static Dictionary<string, string> Keys(ContentDb db)
        {
            var k = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in db.DialogueOrder)
            {
                k["dialogue:" + d.Id] = "conversation";
                if (d.HasCrawl) k["crawl:dialogue/" + d.Id] = "crawl";
            }
            if (db.Campaign.Prologue != null) k["crawl:prologue"] = "crawl";
            if (db.Campaign.Ending != null) k["crawl:ending"] = "crawl";
            foreach (var t in db.Campaign.Themes) k["crawl:" + t.Id] = "crawl";
            foreach (var c in db.Classes.Values)
            {
                k["class:" + c.Id] = "description";
                for (int n = 0; n < c.BossPhases.Count; n++) k[$"bark:bossphase/{c.Id}/{n}"] = "bark";
            }
            foreach (var i in db.Items.Values) k["item:" + i.Id] = "description";
            foreach (var a in db.ArmorSetOrder) k["armorset:" + a.Id] = "description";
            foreach (var t in db.SkillTrees.Values)
            {
                k["skilltree:" + t.Id] = "description";
                foreach (var n in t.Nodes) k["skillnode:" + n.Id] = "description";
            }
            foreach (var npc in DialogueDef.Npcs) k["npc:" + npc] = "description";
            foreach (var r in db.RegionRuleOrder) k["rule:" + r.Id] = "description"; // GG-T359: payload name + short
            foreach (var s in db.StageOrder)
                for (int w = 0; w < s.Waves.Count; w++)
                    if (!string.IsNullOrEmpty(s.Waves[w].SurpriseSpeaker)) k[$"bark:surprise/{s.Id}/{w}"] = "bark";
            foreach (var q in db.QuestOrder) k["quest:" + q.Id] = "quest";
            foreach (var g in db.GlossaryOrder) k["glossary:" + g.Id] = "glossary";
            foreach (var u in db.Caravan.Values) if (!string.IsNullOrEmpty(u.Crew)) k["bark:caravan/" + u.Id] = "bark";
            foreach (var m in db.Mercenaries)
            {
                k[$"bark:merc/{m.Class}/pitch"] = "bark";
                k[$"bark:merc/{m.Class}/renewal"] = "bark";
            }
            return k;
        }

        /// <summary>Reads Content/Text and fills every narrative field from it. With no text file at all (a hand-built test
        /// fixture) nothing is joined and the records keep whatever they were given. Never throws on a missing or broken
        /// text: the field shows <see cref="Placeholder"/> and <see cref="ContentDb.TextProblems"/> says why.</summary>
        public static void Join(ContentDb db, Func<string, string> readText)
        {
            var texts = new Dictionary<string, TextEntry>(StringComparer.Ordinal);
            var kindOf = new Dictionary<string, string>(StringComparer.Ordinal);
            bool any = false;
            foreach (var f in Files)
            {
                var raw = readText("Text/" + f.Value);
                if (string.IsNullOrWhiteSpace(raw)) { db.TextProblems.Add($"text: Content/Text/{f.Value}.json is missing"); continue; }
                any = true;
                TextFile file;
                try { file = JsonConvert.DeserializeObject<TextFile>(raw, Lenient); }
                catch (Exception e) { db.TextProblems.Add($"text: Content/Text/{f.Value}.json is unreadable ({e.Message})"); continue; }
                if (file == null) continue;
                if (file.Schema != Schema) db.TextProblems.Add($"text: Content/Text/{f.Value}.json schema '{file.Schema}' is not {Schema}");
                foreach (var kv in file.Texts ?? new Dictionary<string, TextEntry>())
                {
                    if (kv.Value == null) continue;
                    texts[kv.Key] = kv.Value;
                    kindOf[kv.Key] = f.Key;
                }
            }
            if (!any) { db.TextProblems.Clear(); return; }
            db.TextJoined = true;

            var keys = Keys(db);
            foreach (var kv in texts)
            {
                if (!keys.TryGetValue(kv.Key, out var kind)) db.TextProblems.Add($"text {kv.Key}: no gameplay record uses it (an orphan; retire it in Prose)");
                else if (kind != kindOf[kv.Key]) db.TextProblems.Add($"text {kv.Key}: is a {kind} key but sits in the {kindOf[kv.Key]} file");
            }

            JObject Get(string key, string kind)
            {
                texts.TryGetValue(key, out var e);
                if (e != null && kindOf[key] != kind) e = null;
                db.Texts[key] = new TextRef
                {
                    Key = key, Kind = kind, Id = e?.Id, Guid = e?.Guid, Version = e?.Version ?? 0, Source = e == null ? null : e.Source ?? "prose",
                    Hash = e?.Hash, Missing = e == null,
                };
                if (e == null) db.TextProblems.Add($"text {key}: no {kind} text in Content/Text (shows a placeholder; draft it in Prose)");
                return e == null ? null : e.Payload ?? new JObject();
            }
            string Str(JObject p, string field) => p?[field]?.Type == JTokenType.String ? (string)p[field] : null;
            string Need(JObject p, string field, string key)
            {
                var s = Str(p, field);
                if (s != null) return s;
                if (p != null) db.TextProblems.Add($"text {key}: payload has no '{field}'");
                return Placeholder(key);
            }
            List<string> Lines(JObject p, string key)
            {
                var list = new List<string>();
                if (p?["lines"] is JArray a) foreach (var x in a) if (x.Type == JTokenType.String) list.Add((string)x);
                if (list.Count == 0)
                {
                    if (p != null) db.TextProblems.Add($"text {key}: bark has no lines");
                    list.Add(Placeholder(key));
                }
                return list;
            }
            string Crawl(JObject p, string key)
            {
                if (p == null) return Placeholder(key);
                var paras = new List<string>();
                if (p["paragraphs"] is JArray a) foreach (var x in a) if (x.Type == JTokenType.String) paras.Add((string)x);
                if (paras.Count == 0) { db.TextProblems.Add($"text {key}: crawl has no paragraphs"); return Placeholder(key); }
                return string.Join("\n\n", paras);
            }
            void Leftover(object value, string where)
            {
                if (value != null) db.TextProblems.Add($"{where}: story text in gameplay JSON (it lives in Prose; tools/prose_text.py check)");
            }

            foreach (var c in db.Classes.Values)
            {
                string key = "class:" + c.Id;
                Leftover(c.Description, $"classes.json {c.Id} Description");
                Leftover(c.Lore, $"classes.json {c.Id} Lore");
                var p = Get(key, "description");
                c.Description = Need(p, "short", key);
                c.Lore = p == null ? Placeholder(key) : Str(p, "long") ?? c.Description;
                for (int n = 0; n < c.BossPhases.Count; n++)
                {
                    var ph = c.BossPhases[n];
                    string bk = $"bark:bossphase/{c.Id}/{n}";
                    Leftover(ph.Announce, $"classes.json {c.Id} BossPhases[{n}] Announce");
                    ph.Announce = Lines(Get(bk, "bark"), bk)[0];
                }
            }
            foreach (var d in db.DialogueOrder) JoinDialogue(db, d, Get, Placeholder, Leftover);
            void JoinCrawl(CrawlDef cd, string key, string where)
            {
                if (cd == null) return;
                Leftover(cd.Crawl, where);
                cd.Crawl = Crawl(Get(key, "crawl"), key);
            }
            JoinCrawl(db.Campaign.Prologue, "crawl:prologue", "campaign.json Prologue Crawl");
            JoinCrawl(db.Campaign.Ending, "crawl:ending", "campaign.json Ending Crawl");
            foreach (var t in db.Campaign.Themes)
            {
                Leftover(t.Crawl, $"campaign.json theme {t.Id} Crawl");
                t.Crawl = Crawl(Get("crawl:" + t.Id, "crawl"), "crawl:" + t.Id);
            }
            foreach (var q in db.QuestOrder)
            {
                string key = "quest:" + q.Id;
                Leftover(q.Summary, $"quests.json {q.Id} Summary");
                Leftover(q.TurnIn, $"quests.json {q.Id} TurnIn");
                var p = Get(key, "quest");
                q.Summary = Need(p, "brief", key);
                q.TurnIn = p == null ? Placeholder(key) : Str(p, "complete");
            }
            foreach (var g in db.GlossaryOrder)
            {
                string key = "glossary:" + g.Id;
                Leftover(g.Text, $"glossary.json {g.Id} Text");
                g.Text = Need(Get(key, "glossary"), "definition", key);
            }
            foreach (var i in db.Items.Values)
            {
                Leftover(i.Description, $"items.json {i.Id} Description");
                i.Description = Need(Get("item:" + i.Id, "description"), "short", "item:" + i.Id);
            }
            foreach (var a in db.ArmorSetOrder)
            {
                Leftover(a.Description, $"armorsets.json {a.Id} Description");
                a.Description = Need(Get("armorset:" + a.Id, "description"), "short", "armorset:" + a.Id);
            }
            foreach (var t in db.SkillTrees.Values)
            {
                Leftover(t.Description, $"skilltrees.json {t.Id} Description");
                t.Description = Need(Get("skilltree:" + t.Id, "description"), "short", "skilltree:" + t.Id);
                foreach (var n in t.Nodes)
                {
                    Leftover(n.Description, $"skilltrees.json {t.Id} node {n.Id} Description");
                    n.Description = Need(Get("skillnode:" + n.Id, "description"), "short", "skillnode:" + n.Id);
                }
            }
            foreach (var npc in DialogueDef.Npcs)
                db.NpcBlurbs[npc] = Need(Get("npc:" + npc, "description"), "short", "npc:" + npc);
            foreach (var r in db.RegionRuleOrder)
            {
                // GG-T359: a region rule's name and description are its Prose description ("name", "short").
                string key = "rule:" + r.Id;
                Leftover(r.Name, $"regionrules.json {r.Id} Name");
                Leftover(r.Description, $"regionrules.json {r.Id} Description");
                var p = Get(key, "description");
                r.Name = Need(p, "name", key);
                r.Description = Need(p, "short", key);
            }
            foreach (var s in db.StageOrder)
                for (int w = 0; w < s.Waves.Count; w++)
                {
                    var wave = s.Waves[w];
                    Leftover(wave.SurpriseLine, $"stages.json {s.Id} wave {w} SurpriseLine");
                    if (string.IsNullOrEmpty(wave.SurpriseSpeaker)) continue;
                    string key = $"bark:surprise/{s.Id}/{w}";
                    wave.SurpriseLine = Lines(Get(key, "bark"), key)[0];
                }
            foreach (var u in db.Caravan.Values)
            {
                if (u.Barks.Count > 0) Leftover(u.Barks, $"caravan.json {u.Id} Barks");
                if (string.IsNullOrEmpty(u.Crew)) continue;
                string key = "bark:caravan/" + u.Id;
                u.Barks = Lines(Get(key, "bark"), key);
            }
            foreach (var m in db.Mercenaries)
            {
                Leftover(m.Pitch, $"mercenaries.json {m.Class} Pitch");
                Leftover(m.Renewal, $"mercenaries.json {m.Class} Renewal");
                string pk = $"bark:merc/{m.Class}/pitch", rk = $"bark:merc/{m.Class}/renewal";
                m.Pitch = Lines(Get(pk, "bark"), pk)[0];
                m.Renewal = Lines(Get(rk, "bark"), rk)[0];
            }
        }

        /// <summary>A conversation joins by node id (lines) and choice order (choice texts). The words and the gameplay
        /// shape must agree: a node or choice Prose lacks shows a placeholder, a choice whose "next" differs is reported,
        /// and the <see cref="RightAnswerTag"/> must sit exactly on the choices the game's logic rewards.</summary>
        private static void JoinDialogue(ContentDb db, DialogueDef d, Func<string, string, JObject> get, Func<string, string> placeholder,
                                         Action<object, string> leftover)
        {
            string key = "dialogue:" + d.Id;
            var p = get(key, "conversation");
            var byId = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            if (p?["nodes"] is JArray nodes)
                foreach (var x in nodes)
                    if (x is JObject o && o["id"]?.Type == JTokenType.String) byId[(string)o["id"]] = o;
            foreach (var n in d.Nodes)
            {
                if (n == null) continue;
                string where = $"dialogues.json {d.Id} node {n.Id}";
                leftover(n.Text, where + " Text");
                byId.TryGetValue(n.Id ?? "", out var tn);
                if (p != null && tn == null) db.TextProblems.Add($"text {key}: no node '{n.Id}' (shows a placeholder)");
                var line = tn?["line"];
                n.Text = line?.Type == JTokenType.String ? (string)line : placeholder($"{key}#{n.Id}");
                var tc = tn?["choices"] as JArray;
                if (tn != null && n.Choices.Count != (tc?.Count ?? 0))
                    db.TextProblems.Add($"text {key} node {n.Id}: {tc?.Count ?? 0} choices in Prose, {n.Choices.Count} in the game");
                for (int i = 0; i < n.Choices.Count; i++)
                {
                    var ch = n.Choices[i];
                    if (ch == null) continue;
                    leftover(ch.Label, $"{where} choice {i + 1} Label");
                    var tch = tc != null && i < tc.Count ? tc[i] as JObject : null;
                    var text = tch?["text"];
                    ch.Label = text?.Type == JTokenType.String ? (string)text : placeholder($"{key}#{n.Id}/{i + 1}");
                    if (tch == null) continue;
                    var next = tch["next"]?.Type == JTokenType.String ? (string)tch["next"] : "";
                    if (!string.Equals(next, ch.Next ?? "", StringComparison.OrdinalIgnoreCase))
                        db.TextProblems.Add($"text {key} node {n.Id} choice {i + 1}: next '{next}' in Prose, '{ch.Next}' in the game");
                    bool tagged = false;
                    if (tch["tags"] is JArray tags) foreach (var t in tags) tagged |= t.Type == JTokenType.String && (string)t == RightAnswerTag;
                    if (tagged != RewardsRightAnswer(ch))
                        db.TextProblems.Add($"text {key} node {n.Id} choice {i + 1}: '{RightAnswerTag}' tag {(tagged ? "on an answer the game doesn't reward" : "missing from the answer the game rewards")}");
                }
            }
            if (d.HasCrawl)
            {
                leftover(d.Crawl, $"dialogues.json {d.Id} Crawl");
                string ck = "crawl:dialogue/" + d.Id;
                var cp = get(ck, "crawl");
                var paras = new List<string>();
                if (cp?["paragraphs"] is JArray a) foreach (var x in a) if (x.Type == JTokenType.String) paras.Add((string)x);
                d.Crawl = paras.Count > 0 ? string.Join("\n\n", paras) : placeholder(ck);
            }
            else leftover(d.Crawl, $"dialogues.json {d.Id} Crawl (and no HasCrawl marker)");
        }

        /// <summary>Whether the game's logic rewards this answer (what the <see cref="RightAnswerTag"/> must mark): the
        /// wanderer reply that leads to <see cref="DialogueDef.HeardNode"/>, or an answer that grants a skill point.</summary>
        public static bool RewardsRightAnswer(DialogueChoice ch)
        {
            if (string.Equals(ch.Next, DialogueDef.HeardNode, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var e in ch.Effects) if (e?.Type == DialogueEffectType.GrantSkillPoint) return true;
            return false;
        }
    }

    /// <summary>One story text as the game resolved it (GG-T355), for debugging and telemetry: the readable key, the
    /// Prose text id, its GUID (null until Prose exports it), the applied version (0 = the seed), and whether it was
    /// missing (the field then shows <see cref="ProseText.Placeholder"/>).</summary>
    public sealed class TextRef
    {
        public string Key;
        public string Kind;
        public string Id;
        public string Guid;
        public int Version;
        /// <summary>"prose" (applied from a published Prose text) or "seed" (the one-time snapshot, never approved).</summary>
        public string Source;
        public string Hash;
        public bool Missing;

        public override string ToString() => Missing ? $"{Key} (missing)" : $"{Key} {Id} v{Version} {Source}{(Guid != null ? " " + Guid : "")}";
    }
}
