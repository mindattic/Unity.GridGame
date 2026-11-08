#!/usr/bin/env python3
"""GG-T355 Phase 2: the game reads every story string from Content/Text (docs/PROSE.md, BIBLE section 27.2).

The narrative fields are gone from the gameplay JSON. Their words live in Prose (universe `gridgame`) and reach the game
as Assets/Gaiden/Resources/Content/Text/<kind>.json: written by tools/prose_apply.py from published Prose texts, or, until
the owner approves a text, by the one-time SEED this tool made from the text the gameplay JSON used to hold.

    python tools/prose_text.py check                  every key has a text, no narrative field is left in gameplay JSON
    python tools/prose_text.py snapshot               ONE-TIME migration (done 2026-11-07): seed Content/Text from the
                                                      in-JSON text, record the seed in Data/Prose/applied.jsonl, strip
                                                      the fields; refuses once there is nothing left to strip

Library (the mapper, the seed and the content generators import it):

- NARRATIVE: the stripped fields, per content file (ContentLoader's join and TextSourcingTests mirror it).
- load_texts / hydrate: the Python twin of ContentLoader's join, so tools that read story text (prose_map, prose_seed)
  see the words they used to read from the gameplay JSON.
- seed_entry / upsert_seed: how a generator (tools/weapons.py) gives a new record its text: a seed entry in Content/Text
  (version 0, source "seed", never overwriting a Prose-applied text) plus a prose-text/1 draft to hand to Prose
  (`prose --texts-draft gridgame <dir>`; agents only draft, RFC 0009).

Seed entries have exactly the applier's shape (prose_apply.entry): key, id, guid (null), version (0), source ("seed"
instead of "prose"), hash, about, speakers, payload. The hash is this tool's sha256 of the payload, not Prose's; a
published text always replaces a seed because its version is higher (kit.needs_pull, applied.jsonl).

Standard library only.
"""

from __future__ import annotations

import argparse
import copy
import datetime
import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import prose_apply as A  # noqa: E402
import prose_keys as K  # noqa: E402

TEXT_DIR = os.path.join(K.CONTENT_DIR, "Text")
APPLIED = os.path.join("Data", "Prose", "applied.jsonl")
SEED = "seed"

# The narrative fields Phase 2 moved out of the gameplay JSON (file -> paths; "*" is every element of a list). Names,
# labels and mechanics stay in the game: unit/item/ability/theme/stage/quest names, dialogue Title (the Talk row label),
# quest Giver, glossary Term/Aliases, ability Description and callouts, class Card, item Passive*, caravan
# Description/Levels, pickups, bounties, the tutorial, Credits.
NARRATIVE = {
    "classes": [("*", "Description"), ("*", "Lore"), ("*", "BossPhases", "*", "Announce")],
    "dialogues": [("*", "Crawl"), ("*", "Nodes", "*", "Text"), ("*", "Nodes", "*", "Choices", "*", "Label")],
    "campaign": [("Prologue", "Crawl"), ("Ending", "Crawl"), ("Themes", "*", "Crawl")],
    "quests": [("*", "Summary"), ("*", "TurnIn")],
    "glossary": [("*", "Text")],
    "items": [("*", "Description")],
    "armorsets": [("*", "Description")],
    "skilltrees": [("*", "Description"), ("*", "Nodes", "*", "Description")],
    "caravan": [("*", "Barks")],
    "mercenaries": [("*", "Pitch"), ("*", "Renewal")],
    "stages": [("*", "Waves", "*", "SurpriseLine")],
}


# ------------------------------------------------------------------------------------------------ entries

def payload_hash(payload) -> str:
    raw = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return "sha256:" + hashlib.sha256(raw.encode("utf-8")).hexdigest()


def seed_entry(doc: dict) -> dict:
    """A prose-text/1 document (prose_seed.text) as the Content/Text entry the applier would write, marked as a seed."""
    payload = doc.get("payload") or {}
    return {
        "key": doc["key"],
        "id": doc["id"],
        "guid": None,
        "version": 0,
        "source": SEED,
        "hash": payload_hash(payload),
        "about": list(doc.get("about") or []),
        "speakers": list(doc.get("speakers") or []),
        "payload": payload,
    }


