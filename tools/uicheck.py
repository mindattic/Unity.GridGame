"""
UI verification gate: drives the real development player through the screens and asserts behavior
(not just "it rendered"). Writes artifacts/uicheck/report.json and exits non-zero on any failure.

    python tools/uicheck.py
"""
import contextlib
import json
import os
import sys
import time
import traceback

# The report prints arrows/dashes; a cp1252 Windows console would otherwise crash on the first one.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harness as h  # noqa: E402

results = []


def check(name, ok, detail=""):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + (f" — {detail}" if detail and not ok else ""))


def texts():
    return h.call("texts")


def has_text(sub):
    return any(sub in t for t in texts())


def until(pred, timeout=6.0, step=0.25):
    """Poll instead of sleeping a fixed time: screens on a busy machine take a variable moment to mount."""
    end = time.time() + timeout
    while time.time() < end:
        try:
            if pred(): return True
        except Exception:
            pass
        time.sleep(step)
    return False


@contextlib.contextmanager
def section(name):
    """One crash-isolated stretch of checks: an exception (an unexpected screen, a missing element, a
    dead endpoint) records a clean FAIL for this section instead of killing the run, so the report
    still lists every later check and the script still writes report.json (GG-T110)."""
    try:
        yield
    except Exception as ex:
        tb = traceback.extract_tb(ex.__traceback__)
        where = f"uicheck.py:{tb[-1].lineno}" if tb else ""
        try:
            scr = h.call("state", timeout=5).get("screen")
        except Exception:
            scr = "?"
        check(f"{name} (section ran to completion)", False, f"{type(ex).__name__}: {ex} at {where}, screen={scr}")


def on_screen(name, screen, timeout=6.0):
    """Waits for `screen` and records a check either way; a wrong screen is a FAIL, not a crash."""
    ok = until(lambda: h.call("state")["screen"] == screen, timeout)
    check(name, ok, "" if ok else f"expected '{screen}', on '{h.call('state').get('screen')}'")
    return ok


def wait_idle(ms=300):
    return h.call("wait", ms=ms)


def battle_state():
    return h.call("state").get("battle")


