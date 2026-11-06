"""The polish pass (GG-T251, docs/POLISH.md section 4): measure every automated Polish Standard rule, score each
dimension A-H 0-100, write a scorecard + report, and ratchet against docs/polish/baseline.json.

    python tools/polish.py                 # full pass: live sweep + aspects + uicheck + gates + lint + art
    python tools/polish.py --quick         # skip aspects and uicheck
    python tools/polish.py --no-player     # static parts only (lint, art, gates, records)
    python tools/polish.py --balance       # also run the greedy + casual bot soaks (minutes) and score dimension G
                           [--per-stage N] # casual battles per campaign stage (default 4); --keep-records keeps them
                           [--difficulty N] # the dial 1..10 the soaks fight at (default 10; GG-T309, GAIDEN_DIFFICULTY)
    python tools/polish.py --accept-dims G # merge only these dimensions' rules into the baseline
    python tools/polish.py --records DIR   # score dimension G from existing battle records
    python tools/polish.py --accept        # write this pass as the new baseline (only when nothing regressed)

Set GAIDEN_PORT to a free port when another tool may be driving a player on the default one.
Exit 1 when any dimension scored in both the baseline and this pass drops (rules measured in only one of them are
reported, never failed, so adding a measurement can't block a pass). Build the player first (tools/build.ps1).
"""
import datetime
import html
import json
import os
import statistics
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)

BASELINE = os.path.join(ROOT, "docs", "polish", "baseline.json")
DIMENSIONS = {"A": "Type", "B": "Color", "C": "Layout and touch", "D": "Information and feedback",
              "E": "Motion and VFX", "F": "Audio", "G": "Balance and pacing", "H": "Art cohesion"}
SCREENS = ["hub", "stages", "items", "status", "equip", "abilities", "talents", "respec", "party", "shop", "blacksmith",
           "alchemist", "enchantress", "talk", "summon", "mercenaries", "bounties", "quests", "crucible", "bestiary",
           "settings", "credits"]


class Tally:
    """rule -> {checked, failed, findings[]}; a rule's score is the share of subjects that pass (POLISH.md section 3)."""

    def __init__(self):
        self.rules = {}

    def add(self, rule, checked, failed, findings=()):
        r = self.rules.setdefault(rule, {"checked": 0, "failed": 0, "findings": []})
        r["checked"] += checked
        r["failed"] += failed
        r["findings"].extend(findings)

    def one(self, rule, ok, finding):
        self.add(rule, 1, 0 if ok else 1, [] if ok else [finding])

    def scores(self):
        out = {}
        for rule, r in sorted(self.rules.items()):
            if r["checked"]:
                out[rule] = round(100.0 * (r["checked"] - r["failed"]) / r["checked"], 1)
        return out


# ── live player ────────────────────────────────────────────────────────────────

def contrast(c1, c2):
    def lum(c):
        def ch(v):
            return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
        return 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2])
    a, b = sorted((lum(c1), lum(c2)), reverse=True)
    return (a + 0.05) / (b + 0.05)


def measure_contrast(tally, screen, shot_path, sb, texts):
    """POL-B2: the background behind a text element is the median color of its rect in the screenshot (glyphs cover a
    minority of a text box, so the median lands on the background, image or flat); the text color is the element's
    resolved color composited over it."""
    from PIL import Image
    try:
        img = Image.open(shot_path).convert("RGB")
    except Exception:
        return
    W, H = img.size
    s = sb["w"] / sb["design"][0]
    for t in texts:
        x0 = int(sb["x"] + t["x"] * s); y0 = int(sb["y"] + t["y"] * s)
        x1 = int(sb["x"] + (t["x"] + t["w"]) * s); y1 = int(sb["y"] + (t["y"] + t["h"]) * s)
        x0, y0, x1, y1 = max(0, x0), max(0, y0), min(W, x1), min(H, y1)
        if x1 - x0 < 2 or y1 - y0 < 2:
            continue
        cell = img.crop((x0, y0, x1, y1)).resize((min(48, x1 - x0), min(16, y1 - y0)))
        px = list(cell.get_flattened_data() if hasattr(cell, "get_flattened_data") else cell.getdata())
        bg = [statistics.median(p[i] for p in px) / 255.0 for i in range(3)]
        r, g, b, a = t["color"]
        fg = [r * a + bg[0] * (1 - a), g * a + bg[1] * (1 - a), b * a + bg[2] * (1 - a)]
        ratio = contrast(fg, bg)
        need = 3.0 if t["large"] else 4.5
        tally.one("POL-B2", ratio >= need, {"screen": screen, "subject": t["t"], "detail": f"{ratio:.1f}:1 < {need}:1",
                                            "shot": shot_path})


