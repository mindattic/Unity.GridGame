"""
Pixel-art timeline sprites for every enemy, made from its full-body portrait with ImageMagick.

    python tools/make_pixel_icons.py [--only slime00,bat00]

Writes Assets/Gaiden/Resources/Pixel/<classId>.png (144x144 = a 36x36 sprite at x4, nearest-neighbour)
plus a .meta cloned from Thumbs/slime00 (import settings come from GaidenImport in GaidenBuild.cs).

Recipe: knock out a solid backdrop (flood fill from the corners) -> trim the figure to its silhouette -> median-smooth (kills painterly noise) -> shrink to 32 px ->
hard alpha edge -> brighter, more saturated, more contrast -> 16-colour palette without dithering ->
1 px dark outline + 1 px cream outer rim (a "sticker" that reads on dark and light backgrounds alike) ->
re-ink the eyes from tools/eyes.json (they are the detail shrinking loses).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Assets", "Gaiden", "Resources")
SRC = os.path.join(RES, "Portraits")
OUT = os.path.join(RES, "Pixel")
TEMPLATE = os.path.join(RES, "Thumbs", "slime00.png.meta")
# classId -> [[x, y], ...] eye pixels in the 36x36 sprite, hand-placed from the full-res portraits
EYES = json.load(open(os.path.join(ROOT, "tools", "eyes.json"), encoding="utf-8"))


def opaque_corners(src):
    """True when the portrait was painted on a solid backdrop instead of transparency."""
    out = subprocess.run(["magick", src, "-format", "%[fx:p{0,0}.a+p{w-1,0}.a+p{0,h-1}.a+p{w-1,h-1}.a]", "info:"],
                         capture_output=True, text=True, check=True).stdout
    return float(out) > 2.0


def magick_args(src, dst):
    knockout = []
    if opaque_corners(src):  # flood the backdrop away from each corner
        knockout = ["-alpha", "set", "-fuzz", "14%", "-fill", "none"]
        for c in ("0,0", "%[fx:w-1],0", "0,%[fx:h-1]", "%[fx:w-1],%[fx:h-1]"):
            knockout += ["-draw", f"color {c} floodfill"]
        knockout += ["-fuzz", "0"]
    return [
        "magick", src, *knockout, "-trim", "+repage", "-statistic", "Median", "5x5",
        "-filter", "Triangle", "-resize", "32x32", "-background", "none", "-gravity", "center", "-extent", "36x36",
        "-channel", "A", "-threshold", "45%", "+channel",
        "-channel", "RGB", "-modulate", "112,155", "-sigmoidal-contrast", "5,45%", "+channel",
        "-dither", "None", "-colors", "16",
        "(", "+clone", "-alpha", "extract", "-morphology", "Dilate", "Diamond:1", "-background", "#140c1c", "-alpha", "shape", ")",
        "-compose", "DstOver", "-composite",
        "(", "+clone", "-alpha", "extract", "-morphology", "Dilate", "Diamond:1", "-background", "#fff4dc", "-alpha", "shape", ")",
        "-compose", "DstOver", "-composite",
        "-filter", "point", "-resize", "400%", "-strip", "PNG32:" + dst,
    ]


OUTLINE, RIM, EYE, GLINT = (0x14, 0x0C, 0x1C), (0xFF, 0xF4, 0xDC), (7, 4, 12, 255), (255, 244, 200, 255)
SCALE = 4  # output px per sprite px


def ink_eyes(png, eyes):
    """
    "Enhance" the eyes - the detail shrinking loses - by re-inking each as one solid dark sprite pixel.
    Hand-placed points (tools/eyes.json) can land a pixel off, on the outline or outside the figure, so
    each snaps to the nearest interior pixel (opaque, not outline/rim), keeping two eyes on two pixels.
    """
    if not eyes:
        return
    from PIL import Image
    im = Image.open(png).convert("RGBA")
    n = im.width // SCALE
    px = lambda x, y: im.getpixel((x * SCALE + 1, y * SCALE + 1))
    def interior(x, y):
        if not (0 <= x < n and 0 <= y < n):
            return False
        r, g, b, a = px(x, y)
        return a == 255 and (r, g, b) not in (OUTLINE, RIM)
    used = set()
    for ex, ey in eyes:
        best = None
        for rad in range(0, 3):
            cands = [(ex + dx, ey + dy) for dy in range(-rad, rad + 1) for dx in range(-rad, rad + 1)
                     if max(abs(dx), abs(dy)) == rad and interior(ex + dx, ey + dy) and (ex + dx, ey + dy) not in used]
            if cands:
                best = min(cands, key=lambda c: ((c[0] - ex) ** 2 + (c[1] - ey) ** 2, c[1], c[0]))
                break
        if best is None:
            continue
        used.add(best)
        # On a dark face a black dot disappears; there the eye becomes a pale glint instead.
        around = [px(best[0] + dx, best[1] + dy) for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)) if interior(best[0] + dx, best[1] + dy)]
        luma = sum(0.3 * r + 0.59 * g + 0.11 * b for r, g, b, _ in around) / max(1, len(around))
        ink = GLINT if luma < 70 else EYE
        for dy in range(SCALE):
            for dx in range(SCALE):
                im.putpixel((best[0] * SCALE + dx, best[1] * SCALE + dy), ink)
    im.save(png)


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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    classes = json.load(open(os.path.join(RES, "Content", "classes.json"), encoding="utf-8"))
    if isinstance(classes, dict):
        classes = next(iter(classes.values()))
    ids = [c["Id"] for c in classes if c.get("Team") == "Enemy"]
    if args.only:
        ids = [i for i in ids if i in args.only.split(",")]
    os.makedirs(OUT, exist_ok=True)
    if not os.path.exists(OUT + ".meta"):
        open(OUT + ".meta", "w", newline="\n").write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    missing = []
    for i in ids:
        src = os.path.join(SRC, i + ".png")
        if not os.path.exists(src):
            missing.append(i)
            continue
        dst = os.path.join(OUT, i + ".png")
        subprocess.run(magick_args(src, dst), check=True)
        ink_eyes(dst, EYES.get(i, ()))
        write_meta(dst)
    print(f"pixel sprites: {len(ids) - len(missing)} written to {OUT}")
    if missing:
        print("no portrait for:", ", ".join(missing))
        sys.exit(1)


if __name__ == "__main__":
    main()
