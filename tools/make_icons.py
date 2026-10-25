"""
Every icon in the game — status effects, abilities/spells, items, UI cursor — as pixel art made from
game-icons.net silhouettes (CC BY 3.0; credits in Resources/Credits.txt and tools/icon_src/SOURCES.md).

    python tools/make_icons.py            # fetch any missing SVGs, then (re)build every icon
    python tools/make_icons.py --check    # fail if any ability/item/status has no icon mapping
    python tools/make_icons.py --missing  # build only the icons that have no PNG yet (new content)

Owner direction 2026-10-10: "icons that are memorable and identifiable … run through the pixel art process … a
continuous effort of building up graphics that match the aesthetic." Adding content = adding a line to
tools/icons.json (or an item rule below); this script does the rest.

Pipeline (same family as make_pixel_icons.py): rasterise the white SVG at 64 px -> paint it with a three-stop
vertical gradient from its palette (light top, base, dark bottom) -> shrink to 20 px -> hard alpha edge ->
8 colours, no dither -> 1 px dark outline -> centre on 22x22 -> x4 nearest-neighbour (88x88) + a .meta cloned from
Thumbs/slime00 (import settings come from GaidenImport in GaidenBuild.cs).

Output: Assets/Gaiden/Resources/{StatusIcons,AbilityIcons,ItemIcons,UI}/<id>.png
"""
import argparse
import json
import os
import re
import subprocess
import sys
import urllib.request
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Assets", "Gaiden", "Resources")
SRC = os.path.join(ROOT, "tools", "icon_src")
MANIFEST = os.path.join(ROOT, "tools", "icons.json")
TEMPLATE = os.path.join(RES, "Thumbs", "slime00.png.meta")
GROUP_DIR = {"status": "StatusIcons", "ability": "AbilityIcons", "item": "ItemIcons", "ui": "UI"}


def svg_path(ref):
    return os.path.join(SRC, ref.replace("/", "__") + ".svg")


def fetch(ref):
    dst = svg_path(ref)
    if os.path.exists(dst):
        return dst
    url = f"https://game-icons.net/icons/ffffff/transparent/1x1/{ref}.svg"
    with urllib.request.urlopen(url, timeout=30) as resp:
        data = resp.read()
    if not data.lstrip().startswith(b"<svg"):
        raise RuntimeError(f"not an svg: {ref}")
    os.makedirs(SRC, exist_ok=True)
    open(dst, "wb").write(data)
    return dst


def build(svg, colours, dst, flop=False, rot=0):
    light, base, dark = colours
    args = [
        "magick",
        "(", "-background", "none", "-density", "96", svg, "-resize", "64x64", "-gravity", "center", "-extent", "64x64", *(["-flop"] if flop else []),
        *(["-rotate", str(rot), "-gravity", "center", "-extent", "64x64"] if rot else []), ")",
        "(", "-size", "64x32", f"gradient:{light}-{base}", "-size", "64x32", f"gradient:{base}-{dark}", "-append", ")",
        "-compose", "SrcIn", "-composite", "-compose", "Over",
        "-filter", "Box", "-resize", "20x20",
        "-channel", "A", "-threshold", "40%", "+channel",
        "-dither", "None", "-colors", "8",
        "-background", "none", "-gravity", "center", "-extent", "22x22",
        "(", "+clone", "-alpha", "extract", "-morphology", "Dilate", "Diamond:1", "-background", "#120a14", "-alpha", "shape", ")",
        "-compose", "DstOver", "-composite",
        "-compose", "Over", "-filter", "point", "-resize", "400%", "-strip", "PNG32:" + dst,
    ]
    subprocess.run(args, check=True)


def write_meta(png):
    meta = png + ".meta"
    if os.path.exists(meta):
        return
    t = open(TEMPLATE, encoding="utf-8").read()
    t = re.sub(r"^guid: [0-9a-f]+", "guid: " + uuid.uuid4().hex, t, count=1, flags=re.M)
    # Keep the template's filter/compression as-is: GaidenImport.OnPreprocessTexture (GaidenBuild.cs) forces
    # bilinear + CompressedHQ on everything under Resources/, so writing point/uncompressed here only made
    # Unity rewrite the .meta on the next import and leave git dirty (GG-T197).
    open(meta, "w", encoding="utf-8", newline="\n").write(t)