def main():
    art = h.launch("uicheck", fresh=True)

    with section('Title / save slots'):
        check("title shows New Game", until(lambda: has_text("New Game")))
        check("title music", h.call("music")["track"] == "TellerOfTheTales")
        time.sleep(0.6)  # the title's fade-in swallows a tap sent the moment it mounts
        # A tap sent while the title is still fading in reports it hit the fade (GG-T121): _tap_until repeats
        # only such a failed tap, never the assertion.
        h._tap_until(lambda: h.call("clicktext", text="New Game"), lambda: until(lambda: has_text("Slot 1"), 1.5), tries=3, wait=0.2)
        check("slot screen lists 3 slots", sum(1 for t in texts() if t.startswith("Slot ")) == 3)
        h._tap_until(lambda: h.call("clicktext", text="Slot 1"), lambda: until(lambda: h.call("state")["screen"] == "charselect", 2.0), tries=3, wait=0.2)
        # New Game opens the party pick first (CharacterSelectScreen, since 43fd442); lock in the
        # default small starting party (GG-T118) through the real UI so every later check sees the same squad.
        if on_screen("new game opens the party select", "charselect"):
            h.shot("charselect")
            h.pick_party()
        on_screen("new game lands at camp", "hub")
        party = (h.call("state").get("save") or {}).get("party") or []
        check("new game starts with the small party (GG-T118)", len(party) == len(h.STARTING_PARTY), str(party))
        check("camp music", h.call("music")["track"] == "MinstrelGuild")
        for item in ["Adventure", "Items", "Status", "Equip", "Abilities", "Party", "Talk", "Caravan", "Summon Circle", "Bounties", "Bestiary", "Settings"]:
            if not has_text(item):
                check(f"camp menu has {item}", False)
                break
        else:
            check("camp menu has every command", True)
        # GG-T119: the Camp NPCs are met on the road, so a new save's camp has none of their stations yet.
        npc_rows = [x for x in ["Merchant", "Blacksmith", "Alchemist", "Enchantress", "Inn"] if x in texts()]
        check("a new save's camp hides the stations of NPCs not yet met", not npc_rows, str(npc_rows))
        cards = [h.call("element", name=n) for n in h.call("names", prefix="camp-hero-")]
        win = h.call("element", name="camp-party")
        check("camp party is a 2-column grid with room for six", len(cards) >= 3 and all(c["w"] < win["w"] * 0.55 for c in cards)
              and win["h"] < 700, f"{len(cards)} cards, window {win}")

        # Overwrite confirm on an occupied slot
        h.call("clicktext", text="Save & Quit"); until(lambda: has_text("New Game"))
        h.call("clicktext", text="New Game"); until(lambda: has_text("Slot 1"))
        h.call("clicktext", text="Slot 1")
        check("occupied slot asks to overwrite", until(lambda: has_text("Overwrite slot 1?"), 4.0))
        h.call("clicktext", text="Cancel"); time.sleep(0.4)
        h.call("back"); time.sleep(0.8)
        h._tap_until(lambda: h.call("clicktext", text="Continue"), lambda: until(lambda: h.call("state")["screen"] == "hub", 3.0), tries=3, wait=0.2)
        check("continue resumes the save", until(lambda: h.call("state")["screen"] == "hub"))

    with section('Adventure ordering'):
        h.call("clicktext", text="Adventure"); time.sleep(0.8)
        t = texts()
        check("adventure lists the first stage", any("Green Valley I" in x for x in t))
        check("adventure groups battles into expeditions", any("Green Valley Expedition" in x for x in t), str(t[:8]))
        check("locked stages collapse into one teaser", any("more stages ahead" in x for x in t))
        h.call("clicktext", text="Green Valley I"); time.sleep(1.5)
        if h.call("state")["screen"] == "crawl":
            check("story crawl plays on first entry", True)
            h.call("clicktext", text="Skip"); time.sleep(1.5)
        # Story beat (§27.1, GG-T117): the sample merchant dialogue plays between the crawl and the battle.
        if on_screen("story-beat dialogue plays before the first battle", "dialogue"):
            names = h.call("names", prefix="dlg-")
            check("dialogue shows a speaker portrait", "dlg-portrait-left" in names, str(names))
            h._tap_until(lambda: h.call("click", name="dlg-skip"), lambda: until(lambda: "choice-0" in h.call("names", prefix="choice-"), 2.0), tries=3, wait=0.2)
            choices = h.call("names", prefix="choice-")
            check("dialogue offers branching choices", len(choices) >= 2, str(choices))
            lines = sorted(n for n in h.call("names", prefix="dlg-line-"))
            if len(lines) >= 2:
                a, b = h.call("element", name=lines[-2]), h.call("element", name=lines[-1])
                check("conversation boxes overlap like a stack", b["y"] < a["y"] + a["h"], f"{a} {b}")
            h.shot("dialogue")
            check("dialogue plays through to the battle", h.finish_dialogue())
        check("battle screen", until(lambda: h.call("state")["screen"] == "battle", 6.0))
        time.sleep(1.0)
        check("first battle starts the guided tutorial", any(x.startswith("Tutorial 1/") for x in texts()))
        check("each lesson has an explanation and an example", has_text("Example:"))
        time.sleep(1.0)
        check("tutorial shows the suggested move right away", (h.call("banner")["text"] or "").startswith(("Hint", "Tip")))

        check("battle music", h.call("music")["track"] == "Crusade")

    with section('HUD'):
        names = h.call("names", prefix="")
        for el in ["board", "abilitybar", "orbs", "shield", "menu", "feed"]:
            check(f"HUD element {el}", el in names)
        check("timeline icons present", any(n.startswith("tl-") for n in names))
        check("enemy turn icons are pixel-art sprites", "px" in names, "no 'px' sprite under the timeline icons")
        b = battle_state()
        enemies = [u for u in b["units"] if u["team"] == "Enemy"]
        check("no turn-number badges on the board (the timeline shows turn order)", not any(x.strip().isdigit() for x in texts()), str([x for x in texts() if x.strip().isdigit()]))
        track = h.call("element", name="timeline-track")
        icon = next((h.call("element", name=n) for n in names if n.startswith("tl-")), {})
        tcy = track.get("y", 0) + track.get("h", 0) / 2
        check("timeline track is thin", 0 < track.get("h", 99) <= 8, str(track))
        check("turn icons sit centred on the timeline line",
              all(abs(h.call("element", name=n)["y"] + h.call("element", name=n)["h"] / 2 - tcy) < 6 for n in names if n.startswith("tl-")), f"track={track} icon={icon}")
        fps = h.call("state")["fps"]
        check("frame rate ≥ 50", fps >= 50, f"fps={fps}")

    with section('Actor panel hides unscanned enemy HP'):
        e = enemies[0]
        h.call("tap", x=h.tile(e["x"], e["y"])[0], y=h.tile(e["x"], e["y"])[1]); time.sleep(0.4)
        check("tapping an enemy shows it with hidden HP", has_text("???"))
        check("enemy card describes its kit", has_text("leaves a Slime Trail") or has_text("Leaves a Slime Trail"), str([x for x in texts()][-6:]))

    with section("Drag preview + pincer banner (bot's best move), then commit"):
        sug = h.call("suggest")
        if isinstance(sug, dict) and sug.get("path"):
            pts = [h.tile(*sug["from"])] + [h.tile(x, y) for x, y in sug["path"]]
            h.call("cues", clear=1)
            h.call("drag", path=";".join(f"{x},{y}" for x, y in pts), step=0.1, hold=1)
            time.sleep(0.3)
            banner = h.call("banner")["text"] or ""
            if sug["pincers"] > 0:
                check("pincer preview banner while holding", "PINCER" in banner, banner)
            if sug["moved"]:
                m = sug["moved"][0]
                el = h.call("element", name=f"unit-{m['id']}")
                tx, ty = h.tile(m["x"], m["y"])
                ok = abs(el["x"] + el["w"] / 2 - tx) < 40 and abs(el["y"] + el["h"] / 2 - ty) < 40
                check("displaced unit slides to its preview tile while holding", ok, json.dumps(el))
            h.shot("drag-hold")
            h.call("release")
            st = wait_idle(400)["battle"]
            check("drop commits one action", st["results"]["ActionsTaken"] == 1, str(st["results"]))
            if sug["pincers"] > 0:
                check("a pincer names itself on the billboard", has_text("Pincer") or has_text("Chain"), str(texts()[:12]))
            tut = [x for x in texts() if x.startswith("Tutorial ")]
            check("the tutorial advances after the first move", any(x.startswith(("Tutorial 2/", "Tutorial 3/", "Tutorial 4/")) for x in tut), f"tutorial texts={tut} panel={[x for x in texts()][-8:]}")
            h.call("click", name="tutorial-skip"); time.sleep(0.4)
            check("tutorial can be skipped", not any(x.startswith("Tutorial ") for x in texts()))
            cues = h.call("cues")
            check("pick-up and every step are voiced", "pickup" in cues and cues.count("move") >= len(sug["path"]), ",".join(cues[:20]))
            if sug["pincers"] > 0:
                check("a pincer plays the close-up cut-in", st.get("cutins", 0) >= 1, str(st.get("cutins")))
                check("pincer and hits are voiced", "pincer" in cues and any(c in cues for c in ("hit", "crit", "miss")), ",".join(cues[:30]))
        # Every cue the game can play resolves to a bundled clip (no silent events).
        else:
            check("bot suggested a move", False, str(sug))

    with section('Pincer close-up: portraits on screen mid-sweep, camera pushed in, then all restored'):
        h.call("democutin"); time.sleep(0.5)
        names = h.call("names")
        portraits = [n for n in names if str(n).startswith("cutin-")] if isinstance(names, list) else []
        check("cut-in shows both attackers' portraits", len(portraits) == 2, str(portraits))
        h.shot("cutin")
        time.sleep(1.4)
        names = h.call("names")
        check("cut-in cleans up after itself", isinstance(names, list) and not any(str(n).startswith("cutin") for n in names))

    with section('Targeting + ability card + cancel'):
        h.call("givemana", n=8)
        b = battle_state()
        hero = next(u for u in b["units"] if u["team"] == "Hero")
        hx, hy = h.tile(hero["x"], hero["y"])
        h.call("tap", x=hx, y=hy); time.sleep(0.3)
        slot0 = (25 + 108, 450 + 1440 + 20 + 98)
        h.call("tap", x=slot0[0], y=slot0[1]); time.sleep(0.5)
        st = battle_state()
        if st["targeting"] == 0:
            check("slot tap enters targeting", True)
            check("ability card shows while targeting (or the confirm card for a self skill)", has_text("Tap a highlighted tile") or st.get("confirming"))
            h.shot("targeting")
            h.call("tap", x=slot0[0], y=slot0[1]); time.sleep(0.4)
            check("re-tapping the slot cancels", battle_state()["targeting"] == -1)
        else:
            check("slot tap enters targeting (or auto-casts)", st["results"]["ActionsTaken"] >= 2, str(st["targeting"]))

    with section('Select → confirm → act (owner direction 2026-10-11)'):
        b = battle_state()
        acted = b["results"]["ActionsTaken"]
        h.call("tap", x=hx, y=hy); time.sleep(0.3)
        h.call("tap", x=slot0[0], y=slot0[1]); time.sleep(0.4)
        if not battle_state().get("confirming"):
            # a pick ability: aim at the first legal enemy/ally tile by trying each unit
            for u in battle_state()["units"]:
                h.call("tap", x=h.tile(u["x"], u["y"])[0], y=h.tile(u["x"], u["y"])[1]); time.sleep(0.3)
                if battle_state().get("confirming"):
                    break
        st = battle_state()
        check("a pick waits for confirmation (nothing spent yet)", st.get("confirming") and st["results"]["ActionsTaken"] == acted, str(st.get("confirming")))
        names = h.call("names")
        check("the confirm card offers Cast/Use/Go and Cancel", isinstance(names, list) and "confirm-action" in names and "confirm-cancel" in names)
        h.shot("confirm-card")
        h.call("click", name="confirm-cancel"); time.sleep(0.4)
        check("Cancel backs out without acting", battle_state()["targeting"] == -1 and battle_state()["results"]["ActionsTaken"] == acted)
        h.call("tap", x=hx, y=hy); time.sleep(0.3)
        h.call("tap", x=slot0[0], y=slot0[1]); time.sleep(0.4)
        if not battle_state().get("confirming"):
            for u in battle_state()["units"]:
                h.call("tap", x=h.tile(u["x"], u["y"])[0], y=h.tile(u["x"], u["y"])[1]); time.sleep(0.3)
                if battle_state().get("confirming"):
                    break
        h.call("click", name="confirm-action"); h.call("wait", ms=500)
        cues = h.call("cues")
        check("Confirm acts, with a confirmation chime", battle_state()["results"]["ActionsTaken"] == acted + 1 and "ui.confirm" in cues, str(battle_state()["results"]["ActionsTaken"]))
        check("every press is voiced (press tick)", "ui.press" in cues)

    with section('Generic mana orbs: filled vs empty slots read at a glance'):
        h.call("givemana", n=0)  # forces an orb resync
        bank = battle_state()["bank"]
        have = int(bank.split("/")[0])
        filled = h.call("style", name="orb0")["tint"]
        empty = h.call("style", name=f"orb{min(have, 11)}")["tint"] if have < 12 else filled
        check("orb line shows the bank count in one orb color", have >= 1 and (have >= 12 or filled != empty), f"{bank} {filled} vs {empty}")

    with section('Buff pips with timer ring (US-155/156)'):
        b = battle_state()
        target = next(u for u in b["units"] if u["team"] == "Enemy")
        h.call("buff", unit=target["Id"], id="burning")
        st = h.call("style", name=f"buffs-{target['Id']}")
        check("buff shows as a pip on the unit", st.get("children", 0) >= 1, str(st))

    with section("Why isn't it moving? Freeze badge on the tile and on its timeline circle"):
        h.call("buff", unit=target["Id"], id="frozen"); time.sleep(0.3)
        tl = h.call("names", prefix="tl-status-")
        check("frozen enemy shows a status badge on its timeline circle", len(tl) >= 1, str(tl))
        st = h.call("style", name=f"buffs-{target['Id']}")
        check("statuses share one slot on the tile", st.get("children", 0) == 1, str(st))
        seen = set()
        for _ in range(4):
            seen.add(h.call("style", name=f"statuspip-{target['Id']}").get("key")); time.sleep(0.65)
        check("the status slot flashes through every buff/debuff", len(seen - {None}) >= 2, str(seen))
        names_now = h.call("names", prefix="statuspip-")
        check("status pips are pixel icons (flame, snowflake…)", any(h.call("style", name=n).get("children", 0) >= 1 for n in names_now), str(names_now))
        disc = h.call("style", name=next((n for n in h.call("names", prefix="tl-") if n[3:].isdigit()), "tl-1"))
        check("timeline circles are opaque (the line never shows through)", disc.get("background", "").upper().endswith("FF"), str(disc))

    with section('Power Amplifier marker'):
        h.call("devamp", x=0, y=4); time.sleep(0.3)
        amp = h.call("element", name="amplifier")
        tx, ty = h.tile(0, 4)
        check("the Power Amplifier shows on its tile", amp.get("w", 0) > 0 and abs(amp["x"] + amp["w"] / 2 - tx) < 30 and abs(amp["y"] + amp["h"] / 2 - ty) < 30, str(amp))
        h.shot("amplifier")
        probs = [p for p in h.call("layout").get("problems", []) if "Power Amplifier" in p]
        check("the amplifier's feed line fits the combat feed", not probs, " | ".join(probs))

    with section('Pending spell: countdown ring on the caster, spell chip on the target, cycling when several'):
        hero = next(u for u in battle_state()["units"] if u["team"] == "Hero")
        r = h.call("devcast", target=hero["Id"], ability="charge_ice", ticks=40); time.sleep(0.4)
        caster = r.get("caster", -1)
        ring = h.call("element", name=f"castring-{caster}")
        chip = h.call("element", name=f"castchip-{hero['Id']}")
        check("a pending spell rings its caster and marks its target", ring.get("w", 0) > 0 and chip.get("w", 0) > 0, f"{r} {ring} {chip}")
        check("casts are not drawn on the timeline", not any(n.startswith("tl-") and n[3:].isdigit() and h.call("element", name=n).get("w", 0) == 60 for n in h.call("names", prefix="tl-")))
        h.call("devcast", target=hero["Id"], ability="charge_fireball", ticks=60); time.sleep(0.2)
        seen = set()
        for _ in range(5):
            seen.add(h.call("style", name=f"castchip-{hero['Id']}")["border"]); time.sleep(0.5)
        check("two pending spells on one unit cycle their colours", len(seen) >= 2, str(seen))
        h.shot("castring")

    with section('Long-press tooltip'):
        wait_idle(200)
        h.call("press", x=slot0[0], y=slot0[1], ms=700)
        time.sleep(0.2)
        check("long-press shows the ability card", any(("Instant" in x) or ("Charges" in x) or ("Skill" in x) or ("Item" in x) for x in texts()))
        h.call("release"); time.sleep(0.3)

    with section('Pause menu'):
        h.call("click", name="menu"); time.sleep(0.5)
        t = texts()
        check("pause menu has Resume / Auto battle / Retreat", all(any(k in x for x in t) for k in ("Resume", "Auto battle", "Retreat")))
        h.call("clicktext", text="Resume"); time.sleep(0.3)

    with section('Idle hint (first battles)'):
        wait_idle(200)
        time.sleep(6.0)
        banner = h.call("banner")["text"] or ""
        check("idle hint appears", banner.startswith("Hint") or banner.startswith("Tip"), banner)
        h.shot("hint")

    with section("Settings toggles don't break the battle"):
        for key in ("colorblind", "reducemotion"):
            h.call("setting", key=key, value=1)
        # The first battle is a 20-wave gauntlet now; finish a short one (Green Valley II) to reach the results screen.
        h.call("back"); time.sleep(0.6)
        if h.call("state")["screen"] == "battle":
            h.call("clicktext", text="Resume"); time.sleep(0.3)
        h.call("battle", stage="greenvalley-02"); time.sleep(2.5)
        h.call("speed", x=3)
        h.call("autoplay", on=1)
        for _ in range(240):  # ~100 s typical at ×3 now (5 waves); 120 s left no margin on a loaded machine
            time.sleep(1.0)
            s = h.call("state")
            if s["screen"] != "battle":
                break
        # A win that levels a hero shows LevelUpScreen (one "Skip" per hero) before the results.
        until(lambda: h.call("state")["screen"] in ("levelup", "postbattle"), 4.0)
        h._skip_levelups(h.call("state"))
        on_screen("battle finishes with colorblind + reduce motion + ×3 speed", "postbattle")
        check("results music", h.call("music")["track"] in ("HeroicAge", "Heartbreaking"))
        h.shot("results")
        h.call("setting", key="colorblind", value=0)
        h.call("setting", key="reducemotion", value=0)

    with section('Letterbox'):
        h.call("resize", w=1280, h=720); time.sleep(1.2)
        sb = h.call("stagebounds")
        aspect = sb["w"] / sb["h"]
        centered = abs((sb["x"] + sb["w"] / 2) - sb["screen"][0] / 2) < 4
        check("wide window letterboxes (aspect kept, centered)", abs(aspect - 1170 / 2532) < 0.01 and centered, json.dumps(sb))
        h.shot("letterbox-wide")
        h.call("resize", w=585, h=1266); time.sleep(1.0)

    with section('Boss styling (US-196): purple frame on the 2×2 Cyclops'):
        h.call("back"); time.sleep(0.4)
        h.call("clicktext", text="Continue"); time.sleep(1.0)
        h.call("battle", stage="cave-02"); time.sleep(2.0)
        b = battle_state()
        boss = next((u for u in b["units"] if u["cls"].startswith("cyclops")), None)
        if boss is None:
            for _ in range(30):  # the cyclops may arrive in a later wave: autoplay until it appears
                h.call("autoplay", on=1); time.sleep(1.0)
                b = battle_state() or {}
                boss = next((u for u in b.get("units", []) if u["cls"].startswith("cyclops")), None)
                if boss or not b: break
            h.call("autoplay", on=0)
        blocks = h.call("names", prefix="block-")
        check("physical blocks draw on cave stages", len(blocks) >= 2, str(blocks))
        if boss:
            fr = h.call("child_style", name=f"unit-{boss['Id']}", index=1)
            kl = h.call("child_style", name=f"unit-{boss['Id']}", index=1, child=1)
            check("unit frame uses the menu portrait frame (white border)", fr.get("border", "").upper().startswith("#ECEEFF"), str(fr))
            check("2×2 boss gets the purple boss keyline", kl.get("border", "").upper().startswith("#DB66FF"), str(kl))
            h.shot("boss")
        else:
            check("2×2 boss appears in Frostmaw Caverns II", False)

    with section('Title flow on profiles (GG-T116): create, switch, new, continue, load'):
        h.call("autoplay", on=0)
        h.call("goto", screen="title")
        on_screen("title reachable for the profile flow", "title")
        time.sleep(0.6)
        start = h.call("state").get("profile")
        check("title shows the active profile", until(lambda: has_text(start or "?")), f"profile={start}")
        # Create: Profile row -> picker -> + New profile -> type a name -> Create (switches to it, back on the Title).
        h._tap_until(lambda: h.call("click", name="title-profile"), lambda: until(lambda: h.call("state")["screen"] == "profiles", 2.0), tries=3, wait=0.2)
        on_screen("Profile row opens the picker", "profiles")
        time.sleep(0.5)
        h._tap_until(lambda: h.call("click", name="new-profile"), lambda: until(lambda: h.call("state")["screen"] == "profile-name", 2.0), tries=3, wait=0.2)
        on_screen("+ New profile opens the name screen", "profile-name")
        time.sleep(0.5)
        typed = h.call("type", name="profile-name-field", text="Alpha")
        check("profile name field takes typing", typed.get("value") == "Alpha", str(typed))
        h.shot("profile-flow-01-name")
        h._tap_until(lambda: h.call("click", name="profile-name-ok"), lambda: until(lambda: h.call("state")["screen"] == "title", 2.5), tries=3, wait=0.2)
        on_screen("Create returns to the Title", "title")
        check("a created profile becomes the active one", h.call("state").get("profile") == "Alpha", str(h.call("state").get("profile")))
        time.sleep(0.6)
        check("Title shows the new profile's name", has_text("Alpha"))
        check("a new profile has no Continue (its slots are its own)", "Continue" not in texts(), str(texts()))
        h.shot("profile-flow-02-title-alpha")
        # New Game in the new profile: every slot is empty; take slot 2.
        h._tap_until(lambda: h.call("clicktext", text="New Game"), lambda: until(lambda: has_text("Slot 2"), 1.5), tries=3, wait=0.2)
        slots = [t for t in texts() if t.startswith("Slot ")]
        check("the new profile's New Game lists three empty slots", len(slots) == 3 and all("Empty" in t for t in slots), str(slots))
        h._tap_until(lambda: h.call("clicktext", text="Slot 2"), lambda: until(lambda: h.call("state")["screen"] == "charselect", 2.0), tries=3, wait=0.2)
        if on_screen("new profile's New Game opens the party select", "charselect"):
            h.pick_party()
        on_screen("new profile's New Game lands at camp", "hub")
        st = h.call("state")
        check("new game is in the new profile's slot 2", st.get("profile") == "Alpha" and st.get("slot") == 1, f"profile={st.get('profile')} slot={st.get('slot')}")
        root = h.call("profile").get("root", "")
        alpha_saves = [os.path.join(d, f) for d, _, fs in os.walk(os.path.join(root, "Alpha")) for f in fs if f == "slot2.json"]
        check("the save is written under the new profile's own folder", bool(alpha_saves), root)
        # Continue: back on the Title the new profile now has a Continue that resumes its most recent slot (2).
        h.call("clicktext", text="Save & Quit"); until(lambda: has_text("New Game"))
        time.sleep(0.6)
        h._tap_until(lambda: h.call("clicktext", text="Continue"), lambda: until(lambda: h.call("state")["screen"] == "hub", 3.0), tries=3, wait=0.2)
        st = h.call("state")
        check("Continue resumes the active profile's most recent slot", st.get("screen") == "hub" and st.get("profile") == "Alpha" and st.get("slot") == 1,
              f"screen={st.get('screen')} profile={st.get('profile')} slot={st.get('slot')}")
        # Switch: back to the starting profile through the picker; its own Continue resumes its own slot.
        h.call("clicktext", text="Save & Quit"); until(lambda: has_text("New Game"))
        time.sleep(0.6)
        h._tap_until(lambda: h.call("click", name="title-profile"), lambda: until(lambda: h.call("state")["screen"] == "profiles", 2.0), tries=3, wait=0.2)
        time.sleep(0.5)
        h.shot("profile-flow-03-picker")
        h._tap_until(lambda: h.call("click", name=f"profile-{start}"), lambda: until(lambda: h.call("state")["screen"] == "title", 2.5), tries=3, wait=0.2)
        check("switching profiles from the picker returns to the Title as that profile",
              h.call("state").get("screen") == "title" and h.call("state").get("profile") == start, str(h.call("state").get("profile")))
        time.sleep(0.6)
        h._tap_until(lambda: h.call("clicktext", text="Continue"), lambda: until(lambda: h.call("state")["screen"] == "hub", 3.0), tries=3, wait=0.2)
        st = h.call("state")
        check("after switching, Continue resumes that profile's own save, not the other's",
              st.get("screen") == "hub" and st.get("profile") == start and st.get("slot") == 0, f"profile={st.get('profile')} slot={st.get('slot')}")
        # Load: switch back to Alpha; Load Game lists only Alpha's slot 2 and loads it.
        h.call("clicktext", text="Save & Quit"); until(lambda: has_text("New Game"))
        time.sleep(0.6)
        h._tap_until(lambda: h.call("click", name="title-profile"), lambda: until(lambda: h.call("state")["screen"] == "profiles", 2.0), tries=3, wait=0.2)
        time.sleep(0.5)
        h._tap_until(lambda: h.call("click", name="profile-Alpha"), lambda: until(lambda: h.call("state")["screen"] == "title", 2.5), tries=3, wait=0.2)
        check("switching back to the new profile", h.call("state").get("profile") == "Alpha", str(h.call("state").get("profile")))
        time.sleep(0.6)
        h._tap_until(lambda: h.call("clicktext", text="Load Game"), lambda: until(lambda: h.call("state")["screen"] == "load", 2.0), tries=3, wait=0.2)
        on_screen("Load Game opens the slot list", "load")
        time.sleep(0.4)
        slots = [t for t in texts() if t.startswith("Slot ")]
        check("Load Game lists only the active profile's saves",
              len(slots) == 3 and "Empty" in slots[0] and "Empty" not in slots[1] and "Empty" in slots[2], str(slots))
        h.shot("profile-flow-04-load")
        h._tap_until(lambda: h.call("clicktext", text="Slot 2"), lambda: until(lambda: h.call("state")["screen"] == "hub", 3.0), tries=3, wait=0.2)
        st = h.call("state")
        check("Load Game loads the picked slot of the active profile", st.get("screen") == "hub" and st.get("profile") == "Alpha" and st.get("slot") == 1,
              f"screen={st.get('screen')} profile={st.get('profile')} slot={st.get('slot')}")

    with section('Build settings (US-329)'):
        ps = open(os.path.join(h.ROOT, "ProjectSettings", "ProjectSettings.asset"), encoding="utf-8").read()
        ver = open(os.path.join(h.ROOT, "version.txt"), encoding="utf-8").read().strip()
        check("portrait orientation, no splash, version from version.txt",
              "defaultScreenOrientation: 0" in ps and "m_ShowUnitySplashScreen: 0" in ps and f"bundleVersion: {ver}" in ps)

        logs = h.call("logs")
        errs = [l for l in logs if l.startswith(("Error", "Exception", "Assert")) and "first mouse position" not in l]
        check("no errors or exceptions logged", not errs, " | ".join(e[:160] for e in errs[:5]))

    with section('Safe area (US-287): simulate a 120 px notch + 80 px home bar on a 585×1266 window'):
        try:
            h.call("quit", timeout=3)
        except Exception:
            pass
        time.sleep(1.5)
        h.launch("uicheck-safearea", fresh=True, extra=("-safeArea", "120,80"))
        until(lambda: h.call("element", name="screens").get("w", 0) > 0)
        el = h.call("element", name="screens")
        # 120 screen px at 585 px wide = 240 design px; the stage fills the window exactly.
        check("safe-area insets push content below the notch and above the home bar",
              abs(el.get("y", -1) - 240) < 6 and abs((2532 - (el.get("y", 0) + el.get("h", 0))) - 160) < 6, str(el))
        h.shot("safearea-title")

    passed = sum(1 for r in results if r["ok"])
    report = {"passed": passed, "failed": len(results) - passed, "results": results}
    with open(os.path.join(art, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    print(f"\n{passed}/{len(results)} UI checks passed → {os.path.join(art, 'report.json')}")
    try:
        h.call("quit", timeout=3)
    except Exception:
        pass
    sys.exit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
