"""Draws Thumbnail Studio's icon (GG-T223): a portrait silhouette framed by a gold crop square.

    python tools/ThumbnailStudio/make_icon.py   ->  tools/ThumbnailStudio/ThumbnailStudio.ico (16-256 px)

Each size is drawn on its own at 8x and downsampled, with stroke widths floored so the 16 px icon stays crisp.
"""
from pathlib import Path

from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SS = 8  # supersampling

BG = (28, 29, 37, 255)        # studio panel
BG_EDGE = (48, 50, 63, 255)
FIGURE = (150, 160, 190, 255)  # the portrait
GOLD = (230, 195, 77, 255)     # the crop square (studio accent)


def draw(size: int) -> Image.Image:
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = s / 32.0  # one unit on a 32-grid

    # Rounded dark tile.
    r = 6 * u
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=BG, outline=BG_EDGE, width=max(SS, round(0.75 * u)))

    # Portrait silhouette: head + shoulders, cut off by the tile's bottom edge.
    hx, hy, hr = 16 * u, 13.5 * u, 5.2 * u
    d.ellipse([hx - hr, hy - hr, hx + hr, hy + hr], fill=FIGURE)
    d.chord([5 * u, 20 * u, 27 * u, 42 * u], 180, 360, fill=FIGURE)
    d.rectangle([5 * u, 31 * u, 27 * u, 32 * u], fill=FIGURE)

    # Clip the figure to the rounded tile (re-mask with the tile shape).
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=255)
    img.putalpha(Image.composite(img.getchannel("A"), Image.new("L", (s, s), 0), mask))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, outline=BG_EDGE, width=max(SS, round(0.75 * u)))

    # Gold crop square around the face: corner brackets, thick enough to read at 16 px.
    w = max(1.6 * SS, 2.2 * u) if size <= 24 else 1.9 * u
    a, b = 7.5 * u, 24.5 * u  # square edges
    L = 5.2 * u               # bracket arm length
    for (cx, cy, dx, dy) in [(a, a, 1, 1), (b, a, -1, 1), (a, b, 1, -1), (b, b, -1, -1)]:
        x0, x1 = sorted([cx, cx + dx * L])
        y0, y1 = sorted([cy, cy + dy * L])
        # horizontal arm
        d.rectangle([x0 - w / 2, cy - w / 2, x1 + w / 2, cy + w / 2], fill=GOLD)
        # vertical arm
        d.rectangle([cx - w / 2, y0 - w / 2, cx + w / 2, y1 + w / 2], fill=GOLD)

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = Path(__file__).with_name("ThumbnailStudio.ico")
    images = [draw(n) for n in SIZES]
    images[-1].save(out, format="ICO", sizes=[(n, n) for n in SIZES], append_images=images[:-1])
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
