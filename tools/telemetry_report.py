#!/usr/bin/env python3
"""GG-T63: endless telemetry report.

    python tools/telemetry_report.py <session> [--band-size 5] [--min-samples 3] [--all-stages]

Reads the MindAttic.Telemetry sessions an endless run wrote (one .ndjson session per battle; the GG-T59
"encounter" event record in each is the per-encounter summary) and writes a balance report to
artifacts/endless/<session>/report.md and report.html:

  * win rate and average turns / duration by enemy family, hero class, location and level band;
  * family x level-band heatmaps (wave clear rate, encounter win rate, turns per wave, threat);
  * outliers: always-lost (too hard) and never-threatening (too easy) wave groups and family/band cells,
    stalled or dragging encounters, hero classes and abilities with outsized or zero impact -- each
    flagged encounter with the seed that reproduces it (`python tools/harness.py endless 1 --seed S`).

<session> may be
  * a harness endless run name (artifacts/<run>/endless-summary.json, as `harness.py endless --run <run>`
    writes): the run's own sessions are picked out of its telemetry folders by seed and time window, and its
    lost encounters (crash / stall / hang, no session written) are listed as stalled;
  * a session file or a folder of session files (searched recursively);
  * a telemetry session id or any part of a session folder name (e.g. a date "2026-11-02"), matched under
    the telemetry roots (R:\\Telemetry\\GridGameGaiden, then Documents\\MindAttic\\Telemetry\\GridGameGaiden).

Only endless stages (stage id "endless-*") are counted unless --all-stages is passed or <session> is an
explicit file/folder. Stdlib only.
"""
import argparse
import datetime as dt
import glob
import html
import json
import os
import statistics
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = "GridGameGaiden"
CONTENT = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content")
BASIC_KEYS = {"attack", "support-attack", "other", "shatter"}
WIN = "victory"
STALL_OUTCOMES = {"stalled", "unrecorded", "crash", "stall", "hang"}


def telemetry_roots(extra=None):
    roots = list(extra or [])
    roots.append(r"R:\Telemetry")
    roots.append(os.path.join(os.path.expanduser("~"), "Documents", "MindAttic", "Telemetry"))
    out = []
    for r in roots:
        d = r if os.path.basename(os.path.normpath(r)) == APP else os.path.join(r, APP)
        if os.path.isdir(d) and d not in out:
            out.append(d)
    return out


# ---------------------------------------------------------------- loading

def session_files(path):
    if os.path.isfile(path):
        return [path]
    files = []
    for pat in ("*.ndjson", "*.ndjson.tmp", "*.jsonl"):
        files += glob.glob(os.path.join(path, "**", pat), recursive=True)
    return sorted(set(files))


def parse_time(s):
    try:
        s = s.replace("Z", "+00:00")
        if "." in s:  # .NET writes 7 fractional digits; Python wants at most 6
            head, rest = s.split(".", 1)
            n = 0
            while n < len(rest) and rest[n].isdigit():
                n += 1
            frac, tz = rest[:n], rest[n:]
            s = f"{head}.{frac[:6]}{tz}"
        t = dt.datetime.fromisoformat(s)
        return t if t.tzinfo else t.replace(tzinfo=dt.timezone.utc)
    except (ValueError, AttributeError):
        return None


def read_session(path):
    """One session file -> {file, id, start, encounters[], end, first_at, last_at}. Tolerates a torn last line."""
    s = {"file": path, "id": None, "start": None, "encounters": [], "end": None, "first_at": None, "last_at": None}
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    r = json.loads(line)
                except json.JSONDecodeError:
                    continue
                at = parse_time(r.get("atUtc", ""))
                if at:
                    s["first_at"] = s["first_at"] or at
                    s["last_at"] = at
                kind = str(r.get("kind", "")).lower()
                p = r.get("payload")
                if kind == "sessionstart" and isinstance(p, dict):
                    s["start"] = p
                    s["id"] = p.get("sessionId")
                elif kind == "event" and r.get("name") == "encounter" and isinstance(p, dict):
                    s["encounters"].append(p)
                elif kind == "sessionend":
                    s["end"] = p
    except OSError as e:
        print(f"telemetry_report: can't read {path}: {e}", file=sys.stderr)
    if not s["id"]:
        s["id"] = os.path.basename(path).split(".")[0]
    return s


def find_summary(name):
    for p in (os.path.join(ROOT, "artifacts", name, "endless-summary.json"),
              os.path.join(ROOT, "artifacts", "endless", name, "endless-summary.json")):
        if os.path.isfile(p):
            return p
    return None