def ensure_folder(path):
    os.makedirs(path, exist_ok=True)
    if not os.path.exists(path + ".meta"):
        open(path + ".meta", "w", newline="\n").write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


# ── Items: one rule set maps all 167 (and future) items to a silhouette + palette ──
WEAPON_SVG = {"Sword": "lorc/broadsword", "Greatsword": "delapouite/two-handed-sword", "Staff": "lorc/wizard-staff",
              "Bow": "delapouite/bow-arrow", "Hammer": "delapouite/warhammer", "Dagger": "lorc/plain-dagger",
              "Wand": "lorc/fairy-wand", "Spear": "lorc/spear-hook", "Axe": "lorc/battle-axe", "Mace": "delapouite/flanged-mace",
              # GG-T181's weapon roster (tools/weapons.py)
              "Club": "delapouite/wood-club", "Sickle": "delapouite/sickle", "Sling": "delapouite/sling",
              "Quarterstaff": "delapouite/bo", "Knife": "lorc/bowie-knife", "Javelin": "lorc/thrown-spear",
              "Rod": "lorc/crystal-wand", "Pitchfork": "delapouite/pitchfork", "Dart": "delapouite/dart",
              "Lantern": "lorc/lantern-flame", "Bolas": "lorc/bolas", "Chakram": "delapouite/chakram",
              "Knuckles": "delapouite/brass-knuckles", "Rapier": "lorc/stiletto", "Hatchet": "delapouite/hatchet",
              "Crossbow": "carl-olsen/crossbow", "Flail": "delapouite/flail", "Halberd": "delapouite/war-axe",
              "Whip": "lorc/whip", "Glaive": "delapouite/glaive", "Trident": "lorc/trident", "Katana": "delapouite/katana",
              "Scythe": "lorc/scythe", "Morningstar": "lorc/spiked-mace", "Tome": "lorc/book-cover",
              "Orb": "lorc/crystal-ball", "Totem": "delapouite/totem", "Scepter": "delapouite/winged-scepter",
              "Skullstaff": "delapouite/skull-staff"}
NAME_PALETTE = [("Rusty", "rust"), ("Iron", "iron"), ("Steel", "steel"), ("Bronze", "bronze"), ("Copper", "bronze"),
                ("Silver", "silver"), ("Gold", "gold"), ("Bloodstone", "crimson"), ("Phantom", "shadow"), ("Shadow", "shadow"),
                ("Flame", "fire"), ("Sunfire", "fire"), ("Crystal", "ice"), ("Serpent", "poison"), ("Rune", "arcane"),
                ("Mystic", "arcane"), ("Starfall", "holy"), ("Hunter", "wood"), ("War", "iron"), ("Jade", "jade"), ("Bone", "bone"),
                ("Dragon", "crimson"), ("Leather", "leather"), ("Padded", "cloth"), ("Chain", "iron"), ("Plate", "steel"),
                ("Wizard", "arcane"), ("Mage", "arcane"), ("Horned", "iron"), ("Wind", "teal"), ("Crown", "gold")]
ENCHANT_PALETTE = {"(Flame)": "fire", "(Frost)": "ice", "(Spark)": "spark", "(Shadow)": "shadow",
                   "(Poison)": "poison", "(Holy)": "holy"}


def weapon_base_id(iid):
    """eq_x_plus2 / eq_x_ench_flame -> eq_x: an upgrade or enchant variant shares its base weapon's palette."""
    return re.sub(r"_(plus\d|ench_[a-z]+)$", "", iid)