def load_texts(text_dir: str = TEXT_DIR) -> dict:
    """Every Content/Text entry by key (all kinds)."""
    out = {}
    for kind in A.FILES:
        for key, e in A.read_file(text_dir, kind).items():
            out[key] = e
    return out


def write_texts(text_dir: str, by_kind: dict) -> None:
    for kind, texts in by_kind.items():
        A.write_text(os.path.join(text_dir, A.FILES[kind]), A.render(kind, texts))


def upsert_seed(docs: list, text_dir: str = TEXT_DIR, drafts_dir: str = None) -> dict:
    """Gives new records their text: each doc becomes a seed entry unless its key already holds a Prose-applied text
    (source "prose"), which is never overwritten. With drafts_dir, every doc is also written there as a draft for
    `prose --texts-draft gridgame <drafts_dir>`. Returns counts {"seeded", "kept"}."""
    by_kind = {kind: A.read_file(text_dir, kind) for kind in A.FILES}
    counts = {"seeded": 0, "kept": 0}
    for d in docs:
        held = by_kind[d["kind"]]
        old = held.get(d["key"])
        if old is not None and old.get("source", "prose") != SEED:
            counts["kept"] += 1
            continue
        held[d["key"]] = seed_entry(d)
        counts["seeded"] += 1
    write_texts(text_dir, by_kind)
    if drafts_dir:
        os.makedirs(drafts_dir, exist_ok=True)
        for d in docs:
            with open(os.path.join(drafts_dir, d["id"] + ".json"), "w", encoding="utf-8", newline="\n") as f:
                json.dump(d, f, ensure_ascii=False, indent=2)
                f.write("\n")
    return counts


# ------------------------------------------------------------------------------------------------ the join (Python twin)

def _crawl(p: dict) -> str:
    return "\n\n".join(p.get("paragraphs") or [])


def _line(p: dict) -> str:
    lines = p.get("lines") or []
    return lines[0] if lines else ""


def hydrate(content: dict, texts: dict) -> dict:
    """Puts each key's words back onto the gameplay records, as ContentLoader's join does. Returns content."""
    def pay(key):
        e = texts.get(key)
        return e.get("payload") or {} if isinstance(e, dict) else None

    for c in content.get("classes") or []:
        p = pay("class:" + c.get("Id", ""))
        if p is not None:
            c["Description"], c["Lore"] = p.get("short"), p.get("long")
        for n, ph in enumerate(c.get("BossPhases") or []):
            b = pay(f"bark:bossphase/{c.get('Id')}/{n}")
            if b is not None and isinstance(ph, dict):
                ph["Announce"] = _line(b)
    for d in content.get("dialogues") or []:
        p = pay("dialogue:" + d.get("Id", ""))
        if p is not None:
            nodes = {n.get("id"): n for n in p.get("nodes") or []}
            for n in d.get("Nodes") or []:
                tn = nodes.get(n.get("Id")) or {}
                n["Text"] = tn.get("line")
                tc = tn.get("choices") or []
                for i, ch in enumerate(n.get("Choices") or []):
                    ch["Label"] = tc[i].get("text") if i < len(tc) else None
        cr = pay("crawl:dialogue/" + d.get("Id", ""))
        if cr is not None:
            d["Crawl"] = _crawl(cr)
    camp = content.get("campaign") if isinstance(content.get("campaign"), dict) else {}
    for part, key in (("Prologue", "crawl:prologue"), ("Ending", "crawl:ending")):
        p = pay(key)
        if p is not None and isinstance(camp.get(part), dict):
            camp[part]["Crawl"] = _crawl(p)
    for t in camp.get("Themes") or []:
        p = pay("crawl:" + t.get("Id", ""))
        if p is not None:
            t["Crawl"] = _crawl(p)
    for q in content.get("quests") or []:
        p = pay("quest:" + q.get("Id", ""))
        if p is not None:
            q["Summary"] = p.get("brief")
            if p.get("complete"):
                q["TurnIn"] = p["complete"]
    for g in content.get("glossary") or []:
        p = pay("glossary:" + g.get("Id", ""))
        if p is not None:
            g["Text"] = p.get("definition")
    for scheme, rows in (("item", content.get("items")), ("armorset", content.get("armorsets"))):
        for r in rows or []:
            p = pay(f"{scheme}:{r.get('Id', '')}") if isinstance(r, dict) else None
            if p is not None:
                r["Description"] = p.get("short")
    for t in content.get("skilltrees") or []:
        p = pay("skilltree:" + t.get("Id", ""))
        if p is not None:
            t["Description"] = p.get("short")
        for n in t.get("Nodes") or []:
            p = pay("skillnode:" + n.get("Id", ""))
            if p is not None:
                n["Description"] = p.get("short")
    for u in content.get("caravan") or []:
        p = pay("bark:caravan/" + u.get("Id", ""))
        if p is not None:
            u["Barks"] = list(p.get("lines") or [])
    for m in content.get("mercenaries") or []:
        for field, ctx in (("Pitch", "pitch"), ("Renewal", "renewal")):
            p = pay(f"bark:merc/{m.get('Class')}/{ctx}")
            if p is not None:
                m[field] = _line(p)
    for s in content.get("stages") or []:
        for w, wave in enumerate(s.get("Waves") or []):
            p = pay(f"bark:surprise/{s.get('Id')}/{w}")
            if p is not None and isinstance(wave, dict):
                wave["SurpriseLine"] = _line(p)
    return content