def resolve(session, extra_roots, all_stages):
    """-> (name, sessions, lost_encounters, endless_only, source description)"""
    if os.path.exists(session):
        files = session_files(session)
        name = os.path.basename(os.path.normpath(session)).split(".")[0]
        # An explicit file/folder is taken as-is: every stage in it counts.
        return name, [read_session(f) for f in files], [], False, f"path {os.path.abspath(session)}"

    summary_path = find_summary(session)
    if summary_path:
        with open(summary_path, encoding="utf-8") as f:
            summary = json.load(f)
        seeds = {e["seed"] for e in summary.get("encounters", [])} | {e["seed"] for e in summary.get("lost_encounters", [])}
        end = dt.datetime.fromtimestamp(os.path.getmtime(summary_path), dt.timezone.utc)
        start = end - dt.timedelta(minutes=float(summary.get("minutes") or 0) + 10)
        dirs = [d for d in summary.get("telemetry_dirs", []) if os.path.isdir(d)] or telemetry_roots(extra_roots)
        sessions = []
        for d in dirs:
            for f in session_files(d):
                # cheap prefilter on the folder's file time before parsing
                if dt.datetime.fromtimestamp(os.path.getmtime(f), dt.timezone.utc) < start:
                    continue
                s = read_session(f)
                seed = (s["start"] or {}).get("seed")
                if seed in seeds and s["first_at"] and start <= s["first_at"] <= end + dt.timedelta(minutes=2):
                    sessions.append(s)
        return session, sessions, summary.get("lost_encounters", []), not all_stages, f"harness run {summary_path}"

    roots = telemetry_roots(extra_roots)
    files = []
    for r in roots:
        for d in glob.glob(os.path.join(r, f"*{glob.escape(session)}*")):
            files += session_files(d)
    if not files:
        raise SystemExit(f"telemetry_report: no session, harness run or telemetry folder matches '{session}' "
                         f"(looked in artifacts/ and {', '.join(roots) or 'no telemetry roots found'})")
    return session, [read_session(f) for f in sorted(set(files))], [], not all_stages, f"telemetry match '{session}' in {', '.join(roots)}"


def load_content():
    """Optional: {class id: [ability ids]} and {ability id: def} for the zero-impact ability check."""
    try:
        with open(os.path.join(CONTENT, "classes.json"), encoding="utf-8") as f:
            # The default loadout is what a fielded hero actually carries (its full Abilities list includes
            # skill-tree unlocks a low-level hero hasn't learned, which would flag as noise).
            classes = {c["Id"]: [k for k in (c.get("DefaultLoadout") or []) if not str(k).startswith("item:")]
                       for c in json.load(f) if c.get("Team") == "Hero"}
        with open(os.path.join(CONTENT, "abilities.json"), encoding="utf-8") as f:
            abilities = {a["Id"]: a for a in json.load(f)}
        return classes, abilities
    except (OSError, ValueError, KeyError):
        return {}, {}


# ---------------------------------------------------------------- rows

def band_of(level, size):
    level = max(1, int(level or 1))
    lo = (level - 1) // size * size + 1
    return f"{lo}-{lo + size - 1}"


def band_key(band):
    return int(band.split("-")[0])


def build_rows(sessions, lost, endless_only, band_size):
    rows = []
    seen_seeds = set()
    for s in sessions:
        start = s["start"] or {}
        stage = (start.get("tags") or {}).get("stage")
        if s["encounters"]:
            for e in s["encounters"]:
                if endless_only and not str(e.get("stageId") or stage or "").startswith("endless"):
                    continue
                rows.append(make_row(e, s, band_size))
                seen_seeds.add(e.get("seed"))
        else:
            if endless_only and not str(stage or "").startswith("endless"):
                continue
            # A battle session that never wrote its encounter record: crashed, killed or still running.
            rows.append(stub_row(start.get("seed"), stage, "unrecorded", s, band_size,
                                 note="session has no encounter record (crash, kill or still running)"))
            seen_seeds.add(start.get("seed"))
    for l in lost:
        if l.get("seed") in seen_seeds:
            for r in rows:
                if r["seed"] == l.get("seed") and r["outcome"] == "unrecorded":
                    r["outcome"] = l.get("kind", "lost")
                    r["note"] = f"harness: {l.get('kind')} after {l.get('after_s')}s on launch {l.get('launch')}"
            continue
        rows.append(stub_row(l.get("seed"), l.get("stage"), l.get("kind", "lost"), None, band_size,
                             note=f"harness: {l.get('kind')} after {l.get('after_s')}s on launch {l.get('launch')}, no session"))
    rows.sort(key=lambda r: (r["seed"] is None, r["seed"] or 0))
    return rows


def stub_row(seed, stage, outcome, s, band_size, note=""):
    return {"seed": seed, "stage": stage, "location": stage or "?", "outcome": outcome, "win": False, "recorded": False,
            "turns": None, "seconds": None, "band": None, "families": [], "waves": [], "party": [], "party_hp": 0,
            "damage_taken": 0, "hero_deaths": 0, "threat": None, "by_ability": {}, "waves_cleared": 0, "total_waves": 0,
            "session": s["id"] if s else None, "file": s["file"] if s else None, "note": note}


