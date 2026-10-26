"""
Self-play harness: launches the development player with the automation endpoint and drives it.

    python tools/harness.py smoke            # title → new game → hub → battle (autoplay) → results
    python tools/harness.py sweep [RUN [THEME]]  # screenshot + layout-audit every menu screen (THEME: a menu theme, e.g. Slate)
    python tools/harness.py aspects          # GG-T169: title/hub/battle at 9:23..3:4; phones fill, tablets framed
    python tools/harness.py fullloop [N]     # GG-T136: new game -> bounty -> N battles -> sell/forge/brew/enchant -> claim
    python tools/harness.py clips N          # generate N random-stage/random-wave battle clips
    python tools/harness.py boardaudit N     # GG-T97/T129: N autoplayed battles; fail if two tokens (or two items) are ever drawn on one tile
    python tools/harness.py pause            # GG-T126: pause mid-chain freezes playback (two identical frames), resume finishes it
    python tools/harness.py outline          # GG-T128: frame-record a pincer; the outline must ride the camera twist/shake
    python tools/harness.py durability       # GG-T39: last-strike weapon badge + drag-preview shatter warning
    python tools/harness.py crucible [WAVES [RUN]]  # GG-T95: camp -> Crucible -> Auto -> leave after WAVES (99 = fight to a wipe) -> results -> camp
    python tools/harness.py stress HOURS     # unattended soak test (GG-T21); default 4h
    python tools/harness.py testmode N       # GG-T93: -testmode self-play, N full battle→results→camp cycles
    python tools/harness.py shot NAME        # screenshot the running player
    python tools/harness.py call PATH [k=v]  # raw endpoint call, e.g. call state

Screenshots land in artifacts/<run>/. Design coordinates are 1170x2532 (top-left origin).
"""
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "Build", "Windows", "GridGameGaiden.exe")
# GG-T21: this used to default every worktree to the same fixed port (7787), so two agents running
# harness.py in sibling GridGameGaiden worktrees at once would collide — the second one's launch()
# sees the first's player already listening and calls quit on it, silently ending an unrelated
# agent's run mid-battle with no crash/exception trail (confirmed live: a 5h stress run here died
# after ~5 minutes with a clean Application.Quit() shutdown sequence in Player.log that this script
# never requested). Deriving the default from the worktree's own path keeps every worktree's harness
# on its own port automatically; GAIDEN_PORT still overrides it for anyone who wants a fixed value.
_DEFAULT_PORT = 20000 + (int(hashlib.sha1(ROOT.encode()).hexdigest(), 16) % 20000)
PORT = int(os.environ.get("GAIDEN_PORT", str(_DEFAULT_PORT)))
BASE = f"http://127.0.0.1:{PORT}"

BOARD_LEFT, BOARD_TOP, TILE = 45, 450, 180


