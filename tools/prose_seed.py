#!/usr/bin/env python3
"""GG-T355: the first seed. Converted the story text GridGame's content JSON held into prose-text/1 drafts for Prose
(RFC 0017 section 7, "First seed"). Since Phase 2 the gameplay JSON holds no story text, so this reads the content with
Content/Text joined back on (prose_text.load_content_with_text): re-seeding drafts the game's current words.

    python tools/prose_seed.py <out-dir> [--content Assets/Gaiden/Resources/Content]
                               [--universe Data/Prose/gridgame.universe.json]
    prose --texts-draft gridgame <out-dir>          (drafts only: agents never approve or publish, RFC 0009)

One file per text, <text id>.json, with the key scheme of tools/prose_keys.py. The content JSON is read only.
Gameplay stays out of the texts: triggers, conditions, effects, speakers' classes and portraits remain in the game's
records; a text carries only the words, the node graph (node ids, next, choice order) and the choice tags the game's
logic keys on (right-answer, conditional). `about` and entity speakers are dropped when the universe file lacks the
entity, so a draft never names an entity Prose does not have.

Standard library only.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import prose_keys as K  # noqa: E402
import prose_map  # noqa: E402
import prose_text  # noqa: E402


def text(kind: str, key: str, payload: dict, about=None, speakers=None) -> dict:
    doc = {"schema": "prose-text/1", "id": K.text_id(kind, key), "universe": K.UNIVERSE, "kind": kind, "key": key,
           "about": sorted(set(about or [])), "version": 1, "status": "draft", "payload": payload}
    if speakers:
        doc["speakers"] = list(dict.fromkeys(speakers))
    return doc


def paragraphs(s: str) -> list:
    return [p.strip() for p in (s or "").replace("\r\n", "\n").split("\n\n") if p.strip()]


def speaker_token(spk: dict, known: set) -> str:
    cls, name, sid = spk.get("Class"), spk.get("Name"), spk.get("Id") or "speaker"
    if cls and cls.startswith("$"):
        return cls
    if cls and K.class_entity(cls) in known:
        return K.class_entity(cls)
    npc = K.npc_id_for(name) or K.npc_id_for(sid)
    if npc and K.character_entity(npc) in known:
        return K.character_entity(npc)
    return "$" + K.slug(sid).replace("-", "_")


def trigger_about(trig: dict, known: set) -> list:
    kind, tid = trig.get("Kind"), trig.get("Id") or ""
    cands = []
    if kind in ("Recruit", "Talk"):
        cands = [K.character_entity(tid[4:])] if tid.startswith("npc:") else [K.class_entity(tid)]
    elif kind == "Wanderer" and tid:
        cands = [K.faction_entity(tid)]
    elif kind in ("ThemeStart", "StageStart", "StageCleared"):
        cands = [K.place_entity(tid)]
    return [c for c in cands if c in known]


def conversation(d: dict, known: set):
    tokens = {s.get("Id"): speaker_token(s, known) for s in d.get("Speakers") or [] if s.get("Id")}
    nodes = []
    for n in d.get("Nodes") or []:
        node = {"id": n.get("Id"), "speaker": tokens.get(n.get("Speaker"), "$" + K.slug(n.get("Speaker") or "x").replace("-", "_")),
                "line": n.get("Text") or ""}
        if n.get("Next"):
            node["next"] = n["Next"]
        choices = []
        for c in n.get("Choices") or []:
            tags = []
            effects = [e.get("Type") for e in c.get("Effects") or []]
            if c.get("Next") == "heard" or "GrantSkillPoint" in effects:
                tags.append("right-answer")
            if c.get("Conditions"):
                tags.append("conditional")
            ch = {"text": c.get("Label") or "", "next": c.get("Next") or ""}
            if tags:
                ch["tags"] = tags
            choices.append(ch)
        if choices:
            node["choices"] = choices
        nodes.append(node)
    speakers = [n["speaker"] for n in nodes]
    about = trigger_about(d.get("Trigger") or {}, known) + [s for s in speakers if not s.startswith("$")]
    payload = {"title": d.get("Title") or d["Id"], "start": nodes[0]["id"] if nodes else "", "nodes": nodes}
    return text("conversation", "dialogue:" + d["Id"], payload, about, speakers)


def faction_short(f: dict) -> str:
    name = (f.get("Name") or f["Id"]).strip()
    return f"{name[:1].upper()}{name[1:]}: {prose_map.FACTION_SUMMARY[:1].lower()}{prose_map.FACTION_SUMMARY[1:]}"


def entity_descriptions(content: dict, known: set) -> list:
    """GG-T374: the place (region, stage), faction and caravan-crew descriptions, placeholder words from what the game
    already says of them (a region's crawl opening, the mapper's summaries); Prose's author writes the real ones."""
    out = []
    camp = content["campaign"] if isinstance(content["campaign"], dict) else {}
    themes = {t["Id"]: t for t in camp.get("Themes") or [] if t.get("Id")}

    def add(key, short, eid):
        out.append(text("description", key, {"short": short}, [eid] if eid in known else []))

    for tid, t in themes.items():
        add("place:" + tid, prose_map.first_paragraph(t.get("Crawl")) or t.get("Name") or tid, K.place_entity(tid))
    seen = set()
    for tid, sid in K.campaign_stage_ids(content):
        if sid not in seen:
            seen.add(sid)
            add("place:" + sid, prose_map.stage_summary(themes[tid]), K.place_entity(sid))
    for f in content.get("factions") or []:
        if isinstance(f, dict) and f.get("Id"):
            add("faction:" + f["Id"], faction_short(f), K.faction_entity(f["Id"]))
    for u in content["caravan"]:
        if u.get("Crew") and "character:" + K.slug(u["Crew"]) not in seen:
            seen.add("character:" + K.slug(u["Crew"]))
            add("character:" + K.slug(u["Crew"]), prose_map.crew_summary(u), K.character_entity(u["Crew"]))
    return out