def make_row(e, s, band_size):
    party = e.get("party") or []
    party_hp = sum(h.get("maxHp") or 0 for h in party)
    waves = []
    for w in e.get("waves") or []:
        if not w.get("started"):
            continue
        levels = w.get("enemyLevels") or []
        fams = w.get("families") or ["?"]
        waves.append({
            "index": w.get("index"), "group": w.get("group") or "+".join(sorted(fams)),
            "families": fams, "band": band_of(max(levels) if levels else 1, band_size),
            "cleared": bool(w.get("cleared")), "turns": w.get("turns") or 0, "seconds": w.get("seconds") or 0.0,
            "damage_taken": w.get("damageTaken") or 0,
            "threat": (w.get("damageTaken") or 0) / party_hp if party_hp else 0.0,
        })
    families = sorted({f for w in waves for f in w["families"]})
    loc = e.get("location") or e.get("stageId") or "?"
    return {
        "seed": e.get("seed"), "stage": e.get("stageId"), "location": loc.replace(" — Endless", ""),
        "outcome": e.get("outcome") or "?", "win": e.get("outcome") == WIN, "recorded": True,
        "turns": e.get("turns") or 0, "seconds": e.get("seconds") or 0.0,
        "band": band_of(e.get("levelBandMax") or e.get("recommendedLevel") or 1, band_size),
        "families": families, "waves": waves, "party": party, "party_hp": party_hp,
        "damage_taken": e.get("damageTaken") or 0, "hero_deaths": e.get("heroDeaths") or 0,
        "threat": (e.get("damageTaken") or 0) / party_hp if party_hp else None,
        "by_ability": e.get("byAbility") or {}, "waves_cleared": e.get("wavesCleared") or 0,
        "total_waves": e.get("totalWaves") or 0, "wave_reached": e.get("waveReached") or 0,
        "session": s["id"], "file": s["file"], "note": "",
    }


# ---------------------------------------------------------------- aggregation

