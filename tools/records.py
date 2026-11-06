"""
Battle-record analysis (bible §22.6). Reads the JSON battle records the game and the bot soak write and prints the
balance picture: where battles are lost, what each wave costs, what each role contributes, how mana flows.

    python tools/records.py [dir-or-files…]        # default: artifacts/records
    python tools/records.py --source bot:greedy    # only one kind of player

Record schema v1 (one document per battle): schema, source, stage, waves, seed, party[], opening{state,events},
turns[{index, wave, before{state}, command, accepted, events[], after{…}}], outcome{phase, waveReached, actions, …}.
"""
import collections
import glob
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(paths, source=None):
    files = []
    for p in paths:
        files += sorted(glob.glob(os.path.join(p, "*.json"))) if os.path.isdir(p) else [p]
    recs = []
    for f in files:
        with open(f, encoding="utf-8") as fh:
            r = json.load(fh)
        if source and r.get("source") != source:
            continue
        recs.append(r)
    return recs


def analyse(recs):
    by_source = collections.defaultdict(list)
    for r in recs:
        by_source[r["source"]].append(r)
    for source, rs in by_source.items():
        print(f"\n=== {source}: {len(rs)} battles · stage {rs[0]['stage']} · {rs[0]['waves']} waves ===")
        wins = sum(1 for r in rs if r["outcome"]["phase"] == "Victory")
        reached = collections.Counter(r["outcome"]["waveReached"] for r in rs if r["outcome"]["phase"] != "Victory")
        print(f"victories {wins}/{len(rs)} · avg actions {sum(r['outcome']['actions'] for r in rs) / len(rs):.0f} · lost at waves {dict(sorted(reached.items()))}")

        wave = collections.defaultdict(lambda: collections.Counter())
        roles = collections.defaultdict(lambda: collections.Counter())
        brains = collections.Counter()
        abilities = collections.Counter()
        for r in rs:
            role_of = {h["id"]: h["role"] for h in r["party"]}
            for t in r["turns"]:
                w = (t.get("wave") or 0) + 1
                units = {u["id"]: u for u in t["before"]["units"]}
                c = wave[w]
                c["turns"] += 1
                if t["command"]["type"] == "use":
                    hero = units.get(t["command"]["hero"])
                    slot = hero["bar"][t["command"]["slot"]] if hero and t["command"]["slot"] < len(hero.get("bar", [])) else None
                    name = (slot or {}).get("ability") or (slot or {}).get("item") or "?"
                    abilities[(role_of.get(t["command"]["hero"], "?"), name)] += 1
                    if (slot or {}).get("item"):
                        c["potions"] += 1
                if t["command"]["type"] == "guard":
                    c["guards"] += 1
                for e in t["events"]:
                    ty = e["type"]
                    if ty == "Damaged":
                        tgt, src = units.get(e["TargetId"]), units.get(e["SourceId"])
                        if tgt and tgt["team"] == "Hero":
                            c["heroDmgTaken"] += e["Amount"]
                            if src and src["team"] == "Enemy":
                                brains[src.get("brain", "?")] += e["Amount"]
                        elif src and src["team"] == "Hero":
                            roles[role_of.get(src["id"], "?")]["damage"] += e["Amount"]
                    elif ty == "Healed":
                        src = units.get(e["SourceId"])
                        if src and src["team"] == "Hero":
                            roles[role_of.get(src["id"], "?")]["healing"] += e["Amount"]
                    elif ty == "Died":
                        c["heroDeaths" if e["Team"] == "Hero" else "kills"] += 1
                    elif ty == "ManaCharged":
                        c["charge"] += e["Amount"]
                    elif ty == "OrbMinted" and e.get("Landed"):
                        c["orbs"] += 1
                    elif ty == "OrbsSpent":
                        c["orbsSpent"] += e["Count"]
                    elif ty == "PincerFormed" and e["Team"] == "Hero":
                        c["pincers"] += 1
                        c["chains"] += 1 if e["ChainIndex"] > 0 else 0
                    elif ty == "PickupCollected":
                        c["pickups"] += 1
                    elif ty == "BuffApplied" and e["BuffId"] == "taunting":
                        c["taunts"] += 1
        n = len(rs)
        print("wave  turns  dmgTaken  deaths  potions  pincers chains  charge orbs spent  pickups taunts guards")
        for w in sorted(wave):
            c = wave[w]
            print(f"{w:4} {c['turns'] / n:6.1f} {c['heroDmgTaken'] / n:9.0f} {c['heroDeaths'] / n:7.2f} {c['potions'] / n:8.2f} "
                  f"{c['pincers'] / n:8.1f} {c['chains'] / n:6.1f} {c['charge'] / n:7.0f} {c['orbs'] / n:4.1f} {c['orbsSpent'] / n:5.1f} "
                  f"{c['pickups'] / n:8.2f} {c['taunts'] / n:6.2f} {c['guards'] / n:6.1f}")
        print("role contribution per battle:", {k: {kk: round(vv / n) for kk, vv in v.items()} for k, v in roles.items()})
        print("damage taken by enemy brain per battle:", {k: round(v / n) for k, v in brains.most_common()})
        print("most used:", [(f"{r}:{a}", round(k / n, 1)) for (r, a), k in abilities.most_common(12)])


