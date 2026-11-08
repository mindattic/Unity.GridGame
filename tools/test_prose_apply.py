"""GG-T355: tests of tools/prose_apply.py, GridGame's Prose applier (docs/PROSE.md), end to end on a fixture, with no
Hub and no approval needed. Also pins the key scheme (tools/prose_keys.py) the applier and the seed share.

    python -m unittest tools/test_prose_apply.py        (from the repo root)

tools/fixtures/prose/: Content/ is a tiny slice of gameplay JSON (two classes, a theme, a wanderer talk, a glossary
term), pending/ holds prose-text/1 documents as `kit pull` writes them, before/ the text files as an earlier apply
left them (a glossary entry Prose has since retired), expected/ the exact files this apply must leave.
"""

import io
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import prose_apply  # noqa: E402
import prose_keys  # noqa: E402

FIXTURE = os.path.join(HERE, "fixtures", "prose")
CONTENT = os.path.join(FIXTURE, "Content")
PENDING = os.path.join(FIXTURE, "pending")
BEFORE = os.path.join(FIXTURE, "before")
EXPECTED = os.path.join(FIXTURE, "expected")


def tree(root):
    out = {}
    for d, _, files in os.walk(root):
        for f in files:
            p = os.path.join(d, f)
            with open(p, "rb") as fh:
                out[os.path.relpath(p, root).replace(os.sep, "/")] = fh.read()
    return out


class ApplierTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="gg-prose-apply-")
        self.out = os.path.join(self.tmp, "Text")
        shutil.copytree(BEFORE, self.out)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def apply(self, content=CONTENT, dry_run=False):
        err = io.StringIO()
        rows = prose_apply.apply_all(PENDING, content, self.out, dry_run, err=err)
        return {r["id"]: r for r in rows}, err.getvalue()

    def test_writes_exactly_the_expected_text_files(self):
        rows, err = self.apply()
        self.assertEqual(tree(self.out), tree(EXPECTED))
        self.assertIn("keys still on placeholder", err)
        for i in ("crawl-swamp", "crawl-prologue", "description-class-lich", "conversation-wanderer-ember"):
            self.assertEqual(rows[i]["result"], "applied", i)

    def test_reports_follow_the_kit_contract(self):
        rows, _ = self.apply()
        self.assertEqual(len(rows), len([f for f in os.listdir(PENDING) if f.endswith(".json")]))
        for r in rows.values():
            self.assertIn(r["result"], ("applied", "orphan", "skipped"))
            self.assertIsInstance(r["version"], int)
            self.assertTrue(r["hash"].startswith("sha256:"))
        self.assertEqual(rows["description-class-ghost"]["result"], "orphan")
        self.assertEqual(rows["crawl-lich-mislabeled"]["result"], "orphan")
        self.assertEqual(rows["book-letter"]["result"], "skipped")
        self.assertEqual(rows["quest-draft"]["result"], "skipped")

    def test_texts_are_keyed_by_key_and_carry_the_guid(self):
        self.apply()
        with open(os.path.join(self.out, "crawls.json"), encoding="utf-8") as f:
            crawls = json.load(f)
        self.assertEqual(crawls["schema"], "gridgame-text/1")
        swamp = crawls["texts"]["crawl:swamp"]
        self.assertEqual(swamp["guid"], "01a1b974-0000-7000-8000-000000000001")
        self.assertEqual(swamp["version"], 2)
        self.assertIsNone(crawls["texts"]["crawl:prologue"]["guid"])

    def test_right_answer_tag_survives(self):
        self.apply()
        with open(os.path.join(self.out, "conversations.json"), encoding="utf-8") as f:
            talk = json.load(f)["texts"]["dialogue:wanderer_ember"]["payload"]
        ask = next(n for n in talk["nodes"] if n["id"] == "ask")
        self.assertEqual([c.get("tags") for c in ask["choices"]], [["right-answer"], None])

    def test_retired_text_is_removed(self):
        rows, _ = self.apply()
        self.assertIn("retired: removed", rows["glossary-pincer"]["note"])
        with open(os.path.join(self.out, "glossary.json"), encoding="utf-8") as f:
            self.assertEqual(json.load(f)["texts"], {})

    def test_second_apply_changes_nothing(self):
        self.apply()
        before = tree(self.out)
        rows, _ = self.apply()
        self.assertEqual(tree(self.out), before)
        self.assertEqual(rows["crawl-swamp"]["note"], "unchanged")

    def test_hand_edit_is_overwritten_and_reported(self):
        self.apply()
        path = os.path.join(self.out, "descriptions.json")
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
        doc["texts"]["class:lich"]["payload"]["short"] = "Edited by hand."
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f)
        rows, _ = self.apply()
        self.assertIn("hand edit", rows["description-class-lich"]["note"])
        self.assertEqual(tree(self.out), tree(EXPECTED))

    def test_dry_run_writes_nothing(self):
        before = tree(self.out)
        rows, _ = self.apply(dry_run=True)
        self.assertEqual(tree(self.out), before)
        self.assertEqual(rows["crawl-swamp"]["result"], "applied")

    def test_no_content_skips_rather_than_guessing(self):
        rows, _ = self.apply(content=os.path.join(self.tmp, "missing"))
        self.assertEqual(rows["crawl-swamp"]["result"], "skipped")
        self.assertFalse(os.path.exists(os.path.join(self.out, "crawls.json")))

    def test_key_held_by_another_text_is_skipped(self):
        path = os.path.join(self.out, "descriptions.json")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump({"schema": "gridgame-text/1", "kind": "description", "texts": {"class:lich": {
                "key": "class:lich", "id": "description-lich-old", "guid": None, "version": 1, "hash": "",
                "about": [], "speakers": [], "payload": {"short": "x"}}}}, f)
        rows, _ = self.apply()
        self.assertEqual(rows["description-class-lich"]["result"], "skipped")


class KeySchemeTests(unittest.TestCase):
    def test_game_keys_of_the_fixture(self):
        keys = prose_keys.game_keys(prose_keys.load_content(CONTENT))
        self.assertEqual(keys["dialogue:wanderer_ember"], "conversation")
        self.assertEqual(keys["crawl:prologue"], "crawl")
        self.assertEqual(keys["crawl:swamp"], "crawl")
        self.assertEqual(keys["class:lich"], "description")
        self.assertEqual(keys["glossary:pincer"], "glossary")
        self.assertEqual(keys["npc:merchant"], "description")
        self.assertNotIn("crawl:ending", keys)

    def test_text_ids(self):
        self.assertEqual(prose_keys.text_id("conversation", "dialogue:wanderer_ember"), "conversation-wanderer-ember")
        self.assertEqual(prose_keys.text_id("description", "class:lich"), "description-class-lich")
        self.assertEqual(prose_keys.text_id("crawl", "crawl:dialogue/interlude_x"), "crawl-dialogue-interlude-x")
        self.assertEqual(prose_keys.text_id("bark", "bark:merc/myrmidon/pitch"), "bark-merc-myrmidon-pitch")


if __name__ == "__main__":
    unittest.main()