def tile(x, y):
    return (BOARD_LEFT + TILE * x + TILE // 2, BOARD_TOP + TILE * y + TILE // 2)


def call(endpoint, timeout=130, **params):
    q = urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
    url = f"{BASE}/{endpoint}" + (f"?{q}" if q else "")
    with urllib.request.urlopen(url, timeout=timeout) as r:
        body = r.read().decode("utf-8")
    try:
        return json.loads(body)
    except json.JSONDecodeError:
        return body


def alive():
    try:
        call("ping", timeout=2)
        return True
    except Exception:
        return False


def launch(run, width=585, height=1266, extra=(), fresh=False, muted=True):
    if alive():
        try:
            call("quit", timeout=3)
        except Exception:
            pass
        time.sleep(1.5)
    art = os.path.join(ROOT, "artifacts", run)
    saves = os.path.join(ROOT, "artifacts", run, "saves")
    if fresh and os.path.isdir(saves):
        import shutil
        shutil.rmtree(saves, ignore_errors=True)
    os.makedirs(saves, exist_ok=True)
    # GG-T205: muted by default (Unity.Shared's session mute, -muted) -- the harness drives the player
    # through its automation HTTP endpoint and never listens to it, yet it opens a real windowed process
    # with real audio output on every smoke/sweep/soak run. Pass muted=False to a caller that specifically
    # wants to hear it.
    args = [EXE, "-automation", str(PORT), "-artifacts", art, "-saveRoot", saves,
            "-screen-width", str(width), "-screen-height", str(height), "-screen-fullscreen", "0",
            "-logFile", os.path.join(art, "player.log"), *extra]
    if muted:
        args.append("-muted")
    subprocess.Popen(args, cwd=ROOT)
    for _ in range(60):
        if alive():
            return art
        time.sleep(0.5)
    raise RuntimeError("player did not start")


def shot(name):
    return call("screenshot", name=name)["file"]


def drag_tiles(*tiles, step=0.12):
    pts = ";".join(f"{x},{y}" for x, y in (tile(*t) for t in tiles))
    return call("drag", path=pts, step=step)


STARTING_PARTY = ("paladin", "barbarian", "cleric")  # Meta.Progression.StartingRoster (StartingPartySize, GG-T118)


def _wait_screen(screen, timeout=6.0):
    end = time.time() + timeout
    while time.time() < end:
        try:
            if call("state")["screen"] == screen:
                return True
        except Exception:
            pass
        time.sleep(0.25)
    return False


def pick_party(ids=STARTING_PARTY, timeout=10.0):
    """Drives the New Game party-select screen (CharacterSelectScreen, id "charselect") through the real
    UI: tap a hero's tile to preview it, tap it again to lock it in, once per starting hero. Picks the default
    small starting party (GG-T118: three; the rest join at story beats) so every run gets the same squad the
    2-arg Progression.NewGame gives. Each lock-in is
    verified (a locked tile dims to 0.25 opacity); a tap that doesn't land fails the pick instead of being
    retried blindly. Returns True once the screen has left for the camp hub."""
    if not _wait_screen("charselect", timeout):
        return False
    time.sleep(0.5)  # let the screen's fade-in finish before the first tap
    for cid in ids:
        name = f"tile-{cid}"
        call("scrollto", name=name); time.sleep(0.25)  # most of the grid starts below the fold
        for tap in ("preview", "lock in"):
            r = call("click", name=name); time.sleep(0.4)
            if _tap_failed(r):
                print(f"pick_party: {tap} tap on {cid} didn't land: {r}")
                return False
        if call("state")["screen"] == "charselect" and call("style", name=name).get("opacity", 1) >= 0.5:
            print(f"pick_party: {cid} wasn't locked in after preview + lock-in taps")
            return False
    return _wait_screen("hub", timeout)


def finish_dialogue(timeout=40.0):
    """Plays through a DialogueScreen (§27.1, GG-T117) the way a hurried player would: Skip › to the next
    choice, take the last choice offered (by content convention the way out, so a conversation that loops
    back to its menu still ends), repeat until the screen leaves or the timeout runs out."""
    end = time.time() + timeout
    while time.time() < end:
        if call("state")["screen"] != "dialogue":
            return True
        choices = sorted(call("names", prefix="choice-"))
        if choices:
            call("click", name=choices[-1])
        else:
            call("click", name="dlg-skip")
        time.sleep(0.7)
    return False


def through_story(files=None, wait=1.5):
    """After launching a stage: skips the theme's story crawl (§27) and plays any story-beat dialogue
    (§27.1) so the caller lands on the battle."""
    if call("state")["screen"] == "crawl":
        if files is not None:
            files.append(shot("04-crawl"))
        call("clicktext", text="Skip"); time.sleep(wait)
    if call("state")["screen"] == "dialogue":
        if files is not None:
            time.sleep(0.8); files.append(shot("04-dialogue"))
        finish_dialogue()
        time.sleep(wait)


def smoke(run="smoke"):
    launch(run, fresh=True)  # fresh: an occupied Slot 1 would stop at the overwrite confirm instead
    _wait_screen("title")
    time.sleep(0.6)  # the title's fade-in swallows a tap sent the moment the server answers
    files = [shot("01-title")]
    _tap_until(lambda: call("clicktext", text="New Game"), lambda: any("Slot 1" in t for t in call("texts")), tries=4)
    _tap_until(lambda: call("clicktext", text="Slot 1"), lambda: call("state")["screen"] == "charselect", tries=4)
    files.append(shot("02-charselect"))
    pick_party()
    files.append(shot("02-hub"))
    call("clicktext", text="Adventure"); time.sleep(0.8)
    files.append(shot("03-stages"))
    call("clicktext", text="Green Valley I"); time.sleep(1.5)
    through_story(files)
    files.append(shot("05-battle"))
    call("autoplay", on=1)
    for i in range(40):
        time.sleep(1.5)
        st = call("state")
        if i % 3 == 0 and st["screen"] == "battle":
            files.append(shot(f"06-battle-{i:02d}"))
        if st["screen"] != "battle":
            break
    time.sleep(1)
    files.append(shot("07-results"))
    logs = call("logs")
    print(json.dumps({"final": call("state").get("screen"), "save": call("state").get("save"), "errors": [l for l in logs if l.startswith(("Error", "Exception"))]}, indent=1))
    print("\n".join(files))


def sweep(run="sweep", theme=None):
    launch(run)
    call("newgame", slot=0, gold=5000, npcs=1)  # npcs: every Camp station on the hub shot (GG-T119)
    if theme:  # GG-T185: sweep a menu theme (Art.Themes name, e.g. Slate)
        print(call("setting", key="menutheme", value=theme))
    time.sleep(1)
    report = {}
    for screen in ["hub", "stages", "items", "status", "equip", "abilities", "party", "shop", "blacksmith",
                   "alchemist", "enchantress", "talk", "summon", "bounties", "crucible", "bestiary", "settings", "credits"]:
        call("goto", screen=screen)
        time.sleep(0.9)
        shot(f"sweep-{screen}")
        report[screen] = call("layout").get("problems")
        call("back")
        time.sleep(0.6)
    print(json.dumps(report, indent=1))
    logs = call("logs")
    print("errors:", [l for l in logs if l.startswith(("Error", "Exception"))])


# GG-T169 device fit: (label, aspect w/h). Phones 9:21..9:16 must fill the window; tablets and ultra-tall are framed.
ASPECTS = [("9x23-ultratall", 9 / 23), ("9x21", 9 / 21), ("9x20", 9 / 20), ("9x19.5", 9 / 19.5), ("9x18", 9 / 18),
           ("9x16", 9 / 16), ("2x3-tablet", 2 / 3), ("3x4-tablet", 3 / 4)]


def aspects(run="aspects", height=1200):
    """GG-T169: launches the player at every supported phone ratio plus tablet/ultra-tall shapes; shoots the
    title, hub and a live battle; checks the stage size never drops below 1170x2532, fills the window on phones
    (no frame) and is framed outside 9:21..9:16; runs the layout audit on each screen. Exit 1 on any failure."""
    failures, report = [], {}
    for label, a in ASPECTS:
        w = int(round(height * a))
        launch(f"{run}/{label}", width=w, height=height, fresh=True)
        _wait_screen("title"); time.sleep(0.8)
        sb = call("stagebounds")
        dw, dh = sb["design"]
        expect_framed = not (9 / 21 - 0.002 <= w / height <= 9 / 16 + 0.002)
        entry = {"window": [w, height], "stage": [dw, dh], "framed": sb["framed"], "screens": {}}
        if dw < 1170 or dh < 2532:
            failures.append(f"{label}: stage {dw}x{dh} below the 1170x2532 minimum")
        if sb["framed"] != expect_framed:
            failures.append(f"{label}: framed={sb['framed']}, expected {expect_framed}")
        shot(f"{label}-1-title"); entry["screens"]["title"] = call("layout").get("problems")
        call("newgame", slot=0); time.sleep(1.2)
        shot(f"{label}-2-hub"); entry["screens"]["hub"] = call("layout").get("problems")
        call("clicktext", text="Adventure"); time.sleep(0.8)
        call("clicktext", text="Green Valley I"); time.sleep(1.5)
        through_story(wait=1.2)
        if _wait_screen("battle", 8):
            time.sleep(2.0)
            shot(f"{label}-3-battle"); entry["screens"]["battle"] = call("layout").get("problems")
        else:
            failures.append(f"{label}: never reached the battle screen")
        for scr, probs in entry["screens"].items():
            if probs:
                failures.append(f"{label}/{scr}: {probs}")
        errs = [l for l in call("logs") if l.startswith(("Error", "Exception"))]
        if errs:
            failures.append(f"{label}: log errors {errs[:3]}")
        report[label] = entry
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print(json.dumps(report, indent=1))
    print("FAILURES:" if failures else "ALL ASPECTS OK", *failures, sep="\n  ")
    return not failures


def _play_battle(tag, timeout=480):
    """Turns on autoplay and waits for the current battle to leave the battle screen (win or lose).
    Returns ("ended", state) | ("stalled", state) | ("timeout", state)."""
    call("autoplay", on=1)
    t0 = time.time(); last_clock = -1; stuck = 0; shot_taken = False
    while time.time() - t0 < timeout:
        time.sleep(1.0)
        st = call("state")
        if st["screen"] != "battle":
            return "ended", st
        b = st["battle"]
        if not shot_taken and b["clock"] > 60:
            shot(tag); shot_taken = True
        stuck = stuck + 1 if b["clock"] == last_clock and not b["busy"] else 0
        last_clock = b["clock"]
        if stuck > 12:
            return "stalled", b
    return "timeout", call("state")


def _skip_levelups(st):
    for _ in range(8):  # one "Skip" per hero that leveled up (LevelUpScreen.Advance), capped at MaxPartySize
        if st["screen"] != "levelup":
            break
        call("clicktext", text="Skip"); time.sleep(0.7)
        st = call("state")
    return st


def campaign(run="campaign", stages=6, speed=3, extra=(), max_loss_retries=3):
    """Autoplays consecutive stages through the real UI (Next stage / Retry), checking for stalls and errors.
    Pass extra=("-capture", "video", "screen", "5") to record the run (capture.json must have "enabled": true).
    The raw tape grows fast at HighQuality — ~500MB+/minute of real time observed on one run — so a
    continuous capture of more than a handful of stages means a very large .mkv before it's finalized
    into the much smaller .mp4; capture.json's battle-start/battle-end rules (separate per-battle clips)
    are the safer default for anything longer than a short highlight.

    A lost battle inside a multi-battle expedition restarts it from battle 1 by design (GG-T17): that's
    not a harness bug to route around, so on defeat this clicks the real "Retry" button and replays, up
    to max_loss_retries times, before giving up and recording DIFFICULTY_WALL — distinguishing a real
    balance cliff for blind autoplay (loses every attempt) from one-off automation flakiness (wins on retry)."""
    launch(run, extra=extra)
    call("newgame", slot=0)
    time.sleep(1)
    call("speed", x=speed)
    call("clicktext", text="Adventure"); time.sleep(0.8)
    call("clicktext", text="Green Valley I"); time.sleep(1.2)
    through_story(wait=1.2)
    results = []
    for n in range(stages):
        outcome, st = _play_battle(f"stage{n}-mid")
        if outcome != "ended":
            results.append({"stage": n, "result": outcome.upper(), "state": st})
            break

        time.sleep(1.2)
        st = _skip_levelups(call("state"))

        # Win or lose, PostBattleScreen always renders a "Continue" button; only a loss also renders
        # "Retry". Probing for it both tells win from loss and, on a loss, is the retry tap itself.
        gave_up = False
        for attempt in range(1, max_loss_retries + 1):
            r = call("clicktext", text="Retry")
            if isinstance(r, dict) and r.get("error"):
                break  # no "Retry" button visible: this was a win
            print(f"stage{n}: lost, retrying ({attempt}/{max_loss_retries})")
            time.sleep(1.5)
            for _ in range(20):
                if call("state")["screen"] == "battle":
                    break
                time.sleep(0.2)
            outcome, st = _play_battle(f"stage{n}-retry{attempt}-mid")
            if outcome != "ended":
                results.append({"stage": n, "result": outcome.upper(), "state": st, "after_retries": attempt})
                gave_up = True
                break
            time.sleep(1.2)
            st = _skip_levelups(call("state"))
        else:
            # Every retry up to max_loss_retries played ("ended" each time), but that last retry's own
            # result was never probed — that would be one more iteration. Check it now before declaring
            # a wall, so a win on the final retry isn't misreported as one.
            r = call("clicktext", text="Retry")
            if not (isinstance(r, dict) and r.get("error")):
                results.append({"stage": n, "result": "DIFFICULTY_WALL", "retries": max_loss_retries})
                print(f"stage{n}: lost {max_loss_retries} times running — real difficulty cliff, not automation flakiness, stopping")
                gave_up = True
        if gave_up:
            break

        shot(f"stage{n}-result")
        results.append({"stage": n, "screen": st["screen"], "gold": st.get("save", {}).get("Gold"), "levels": st.get("save", {}).get("levels")})
        if st["screen"] != "postbattle":
            break
        # PostBattleScreen (owner's Expedition rework) names its continuation button "next" whether
        # its text reads "Next battle ›" (mid-expedition) or "Next expedition ›" (crossing into the
        # next one) — there is no literal "Next stage" button. Only a dead-end (last unlocked content)
        # lacks a "next" element, in which case "Continue" is the only way forward (back to the Hub).
        #
        # AutomationServer's click/clicktext could occasionally go silently unresponsive on a
        # long-lived player process (GG-T16): the HTTP call replies ok:true with plausible coordinates,
        # but the tap never registers and the screen never advances. Root-caused and fixed in GG-T20
        # (an orphaned Confirm veil eating every click) — this verify-and-retry is kept as defense in
        # depth rather than ripped out, since it's cheap and a real human-facing path could still leave
        # some other UI element in a dead state we haven't found yet.
        for attempt in range(5):
            r = call("click", name="next")
            if _tap_failed(r):
                call("clicktext", text="Continue")
            time.sleep(1.5)
            if call("state")["screen"] != "postbattle":
                break
            print(f"stage{n}: click(next)/Continue didn't advance, retry {attempt + 1}/5")
        else:
            print(f"stage{n}: click retries exhausted, screen still postbattle — stopping run")
            break
        through_story(wait=1.2)
    logs = call("logs")
    print(json.dumps(results, indent=1))
    print("errors:", [l[:300] for l in logs if l.startswith(("Error", "Exception")) and "first mouse" not in l])


def _inv():
    return dict(call("state").get("save", {}).get("inventory") or {})


def _goto(screen):
    call("goto", screen=screen); time.sleep(0.9)
    return call("state")["screen"] == screen


def fullloop(run="fullloop", battles=4, speed=3):
    """GG-T136: one fresh save through every system in order, asserting each hands off to the next: New Game
    (real UI) -> accept a bounty -> story beats + several battles on autoplay -> back to camp -> sell a drop ->
    forge / brew / enchant whatever the drops now allow -> claim the bounty if done. Fails on a battle that
    stalls, a hand-off whose numbers don't move (fights pay no gold, a sale pays nothing, a craft makes nothing),
    or any logged error. Skill points (GG-T53) are parked with the build epic and not exercised yet."""
    fails, notes = [], []
    launch(run, fresh=True)
    _wait_screen("title"); time.sleep(0.6)
    _tap_until(lambda: call("clicktext", text="New Game"), lambda: any("Slot 1" in t for t in call("texts")), tries=4)
    _tap_until(lambda: call("clicktext", text="Slot 1"), lambda: call("state")["screen"] == "charselect", tries=4, wait=1.0)
    if not pick_party():
        print("FAIL: party select"); return False
    call("speed", x=speed)
    gold0, inv0 = call("camp")["Gold"], _inv()
    shot("01-hub-start")

    # GG-T174: a fresh camp opens stations one at a time — the Bounty board starts locked.
    if not call("names", prefix="locked-bounties"):
        fails.append("fresh camp: Bounties should be a locked row")

    # Fights: Adventure -> first stage, story beats, then `battles` battles in a row.
    call("clicktext", text="Adventure"); time.sleep(0.8)
    call("clicktext", text="Green Valley I"); time.sleep(1.2)
    through_story(wait=1.2)
    for n in range(battles):
        outcome, st = _play_battle(f"02-battle{n}-mid")
        if outcome != "ended":
            fails.append(f"battle {n}: {outcome}"); break
        time.sleep(1.2)
        st = _skip_levelups(call("state"))
        for attempt in range(3):
            r = call("clicktext", text="Retry")
            if isinstance(r, dict) and r.get("error"):
                break
            notes.append(f"battle {n}: lost, retry {attempt + 1}")
            time.sleep(1.5); _wait_screen("battle", 8)
            outcome, st = _play_battle(f"02-battle{n}-retry{attempt}")
            time.sleep(1.2); st = _skip_levelups(call("state"))
        shot(f"03-result{n}")
        last = n == battles - 1
        call("click" if not last else "clicktext", **({"name": "next"} if not last else {"text": "Continue"}))
        time.sleep(1.5)
        if not last:
            through_story(wait=1.2)
    if not _wait_screen("hub", 10):
        call("goto", screen="hub"); time.sleep(1)
    camp = call("camp")
    gold1, inv1 = camp["Gold"], _inv()
    gained = {k: v - inv0.get(k, 0) for k, v in inv1.items() if v > inv0.get(k, 0)}
    if gold1 <= gold0:
        fails.append(f"fights paid no gold ({gold0} -> {gold1})")
    if not gained:
        fails.append("fights dropped nothing into the inventory")
    shot("04-hub-after-fights")

    # Bounty board: opened by now (GG-T174 milestone); take the first contract.
    if call("names", prefix="locked-bounties"):
        fails.append(f"Bounties still locked after {battles} battles")
    elif _goto("bounties"):
        posted = call("camp")["bounty"]["posted"]
        if posted:
            call("click", name=f"bounty-{posted[0]}"); time.sleep(0.6)
            if not call("camp")["bounty"]["active"]:
                fails.append(f"accepting bounty {posted[0]} didn't make it active")
        else:
            notes.append("no bounties posted")
        call("back"); time.sleep(0.6)

    # Merchant: sell one of something the fights dropped (fall back to anything sellable).
    sellable = camp["sellable"]
    pick = next((s for s in sellable if s["Id"] in gained), sellable[0] if sellable else None)
    if pick and _goto("shop"):
        call("clicktext", text="Sell"); time.sleep(0.6)
        call("click", name=f"cart-plus-{pick['Id']}"); time.sleep(0.4)
        call("click", name="shop-commit"); time.sleep(0.8)
        after = call("camp")["Gold"]
        if after != gold1 + pick["price"]:
            fails.append(f"selling 1x {pick['Id']} @ {pick['price']} moved gold {gold1} -> {after}")
        shot("05-shop-sold")
        call("back"); time.sleep(0.6)
    elif not pick:
        fails.append("nothing sellable after the fights")

    # Crafting stations: make the first thing each one can make now.
    crafted = 0
    for station, screen in (("forge", "blacksmith"), ("alchemy", "alchemist"), ("enchant", "enchantress")):
        options = call("camp")[station]
        if not options:
            notes.append(f"{screen}: nothing craftable yet")
            continue
        r = options[0]
        before = _inv().get(r["Result"], 0)
        if not _goto(screen):
            fails.append(f"couldn't open {screen}"); continue
        call("click", name=f"recipe-{r['Id']}"); time.sleep(0.5)
        call("click", name="craft"); time.sleep(0.8)
        if _inv().get(r["Result"], 0) <= before:
            fails.append(f"{screen}: crafting {r['Id']} produced no {r['Result']}")
        else:
            crafted += 1
        shot(f"06-{screen}")
        call("back"); time.sleep(0.6)
    if crafted == 0:
        notes.append("no station could craft anything from these drops (check drop tables vs. tier-1 recipes)")

    # Bounty: claim if the fights finished it.
    b = call("camp")["bounty"]
    if b["active"] and b["complete"] and _goto("bounties"):
        g = call("camp")["Gold"]
        call("click", name="bounty-claim"); time.sleep(0.8)
        if call("camp")["Gold"] <= g:
            fails.append("claiming a finished bounty paid nothing")
        call("back"); time.sleep(0.6)
    elif b["active"]:
        notes.append(f"bounty {b['active']} at {b['progress']} after {battles} battles (not complete)")

    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception")) and "first mouse" not in l]
    if errs:
        fails.append(f"log errors: {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print(json.dumps({"gold": [gold0, gold1], "dropped": gained, "crafted": crafted, "notes": notes}, indent=1))
    print("FAILURES:" if fails else "FULL LOOP OK", *fails, sep="\n  ")
    return not fails


def camptips(run="camptips"):
    """GG-T173: the first visit to a Camp station shows its tip (veil + pointing hand + card); "Got it" dismisses it
    for good; Settings > How to play > Replay camp tips brings it back. Launches with -campTips (harness runs
    suppress tips otherwise)."""
    fails = []
    launch(run, extra=("-campTips",), fresh=True)
    call("newgame", slot=0, gold=500, npcs=1); time.sleep(1)
    tip = lambda: bool(call("names", prefix="camp-tip-card"))
    for station in ("shop", "blacksmith", "alchemist", "bounties"):
        _goto(station); time.sleep(0.5)
        shot(f"tip-{station}")
        if not tip():
            fails.append(f"{station}: no tip on first visit")
        else:
            probs = call("layout").get("problems")
            if probs:
                fails.append(f"{station}: layout {probs}")
        call("click", name="camp-tip-ok"); time.sleep(0.5)
        if tip():
            fails.append(f"{station}: Got it didn't dismiss the tip")
        call("back"); time.sleep(0.5)
    _goto("shop"); time.sleep(0.5)
    if tip():
        fails.append("shop: tip came back after being dismissed")
    call("back"); time.sleep(0.5)
    _goto("settings"); call("scrollto", name="replay-camp-tips"); time.sleep(0.3)
    call("click", name="replay-camp-tips"); time.sleep(0.6); call("back"); time.sleep(0.5)
    _goto("shop"); time.sleep(0.5)
    if not tip():
        fails.append("shop: Replay camp tips didn't bring the tip back")
    shot("tip-shop-replayed")
    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception"))]
    if errs:
        fails.append(f"log errors {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("FAILURES:" if fails else "CAMP TIPS OK", *fails, sep="\n  ")
    return not fails


def smithrepair(run="smithrepair"):
    """GG-T175: onboarding step 2. With only the Merchant and Blacksmith met and a hero's weapon worn to 50%, the
    Blacksmith opens on Repair (Forge/Upgrade/Salvage dimmed and refusing taps), his camp tip names the worn piece
    and rings its repair row; dismissing it and tapping that row repairs it to full."""
    fails = []
    launch(run, extra=("-campTips",), fresh=True)
    call("newgame", slot=0, gold=500, npcs="merchant,blacksmith"); time.sleep(1)
    call("devequipall", item="eq_sword_iron", wear=0.5); time.sleep(0.3)
    worn = call("camp").get("worn")
    if not worn or worn["Durability"] >= worn["Max"]:
        fails.append(f"devequipall wear=0.5 left nothing worn: {worn}")
    if call("camp").get("workshop"):
        fails.append("workshop should be closed before the Alchemist joins")
    _goto("blacksmith"); time.sleep(0.8)
    shot("01-smith-tip")
    if not call("names", prefix="camp-tip-card"):
        fails.append("no Blacksmith tip on first visit")
    elif not call("names", prefix="camp-tip-ring"):
        fails.append("Blacksmith tip has no ring: worn row not found")
    if worn and not call("names", prefix=worn["row"]):
        fails.append(f"worn piece row {worn['row']} not on screen (Repair should be the open tab)")
    probs = call("layout").get("problems")
    if probs:
        fails.append(f"layout {probs}")
    call("click", name="camp-tip-ok"); time.sleep(0.5)
    call("click", name="tab-forge"); time.sleep(0.5)
    if worn and not call("names", prefix=worn["row"]):
        fails.append("tapping the locked Forge tab left Repair")
    shot("02-smith-forge-locked")
    if worn:
        g = call("camp")["Gold"]
        call("click", name=worn["row"]); time.sleep(0.8)
        after = call("camp")
        w2 = after.get("worn")
        if after["Gold"] >= g:
            fails.append("repairing the worn piece cost nothing")
        if w2 and w2["row"] == worn["row"] and w2["Durability"] < w2["Max"]:
            fails.append(f"worn piece not repaired: {w2}")
        shot("03-smith-repaired")
    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception"))]
    if errs:
        fails.append(f"log errors {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("FAILURES:" if fails else "SMITH REPAIR OK", *fails, sep="\n  ")
    return not fails


def firstbrew(run="firstbrew"):
    """GG-T176: onboarding step 3. With the Merchant and Alchemist met, a Wolf Pelt, Tattered Cloth and the Empty
    Bottle he hands over in the bag, the Alchemist opens with the Healing Potion bottle recipe selected; his camp tip
    names it and rings Brew; dismissing it and tapping Brew turns the loot and bottle into a Healing Potion."""
    fails = []
    launch(run, extra=("-campTips",), fresh=True)
    call("newgame", slot=0, gold=100, npcs="merchant,alchemist"); time.sleep(1)
    for item in ("mat_wolf_pelt", "mat_tattered_cloth", "empty_bottle"):
        call("devgive", item=item)
    def count(item):
        return next((x["count"] for x in call("camp")["sellable"] if x["Id"] == item), 0)
    potions = count("healing_potion_basic")
    _goto("alchemist"); time.sleep(0.8)
    shot("01-alchemist-tip")
    if not call("names", prefix="camp-tip-card"):
        fails.append("no Alchemist tip on first visit")
    elif not call("names", prefix="camp-tip-ring"):
        fails.append("Alchemist tip has no ring: Brew button not found (bottle recipe not preselected?)")
    texts = " ".join(str(t) for t in call("texts"))
    if "Healing Potion" not in texts or "Tap Brew" not in texts:
        fails.append("tip doesn't name the Healing Potion brew")
    probs = call("layout").get("problems")
    if probs:
        fails.append(f"layout {probs}")
    call("click", name="camp-tip-ok"); time.sleep(0.5)
    call("click", name="craft"); time.sleep(0.8)
    shot("02-alchemist-brewed")
    if count("healing_potion_basic") != potions + 1:
        fails.append(f"Brew didn't make a Healing Potion ({potions} -> {count('healing_potion_basic')})")
    if count("empty_bottle") or count("mat_wolf_pelt"):
        fails.append("the bottle and pelt weren't used up")
    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception"))]
    if errs:
        fails.append(f"log errors {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("FAILURES:" if fails else "FIRST BREW OK", *fails, sep="\n  ")
    return not fails


def firstforge(run="firstforge"):
    """GG-T177: onboarding step 4. With the Blacksmith's repair tip behind him and the Alchemist met (the workshop is
    open), and 2 Slime Gel + a Tattered Cloth in the bag, the Blacksmith opens on Forge with the Tallow Lantern starter
    recipe selected; the Forge tip names it and the Cleric and rings Craft; dismissing it and tapping Craft forges the
    lantern for no gold and the Cleric equips it."""
    fails = []
    launch(run, extra=("-campTips",), fresh=True)
    call("newgame", slot=0, gold=100, npcs="merchant,blacksmith,alchemist", seen="blacksmith"); time.sleep(1)
    for item, n in (("mat_slime_gel", 2), ("mat_tattered_cloth", 1)):
        for _ in range(n):
            call("devgive", item=item)
    camp = call("camp")
    ff = camp.get("firstForge")
    if not camp.get("workshop"):
        fails.append("workshop should be open once the Alchemist has joined")
    if not ff or ff["recipe"] != "recipe_starter_lantern" or ff["hero"] != "cleric" or not ff["ready"]:
        fails.append(f"first forge should be the ready Tallow Lantern for the Cleric: {ff}")
    _goto("blacksmith"); time.sleep(0.8)
    shot("01-forge-tip")
    if not call("names", prefix="camp-tip-card"):
        fails.append("no Forge tip on opening the workshop")
    elif not call("names", prefix="camp-tip-ring"):
        fails.append("Forge tip has no ring: Craft button not found (starter recipe not preselected?)")
    texts = " ".join(str(t) for t in call("texts"))
    if "Tallow Lantern" not in texts or "Cleric" not in texts or "Tap Craft" not in texts:
        fails.append("tip doesn't name the Tallow Lantern for the Cleric")
    probs = call("layout").get("problems")
    if probs:
        fails.append(f"layout {probs}")
    call("click", name="camp-tip-ok"); time.sleep(0.5)
    gold = call("camp")["Gold"]
    call("click", name="craft"); time.sleep(0.8)
    shot("02-forged-and-equipped")
    after = call("camp")
    if after["weapons"].get("cleric") != "eq_lantern_tallow":
        fails.append(f"the Cleric didn't equip the lantern: {after['weapons']}")
    if after["Gold"] != gold:
        fails.append(f"the first forge cost gold ({gold} -> {after['Gold']})")
    if after.get("firstForge"):
        fails.append("first forge still pending after forging")
    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception"))]
    if errs:
        fails.append(f"log errors {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("FAILURES:" if fails else "FIRST FORGE OK", *fails, sep="\n  ")
    return not fails


def firstarmor(run="firstarmor"):
    """GG-T178: onboarding step 5. Before the first forge no armor-set recipe is open; forging the starter lantern
    (as in firstforge) opens them and the Forge's armor tip appears at once, naming Wayfarer's Trousers for the
    Barbarian and ringing Craft; dismissing it and tapping Craft forges them (15 G) and the Barbarian wears them."""
    fails = []
    launch(run, extra=("-campTips",), fresh=True)
    call("newgame", slot=0, gold=100, npcs="merchant,blacksmith,alchemist", seen="blacksmith"); time.sleep(1)
    for item, n in (("mat_slime_gel", 2), ("mat_tattered_cloth", 1), ("mat_leather", 2), ("mat_wolf_pelt", 1)):
        for _ in range(n):
            call("devgive", item=item)
    camp = call("camp")
    if camp.get("setRecipes") or camp.get("firstArmor"):
        fails.append(f"armor-set recipes open before the first forge: {camp.get('setRecipes')} {camp.get('firstArmor')}")
    _goto("blacksmith"); time.sleep(0.8)
    call("click", name="camp-tip-ok"); time.sleep(0.5)
    call("click", name="craft"); time.sleep(1.0)
    shot("01-armor-tip")
    camp = call("camp")
    fa = camp.get("firstArmor")
    if camp.get("weapons", {}).get("cleric") != "eq_lantern_tallow":
        fails.append(f"first forge didn't go through: {camp.get('weapons')}")
    if not camp.get("setRecipes"):
        fails.append("armor-set recipes still shut after the first forge")
    if not fa or fa["recipe"] != "recipe_wayfarer_legs" or fa["hero"] != "barbarian" or not fa["ready"]:
        fails.append(f"first armor should be the ready Wayfarer's Trousers for the Barbarian: {fa}")
    if not call("names", prefix="camp-tip-card"):
        fails.append("no armor tip after the first forge")
    elif not call("names", prefix="camp-tip-ring"):
        fails.append("armor tip has no ring: Craft button not found (piece not preselected?)")
    texts = " ".join(str(t) for t in call("texts"))
    if "Wayfarer's Trousers" not in texts or "Barbarian" not in texts or "Tap Craft" not in texts:
        fails.append("armor tip doesn't name Wayfarer's Trousers for the Barbarian")
    probs = call("layout").get("problems")
    if probs:
        fails.append(f"layout {probs}")
    call("click", name="camp-tip-ok"); time.sleep(0.5)
    gold = camp["Gold"]
    call("click", name="craft"); time.sleep(0.8)
    shot("02-armor-forged-and-worn")
    after = call("camp")
    if "eq_wayfarer_legs" not in after.get("armor", {}).get("barbarian", []):
        fails.append(f"the Barbarian isn't wearing the trousers: {after.get('armor')}")
    if after["Gold"] != gold - 15:
        fails.append(f"the first armor should cost its 15 G ({gold} -> {after['Gold']})")
    if after.get("firstArmor"):
        fails.append("first armor still pending after forging")
    errs = [l[:300] for l in call("logs") if l.startswith(("Error", "Exception"))]
    if errs:
        fails.append(f"log errors {errs[:3]}")
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("FAILURES:" if fails else "FIRST ARMOR OK", *fails, sep="\n  ")
    return not fails


def story_recruits(run="recruits", speed=3):
    """GG-T118: a new game starts small and grows at story beats. Picks the starting three through the real
    party select, clears Green Valley I on autoplay, then launches the next battle and checks that the
    story-recruit dialogue plays first — the joining hero speaking with their own portrait — and that the
    party is four once it ends. Screenshots: charselect, hub, the recruit dialogue, the next battle."""
    launch(run, fresh=True)
    _wait_screen("title"); time.sleep(0.6)
    ok = {}
    _tap_until(lambda: call("clicktext", text="New Game"), lambda: any("Slot 1" in t for t in call("texts")), tries=4)
    _tap_until(lambda: call("clicktext", text="Slot 1"), lambda: call("state")["screen"] == "charselect", tries=4, wait=1.0)
    shot("01-charselect")
    ok["charselect_hint"] = any("more heroes join" in t for t in call("texts"))
    pick_party()
    shot("02-hub")
    party0 = list(call("state").get("save", {}).get("party") or [])
    ok["starts_with_three"] = party0 == list(STARTING_PARTY)
    call("speed", x=speed)
    call("clicktext", text="Adventure"); time.sleep(0.8)
    call("clicktext", text="Green Valley I"); time.sleep(1.5)
    through_story()
    outcome, st = _play_battle("03-gv1-mid")
    time.sleep(1.2)
    st = _skip_levelups(call("state"))
    ok["gv1_won"] = outcome == "ended" and (call("state").get("save") or {}).get("HighestClearedStage", -1) >= 0
    for attempt in range(5):
        r = call("click", name="next")
        if isinstance(r, dict) and r.get("error"):
            call("clicktext", text="Continue")
        time.sleep(1.5)
        if call("state")["screen"] != "postbattle":
            break
    if call("state")["screen"] == "crawl":
        call("clicktext", text="Skip"); time.sleep(1.5)
    ok["recruit_beat_plays"] = _wait_screen("dialogue", 6.0)
    texts = []
    if ok["recruit_beat_plays"]:
        time.sleep(1.0)
        shot("04-recruit-dialogue")
        texts = call("texts")
        ok["recruit_has_portrait"] = any(n.startswith("dlg-portrait") for n in call("names", prefix="dlg-portrait"))
        finish_dialogue()
        time.sleep(1.5)
    while call("state")["screen"] == "dialogue":  # the merchant's after-battle chat may follow on a later launch
        finish_dialogue(); time.sleep(1.0)
    party1 = list(call("state").get("save", {}).get("party") or [])
    ok["party_grew_to_four"] = len(party1) == len(party0) + 1 and party1[:len(party0)] == party0
    shot("05-next-battle")
    logs = call("logs")
    errors = [l[:300] for l in logs if l.startswith(("Error", "Exception")) and "first mouse" not in l]
    print(json.dumps({"checks": ok, "party_before": party0, "party_after": party1, "dialogue_text": texts[:12], "errors": errors}, indent=1))
    return all(ok.values()) and not errors


def battle_clips(run="clips", count=10, speed=3, timeout=240, capture=True, relaunch=True):
    """Generates many short, independent battle clips via the randombattle endpoint: a random real
    stage + a random wave from it (one enemy family per clip, per the house wave convention), at that
    stage's real recommended level. No UI clicks are involved in the loop at all (ResetTo tears down
    whatever screen a prior clip left behind), so this sidesteps the click-reliability concerns (GG-T16,
    root-caused and fixed as the orphaned-Confirm-veil bug in GG-T20) that campaign() has to guard against.
    capture.json's battle-start/battle-end rules are NOT auto-loaded just by sitting in StreamingAssets —
    MindAttic.Capture only reads a config file when told to with -capture-config (confirmed by reading
    CaptureUnity.Boot()/CaptureOptions.Apply() in the package; the literal -capture flag is NOT required
    for rule-triggered recording, only -capture-config is). So this passes -capture-config pointing at the
    built player's own copy of capture.json, which still needs "enabled": true to actually record — set
    that before calling this, and back to false afterward, then rebuild so the committed state matches."""
    extra = ("-capture-config", os.path.join(os.path.dirname(EXE), "GridGameGaiden_Data",
                                              "StreamingAssets", "capture.json")) if capture else ()
    if relaunch:
        launch(run, extra=extra)
    call("speed", x=speed)
    clips = []
    for n in range(count):
        info = call("randombattle")
        if not isinstance(info, dict) or not info.get("ok"):
            print(f"clip{n}: randombattle failed: {info}")
            break
        print(f"clip{n}: {info['source']} -> {info['stage']} lvl{info['level']} {info['enemies']}")
        # ResetTo's fade transition (ScreenRouter.Go) takes ~0.2s+ to actually mount BattleScreen;
        # calling autoplay before that lands hits "not in battle" and silently never turns it on.
        for _ in range(20):
            if call("state")["screen"] == "battle":
                break
            time.sleep(0.2)
        r = call("autoplay", on=1)
        if isinstance(r, dict) and r.get("error"):
            print(f"clip{n}: autoplay failed to engage: {r}")
        t0 = time.time(); last_clock = -1; stuck = 0; result = "TIMEOUT"
        while time.time() - t0 < timeout:
            time.sleep(1.0)
            st = call("state")
            if st["screen"] != "battle":
                result = st["screen"]
                break
            b = st["battle"]
            stuck = stuck + 1 if b["clock"] == last_clock and not b["busy"] else 0
            last_clock = b["clock"]
            if stuck > 12:
                result = "STALLED"
                break
        clips.append({**info, "result": result})
    logs = call("logs")
    print(json.dumps(clips, indent=1))
    print("errors:", [l[:300] for l in logs if l.startswith(("Error", "Exception")) and "first mouse" not in l])
    return clips


def board_audit(run="boardaudit", count=12, speed=4, timeout=300):
    """GG-T97: plays `count` random real battles on autoplay (randombattle, no capture) and fails unless the drawn
    board stayed honest the whole time: after every beat no two tokens on one tile, after every action every token
    on exactly its unit's State tile (before the SyncAll safety net could hide a miss), and the engine's last-resort
    tile-conflict net never fired. GG-T129 adds board items: no two item tokens (pickups, the Power Amplifier) ever
    drawn on one tile, and no stale item token (every drawn pickup/amplifier matches the State). Reads BoardAudit
    through the boardaudit endpoint. Returns True when clean."""
    launch(run)
    call("boardaudit", reset=1)
    clips = battle_clips(run=run, count=count, speed=speed, timeout=timeout, capture=False, relaunch=False)
    audit = call("boardaudit")["audit"]
    print(json.dumps({"battles": len(clips), "audit": audit}, indent=1))
    bad = audit["beat_overlaps"] + audit["settle_drift"] + audit["synced"] + audit["tile_conflicts"]
    if audit["actions"] == 0:
        print("FAIL: no actions were audited")
        return False
    if bad:
        print(f"FAIL: {bad} board audit problem(s)")
        return False
    print(f"OK: {audit['actions']} actions / {audit['beats']} beats, every token and item on its own tile")
    return True


def _frame_diff(a, b):
    """Fraction of pixels that differ between two screenshots (0.0 = identical frames)."""
    from PIL import Image, ImageChops
    ia, ib = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
    if ia.size != ib.size:
        return 1.0
    diff = ImageChops.difference(ia, ib).convert("L").point(lambda v: 255 if v > 8 else 0)
    return (diff.histogram()[255]) / float(ia.size[0] * ia.size[1])


def pause_check(run="pause", speed=1, timeout=240):
    """GG-T126: Pause must pause. Autoplays random battles until playback is mid-pincer (outline strokes drawn,
    chain >= 1; a CHAIN x2 is preferred for the first 90 s), taps the real "II" menu button, then screenshots twice
    ~2 s apart: no playback beat may start (beats / beatsWhilePaused), the outline, clock and frame must be
    identical, and the pause veil must be the topmost Overlay layer. Then taps Resume and checks the chain plays
    on to the end. Returns True when every check passes."""
    art = launch(run)
    call("speed", x=speed)
    t0 = time.time(); caught = None
    while time.time() - t0 < timeout and caught is None:
        call("randombattle"); time.sleep(1.0)
        call("autoplay", on=1)
        t1 = time.time()
        while time.time() - t1 < 90:
            st = call("state")
            b = st.get("battle")
            if st["screen"] != "battle" or b is None:
                break
            want = 2 if time.time() - t0 < 90 else 1
            if b["busy"] and b["chain"] >= want and b["lines"] > 0:
                caught = b
                break
            time.sleep(0.03)
    if caught is None:
        print("FAIL: never caught playback mid-pincer")
        return False
    r = call("click", name="menu"); time.sleep(0.3)
    if _tap_failed(r):
        print(f"FAIL: the pause button tap didn't land: {r}")
        return False
    s1 = call("state")["battle"]
    a = shot("paused-a")
    time.sleep(2.0)
    s2 = call("state")["battle"]
    b2 = shot("paused-b")
    diff = _frame_diff(a, b2)
    checks = {
        "paused": s1["paused"] and s2["paused"],
        "paused mid-chain": s1["busy"] and s1["chain"] >= 1 and s1["lines"] > 0,
        "no beat advanced": s1["beats"] == s2["beats"] and s2["beatsWhilePaused"] == 0,
        "outline frozen": s1["lines"] == s2["lines"],
        "clock frozen": s1["clock"] == s2["clock"] and s1["bank"] == s2["bank"],
        "veil on top": s1["pauseOnTop"] and s2["pauseOnTop"],
        "frames identical": diff == 0.0,
    }
    for _ in range(8):
        call("clicktext", text="Resume"); time.sleep(0.3)
        if not call("state")["battle"]["paused"]:
            break
    resumed = call("state")["battle"]
    checks["resumed"] = not resumed["paused"]
    end = time.time() + 30
    after = resumed
    while time.time() < end:
        st = call("state")
        if st["screen"] != "battle":
            after = None
            break
        after = st["battle"]
        # The paused event list has played out once beats moved on and either playback went idle or the bot
        # already committed its next command (the state clock only changes on a new command).
        if after["beats"] > s2["beats"] and (not after["busy"] or after["clock"] != s2["clock"]):
            break
        time.sleep(0.2)
    checks["chain finished after resume"] = after is None or (after["beats"] > s2["beats"] and (not after["busy"] or after["clock"] != s2["clock"]))
    shot("resumed")
    logs = call("logs")
    pause_errors = [l[:300] for l in logs if "[Pause]" in l or "watchdog" in l]
    checks["no pause/watchdog errors"] = not pause_errors
    print(json.dumps({"caught": {k: caught[k] for k in ("chain", "lines", "beats")}, "paused_a": {k: s1[k] for k in ("beats", "lines", "clock", "chain")},
                      "paused_b": {k: s2[k] for k in ("beats", "lines", "clock", "chain")}, "frame_diff": diff,
                      "after": after and {k: after[k] for k in ("beats", "busy", "clock")}, "errors": pause_errors, "checks": checks}, indent=1))
    print(a); print(b2)
    return all(checks.values())


def outline_frames(run="outline", speed=1, frames=48, every=3, timeout=240):
    """GG-T128: the pincer outline lives inside the board again, so it rides every camera twist/zoom/drift and
    screen shake. Autoplays random battles until a pincer's outline is drawn, then frame-records the pincer
    (camera twist, shake, cut-in cards) into artifacts/<run>/pincer/f###.png for review: the outline must hug
    its tiles in every frame and no cut-in card may cover it. Returns the frame directory, or None."""
    launch(run)
    call("speed", x=speed)
    t0 = time.time()
    while time.time() - t0 < timeout:
        call("randombattle"); time.sleep(1.0)
        call("autoplay", on=1)
        t1 = time.time()
        while time.time() - t1 < 90:
            st = call("state")
            b = st.get("battle")
            if st["screen"] != "battle" or b is None:
                break
            if b["busy"] and b["chain"] >= 1 and b["lines"] > 0:
                rec = call("record", name="pincer", frames=frames, every=every, step=1 / 30)
                print(json.dumps(rec))
                return rec.get("dir")
            time.sleep(0.03)
    print("FAIL: never caught a pincer outline")
    return None


def orb_fanout(run="orbs", charge=250, frames=270, every=3, timeout=300):
    """GG-T112: orbs minted together must fan into consecutive bank slots, not all fly to the last one. Starts a
    random battle, plays the bot's suggested moves until one forms a pincer, then pre-loads the mana meter past
    one orb (givemana charge=250, so that pincer's charge rolls 3 orbs in one AddCharge) and frame-records the
    pincer into artifacts/<run>/orbs/f###.png. Checks the battle's orbSlots telemetry: the new orbs flew to
    distinct, consecutive slots starting at the bank's count before the move. Returns True when they did."""
    import threading
    art = launch(run)
    call("speed", x=1)
    t0 = time.time()
    while time.time() - t0 < timeout:
        call("randombattle"); time.sleep(1.0)
        for _ in range(20):
            if call("state")["screen"] == "battle":
                break
            time.sleep(0.2)
        call("click", name="tutorial-skip"); time.sleep(0.3)
        for _ in range(15):
            st = call("state")
            if st["screen"] != "battle" or st.get("battle") is None:
                break
            while call("state").get("battle", {}).get("busy"):
                time.sleep(0.2)
            sug = call("suggest")
            if not (isinstance(sug, dict) and sug.get("path")):
                break
            pts = ";".join(f"{x},{y}" for x, y in ([tile(*sug["from"])] + [tile(x, y) for x, y in sug["path"]]))
            if sug.get("pincers", 0) == 0:
                call("drag", path=pts, step=0.1); time.sleep(0.6)
                continue
            gm = call("givemana", n=0, charge=charge)
            b0 = call("state")["battle"]
            bank_before = int(b0["bank"].split("/")[0]); cap = int(b0["bank"].split("/")[1])
            flown_before = len(b0["orbSlots"])
            th = threading.Thread(target=lambda: call("drag", path=pts, step=0.1)); th.start()
            t1 = time.time()
            while time.time() - t1 < 20 and not call("state")["battle"]["busy"]:
                time.sleep(0.03)
            rec = call("record", name="orbs", frames=frames, every=every, step=1 / 30, timeout=300)
            th.join()
            t1 = time.time()
            while time.time() - t1 < 60:
                b1 = call("state").get("battle")
                if b1 is None or not b1["busy"]:
                    break
                time.sleep(0.2)
            b1 = call("state").get("battle") or b1
            flown = b1["orbSlots"]
            new = flown[flown_before:] if len(flown) >= flown_before else flown
            want = list(range(bank_before, min(cap, bank_before + len(new))))
            checks = {"several orbs minted at once": len(new) >= 2,
                      "each orb its own slot": len(set(new)) == len(new),
                      "consecutive from the old count": new == want}
            print(json.dumps({"givemana": gm, "bank_before": b0["bank"], "bank_after": b1["bank"], "slots_flown": new,
                              "expected": want, "frames": rec, "checks": checks}, indent=1))
            return all(checks.values())
    print("FAIL: never found a pincer move")
    return False


def durability(run="durability"):
    """GG-T39: forces the bot's suggested hero to its weapon's last-strike durability (1) mid-battle,
    then verifies the battle card shows the cracked-weapon badge and the next drag preview (the same
    pincer-forming move the bot already found) warns the pincer will shatter it. Skips UI navigation
    (newgame/battle endpoints go straight to a real campaign-stage battle) so it isn't blocked by
    unrelated screens. Screenshots land in artifacts/<run>/."""
    launch(run, fresh=True)
    call("newgame", slot=0, gold=500)
    call("devequipall")  # a fresh roster starts bare-handed; equip Iron Swords so there's a weapon to wear down
    info = call("battle")
    for _ in range(20):
        if call("state")["screen"] == "battle":
            break
        time.sleep(0.2)
    shot("01-battle")
    # The first-battle tutorial periodically overwrites the banner with its own "Hint: ..." text while
    # idle, racing the pincer/shatter banner this check reads — skip it the same way uicheck.py does.
    call("click", name="tutorial-skip"); time.sleep(0.4)
    # A fresh battle's opening move rarely pincers (enemies haven't closed in yet) — play the bot's
    # own suggested moves until one does, same as a player just advancing the fight.
    sug = call("suggest")
    tries = 0
    while isinstance(sug, dict) and sug.get("path") and sug.get("pincers", 0) == 0 and tries < 15 and call("state")["screen"] == "battle":
        pts = ";".join(f"{x},{y}" for x, y in ([tile(*sug["from"])] + [tile(x, y) for x, y in sug["path"]]))
        call("drag", path=pts, step=0.1)
        time.sleep(0.6)
        sug = call("suggest")
        tries += 1
    if not (isinstance(sug, dict) and sug.get("path") and sug.get("pincers", 0) > 0):
        print(json.dumps({"stage": info, "error": "bot found no pincer move to test", "tries": tries, "suggest": sug}, indent=1))
        return
    hero_id = sug["hero"]
    forced = call("devweapon", unit=hero_id, durability=1)
    crack = call("element", name=f"weapon-crack-{hero_id}")
    pts = ";".join(f"{x},{y}" for x, y in ([tile(*sug["from"])] + [tile(x, y) for x, y in sug["path"]]))
    call("drag", path=pts, step=0.1, hold=1)
    time.sleep(0.3)
    banner = call("banner")["text"] or ""
    shot("02-drag-shatter-warning")
    call("release"); time.sleep(0.5)
    shot("03-after-drop")
    logs = call("logs")
    print(json.dumps({
        "stage": info, "forced_durability_1": forced,
        "crack_badge_visible": crack.get("visible"), "crack_badge": crack,
        "banner": banner, "banner_warns_shatter": "shatter" in banner.lower(),
        "errors": [l[:300] for l in logs if l.startswith(("Error", "Exception"))],
    }, indent=1))


def bestiary_drops(run="bestiary"):
    """Plays Green Valley I at high speed (its wave 3 is an all-frog family per the house wave
    convention), then opens the Bestiary and taps the now-Seen Frog entry to screenshot its Drops
    line (GG-T43: every enemy family's signature material, listed once Seen — not just Scanned)."""
    launch(run)
    call("newgame", slot=0)
    time.sleep(1)
    call("speed", x=8)
    call("battle", stage="greenvalley-01")
    for _ in range(20):
        if call("state")["screen"] == "battle":
            break
        time.sleep(0.2)
    call("autoplay", on=1)
    t0 = time.time()
    while time.time() - t0 < 300:
        time.sleep(1.0)
        if call("state")["screen"] != "battle":
            break
    else:
        raise RuntimeError("battle did not finish within 300s even at speed x8")
    time.sleep(1.2)
    call("goto", screen="bestiary")
    time.sleep(0.9)
    # frog00 is always among the first two rows of the (alphabetically-sorted) grid and greenvalley-01's
    # wave 3 always fights it, so its thumb is both Seen and on-screen without needing to scroll.
    r = call("click", name="beast-frog00")
    time.sleep(0.6)
    files = [shot("bestiary-frog-drops")]
    print(json.dumps({"click": r, "layout": call("layout").get("problems")}, indent=1))
    print("\n".join(files))


def testmode(run="testmode", cycles=4, timeout_per_cycle=600):
    """GG-T93: launches the player with -testmode (bot-only: throwaway save, no story/crawl/tutorial) and
    lets it drive itself through `cycles` full launch → battle → results → camp loops. Screenshots each
    cycle's battle, results and camp (the Hub after CampRoutine ran), collects the one-line-per-cycle
    "[TestMode] cycle N | ..." log into artifacts/<run>/testmode.log, and checks the evidence: every cycle
    logged, a battle/results/hub screen seen per cycle, the party level never falling, an ability learned
    in any run of 6+ cycles (GG-T100), and no errors."""
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # camp lines carry ★ (masterwork) glyphs
    art = launch(run, extra=("-testmode",), fresh=True)
    lines, errors, seen = [], [], []
    shots_for = {}  # (cycle, screen) -> file
    last_screen = None
    t0 = time.time()
    done = 0
    while done < cycles and time.time() - t0 < timeout_per_cycle * cycles:
        time.sleep(0.5)
        try:
            st = call("state", timeout=10)
        except Exception as e:
            errors.append(f"state failed: {e}")
            if not alive():
                break
            continue
        tm = st.get("testmode") or {}
        done = tm.get("cycles", 0)
        scr = st["screen"]
        cyc = done + 1
        if scr != last_screen:
            seen.append((cyc, scr))
            last_screen = scr
        key = (cyc, scr)
        if scr in ("postbattle", "levelup") and key not in shots_for:
            shots_for[key] = shot(f"c{cyc:02d}-{scr}")
        if scr == "battle" and key not in shots_for and st.get("battle", {}).get("clock", 0) > 30:
            shots_for[key] = shot(f"c{cyc:02d}-battle")
        logs = call("logs", clear=1)
        lines += [l.split(": ", 1)[1] for l in logs if "[TestMode]" in l]
        errors += [l[:300] for l in logs if l.startswith(("Error", "Exception")) and "first mouse" not in l]
        if done >= 1 and scr == "hub" and (done, "hub") not in shots_for:
            shots_for[(done, "hub")] = shot(f"c{done:02d}-camp")
    cycle_lines = [l for l in lines if l.startswith("[TestMode] cycle") and " launch " not in l]
    with open(os.path.join(art, "testmode.log"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    levels = []
    for l in cycle_lines:
        part = l.split("| party Lv ", 1)[1].split(" ", 1)[0]
        levels.append(tuple(int(x) for x in part.split("->")))
    per_cycle = {n: {s for c, s in seen if c == n} for n in range(1, done + 1)}
    checks = {
        "cycles_logged": len(cycle_lines) >= cycles,
        "battle_results_camp_each_cycle": all({"battle", "postbattle", "hub"} <= per_cycle.get(n, set()) for n in range(1, cycles + 1)),
        "party_level_never_falls": all(b >= a for a, b in levels) and all(levels[i + 1][0] >= levels[i][1] for i in range(len(levels) - 1)),
        "camp_did_something": any(": nothing to do" not in l for l in cycle_lines),
        # GG-T100: "learn abilities" is part of what Test Mode must prove — a run of 6+ cycles that never
        # learned one fails (CampRoutine logs "<Hero> learned <Ability> (<price>g)"; TestLoop.CyclesThatMustLearnAnAbility).
        "learned_an_ability": len(cycle_lines) < 6 or any(" learned " in l for l in cycle_lines),
        "no_errors": not errors,
    }
    try:
        call("quit", timeout=3)
    except Exception:
        pass
    print("\n".join(cycle_lines))
    print(json.dumps({"checks": checks, "errors": errors[:10], "screens": seen[:60]}, indent=1))
    print("\n".join(sorted(shots_for.values())))
    return all(checks.values())

def _tap_failed(r):
    """A tap/click reply that says it didn't land: the element isn't there yet, the point hit nothing (or a
    screen-transition fade), or the events never reached the panel (GG-T121: replies are honest now)."""
    return isinstance(r, dict) and (r.get("ok") is False or "error" in r)


def _tap_until(tap, done, tries=8, wait=4.0):
    """Taps, then waits up to `wait` seconds for `done()`. Since GG-T121 automation taps go straight into the
    UI Toolkit panel (no OS focus involved) and say when they miss, a tap is only repeated when its reply
    reports it didn't land (e.g. the target screen is still fading in) — never blindly. Returns how many taps
    it took (0 = never got there)."""
    for i in range(tries):
        if _tap_failed(tap()):
            time.sleep(0.3)
            continue
        end = time.time() + wait
        while True:
            if done():
                return i + 1
            if time.time() >= end:
                return 0
            time.sleep(0.1)
    return 0


def crucible(run="crucible", leave_after=3, speed=4, timeout=600):
    """GG-T95: the Crucible end to end through the real UI. Camp -> the Crucible row -> Enter -> the battle on
    Auto until `leave_after` waves are cleared (or the party wipes first) -> pause menu "Leave the Crucible" ->
    level-ups -> results -> Continue -> camp, checking the haul really landed in the save (gold, items, XP),
    the party recovered to full and the camp row now shows the best record. Screenshots in artifacts/<run>/."""
    launch(run, fresh=True)
    call("newgame", slot=0)
    time.sleep(1.5)
    call("cues", clear=1)
    before = call("state")["save"]
    out = {"before": {k: before[k] for k in ("Gold", "inventory", "xp", "crucible", "HighestClearedStage")}, "taps": {}}
    screen = lambda: call("state")["screen"]
    texts = lambda: [t for t in call("texts") if isinstance(t, str)]
    shot("01-camp")
    out["camp_row_before"] = [t for t in texts() if t in ("Crucible", "Endless waves") or t.startswith("Best:")]
    out["taps"]["camp->crucible"] = _tap_until(lambda: call("clicktext", text="Crucible"), lambda: screen() == "crucible")
    shot("02-crucible-screen")
    out["taps"]["enter"] = _tap_until(lambda: call("click", name="crucible-enter"), lambda: screen() == "battle", wait=6.0)
    call("speed", x=speed)
    call("autoplay", on=1)
    t0 = time.time(); ended_by = "timeout"; last_wave = -1
    while time.time() - t0 < timeout:
        time.sleep(1.0)
        st = call("state")
        if st["screen"] != "battle":
            ended_by = "wipe"
            break
        b = st["battle"]
        if b["wave"] != last_wave:
            last_wave = b["wave"]
            time.sleep(0.4)  # let the wave banner land before the shot
            shot(f"03-battle-wave{b['wave'] + 1:02d}")
        if b["wave"] >= leave_after:
            # Leave the way a player would: Auto off, let the current action finish, pause menu, confirm.
            call("autoplay", on=0)
            for _ in range(80):
                if not call("state").get("battle", {}).get("busy", True):
                    break
                time.sleep(0.25)
            out["taps"]["pause"] = _tap_until(lambda: call("click", name="menu"), lambda: any("Leave the Crucible" in t for t in texts()))
            shot("04-pause-menu")
            out["taps"]["leave-row"] = _tap_until(lambda: call("clicktext", text="Leave the Crucible"), lambda: any(t.startswith("Leave the Crucible after") for t in texts()))
            shot("05-leave-confirm")
            out["taps"]["leave-confirm"] = _tap_until(lambda: call("clicktext", text="Leave"), lambda: screen() != "battle", wait=6.0)
            ended_by = "left"
            break
    out["ended_by"] = ended_by
    out["waves_reached"] = last_wave + 1
    time.sleep(1.0)
    st = call("state")
    if st["screen"] == "levelup":
        shot("06-levelup")
    for _ in range(8):  # one "Skip" per hero that leveled up
        if screen() != "levelup":
            break
        _tap_until(lambda: call("clicktext", text="Skip"), lambda: True, tries=1, wait=0.8)
    time.sleep(1.5)
    out["results_screen"] = screen()
    shot("07-results")
    out["results_texts"] = texts()[:40]
    after = call("state")["save"]
    out["taps"]["continue"] = _tap_until(lambda: call("click", name="continue"), lambda: screen() == "hub", wait=6.0)
    out["back_at"] = screen()
    shot("08-camp-after")
    out["after"] = {k: after[k] for k in ("Gold", "inventory", "xp", "hp", "crucible", "HighestClearedStage", "expedition")}
    gained = {k: v - before["inventory"].get(k, 0) for k, v in after["inventory"].items() if v != before["inventory"].get(k, 0)}
    out["checks"] = {
        "gold_gained": after["Gold"] - before["Gold"],
        "items_changed": gained,
        "xp_gained": {k: after["xp"][k] - before["xp"].get(k, 0) for k in after["xp"]},
        "party_full_hp": all(after["hp"][k] == 0 for k in before["party"]),
        "best_recorded": after["crucible"]["Best"],
        "campaign_untouched": after["HighestClearedStage"] == before["HighestClearedStage"] and after["expedition"] is None,
        "camp_row_after": [t for t in texts() if t == "Crucible" or t.startswith("Best:")],
        "crucible_cues": sorted({c for c in call("cues") if c.startswith("crucible")}),
    }
    # GG-T121: taps no longer depend on window focus, so 0 in "taps" is a real game-side problem.
    logs = call("logs")
    out["errors"] = [l[:300] for l in logs if l.startswith(("Error", "Exception"))]
    print(json.dumps(out, indent=1))


def _report_death(art, returncode, n, extra_note=""):
    """GG-T21: the player process can vanish mid-battle with no exception in Player.log and no
    Windows "Application Error" event — the only report from that incident was a human skimming
    Task Manager afterward. This captures the OS-level evidence immediately, while it's still fresh:
    CrashDumps (WER only writes these for crashes it catches, which this one evidently wasn't) and
    the Application event log window around the death, so a recurrence leaves an actual trail."""
    report = os.path.join(art, "crash-report.txt")
    lines = [f"process died after {n} clips (returncode={returncode}) at {time.strftime('%Y-%m-%d %H:%M:%S')}", extra_note]
    dumps_dir = os.path.join(os.environ.get("LOCALAPPDATA", ""), "CrashDumps")
    try:
        names = [f for f in os.listdir(dumps_dir) if "GridGameGaiden" in f]
        lines.append(f"CrashDumps matching GridGameGaiden: {names or 'none'}")
    except Exception as e:
        lines.append(f"could not list CrashDumps: {e}")
    try:
        ps = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-15)} "
             "-ErrorAction SilentlyContinue | Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message "
             "| Format-List | Out-String -Width 200"],
            capture_output=True, text=True, timeout=30)
        lines.append("Application event log, last 15 minutes:")
        lines.append(ps.stdout.strip() or "(no matching events)")
        if ps.stderr.strip():
            lines.append(f"(stderr: {ps.stderr.strip()})")
    except Exception as e:
        lines.append(f"could not query event log: {e}")
    log_path = os.path.join(art, "player.log")
    if os.path.exists(log_path):
        with open(log_path, "r", errors="replace") as f:
            tail = f.readlines()[-40:]
        lines.append("Player.log tail:")
        lines.append("".join(tail))
    text = "\n".join(lines)
    with open(report, "w") as f:
        f.write(text)
    print(text)
    print(f"\nfull report: {report}")


def stress(run="stress", hours=4.0, speed=3, timeout=240):
    """GG-T21 soak test: launches once and keeps generating random battle clips (same approach as
    battle_clips) for `hours` of wall-clock time in one long-lived process, to try to reproduce the
    process disappearing mid-battle during an earlier ~1h unattended run with no diagnostic trail.
    Unlike battle_clips/campaign, this polls the OS process directly (Popen.poll()) on every
    iteration instead of only noticing trouble when an HTTP call fails or times out, since the
    original incident was the whole process vanishing, not a hang. On a process death it writes
    artifacts/<run>/crash-report.txt with whatever OS-level evidence it can still gather."""
    art = os.path.join(ROOT, "artifacts", run)
    saves = os.path.join(art, "saves")
    os.makedirs(saves, exist_ok=True)
    args = [EXE, "-automation", str(PORT), "-artifacts", art, "-saveRoot", saves,
            "-screen-width", "585", "-screen-height", "1266", "-screen-fullscreen", "0",
            "-logFile", os.path.join(art, "player.log")]
    proc = subprocess.Popen(args, cwd=ROOT)
    for _ in range(60):
        if alive():
            break
        time.sleep(0.5)
    else:
        raise RuntimeError("player did not start")

    class Died(Exception):
        pass

    def checked_call(endpoint, **params):
        # Every call() in this loop goes through here so a process death is always caught and
        # reported, instead of the exception from a refused connection escaping uncaught (as it
        # did the first time this ran: the process died between a poll() check and the next call()
        # one line later, and only the calls adjacent to an explicit poll() check were guarded).
        if proc.poll() is not None:
            raise Died()
        try:
            return call(endpoint, **params)
        except Exception as e:
            if proc.poll() is not None:
                raise Died(str(e))
            raise

    checked_call("speed", x=speed)
    deadline = time.time() + hours * 3600
    clips = []
    n = 0
    try:
        while time.time() < deadline:
            info = checked_call("randombattle")
            if not isinstance(info, dict) or not info.get("ok"):
                print(f"clip{n}: randombattle failed: {info}")
                break
            for _ in range(20):
                if checked_call("state")["screen"] == "battle":
                    break
                time.sleep(0.2)
            checked_call("autoplay", on=1)
            t0 = time.time(); last_clock = -1; stuck = 0; result = "TIMEOUT"
            while time.time() - t0 < timeout:
                time.sleep(1.0)
                st = checked_call("state")
                if st["screen"] != "battle":
                    result = st["screen"]
                    break
                b = st["battle"]
                stuck = stuck + 1 if b["clock"] == last_clock and not b["busy"] else 0
                last_clock = b["clock"]
                if stuck > 12:
                    result = "STALLED"
                    break
            clips.append({**info, "result": result})
            n += 1
            if n % 10 == 0:
                elapsed = time.time() - (deadline - hours * 3600)
                print(f"stress: {n} clips done, {elapsed/3600:.2f}h elapsed, process alive")
    except Died as e:
        _report_death(art, proc.returncode, n, extra_note=str(e))
        return clips
    logs = call("logs")
    errors = [l[:300] for l in logs if l.startswith(("Error", "Exception")) and "first mouse" not in l]
    print(json.dumps({"clips_completed": n, "hours": hours, "errors": errors}, indent=1))
    return clips


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "smoke"
    if cmd == "smoke":
        smoke(sys.argv[2] if len(sys.argv) > 2 else "smoke")
    elif cmd == "campaign":
        campaign(stages=int(sys.argv[2]) if len(sys.argv) > 2 else 6)
    elif cmd == "sweep":
        sweep(sys.argv[2] if len(sys.argv) > 2 else "sweep", sys.argv[3] if len(sys.argv) > 3 else None)
    elif cmd == "aspects":
        sys.exit(0 if aspects() else 1)
    elif cmd == "camptips":
        sys.exit(0 if camptips() else 1)
    elif cmd == "smithrepair":
        sys.exit(0 if smithrepair() else 1)
    elif cmd == "firstbrew":
        sys.exit(0 if firstbrew() else 1)
    elif cmd == "firstforge":
        sys.exit(0 if firstforge() else 1)
    elif cmd == "firstarmor":
        sys.exit(0 if firstarmor() else 1)
    elif cmd == "fullloop":
        sys.exit(0 if fullloop(battles=int(sys.argv[2]) if len(sys.argv) > 2 else 4) else 1)
    elif cmd == "clips":
        battle_clips(count=int(sys.argv[2]) if len(sys.argv) > 2 else 10)
    elif cmd == "boardaudit":
        sys.exit(0 if board_audit(count=int(sys.argv[2]) if len(sys.argv) > 2 else 12) else 1)
    elif cmd == "durability":
        durability()
    elif cmd == "outline":
        sys.exit(0 if outline_frames() else 1)
    elif cmd == "orbs":
        sys.exit(0 if orb_fanout() else 1)
    elif cmd == "pause":
        sys.exit(0 if pause_check() else 1)
    elif cmd == "crucible":
        crucible(sys.argv[3] if len(sys.argv) > 3 else "crucible", leave_after=int(sys.argv[2]) if len(sys.argv) > 2 else 3)
    elif cmd == "bestiary":
        bestiary_drops(sys.argv[2] if len(sys.argv) > 2 else "bestiary")
    elif cmd == "stress":
        stress(hours=float(sys.argv[2]) if len(sys.argv) > 2 else 4.0)
    elif cmd == "recruits":
        sys.exit(0 if story_recruits() else 1)
    elif cmd == "testmode":
        sys.exit(0 if testmode(cycles=int(sys.argv[2]) if len(sys.argv) > 2 else 4) else 1)
    elif cmd == "shot":
        print(shot(sys.argv[2]))
    elif cmd == "call":
        kv = dict(a.split("=", 1) for a in sys.argv[3:])
        print(json.dumps(call(sys.argv[2], **kv), indent=1))