# Events that end an ability's attribution window: the next action, turn, pincer or enemy swing.
BOUNDARY = {"AbilityResolved", "ItemUsed", "ActionBegan", "TurnStarted", "PincerFormed", "Attack", "WaveStarted", "BattleEnded"}


def ability_usage(recs, known=None):
    """GG-T246: per ability, what each resolution actually did. Effects are attributed from an AbilityResolved (or the
    ItemUsed before it) to the next boundary event, so a cast-time spell is credited when it lands, not when queued."""
    stats = collections.defaultdict(collections.Counter)
    mana = collections.Counter()
    buffs = known_buffs()
    for r in recs:
        team = {}
        enemy_turn, hero_action = action_values(r)
        active = {}       # (unitId, buffId) -> the ability whose resolution applied it (GG-T264 utility credit)
        ticked = None     # (unitId, buffId) of the BuffTicked just before a tick's Damaged/Healed
        for t in r["turns"]:
            for u in t["before"]["units"]:
                team[u["id"]] = u["team"]
            if t["command"]["type"] == "use":
                units = {u["id"]: u for u in t["before"]["units"]}
                hero = units.get(t["command"]["hero"])
                bar = (hero or {}).get("bar", [])
                slot = bar[t["command"]["slot"]] if hero and t["command"]["slot"] < len(bar) else None
                key = ("item:" + slot["item"]) if (slot or {}).get("item") else (slot or {}).get("ability")
                if key:
                    stats[key]["chosen"] += 1
            if t["command"]["type"] == "move":
                mana["moves"] += 1
            cur, caster, item = None, None, None
            for e in t["events"]:
                ty = e["type"]
                credit_utility(e, ty, team, active, buffs, stats, enemy_turn, hero_action, ticked)
                ticked = (e["UnitId"], e["BuffId"]) if ty == "BuffTicked" else None
                if ty == "OrbMinted":
                    mana["minted"] += 1
                    mana["landed" if e.get("Landed") else "overflowed"] += 1
                elif ty == "OrbsSpent":
                    mana["spent"] += e.get("Count", 0)
                if ty in BOUNDARY:
                    if ty == "ItemUsed":
                        cur, caster, item = None, e["UserId"], e["ItemId"]
                        continue
                    if ty == "AbilityResolved" and team.get(e["CasterId"]) == "Hero":
                        cur = ("item:" + item) if item and e["CasterId"] == caster else e["AbilityId"]
                        caster, item = e["CasterId"], None
                        stats[cur]["uses"] += 1
                        stats[cur]["targets"] += len(e.get("Targets", []))
                        continue
                    cur, caster, item = None, None, None
                    continue
                if cur is None:
                    continue
                c = stats[cur]
                if ty == "Damaged" and e["SourceId"] == caster and team.get(e["TargetId"]) == "Enemy":
                    c["damage"] += e["Amount"]
                elif ty == "Healed" and team.get(e["TargetId"]) == "Hero":
                    c["healing"] += e["Amount"]
                elif ty == "Died" and e["Team"] == "Enemy":
                    c["kills"] += 1
                elif ty == "BuffApplied":
                    c["buffsOnFoes" if team.get(e["UnitId"]) == "Enemy" else "buffsOnAllies"] += 1
                    active[(e["UnitId"], e["BuffId"])] = cur
                elif ty == "BuffRemoved" and team.get(e["UnitId"]) == "Hero":
                    c["cleansed"] += 1
                elif ty == "PushedBack" and team.get(e["UnitId"]) == "Enemy":
                    c["delayed"] += 1
                elif ty == "Hastened" and team.get(e["UnitId"]) == "Hero":
                    c["hastened"] += 1
                elif ty in ("Displaced", "Teleported"):
                    c["moved"] += 1
                elif ty == "TrapPlaced":
                    c["traps"] += 1
                elif ty == "Revived":
                    c["revives"] += 1
                elif ty == "OrbMinted" and e.get("Landed"):
                    c["orbs"] += 1
    return stats, mana