def audit_screen(h, tally, screen, shots, tag, fills):
    name = f"{screen}@{tag}"
    path = h.shot(f"polish-{tag}-{screen}")
    sb = h.call("stagebounds")
    lay = h.call("layout")
    p = lay.get("polish") or {}
    for rule, c in (p.get("counts") or {}).items():
        fs = [{"screen": name, "subject": f["subject"], "detail": f["detail"], "shot": path}
              for f in p.get("findings", []) if f["rule"] == rule]
        tally.add(rule, c["checkedCount"], c["failed"], fs)
    measure_contrast(tally, name, path, sb, p.get("texts") or [])
    shots[name] = path
    if p.get("fill"):
        fills[(tag, screen)] = {**p["fill"], "width": sb["design"][0]}
    return lay


def sweep_at(h, run, tag, width, height):
    """Every screen at one window shape. Returns (tally, shots, fills, log errors)."""
    tally, shots, fills = Tally(), {}, {}
    h.launch(f"{run}/{tag}", width=width, height=height, fresh=True)
    h._wait_screen("title", 10); time.sleep(1.0)
    audit_screen(h, tally, "title", shots, tag, fills)
    h.call("newgame", slot=0, gold=5000, npcs=1); time.sleep(1.2)
    audit_screen(h, tally, "hub", shots, tag, fills)
    for screen in SCREENS[1:]:
        if not h.call("goto", screen=screen).get("ok"):
            continue
        time.sleep(0.9)
        audit_screen(h, tally, screen, shots, tag, fills)
        h.call("back"); time.sleep(0.6)
    h.call("battle"); time.sleep(3.0)
    audit_screen(h, tally, "battle", shots, tag, fills)
    errors = [l for l in h.call("logs") if l.startswith(("Error", "Exception"))]
    try:
        h.call("quit", timeout=3)
    except Exception:
        pass
    time.sleep(1.5)
    return tally, shots, fills, errors


def live_sweep(tally, out, quick):
    """The layout audit on every screen at every `harness.py aspects` shape (9:23 -> 3:4), or the default window
    only with --quick. Returns per-aspect tallies so dimension C can be scored at its worst aspect."""
    import harness as h
    run = os.path.relpath(out, os.path.join(ROOT, "artifacts")).replace("\\", "/")
    shapes = [("default", 585, 1266)] if quick else [(label, int(round(1200 * a)), 1200) for label, a in h.ASPECTS]
    per_aspect, shots, fills, errors = {}, {}, {}, []
    for tag, w, hh in shapes:
        try:
            t, s, f, e = sweep_at(h, run, tag, w, hh)
        except OSError:  # the player went away mid-sweep (another tool on the port, a crash): one retry
            time.sleep(3)
            t, s, f, e = sweep_at(h, run, tag, w, hh)
        per_aspect[tag] = t
        shots.update(s); fills.update(f); errors += e
        for rule, r in t.rules.items():
            tally.add(rule, r["checked"], r["failed"], r["findings"])
    use_the_space(tally, fills)
    return shots, errors, per_aspect


def use_the_space(tally, fills):
    """POL-C6: from the 1170-wide stage (9:19.5) to the 1424-wide one (9:16) a screen's main panel (its widest
    window or scroll view; the content span when it has none) grows by at least 80% of the extra width."""
    for (tag, screen), wide in fills.items():
        if tag != "9x16" or ("9x19.5", screen) not in fills:
            continue
        narrow = fills[("9x19.5", screen)]
        extra = wide["width"] - narrow["width"]
        if extra <= 0:
            continue
        if wide.get("main") and narrow.get("main"):
            grow = (wide["main"] - narrow["main"]) / extra
        else:
            grow = ((wide["maxX"] - wide["minX"]) - (narrow["maxX"] - narrow["minX"])) / extra
        tally.one("POL-C6", grow >= 0.8, {"screen": screen, "subject": screen,
                                          "detail": f"content grew {grow:.0%} of the extra {extra:.0f}px"})


# ── static parts ───────────────────────────────────────────────────────────────