def load_content_with_text(content_dir: str = K.CONTENT_DIR, text_dir: str = None) -> dict:
    """The content JSON with Content/Text joined back on (what the game sees in memory)."""
    content = K.load_content(content_dir)
    return hydrate(content, load_texts(text_dir or os.path.join(content_dir, "Text")))


# ------------------------------------------------------------------------------------------------ scanning

def narrative_paths(name: str, data) -> list:
    """Concrete paths of every narrative field still present in a content file's parsed JSON."""
    out = []

    def walk(node, pattern, at):
        if not pattern:
            out.append(tuple(at))
            return
        step, rest = pattern[0], pattern[1:]
        if step == "*":
            if isinstance(node, list):
                for i, x in enumerate(node):
                    walk(x, rest, at + [i])
        elif isinstance(node, dict) and step in node:
            if rest or node[step] is not None:
                walk(node[step], rest, at + [step])

    for pattern in NARRATIVE.get(name, []):
        walk(data, list(pattern), [])
    return out


def leftovers(content_dir: str = K.CONTENT_DIR) -> list:
    """'file: path' for every narrative field left in gameplay JSON (should be empty)."""
    out = []
    for name in NARRATIVE:
        path = os.path.join(content_dir, name + ".json")
        if os.path.isfile(path):
            for p in narrative_paths(name, K.load(path)):
                out.append(f"{name}.json: {'/'.join(str(x) for x in p)}")
    return out


# ------------------------------------------------------------------------------------------------ span-exact JSON edits

class _Spans:
    """Records the character span of every object member (key start .. value end) by path, so a field can be removed
    from a hand-formatted JSON file without reformatting the rest of it."""

    WS = " \t\r\n"

    def __init__(self, text: str):
        self.t = text
        self.members = {}
        end = self.value(self.skip(0), ())
        assert self.skip(end) == len(text), "trailing data"

    def skip(self, i):
        while i < len(self.t) and self.t[i] in self.WS:
            i += 1
        return i

    def string(self, i):
        assert self.t[i] == '"'
        i += 1
        while True:
            ch = self.t[i]
            if ch == "\\":
                i += 2
            elif ch == '"':
                return i + 1
            else:
                i += 1

    def value(self, i, path):
        ch = self.t[i]
        if ch == "{":
            i = self.skip(i + 1)
            if self.t[i] == "}":
                return i + 1
            while True:
                ks = i
                ke = self.string(i)
                key = json.loads(self.t[ks:ke])
                i = self.skip(ke)
                assert self.t[i] == ":"
                ve = self.value(self.skip(i + 1), path + (key,))
                self.members[path + (key,)] = (ks, ve)
                i = self.skip(ve)
                if self.t[i] == ",":
                    i = self.skip(i + 1)
                    continue
                assert self.t[i] == "}"
                return i + 1
        if ch == "[":
            i = self.skip(i + 1)
            if self.t[i] == "]":
                return i + 1
            n = 0
            while True:
                i = self.skip(self.value(i, path + (n,)))
                n += 1
                if self.t[i] == ",":
                    i = self.skip(i + 1)
                    continue
                assert self.t[i] == "]"
                return i + 1
        if ch == '"':
            return self.string(i)
        j = i
        while j < len(self.t) and self.t[j] not in ",]}" + self.WS:
            j += 1
        return j


