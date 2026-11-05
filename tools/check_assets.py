"""Fails if any sound cue or music track the code references has no bundled file (no silent events),
an ability, item or status has no pixel icon, or an art asset breaks the art standard outright
(docs/POLISH.md section H). Softer art-standard misses are printed as warnings and scored by tools/polish.py."""
import glob, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RESD = os.path.join(ROOT, "Assets", "Gaiden", "Resources")
SFX = os.path.join(RESD, "Audio", "Sfx")
MUSIC = os.path.join(RESD, "Audio", "Music")
# Cue key -> file, as App/Services/Audio.Cue resolves it (GG-T254): dots become underscores, except these.
SPECIAL = {"ui.back": "ui_click", "battle.move": "battle_move_0", "battle.hit": "battle_hit_0"}

# docs/POLISH.md section H thresholds.
PERSON_CANVAS = 640          # POL-H1: a full-height person's canvas; shorter canvases are smaller creatures
MIN_FILL = 0.95              # POL-H1: figure spans >= 95% of its canvas height
FOOT_SLACK = 0.02            # POL-H1: figure reaches within 2% of the bottom edge (the foot line)
SIDE_MARGIN = 0.08           # POL-H1 (warning only): transparent margin left/right <= 8% of the width
PROPS = {"bonepile", "corpse", "gravestone"}
ICON_PX, ICON_GRID, ICON_COLORS = 88, 22, 9   # POL-H2: 22x22 pixel art at x4, 8 colours + outline
UI_ICON_PX = 64
BACKDROP_SIZE = (720, 1440)  # POL-H3
BACKDROP_MEAN = (0.25, 0.50) # POL-H3: mean luminance band shared by every backdrop
DIALOGUE_FIGURE_PX = 1200    # POL-H4: DialogueScreen.FigureHeight, the largest a portrait is drawn


def art_findings():
    """[(rule, subject, ok, detail, hard)] for every portrait, icon and backdrop."""
    from PIL import Image, ImageStat
    out = []
    pdir = os.path.join(RESD, "Portraits")
    for f in sorted(glob.glob(os.path.join(pdir, "*.png"))):
        n = os.path.basename(f)[:-4]
        im = Image.open(f)
        w, h = im.size
        hard, soft = [], []
        if im.mode not in ("RGBA", "LA"):
            hard.append("no transparency")
        if h > PERSON_CANVAS:
            hard.append(f"canvas {w}x{h} is taller than {PERSON_CANVAS}")
        if not hard and n not in PROPS:
            bb = im.getchannel("A").point(lambda v: 255 if v > 16 else 0).getbbox() or (0, 0, w, h)
            if (bb[3] - bb[1]) / h < MIN_FILL:
                soft.append(f"figure fills {(bb[3] - bb[1]) / h:.0%} of the canvas height")
            if bb[3] < h * (1 - FOOT_SLACK):
                soft.append(f"feet {h - bb[3]} px above the bottom edge")
        out.append(("POL-H1", f"Portraits/{n}.png", not (hard or soft), "; ".join(hard + soft), bool(hard)))
        if not hard and n not in PROPS:
            bb = im.getchannel("A").point(lambda v: 255 if v > 16 else 0).getbbox() or (0, 0, w, h)
            margin = max(bb[0], w - bb[2]) / w
            if margin > SIDE_MARGIN:
                out.append(("warn", f"Portraits/{n}.png", False, f"{margin:.0%} transparent side margin (crop tighter)", False))
        scale = DIALOGUE_FIGURE_PX / PERSON_CANVAS
        out.append(("POL-H4", f"Portraits/{n}.png", scale <= 1.0,
                    f"drawn at {scale:.2f}x its native size in dialogue (needs a {int(h * scale)} px tall source)", False))

    for folder, px in (("AbilityIcons", ICON_PX), ("ItemIcons", ICON_PX), ("StatusIcons", ICON_PX), ("Icons", UI_ICON_PX)):
        for f in sorted(glob.glob(os.path.join(RESD, folder, "*.png"))):
            im = Image.open(f).convert("RGBA")
            why, hard = [], False
            if im.size != (px, px):
                why.append(f"{im.size[0]}x{im.size[1]}, not {px}x{px}"); hard = True
            elif px == ICON_PX:
                small = im.resize((ICON_GRID, ICON_GRID), Image.NEAREST)
                if small.resize((px, px), Image.NEAREST).tobytes() != im.tobytes():
                    why.append(f"off the {ICON_GRID}x{ICON_GRID} pixel grid")
                cols = {p[:3] for p in small.get_flattened_data() if p[3] > 16} if hasattr(small, "get_flattened_data") \
                    else {p[:3] for p in small.getdata() if p[3] > 16}
                if len(cols) > ICON_COLORS:
                    why.append(f"{len(cols)} colours (max {ICON_COLORS})")
            out.append(("POL-H2", f"{folder}/{os.path.basename(f)}", not why, "; ".join(why), hard))

    for f in sorted(glob.glob(os.path.join(RESD, "Backgrounds", "*"))):
        if f.endswith(".meta"):
            continue
        im = Image.open(f)
        why, hard = [], False
        if not f.lower().endswith(".jpg") or im.size != BACKDROP_SIZE:
            why.append(f"{os.path.splitext(f)[1]} {im.size[0]}x{im.size[1]}, not a {BACKDROP_SIZE[0]}x{BACKDROP_SIZE[1]} JPEG"); hard = True
        mean = ImageStat.Stat(im.convert("L")).mean[0] / 255
        if not BACKDROP_MEAN[0] <= mean <= BACKDROP_MEAN[1]:
            why.append(f"mean luminance {mean:.2f} outside {BACKDROP_MEAN[0]}-{BACKDROP_MEAN[1]}")
        out.append(("POL-H3", f"Backgrounds/{os.path.basename(f)}", not why, "; ".join(why), hard))
    return out