def known_buffs():
    path = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content", "buffs.json")
    with open(path, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    items = data if isinstance(data, list) else next(iter(data.values()))
    return {b["Id"]: b for b in items}


def action_values(r):
    """What one enemy turn and one hero action are worth in this battle, in HP: enemy damage to heroes per enemy
    turn started, and hero damage plus healing per player command. The exchange rate for utility credit."""
    team, enemy_dmg, enemy_turns, hero_val = {}, 0, 0, 0
    for t in r["turns"]:
        for u in t["before"]["units"]:
            team[u["id"]] = u["team"]
        for e in t["events"]:
            ty = e["type"]
            if ty == "TurnStarted" and team.get(e["UnitId"]) == "Enemy":
                enemy_turns += 1
            elif ty == "Damaged":
                if team.get(e["SourceId"]) == "Enemy" and team.get(e["TargetId"]) == "Hero":
                    enemy_dmg += e["Amount"]
                elif team.get(e["SourceId"]) == "Hero" and team.get(e["TargetId"]) == "Enemy":
                    hero_val += e["Amount"]
            elif ty == "Healed" and team.get(e["TargetId"]) == "Hero":
                hero_val += e["Amount"]
    return enemy_dmg / max(1, enemy_turns), hero_val / max(1, len(r["turns"]))


def credit_utility(e, ty, team, active, buffs, stats, enemy_turn, hero_action, ticked):
    """GG-T264: what a buff or debuff actually did after it landed, credited (in HP) to the ability that applied it.
    Damage-up: the extra share of the buffed ally's damage. Guard/taunt: damage the multiplier prevented, and a taunt
    is also credited every hit its tank absorbed (redirected away from squishier allies). Damage/heal ticks
    at face value. Frozen/sleep: each skipped enemy turn = one enemy turn's damage. Slow: the turns it took away.
    Blind: the expected misses. Silence: half an enemy turn per turn it held. Haste/push-back: the timeline shift
    times one action's worth."""
    def on(unit):
        return [(b, owner) for (u, b), owner in active.items() if u == unit and owner]

    def give(owner, kind, v):
        if owner and v > 0:
            stats[owner]["utility"] += v
            stats[owner][kind] += v

    if ty == "Damaged":
        src, tgt, amt = e["SourceId"], e["TargetId"], e["Amount"]
        if ticked and ticked[0] == tgt and buffs.get(ticked[1], {}).get("DamagePerTick"):
            give(active.get(ticked), "u_ticks", amt)
            return
        if team.get(src) == "Hero" and team.get(tgt) == "Enemy":
            for b, owner in on(src):
                m = buffs.get(b, {}).get("OutgoingDamageMultiplier") or 1.0
                if m > 1:
                    give(owner, "u_dmgUp", amt * (1 - 1 / m))
        if team.get(tgt) == "Hero" and team.get(src) == "Enemy":
            for b, owner in on(tgt):
                d = buffs.get(b, {})
                m = d.get("IncomingDamageMultiplier") or 1.0
                if m < 1:
                    give(owner, "u_prevented", amt * (1 / m - 1))
                if d.get("Taunts"):
                    give(owner, "u_redirected", amt)
    elif ty == "Healed":
        if ticked and ticked[0] == e["TargetId"] and buffs.get(ticked[1], {}).get("HealPerTick"):
            give(active.get(ticked), "u_ticks", e["Amount"])
    elif ty == "TurnSkipped" and team.get(e["UnitId"]) == "Enemy":
        for b, owner in on(e["UnitId"]):
            d = buffs.get(b, {})
            if d.get("Immobile") or d.get("HaltsTimeline"):
                give(owner, "u_skipped", enemy_turn)
                break
    elif ty == "TurnStarted" and team.get(e["UnitId"]) == "Enemy":
        for b, owner in on(e["UnitId"]):
            d = buffs.get(b, {})
            m = d.get("TimelineSpeedMultiplier") or 1.0
            if m < 1:
                give(owner, "u_slowed", enemy_turn * (1 / m - 1))
            if d.get("BlocksSpells"):
                give(owner, "u_silenced", enemy_turn * 0.5)
    elif ty == "Attack" and team.get(e["AttackerId"]) == "Enemy":
        for b, owner in on(e["AttackerId"]):
            acc = buffs.get(b, {}).get("AccuracyMultiplier") or 1.0
            if acc < 1:
                give(owner, "u_blinded", enemy_turn * (1 - acc))
    elif ty == "BuffRemoved":
        active.pop((e["UnitId"], e["BuffId"]), None)
    elif ty == "Died":
        for k in [k for k in active if k[0] == e.get("UnitId")]:
            del active[k]


def impact(c):
    """One usefulness number per resolution, in HP: damage and healing at face value, a kill ≈ 15, a revive ≈ 40,
    plus (GG-T264) the measured utility of every buff/debuff it applied (credit_utility) and its tempo shifts
    (haste/push-back ≈ 6 each, the timeline equivalent of a short delay); repositioning, traps, cleanses and orbs
    keep a flat 6 each because the records can't follow their downstream effect."""
    u = max(1, c["uses"])
    v = (c["damage"] + c["healing"] + 15 * c["kills"] + 40 * c["revives"] + c["utility"]
         + 6 * (c["cleansed"] + c["delayed"] + c["hastened"] + c["moved"] + c["traps"] + c["orbs"]))
    return v / u


def print_abilities(recs, known):
    stats, mana = ability_usage(recs)
    n = len(recs)
    wins = sum(1 for r in recs if r["outcome"]["phase"] == "Victory")
    print(f"### {n} battles - {wins} victories - {sum(c['uses'] for c in stats.values())} hero ability resolutions\n")
    if mana["minted"]:
        print(f"Mana: {mana['landed'] / n:.1f} orbs banked / battle, {mana['overflowed'] / n:.1f} overflowed (bank full), "
              f"{mana['spent'] / n:.1f} spent; {100 * mana['overflowed'] / mana['minted']:.0f}% of minted orbs wasted.\n")
    print("| ability | kind | cost | uses | /battle | dmg/use | heal/use | kills | status | tempo/move | impact/use |")
    print("|---|---|---|---|---|---|---|---|---|---|---|")
    rows = sorted(stats.items(), key=lambda kv: -impact(kv[1]))
    for key, c in rows:
        if not c["uses"]:
            continue
        meta = known.get(key.split("item:")[-1], {}) if key.startswith("item:") else known.get(key, {})
        u = c["uses"]
        print(f"| {key} | {meta.get('Kind', 'Item' if key.startswith('item:') else '?')} | {meta.get('Cost', 0)} | {u} | {u / n:.2f} | "
              f"{c['damage'] / u:.0f} | {c['healing'] / u:.0f} | {c['kills']} | {c['buffsOnFoes'] + c['buffsOnAllies'] + c['cleansed']} | "
              f"{c['delayed'] + c['hastened'] + c['moved'] + c['traps']} | {impact(c):.0f} |")
    return stats


def known_abilities():
    path = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content", "abilities.json")
    with open(path, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    items = data if isinstance(data, list) else data.get("Abilities", data.get("abilities", []))
    return {a["Id"]: a for a in items}


def barred_abilities(recs):
    """Every ability that sat on some hero's bar in these battles (so 'never used' means 'had the chance')."""
    out = set()
    for r in recs:
        for h in r["party"]:
            for b in h.get("bar", []):
                if isinstance(b, dict) and b.get("ability"):
                    out.add(b["ability"])
                elif isinstance(b, str) and not b.startswith("item:"):
                    out.add(b)
    return out


def main():
    args = sys.argv[1:]
    source = None
    if "--source" in args:
        i = args.index("--source"); source = args[i + 1]; args = args[:i] + args[i + 2:]
    abilities = "--abilities" in args
    args = [a for a in args if a != "--abilities"]
    paths = args or [os.path.join(ROOT, "artifacts", "records")]
    recs = load(paths, source)
    if not recs:
        print("no records found in", paths)
        return
    if abilities:
        known = known_abilities()
        stats = print_abilities(recs, known)
        barred = barred_abilities(recs)
        never = sorted(a for a in barred if not stats[a]["uses"])
        print(f"\n**On a bar but never resolved ({len(never)}):** " + (", ".join(never) or "none"))
        weak = [k for k, c in stats.items() if c["uses"] >= 3 and impact(c) < 8]
        print(f"\n**Weak (>=3 uses, impact/use < 8):** " + (", ".join(sorted(weak)) or "none"))
        return
    analyse(recs)


if __name__ == "__main__":
    main()