def _removal(t: str, ks: int, ve: int):
    """The (start, end) to delete for a member: its whole line when it stands alone on one, else the member and its
    comma (the following one, or the preceding one when it is the last member)."""
    ls = t.rfind("\n", 0, ks) + 1
    j = ve
    if j < len(t) and t[j] == ",":
        j += 1
    k = j
    while k < len(t) and t[k] in " \t\r":
        k += 1
    if t[ls:ks].strip() == "" and (k >= len(t) or t[k] == "\n"):
        return ls, min(k + 1, len(t))
    m = ve
    while m < len(t) and t[m] in " \t":
        m += 1
    if m < len(t) and t[m] == ",":
        m += 1
        e = m
        while e < len(t) and t[e] in " \t":
            e += 1
        if e >= len(t) or t[e] in "\r\n":  # the member ends its line: drop it and the space before it, keep the newline
            b = ks
            while b > ls and t[b - 1] in " \t":
                b -= 1
            return b, e
        return ks, e
    b = ks - 1
    while b >= 0 and t[b] in " \t":
        b -= 1
    if b >= 0 and t[b] == ",":
        return b, ve
    return ks, ve


def _drop_dangling_commas(t: str) -> str:
    out, i, n, in_str = [], 0, len(t), False
    while i < n:
        ch = t[i]
        if in_str:
            out.append(ch)
            if ch == "\\":
                out.append(t[i + 1])
                i += 2
                continue
            if ch == '"':
                in_str = False
        elif ch == '"':
            in_str = True
            out.append(ch)
        elif ch == ",":
            j = i + 1
            while j < n and t[j] in " \t\r\n":
                j += 1
            if j < n and t[j] in "}]":
                i += 1
                continue
            out.append(ch)
        else:
            out.append(ch)
        i += 1
    return "".join(out)


def strip_file(text: str, paths: list, replace: dict = None) -> str:
    """Removes the members at `paths` (and swaps any in `replace`: path -> new member text) in place."""
    spans = _Spans(text)
    edits = []
    for p in paths:
        ks, ve = spans.members[tuple(p)]
        if replace and tuple(p) in replace:
            edits.append((ks, ve, replace[tuple(p)]))
        else:
            s, e = _removal(text, ks, ve)
            edits.append((s, e, ""))
    edits.sort(reverse=True)
    for s, e, new in edits:
        text = text[:s] + new + text[e:]
    return _drop_dangling_commas(text)


def _remove(data, path):
    node = data
    for step in path[:-1]:
        node = node[step]
    del node[path[-1]]


# ------------------------------------------------------------------------------------------------ commands

