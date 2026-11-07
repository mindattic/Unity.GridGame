"""
Erase a flat (usually white) background from a piece of art, leaving the figure untouched (GG-T327).

    python tools/remove_bg.py <image>            # or --clipboard to read a copied/pasted image
    python tools/remove_bg.py <image> --import chronomancer

How only the background is erased:
  1. The background colour is the median of the image's border pixels.
  2. Every pixel gets a distance from that colour (max RGB channel difference, 0-255).
  3. The tolerance is CALCULATED from the distance histogram: the background is the big peak near 0, and the
     tolerance sits in the valley right after it (where the background's own noise/JPEG grain ends and the figure
     begins). --tolerance N overrides it.
  4. Only background-coloured pixels CONNECTED TO THE EDGE are erased (a flood fill), so white eyes, teeth,
     highlights and white clothing inside the figure survive. Enclosed pockets of background (the gap between an
     arm and the body, gaps in hair) are erased too when nearly pure background colour (half the tolerance) and at
     least --hole-area % of the image (default 0.005%, ~20 px on a 640 px portrait; raise it if a white detail goes).
  5. The cut edge is feathered over a soft band past the tolerance, and the background colour is pulled back out
     of those edge pixels (defringe), so there is no white halo on a dark board.

Writes, next to the input (or --out): <name>.cut.png (RGBA) and <name>.preview.png (the cut over a checkerboard,
dark and light, beside the original) to judge it by. --import <classId> also saves the cut as the game's
Portraits/<classId>.png the same way tools/import_art.py does (a 1024x1024 transparent canvas, never trimmed or shrunk,
GG-T352), keeping its .meta.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageGrab
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def load(args):
    if args.clipboard:
        clip = ImageGrab.grabclipboard()
        if isinstance(clip, list) and clip:
            return Image.open(clip[0]), os.path.splitext(os.path.basename(clip[0]))[0]
        if isinstance(clip, Image.Image):
            return clip, "clipboard"
        sys.exit("The clipboard holds no image. Copy the picture (or its file) and run again, or pass a path.")
    if not args.image:
        sys.exit("Pass an image path, or --clipboard.")
    return Image.open(args.image), os.path.splitext(os.path.basename(args.image))[0]


def background_colour(rgb):
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]]).astype(np.float32)
    return np.median(border, axis=0)


def auto_tolerance(dist, edge_mask):
    """The valley after the background's peak in the distance histogram of the edge-connected candidates."""
    hist = np.bincount(dist[edge_mask].ravel(), minlength=256).astype(np.float64)
    hist = np.convolve(hist, np.ones(5) / 5, mode="same")
    peak = int(np.argmax(hist[:64]))
    floor = hist[peak] * 0.01
    t = peak
    while t < 120 and hist[t] > floor and not (hist[t] < hist[t + 1] and hist[t] < hist[peak] * 0.1):
        t += 1
    return int(np.clip(t + 4, 10, 90))


