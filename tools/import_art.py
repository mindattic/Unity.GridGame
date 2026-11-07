"""
Reproducible art/audio import from the GridGame2026 reference project into GridGameGaiden.

    python tools/import_art.py [--src D:/Projects/MindAttic/GridGame2026/Assets]

Produces (under Assets/Gaiden/Resources/):
  Portraits/<classId>.png    full-body art on a 1024x1024 transparent canvas (owner 2026-11-07, GG-T352: "all
                             characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the
                             assets are"): the one image per unit, never trimmed or shrunk. Every thumbnail (board
                             token, timeline icon, cards) is a zoom + offset into it, kept in classes.json as the
                             class's "Thumbnail" and framed in tools/ThumbnailStudio (GG-T220) - no thumb images are
                             cut. The figure's own box (alpha bounds) is written to the class's "Figure" so whole-figure
                             views (dialogue, cut-ins, cards) draw the figure, not the empty canvas around it.
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


# Owner 2026-11-07 (GG-T352): "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what
# the assets are." Portraits are written at exactly this size: never alpha-trimmed, never capped smaller.
CANVAS = 1024


def fit_canvas(im):
    """im on a CANVAS x CANVAS transparent canvas. A 1024x1024 image is kept pixel for pixel. Any other size is cut to
    its figure (alpha bounds, so nothing of the figure is lost), scaled down only if that is still larger than the
    canvas, and centred."""
    im = im.convert("RGBA")
    if im.size == (CANVAS, CANVAS):
        return im.copy()
    bbox = im.getchannel("A").getbbox()
    if bbox:
        im = im.crop(bbox)
    s = min(1.0, CANVAS / im.width, CANVAS / im.height)
    if s < 1.0:
        size = (max(1, round(im.width * s)), max(1, round(im.height * s)))
        im = im.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")  # premultiplied: no dark fringe
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((CANVAS - im.width) // 2, (CANVAS - im.height) // 2))
    return canvas


def figure_box(im):
    """The figure's box on the portrait as classes.json "Figure" fractions {X, Y, W, H} (left, top, width, height of
    the alpha bounds over the canvas width / height), or None for an empty image."""
    bbox = im.getchannel("A").getbbox()
    if not bbox:
        return None
    w, h = im.size
    x0, y0, x1, y1 = bbox
    return {"X": x0 / w, "Y": y0 / h, "W": (x1 - x0) / w, "H": (y1 - y0) / h}


def _num(v):
    """A number the way tools/ThumbnailStudio writes it (Thumb.Num): 4 decimals, trailing zeros dropped."""
    t = f"{round(v, 4):.4f}".rstrip("0").rstrip(".")
    return "0" if t in ("-0", "") else t


def set_class_blocks(updates, path=None):
    """Targeted edit of classes.json: for each {classId: {"Thumbnail": {...}, "Figure": {...}}} replace that block of
    the class (or insert it after its Thumbnail / Portrait / Id line), leaving every other byte alone like
    tools/ThumbnailStudio does (other agents own the rest of the file). Returns the ids that are not classes."""
    import re
    path = path or os.path.join(RES, "Content", "classes.json")
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    missing = []
    for cid, blocks in updates.items():
        m = re.search(r'^([ \t]*)"Id": "' + re.escape(cid) + r'",?\r?$', text, re.M)
        if not m:
            missing.append(cid)
            continue
        ind = m.group(1)
        start = m.start()
        end = text.find(nl + ind[:-2] + "}", m.end())  # the class object's closing brace
        body = text[start:end]
        for key, val in blocks.items():
            inner = ("," + nl).join(f'{ind}  "{k}": {_num(v)}' for k, v in val.items())
            block = f'"{key}": {{{nl}{inner}{nl}{ind}}}'
            bm = re.search(r'"' + key + r'": \{[^{}]*\}', body)
            if bm:
                body = body[:bm.start()] + block + body[bm.end():]
                continue
            anchor = None
            for a in ("Thumbnail", "Portrait", "Id"):
                anchor = re.search(r'^' + re.escape(ind) + '"' + a + r'": (\{[^{}]*\}|"[^"]*"),?(?=\r?$)', body, re.M)
                if anchor:
                    break
            line = anchor.group(0)
            ins = (line + nl + ind + block + ",") if line.endswith(",") else (line + "," + nl + ind + block)
            body = body[:anchor.start()] + ins + body[anchor.end():]
        text = text[:start] + body + text[end:]
    json.loads(text)  # still valid JSON
    data = text.encode("utf-8")
    if raw.startswith(b"\xef\xbb\xbf"):
        data = b"\xef\xbb\xbf" + data
    tmp = path + ".import.tmp"
    open(tmp, "wb").write(data)
    os.replace(tmp, path)
    return missing


def save_portrait_assets(im, class_id):
    """im: an opened RGBA PIL.Image. Writes Portraits/<id>.png as a 1024x1024 RGBA canvas (fit_canvas: a 1024 image
    untouched, never trimmed or shrunk; any other size fitted onto the canvas whole) and returns it. The existing .meta
    (GUID) is kept, and the class's "Figure" box in classes.json is set from the canvas (figure_box), when the class
    exists. A new class also needs a "Thumbnail" in classes.json (frame it in tools/ThumbnailStudio)."""
    portraits = ensure(os.path.join(RES, "Portraits"))
    full = fit_canvas(im)
    full.save(os.path.join(portraits, class_id + ".png"), optimize=True)
    box = figure_box(full)
    if box and set_class_blocks({class_id: {"Figure": box}}):
        print(f"  {class_id} is not a class yet: give it \"Figure\": {json.dumps({k: float(_num(v)) for k, v in box.items()})}")
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