def cmd_snapshot(args) -> int:
    import prose_seed  # noqa: E402  (imports this module)
    content_dir, text_dir = args.content, args.out
    raw = {name: open(os.path.join(content_dir, name + ".json"), encoding="utf-8-sig").read() for name in NARRATIVE}
    parsed = {name: json.loads(raw[name]) for name in NARRATIVE}
    found = {name: narrative_paths(name, parsed[name]) for name in NARRATIVE}
    if not any(found.values()):
        print("prose_text snapshot: no narrative field is left in the gameplay JSON; nothing to seed (it ran once)")
        return 1
    # The seed, from today's in-JSON text: exactly the drafts prose_seed sent to Prose, as the applier's entries.
    content = K.load_content(content_dir)
    universe = os.path.join("Data", "Prose", "gridgame.universe.json")
    known = {e["id"] for e in K.load(universe).get("entities") or []} if os.path.isfile(universe) else set()
    docs = prose_seed.build(content, known)
    by_kind = {kind: {} for kind in A.FILES}
    for d in docs:
        by_kind[d["kind"]][d["key"]] = seed_entry(d)
    # A dialogue's crawl is optional, so the gameplay record keeps a marker for its key (HasCrawl).
    replace = {}
    for name in NARRATIVE:
        expected = copy.deepcopy(parsed[name])
        for p in found[name]:
            if name == "dialogues" and len(p) == 2 and p[1] == "Crawl":
                replace[p] = '"HasCrawl": true'
                expected[p[0]].pop("Crawl")
                expected[p[0]]["HasCrawl"] = True
            else:
                _remove(expected, p)
        new = strip_file(raw[name], found[name], {k: v for k, v in replace.items() if name == "dialogues"})
        got = json.loads(new)
        if name == "dialogues":  # key order: HasCrawl sits where Crawl was
            assert json.dumps(got, sort_keys=True) == json.dumps(expected, sort_keys=True), name
        else:
            assert got == expected, name
        parsed[name] = got
        raw[name] = new
    # Round trip: the seed joined onto the stripped JSON gives back every word the JSON held.
    stripped = copy.deepcopy(parsed)
    texts = {k: v for t in by_kind.values() for k, v in t.items()}
    joined = hydrate({**content, **copy.deepcopy(stripped)}, texts)
    diffs = []
    for name in NARRATIVE:
        before = json.loads(open(os.path.join(content_dir, name + ".json"), encoding="utf-8-sig").read())
        for p in found[name]:
            a, b = before, joined[name]
            for step in p:
                a = a[step]
                b = b[step] if b is not None and (isinstance(b, list) or step in b) else None
            if a != b:
                diffs.append(f"{name}: {'/'.join(map(str, p))}: {a!r:.60} != {b!r:.60}")
    if diffs:
        print("prose_text snapshot: the seed does not give back the in-JSON text:\n  " + "\n  ".join(diffs[:40]))
        return 2
    keys = K.game_keys({**content, **stripped})
    assert set(keys) == set(texts), sorted(set(keys) ^ set(texts))[:20]
    if args.dry_run:
        print(f"prose_text snapshot (dry run): {len(texts)} texts, {sum(len(v) for v in found.values())} fields to strip")
        return 0
    write_texts(text_dir, by_kind)
    for name in NARRATIVE:
        with open(os.path.join(content_dir, name + ".json"), "w", encoding="utf-8", newline="") as f:
            f.write(raw[name])
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    rows = [{"id": e["id"], "version": 0, "hash": e["hash"], "key": e["key"], "kind": kind, "status": SEED,
             "appliedAt": now} for kind in A.FILES for e in sorted(by_kind[kind].values(), key=lambda x: x["key"])]
    os.makedirs(os.path.dirname(args.applied), exist_ok=True)
    with open(args.applied, "a", encoding="utf-8", newline="\n") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    print(f"prose_text snapshot: {len(texts)} texts seeded into {text_dir} "
          f"({', '.join(f'{k} {len(v)}' for k, v in by_kind.items())}); "
          f"{sum(len(v) for v in found.values())} narrative fields stripped; {len(rows)} seed rows in {args.applied}")
    return 0


def cmd_check(args) -> int:
    left = leftovers(args.content)
    content = K.load_content(args.content)
    keys = K.game_keys(content)
    texts = load_texts(args.out)
    missing = sorted(k for k in keys if k not in texts)
    wrong = sorted(k for k in keys if k in texts and texts[k].get("id") != K.text_id(keys[k], k))
    orphans = sorted(k for k in texts if k not in keys)
    for label, rows in (("narrative field left in gameplay JSON", left), ("key without a text", missing),
                        ("text id does not match its key", wrong), ("text no gameplay record uses", orphans)):
        for r in rows[:50]:
            print(f"  {label}: {r}")
    seeds = sum(1 for e in texts.values() if e.get("source") == SEED)
    print(f"prose_text check: {len(keys)} keys, {len(texts)} texts ({seeds} seed, {len(texts) - seeds} from Prose); "
          f"{len(left)} leftover, {len(missing)} missing, {len(wrong)} mismatched, {len(orphans)} orphan")
    return 1 if left or missing or wrong else 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="GridGame's Content/Text (Prose) snapshot and checks (docs/PROSE.md).")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("snapshot", "check"):
        p = sub.add_parser(name)
        p.add_argument("--content", default=K.CONTENT_DIR)
        p.add_argument("--out", default=TEXT_DIR)
        p.add_argument("--applied", default=APPLIED)
        p.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)
    return cmd_snapshot(args) if args.cmd == "snapshot" else cmd_check(args)


if __name__ == "__main__":
    sys.exit(main())
