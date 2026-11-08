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
  matte      (GG-T369) an analytic re-cut of an existing cut-out's edge: composite over the known background,
             colour-to-alpha + two-colour projection on a trimap band, guided-filter refinement, contour coverage for
             hard cuts, colour decontamination. Fixes jaggies, leftover white fringe blocks and false holes; see the
             comment block above matte() for the maths. Run via --pick matte (an opaque original gets smoke first).

    python tools/portrait_cut.py --score [dir|file.png ...]          # jaggy metrics, worst first (jaggy_metrics)
    python tools/portrait_cut.py Portraits/x.png --pick matte --import x   # re-cut a committed portrait in place
The comparison sheet shows tolerance, ai, hybrid and smoke; unmix (the weakest on the samples) runs only via --pick.

Each variation is written as <name>.<method>.png beside --out, plus <name>.compare.png showing all four over dark and
light checkerboards with a zoomed detail. --import <classId> saves the --pick'd one as Portraits/<classId>.png the way
tools/import_art.py does: a 1024x1024 transparent canvas, never trimmed or shrunk (owner 2026-11-07, GG-T352: "all
characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the assets are"; a pasted image of
another size is fitted onto the canvas whole), with the class's "Figure" box written to classes.json; its existing
.meta (GUID) is kept.
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


# ---------------------------------------------------------------------------------------------------------------------
# matte: an analytic re-cut of an existing cut-out (GG-T369). Owner 2026-11-08: "find portraits with 'jaggies' reapply a
# white background then use your smarts to remove all background again, maybe you can calculate a better cut out than
# trial and error".
#
# Every step is a formula, not a tuned try-and-look:
#  1. Composite.  I = a*F + (1-a)*B over the known background B (white for a cut-out; the measured panel colour for an
#     RGB original). Where the cut-out's transparent pixels still hold the original light background with its
#     anti-aliased tail (several GG2026 originals only zeroed the alpha), those raw pixels are kept: they ARE the
#     original render.
#  2. Colour-to-alpha.  For a known B, the least alpha that explains pixel I as some colour F over B is, per channel,
#     (I-B)/(255-B) above B and (B-I)/B below it, maxed over channels (the per-pixel largest achievable distance,
#     GIMP's colour-to-alpha / Porter-Duff unmixing). It is exact where the art was anti-aliased against B.
#  3. Trimap.  The old silhouette S (a >= 0.5) gives the trimap: deeper than BAND px inside S (2 px when an aligned
#     original is given: that cut came from the AI matte, whose figure/background call is trusted) is certain figure,
#     pixels already transparent and background-coloured (u < BG_U) are certain background, and only the band between
#     is solved. In the band, background is what is reachable from certain background through background-coloured
#     pixels (4-connected, so a closed line-art outline protects white cloth). That removes the old cut's leftover
#     white blocks and fringes, and turns enclosed false holes (transparent, but figure-coloured) back into figure. An
#     intrusion into S that runs on until the certain core is not closed off by any outline: it is white cloth or hair
#     touching the white background, so it stays figure (white-on-white is not separable by colour).
#  4. Two-colour unmixing.  With the nearest certain-figure colour F as the known foreground, an edge pixel solves as
#     a = (I-B).(F-B)/|F-B|^2, bounded below by the colour-to-alpha u (the least alpha any F allows). Plain u alone
#     would read a pale figure colour (a pale leaf, light fur) as a translucent dark one and paint a dark rim on it.
#  5. Edge-aware refinement.  The band's alpha is smoothed with a guided filter (He et al., the local-linear
#     closed-form-matting approximation; grey guide, r=1) to drop JPEG noise without crossing image edges. Where the
#     art carries no mixing at all (a hard 0/255 cut whose rim pixels are pure figure colour), alpha is the coverage of
#     the silhouette's smoothed contour: supersampled x4, Gaussian (sigma 0.5 px), thresholded at 0.5 and box-averaged
#     back, so a straight edge stays crisp and a diagonal or curve gets its sub-pixel coverage. Thin strands (under 42%
#     figure in their 5x5 window) are exempt from the smoothing, which would break them.
#  6. Decontamination.  Edge colour is un-premultiplied: F = (I - (1-a)*B)/a, blended toward the nearest certain
#     figure colour where a is too small for that to be stable. Transparent pixels near the figure take the nearest
#     figure colour too, so bilinear sampling and mips bleed figure colour, not white.
#  7. Specks of fewer than SPECK px, and islands the old cut did not have at all (paper grain, JPEG noise), are
#     dropped. Soft regions away from any hard pixel (smoke, flame, glow kept by the smoke cut) are left exactly as
#     they were.
# White-on-white is the method's limit: a white figure (the mannequin) or white hair whose outline is open can still
# lose edge pixels, so look at a re-cut on a dark background before keeping it.
# Pixels outside the band are untouched, so interior detail and placement stay pixel for pixel.