def cut(im, tolerance=None, feather=None, hole_area=0.005):
    rgba = np.array(im.convert("RGBA"))
    rgb = rgba[..., :3].astype(np.int16)
    alpha0 = rgba[..., 3].astype(np.float32) / 255.0
    bg = background_colour(rgba[..., :3])
    dist = np.abs(rgb - bg.astype(np.int16)).max(axis=2).astype(np.int32)

    # Candidates reachable from the edge through loosely background-ish pixels, to measure the tolerance on.
    loose, _ = ndimage.label(dist <= 120)
    edge_labels = np.unique(np.concatenate([loose[0], loose[-1], loose[:, 0], loose[:, -1]]))
    edge_labels = edge_labels[edge_labels != 0]
    tol = tolerance if tolerance is not None else auto_tolerance(dist, np.isin(loose, edge_labels))
    soft = feather if feather is not None else max(6, tol // 2)

    # Background = within tolerance AND connected to the image border.
    within = dist <= tol
    lab, n = ndimage.label(within)
    edge = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    bgmask = np.isin(lab, edge[edge != 0])

    # Large enclosed pockets that are clearly background (tighter than the tolerance) go too.
    holes = 0
    if hole_area > 0:
        tight, nt = ndimage.label((dist <= max(4, tol // 2)) & ~bgmask)
        if nt:
            sizes = ndimage.sum(np.ones_like(tight), tight, index=np.arange(1, nt + 1))
            big = np.where(sizes >= hole_area / 100.0 * dist.size)[0] + 1
            if len(big):
                pocket = np.isin(tight, big)
                pocket = ndimage.binary_dilation(pocket, iterations=2) & within  # grow back to the pocket's rim
                bgmask |= pocket
                holes = len(big)

    # Feather: pixels next to the background within tol..tol+soft fade out with their distance.
    alpha = np.ones(dist.shape, np.float32)
    alpha[bgmask] = 0.0
    near = ndimage.binary_dilation(bgmask, iterations=2) & ~bgmask
    ramp = np.clip((dist - tol) / float(soft), 0.0, 1.0)
    alpha[near] = np.minimum(alpha[near], ramp[near])
    alpha *= alpha0

    # Defringe: remove the background colour mixed into semi-transparent edge pixels.
    out = rgb.astype(np.float32)
    edge_px = (alpha > 0) & (alpha < 1)
    a = alpha[edge_px][:, None]
    out[edge_px] = np.clip((out[edge_px] - (1 - a) * bg) / a, 0, 255)

    res = np.dstack([out, alpha * 255.0]).round().astype(np.uint8)
    return Image.fromarray(res, "RGBA"), dict(bg=bg.round().astype(int).tolist(), tolerance=int(tol), feather=int(soft),
                                              holes=holes, erased=float(bgmask.mean()))


def checker(size, a, b, cell=16):
    w, h = size
    y, x = np.mgrid[0:h, 0:w]
    m = ((x // cell + y // cell) % 2).astype(bool)
    arr = np.where(m[..., None], np.array(a, np.uint8), np.array(b, np.uint8))
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def preview(orig, cutimg):
    w, h = cutimg.size
    panes = [orig.convert("RGB")]
    for a, b in (((40, 40, 48), (58, 58, 70)), ((225, 225, 225), (250, 250, 250))):
        bgp = checker((w, h), a, b).convert("RGBA")
        bgp.alpha_composite(cutimg)
        panes.append(bgp.convert("RGB"))
    sheet = Image.new("RGB", (w * 3 + 16, h), (20, 20, 24))
    for i, p in enumerate(panes):
        sheet.paste(p, (i * (w + 8), 0))
    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("image", nargs="?")
    ap.add_argument("--clipboard", action="store_true", help="read the image from the clipboard")
    ap.add_argument("--tolerance", type=int, help="override the calculated tolerance (0-255 per channel)")
    ap.add_argument("--feather", type=int, help="soft edge band past the tolerance (default tolerance/2, min 6)")
    ap.add_argument("--hole-area", type=float, default=0.005,
                    help="erase enclosed background pockets at least this %% of the image (0 = never)")
    ap.add_argument("--out", help="output folder (default: beside the input, or the current folder)")
    ap.add_argument("--import", dest="class_id", help="also save as the game's Portraits/<classId>.png")
    args = ap.parse_args()

    im, name = load(args)
    cutimg, info = cut(im, args.tolerance, args.feather, args.hole_area)
    out = args.out or (os.path.dirname(os.path.abspath(args.image)) if args.image else os.getcwd())
    os.makedirs(out, exist_ok=True)
    cut_path = os.path.join(out, name + ".cut.png")
    prev_path = os.path.join(out, name + ".preview.png")
    cutimg.save(cut_path, optimize=True)
    preview(im, cutimg).save(prev_path)
    print(f"background {info['bg']}  tolerance {info['tolerance']}  feather {info['feather']}  "
          f"enclosed pockets {info['holes']}  erased {info['erased']:.1%}")
    print("cut     ", cut_path)
    print("preview ", prev_path)

    if args.class_id:
        from import_art import save_portrait_assets
        full = save_portrait_assets(cutimg, args.class_id)
        dest = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Portraits", args.class_id + ".png")
        print(f"imported {dest} {full.size}  (re-frame its Thumbnail in tools/ThumbnailStudio if the crop moved)")


if __name__ == "__main__":
    main()
