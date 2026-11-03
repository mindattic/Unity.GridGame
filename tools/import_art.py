"""
Reproducible art/audio import from the GridGame2026 reference project into GridGameGaiden.

    python tools/import_art.py [--src D:/Projects/MindAttic/GridGame2026/Assets]

Produces (under Assets/Gaiden/Resources/):
  Portraits/<classId>.png    full-body art, max 640 px tall: the one image per unit. Every thumbnail (board token,
                             timeline icon, cards) is a zoom + offset into it, kept in classes.json as the class's
                             "Thumbnail" and framed in tools/ThumbnailStudio (GG-T220) - no thumb images are cut.
  Backgrounds/<theme>_<n>.jpg 720x1440 biome backdrops (stage themes mapped to biome folders)
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

THEME_BIOME = {
    "greenvalley": "Moors",
    "desert": "RedThorns",
    "swamp": "ElectricWasteland",
    "cave": "UnderTheBridge",
    "cityruins": "CyberNecropolis",
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


def face_crop(im):
    """Returns a square crop box (l, t, r, b) around the head/upper body (used by tools/eyezoom.py)."""
    alpha = im.getchannel("A")
    bbox = alpha.getbbox()
    w, h = im.size
    if not bbox:
        return (0, 0, w, h)
    l, t, r, b = bbox
    bw, bh = r - l, b - t
    band_h = max(1, int(bh * 0.30))
    band = alpha.crop((l, t, r, t + band_h))
    px = band.load()
    total = 0
    acc = 0
    for y in range(0, band.size[1], 4):
        for x in range(0, band.size[0], 4):
            a = px[x, y]
            if a > 32:
                total += a
                acc += a * x
    cx = l + (acc / total if total else bw / 2)
    size = int(max(min(bw, bh * 0.42), w * 0.28))
    size = min(size, w, h)
    top = max(0, int(t - size * 0.06))
    left = int(cx - size / 2)
    left = max(0, min(w - size, left))
    top = min(h - size, top)
    return (left, top, left + size, top + size)


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
    out = ensure(os.path.join(RES, "Backgrounds"))
    for theme, biome in THEME_BIOME.items():
        folder = os.path.join(src, "Sprites", "Backgrounds", biome)
        files = sorted(f for f in os.listdir(folder) if f.endswith(".png"))[:4]
        for i, f in enumerate(files):
            im = Image.open(os.path.join(folder, f)).convert("RGB")
            im = im.resize((720, 1440), Image.LANCZOS)
            im.save(os.path.join(out, f"{theme}_{i}.jpg"), quality=86, optimize=True)
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