MATTE_BAND = 10     # px inside the old silhouette that may still be background (the old cut's leftover white blocks)
MATTE_BG_U = 0.12   # colour alpha below this reads as background
MATTE_SPECK = 3     # px: isolated specks smaller than this are dropped


def guided_filter(guide, p, r=1, eps=1e-3):
    """He, Sun & Tang's guided filter (grey guide): the edge-preserving local-linear fit q = a*I + b."""
    k = 2 * r + 1
    box = lambda x: ndimage.uniform_filter(x, k, mode="nearest")
    mi, mp = box(guide), box(p)
    var = box(guide * guide) - mi * mi
    cov = box(guide * p) - mi * mp
    a = cov / (var + eps)
    b = mp - a * mi
    return box(a) * guide + box(b)


def contour_coverage(mask, up=4, sigma=0.5):
    """Sub-pixel coverage of a binary mask's smoothed contour: supersample, smooth by sigma px, threshold, box-average."""
    h, w = mask.shape
    big = ndimage.zoom(mask.astype(np.float32), up, order=1)
    big = ndimage.gaussian_filter(big, sigma * up) >= 0.5
    return big.reshape(h, up, w, up).mean(axis=(1, 3))


def matte(im, source=None, bg=None):
    """Analytic re-cut of an RGBA cut-out `im` (its alpha is the prior silhouette). source: optionally the original art
    on its flat background, pixel-aligned with im (same size); it then supplies the composite I and the background is
    measured from it. Without it, im is composited over bg (default white). Returns (RGBA image, info)."""
    arr = np.asarray(im.convert("RGBA")).astype(np.float32)
    raw = arr[..., :3]
    a0 = arr[..., 3] / 255.0
    if source is None:
        B = np.array(bg if bg is not None else (255, 255, 255), np.float32)
        I = raw * a0[..., None] + B * (1 - a0[..., None])
        lightraw = unmix_alpha(raw, B) < 0.35
        I = np.where(((a0 < 0.5) & lightraw)[..., None], raw, I)   # an un-erased original background is the render
    else:
        I = np.asarray(source.convert("RGB")).astype(np.float32)
        B = np.array(bg if bg is not None else remove_bg.background_colour(I.astype(np.uint8)), np.float32)
    u = unmix_alpha(I, B)
    solid = a0 >= 0.5
    hard = a0 >= 0.96
    # Soft regions with no hard pixel within 2 px: translucent smoke/glow the old cut kept on purpose. Leave them.
    smoke = (a0 > 0.04) & ~hard & ~ndimage.binary_dilation(hard, iterations=2)
    # With an aligned original the old cut came from the AI matte, whose figure/background call is trusted (it keeps
    # white cloth against the white panel); only its rim is re-solved. A bare cut-out may hide whole blocks of the old
    # background inside its silhouette, so its band is wider.
    depth = MATTE_BAND if source is None else 2
    core = ndimage.binary_erosion(solid, iterations=depth, border_value=1)
    band = ~core & ndimage.binary_dilation(solid | (a0 > 0.04), iterations=2) & ~smoke
    # Certain background: already transparent and background-coloured. Flood through background-coloured band pixels.
    seeds = (a0 <= 0.04) & (u < MATTE_BG_U)
    bgc = (u < MATTE_BG_U) & ~smoke & ~core & (band | seeds)
    lab, _ = ndimage.label(bgc)
    hit = np.unique(lab[seeds & bgc])
    bgr = np.isin(lab, hit[hit != 0])
    # Leftover background inside the old silhouette is closed off by the figure's outline within the band; a flood
    # that runs on until the certain-figure core is white cloth or hair the outline doesn't close, so it stays figure.
    intr, ni = ndimage.label(bgr & solid)
    if ni:
        reach = np.unique(intr[ndimage.binary_dilation(core) & (intr > 0)])
        bgr &= ~np.isin(intr, reach[reach != 0])
    # The figure in the band: everything not background. Its edge is the transition to solve.
    fig = (core | (band & ~bgr) | smoke | ((a0 > 0.04) & ~band & ~bgr))
    trans = band & (ndimage.binary_dilation(bgr, iterations=2) | ndimage.binary_dilation(~fig, iterations=1))
    trans &= ndimage.binary_dilation(fig, iterations=2)
    # Known foreground: the nearest certain-figure colour F. With F and B both known, the edge pixel I = a*F + (1-a)*B
    # is solved by projection, a = (I-B).(F-B) / |F-B|^2 (two-colour unmixing). Plain colour-to-alpha would read a
    # pale figure colour (a pale leaf, light fur) as a translucent dark one and paint a dark rim on it.
    sure0 = fig & ~trans & ~smoke
    _, idx = ndimage.distance_transform_edt(~sure0, return_indices=True)
    near = I[idx[0], idx[1]]
    fb = near - B
    fb2 = (fb * fb).sum(axis=2)
    ap = np.clip(((I - B) * fb).sum(axis=2) / np.maximum(fb2, 1.0), 0, 1)
    ap = np.where(fb2 < 40.0 ** 2, np.where(u < MATTE_BG_U, u, 1.0), ap)  # F too close to B to separate by colour
    # Colour-to-alpha is the least alpha any foreground colour allows, so it bounds the projection from below (the
    # nearest certain colour can be unrepresentative: a thin red line beside a dark body).
    ap = np.maximum(ap, u)
    grey = I.mean(axis=2) / 255.0
    ug = np.clip(guided_filter(grey, ap, 1, 1e-3), 0, 1)
    ug = np.where(ap < 0.02, ap, ug)  # the filter must not lift clean background
    cov = contour_coverage(fig & ~smoke | core)
    # Where the art itself carries mixing (a partial alpha within 1 px), the solved alpha is the answer; where it
    # carries none (a hard cut), the contour coverage is.
    has_mix = ndimage.binary_dilation((ug > 0.04) & (ug < 0.96) & trans, structure=np.ones((3, 3)))
    hard_src = False
    if source is None:
        # A cut-out whose transparent ring does not hold the light background it was cut from (zeroed to black, say)
        # carries no record of how its rim mixed with that background: only the contour geometry is left.
        ring = (a0 <= 0.04) & ndimage.binary_dilation(solid, iterations=3)
        if ring.any() and lightraw[ring].mean() < 0.5 and (a0[(a0 > 0.04)] < 0.96).mean() < 0.01:
            has_mix[:] = False
            hard_src = True
    a_new = np.where(has_mix, ug, cov)
    # Thin structures (strands, whiskers, hair tips: under 42% figure in their 5x5 window, where a big shape's edge has
    # about half) would be thinned or broken by contour smoothing: they keep their solved colour alpha, or stay solid.
    figm = fig & ~smoke | core
    thin = figm & (ndimage.uniform_filter(figm.astype(np.float32), 5) < 0.42)
    a_new = np.where(thin, 1.0 if hard_src else np.maximum(ug, np.where(has_mix, 0.0, 1.0)), a_new)
    a_new = np.where(u >= MATTE_BG_U, np.maximum(a_new, u), a_new)  # the colour bound holds after filtering too
    alpha = a0.copy()
    alpha[band] = np.where(fig[band], 1.0, 0.0)
    alpha[trans] = a_new[trans]
    alpha[bgr & ~trans] = 0.0
    alpha = np.where(alpha < 0.02, 0.0, alpha)
    # Specks.
    lab, n = ndimage.label(alpha > 0, structure=np.ones((3, 3)))
    if n:
        sizes = ndimage.sum(np.ones_like(alpha), lab, index=np.arange(1, n + 1))
        drop = np.isin(lab, np.where(sizes < MATTE_SPECK)[0] + 1) & band
        # Islands the old cut did not have at all (source paper grain, JPEG noise) are not figure either.
        had = np.unique(lab[(a0 > 0.04) & (lab > 0)])
        drop |= (lab > 0) & ~np.isin(lab, had)
        alpha[drop] = 0.0
    # Decontaminate: un-premultiply the background out of edge colour with the solved alpha; a pixel that solved as
    # pure figure keeps its colour, and tiny-alpha pixels take the nearest figure colour.
    aa = np.maximum(ap, 1e-3)[..., None]
    unm = np.clip((I - (1 - aa) * B) / aa, 0, 255)
    wgt = np.clip((ap - 0.08) / 0.3, 0, 1)[..., None]
    edge_rgb = np.where((ap >= 0.96)[..., None], I, unm * wgt + near * (1 - wgt))
    if hard_src:  # no mixing to undo: figure pixels keep their colour, new coverage outside takes the nearest figure's
        edge_rgb = np.where(fig[..., None], I, near)
    out = raw.copy()                       # outside the band: the cut-out's own colours, untouched
    out[trans] = edge_rgb[trans]
    solidb = band & ~trans & fig & (alpha >= 0.99)
    out[solidb] = I[solidb]                # band pixels now solid (fringe blocks resolved, false holes filled)
    nearfig = (alpha == 0) & ndimage.binary_dilation(alpha > 0, iterations=8)
    out[nearfig] = near[nearfig]
    changed = int((np.abs(alpha - a0) > 0.02).sum())
    info = f"matte: band {MATTE_BAND}px, bg {tuple(int(v) for v in B)}, {changed} px re-solved"
    return rgba(out, alpha), info


