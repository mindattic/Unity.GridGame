"""Fails if any non-ASCII character used in game text/code isn't covered by the bundled font chain
(Noto Sans -> Noto Sans Symbols 2 -> Outfit; Cinzel for titles). Every selectable body-font preset
(Settings -> Text style: Classic/Outfit, Lato, Roboto, Play, Montserrat, Inter) falls back through
this same chain (Fonts.Make), so a glyph only has to be covered by it, not by the active preset
itself. Prevents missing-glyph boxes."""
import glob, os, sys
from fontTools.ttLib import TTFont
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Fonts")
cover = set()
for f in ("NotoSans", "NotoSansSymbols2", "Outfit"):
    cover |= set(TTFont(os.path.join(FONTS, f + ".ttf")).getBestCmap().keys())
files = glob.glob(os.path.join(ROOT, "Assets", "Gaiden", "App", "**", "*.cs"), recursive=True)
files += glob.glob(os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content", "*.json"))
files += glob.glob(os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content", "Text", "*.json"))  # story text from Prose (GG-T355)
files += [os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Credits.txt")]
bad = {}
for p in files:
    text = open(p, encoding="utf-8").read()
    for i, line in enumerate(text.splitlines(), 1):
        if p.endswith(".cs") and line.strip().startswith("//"):
            continue
        for ch in line:
            if ord(ch) > 127 and ord(ch) not in cover and ch not in "﻿":
                bad.setdefault(ch, []).append(f"{os.path.relpath(p, ROOT)}:{i}")
if bad:
    for ch, where in bad.items():
        # Not ch!r: the character itself may not be representable in the console's output encoding
        # (e.g. cp1252 on Windows), which would crash the report instead of printing it.
        print(f"MISSING GLYPH U+{ord(ch):04X} used at {where[:3]}")
    sys.exit(1)
print(f"glyph check OK ({len(files)} files)")
