#!/usr/bin/env python3
"""GG-T355: GridGame's applier for the Prose conduit (docs/PROSE.md; Prose docs/CONDUIT.md section 6).

    python tools/prose_apply.py <pending-dir> [--content Assets/Gaiden/Resources/Content]
                                [--out Assets/Gaiden/Resources/Content/Text] [--dry-run]

The consumer kit (`kit.ps1 apply`) runs it from the repo root with pending/ full of approved or published
`prose-text/1` documents. It writes each one into the game's text file for its kind, keyed by the text's key, and
prints one JSON line per text to stdout (the kit's contract):

    {"id": "...", "version": 3, "hash": "sha256:...", "result": "applied" | "orphan" | "skipped", "note": "..."}

The kit records the `applied` lines in Data/Prose/applied.jsonl and clears them from pending/; this script never
writes applied.jsonl itself.

- applied: written (or already identical). A retired text is removed from its file.
- orphan:  the key names no gameplay record (tools/prose_keys.game_keys), or names one of another kind.
- skipped: the text cannot be applied as it is (not prose-text/1, a kind GridGame does not use, not approved or
           published, or its key is already held by another text id).

Text files (one per kind; written only by this script, never by hand: an edit belongs in Prose):

    conversation -> conversations.json   crawl -> crawls.json      description -> descriptions.json
    quest        -> quests.json          bark  -> barks.json       glossary    -> glossary.json

    {"schema": "gridgame-text/1", "kind": "crawl",
     "texts": {"crawl:swamp": {"key", "id", "guid", "version", "hash", "about", "speakers", "payload"}, ...}}

`texts` is sorted by key; files are UTF-8, two-space indented, LF, so the same texts give the same bytes. `guid` is
the text's Prose GUID when the pulled document carries one (`guid`, or `entityId`), else null: prose-text/1 pulls
expose only the slug id today (a Prose-side gap, docs/PROSE.md). Prose's bookkeeping (status, approvedBy, dates) is
left out, so approved -> published unchanged writes the same bytes.

On stderr it reports, per kind, how many gameplay keys still have no text (they keep their in-JSON placeholder).
The game does not read these files yet: Phase 2 adds the ContentLoader join.

Standard library only. Exit code 0 unless it could not run at all (2).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import prose_keys as K  # noqa: E402

SCHEMA_IN = "prose-text/1"
SCHEMA_OUT = "gridgame-text/1"
FILES = {"conversation": "conversations.json", "crawl": "crawls.json", "description": "descriptions.json",
         "quest": "quests.json", "bark": "barks.json", "glossary": "glossary.json"}
STABLE_ID = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
KEY_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.:/-]*$")


def load(path: str):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def entry(doc: dict) -> dict:
    return {
        "key": doc["key"],
        "id": doc["id"],
        "guid": doc.get("guid") or doc.get("entityId"),
        "version": doc["version"],
        "hash": doc.get("hash", ""),
        "about": list(doc.get("about") or []),
        "speakers": list(doc.get("speakers") or []),
        "payload": doc.get("payload") or {},
    }


def render(kind: str, texts: dict) -> str:
    doc = {"schema": SCHEMA_OUT, "kind": kind, "texts": {k: texts[k] for k in sorted(texts)}}
    return json.dumps(doc, ensure_ascii=False, indent=2) + "\n"


def read_file(out: str, kind: str) -> dict:
    path = os.path.join(out, FILES[kind])
    if not os.path.isfile(path):
        return {}
    try:
        d = load(path)
    except (OSError, ValueError):
        return {}
    texts = d.get("texts") if isinstance(d, dict) else None
    return dict(texts) if isinstance(texts, dict) else {}


def report(doc, result: str, note: str = "") -> dict:
    d = doc if isinstance(doc, dict) else {}
    row = {"id": d.get("id"), "version": d.get("version"), "hash": d.get("hash"), "result": result}
    if note:
        row["note"] = note
    return row


def check(doc) -> str:
    """Why this document cannot be applied, or "" when it can."""
    if not isinstance(doc, dict):
        return "not a JSON object"
    if doc.get("schema") != SCHEMA_IN:
        return f"schema is not {SCHEMA_IN}"
    if not isinstance(doc.get("id"), str) or not STABLE_ID.match(doc["id"]):
        return f"id '{doc.get('id')}' is not a stable slug"
    if doc.get("kind") not in FILES:
        return f"kind '{doc.get('kind')}' is not one GridGame uses ({', '.join(FILES)})"
    if not isinstance(doc.get("key"), str) or not KEY_RE.match(doc["key"]):
        return f"key '{doc.get('key')}' is not a valid key"
    if not isinstance(doc.get("version"), int) or doc["version"] < 1:
        return "version must be a positive integer"
    if doc.get("status") not in ("approved", "published", "retired"):
        return f"status '{doc.get('status')}' is not approved, published or retired (only those leave Prose)"
    if doc.get("status") != "retired" and not isinstance(doc.get("payload"), dict):
        return "payload must be an object"
    return ""


def write_text(path: str, text: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    os.replace(tmp, path)


def apply_all(pending: str, content_dir: str, out: str, dry_run: bool, err=sys.stderr) -> list:
    """Applies every pending text (file name order); returns the report rows."""
    files = sorted(f for f in os.listdir(pending) if f.endswith(".json")) if os.path.isdir(pending) else []
    keys = K.game_keys(K.load_content(content_dir)) if os.path.isdir(content_dir) else None
    texts = {kind: read_file(out, kind) for kind in FILES}
    before = {kind: render(kind, t) for kind, t in texts.items()}
    rows = []
    for name in files:
        try:
            doc = load(os.path.join(pending, name))
        except (OSError, ValueError) as e:
            rows.append({"id": None, "version": None, "hash": None, "result": "skipped", "note": f"{name}: unreadable ({e})"})
            continue
        why = check(doc)
        if why:
            rows.append(report(doc, "skipped", why))
            continue
        kind, key = doc["kind"], doc["key"]
        held = texts[kind]
        if doc["status"] == "retired":
            if key in held and held[key].get("id") == doc["id"]:
                del held[key]
                rows.append(report(doc, "applied", f"retired: removed {key} from {FILES[kind]}"))
            else:
                rows.append(report(doc, "applied", "retired: nothing to remove"))
            continue
        if keys is None:
            rows.append(report(doc, "skipped", f"no content folder at {content_dir} to check the key against"))
            continue
        if key not in keys:
            rows.append(report(doc, "orphan", f"key '{key}': no gameplay record uses it"))
            continue
        if keys[key] != kind:
            rows.append(report(doc, "orphan", f"key '{key}' is a {keys[key]} key, not a {kind}"))
            continue
        old = held.get(key)
        if old is not None and old.get("id") != doc["id"]:
            rows.append(report(doc, "skipped", f"key '{key}' is already held by applied text '{old.get('id')}'"))
            continue
        new = entry(doc)
        if old == new:
            note = "unchanged"
        elif old is None:
            note = "new"
        elif old.get("version") == new["version"] and old.get("hash") == new["hash"]:
            note = "overwrote a hand edit (edit the text in Prose instead)"
        else:
            note = f"updated v{old.get('version')} -> v{new['version']}"
        held[key] = new
        rows.append(report(doc, "applied", note))
    if not dry_run:
        for kind, t in texts.items():
            text = render(kind, t)
            if text != before[kind] and (t or os.path.isfile(os.path.join(out, FILES[kind]))):
                write_text(os.path.join(out, FILES[kind]), text)
    if keys is not None:
        missing = {}
        for key, kind in keys.items():
            if key not in texts[kind]:
                missing[kind] = missing.get(kind, 0) + 1
        err.write("prose_apply: " + ", ".join(
            f"{kind} {len(texts[kind])} applied / {missing.get(kind, 0)} keys still on placeholder" for kind in FILES) + "\n")
    return rows


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="GridGame's Prose applier (docs/PROSE.md).")
    ap.add_argument("pending", nargs="?", default=os.environ.get("PROSE_KIT_PENDING", os.path.join("Data", "Prose", "pending")))
    ap.add_argument("--content", default=K.CONTENT_DIR)
    ap.add_argument("--out", default=os.path.join(K.CONTENT_DIR, "Text"))
    ap.add_argument("--dry-run", action="store_true", default=os.environ.get("PROSE_KIT_DRY_RUN") == "1")
    args = ap.parse_args(argv)
    try:
        rows = apply_all(args.pending, args.content, args.out, args.dry_run)
    except OSError as e:
        print(f"prose_apply: {e}", file=sys.stderr)
        return 2
    for r in rows:
        print(json.dumps(r, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