def method_matte(im):
    """An existing cut-out is re-cut as it stands; an opaque original first gets the smoke cut as its silhouette."""
    if im.mode == "RGBA" and np.asarray(im)[..., 3].min() < 250:
        return matte(im)
    cut, _ = method_smoke(im)
    return matte(cut, source=im.convert("RGB"))


def jaggy_metrics(im):
    """Objective jaggy measures for an RGBA cut-out (GG-T369).
    hard   fraction of 4-neighbour pairs across the alpha 0.5 contour that jump >= 85% of full alpha in one pixel
           (an anti-aliased edge spreads that over an intermediate pixel)
    stair  sub-pixel roughness of the alpha 0.5 iso-contour: mean |turning angle - its 7-step running mean| (rad); a
           hard edge snaps the contour to the pixel grid so its direction jitters between 0/45/90 degrees
    specks visible components under 40 px; holes: enclosed transparent components under 60 px
    halo   fraction of rim pixels (visible, touching transparency) that are near-white and >= 80 levels lighter than the
           darkest figure pixel 1-2 px inward: the old background's fringe left outside the outline
    score  50*hard + 25*min(stair/0.6, 1) + 100*halo + 0.3*min(specks+holes, 50)"""
    from skimage import measure
    a8 = np.asarray(im.convert("RGBA"))
    rgb = a8[..., :3].astype(np.float32)
    a = a8[..., 3].astype(np.float32) / 255.0
    solid = a >= 0.5
    dh = np.abs(a[:, 1:] - a[:, :-1])[solid[:, 1:] != solid[:, :-1]]
    dv = np.abs(a[1:] - a[:-1])[solid[1:] != solid[:-1]]
    cross = np.concatenate([dh, dv])
    hard = float((cross >= 0.85).mean()) if cross.size else 0.0
    rough = []
    for c in measure.find_contours(a, 0.5):
        if len(c) < 40:
            continue
        d = np.diff(c, axis=0)
        ang = np.unwrap(np.arctan2(d[:, 0], d[:, 1]))
        sm = np.convolve(ang, np.ones(7) / 7, mode="valid")
        rough.append(np.abs(ang[3:3 + sm.size] - sm))
    stair = float(np.concatenate(rough).mean()) if rough else 0.0
    lab, n = ndimage.label(a > 0.02)
    sizes = ndimage.sum(np.ones_like(a), lab, index=np.arange(1, n + 1)) if n else np.zeros(0)
    specks = int((sizes < 40).sum())
    bgm = a <= 0.02
    hl, hn = ndimage.label(bgm)
    border = np.unique(np.concatenate([hl[0], hl[-1], hl[:, 0], hl[:, -1]]))
    hs = ndimage.sum(np.ones_like(a), hl, index=np.arange(1, hn + 1)) if hn else np.zeros(0)
    enclosed = np.ones(hn, bool)
    enclosed[border[border > 0] - 1] = False
    holes = int((enclosed & (hs < 60)).sum())
    lum = rgb.mean(axis=2)
    rim = (a > 0.05) & ndimage.binary_dilation(bgm)
    inner = np.where(solid & ~rim, lum, 255.0)
    darkest_in = ndimage.minimum_filter(inner, size=5)
    halo_px = rim & (lum > 200) & (rgb.min(axis=2) > 185) & (darkest_in < lum - 80)
    halo = float(halo_px.sum() / max(rim.sum(), 1))
    score = 50 * hard + 25 * min(stair / 0.6, 1) + 100 * halo + 0.3 * min(specks + holes, 50)
    return {"hard": round(hard, 3), "stair": round(stair, 3), "specks": specks, "holes": holes,
            "halo": round(halo, 3), "score": round(score, 1)}