def lint(tally):
    import polish_lint
    for rule, r in polish_lint.lint().items():
        tally.add(rule, r["checked"], r["failed"],
                  [{"screen": "code", "subject": f, "detail": f"{n} hit(s)"} for f, n in r["worst"]])


def art(tally):
    """POL-H1..H4 from check_assets.art_findings (one rule set for the gate and the score)."""
    sys.path.insert(0, TOOLS)
    import check_assets
    for rule, subject, ok, detail, _hard in check_assets.art_findings():
        if rule.startswith("POL-"):
            tally.one(rule, ok, {"screen": "art", "subject": subject, "detail": detail})


def audio(tally):
    """POL-F2 SFX loudness and POL-F3 music loudness from tools/loudness.py; POL-F3 ducking hooks and POL-F4 variant
    pools and the repeat throttle read from the code (POL-F1 comes from the lint)."""
    sys.path.insert(0, TOOLS)
    import loudness
    rep = loudness.report()
    for kind, rule in (("sfx", "POL-F2"), ("music", "POL-F3")):
        for f in rep[kind]["files"]:
            tally.one(rule, f["ok"], {"screen": "audio", "subject": f["file"],
                                      "detail": f"{f['lufs']} LUFS, {f['dbtp']} dBTP (target {rep[kind]['target']})"})
    app = os.path.join(ROOT, "Assets", "Gaiden", "App")

    def src(rel):
        p = os.path.join(app, rel)
        return open(p, encoding="utf-8").read() if os.path.exists(p) else ""
    for rel, what in (("Battle/BattleScreen.CutIns.cs", "cut-ins"), ("Screens/LevelUpScreen.cs", "level-up fanfare"),
                      ("Screens/DialogueScreen.cs", "dialogue")):
        tally.one("POL-F3", "Audio.DuckFor(" in src(rel), {"screen": "audio", "subject": what, "detail": "music not ducked"})
    sfx = os.listdir(loudness.SFX_DIR)
    for pool in ("battle_move_", "battle_hit_"):
        n = sum(1 for f in sfx if f.startswith(pool) and f.endswith(".ogg"))
        tally.one("POL-F4", n >= 3, {"screen": "audio", "subject": pool.rstrip("_"), "detail": f"{n} variants"})
    tally.one("POL-F4", "lastPlayed" in src("Services/Audio.cs"), {"screen": "audio", "subject": "throttle",
                                                                    "detail": "no repeat throttle"})


def gates():
    out = {}
    for name, cmd in (("glyphs", ["python", os.path.join(TOOLS, "check_glyphs.py")]),
                      ("assets", ["python", os.path.join(TOOLS, "check_assets.py")])):
        r = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
                           env={**os.environ, "PYTHONIOENCODING": "utf-8"})
        out[name] = {"ok": r.returncode == 0, "tail": (r.stdout + r.stderr).strip().splitlines()[-3:]}
    return out