def item_rule(it, manifest):
    iid, name = it["Id"], it["Name"]
    if iid in manifest.get("item", {}):
        return manifest["item"][iid]
    if it["Type"] == "Equipment":
        if it.get("WeaponType"):
            svg = WEAPON_SVG.get(it["WeaponType"])
            if svg is None:
                return None  # unmapped WeaponType: report through the normal "NO ICON MAPPING" path, not a crash
        elif "Ring" in name or "Band" in name:
            svg = "delapouite/ring"
        elif "Amulet" in name or "Talisman" in name:
            svg = "lorc/gem-pendant"
        elif "Crown" in name:
            svg = "lorc/crown"
        elif "Helm" in name:
            svg = "lorc/visored-helm"
        elif "Hat" in name:
            svg = "lorc/pointy-hat"
        elif any(k in name for k in ("Boots", "Greaves", "Sabatons", "Runners")):
            svg = "lorc/boots"
        elif any(k in name for k in ("Legging", "Trousers")):
            svg = "delapouite/leg-armor"
        elif any(k in name for k in ("Glove", "Gauntlet")):
            svg = "delapouite/gloves"
        elif "Earring" in name:
            svg = "delapouite/drop-earrings"
        elif "Rob" in name:
            svg = "lorc/robe"
        elif "Vest" in name:
            svg = "lorc/leather-vest"
        else:
            svg = "lorc/breastplate"
        pal = (next((p for k, p in ENCHANT_PALETTE.items() if k in name), None)
               or manifest.get("weapon_palette", {}).get(weapon_base_id(iid))
               or next((p for k, p in NAME_PALETTE if k in name), "steel"))
        return [svg, pal]
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--missing", action="store_true", help="only build icons whose PNG doesn't exist yet")
    args = ap.parse_args()
    m = json.load(open(MANIFEST, encoding="utf-8"))
    palettes = m["palettes"]
    content = os.path.join(RES, "Content")
    abilities = json.load(open(os.path.join(content, "abilities.json"), encoding="utf-8"))
    items = json.load(open(os.path.join(content, "items.json"), encoding="utf-8"))
    buffs = json.load(open(os.path.join(content, "buffs.json"), encoding="utf-8"))

    jobs, missing = [], []
    for b in buffs:
        spec = m["status"].get(b["Id"])
        (jobs.append(("status", b["Id"], spec)) if spec else missing.append("status:" + b["Id"]))
    for extra in ("stunned",):
        jobs.append(("status", extra, m["status"][extra]))
    for a in abilities:
        spec = m["ability"].get(a["Id"])
        (jobs.append(("ability", a["Id"], spec)) if spec else missing.append("ability:" + a["Id"]))
    for it in items:
        spec = item_rule(it, m)
        (jobs.append(("item", it["Id"], spec)) if spec else missing.append("item:" + it["Id"]))
    for uid, spec in m["ui"].items():
        jobs.append(("ui", uid, spec))
    if missing:
        print("NO ICON MAPPING:", ", ".join(missing))
        sys.exit(1)
    if args.check:
        print(f"icon check OK ({len(jobs)} icons mapped)")
        return

    for g in GROUP_DIR.values():
        ensure_folder(os.path.join(RES, g))
    built = 0
    for group, iid, spec in jobs:
        ref, pal = spec[0], spec[1]
        opts = spec[2:]
        flop = "flop" in opts
        rot = next((int(o[4:]) for o in opts if isinstance(o, str) and o.startswith("rot:")), 0)
        svg = fetch(ref)
        dst = os.path.join(RES, GROUP_DIR[group], iid + ".png")
        if args.missing and os.path.exists(dst):
            continue
        build(svg, palettes[pal], dst, flop, rot)
        write_meta(dst)
        built += 1
    # credits: every silhouette used, by author
    refs = sorted({spec[0] for _, _, spec in jobs})
    authors = sorted({r.split("/")[0] for r in refs})
    with open(os.path.join(SRC, "SOURCES.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Icon sources\n\nSilhouettes from game-icons.net (https://game-icons.net), CC BY 3.0 "
                "(https://creativecommons.org/licenses/by/3.0/), pixelated and coloured by tools/make_icons.py.\n\n")
        f.write("Authors: " + ", ".join(authors) + "\n\n")
        for r in refs:
            f.write(f"- {r}\n")
    print(f"icons: {built} built from {len(refs)} silhouettes by {len(authors)} authors")


if __name__ == "__main__":
    main()
