"""
"There is always a way" (GG-T371, BIBLE §3.3.1): the Merchant's elemental flasks and board tools.

Owner 2026-11-08: "always a way to defeat them (cast fire against ice, be able to buy a consumable with elemental
effects in case your party doesn't have what it needs specced into the build, there is always a way)."

One flask per element (deals it in a small plus and may apply its status), Purifying Salts (wash residue off the
board: slime trails, mire, quicksand, poison clouds) and a Disarm Kit (pull mines, snares and caltrops). Every one is
SoldByVendor from the first expedition, priced against the potions (Healing Potion 25, Flame Oil 70, Hand Grenade 90).
Each item fires an Item ability; tile tools name an elements.json reaction set (AbilityDef.TileReaction).

    python tools/counter_items.py      # (re)write the rows into items.json + abilities.json, and the descriptions
                                       # into Content/Text (seed) + Data/Prose/drafts (GG-T355, docs/PROSE.md)

Idempotent: rows with these ids are replaced in place, new ones appended. Item names are gameplay labels; the
descriptions are Prose text (description kind, key item:<id>) and never go into items.json.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTENT = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

STATS = {k: 0 for k in ("Strength", "Vitality", "Agility", "Speed", "Stamina", "Intelligence", "Wisdom", "Luck")}

# id, name, element, damage, status, status chance, price, vfx (cast, projectile, impact, linger), description (Prose seed)
FLASKS = [
    ("flask_fire", "Fire Flask", "Fire", 20, "burning", 1.0, 55, ("FireRain", "Fireball", "PuffyExplosion", "Flame"),
     "A stoppered bottle of lamp-black oil that bursts into flame where it lands. Fire thaws the frozen and burns slime away."),
    ("flask_frost", "Frost Flask", "Ice", 16, "frozen", 0.5, 60, (None, "IceSparkle", "IcyWind", "IceSparkle"),
     "Rime-sealed glass that shatters into a killing cold. Ice answers fire, and freezes mire and quicksand solid."),
    ("flask_storm", "Storm Flask", "Lightning", 20, None, 1.0, 55, ("RayBlast", "LightningStrike", "ShockBolt", None),
     "A jar of bottled thunder. It strikes hardest on the soaked and sets off shock mines harmlessly."),
    ("flask_venom", "Venom Flask", "Poison", 12, "poisoned", 1.0, 45, ("ToxicCloud", "AcidSplash", "AcidSplash", "PoisonCloud"),
     "Swamp venom in a thin clay shell. It bursts into a green haze that sickens anything that breathes it."),
    ("flask_holy", "Blessed Flask", "Holy", 20, None, 1.0, 55, (None, None, None, None),
     "Water drawn from a shrine spring and sealed with a prayer. The dark and the dead flinch from it; it clears poison clouds."),
    ("flask_shadow", "Shadow Flask", "Dark", 20, "blinded", 0.35, 55, (None, None, None, None),
     "Ink-dark smoke trapped in black glass. It bites the holy and the arcane, and may leave its victims blinded."),
    ("flask_arcane", "Aether Flask", "Arcane", 20, "silenced", 0.35, 55, ("PinkSpark", "PinkDust", "SilenceMute", None),
     "Raw aether, humming in a crystal vial. It slips past armor plates and may leave a caster silenced."),
]

STATUS_WORD = {"burning": "[Burning]", "frozen": "[Frozen]", "poisoned": "[Poisoned]", "blinded": "[Blinded]", "silenced": "[Silenced]"}
CHANCE_WORD = [(0.15, "[slight]"), (0.27, "[small]"), (0.42, "[fair]"), (0.62, "[good]")]

# id, name, reaction set, price, vfx, description, ability description
TOOLS = [
    ("cons_purifying_salts", "Purifying Salts", "cleanse", 30, ("GoldSparkle", None, "AntidoteSparkle", None),
     "Coarse white salt blessed at a wayside shrine. Scattered over fouled ground, it washes away slime, mire, quicksand and poison clouds.",
     "Scatter salts over a 3×3 area: clears slime trails, mire, quicksand and poison clouds."),
    ("cons_disarm_kit", "Disarm Kit", "disarm", 40, (None, None, None, None),
     "Pliers, wire and a steady hand in a leather roll. Pulls the teeth of mines, snares and caltrops before anyone steps on them.",
     "Disarm a 3×3 area: removes shock mines, venom snares and caltrops."),
]


def chance_word(p):
    for limit, word in CHANCE_WORD:
        if p <= limit:
            return word + " chance of "
    return ""


def ability_row(aid, name, desc, element, dmg, status, chance, shape, radius, filt, vfx, reaction=None):
    cast, proj, impact, linger = vfx
    row = {"Id": aid, "Name": name, "Description": desc, "Kind": "Item", "Cost": 0, "CastTime": 0.0, "CooldownTurns": 0,
           "Shape": shape, "Radius": radius, "Mode": "PickTile", "Filter": filt, "Motion": "Bezier", "DamageType": element,
           "BaseDamage": dmg, "BaseHeal": 0, "ApplyBuff": status, "ApplyBuffChance": chance, "RemovesDebuffs": False,
           "StealsMana": False, "HastenU": 0.0, "IsTeleport": False, "Icon": None, "CastVfx": cast, "ProjectileVfx": proj,
           "ImpactVfx": impact, "LingerVfx": linger, "Sfx": None}
    if reaction:
        row["TileReaction"] = reaction
    return row


def item_row(iid, name, price, ability, element):
    return {"Id": iid, "Name": name, "Type": "Consumable", "Rarity": "Common", "Slot": "None", "WeaponType": None,
            "BaseCost": price, "SellValue": -1, "MaxStack": 5, "Durability": 0, "BaseHealing": 0, "BaseDamage": 0,
            "Stats": dict(STATS), "BattleStartManaOrbs": 0, "OnUseAbility": ability, "Resistances": {},
            "SoldByVendor": True, "Icon": None, "DamageType": element}


def rows():
    abilities, items, story = [], [], {}
    for iid, name, element, dmg, status, chance, price, vfx, desc in FLASKS:
        aid = iid + "_burst"
        text = f"Shatter a flask: {dmg} {element.lower()} damage to every foe in a plus"
        if status:
            text += " and a " + chance_word(chance) + STATUS_WORD[status] if chance < 1.0 else " and " + STATUS_WORD[status]
        abilities.append(ability_row(aid, name, text + ".", element, dmg, status, chance, "Cross", 1, "EnemyOnly", vfx))
        items.append(item_row(iid, name, price, aid, element))
        story[iid] = desc
    for iid, name, reaction, price, vfx, desc, adesc in TOOLS:
        aid = iid.replace("cons_", "") + "_use"
        abilities.append(ability_row(aid, name, adesc, "Physical", 0, None, 1.0, "Square", 1, "Any", vfx, reaction))
        items.append(item_row(iid, name, price, aid, "Physical"))
        story[iid] = desc
    return abilities, items, story


def upsert(existing, new):
    index = {r["Id"]: i for i, r in enumerate(existing)}
    for r in new:
        if r["Id"] in index:
            existing[index[r["Id"]]] = r
        else:
            existing.append(r)
    return existing


def main():
    abilities, items, story = rows()
    path_a, path_i = os.path.join(CONTENT, "abilities.json"), os.path.join(CONTENT, "items.json")
    all_a = upsert(json.load(open(path_a, encoding="utf-8")), abilities)
    all_i = upsert(json.load(open(path_i, encoding="utf-8")), items)
    with open(path_a, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(all_a, indent=1, ensure_ascii=False) + "\n")
    with open(path_i, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(all_i, indent=2, ensure_ascii=True) + "\n")
    seeded = seed_descriptions(story)
    print(f"counter items: {len(items)} items, {len(abilities)} abilities; descriptions: {seeded['seeded']} seeded, "
          f"{seeded['kept']} already from Prose; drafts in Data/Prose/drafts")


def seed_descriptions(story):
    """The items' descriptions as Prose drafts and Content/Text seed entries (docs/PROSE.md, as tools/weapons.py does)."""
    import prose_keys
    import prose_seed
    import prose_text
    universe = os.path.join(ROOT, "Data", "Prose", "gridgame.universe.json")
    known = {e["id"] for e in prose_keys.load(universe).get("entities") or []} if os.path.isfile(universe) else set()
    docs = [prose_seed.text("description", "item:" + iid, {"short": d},
                            [prose_keys.item_entity(iid)] if prose_keys.item_entity(iid) in known else [])
            for iid, d in sorted(story.items())]
    return prose_text.upsert_seed(docs, os.path.join(CONTENT, "Text"), os.path.join(ROOT, "Data", "Prose", "drafts"))


if __name__ == "__main__":
    main()