def mean(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None


def median(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None


def group_encounters(rows, keyfn):
    """keyfn(row) -> iterable of keys. -> {key: stats}"""
    groups = {}
    for r in rows:
        for k in keyfn(r):
            groups.setdefault(k, []).append(r)
    out = {}
    for k, rs in groups.items():
        rec = [r for r in rs if r["recorded"]]
        out[k] = {"n": len(rs), "wins": sum(r["win"] for r in rs), "rate": sum(r["win"] for r in rs) / len(rs),
                  "turns": mean([r["turns"] for r in rec]), "seconds": mean([r["seconds"] for r in rec]),
                  "deaths": mean([r["hero_deaths"] for r in rec]), "threat": mean([r["threat"] for r in rec]),
                  "stalled": sum(r["outcome"] in STALL_OUTCOMES for r in rs), "rows": rs}
    return out


def wave_items(rows):
    for r in rows:
        for w in r["waves"]:
            yield r, w


def group_waves(rows, keyfn):
    groups = {}
    for r, w in wave_items(rows):
        for k in keyfn(r, w):
            groups.setdefault(k, []).append((r, w))
    out = {}
    for k, items in groups.items():
        out[k] = {"n": len(items), "cleared": sum(w["cleared"] for _, w in items),
                  "rate": sum(w["cleared"] for _, w in items) / len(items),
                  "turns": mean([w["turns"] for _, w in items]), "seconds": mean([w["seconds"] for _, w in items]),
                  "threat": mean([w["threat"] for _, w in items]), "max_threat": max(w["threat"] for _, w in items),
                  "enc_wins": sum(r["win"] for r, _ in items), "items": items}
    return out


def hero_output(h):
    return (h.get("damageDealt") or 0) + (h.get("healingDone") or 0)


def analyze(rows, args, content):
    a = {}
    rec = [r for r in rows if r["recorded"]]
    a["rows"], a["recorded"] = rows, rec
    outcomes = {}
    for r in rows:
        outcomes[r["outcome"]] = outcomes.get(r["outcome"], 0) + 1
    a["outcomes"] = outcomes
    a["overall"] = group_encounters(rows, lambda r: ["all"]).get("all")

    a["by_family_enc"] = group_encounters(rec, lambda r: r["families"])
    a["by_family_wave"] = group_waves(rec, lambda r, w: w["families"])
    a["by_class"] = group_encounters(rec, lambda r: sorted({h.get("classId") or "?" for h in r["party"]}))
    a["by_location"] = group_encounters(rows, lambda r: [r["location"]])
    a["by_band"] = group_encounters(rec, lambda r: [r["band"]])
    a["by_group"] = group_waves(rec, lambda r, w: [w["group"]])
    a["cells"] = group_waves(rec, lambda r, w: [(f, w["band"]) for f in w["families"]])
    a["families"] = sorted({f for r in rec for f in r["families"]})
    a["bands"] = sorted({w["band"] for _, w in wave_items(rec)}, key=band_key)

    # Hero impact: each hero's share of its party's output (damage dealt + healing done) against a fair share.
    classes = {}
    for r in rec:
        total = sum(hero_output(h) for h in r["party"])
        size = len(r["party"]) or 1
        for h in r["party"]:
            c = classes.setdefault(h.get("classId") or "?", {"n": 0, "ratios": [], "dealt": [], "healed": [],
                                                              "deaths": 0, "zero": 0, "best": []})
            c["n"] += 1
            out = hero_output(h)
            ratio = (out / total) * size if total else 0.0
            c["ratios"].append(ratio)
            c["dealt"].append(h.get("damageDealt") or 0)
            c["healed"].append(h.get("healingDone") or 0)
            c["deaths"] += h.get("deaths") or 0
            c["zero"] += out == 0
            c["best"].append((ratio, r["seed"]))
    for c in classes.values():
        c["ratio"] = mean(c["ratios"])
        c["best"].sort(reverse=True)
    a["classes"] = classes

    # Ability impact: share of all hero damage, by ability.
    abil, total_dealt = {}, 0
    for r in rec:
        for h in r["party"]:
            for k, v in (h.get("dealtByAbility") or {}).items():
                ab = abil.setdefault(k, {"dealt": 0, "encounters": 0, "classes": set(), "top": []})
                ab["dealt"] += v
                ab["classes"].add(h.get("classId") or "?")
                ab["top"].append((v, r["seed"]))
                total_dealt += v
        for k in {k for h in r["party"] for k in (h.get("dealtByAbility") or {})}:
            abil[k]["encounters"] += 1
    for ab in abil.values():
        ab["share"] = ab["dealt"] / total_dealt if total_dealt else 0.0
        ab["top"].sort(reverse=True)
    a["abilities"], a["total_dealt"] = abil, total_dealt
    taken = {}
    for r in rec:
        for k, t in r["by_ability"].items():
            taken[k] = taken.get(k, 0) + (t.get("taken") or 0)
    a["taken"] = taken

    a["flags"] = flags(a, args, content)
    return a


def flags(a, args, content):
    m = args.min_samples
    f = {"hard": [], "easy": [], "easy_enc": [], "stalled": [], "slow": [], "hero_big": [], "hero_zero": [],
         "abil_big": [], "abil_zero": []}

    def seeds_of(items, pred=lambda r, w: True, limit=12):
        out = []
        for r, w in items:
            if pred(r, w) and r["seed"] not in out:
                out.append(r["seed"])
        return out[:limit]

    for g, st in sorted(a["by_group"].items()):
        if st["n"] >= m and st["rate"] <= args.hard_rate:
            f["hard"].append({"what": f"wave group `{g}`", "n": st["n"], "rate": st["rate"], "threat": st["threat"],
                              "seeds": seeds_of(st["items"], lambda r, w: not w["cleared"])})
        elif st["n"] >= m and st["rate"] >= 1.0 and st["max_threat"] < args.easy_threat:
            f["easy"].append({"what": f"wave group `{g}`", "n": st["n"], "rate": st["rate"], "threat": st["threat"],
                              "seeds": seeds_of(st["items"])})
    for (fam, band), st in sorted(a["cells"].items(), key=lambda kv: (kv[0][0], band_key(kv[0][1]))):
        if st["n"] >= m and st["rate"] <= args.hard_rate:
            f["hard"].append({"what": f"family `{fam}` at levels {band}", "n": st["n"], "rate": st["rate"],
                              "threat": st["threat"], "seeds": seeds_of(st["items"], lambda r, w: not w["cleared"])})
        elif st["n"] >= m and st["rate"] >= 1.0 and st["max_threat"] < args.easy_threat:
            f["easy"].append({"what": f"family `{fam}` at levels {band}", "n": st["n"], "rate": st["rate"],
                              "threat": st["threat"], "seeds": seeds_of(st["items"])})

    for r in a["recorded"]:
        if r["win"] and r["hero_deaths"] == 0 and r["threat"] is not None and r["threat"] < args.easy_threat:
            f["easy_enc"].append(r)
    f["easy_enc"].sort(key=lambda r: r["threat"])

    for r in a["rows"]:
        if r["outcome"] in STALL_OUTCOMES:
            f["stalled"].append(r)
    # Dragging: turns per wave reached far above the run's median (a near-stalemate that still ended).
    per_wave = [(r["turns"] / max(1, r.get("wave_reached") or 1), r) for r in a["recorded"]]
    med = median([x for x, _ in per_wave])
    if med:
        for x, r in per_wave:
            if x >= args.slow_factor * med and r["outcome"] not in STALL_OUTCOMES:
                f["slow"].append((x, med, r))
        f["slow"].sort(key=lambda t: -t[0])

    for cls, c in sorted(a["classes"].items()):
        if c["n"] < m:
            continue
        if c["ratio"] >= args.outsized:
            f["hero_big"].append((cls, c))
        elif c["ratio"] <= args.zero_ratio or c["zero"] == c["n"]:
            f["hero_zero"].append((cls, c))

    for k, ab in sorted(a["abilities"].items(), key=lambda kv: -kv[1]["share"]):
        if k in BASIC_KEYS or k.startswith("buff:") or k.startswith("trap:"):
            continue
        if ab["share"] >= args.ability_share:
            f["abil_big"].append((k, ab))

    classes_def, abil_def = content
    if classes_def and abil_def:
        fielded = {cls: c["n"] for cls, c in a["classes"].items() if c["n"] >= m}
        dealt = {(h.get("classId"), k) for r in a["recorded"] for h in r["party"]
                 for k, v in (h.get("dealtByAbility") or {}).items() if v > 0}
        for cls, n in sorted(fielded.items()):
            for k in classes_def.get(cls, []):
                d = abil_def.get(k)
                if not d or d.get("Kind") == "Passive" or (d.get("BaseDamage") or 0) <= 0:
                    continue
                if (cls, k) not in dealt:
                    f["abil_zero"].append((k, cls, n))
    return f


# ---------------------------------------------------------------- formatting

def pct(x):
    return "–" if x is None else f"{x * 100:.0f}%"


def num(x, d=1):
    return "–" if x is None else f"{x:.{d}f}"


def repro(seed):
    return f"python tools/harness.py endless 1 --seed {seed}" if seed is not None else "(no seed recorded)"


def seed_list(seeds):
    return ", ".join(f"`{s}`" for s in seeds) if seeds else "–"


class Doc:
    """Builds the markdown and the HTML side by side from the same calls."""

    def __init__(self, title):
        self.md, self.html, self.title = [], [], title

    def h(self, level, text, anchor=None):
        self.md.append(f"\n{'#' * level} {text}\n")
        aid = f' id="{anchor}"' if anchor else ""
        self.html.append(f"<h{level}{aid}>{inline(text)}</h{level}>")

    def p(self, text):
        self.md.append(text + "\n")
        self.html.append(f"<p>{inline(text)}</p>")

    def bullets(self, items):
        if not items:
            return
        self.md.append("\n".join(f"- {i}" for i in items) + "\n")
        self.html.append("<ul>" + "".join(f"<li>{inline(i)}</li>" for i in items) + "</ul>")

    def table(self, head, rows):
        if not rows:
            self.p("_none_")
            return
        # A column is right-aligned when every cell in it is a number, a percentage, a ratio or a dash.
        numeric = [all(is_num(r[i]) for r in rows) for i in range(len(head))]
        self.md.append("| " + " | ".join(head) + " |")
        self.md.append("|" + "|".join("---:" if numeric[i] else "---" for i in range(len(head))) + "|")
        for r in rows:
            self.md.append("| " + " | ".join(str(c).replace("|", "\\|") for c in r) + " |")
        self.md.append("")
        out = ["<div class='scroll'><table><thead><tr>" + "".join(f"<th>{inline(h)}</th>" for h in head) + "</tr></thead><tbody>"]
        for r in rows:
            out.append("<tr>" + "".join(f"<td class='{'n' if numeric[i] else ''}'>{inline(str(c))}</td>"
                                        for i, c in enumerate(r)) + "</tr>")
        out.append("</tbody></table></div>")
        self.html.append("".join(out))

    def heatmap(self, title, families, bands, cell, color, note):
        """cell(fam, band) -> (value or None, label, n); color(value) -> css colour."""
        self.h(3, title)
        self.p(note)
        if not families or not bands:
            self.p("_no data_")
            return
        self.md.append("| family | " + " | ".join(bands) + " |")
        self.md.append("|---|" + "|".join("---:" for _ in bands) + "|")
        hrows = []
        for fam in families:
            md_cells, h_cells = [], []
            for b in bands:
                v, label, n = cell(fam, b)
                if v is None:
                    md_cells.append("")
                    h_cells.append("<td class='hm empty'></td>")
                else:
                    md_cells.append(f"{label} ({n})")
                    h_cells.append(f"<td class='hm' style='background:{color(v)}' title='{html.escape(fam)} · levels {b} · n={n}'>"
                                   f"{html.escape(label)}<small>{n}</small></td>")
            self.md.append(f"| {fam} | " + " | ".join(md_cells) + " |")
            hrows.append(f"<tr><th class='rowh'>{html.escape(fam)}</th>{''.join(h_cells)}</tr>")
        self.md.append("")
        self.html.append("<div class='scroll'><table class='heat'><thead><tr><th>family \\ levels</th>"
                         + "".join(f"<th>{html.escape(b)}</th>" for b in bands) + "</tr></thead><tbody>"
                         + "".join(hrows) + "</tbody></table></div>")


def is_num(c):
    t = str(c).replace(",", "").replace("%", "").replace("/", "").replace("–", "").replace(".", "").replace("-", "").strip()
    return t == "" or t.isdigit()


def inline(text):
    """Escapes for HTML and renders the two bits of markdown the report uses: `code` and **bold**."""
    out, parts = [], text.split("`")
    for i, part in enumerate(parts):
        esc = html.escape(part)
        if i % 2:
            out.append(f"<code>{esc}</code>")
        else:
            bits = esc.split("**")
            out.append("".join(f"<strong>{b}</strong>" if j % 2 else b for j, b in enumerate(bits)))
    return "".join(out)


def scale(v, lo, hi, good_high=True):
    """v in [lo, hi] -> hsl from red (bad) to green (good)."""
    if hi <= lo:
        t = 0.5
    else:
        t = min(1.0, max(0.0, (v - lo) / (hi - lo)))
    if not good_high:
        t = 1 - t
    return f"hsl({int(t * 125)}, 55%, var(--hm-l))"


def render(name, a, args, source, out_dir):
    d = Doc(f"Endless telemetry · {name}")
    rows, rec, ov = a["rows"], a["recorded"], a["overall"]
    when = dt.datetime.now().strftime("%Y-%m-%d %H:%M")
    d.h(1, f"Endless telemetry report · {name}")
    d.p(f"Source: {source}. Generated {when} by `tools/telemetry_report.py`. "
        f"Level bands are {args.band_size} levels wide (encounter band = its highest enemy level; wave band = the wave's "
        f"highest enemy level). Outlier groups need at least {args.min_samples} samples. "
        f"**Threat** = damage the party took ÷ the party's total max HP.")
    if not rows:
        d.p("**No encounters found.** Nothing to report.")
        return d

    d.h(2, "Overview", "overview")
    seeds = [r["seed"] for r in rows if r["seed"] is not None]
    d.table(["metric", "value"], [
        ["encounters", len(rows)],
        ["with an encounter record", len(rec)],
        ["outcomes", ", ".join(f"{k} {v}" for k, v in sorted(a["outcomes"].items(), key=lambda kv: -kv[1]))],
        ["win rate", pct(ov["rate"])],
        ["avg turns", num(ov["turns"])],
        ["avg duration (s)", num(ov["seconds"])],
        ["avg hero deaths", num(ov["deaths"], 2)],
        ["avg threat", pct(ov["threat"])],
        ["seeds", f"{min(seeds)}..{max(seeds)}" if seeds else "–"],
    ])

    f = a["flags"]
    d.h(2, "Flags", "flags")
    d.bullets([
        f"**{len(f['hard'])}** always-lost (too hard) wave groups / family-band cells",
        f"**{len(f['easy'])}** never-threatening (too easy) wave groups / family-band cells, "
        f"**{len(f['easy_enc'])}** never-threatening encounters",
        f"**{len(f['stalled'])}** stalled / unrecorded encounters, **{len(f['slow'])}** dragging encounters",
        f"**{len(f['hero_big'])}** outsized and **{len(f['hero_zero'])}** zero-impact hero classes",
        f"**{len(f['abil_big'])}** outsized and **{len(f['abil_zero'])}** zero-impact abilities",
    ])

    def enc_table(st, label):
        out = []
        for k, s in sorted(st.items(), key=lambda kv: (-kv[1]["n"], str(kv[0]))):
            out.append([k, s["n"], pct(s["rate"]), num(s["turns"]), num(s["seconds"]), num(s["deaths"], 2), pct(s["threat"])])
        d.table([label, "encounters", "win rate", "avg turns", "avg secs", "hero deaths", "threat"], out)

    d.h(2, "By enemy family", "family")
    d.p("Encounter columns count every encounter with a wave of that family; wave columns count that family's waves alone.")
    wave_st = a["by_family_wave"]
    out = []
    for fam, s in sorted(a["by_family_enc"].items(), key=lambda kv: (-kv[1]["n"], kv[0])):
        w = wave_st.get(fam) or {}
        out.append([fam, s["n"], pct(s["rate"]), w.get("n", 0), pct(w.get("rate")), num(w.get("turns")),
                    num(w.get("seconds")), pct(w.get("threat"))])
    d.table(["family", "encounters", "enc. win rate", "waves", "wave clear", "turns / wave", "secs / wave", "threat / wave"], out)

    d.h(2, "By hero class", "class")
    out = []
    for cls, s in sorted(a["by_class"].items(), key=lambda kv: (-kv[1]["n"], kv[0])):
        c = a["classes"].get(cls) or {}
        out.append([cls, s["n"], pct(s["rate"]), num(s["turns"]), num(s["seconds"]),
                    num(mean(c.get("dealt", [])), 0), num(mean(c.get("healed", [])), 0),
                    num(c.get("deaths", 0) / max(1, c.get("n", 1)), 2), num(c.get("ratio"), 2)])
    d.table(["class", "encounters", "win rate", "avg turns", "avg secs", "dmg dealt", "healing", "deaths", "impact ×fair"], out)

    d.h(2, "By location", "location")
    enc_table(a["by_location"], "location")

    d.h(2, "By level band", "band")
    out = []
    for b, s in sorted(a["by_band"].items(), key=lambda kv: band_key(kv[0])):
        out.append([b, s["n"], pct(s["rate"]), num(s["turns"]), num(s["seconds"]), num(s["deaths"], 2), pct(s["threat"])])
    d.table(["levels", "encounters", "win rate", "avg turns", "avg secs", "hero deaths", "threat"], out)

    d.h(2, "Family × level band", "heatmaps")
    cells, fams, bands = a["cells"], a["families"], a["bands"]

    def cell(key, fmt):
        def fn(fam, b):
            s = cells.get((fam, b))
            if not s:
                return None, "", 0
            v = s[key] if key != "enc_rate" else s["enc_wins"] / s["n"]
            return v, fmt(v), s["n"]
        return fn

    d.heatmap("Wave clear rate", fams, bands, cell("rate", pct), lambda v: scale(v, 0, 1),
              "Share of the family's waves at that band the party cleared (wave count in brackets).")
    d.heatmap("Encounter win rate", fams, bands, cell("enc_rate", pct), lambda v: scale(v, 0, 1),
              "Share of encounters containing such a wave that ended in victory.")
    turns = [s["turns"] for s in cells.values() if s["turns"] is not None]
    d.heatmap("Turns per wave", fams, bands, cell("turns", lambda v: f"{v:.1f}"),
              lambda v: scale(v, min(turns or [0]), max(turns or [1]), good_high=False),
              "Average commands taken to clear (or lose) one wave; redder = slower.")
    threats = [s["threat"] for s in cells.values() if s["threat"] is not None]
    d.heatmap("Threat per wave", fams, bands, cell("threat", pct),
              lambda v: scale(v, min(threats or [0]), max(threats or [1]), good_high=False),
              "Damage taken in the wave ÷ party max HP; redder = more dangerous.")

    d.h(2, "Outliers", "outliers")
    d.p("Every flagged encounter comes with its seed; replay one with `python tools/harness.py endless 1 --seed <seed>`.")

    d.h(3, "Always lost (too hard)")
    d.p(f"Wave groups and family/band cells cleared in ≤{pct(args.hard_rate)} of at least {args.min_samples} appearances.")
    d.table(["what", "waves", "clear rate", "threat", "seeds (lost)"],
            [[x["what"], x["n"], pct(x["rate"]), pct(x["threat"]), seed_list(x["seeds"])] for x in f["hard"]])

    d.h(3, "Never threatening (too easy)")
    d.p(f"Wave groups and family/band cells always cleared, never costing the party {pct(args.easy_threat)} of its HP "
        f"in a single wave (at least {args.min_samples} appearances).")
    d.table(["what", "waves", "clear rate", "avg threat", "seeds"],
            [[x["what"], x["n"], pct(x["rate"]), pct(x["threat"]), seed_list(x["seeds"])] for x in f["easy"]])
    d.p(f"Encounters won with no hero down and under {pct(args.easy_threat)} threat:")
    d.table(["seed", "location", "levels", "families", "turns", "threat", "reproduce"],
            [[r["seed"], r["location"], r["band"], ", ".join(r["families"]), r["turns"], pct(r["threat"]), f"`{repro(r['seed'])}`"]
             for r in f["easy_enc"][:args.list_limit]])
    if len(f["easy_enc"]) > args.list_limit:
        d.p(f"…and {len(f['easy_enc']) - args.list_limit} more.")

    d.h(3, "Stalled")
    d.p("Encounters that never resolved: the battle was left unresolved (`stalled`), the session ended without an "
        "encounter record (`unrecorded`), or the harness lost it to a crash / stall / hang.")
    d.table(["seed", "location", "outcome", "turns", "session", "note", "reproduce"],
            [[r["seed"], r["location"], r["outcome"], r["turns"] if r["turns"] is not None else "–", r["session"] or "–",
              r["note"] or "–", f"`{repro(r['seed'])}`"] for r in f["stalled"]])
    d.p(f"Dragging: encounters whose turns per wave reached are ≥{args.slow_factor:g}× the run's median.")
    d.table(["seed", "location", "outcome", "turns", "turns / wave", "median", "reproduce"],
            [[r["seed"], r["location"], r["outcome"], r["turns"], num(x), num(m), f"`{repro(r['seed'])}`"]
             for x, m, r in f["slow"][:args.list_limit]])

    d.h(3, "Hero impact")
    d.p(f"Impact = a hero's share of its party's damage dealt + healing done, × party size (1.0 = a fair share). "
        f"Outsized ≥ {args.outsized:g}, zero ≤ {args.zero_ratio:g} or nothing at all, over at least {args.min_samples} encounters.")
    hero_rows = []
    for cls, c in f["hero_big"]:
        hero_rows.append([cls, "outsized", c["n"], num(c["ratio"], 2), seed_list([s for _, s in c["best"][:5]])])
    for cls, c in f["hero_zero"]:
        hero_rows.append([cls, "zero", c["n"], num(c["ratio"], 2), seed_list([s for _, s in sorted(c["best"])[:5]])])
    d.table(["class", "flag", "encounters", "impact ×fair", "seeds"], hero_rows)

    d.h(3, "Ability impact")
    d.p(f"Share of all hero damage ({a['total_dealt']:,}) by ability. Outsized: a non-basic ability doing ≥ "
        f"{pct(args.ability_share)} of it.")
    d.table(["ability", "share", "damage", "encounters", "classes", "top seeds"],
            [[k, pct(ab["share"]), f"{ab['dealt']:,}", ab["encounters"], ", ".join(sorted(ab["classes"])),
              seed_list([s for _, s in ab["top"][:5]])] for k, ab in f["abil_big"]])
    d.p("Zero impact: damaging abilities in a fielded class's default loadout (classes.json / abilities.json) that "
        "never dealt damage — never cast, always missed, or dead weight.")
    d.table(["ability", "class", "class encounters"], [[k, cls, n] for k, cls, n in f["abil_zero"]])

    d.h(3, "All hero damage by ability")
    d.table(["ability", "share", "damage", "encounters", "classes"],
            [[k, pct(ab["share"]), f"{ab['dealt']:,}", ab["encounters"], ", ".join(sorted(ab["classes"]))]
             for k, ab in sorted(a["abilities"].items(), key=lambda kv: -kv[1]["dealt"])])
    if a["taken"]:
        d.h(3, "Damage heroes took, by source")
        tot = sum(a["taken"].values()) or 1
        d.table(["source", "share", "damage"],
                [[k, pct(v / tot), f"{v:,}"] for k, v in sorted(a["taken"].items(), key=lambda kv: -kv[1]) if v > 0])

    d.h(2, "Encounters", "encounters")
    d.table(["seed", "location", "levels", "outcome", "waves", "turns", "secs", "deaths", "threat", "families", "session"],
            [[r["seed"], r["location"], r["band"] or "–", r["outcome"],
              f"{r['waves_cleared']}/{r['total_waves']}" if r["recorded"] else "–",
              r["turns"] if r["turns"] is not None else "–", num(r["seconds"]), r["hero_deaths"], pct(r["threat"]),
              ", ".join(r["families"]) or "–", r["session"] or "–"] for r in rows])
    return d


CSS = """
:root{--bg:#f7f7f5;--fg:#1d1f22;--muted:#666b73;--line:#d9dad6;--card:#fff;--code:#eceae4;--hm-l:78%;--accent:#2e5c8a}
@media (prefers-color-scheme:dark){:root{--bg:#16181b;--fg:#e4e5e7;--muted:#9aa0a8;--line:#30343a;--card:#1e2125;--code:#2a2e33;--hm-l:30%;--accent:#7fb0e0}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:1200px;margin:0 auto;padding:24px 16px 64px}
h1{font-size:24px;margin:0 0 8px}h2{font-size:19px;margin:36px 0 8px;padding-top:8px;border-top:1px solid var(--line)}
h3{font-size:15px;margin:22px 0 6px;color:var(--accent)}
p{margin:6px 0;color:var(--fg)}code{background:var(--code);padding:1px 4px;border-radius:3px;font-size:12px}
nav{display:flex;flex-wrap:wrap;gap:6px 14px;margin:10px 0 0;font-size:13px}nav a{color:var(--accent);text-decoration:none}
.scroll{overflow-x:auto;margin:8px 0}
table{border-collapse:collapse;background:var(--card);font-variant-numeric:tabular-nums;min-width:40%}
th,td{border:1px solid var(--line);padding:4px 8px;text-align:left;vertical-align:top}
th{background:var(--code);font-weight:600;white-space:nowrap}td.n{text-align:right}
table.heat td.hm{text-align:center;min-width:64px;white-space:nowrap}table.heat td.hm small{display:block;font-size:10px;color:var(--muted)}
table.heat td.empty{background:transparent}th.rowh{text-align:left}
ul{margin:6px 0;padding-left:20px}
"""


def write(d, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    md_path = os.path.join(out_dir, "report.md")
    html_path = os.path.join(out_dir, "report.html")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("\n".join(d.md).lstrip() + "\n")
    nav = "".join(f"<a href='#{a}'>{t}</a>" for a, t in [
        ("overview", "Overview"), ("flags", "Flags"), ("family", "Family"), ("class", "Class"), ("location", "Location"),
        ("band", "Level band"), ("heatmaps", "Heatmaps"), ("outliers", "Outliers"), ("encounters", "Encounters")])
    body = "\n".join(d.html)
    body = body.replace("</h1>", f"</h1><nav>{nav}</nav>", 1)
    with open(html_path, "w", encoding="utf-8") as f:
        f.write(f"<!doctype html><html lang='en'><head><meta charset='utf-8'>"
                f"<meta name='viewport' content='width=device-width,initial-scale=1'><title>{html.escape(d.title)}</title>"
                f"<style>{CSS}</style></head><body><main>{body}</main></body></html>\n")
    return md_path, html_path


def main(argv=None):
    ap = argparse.ArgumentParser(prog="telemetry_report.py", description=__doc__.split("\n\n")[0],
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("session", help="harness endless run name, session file/folder, or telemetry session id / folder-name fragment")
    ap.add_argument("--out", help="output folder (default artifacts/endless/<session>)")
    ap.add_argument("--telemetry-root", action="append", default=[], help="extra telemetry root to search (repeatable)")
    ap.add_argument("--all-stages", action="store_true", help="count non-endless stages too")
    ap.add_argument("--band-size", type=int, default=5, help="levels per level band (default 5)")
    ap.add_argument("--min-samples", type=int, default=3, help="samples a group needs before it can be flagged (default 3)")
    ap.add_argument("--hard-rate", type=float, default=0.0, help="clear rate at or below which a group is too hard (default 0)")
    ap.add_argument("--easy-threat", type=float, default=0.05, help="threat below which a won wave/encounter is too easy (default 0.05)")
    ap.add_argument("--slow-factor", type=float, default=3.0, help="turns-per-wave multiple of the median that flags a dragging encounter")
    ap.add_argument("--outsized", type=float, default=2.0, help="hero impact (x fair share) that counts as outsized (default 2)")
    ap.add_argument("--zero-ratio", type=float, default=0.1, help="hero impact (x fair share) that counts as zero (default 0.1)")
    ap.add_argument("--ability-share", type=float, default=0.25, help="share of all hero damage that makes an ability outsized")
    ap.add_argument("--list-limit", type=int, default=25, help="rows shown in long outlier lists (default 25)")
    args = ap.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args.band_size = max(1, args.band_size)

    name, sessions, lost, endless_only, source = resolve(args.session, args.telemetry_root, args.all_stages)
    rows = build_rows(sessions, lost, endless_only, args.band_size)
    a = analyze(rows, args, load_content())
    safe = "".join(ch if ch.isalnum() or ch in "-_." else "_" for ch in name) or "session"
    out_dir = args.out or os.path.join(ROOT, "artifacts", "endless", safe)
    d = render(name, a, args, source, out_dir)
    md_path, html_path = write(d, out_dir)

    f = a["flags"]
    ov = a["overall"]
    print(f"telemetry_report: {len(sessions)} session file(s), {len(rows)} encounter(s)"
          + (f", win rate {pct(ov['rate'])}, avg {num(ov['turns'])} turns / {num(ov['seconds'])}s" if ov else ""))
    print(f"  flags: {len(f['hard'])} too hard, {len(f['easy'])} too easy (+{len(f['easy_enc'])} encounters), "
          f"{len(f['stalled'])} stalled, {len(f['slow'])} dragging, heroes {len(f['hero_big'])} outsized / "
          f"{len(f['hero_zero'])} zero, abilities {len(f['abil_big'])} outsized / {len(f['abil_zero'])} zero")
    print(f"  {md_path}\n  {html_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
