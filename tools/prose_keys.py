#!/usr/bin/env python3
"""GG-T355: GridGame's side of the Prose conduit, the parts the mapper, the seed and the applier share
(docs/PROSE.md; Prose docs/CONDUIT.md, RFC 0017).

- Loading the content JSON (read only; never written here).
- Stable ids: entity ids (`class-<id>`, `place-<theme>`, ...) and text ids (`conversation-<dialogue>`, ...).
- The text KEY scheme: the handle the game will look each text up by (Phase 2's loader join).

    kind          key                              text id                              from
    conversation  dialogue:<dialogueId>            conversation-<dialogueId>            dialogues.json (every dialogue)
    crawl         crawl:prologue                   crawl-prologue                       campaign.json Prologue.Crawl
    crawl         crawl:ending                     crawl-ending                         campaign.json Ending.Crawl
    crawl         crawl:<themeId>                  crawl-<themeId>                      campaign.json Themes[].Crawl
    crawl         crawl:dialogue/<dialogueId>      crawl-dialogue-<dialogueId>          dialogues.json Crawl (interludes)
    description   class:<classId>                  description-class-<classId>          classes.json Description (short) / Lore (long)
                                                                                        (hero blurbs, enemy and Bestiary entries)
    description   item:<itemId>                    description-item-<itemId>            items.json Description (every item)
    description   armorset:<setId>                 description-armorset-<setId>         armorsets.json Description
    description   skilltree:<treeId>               description-skilltree-<treeId>       skilltrees.json Description
    description   skillnode:<nodeId>               description-skillnode-<nodeId>       skilltrees.json Nodes[].Description
    description   npc:<npcId>                      description-npc-<npcId>              the camp NPC's blurb (no gameplay field yet)
    quest         quest:<questId>                  quest-<questId>                      quests.json Name/Giver/Summary/TurnIn
    glossary      glossary:<glossaryId>            glossary-<glossaryId>                glossary.json Term/Aliases/Text
    bark          bark:caravan/<upgradeId>         bark-caravan-<upgradeId>             caravan.json Barks (the wagon's crew)
    bark          bark:merc/<classId>/pitch        bark-merc-<classId>-pitch            mercenaries.json Pitch
    bark          bark:merc/<classId>/renewal      bark-merc-<classId>-renewal          mercenaries.json Renewal
    bark          bark:bossphase/<classId>/<n>     bark-bossphase-<classId>-<n>         classes.json BossPhases[n].Announce
    bark          bark:surprise/<stageId>/<wave>   bark-surprise-<stageId>-<wave>       stages.json Waves[wave].SurpriseLine

Stays in the game (UI and mechanics, not story): names (class, item, ability, theme, stage, quest-board labels;
Prose holds them as entity names, the game owns the id), ability Description and Trigger.Callout (mechanical floats),
class Card and Support.Label, item PassiveName/PassiveDescription (numbers), caravan Description/Levels[].Text,
pickups, bounties, encounters, the battle tutorial (BattleScreen.Tutorial.cs) and Credits.txt.

Keys keep the game's own ids verbatim (underscores and all); text and entity ids are those ids slugged
(lowercase, `_` -> `-`). A key exists exactly when the gameplay record it names exists, so `game_keys()` is also
how the applier tells an orphan (a text no gameplay record would look up) from a text to apply.

Standard library only.
"""

from __future__ import annotations

import json
import os
import re

CONTENT_DIR = os.path.join("Assets", "Gaiden", "Resources", "Content")
UNIVERSE = "gridgame"

# Camp NPCs: the game's npc id (Talk trigger "npc:<id>", JoinNpc) -> the name dialogues and quests use for them.
NPCS = {
    "innkeeper": "Innkeeper",
    "blacksmith": "Blacksmith",
    "alchemist": "Alchemist",
    "enchantress": "Enchantress",
    "merchant": "Lantern Merchant",
}
NPC_BY_NAME = {v: k for k, v in NPCS.items()}

# Choice tags the game's dialogue logic keys on (prose.config.json "tags"):
#   right-answer  the answer that speaks to them: a wanderer's "heard" branch (Wanderers.RightAnswer) or the
#                 Advice-quiz answer that earns a skill point (GrantSkillPoint).
#   conditional   a choice shown only when its gameplay Conditions hold (gold, a flag, the roster).
TAGS = ("right-answer", "conditional")

_SLUG_BAD = re.compile(r"[^a-z0-9]+")


def slug(s: str) -> str:
    """A stable id piece: lowercase, every run of other characters one '-', no edge dashes."""
    return _SLUG_BAD.sub("-", (s or "").lower()).strip("-")


