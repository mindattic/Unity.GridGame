"""
Reproducible art/audio import from the GridGame2026 reference project into GridGameGaiden.

    python tools/import_art.py [--src D:/Projects/MindAttic/GridGame2026/Assets]

Produces (under Assets/Gaiden/Resources/):
  Portraits/<classId>.png    full-body art, max 640 px tall: the one image per unit. Every thumbnail (board token,
                             timeline icon, cards) is a zoom + offset into it, kept in classes.json as the class's
                             "Thumbnail" and framed in tools/ThumbnailStudio (GG-T220) - no thumb images are cut.
  Backgrounds/<set>_<NN>.jpg every GridGame2026 biome backdrop at its original resolution (1536x3072), JPEG q95
                             (PNG if it had transparency), named after its STORY.md region and numbered from 01
                             (owner 2026-11-07, GG-T353: "make sure the source files for the Backgrounds all get
                             proper naming to identify them to their world lore counterparts"). Biome folder -> set in
                             BIOME_SET; campaign.json names each theme's set ("Backgrounds": "tithemoors"). Byte-
                             identical duplicates are skipped; a new file gets a .meta from an existing backdrop's.
  Icons/<Key>.png            ability icons (64x64)
  Tiles/tile.png             the original board tile (checkerboard tint applied in BoardView)
  Audio/Sfx/*.ogg|wav, Audio/Music/*.mp3

"""
import argparse
import json
import os
import shutil
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Assets", "Gaiden", "Resources")

# GridGame2026 biome folder -> background set (its STORY.md region; GG-T353). The campaign theme that shows it, in
# campaign.json "Backgrounds": greenvalley, desert, swamp, cave, cityruins (their postgame themes borrow the same set).
BIOME_SET = {
    "Moors": "tithemoors",
    "RedThorns": "shardfall",
    "ElectricWasteland": "arkgrave",
    "UnderTheBridge": "anchorwastes",
    "CyberNecropolis": "halcyon",
}

SFX_KEEP_PREFIXES = (
    "Click", "Death", "Defeat", "Heal", "Move0", "NextTurn", "Portrait", "Rumble", "Select", "Slash0",
    "Slide", "Victory", "SFX_", "Vefects_SFX_Charge", "Vefects_SFX_Explosion", "Vefects_SFX_Fire_Burst",
    "Vefects_SFX_Impact", "Vefects_SFX_Lightning", "Vefects_SFX_Liquid_Hit_01", "Vefects_SFX_Poison_Puff_01",
    "Vefects_SFX_Slash", "Vefects_SFX_Shrapnel_01", "Vefects_SFX_Dust_Puff_01",
)


def ensure(path):
    os.makedirs(path, exist_ok=True)
    return path


def save_portrait_assets(im, class_id):
    """im: an opened RGBA PIL.Image. Writes Portraits/<id>.png (alpha-bbox-trimmed, max 640px tall) and returns it.
    A new class also needs a "Thumbnail" in classes.json (frame it in tools/ThumbnailStudio)."""
    portraits = ensure(os.path.join(RES, "Portraits"))
    full = im.copy()
    bbox = full.getchannel("A").getbbox()
    if bbox:
        full = full.crop(bbox)
    full.thumbnail((640, 640), Image.LANCZOS)
    full.save(os.path.join(portraits, class_id + ".png"), optimize=True)
    return full


def import_portraits(src, classes):
    done = 0
    for c in classes:
        key = c.get("Portrait") or c["Name"]
        path = os.path.join(src, "Sprites", "Actor", "Portraits", key + ".png")
        if not os.path.exists(path):
            print("  missing portrait", key)
            continue
        im = Image.open(path).convert("RGBA")
        save_portrait_assets(im, c["Id"])
        done += 1
    print(f"  portraits: {done}")