RUN = {"tolerance": method_tolerance, "unmix": method_unmix, "ai": method_ai, "hybrid": method_hybrid,
       "smoke": method_smoke, "matte": method_matte}


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


def trim_bars(im):
    """Crop away solid dark letterbox/pillarbox bars (some renders frame the white panel in black, with a logo in the
    bar), so the background is measured on the real panel."""
    a = np.asarray(im.convert("RGB")).astype(np.float32)
    lum = a.mean(axis=2)
    def dark(v):
        return v.mean() < 40 and np.percentile(v, 90) < 90
    h, w = lum.shape
    l, r, t, b = 0, w, 0, h
    while l < w // 3 and dark(lum[:, l]): l += 1
    while r > w - w // 3 and dark(lum[:, r - 1]): r -= 1
    while t < h // 3 and dark(lum[t, l:r]): t += 1
    while b > h - h // 3 and dark(lum[b - 1, l:r]): b -= 1
    if (l, r, t, b) == (0, w, 0, h):
        return im
    inset = 3  # the bar's anti-aliased edge
    return im.crop((l + inset if l else 0, t + inset if t else 0, r - inset if r < w else w, b - inset if b < h else h))


def load(args):
    im, name = _load(args)
    if np.asarray(im)[..., 3].min() < 250:
        return im, name  # already a cut-out: its transparent pixels' RGB is not a letterbox
    return trim_bars(im), name


def _load(args):
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
    ap.add_argument("--score", nargs="*", metavar="PATH", help="print jaggy metrics (default: every portrait)")
    args = ap.parse_args()
    if args.score is not None:
        import glob
        paths = args.score or [os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Portraits")]
        files = [f for p in paths for f in (sorted(glob.glob(os.path.join(p, "*.png"))) if os.path.isdir(p) else [p])]
        rows = [(os.path.splitext(os.path.basename(f))[0], jaggy_metrics(Image.open(f))) for f in files]
        for name, m in sorted(rows, key=lambda r: -r[1]["score"]):
            print(f"{name:20s} " + " ".join(f"{k} {v}" for k, v in m.items()))
        return
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
        print(f"imported Portraits/{args.class_id}.png {full.size[0]}x{full.size[1]} (re-frame its Thumbnail in tools/ThumbnailStudio)")


if __name__ == "__main__":
    main()