def load(path: str):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def load_content(content_dir: str = CONTENT_DIR) -> dict:
    """The content files the conduit reads, by name ('classes', 'dialogues', ...). A missing file is an empty list
    (campaign: an empty object), so a partial fixture still loads."""
    out = {}
    for name in ("classes", "factions", "campaign", "stages", "dialogues", "quests", "glossary", "caravan",
                 "mercenaries", "items", "armorsets", "skilltrees"):
        path = os.path.join(content_dir, name + ".json")
        if os.path.isfile(path):
            out[name] = load(path)
        else:
            out[name] = {} if name == "campaign" else []
    return out


# ------------------------------------------------------------------------------------------------ ids

def class_entity(class_id: str) -> str:
    return "class-" + slug(class_id)


def faction_entity(faction_id: str) -> str:
    return "faction-" + slug(faction_id)


def place_entity(theme_or_stage: str) -> str:
    return "place-" + slug(theme_or_stage)


def character_entity(name_or_id: str) -> str:
    return "character-" + slug(name_or_id)


def concept_entity(glossary_id: str) -> str:
    return "concept-" + slug(glossary_id)


def item_entity(item_id: str) -> str:
    return "item-" + slug(item_id)


def npc_id_for(name_or_id: str):
    """The camp NPC id for a speaker/giver name ('Lantern Merchant') or id ('merchant', 'smith'), else None."""
    if not name_or_id:
        return None
    if name_or_id in NPC_BY_NAME:
        return NPC_BY_NAME[name_or_id]
    if name_or_id in NPCS:
        return name_or_id
    if name_or_id == "smith":
        return "blacksmith"
    return None


# ------------------------------------------------------------------------------------------------ keys

def game_keys(content: dict) -> dict:
    """Every text key the gameplay data defines -> its kind. A text with any other key is an orphan."""
    keys = {}
    for d in content.get("dialogues") or []:
        if d.get("Id"):
            keys["dialogue:" + d["Id"]] = "conversation"
            if d.get("Crawl"):
                keys["crawl:dialogue/" + d["Id"]] = "crawl"
    camp = content.get("campaign") or {}
    if isinstance(camp, dict):
        if isinstance(camp.get("Prologue"), dict):
            keys["crawl:prologue"] = "crawl"
        if isinstance(camp.get("Ending"), dict):
            keys["crawl:ending"] = "crawl"
        for t in camp.get("Themes") or []:
            if t.get("Id"):
                keys["crawl:" + t["Id"]] = "crawl"
    for c in content.get("classes") or []:
        if c.get("Id"):
            keys["class:" + c["Id"]] = "description"
            for n, ph in enumerate(c.get("BossPhases") or []):
                if isinstance(ph, dict) and ph.get("Announce"):
                    keys[f"bark:bossphase/{c['Id']}/{n}"] = "bark"
    for i in content.get("items") or []:
        if isinstance(i, dict) and i.get("Id"):
            keys["item:" + i["Id"]] = "description"
    for a in content.get("armorsets") or []:
        if a.get("Id"):
            keys["armorset:" + a["Id"]] = "description"
    for t in content.get("skilltrees") or []:
        if t.get("Id"):
            keys["skilltree:" + t["Id"]] = "description"
            for n in t.get("Nodes") or []:
                if n.get("Id"):
                    keys["skillnode:" + n["Id"]] = "description"
    for npc in NPCS:
        keys["npc:" + npc] = "description"
    for s in content.get("stages") or []:
        for w, wave in enumerate(s.get("Waves") or []):
            if s.get("Id") and isinstance(wave, dict) and wave.get("SurpriseLine"):
                keys[f"bark:surprise/{s['Id']}/{w}"] = "bark"
    for q in content.get("quests") or []:
        if q.get("Id"):
            keys["quest:" + q["Id"]] = "quest"
    for g in content.get("glossary") or []:
        if g.get("Id"):
            keys["glossary:" + g["Id"]] = "glossary"
    for u in content.get("caravan") or []:
        if u.get("Id") and u.get("Barks"):
            keys["bark:caravan/" + u["Id"]] = "bark"
    for m in content.get("mercenaries") or []:
        if m.get("Class"):
            keys[f"bark:merc/{m['Class']}/pitch"] = "bark"
            keys[f"bark:merc/{m['Class']}/renewal"] = "bark"
    return keys


def text_id(kind: str, key: str) -> str:
    """The text id for a key: the kind, then the key's game id(s) slugged (class texts keep 'class')."""
    scheme, _, rest = key.partition(":")
    if kind == "conversation":
        return "conversation-" + slug(rest)
    if kind == "description":
        return f"description-{slug(scheme)}-{slug(rest)}"
    return f"{kind}-{slug(rest)}"
