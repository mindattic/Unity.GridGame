"""
Biome board tiles from GridGame2026's original tile (owner, 2026-10-10: "each area should use a different tile
style; reuse the original when the biome matches its colours"; owner, 2026-10-13: "the board tiles should be
terrain textures that match the level biome you're in: desert, snow, dirt, wasteland, urban, etc").

    python tools/make_tiles.py [theme ...]

Reads Assets/Gaiden/Resources/Tiles/tile.png (the original slate tile, imported by tools/import_art.py) and writes
Tiles/<themeId>.png per campaign theme. The outer bevel rim (the black edge + grey highlight BoardView's
checkerboard tinting relies on) is kept pixel-identical; only the flat interior fill is replaced with a
deterministically-seeded procedural terrain pattern per biome — sand ripples and grit for the desert, mottled
grass-and-dirt for the moss moors, bog muck and reed streaks for the swamp, frost-glint rock for the frozen
caverns, and cobblestone/rubble masonry for the city ruins. `cave` ("The Anchor Wastes") is themed as icy rock
rather than left as the plain original slate, since "snow" was named explicitly.
"""
import os
from PIL import Image
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TILES = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Tiles")

SEED = 20261013


def load_base():
    im = Image.open(os.path.join(TILES, "tile.png")).convert("RGBA")
    arr = np.array(im).astype(np.int16)
    fill = arr[arr.shape[0] // 2, arr.shape[1] // 2].copy()
    # Interior = pixels close to the flat fill colour; everything else (bevel rim, outer edge,
    # transparent corners) is preserved pixel-for-pixel so the tile frame/checkerboard look survives.
    dist = np.abs(arr[:, :, :3] - fill[:3]).sum(axis=2)
    mask = dist < 12
    return arr, mask


def rng_fields(h, w, seed):
    r = np.random.RandomState(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    return r, yy.astype(np.float32), xx.astype(np.float32)


def smooth_noise(r, h, w, cell):
    """Cheap value-noise: random values on a coarse grid, bilinearly upsampled — no scipy needed."""
    gh, gw = h // cell + 2, w // cell + 2
    coarse = r.uniform(0.0, 1.0, size=(gh, gw)).astype(np.float32)
    big = Image.fromarray((coarse * 255).astype(np.uint8), mode="L").resize((gw * cell, gh * cell), Image.BILINEAR)
    return np.array(big).astype(np.float32)[:h, :w] / 255.0


def clamp_u8(a):
    return np.clip(a, 0, 255).astype(np.uint8)


def desert(arr, mask, h, w):
    r, yy, xx = rng_fields(h, w, SEED + 1)
    base = np.array([214, 178, 120], dtype=np.float32)
    n = smooth_noise(r, h, w, 14)
    ripple = 0.5 + 0.5 * np.sin(xx * 0.11 + np.sin(yy * 0.05) * 2.2 + n * 2.0)
    grain = r.uniform(-14, 14, size=(h, w)).astype(np.float32)
    lum = 0.78 + 0.22 * ripple
    out = base[None, None, :] * lum[:, :, None] + grain[:, :, None]
    return out


def greenvalley(arr, mask, h, w):
    r, yy, xx = rng_fields(h, w, SEED + 2)
    grass = np.array([78, 128, 58], dtype=np.float32)
    dirt = np.array([108, 82, 52], dtype=np.float32)
    patches = smooth_noise(r, h, w, 20)
    blade = 0.5 + 0.5 * np.sin(yy * 0.9 + np.sin(xx * 0.15) * 1.5)
    grain = r.uniform(-10, 10, size=(h, w)).astype(np.float32)
    t = np.clip((patches - 0.35) / 0.3, 0, 1)[:, :, None]
    out = grass[None, None, :] * (1 - t) + dirt[None, None, :] * t
    out = out * (0.9 + 0.1 * blade[:, :, None]) + grain[:, :, None]
    return out


def swamp(arr, mask, h, w):
    r, yy, xx = rng_fields(h, w, SEED + 3)
    muck = np.array([58, 92, 78], dtype=np.float32)
    deep = np.array([34, 58, 54], dtype=np.float32)
    patches = smooth_noise(r, h, w, 22)
    ripples = smooth_noise(r, h, w, 9)
    grain = r.uniform(-10, 10, size=(h, w)).astype(np.float32)
    t = np.clip((patches - 0.4) / 0.35, 0, 1)[:, :, None]
    out = muck[None, None, :] * (1 - t) + deep[None, None, :] * t
    # Short broken reed streaks instead of long straight bands — gated by the ripple field so no
    # two neighbouring rows read as one continuous plank line.
    reed = 0.5 + 0.5 * np.sin(xx * 0.05 + yy * 0.4 + patches * 3.0)
    streak = ((reed > 0.88) & (ripples > 0.5)).astype(np.float32) * 16
    out = out - streak[:, :, None] + grain[:, :, None]
    return out


def cave(arr, mask, h, w):
    r, yy, xx = rng_fields(h, w, SEED + 4)
    rock = np.array([58, 70, 82], dtype=np.float32)
    ice = np.array([150, 190, 210], dtype=np.float32)
    patches = smooth_noise(r, h, w, 18)
    facet = 0.5 + 0.5 * np.sin(xx * 0.22 + yy * 0.18 + patches * 4.0)
    grain = r.uniform(-12, 12, size=(h, w)).astype(np.float32)
    t = np.clip((patches - 0.55) / 0.25, 0, 1)[:, :, None]
    out = rock[None, None, :] * (0.85 + 0.15 * facet[:, :, None]) * (1 - t) + ice[None, None, :] * t
    glint = (r.uniform(0, 1, size=(h, w)) > 0.985).astype(np.float32) * 90
    out = out + glint[:, :, None] + grain[:, :, None]
    return out


def cityruins(arr, mask, h, w):
    r, yy, xx = rng_fields(h, w, SEED + 5)
    stone = np.array([150, 96, 106], dtype=np.float32)
    mortar = np.array([74, 50, 54], dtype=np.float32)
    cell = 34
    brick_x = (xx.astype(np.int32) // cell)
    brick_y = (yy.astype(np.int32) // cell)
    offset = (brick_y % 2) * (cell // 2)
    bx = ((xx.astype(np.int32) + offset) % cell)
    by = (yy.astype(np.int32) % cell)
    joint = ((bx < 2) | (bx > cell - 3) | (by < 2) | (by > cell - 3)).astype(np.float32)
    tint = (np.sin(brick_x * 12.9898 + brick_y * 78.233) * 43758.5453) % 1.0
    tint = (tint[:, :, None] - 0.5) * 24
    grain = r.uniform(-8, 8, size=(h, w)).astype(np.float32)
    brick_col = stone[None, None, :] + tint
    j = joint[:, :, None]
    out = brick_col * (1 - j) + mortar[None, None, :] * j + grain[:, :, None]
    return out


def still(arr, mask, h, w):
    """GG-T344 The Still: one polished white Concord floor slab (a board cell is one slab, so no inner seams), a
    diagonal polish glare, and faint cyan aether pooled on the shine."""
    r, yy, xx = rng_fields(h, w, SEED + 6)
    slab = np.array([204, 216, 220], dtype=np.float32)
    aether = np.array([96, 210, 212], dtype=np.float32)
    sheen = smooth_noise(r, h, w, 28)
    streak = 0.5 + 0.5 * np.sin((xx + yy) * 0.035 + sheen * 2.5)
    grain = r.uniform(-4, 4, size=(h, w)).astype(np.float32)
    body = slab[None, None, :] * (0.86 + 0.09 * streak[:, :, None] + 0.05 * sheen[:, :, None])
    pool = np.clip((sheen - 0.66) / 0.22, 0, 1)[:, :, None] * 0.28
    out = body * (1 - pool) + aether[None, None, :] * pool + grain[:, :, None]
    return out


def lanternroad(arr, mask, h, w):
    """GG-T345 The Lantern Road: one worn, moonlit blue-grey flagstone of the pilgrim stairs (a board cell is one stone,
    so no inner seams), moss in the low spots and a warm pool of lantern light on the face."""
    r, yy, xx = rng_fields(h, w, SEED + 7)
    stone = np.array([84, 96, 118], dtype=np.float32)
    moss = np.array([62, 88, 70], dtype=np.float32)
    lamp = np.array([214, 150, 72], dtype=np.float32)
    wear = smooth_noise(r, h, w, 16)
    lows = smooth_noise(r, h, w, 24)
    grain = r.uniform(-9, 9, size=(h, w)).astype(np.float32)
    body = stone[None, None, :] * (0.84 + 0.16 * wear[:, :, None])
    t = np.clip((lows - 0.62) / 0.25, 0, 1)[:, :, None] * 0.6
    out = body * (1 - t) + moss[None, None, :] * t
    cy, cx = h * 0.35, w * 0.7
    glow = np.exp(-(((yy - cy) / (h * 0.55)) ** 2 + ((xx - cx) / (w * 0.55)) ** 2))[:, :, None] * 0.22
    out = out * (1 - glow) + lamp[None, None, :] * glow + grain[:, :, None]
    return out


def cathedral(arr, mask, h, w):
    """GG-T350 The Choir's Cathedral: one polished slab of the nave's dark blue-grey marble (a board cell is one slab,
    so no inner seams), faint pale veining, and the organ's cyan aether-light pooled on the face."""
    r, yy, xx = rng_fields(h, w, SEED + 8)
    stone = np.array([52, 62, 84], dtype=np.float32)
    vein = np.array([150, 170, 190], dtype=np.float32)
    aether = np.array([96, 214, 226], dtype=np.float32)
    cloud = smooth_noise(r, h, w, 18)
    body = stone[None, None, :] * (0.86 + 0.18 * cloud[:, :, None])
    ridge = np.abs(smooth_noise(r, h, w, 10) - 0.5)
    v = np.clip((0.04 - ridge) / 0.04, 0, 1)[:, :, None] * 0.35
    out = body * (1 - v) + vein[None, None, :] * v
    cy, cx = h * 0.3, w * 0.5
    glow = np.exp(-(((yy - cy) / (h * 0.6)) ** 2 + ((xx - cx) / (w * 0.6)) ** 2))[:, :, None] * 0.2
    grain = r.uniform(-5, 5, size=(h, w)).astype(np.float32)
    out = out * (1 - glow) + aether[None, None, :] * glow + grain[:, :, None]
    return out


def bloom(arr, mask, h, w):
    """GG-T347 The Bloom: broken Concord white stone with moss in the cracks and red blossom fallen across it."""
    r, yy, xx = rng_fields(h, w, SEED + 9)
    stone = np.array([206, 204, 196], dtype=np.float32)
    moss = np.array([92, 112, 58], dtype=np.float32)
    petal = np.array([196, 28, 44], dtype=np.float32)
    cloud = smooth_noise(r, h, w, 16)
    out = stone[None, None, :] * (0.88 + 0.14 * cloud[:, :, None])
    crack = np.abs(smooth_noise(r, h, w, 12) - 0.5)
    c = np.clip((0.035 - crack) / 0.035, 0, 1)[:, :, None]
    out = out * (1 - c * 0.6) + moss[None, None, :] * (c * 0.6)
    drift = smooth_noise(r, h, w, 6)
    p = np.clip((drift - 0.72) / 0.1, 0, 1)[:, :, None] * 0.85
    out = out * (1 - p) + petal[None, None, :] * p
    grain = r.uniform(-6, 6, size=(h, w)).astype(np.float32)
    return out + grain[:, :, None]


def morrowsshadow(arr, mask, h, w):
    """GG-T348 Morrow's Shadow: the cracked pale plain under the low moon, dry grey-tan earth split by dark fissures, a
    cold blue cast from the moonlight."""
    r, yy, xx = rng_fields(h, w, SEED + 10)
    earth = np.array([184, 176, 158], dtype=np.float32)
    shade = np.array([70, 74, 86], dtype=np.float32)
    moon = np.array([170, 190, 214], dtype=np.float32)
    cloud = smooth_noise(r, h, w, 20)
    out = earth[None, None, :] * (0.84 + 0.18 * cloud[:, :, None])
    crack = np.abs(smooth_noise(r, h, w, 22) - 0.5)
    c = np.clip((0.03 - crack) / 0.03, 0, 1)[:, :, None] * 0.85
    out = out * (1 - c) + shade[None, None, :] * c
    cast = (0.12 + 0.08 * smooth_noise(r, h, w, 40))[:, :, None]
    grain = r.uniform(-7, 7, size=(h, w)).astype(np.float32)
    return out * (1 - cast) + moon[None, None, :] * cast + grain[:, :, None]


BIOMES = {
    "greenvalley": greenvalley,
    "desert": desert,
    "swamp": swamp,
    "cave": cave,
    "cityruins": cityruins,
    "still": still,
    "lanternroad": lanternroad,
    "cathedral": cathedral,
    "bloom": bloom,
    "morrowsshadow": morrowsshadow,
}


def main():
    import sys
    only = set(sys.argv[1:])  # `python tools/make_tiles.py still` writes just that theme's tile
    arr, mask = load_base()
    h, w = mask.shape
    for theme, fn in BIOMES.items():
        if only and theme not in only:
            continue
        out = arr.copy()
        painted = clamp_u8(fn(arr, mask, h, w))
        out[:, :, :3][mask] = painted[mask]
        Image.fromarray(out.astype(np.uint8), mode="RGBA").save(os.path.join(TILES, theme + ".png"))
        print("tile:", theme, "textured")


if __name__ == "__main__":
    main()
