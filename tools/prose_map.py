#!/usr/bin/env python3
"""GG-T355: GridGame's mapper for the Prose conduit (docs/PROSE.md; Prose docs/CONDUIT.md section 6).

    python tools/prose_map.py [--content Assets/Gaiden/Resources/Content] [--out Data/Prose/gridgame.universe.json]

Reads the gameplay content JSON (read only) and writes the Universe Interchange file (RFC 0007) the consumer kit pushes
to Prose's `gridgame` universe. Deterministic: the same content gives the same bytes.

Entities (ids are the game's, stable and type-prefixed; a rename changes `name`, never `id`):

    class-<id>       every class in classes.json: team, role, boss, tags, home tree; member-of its hero faction;
                     an enemy is found-in each campaign theme whose stages field it; a boss guards its finale stage
    faction-<id>     every faction in factions.json (the hero factions)
    place-<theme>    every campaign theme (chapter order, main or postgame); road-to the next theme in campaign
                     order, contains its stages, a postgame region extends the main region its stages belong to,
                     and ADJACENCY's stated facts (the Still foot-of Halcyon, the Cathedral top-of it)
    place-<stage>    every campaign stage, part-of its theme (test stages are left out)
    character-<id>   the camp NPCs (innkeeper, blacksmith, alchemist, enchantress, merchant) and the caravan crew

Every place, faction and character entity has a description key (prose_keys.description_keys, GG-T374): its
description text is `about` it, the way a class's is; main() refuses to write a file with one that has none.
    concept-<id>     every glossary term
    armorset-<id>    every armor set in armorsets.json
    item-<id>        every item in items.json (gear, consumables, materials); a set piece is part-of its armorset

No balance numbers cross: no stats, levels, prices, rewards, waves or counts. Summaries are placeholder canon taken
from the gameplay records' own words; Prose owns the words from here on.

The universe record (setting, logline, rules) is the owner's DECIDED premise of 2026-11-07, "A War in Heaven" (v4),
which supersedes docs/STORY.md's retired "Under Morrow" (v1). Prose holds the story; this is only its seed.

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
import prose_text  # noqa: E402

GENERATOR = "tools/prose_map.py"
MAPPER_VERSION = "1"

UNIVERSE_RECORD = {
    "id": K.UNIVERSE,
    "name": "GridGame",
    "tagline": "A War in Heaven",
    "setting": (
        "A world held at the edge against Morrow, the hungry thing from Outside, by the Bright Host: an army of "
        "celestial beings, demigods, saints and legendary heroes, winged and armoured in light. The Host burns essence, "
        "the living light in people and places, and the Pale Concord tithes it from every hold in white domes called "
        "Stills while the Choir sings it skyward. Every tithe shortens a life and dims a place; once-splendid magical "
        "cities are cold ruins where the last magic hides."
    ),
    "logline": (
        "The war in heaven is being lost: celestials fall as streaks of light, their bodies rain down, and Morrow's "
        "beasts come through the breaches. A crew of tithe-runaways from Saltmoss, in a stolen Concord caravan, learn "
        "that the Hierarch on the Concord's throne is a celestial deserter drinking the tithe meant for the war, and "
        "that essence given freely burns brighter than essence taken."
    ),
    "rules": [
        "Essence is the living light in people and places; magic is made of it and celestials burn it to fight.",
        "Every tithe shortens a life and dims a place; drained places go cold and their magic hides in the ruins.",
        "The Host is failing: there is not enough essence left, and its defenders die, retreat or are corrupted.",
        "Where a defender falls the line breaches, and Morrow's beasts come through the holes.",
        "An Anchor is a siege-gate where celestial power touches ground; when its angel dies the gate goes dark.",
        "Essence given freely burns brighter than essence taken; where it is given back, the Bloom grows.",
        "The moons in the sky are only sky: no falling moon, no gravity, no stars as plot.",
    ],
}

# Camp NPCs: short functional roles (the owner's brief, 2026-11-07). Their story lives in Prose.
NPC_SUMMARY = {
    "innkeeper": "Saltmoss's tavern-keeper, who hid the crew on tithe day and came along with the caravan.",
    "blacksmith": "A Free Hold armourer who travels with the caravan and refuses to forge for the Captain.",
    "alchemist": "A Street splice-cutter who travels with the caravan and knows exactly what grafts cost.",
    "enchantress": "A Choir runaway who travels with the caravan; she still hears the hymn and won't sing it.",
    "merchant": "A Lantern Road trader who follows the caravan; sells to everyone, informs on no one.",
}
NPC_PORTRAIT_FALLBACK = {"merchant": "courier", "innkeeper": "pandagirl", "blacksmith": "tinkerer",
                         "alchemist": "alchemist", "enchantress": "whitewitch"}


# Spatial facts the campaign states (theme id -> [(relation, theme id)]): the Still stands at the foot of Halcyon's
# spire, where the beams come down; the Choir's Cathedral stands at its top; the Underspire lies beneath Halcyon.
ADJACENCY = {
    "still": [("foot-of", "cityruins")],
    "cathedral": [("top-of", "cityruins")],
    "cityruins2": [("beneath", "cityruins")],
}


def first_paragraph(text: str) -> str:
    return (text or "").strip().split("\n\n", 1)[0].strip()


FACTION_SUMMARY = "A hero faction; its people can join the caravan as wanderers and recruits."


def stage_summary(theme: dict) -> str:
    return f"A battlefield of {theme.get('Name') or theme['Id']}."


def crew_summary(u: dict) -> str:
    return f"The caravan's {u.get('CrewRole') or 'crew'}, who keeps the {u.get('Name') or u.get('Id')} wagon."


def is_boss(c: dict) -> bool:
    return "Boss" in (c.get("Tags") or []) or bool(c.get("BossPhases"))


def npc_portraits(dialogues: list) -> dict:
    out = {}
    for d in dialogues:
        for s in d.get("Speakers") or []:
            npc = K.npc_id_for(s.get("Name")) if s.get("Name") else None
            if npc and s.get("Portrait"):
                out.setdefault(npc, s["Portrait"])
    return out


def build(content: dict) -> dict:
    classes = content["classes"]
    class_ids = {c["Id"] for c in classes if c.get("Id")}
    camp = content["campaign"] if isinstance(content["campaign"], dict) else {}
    themes = [t for t in camp.get("Themes") or [] if t.get("Id")]
    stages = {s["Id"]: s for s in content["stages"] if s.get("Id")}
    entities = []

    # --- places: themes, then their stages
    stage_theme = {}
    main = True
    for idx, t in enumerate(themes):
        # Postgame themes are the ones after the theme the Ending's stage closes.
        details = {"kind": "region", "chapter": idx + 1, "campaign": "main" if main else "postgame"}
        if t.get("Backgrounds"):
            details["art"] = "Backgrounds/" + t["Backgrounds"]
        entities.append({
            "id": K.place_entity(t["Id"]), "type": "place", "name": t.get("Name") or t["Id"],
            "summary": first_paragraph(t.get("Crawl")), "details": details,
            "tags": ["region", "main" if main else "postgame"],
        })
        for sid in t.get("Stages") or []:
            stage_theme[sid] = t["Id"]
        if camp.get("Ending", {}).get("Stage") in (t.get("Stages") or []):
            main = False
    # Travel geography: road-to along the campaign order (main route, then on into the postgame), contains from a
    # region to its stages, extends from a postgame region to the main region its stages belong to (stages.json
    # Theme), and the few spatial facts the campaign states outright (ADJACENCY).
    by_theme = {e["id"]: e for e in entities}
    for prev, nxt in zip(themes, themes[1:]):
        by_theme[K.place_entity(prev["Id"])].setdefault("relations", []).append(
            {"to": K.place_entity(nxt["Id"]), "kind": "road-to"})
    theme_ids = {t["Id"] for t in themes}
    for t in themes:
        ent = by_theme[K.place_entity(t["Id"])]
        for sid in t.get("Stages") or []:
            if sid in stages and not stages[sid].get("IsTest"):
                ent.setdefault("relations", []).append({"to": K.place_entity(sid), "kind": "contains"})
        homes = sorted({stages[s].get("Theme") for s in t.get("Stages") or [] if s in stages} - {t["Id"], None})
        for h in homes:
            if h in theme_ids:
                ent.setdefault("relations", []).append({"to": K.place_entity(h), "kind": "extends"})
        for kind, other in ADJACENCY.get(t["Id"], ()):
            if other in theme_ids:
                ent.setdefault("relations", []).append({"to": K.place_entity(other), "kind": kind})
    for t in themes:
        for i, sid in enumerate(t.get("Stages") or []):
            s = stages.get(sid)
            if not s or s.get("IsTest"):
                continue
            entities.append({
                "id": K.place_entity(sid), "type": "place", "name": s.get("Name") or sid,
                "summary": stage_summary(t),
                "details": {"kind": "stage", "theme": t["Id"], "order": i + 1},
                "relations": [{"to": K.place_entity(t["Id"]), "kind": "part-of"}],
                "tags": ["stage"],
            })

    # --- where each enemy class fights, and which boss guards which stage
    found_in = {}
    for sid, tid in stage_theme.items():
        s = stages.get(sid)
        if not s:
            continue
        for w in s.get("Waves") or []:
            for e in w.get("Enemies") or []:
                if e.get("Class") in class_ids:
                    found_in.setdefault(e["Class"], set()).add(tid)
    guards = {}
    for d in content["dialogues"]:
        trig = d.get("Trigger") or {}
        if trig.get("Kind") != "StageStart" or trig.get("Id") not in stage_theme:
            continue
        for s in d.get("Speakers") or []:
            if s.get("Id") == "boss" and s.get("Class") in class_ids:
                guards.setdefault(s["Class"], set()).add(trig["Id"])

    # --- factions
    faction_of = {}
    for f in content["factions"]:
        if not f.get("Id"):
            continue
        for h in f.get("Heroes") or []:
            faction_of.setdefault(h, f["Id"])
        entities.append({
            "id": K.faction_entity(f["Id"]), "type": "faction", "name": f.get("Name") or f["Id"],
            "summary": FACTION_SUMMARY,
            "details": {"heroFaction": True},
            "tags": ["faction"],
        })
    faction_ids = {f["Id"] for f in content["factions"] if f.get("Id")}

    # --- classes
    for c in classes:
        cid = c.get("Id")
        if not cid:
            continue
        hero = c.get("Team") == "Hero"
        details = {"team": c.get("Team") or "", "boss": is_boss(c), "traits": list(c.get("Tags") or [])}
        if c.get("Role"):
            details["role"] = c["Role"]
        if c.get("HomeTree"):
            details["homeTree"] = c["HomeTree"]
        if c.get("Portrait"):
            details["art"] = "Portraits/" + c["Portrait"]
        rel = []
        if faction_of.get(cid) in faction_ids:
            rel.append({"to": K.faction_entity(faction_of[cid]), "kind": "member-of"})
        for tid in sorted(found_in.get(cid, ())):
            rel.append({"to": K.place_entity(tid), "kind": "found-in"})
        for sid in sorted(guards.get(cid, ())):
            rel.append({"to": K.place_entity(sid), "kind": "guards"})
        tags = ["hero" if hero else "enemy"] + (["boss"] if details["boss"] else [])
        ent = {"id": K.class_entity(cid), "type": "class", "name": c.get("Name") or cid,
               "summary": (c.get("Description") or "").strip(), "details": details, "tags": tags}
        if rel:
            ent["relations"] = rel
        entities.append(ent)

    # --- characters: camp NPCs and the caravan crew
    portraits = npc_portraits(content["dialogues"])
    for npc, name in K.NPCS.items():
        entities.append({
            "id": K.character_entity(npc), "type": "character", "name": name,
            "summary": NPC_SUMMARY[npc],
            "details": {"role": "camp NPC", "npcId": npc,
                        "art": "Portraits/" + portraits.get(npc, NPC_PORTRAIT_FALLBACK[npc])},
            "tags": ["camp-npc"],
        })
    for u in content["caravan"]:
        if not u.get("Crew"):
            continue
        entities.append({
            "id": K.character_entity(u["Crew"]), "type": "character", "name": u["Crew"],
            "summary": crew_summary(u),
            "details": {"role": "caravan crew", "crewRole": u.get("CrewRole") or "", "upgrade": u.get("Id") or ""},
            "tags": ["caravan-crew"],
        })

    # --- glossary concepts
    for g in content["glossary"]:
        if not g.get("Id"):
            continue
        details = {}
        if g.get("Aliases"):
            details["aliases"] = list(g["Aliases"])
        if g.get("LinkKind"):
            details["mechanic"] = f"{g['LinkKind']}:{g.get('LinkId') or ''}"
        entities.append({"id": K.concept_entity(g["Id"]), "type": "concept", "name": g.get("Term") or g["Id"],
                         "summary": (g.get("Text") or "").strip(), "details": details, "tags": ["glossary"]})

    # --- armor sets, then every item (gear, consumables, materials); a set's pieces are part-of the set
    items = [i for i in content["items"] if isinstance(i, dict) and i.get("Id")]
    item_ids = {i["Id"] for i in items}
    set_of = {}
    for a in content["armorsets"]:
        if not a.get("Id"):
            continue
        for p in a.get("Pieces") or []:
            set_of.setdefault(p, a["Id"])
        entities.append({"id": "armorset-" + K.slug(a["Id"]), "type": "armorset", "name": a.get("Name") or a["Id"],
                         "summary": (a.get("Description") or "").strip(),
                         "details": {"weight": a.get("Weight") or ""}, "tags": ["armorset"]})
    quest_rewards = {q.get("RewardItem") for q in content["quests"]}
    milestones = {t.get("MilestoneReward") for t in themes}
    for i in items:
        details = {"itemType": i.get("Type") or "", "rarity": i.get("Rarity") or ""}
        if i.get("Slot") and i["Slot"] != "None":
            details["slot"] = i["Slot"]
        if i.get("WeaponType"):
            details["weaponType"] = i["WeaponType"]
        tags = ["item"]
        if i["Id"] in quest_rewards:
            tags.append("quest-reward")
        if i["Id"] in milestones:
            tags.append("milestone-reward")
        ent = {"id": K.item_entity(i["Id"]), "type": "item", "name": i.get("Name") or i["Id"],
               "summary": (i.get("Description") or "").strip(), "details": details, "tags": tags}
        if set_of.get(i["Id"]):
            ent["relations"] = [{"to": "armorset-" + K.slug(set_of[i["Id"]]), "kind": "part-of"}]
        entities.append(ent)

    # Disambiguate the (rare) slug collisions deterministically: -2, -3 (docs/CONDUIT.md section 2).
    seen = {}
    for e in entities:
        n = seen.get(e["id"], 0)
        seen[e["id"]] = n + 1
        if n:
            e["id"] = f"{e['id']}-{n + 1}"

    return {
        "universe": dict(UNIVERSE_RECORD),
        "source": {"consumer": K.UNIVERSE, "generator": GENERATOR, "version": MAPPER_VERSION},
        "entities": entities,
    }


DESCRIBED_TYPES = ("place", "faction", "character")


def undescribed_entities(doc: dict, content: dict) -> list:
    """GG-T374: every place, faction and character entity this mapper emits must have a description key
    (place:<theme|stage>, faction:<id>, npc:<id>, character:<crew>), the text a player can look the entity up by;
    the ones without one, sorted (should be empty)."""
    keys = K.description_keys(content)
    return sorted(e["id"] for e in doc["entities"] if e["type"] in DESCRIBED_TYPES and e["id"] not in keys)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="GridGame's Prose mapper (docs/PROSE.md).")
    ap.add_argument("--content", default=K.CONTENT_DIR)
    ap.add_argument("--out", default=os.environ.get("PROSE_KIT_UNIVERSE_FILE",
                                                    os.path.join("Data", "Prose", "gridgame.universe.json")))
    args = ap.parse_args(argv)
    if not os.path.isdir(args.content):
        print(f"prose_map: no content folder at {args.content}", file=sys.stderr)
        return 2
    content = prose_text.load_content_with_text(args.content)  # Phase 2: story text comes from Content/Text
    doc = build(content)
    undescribed = undescribed_entities(doc, content)
    if undescribed:
        print("prose_map: entities with no description key (tools/prose_keys.py description_keys): "
              + ", ".join(undescribed[:20]), file=sys.stderr)
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")
    by_type = {}
    for e in doc["entities"]:
        by_type[e["type"]] = by_type.get(e["type"], 0) + 1
    edges = sum(len(e.get("relations") or []) for e in doc["entities"])
    print(f"prose_map: {len(doc['entities'])} entities ({', '.join(f'{k} {v}' for k, v in sorted(by_type.items()))}), "
          f"{edges} relations -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
