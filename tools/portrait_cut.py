"""
Consume a new character portrait on a white/flat background and convert it into game art (GG-T327).

    python tools/portrait_cut.py <image> | --clipboard            # four variations + a comparison sheet
    python tools/portrait_cut.py <image> --import chronomancer        # cut with "smoke" (the default) and import

The four methods (research summary in the GG-T327 ticket):
  tolerance  Flood fill from the border with a CALCULATED colour tolerance (tools/remove_bg.py): only edge-connected
             background-coloured pixels go, so white details inside the figure stay. Hard, crisp edge. Weak where a
             white garment touches the background.
  unmix      "Colour to alpha" (GIMP's method) on the edge-connected region: each pixel's alpha is how far it is from
             the background colour, and that colour is subtracted back out. Keeps glows, smoke and soft shadows as
             real translucency. Can thin a near-white garment at its edge.
  ai         A segmentation network trained on anime characters (rembg's isnet-anime), the same kind of model a
             paint program's "Erase background" button runs. Knows what a figure is, so white cloth survives.
             Its edge is softer and can clip thin details.
  hybrid     The AI matte decides figure vs background, the tolerance flood guarantees pure background is gone,
             and the colour unmix refines only the semi-transparent rim (and removes the white fringe).
  smoke      hybrid, plus translucent effects outside the figure (smoke, flame, glow) kept at their real translucency.
The comparison sheet shows tolerance, ai, hybrid and smoke; unmix (the weakest on the samples) runs only via --pick.

Each variation is written as <name>.<method>.png beside --out, plus <name>.compare.png showing all four over dark and
light checkerboards with a zoomed detail. --import <classId> saves the --pick'd one as Portraits/<classId>.png
(alpha-trimmed, max 640 px, like tools/import_art.py); its existing .meta (GUID) is kept.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageGrab
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import remove_bg  # noqa: E402

METHODS = ("tolerance", "ai", "hybrid", "smoke")  # "unmix" stays available via --pick


def edge_connected(mask):
    lab, _ = ndimage.label(mask)
    edge = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    return np.isin(lab, edge[edge != 0])


def unmix_alpha(rgb, bg):
    """GIMP colour-to-alpha: the least alpha that explains the pixel as figure colour over the background."""
    c = rgb.astype(np.float32)
    b = bg.astype(np.float32)
    # An off-white background (~252) leaves almost no headroom above it, so a pixel a few levels brighter would read
    # as solid: give "brighter than the background" a floor of 48 levels of headroom.
    up = np.where(c > b, (c - b) / np.maximum(255.0 - b, 48.0), 0.0)
    down = np.where(c < b, (b - c) / np.maximum(b, 1.0), 0.0)
    return np.clip(np.maximum(up, down).max(axis=2), 0.0, 1.0)


def defringe(rgb, alpha, bg):
    out = rgb.astype(np.float32)
    m = (alpha > 0.004) & (alpha < 0.999)
    a = alpha[m][:, None]
    out[m] = np.clip((out[m] - (1 - a) * bg) / a, 0, 255)
    return out


def rgba(out, alpha):
    return Image.fromarray(np.dstack([out, alpha * 255.0]).round().clip(0, 255).astype(np.uint8), "RGBA")


def method_tolerance(im):
    cut, info = remove_bg.cut(im)
    return cut, f"tolerance {info['tolerance']}, {info['holes']} enclosed gaps"


def method_unmix(im):
    rgb = np.array(im.convert("RGB"))
    bg = remove_bg.background_colour(rgb)
    a = unmix_alpha(rgb, bg)
    # Only pixels reachable from the border through background-ish colour may turn translucent; the figure stays solid.
    region = edge_connected(a < 0.35)
    region = ndimage.binary_dilation(region, iterations=2)
    alpha = np.where(region, a, 1.0)
    alpha = np.where(alpha < 0.03, 0.0, alpha)
    return rgba(defringe(rgb, alpha, bg), alpha), "colour-to-alpha on the border-connected region"


_session = None


def ai_alpha(im):
    global _session
    from rembg import new_session, remove
    if _session is None:
        _session = new_session("isnet-anime")
    out = remove(im.convert("RGB"), session=_session, post_process_mask=False)
    return np.array(out)[..., 3].astype(np.float32) / 255.0


def method_ai(im):
    rgb = np.array(im.convert("RGB"))
    a = ai_alpha(im)
    return rgba(rgb.astype(np.float32), a), "isnet-anime segmentation"


def method_hybrid(im):
    rgb = np.array(im.convert("RGB"))
    bg = remove_bg.background_colour(rgb)
    ai = ai_alpha(im)
    tol_cut, info = remove_bg.cut(im)
    tol_a = np.array(tol_cut)[..., 3].astype(np.float32) / 255.0
    un = unmix_alpha(rgb, bg)

    figure = ai > 0.5
    core = ndimage.binary_erosion(figure, iterations=3)          # well inside the figure: solid
    rim = ndimage.binary_dilation(figure, iterations=3) & ~core  # the edge band: refine by colour
    alpha = np.zeros(ai.shape, np.float32)
    alpha[core] = 1.0
    # In the rim, trust colour where the pixel is far from the background, else the AI's soft value.
    alpha[rim] = np.maximum(np.minimum(un[rim] * 1.6, 1.0) * (ai[rim] > 0.05), ai[rim] * (un[rim] > 0.08))
    # Enclosed white gaps the AI kept but the tolerance flood (and the colour) say are background.
    alpha = np.where((tol_a == 0) & (un < 0.06), 0.0, alpha)
    alpha = np.where(alpha < 0.03, 0.0, alpha)
    return rgba(defringe(rgb, alpha, bg), alpha), f"AI matte + tolerance {info['tolerance']} + unmixed rim"


def method_smoke(im):
    """Hybrid, plus translucent effects (smoke, flame, glow) the AI matte leaves out: anything outside the figure that
    is clearly coloured is kept, at the colour-to-alpha translucency it really has."""
    rgb = np.array(im.convert("RGB"))
    bg = remove_bg.background_colour(rgb)
    hyb, _ = method_hybrid(im)
    alpha = np.array(hyb)[..., 3].astype(np.float32) / 255.0
    un = unmix_alpha(rgb, bg)
    tinted = un >= 0.10
    lab, n = ndimage.label(tinted)
    if n:
        sizes = ndimage.sum(tinted, lab, index=np.arange(1, n + 1))
        keep = np.isin(lab, np.where(sizes >= 0.0002 * un.size)[0] + 1)  # drop JPEG specks and stray lines
        # Pure colour-to-alpha turns pale smoke into a faint dark tint that vanishes on a dark board; boost it so the
        # smoke keeps its pale look and only its thinnest edge goes translucent.
        alpha = np.where(keep, np.maximum(alpha, np.clip(un * 3.0, 0.0, 1.0)), alpha)
    alpha = np.where(alpha < 0.03, 0.0, alpha)
    return rgba(defringe(rgb, alpha, bg), alpha), "hybrid + translucent smoke/flame kept"


RUN = {"tolerance": method_tolerance, "unmix": method_unmix, "ai": method_ai, "hybrid": method_hybrid,
       "smoke": method_smoke}


def over(img, a, b, cell=16):
    base = remove_bg.checker(img.size, a, b, cell).convert("RGBA")
    base.alpha_composite(img)
    return base.convert("RGB")


def detail_box(alpha_any):
    """A zoom box on the busiest stretch of edge: where the most partially transparent pixels are."""
    h, w = alpha_any.shape
    soft = ((alpha_any > 0.05) & (alpha_any < 0.95)).astype(np.float32)
    k = max(32, min(h, w) // 6)
    dens = ndimage.uniform_filter(soft, k)
    y, x = np.unravel_index(np.argmax(dens), dens.shape)
    x0 = int(np.clip(x - k, 0, w - 2 * k)); y0 = int(np.clip(y - k, 0, h - 2 * k))
    return (x0, y0, x0 + 2 * k, y0 + 2 * k)


def compare_sheet(src, results, path):
    tile = 360
    font = None
    try:
        font = ImageFont.truetype("arial.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    box = detail_box(np.array(results["hybrid"][0])[..., 3] / 255.0)
    cols = [("original", src.convert("RGBA"), "")] + [(m, results[m][0], results[m][1]) for m in METHODS]
    rows = [("dark", (34, 34, 42), (52, 52, 64)), ("light", (226, 226, 226), (250, 250, 250)), ("zoom", (34, 34, 42), (52, 52, 64))]
    head = 52
    sheet = Image.new("RGB", (len(cols) * (tile + 8) + 8, head + len(rows) * (tile + 8)), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    for ci, (name, img, note) in enumerate(cols):
        x = 8 + ci * (tile + 8)
        label = name if ci == 0 else f"{ci}. {name}"
        d.text((x, 6), label, fill=(240, 240, 240), font=font)
        d.text((x, 28), note[:46], fill=(150, 150, 160), font=ImageFont.load_default())
        for ri, (rname, a, b) in enumerate(rows):
            y = head + ri * (tile + 8)
            src_img = img.crop(box) if rname == "zoom" else img
            view = over(src_img, a, b, 8 if rname != "zoom" else 12)
            view.thumbnail((tile, tile), Image.LANCZOS if rname != "zoom" else Image.NEAREST)
            if rname == "zoom":
                view = view.resize((tile, tile), Image.NEAREST)
            sheet.paste(view, (x + (tile - view.width) // 2, y + (tile - view.height) // 2))
    sheet.save(path)


def load(args):
    if args.clipboard:
        clip = ImageGrab.grabclipboard()
        if isinstance(clip, list) and clip:
            return Image.open(clip[0]).convert("RGBA"), os.path.splitext(os.path.basename(clip[0]))[0]
        if isinstance(clip, Image.Image):
            return clip.convert("RGBA"), "clipboard"
        sys.exit("The clipboard holds no image.")
    if not args.image:
        sys.exit("Pass an image path, or --clipboard.")
    return Image.open(args.image).convert("RGBA"), os.path.splitext(os.path.basename(args.image))[0]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("image", nargs="?")
    ap.add_argument("--clipboard", action="store_true")
    ap.add_argument("--out", help="output folder (default beside the input, or the current folder)")
    ap.add_argument("--pick", choices=list(RUN), help="run only this method")
    ap.add_argument("--import", dest="class_id", help="save the --pick'd result as Portraits/<classId>.png")
    args = ap.parse_args()
    if args.class_id and not args.pick:
        args.pick = "smoke"  # the method that held up on every GG-T327 sample

    im, name = load(args)
    out = args.out or (os.path.dirname(os.path.abspath(args.image)) if args.image else os.getcwd())
    os.makedirs(out, exist_ok=True)
    methods = [args.pick] if args.pick else list(METHODS)
    results = {}
    for m in methods:
        results[m] = RUN[m](im)
        p = os.path.join(out, f"{name}.{m}.png")
        results[m][0].save(p, optimize=True)
        print(f"{m:10s} {results[m][1]:52s} {p}")
    if not args.pick:
        cp = os.path.join(out, f"{name}.compare.png")
        compare_sheet(im, results, cp)
        print("compare   ", cp)
    if args.class_id:
        from import_art import save_portrait_assets
        full = save_portrait_assets(results[args.pick][0], args.class_id)
        print(f"imported Portraits/{args.class_id}.png {full.size} (re-frame its Thumbnail in tools/ThumbnailStudio)")


if __name__ == "__main__":
    main()