def build(content: dict, known: set) -> list:
    out = []
    for d in content["dialogues"]:
        if not d.get("Id"):
            continue
        out.append(conversation(d, known))
        if d.get("Crawl"):
            out.append(text("crawl", "crawl:dialogue/" + d["Id"],
                            {"title": d.get("Title") or d["Id"], "paragraphs": paragraphs(d["Crawl"])},
                            trigger_about(d.get("Trigger") or {}, known)))
    camp = content["campaign"] if isinstance(content["campaign"], dict) else {}
    for part, key in (("Prologue", "crawl:prologue"), ("Ending", "crawl:ending")):
        p = camp.get(part)
        if isinstance(p, dict) and p.get("Crawl"):
            about = [K.place_entity(p[k]) for k in ("Backdrop", "Stage") if p.get(k) and K.place_entity(p[k]) in known]
            out.append(text("crawl", key, {"title": p.get("Title") or part, "paragraphs": paragraphs(p["Crawl"])}, about))
    for t in camp.get("Themes") or []:
        if t.get("Id") and t.get("Crawl"):
            pid = K.place_entity(t["Id"])
            out.append(text("crawl", "crawl:" + t["Id"], {"title": t.get("Name") or t["Id"], "paragraphs": paragraphs(t["Crawl"])},
                            [pid] if pid in known else []))
    for c in content["classes"]:
        if c.get("Id") and (c.get("Description") or c.get("Lore")):
            p = {}
            if c.get("Description"):
                p["short"] = c["Description"]
            if c.get("Lore"):
                p["long"] = c["Lore"]
            eid = K.class_entity(c["Id"])
            out.append(text("description", "class:" + c["Id"], p, [eid] if eid in known else []))
        for n, ph in enumerate(c.get("BossPhases") or []):
            if isinstance(ph, dict) and ph.get("Announce"):
                eid = K.class_entity(c["Id"])
                spk = eid if eid in known else "$boss"
                out.append(text("bark", f"bark:bossphase/{c['Id']}/{n}",
                                {"speaker": spk, "context": "boss phase announcement", "lines": [ph["Announce"]]},
                                [eid] if eid in known else [], [spk]))
    for i in content["items"]:
        if isinstance(i, dict) and i.get("Id") and i.get("Description"):
            eid = K.item_entity(i["Id"])
            out.append(text("description", "item:" + i["Id"], {"short": i["Description"]}, [eid] if eid in known else []))
    for a in content["armorsets"]:
        if a.get("Id") and a.get("Description"):
            eid = "armorset-" + K.slug(a["Id"])
            out.append(text("description", "armorset:" + a["Id"], {"short": a["Description"]}, [eid] if eid in known else []))
    for t in content["skilltrees"]:
        if t.get("Id") and t.get("Description"):
            out.append(text("description", "skilltree:" + t["Id"], {"short": t["Description"]}))
        for n in t.get("Nodes") or []:
            if n.get("Id") and n.get("Description"):
                out.append(text("description", "skillnode:" + n["Id"], {"short": n["Description"]}))
    for npc in K.NPCS:
        eid = K.character_entity(npc)
        out.append(text("description", "npc:" + npc, {"short": prose_map.NPC_SUMMARY[npc]}, [eid] if eid in known else []))
    out.extend(entity_descriptions(content, known))
    for q in content["quests"]:
        if not q.get("Id"):
            continue
        npc = K.npc_id_for(q.get("Giver"))
        giver = K.character_entity(npc) if npc else ("$party" if q.get("Giver") == "Party" else (q.get("Giver") or ""))
        p = {"title": q.get("Name") or q["Id"], "brief": q.get("Summary") or ""}
        if giver:
            p["giver"] = giver
        if q.get("TurnIn"):
            p["complete"] = q["TurnIn"]
        about = [giver, K.place_entity(q.get("Theme") or "")]
        obj = q.get("Objective") or {}
        if obj.get("Kind") == "Defeat" and obj.get("Id"):
            about.append(K.class_entity(obj["Id"]))
        out.append(text("quest", "quest:" + q["Id"], p, [a for a in about if a in known]))
    for g in content["glossary"]:
        if g.get("Id") and g.get("Text"):
            p = {"term": g.get("Term") or g["Id"], "definition": g["Text"]}
            if g.get("Aliases"):
                p["aliases"] = list(g["Aliases"])
            eid = K.concept_entity(g["Id"])
            out.append(text("glossary", "glossary:" + g["Id"], p, [eid] if eid in known else []))
    for u in content["caravan"]:
        if u.get("Id") and u.get("Barks"):
            eid = K.character_entity(u.get("Crew") or "")
            spk = eid if eid in known else "$crew"
            out.append(text("bark", "bark:caravan/" + u["Id"],
                            {"speaker": spk, "context": f"caravan: {u.get('Name') or u['Id']}", "lines": list(u["Barks"])},
                            [eid] if eid in known else [], [spk]))
    for m in content["mercenaries"]:
        cls = m.get("Class")
        if not cls:
            continue
        eid = K.class_entity(cls)
        spk = eid if eid in known else "$mercenary"
        for field, ctx in (("Pitch", "pitch"), ("Renewal", "renewal")):
            if m.get(field):
                out.append(text("bark", f"bark:merc/{cls}/{ctx}",
                                {"speaker": spk, "context": f"mercenary {ctx}", "lines": [m[field]]},
                                [eid] if eid in known else [], [spk]))
    for s in content["stages"]:
        for w, wave in enumerate(s.get("Waves") or []):
            if s.get("Id") and isinstance(wave, dict) and wave.get("SurpriseLine"):
                eid = K.class_entity(wave.get("SurpriseSpeaker") or "")
                spk = eid if eid in known else "$enemy"
                pid = K.place_entity(s["Id"])
                out.append(text("bark", f"bark:surprise/{s['Id']}/{w}",
                                {"speaker": spk, "context": "surprise wave", "lines": [wave["SurpriseLine"]]},
                                [a for a in (eid, pid) if a in known], [spk]))
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Seed GridGame's in-repo story text into prose-text/1 drafts.")
    ap.add_argument("out")
    ap.add_argument("--content", default=K.CONTENT_DIR)
    ap.add_argument("--universe", default=os.path.join("Data", "Prose", "gridgame.universe.json"))
    args = ap.parse_args(argv)
    content = prose_text.load_content_with_text(args.content)  # Phase 2: the words live in Content/Text
    known = set()
    if os.path.isfile(args.universe):
        known = {e["id"] for e in K.load(args.universe).get("entities") or []}
    docs = build(content, known)
    keys = K.game_keys(content)
    os.makedirs(args.out, exist_ok=True)
    counts = {}
    for d in docs:
        assert keys.get(d["key"]) == d["kind"], d["key"]
        with open(os.path.join(args.out, d["id"] + ".json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(d, f, ensure_ascii=False, indent=2)
            f.write("\n")
        counts[d["kind"]] = counts.get(d["kind"], 0) + 1
    print(f"prose_seed: {len(docs)} texts ({', '.join(f'{k} {v}' for k, v in sorted(counts.items()))}) -> {args.out}; "
          f"{len(keys) - len({d['key'] for d in docs})} game keys have no text to seed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