def import_backgrounds(src):
    """Every biome backdrop, full size, as Backgrounds/<set>_01.. (see the module docstring). No resize: the art's
    resolution is the asset's (owner rule, GG-T344/GG-T353); GaidenBuild caps Backgrounds at 4096 so Unity keeps it."""
    import hashlib, re, uuid
    out = ensure(os.path.join(RES, "Backgrounds"))
    template = next((os.path.join(out, f) for f in sorted(os.listdir(out)) if f.endswith(".jpg.meta")), None)
    for biome, set_name in BIOME_SET.items():
        folder = os.path.join(src, "Sprites", "Backgrounds", biome)
        seen, n = set(), 0
        for f in sorted(f for f in os.listdir(folder) if f.endswith(".png")):
            path = os.path.join(folder, f)
            digest = hashlib.md5(open(path, "rb").read()).hexdigest()
            if digest in seen:
                print(f"  {biome}/{f}: a byte-identical duplicate, skipped")
                continue
            seen.add(digest)
            n += 1
            im = Image.open(path)
            alpha = im.mode in ("RGBA", "LA", "PA") or "transparency" in im.info
            if alpha and im.convert("RGBA").getchannel("A").getextrema()[0] == 255:
                alpha = False
            dest = os.path.join(out, f"{set_name}_{n:02d}." + ("png" if alpha else "jpg"))
            if alpha:
                im.convert("RGBA").save(dest, optimize=True)
            else:
                im.convert("RGB").save(dest, quality=95, subsampling=0, optimize=True)
            if template and not os.path.exists(dest + ".meta"):
                meta = open(template, encoding="utf-8").read()
                meta = re.sub(r"(?m)^guid: [0-9a-f]{32}$", "guid: " + uuid.uuid4().hex, meta)
                meta = re.sub(r"maxTextureSize: \d+", "maxTextureSize: 4096", meta)
                open(dest + ".meta", "w", encoding="utf-8", newline="\n").write(meta)
        print(f"  {biome} -> {set_name}_01..{n:02d}")
    print("  backgrounds done")


def import_icons(src, abilities):
    out = ensure(os.path.join(RES, "Icons"))
    n = 0
    for a in abilities:
        key = a.get("Icon")
        if not key:
            continue
        path = os.path.join(src, "Sprites", "Spells", key + ".png")
        if os.path.exists(path):
            shutil.copyfile(path, os.path.join(out, key + ".png"))
            n += 1
    print(f"  icons: {n}")


def import_audio(src):
    sfx = ensure(os.path.join(RES, "Audio", "Sfx"))
    music = ensure(os.path.join(RES, "Audio", "Music"))
    n = 0
    for f in os.listdir(os.path.join(src, "SoundEffects")):
        if f.endswith(".meta") or not f.startswith(SFX_KEEP_PREFIXES):
            continue
        if "Loop" in f:
            continue
        shutil.copyfile(os.path.join(src, "SoundEffects", f), os.path.join(sfx, f))
        n += 1
    for f in os.listdir(os.path.join(src, "MusicTracks")):
        if f.endswith(".mp3"):
            shutil.copyfile(os.path.join(src, "MusicTracks", f), os.path.join(music, f))
    print(f"  sfx: {n}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="D:/Projects/MindAttic/GridGame2026/Assets")
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    content = os.path.join(RES, "Content")
    classes = json.load(open(os.path.join(content, "classes.json"), encoding="utf-8"))
    abilities = json.load(open(os.path.join(content, "abilities.json"), encoding="utf-8"))
    only = set(args.only.split(",")) if args.only else None
    if not only or "portraits" in only:
        import_portraits(args.src, classes)
    if not only or "backgrounds" in only:
        import_backgrounds(args.src)
    if not only or "icons" in only:
        import_icons(args.src, abilities)
    if not only or "audio" in only:
        import_audio(args.src)
    if not only or "tiles" in only:
        # The original board tile (GridGame2026 SpriteLibrary "Tile" = Sprites/Tiles/tile3); BoardView tints it into a checkerboard.
        shutil.copyfile(os.path.join(args.src, "Sprites", "Tiles", "tile3.png"), os.path.join(ensure(os.path.join(RES, "Tiles")), "tile.png"))
        import subprocess
        subprocess.run([sys.executable, os.path.join(ROOT, "tools", "make_tiles.py")], check=True)  # per-biome recolours


if __name__ == "__main__":
    sys.exit(main())
