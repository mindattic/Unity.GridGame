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
PORTRAIT_CANVAS = 1024       # POL-H1, owner 2026-11-07 (GG-T352): "all characters are 1024x1024 with transparent
                             # backgrounds; that's the rule, that's what the assets are" - never trimmed or shrunk
FIGURE_BOX = 640             # POL-H1: the figure's box (classes.json "Figure") fitted in this square, never enlarged,
                             # is its drawn size (App Art.FigureSize): 640 = one person, smaller = a smaller creature
ICON_PX, ICON_GRID, ICON_COLORS = 88, 22, 9   # POL-H2: 22x22 pixel art at x4, 8 colours + outline
UI_ICON_PX = 64
BACKDROP_MIN_H = 1536         # POL-H3 (GG-T353): a 1:2 portrait backdrop at the art's own resolution, never downscaled
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
        if im.mode != "RGBA":
            hard.append(f"{im.mode}, not RGBA (no transparency)")
        if (w, h) != (PORTRAIT_CANVAS, PORTRAIT_CANVAS):
            hard.append(f"canvas {w}x{h}, not {PORTRAIT_CANVAS}x{PORTRAIT_CANVAS} (run tools/import_art.py / portrait_cut.py --import)")
        bb = im.getchannel("A").getbbox() if im.mode == "RGBA" else None
        if not hard:
            clear = sum(im.getchannel("A").point(lambda v: 255 if v < 16 else 0).histogram()[255:]) / (w * h)
            if clear < 0.2:
                hard.append(f"only {clear:.0%} of the canvas is transparent (a solid background left in?)")
        out.append(("POL-H1", f"Portraits/{n}.png", not (hard or soft), "; ".join(hard + soft), bool(hard)))
        if bb:
            bw, bh = bb[2] - bb[0], bb[3] - bb[1]
            drawn = bh * min(1.0, FIGURE_BOX / max(bw, bh)) * DIALOGUE_FIGURE_PX / FIGURE_BOX
            scale = drawn / bh
            out.append(("POL-H4", f"Portraits/{n}.png", scale <= 1.0,
                        f"drawn at {scale:.2f}x its native size in dialogue (a {bh} px figure at {drawn:.0f} px)", False))

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
        w, h = im.size
        if not f.lower().endswith((".jpg", ".png")) or w * 2 != h or h < BACKDROP_MIN_H:
            why.append(f"{os.path.splitext(f)[1]} {w}x{h}, not a 1:2 JPEG/PNG at least {BACKDROP_MIN_H} tall"); hard = True
        if not re.fullmatch(r"[a-z]+_\d\d\.(jpg|png)", os.path.basename(f)):
            why.append("not named <region>_NN (owner rule, GG-T353)"); hard = True
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
              f"(a {FIGURE_BOX}-unit figure is {DIALOGUE_FIGURE_PX} px tall)")
    if missing or broken:
        print("MISSING ASSETS:", ", ".join(missing + broken)); sys.exit(1)
    print(f"asset check OK ({len(cues)} cues, {len(tracks)} tracks, pixel icons, "
          f"{len(soft)} art warnings, see docs/POLISH.md section H)")


if __name__ == "__main__":
    main()