def main():
    code = "".join(open(p, encoding="utf-8").read() for p in glob.glob(os.path.join(ROOT, "Assets", "Gaiden", "App", "**", "*.cs"), recursive=True))
    cues = set(re.findall(r'Sfx\("([^"]+)"', code))
    # (NB: a bare "A and B or []" here would silently evaluate to [] whenever A itself found nothing,
    # even though B is the pattern that actually matters — never leave that construct for the next edit.)
    cues |= set(c for pair in re.findall(r'Sfx\([^;]*?"([a-z.]+)"\s*:\s*"([a-z.]+)"', code) for c in pair)
    for m in re.findall(r'case DamageType\.\w+: return "([a-z._]+)";', code): cues.add(m)
    cues |= set(re.findall(r'StepAnim\([^;]*?"([a-z._]+)"\)', code))
    missing = [c for c in sorted(cues) if not os.path.exists(os.path.join(SFX, SPECIAL.get(c, c.replace(".", "_")) + ".ogg"))]
    tracks = set(re.findall(r'Music => "([A-Za-z]+)"', code)) | set(t for p in re.findall(r'Music => [^;]*?"([A-Z][A-Za-z]+)"\s*:\s*"([A-Z][A-Za-z]+)"', code) for t in p)
    missing += [f"music:{t}" for t in sorted(tracks) if not os.path.exists(os.path.join(MUSIC, t + ".mp3"))]

    def _ids(name): return [x["Id"] for x in json.load(open(os.path.join(RESD, "Content", name), encoding="utf-8"))]
    for folder, ids in (("AbilityIcons", _ids("abilities.json")), ("ItemIcons", _ids("items.json")), ("StatusIcons", _ids("buffs.json") + ["stunned"])):
        missing += [f"icon:{folder}/{i} (run tools/make_icons.py)" for i in ids if not os.path.exists(os.path.join(RESD, folder, i + ".png"))]

    findings = art_findings()
    broken = [f"{s}: {d}" for r, s, ok, d, hard in findings if hard]
    soft = [(r, s, d) for r, s, ok, d, hard in findings if not ok and not hard and r != "POL-H4"]
    upscaled = sum(1 for r, s, ok, d, hard in findings if r == "POL-H4" and not ok)
    for r, s, d in soft:
        print(f"art warning {r if r != 'warn' else '(composition)'} {s}: {d}")
    if upscaled:
        print(f"art warning POL-H4: {upscaled} portraits are drawn above native size in dialogue "
              f"({DIALOGUE_FIGURE_PX} px from {PERSON_CANVAS} px canvases)")
    if missing or broken:
        print("MISSING ASSETS:", ", ".join(missing + broken)); sys.exit(1)
    print(f"asset check OK ({len(cues)} cues, {len(tracks)} tracks, pixel icons, "
          f"{len(soft)} art warnings, see docs/POLISH.md section H)")


if __name__ == "__main__":
    main()
