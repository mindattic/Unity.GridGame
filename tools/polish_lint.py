"""Static Polish Standard lint (GG-T251, docs/POLISH.md): counts literal colors, sizes, spacing and durations
written at the call site instead of through the design tokens (App/UI/Tokens.cs, Theme.uss :root), faux bold,
per-site text-fit checks, random durations, hand-written easing curves and off-taxonomy sound cues.

    python tools/polish_lint.py            # table
    python tools/polish_lint.py --json     # machine-readable, read by tools/polish.py

Scores follow POLISH.md section 3: a rule's score is the share of its subjects that pass. For "token" rules the
subject is each use site (a token use passes, a literal fails); for "never" rules the subject is each file
(a file with zero hits passes); for POL-F1 the subject is each distinct cue name.
"""
import json
import os
import re
import sys
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "Assets", "Gaiden", "App")
USS = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "UI", "Theme.uss")
TOKEN_FILES = {os.path.join(APP, "UI", "Tokens.cs")}
SKIP_DIRS = {os.path.join(APP, "Automation")}  # dev-only tooling, never shipped

NUM = r"-?\d+(?:\.\d+)?f?"

# rule -> (kind, literal regexes, token regexes)
CS_RULES = {
    "POL-A1": ("token", [rf"\bfontSize\s*=\s*{NUM}\b", rf"\bFloat\([^;]*?,\s*{NUM}\s*\)"], [r"\bTypeScale\.\w+"]),
    "POL-B1": ("token", [r"\bnew\s+Color32?\s*\(", r"\bColor\.(?:white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b"],
               [r"\bPalette\.\w+"]),
    "POL-C1": ("token", [rf"\bstyle\.(?:margin|padding)\w*\s*=\s*{NUM}\b", rf"\bstyle\.(?:width|height|left|right|top|bottom)\s*=\s*{NUM}\b"],
               [r"\bSpace\.\w+"]),
    "POL-E1": ("token", [rf"\bTween\.(?:Run|Wait|Move|Fade|Shake|Pop)\(\s*{NUM}\s*[,)]"], [r"\bTiming\.\w+"]),
    "POL-E2": ("token", [r"Mathf\.Sin\(\s*\w+\s*\*\s*Mathf\.PI\s*\)", r"\bMathf\.SmoothStep\("], [r"\bEase\.\w+"]),
    "POL-A3": ("never", [r"\bFontStyle\.Bold(?:AndItalic)?\b"], []),
    "POL-A6": ("never", [r"\.Length\s*>\s*\d+\s*\?"], []),
    "POL-E3": ("never", [r"\bTween\.\w+\(\s*Random\.Range"], []),
}
USS_RULES = {
    "POL-A1": ("token", [r"font-size:\s*\d"], [r"font-size:\s*var\(--type"]),
    "POL-B1": ("token", [r"\brgba?\(", r"#[0-9a-fA-F]{3,8}\b"], [r"var\(--color"]),
    "POL-C1": ("token", [r"\b(?:margin|padding)[\w-]*:\s*-?\d+px"], [r"\b(?:margin|padding)[\w-]*:\s*var\(--space"]),
    "POL-A3": ("never", [r"-unity-font-style:\s*bold"], []),
}
CUE = re.compile(r"\bSfx\(\s*\"([^\"]+)\"")
CUE_OK = re.compile(r"^(ui|battle|element|status|story)\.")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def uss_without_root(text):
    # token definitions live in :root and are not literals at a use site
    return re.sub(r":root\s*\{.*?\}", "", strip_comments(text), flags=re.S)


def files():
    for d, _, fs in os.walk(APP):
        if any(d.startswith(s) for s in SKIP_DIRS):
            continue
        for f in fs:
            p = os.path.join(d, f)
            if f.endswith(".cs") and p not in TOKEN_FILES:
                yield p


def lint():
    lit = defaultdict(Counter)   # rule -> file -> literal count
    tok = defaultdict(Counter)   # rule -> file -> token count
    subjects_files = []
    cues = set()
    for p in files():
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        subjects_files.append(rel)
        text = strip_comments(open(p, encoding="utf-8").read())
        for rule, (_, lits, toks) in CS_RULES.items():
            for rx in lits:
                n = len(re.findall(rx, text))
                if n:
                    lit[rule][rel] += n
            for rx in toks:
                n = len(re.findall(rx, text))
                if n:
                    tok[rule][rel] += n
        cues.update(CUE.findall(text))
    rel = os.path.relpath(USS, ROOT).replace("\\", "/")
    subjects_files.append(rel)
    uss = uss_without_root(open(USS, encoding="utf-8").read())
    for rule, (_, lits, toks) in USS_RULES.items():
        for rx in lits:
            n = len(re.findall(rx, uss))
            # a var() use matches no literal regex, but font-size/margin token uses must not double count
            if n:
                lit[rule][rel] += n
        for rx in toks:
            n = len(re.findall(rx, uss))
            if n:
                tok[rule][rel] += n
    # token uses inside USS literal regexes (font-size: var(...)) never matched "\d", so no correction is needed

    kinds = {r: k for r, (k, _, _) in {**USS_RULES, **CS_RULES}.items()}
    out = {}
    for rule in sorted(kinds):
        if kinds[rule] == "token":
            t, l = sum(tok[rule].values()), sum(lit[rule].values())
            checked, failed = t + l, l
        else:
            checked, failed = len(subjects_files), len(lit[rule])
        out[rule] = {
            "kind": kinds[rule], "checked": checked, "failed": failed,
            "score": round(100.0 * (checked - failed) / checked, 1) if checked else None,
            "literals": sum(lit[rule].values()), "tokens": sum(tok[rule].values()),
            "worst": lit[rule].most_common(5),
        }
    bad = sorted(c for c in cues if not CUE_OK.match(c))
    out["POL-F1"] = {
        "kind": "cue", "checked": len(cues), "failed": len(bad),
        "score": round(100.0 * (len(cues) - len(bad)) / len(cues), 1) if cues else None,
        "worst": [(c, 1) for c in bad[:12]],
    }
    return out


def main():
    res = lint()
    if "--json" in sys.argv:
        print(json.dumps(res, indent=1))
        return
    print(f"{'rule':8} {'score':>6} {'fail':>5}/{'chk':<5} worst")
    for rule, r in res.items():
        worst = ", ".join(f"{os.path.basename(f)}:{n}" for f, n in r["worst"][:3])
        print(f"{rule:8} {str(r['score']):>6} {r['failed']:>5}/{r['checked']:<5} {worst}")


if __name__ == "__main__":
    main()