def uicheck():
    r = subprocess.run(["python", os.path.join(TOOLS, "uicheck.py")], cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    rep = os.path.join(ROOT, "artifacts", "uicheck", "report.json")
    summary = None
    try:
        summary = json.load(open(rep, encoding="utf-8"))
    except Exception:
        pass
    return {"ok": r.returncode == 0, "tail": (r.stdout + r.stderr).strip().splitlines()[-3:], "report": rep if summary else None}


def campaign_order():
    try:
        camp = json.load(open(os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content", "campaign.json"), encoding="utf-8"))
        return [sid for t in camp["Themes"] for sid in t["Stages"]]
    except Exception:
        return []


def balance(tally, rec_dirs):
    """Dimension G from battle records (records.py). POL-G1 casual win band and HP-left band per stage, POL-G2 no
    spike between consecutive stages, POL-G3 mana waste and full-bank share per battle, POL-G4 ability use share,
    POL-G5 impact band (against the median of the same ability kind, a stand-in for the role median), POL-G6
    actions per completed wave, POL-G7 no role composition winning > 90% for the casual bot."""
    import records
    recs = records.load(rec_dirs if isinstance(rec_dirs, list) else [rec_dirs], None)
    if not recs:
        return 0
    for r in recs:
        _, mana = records.ability_usage([r])
        if mana["minted"]:
            waste = mana["overflowed"] / mana["minted"]
            tally.one("POL-G3", waste <= 0.20, {"screen": "records", "subject": r.get("stage", "?"), "detail": f"{waste:.0%} of orbs wasted ({r.get('source')})"})
        turns = r["turns"]
        if turns:
            full = sum(1 for t in turns if t["before"].get("bankCapacity") and t["before"]["bank"] >= t["before"]["bankCapacity"])
            tally.one("POL-G3", full / len(turns) <= 0.08, {"screen": "records", "subject": r.get("stage", "?"), "detail": f"bank full on {full / len(turns):.0%} of turns ({r.get('source')})"})
    barred, used = {}, {}
    for r in recs:
        stats, _ = records.ability_usage([r])
        for a in records.barred_abilities([r]):
            barred[a] = barred.get(a, 0) + 1
            if stats[a]["uses"]:
                used[a] = used.get(a, 0) + 1
    for a, n in sorted(barred.items()):
        share = used.get(a, 0) / n
        tally.one("POL-G4", share >= 0.25, {"screen": "records", "subject": a, "detail": f"fired in {share:.0%} of {n} battles"})
    stats, _ = records.ability_usage(recs)
    known = records.known_abilities()
    by_kind = {}
    for k, c in stats.items():
        if c["uses"] >= 3:
            kind = "Item" if k.startswith("item:") else known.get(k, {}).get("Kind", "?")
            by_kind.setdefault(kind, []).append((k, records.impact(c)))
    for kind, rows in by_kind.items():
        med = statistics.median(v for _, v in rows) or 1
        for k, v in rows:
            tally.one("POL-G5", 0.5 * med <= v <= 2 * med, {"screen": "records", "subject": k, "detail": f"impact {v:.0f} vs {kind} median {med:.0f}"})
    for r in recs:
        waves = {}
        for t in r["turns"]:
            waves[t.get("wave") or 0] = waves.get(t.get("wave") or 0, 0) + 1
        won = r["outcome"]["phase"] == "Victory"
        last = max(waves) if waves else 0
        for w, n in waves.items():
            if w == last and not won:
                continue  # a wave the party fell or left on isn't a finished wave
            tally.one("POL-G6", 6 <= n <= 20, {"screen": "records", "subject": f"{r.get('stage', '?')} wave {w + 1}", "detail": f"{n} actions"})
    stages = {}
    for r in recs:
        if "casual" not in str(r.get("source", "")):
            continue
        s = stages.setdefault(r.get("stage", "?"), {"n": 0, "wins": 0, "hp": []})
        s["n"] += 1
        if r["outcome"]["phase"] == "Victory":
            s["wins"] += 1
            maxhp = sum(h.get("maxHp", 0) for h in r["party"]) or 1
            last_hp = r["turns"][-1]["after"].get("heroHp", []) if r["turns"] else []
            s["hp"].append(sum(last_hp) / maxhp)
    for st, s in stages.items():
        rate = s["wins"] / s["n"]
        tally.one("POL-G1", 0.55 <= rate <= 0.85, {"screen": "records", "subject": st, "detail": f"casual wins {s['wins']}/{s['n']} ({rate:.0%})"})
        if s["hp"]:
            hp = statistics.mean(s["hp"])
            tally.one("POL-G1", 0.20 <= hp <= 0.60, {"screen": "records", "subject": st, "detail": f"{hp:.0%} party HP left on victories"})
    order = [sid for sid in campaign_order() if sid in stages]
    for prev, cur in zip(order, order[1:]):
        a, b = stages[prev]["wins"] / stages[prev]["n"], stages[cur]["wins"] / stages[cur]["n"]
        tally.one("POL-G2", b >= a - 0.20, {"screen": "records", "subject": cur, "detail": f"casual wins {b:.0%} after {prev} {a:.0%}"})
    comps = {}
    for r in recs:
        if "casual" not in str(r.get("source", "")):
            continue
        key = "+".join(sorted(h.get("role", "?") for h in r["party"] if not h.get("guest")))
        c = comps.setdefault(key, {"n": 0, "wins": 0, "stages": set()})
        c["n"] += 1
        c["wins"] += r["outcome"]["phase"] == "Victory"
        c["stages"].add(r.get("stage"))
    for key, c in sorted(comps.items()):
        if c["n"] >= 5 and len(c["stages"]) >= 3:
            rate = c["wins"] / c["n"]
            tally.one("POL-G7", rate <= 0.90, {"screen": "records", "subject": key, "detail": f"casual wins {c['wins']}/{c['n']} across {len(c['stages'])} stages"})
    return len(recs)


def run_soak(out, per_stage=4, difficulty=None):
    """The two measurement soaks: the greedy bot through every hero class (ability use, POL-G3..G6) and the casual
    bot fighting each campaign stage `per_stage` times with rotating parties (POL-G1, G2, G7)."""
    proj = os.path.join(ROOT, "Tests", "Core.Tests", "Core.Tests.csproj")
    dirs = []
    for label, extra in (("greedy", {}), ("casual", {"GAIDEN_ABILITY_BOT": "casual", "GAIDEN_ABILITY_PER_STAGE": str(per_stage)})):
        d = os.path.join(out, "records", label)
        os.makedirs(d, exist_ok=True)
        subprocess.run(["dotnet", "test", proj, "--filter", "FullyQualifiedName~AbilityUsageSoak"], cwd=ROOT,
                       env={**os.environ, "GAIDEN_ABILITY_RECORDS": d, **extra,
                            **({"GAIDEN_DIFFICULTY": str(difficulty)} if difficulty else {})})
        dirs.append(d)
    return dirs


# ── scoring, ratchet, report ───────────────────────────────────────────────────

def dimension_scores(rule_scores):
    dims = {}
    for d in DIMENSIONS:
        vals = [v for r, v in rule_scores.items() if r.startswith(f"POL-{d}")]
        dims[d] = round(sum(vals) / len(vals), 1) if vals else None
    return dims


def ratchet(rule_scores, base):
    """Compare each dimension over the rules both passes measured, so new measurements never fail the pass."""
    if not base:
        return [], {}
    prev = base.get("rules", {})
    common = {r for r in rule_scores if r in prev}
    now = dimension_scores({r: rule_scores[r] for r in common})
    then = dimension_scores({r: prev[r] for r in common})
    drops = [f"{d} {DIMENSIONS[d]}: {then[d]} -> {now[d]}" for d in DIMENSIONS
             if now[d] is not None and then[d] is not None and now[d] < then[d] - 0.05]
    return drops, {d: (then[d], now[d]) for d in DIMENSIONS}


def write_report(out, card, tally):
    rows = []
    for d, name in DIMENSIONS.items():
        rows.append(f"<h2>{d}. {html.escape(name)} - {card['dimensions'][d] if card['dimensions'][d] is not None else 'not measured'}</h2>")
        for rule, r in sorted(tally.rules.items()):
            if not rule.startswith(f"POL-{d}"):
                continue
            sc = card["rules"].get(rule)
            rows.append(f"<h3>{rule}: {sc} ({r['failed']} of {r['checked']} fail)</h3><ul>")
            for f in r["findings"][:40]:
                shot = f.get("shot")
                link = f' <a href="{html.escape(os.path.relpath(shot, out))}">shot</a>' if shot else ""
                rows.append(f"<li><b>{html.escape(str(f.get('screen')))}</b> {html.escape(str(f.get('subject')))}: "
                            f"{html.escape(str(f.get('detail')))}{link}</li>")
            rows.append("</ul>")
    gates = "".join(f"<li>{html.escape(k)}: {'ok' if v['ok'] else 'FAIL'}</li>" for k, v in card["gates"].items())
    drops = "".join(f"<li>{html.escape(x)}</li>" for x in card["regressions"]) or "<li>none</li>"
    page = f"""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Polish Scorecard</title><style>body{{font:15px/1.5 system-ui,sans-serif;max-width:960px;margin:0 auto;padding:16px;background:#0e1222;color:#e8eaf6}}
h2{{border-top:1px solid #333a5c;padding-top:12px}}h3{{font-size:15px;margin:10px 0 2px}}a{{color:#8fb3ff}}li{{margin:2px 0}}</style></head><body>
<h1>Polish scorecard {html.escape(card['date'])}</h1>
<p>{' · '.join(f"{d} {card['dimensions'][d] if card['dimensions'][d] is not None else '–'}" for d in DIMENSIONS)}</p>
<h2>Regressions</h2><ul>{drops}</ul><h2>Gates</h2><ul>{gates}</ul>{''.join(rows)}</body></html>"""
    with open(os.path.join(out, "report.html"), "w", encoding="utf-8") as fh:
        fh.write(page)


def main():
    args = sys.argv[1:]
    quick, no_player, accept = "--quick" in args, "--no-player" in args, "--accept" in args
    rec_dir = args[args.index("--records") + 1] if "--records" in args else None
    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M")
    out = os.path.join(ROOT, "artifacts", "polish", stamp)
    os.makedirs(out, exist_ok=True)
    tally = Tally()
    errors, shots, ui, per_aspect = [], {}, None, {}
    if not no_player:
        shots, errors, per_aspect = live_sweep(tally, out, quick)
        if not quick:
            ui = uicheck()
    lint(tally)
    art(tally)
    audio(tally)
    g = gates()
    if ui is not None:
        g["uicheck"] = ui
    g["log-errors"] = {"ok": not errors, "tail": errors[:3]}
    soaked = None
    if "--balance" in args:
        per_stage = int(args[args.index("--per-stage") + 1]) if "--per-stage" in args else 4
        difficulty = int(args[args.index("--difficulty") + 1]) if "--difficulty" in args else None
        rec_dir = soaked = run_soak(out, per_stage, difficulty)
    battles = balance(tally, rec_dir) if rec_dir else 0
    if soaked and "--keep-records" not in args:
        import shutil
        shutil.rmtree(os.path.join(out, "records"), ignore_errors=True)  # MAT-T90: soak records are scratch, not artifacts

    rules = tally.scores()
    # Dimension C is scored at its worst aspect: a layout that only works on one phone shape isn't polished.
    aspect_scores = {tag: t.scores() for tag, t in per_aspect.items()}
    worst_aspect = {}
    for rule in rules:
        if rule.startswith("POL-C"):
            per = [(s[rule], tag) for tag, s in aspect_scores.items() if rule in s]
            if per:
                rules[rule], worst_aspect[rule] = min(per)
    base =json.load(open(BASELINE, encoding="utf-8")) if os.path.exists(BASELINE) else None
    drops, compared = ratchet(rules, base)
    card = {"date": stamp, "dimensions": dimension_scores(rules), "rules": rules,
            "counts": {r: {"checked": v["checked"], "failed": v["failed"]} for r, v in tally.rules.items()},
            "gates": g, "regressions": drops, "compared": compared, "battles": battles,
            "worstAspect": worst_aspect, "aspects": aspect_scores,
            "screens": sorted(shots)}
    with open(os.path.join(out, "scorecard.json"), "w", encoding="utf-8") as fh:
        json.dump({**card, "findings": {r: v["findings"][:200] for r, v in tally.rules.items()}}, fh, indent=1)
    write_report(out, card, tally)

    print(f"Polish scorecard {stamp} -> {os.path.relpath(out, ROOT)}")
    for d, name in DIMENSIONS.items():
        print(f"  {d} {name:26} {card['dimensions'][d] if card['dimensions'][d] is not None else 'not measured'}")
    worst = sorted(((v, r) for r, v in rules.items()))[:5]
    print("  lowest rules:", ", ".join(f"{r} {v}" for v, r in worst))
    print("  gates:", ", ".join(f"{k} {'ok' if v['ok'] else 'FAIL'}" for k, v in g.items()))
    if drops:
        print("REGRESSIONS:", *drops, sep="\n  ")
        sys.exit(1)
    if "--accept-dims" in args:
        # Merge only the named dimensions' rules into the baseline, e.g. after a --balance pass: --accept-dims G.
        dims = args[args.index("--accept-dims") + 1].upper()
        merged = dict((base or {}).get("rules", {}))
        merged = {r: v for r, v in merged.items() if r[4:5] not in dims}
        merged.update({r: v for r, v in rules.items() if r[4:5] in dims})
        with open(BASELINE, "w", encoding="utf-8") as fh:
            json.dump({"date": stamp, "dimensions": dimension_scores(merged), "rules": merged}, fh, indent=1)
        print(f"baseline dimensions {dims} merged:", os.path.relpath(BASELINE, ROOT))
    elif accept or (base is None and not quick and not no_player):
        os.makedirs(os.path.dirname(BASELINE), exist_ok=True)
        with open(BASELINE, "w", encoding="utf-8") as fh:
            json.dump({"date": stamp, "dimensions": card["dimensions"], "rules": rules}, fh, indent=1)
        print("baseline written:", os.path.relpath(BASELINE, ROOT))


if __name__ == "__main__":
    main()
