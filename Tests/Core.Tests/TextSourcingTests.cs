using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>GG-T355 Phase 2 (BIBLE §27.2, docs/PROSE.md): the game handles gameplay, Prose handles narrative. Every
    /// story string comes from Content/Text (Prose-applied, or the one-time seed); the gameplay JSON holds none; the
    /// loader's join resolves every key, shows a visible placeholder for a missing one without crashing, exposes each
    /// text's Prose id, GUID and version, and keeps the conversations' right-answer tags on the answers the game rewards.</summary>
    public class TextSourcingTests
    {
        private static ContentDb Real => TestKit.Real;

        private static string Read(string name)
        {
            var p = Path.Combine(TestKit.ContentDir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        private static bool IsPlaceholder(string s) => s != null && s.StartsWith("(missing text: ", StringComparison.Ordinal);

        [Test]
        public void Every_story_key_resolves_to_a_text()
        {
            var db = Real;
            Assert.That(db.TextJoined, Is.True, "Content/Text was loaded and joined");
            var keys = ProseText.Keys(db);
            Assert.That(keys.Count, Is.GreaterThan(1900), "every record names its key (1976 at the seed)");
            var missing = keys.Keys.Where(k => db.TryText(k) == null || db.TryText(k).Missing).ToList();
            Assert.That(missing, Is.Empty, string.Join("\n", missing.Take(40)));
            Assert.That(db.TextProblems, Is.Empty, string.Join("\n", db.TextProblems.Take(40)));
            var problems = ContentLoader.Validate(db).Where(p => p.StartsWith("text") || p.Contains("story text in gameplay JSON")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(40)));
        }

        [Test]
        public void Consumers_read_the_joined_words_with_no_placeholder()
        {
            var db = Real;
            var bad = new List<string>();
            void Need(string s, string where) { if (string.IsNullOrWhiteSpace(s) || IsPlaceholder(s)) bad.Add(where); }
            foreach (var c in db.Classes.Values) { Need(c.Description, $"class {c.Id} Description"); Need(c.Lore, $"class {c.Id} Lore"); }
            foreach (var c in db.Classes.Values) foreach (var ph in c.BossPhases) Need(ph.Announce, $"class {c.Id} boss phase");
            foreach (var i in db.Items.Values) Need(i.Description, $"item {i.Id}");
            foreach (var a in db.ArmorSetOrder) Need(a.Description, $"armor set {a.Id}");
            foreach (var t in db.SkillTrees.Values) { Need(t.Description, $"tree {t.Id}"); foreach (var n in t.Nodes) Need(n.Description, $"node {n.Id}"); }
            foreach (var d in db.DialogueOrder)
            {
                foreach (var n in d.Nodes) { Need(n.Text, $"dialogue {d.Id} node {n.Id}"); foreach (var ch in n.Choices) Need(ch.Label, $"dialogue {d.Id} node {n.Id} choice"); }
                if (d.HasCrawl) Need(d.Crawl, $"dialogue {d.Id} crawl");
            }
            Need(db.Campaign.Prologue?.Crawl, "prologue"); Need(db.Campaign.Ending?.Crawl, "ending");
            foreach (var t in db.Campaign.Themes) Need(t.Crawl, $"theme {t.Id}");
            foreach (var q in db.QuestOrder) { Need(q.Summary, $"quest {q.Id} brief"); Need(q.TurnIn, $"quest {q.Id} turn-in"); }
            foreach (var g in db.GlossaryOrder) Need(g.Text, $"glossary {g.Id}");
            foreach (var u in db.Caravan.Values) foreach (var b in u.Barks) Need(b, $"caravan {u.Id}");
            foreach (var m in db.Mercenaries) { Need(m.Pitch, $"merc {m.Class} pitch"); Need(m.Renewal, $"merc {m.Class} renewal"); }
            foreach (var s in db.StageOrder) foreach (var w in s.Waves) if (w.SurpriseSpeaker != null) Need(w.SurpriseLine, $"stage {s.Id} surprise");
            foreach (var npc in DialogueDef.Npcs) Need(db.NpcBlurbs.TryGetValue(npc, out var b) ? b : null, $"npc {npc}");
            Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(40)));
            Assert.That(db.Dialogues.Values.Count(d => d.HasCrawl), Is.GreaterThan(0), "the level-10 interlude keeps its crawl marker");
        }

        [Test]
        public void No_story_text_is_left_in_gameplay_json()
        {
            var found = new List<string>();
            foreach (var kv in ProseText.NarrativeFields)
            {
                var raw = Read(kv.Key);
                Assert.That(raw, Is.Not.Null, kv.Key + ".json");
                var root = JToken.Parse(raw);
                foreach (var path in kv.Value) Walk(root, path.Split('/'), 0, kv.Key + ".json", found);
            }
            Assert.That(found, Is.Empty, "story text belongs in Prose (Content/Text), not gameplay JSON:\n" + string.Join("\n", found.Take(40)));
        }

        private static void Walk(JToken node, string[] steps, int i, string at, List<string> found)
        {
            if (node == null) return;
            if (i == steps.Length) { if (node.Type != JTokenType.Null) found.Add(at); return; }
            if (steps[i] == "*") { if (node is JArray a) for (int k = 0; k < a.Count; k++) Walk(a[k], steps, i + 1, $"{at}/{k}", found); }
            else if (node is JObject o && o.TryGetValue(steps[i], out var child)) Walk(child, steps, i + 1, $"{at}/{steps[i]}", found);
        }

        [Test]
        public void Right_answer_tags_match_the_wanderer_and_quiz_logic()
        {
            var texts = (JObject)JObject.Parse(Read("Text/conversations"))["texts"];
            int wanderers = 0, quizzes = 0;
            foreach (var d in Real.DialogueOrder)
            {
                var t = texts["dialogue:" + d.Id] as JObject;
                Assert.That(t, Is.Not.Null, $"{d.Id} has a conversation text");
                var nodes = ((JArray)t["payload"]["nodes"]).OfType<JObject>().ToDictionary(n => (string)n["id"], StringComparer.OrdinalIgnoreCase);
                List<int> Tagged(DialogueNode n) =>
                    ((nodes[n.Id]["choices"] as JArray) ?? new JArray()).Select((c, i) => (c, i))
                        .Where(x => (x.c["tags"] as JArray)?.Any(tag => (string)tag == ProseText.RightAnswerTag) == true).Select(x => x.i).ToList();
                foreach (var n in d.Nodes.Where(n => n.Choices.Count > 0))
                {
                    var rewarded = n.Choices.Select((c, i) => (c, i)).Where(x => ProseText.RewardsRightAnswer(x.c)).Select(x => x.i).ToList();
                    Assert.That(Tagged(n), Is.EqualTo(rewarded), $"{d.Id} node {n.Id}: right-answer tags");
                }
                if (d.Trigger.Kind == DialogueTriggerKind.Wanderer)
                {
                    wanderers++;
                    var ask = d.Nodes.First(n => n.Choices.Count > 0);
                    Assert.That(Tagged(ask), Is.EqualTo(new List<int> { Wanderers.RightAnswer(d) }), $"{d.Id}: the tag marks Wanderers.RightAnswer");
                }
                if (d.AllEffects().Any(e => e.Type == DialogueEffectType.GrantSkillPoint)
                    && d.Nodes.Any(n => n.Choices.Any(c => c.Effects.Any(e => e.Type == DialogueEffectType.GrantSkillPoint))))
                    quizzes++;
            }
            Assert.That(wanderers, Is.GreaterThan(0), "wanderer talks are checked");
            Assert.That(quizzes, Is.GreaterThan(0), "Advice quizzes are checked");
        }

        [Test]
        public void Resolved_texts_expose_their_Prose_id_guid_and_version()
        {
            var t = Real.TryText("crawl:prologue");
            Assert.That(t, Is.Not.Null);
            Assert.That(t.Missing, Is.False);
            Assert.That(t.Kind, Is.EqualTo("crawl"));
            Assert.That(t.Id, Is.EqualTo("crawl-prologue"));
            if (t.Source == ProseText.Seed) { Assert.That(t.Version, Is.EqualTo(0)); Assert.That(t.Guid, Is.Null, "a seed was never in Prose's hands"); }
            else { Assert.That(t.Source, Is.EqualTo("prose")); Assert.That(t.Version, Is.GreaterThanOrEqualTo(1)); }
            Assert.That(Real.TryText("class:" + Real.Classes.Values.First().Id)?.Id, Does.StartWith("description-class-"));
            Assert.That(Real.Texts.Values.All(x => x.Hash != null && x.Hash.StartsWith("sha256:")), Is.True, "every text carries its hash");
        }

        // ── the join under damage ──────────────────────────────────────────────

        private static Func<string, string> Patched(string file, Func<string, string> patch) =>
            name => name == file ? patch(Read(name)) : Read(name);

        [Test]
        public void A_missing_key_shows_a_visible_placeholder_and_is_reported_never_a_crash()
        {
            var cls = Real.Classes.Values.First();
            string key = "class:" + cls.Id;
            ContentDb db = null;
            Assert.DoesNotThrow(() => db = ContentLoader.Load(Patched("Text/descriptions", raw =>
            {
                var doc = JObject.Parse(raw);
                ((JObject)doc["texts"]).Remove(key);
                return doc.ToString();
            })));
            Assert.That(db.Classes[cls.Id].Description, Is.EqualTo(ProseText.Placeholder(key)));
            Assert.That(db.TryText(key).Missing, Is.True);
            Assert.That(ContentLoader.Validate(db), Has.Some.Contains(key).And.Some.Contains("placeholder"));
        }

        [Test]
        public void A_conversation_missing_a_node_keeps_the_rest_and_marks_the_hole()
        {
            var d = Real.DialogueOrder.First(x => x.Nodes.Count > 1);
            string key = "dialogue:" + d.Id, gone = d.Nodes[1].Id;
            var db = ContentLoader.Load(Patched("Text/conversations", raw =>
            {
                var doc = JObject.Parse(raw);
                var nodes = (JArray)doc["texts"][key]["payload"]["nodes"];
                nodes.Where(n => (string)n["id"] == gone).ToList().ForEach(n => n.Remove());
                return doc.ToString();
            }));
            var def = db.Dialogues[d.Id];
            Assert.That(def.Nodes[0].Text, Is.EqualTo(Real.Dialogues[d.Id].Nodes[0].Text));
            Assert.That(IsPlaceholder(def.Nodes[1].Text), Is.True);
            Assert.That(ContentLoader.Validate(db), Has.Some.Contains($"no node '{gone}'"));
        }

        [Test]
        public void Story_text_put_back_into_gameplay_json_is_reported_and_Prose_wins()
        {
            var first = Real.GlossaryOrder[0];
            var db = ContentLoader.Load(Patched("glossary", raw =>
            {
                var arr = JArray.Parse(raw);
                arr[0]["Text"] = "Written in the game by hand.";
                return arr.ToString();
            }));
            Assert.That(db.GlossaryOrder[0].Text, Is.EqualTo(first.Text), "the Prose text replaces it");
            Assert.That(ContentLoader.Validate(db), Has.Some.Contains("story text in gameplay JSON"));
        }

        [Test]
        public void An_orphan_text_and_a_moved_right_answer_are_reported()
        {
            var wanderer = Real.DialogueOrder.First(d => d.Trigger.Kind == DialogueTriggerKind.Wanderer);
            var db = ContentLoader.Load(name =>
            {
                var raw = Read(name);
                if (name == "Text/descriptions")
                {
                    var doc = JObject.Parse(raw);
                    var any = (JObject)((JObject)doc["texts"]).Properties().First().Value.DeepClone();
                    any["key"] = "class:no_such_class";
                    doc["texts"]["class:no_such_class"] = any;
                    return doc.ToString();
                }
                if (name == "Text/conversations")
                {
                    var doc = JObject.Parse(raw);
                    foreach (var n in (JArray)doc["texts"]["dialogue:" + wanderer.Id]["payload"]["nodes"])
                        foreach (var c in (n["choices"] as JArray) ?? new JArray()) ((JObject)c).Remove("tags");
                    return doc.ToString();
                }
                return raw;
            });
            var p = ContentLoader.Validate(db);
            Assert.That(p, Has.Some.Contains("class:no_such_class").And.Some.Contains("orphan"));
            Assert.That(p, Has.Some.Contains("dialogue:" + wanderer.Id).And.Some.Contains("right-answer"));
        }

        [Test]
        public void A_fixture_without_Content_Text_is_not_joined()
        {
            var db = ContentLoader.Load(name => name.StartsWith("Text/") ? null : Read(name));
            Assert.That(db.TextJoined, Is.False);
            Assert.That(db.TextProblems, Is.Empty, "hand-built fixtures keep the words they were given");
            Assert.That(db.Texts, Is.Empty);
        }
    }
}
