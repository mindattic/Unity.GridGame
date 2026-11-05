"""
Original sound effects, synthesized from scratch (numpy) so every SFX in the repo is MindAttic's own
work — no third-party pack licensing. Writes OGG files into Assets/Gaiden/Resources/Audio/Sfx/.

    python tools/make_sfx.py                       # every cue
    python tools/make_sfx.py crucible_enter ...    # only the named cues (the rest are still synthesized, so the
                                                   # shared RNG stream and every other file stay byte-identical)
"""
import os
import subprocess
import sys
import tempfile
import wave

import numpy as np
from scipy.signal import butter, lfilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import loudness  # noqa: E402

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Audio", "Sfx")
rng = np.random.default_rng(7)
ONLY = set(sys.argv[1:])


def t(sec):
    return np.arange(int(SR * sec)) / SR


def env(n, attack=0.005, decay=0.2, curve=4.0):
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    d = np.exp(-curve * np.maximum(0, x - attack) / max(decay, 1e-4))
    return a * d


def sine(f, sec, phase=0):
    f = np.broadcast_to(f, t(sec).shape) if np.ndim(f) else np.full(t(sec).shape, f)
    return np.sin(2 * np.pi * np.cumsum(f) / SR + phase)


def saw(f, sec):
    ph = np.cumsum(np.full(t(sec).shape, f) if not np.ndim(f) else f) / SR
    return 2 * (ph % 1.0) - 1


def noise(sec):
    return rng.uniform(-1, 1, int(SR * sec))


def filt(x, kind, cutoff, order=2):
    nyq = SR / 2
    if kind == "band":
        b, a = butter(order, [cutoff[0] / nyq, cutoff[1] / nyq], btype="band")
    else:
        b, a = butter(order, cutoff / nyq, btype=kind)
    return lfilter(b, a, x)


def sweep(f0, f1, sec, exp=True):
    n = int(SR * sec)
    return np.geomspace(f0, f1, n) if exp else np.linspace(f0, f1, n)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def pad(x, sec):
    return np.concatenate([x, np.zeros(int(SR * sec))])


def norm(x, peak=0.85):
    m = np.max(np.abs(x)) or 1.0
    return x / m * peak


def file_for(name):
    """Synth name -> taxonomy file (POL-F1, GG-T254): battle.dash plays battle_dash.ogg (App/Services/Audio.cs)."""
    if name in FILES:
        return FILES[name]
    if name.startswith(("move_", "hit_")):
        return "battle_" + name
    return name


FILES = {
    "ui_coin": "ui_buy", "slide": "battle_slide", "pickup": "battle_pickup", "pincer": "battle_pincer",
    "crit": "battle_crit", "miss": "battle_miss", "death": "battle_death", "turn": "battle_turn", "orb": "battle_orb",
    "heal": "battle_heal", "cast": "battle_cast", "fire": "element_fire", "ice": "element_ice",
    "lightning": "element_lightning", "poison": "element_poison", "magic": "element_arcane",
    "explosion": "battle_explosion", "shatter": "battle_shatter", "debuff": "status_debuff", "trap": "battle_trap",
    "dash": "battle_dash", "victory": "battle_victory", "defeat": "battle_defeat", "whoosh": "battle_whoosh",
    "splash": "element_water", "channel": "battle_channel", "glug": "battle_glug", "glass": "battle_glass",
    "taunt": "battle_taunt", "warcry": "battle_warcry", "powerup": "battle_powerup", "bubble": "status_bubble",
    "clang": "battle_clang", "crucible_enter": "battle_crucible_enter", "crucible_wave": "battle_crucible_wave",
    "crucible_best": "battle_crucible_best", "quest_accept": "story_quest_accept", "quest_complete": "story_quest_complete",
}


def write(name, x, peak=0.85):
    out = file_for(name)
    if ONLY and name not in ONLY and out not in ONLY:
        return
    name = out
    # Every cue lands at the same perceived loudness, under the true-peak ceiling (POL-F2, tools/loudness.py).
    target, ceiling = loudness.SFX_TARGET, loudness.SFX_PEAK_CEILING
    path = os.path.join(OUT, name + ".ogg")
    for _ in range(3):
        y = loudness.normalize(norm(x, peak), SR, target=target, ceiling=ceiling)
        fade = min(len(y), int(SR * 0.004))
        y[-fade:] *= np.linspace(1, 0, fade)
        pcm = (np.clip(y, -1, 1) * 32767).astype(np.int16)
        with tempfile.TemporaryDirectory() as d:
            wav = os.path.join(d, "x.wav")
            with wave.open(wav, "wb") as w:
                w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", path],
                           check=True)
        # The encoder trims the top octave, so bright cues come out quieter: aim once more by what was lost.
        enc, rate = loudness.decode(path)
        lost = loudness.SFX_TARGET - loudness.loudness(enc, rate)
        over = loudness.true_peak(enc) - loudness.SFX_PEAK_CEILING
        if abs(lost) < 0.5 and over <= 0:
            break
        target += lost
        ceiling -= max(0.0, over) + 0.2


def tone_env(f, sec, a=0.003, d=0.15, wave_fn=sine):
    x = wave_fn(f, sec)
    return x * env(len(x), a, d)


def main():
    os.makedirs(OUT, exist_ok=True)
    # ── UI ────────────────────────────────────────────────────────────────
    write("ui_click", tone_env(1900, 0.05, 0.001, 0.015) + 0.3 * tone_env(3800, 0.05, 0.001, 0.01))
    write("ui_select", np.concatenate([tone_env(880, 0.05, 0.002, 0.04), tone_env(1320, 0.08, 0.002, 0.06)]))
    write("ui_error", saw(110, 0.18) * env(int(SR * 0.18), 0.005, 0.12, 2) * 0.6 + saw(116, 0.18) * env(int(SR * 0.18), 0.005, 0.12, 2) * 0.6)
    write("ui_coin", np.concatenate([tone_env(1567, 0.06, 0.001, 0.05), tone_env(2093, 0.22, 0.001, 0.18)]))
    # ── Board ─────────────────────────────────────────────────────────────
    for i in range(6):
        n = noise(0.06)
        tick = filt(n, "band", (700 + 180 * i, 2600 + 200 * i)) * env(len(n), 0.001, 0.02, 5)
        thump = sine(160 + 15 * i, 0.06) * env(int(SR * 0.06), 0.001, 0.025)
        write(f"move_{i}", tick + 0.6 * thump, 0.6)
    n = noise(0.22)
    write("slide", filt(n, "band", (300, 1600)) * env(len(n), 0.04, 0.12, 3), 0.55)
    write("pickup", sine(sweep(420, 960, 0.12), 0.12) * env(int(SR * 0.12), 0.005, 0.08) * 0.8)
    # ── Combat ────────────────────────────────────────────────────────────
    chord = sum(tone_env(f, 0.45, 0.004, 0.25, saw) for f in (440, 554.4, 659.3))
    shimmer = sine(sweep(1200, 2400, 0.45), 0.45) * env(int(SR * 0.45), 0.05, 0.3) * 0.4
    write("pincer", filt(chord, "low", 4500) + shimmer)
    for i in range(7):
        n = noise(0.14)
        crack = filt(n, "band", (900 + 150 * i, 5000)) * env(len(n), 0.001, 0.035, 5)
        body = sine(sweep(180 - 8 * i, 70, 0.14), 0.14) * env(int(SR * 0.14), 0.001, 0.07)
        write(f"hit_{i}", crack + 0.9 * body)
    n = noise(0.3)
    write("crit", mix(filt(n, "low", 3000) * env(len(n), 0.001, 0.08, 4), sine(sweep(160, 55, 0.3), 0.3) * env(int(SR * 0.3), 0.001, 0.15), 0.5 * tone_env(2637, 0.3, 0.001, 0.2)))
    n = noise(0.16)
    write("miss", filt(n, "band", (500, 2500)) * env(len(n), 0.03, 0.06, 3), 0.45)
    n = noise(0.6)
    write("death", sine(sweep(420, 70, 0.6), 0.6) * env(int(SR * 0.6), 0.005, 0.35) + 0.4 * filt(n, "low", 900) * env(len(n), 0.01, 0.25))
    write("turn", tone_env(196, 0.45, 0.004, 0.3) + 0.5 * tone_env(392, 0.45, 0.004, 0.2) + 0.25 * tone_env(588, 0.45, 0.004, 0.12))
    write("orb", tone_env(1480, 0.2, 0.001, 0.12) + 0.5 * tone_env(2960, 0.2, 0.001, 0.08) + 0.25 * tone_env(4440, 0.2, 0.001, 0.05))
    # ── Magic ─────────────────────────────────────────────────────────────
    arp = np.concatenate([tone_env(f, 0.12, 0.004, 0.25) for f in (523.3, 659.3, 784.0)] + [tone_env(1046.5, 0.45, 0.004, 0.35)])
    write("heal", mix(arp, 0.3 * sine(sweep(1500, 3000, 0.3), 0.3) * env(int(SR * 0.3), 0.1, 0.2)))
    write("cast", sine(sweep(300, 1500, 0.35), 0.35) * env(int(SR * 0.35), 0.15, 0.2) + 0.4 * filt(noise(0.35), "band", (2000, 6000)) * env(int(SR * 0.35), 0.15, 0.15))
    n = noise(0.45)
    crackle = filt(n, "low", 2200) * env(len(n), 0.005, 0.25, 3) * (1 + 0.8 * (rng.uniform(0, 1, len(n)) > 0.985))
    write("fire", crackle + 0.6 * sine(sweep(130, 60, 0.45), 0.45) * env(int(SR * 0.45), 0.005, 0.2))
    ice = mix(*[np.concatenate([np.zeros(int(SR * d)), tone_env(f, 0.25, 0.001, 0.12)]) for d, f in ((0, 2093), (0.05, 2637), (0.1, 3136), (0.16, 3951))])
    write("ice", mix(ice, 0.3 * filt(noise(0.4), "high", 5000) * env(int(SR * 0.4), 0.002, 0.1)))
    n = noise(0.3)
    zap = saw(sweep(1800, 200, 0.3), 0.3) * env(int(SR * 0.3), 0.001, 0.1)
    write("lightning", filt(n, "high", 1500) * env(len(n), 0.001, 0.05, 5) + 0.5 * zap)
    bub = sine(220 + 120 * np.sin(2 * np.pi * 9 * t(0.4)), 0.4) * env(int(SR * 0.4), 0.01, 0.25)
    write("poison", bub + 0.3 * filt(noise(0.4), "low", 800) * env(int(SR * 0.4), 0.02, 0.2))
    fm = np.sin(2 * np.pi * 880 * t(0.5) + 3 * np.sin(2 * np.pi * 1320 * t(0.5)) * env(int(SR * 0.5), 0.001, 0.2))
    write("magic", fm * env(int(SR * 0.5), 0.002, 0.3))
    n = noise(0.9)
    boom = filt(n, "low", 700) * env(len(n), 0.002, 0.45, 3) + sine(sweep(90, 35, 0.9), 0.9) * env(int(SR * 0.9), 0.002, 0.4)
    write("explosion", boom)
    shards = mix(*[np.concatenate([np.zeros(int(SR * d)), tone_env(f, 0.2, 0.001, 0.08)]) for d, f in ((0, 3520), (0.03, 4186), (0.07, 2794), (0.11, 4699))])
    write("shatter", mix(filt(noise(0.35), "high", 2500) * env(int(SR * 0.35), 0.001, 0.08, 4), shards))
    write("debuff", sine(sweep(700, 180, 0.35), 0.35) * (0.6 + 0.4 * np.sin(2 * np.pi * 14 * t(0.35))) * env(int(SR * 0.35), 0.005, 0.25))
    snap = filt(noise(0.08), "band", (1500, 6000)) * env(int(SR * 0.08), 0.001, 0.02, 6)
    write("trap", mix(snap, 0.7 * sine(sweep(500, 140, 0.3), 0.3) * env(int(SR * 0.3), 0.002, 0.18)))
    n = noise(0.18)
    write("dash", filt(n, "band", (800, 4000)) * env(len(n), 0.01, 0.06, 4), 0.55)
    # ── Jingles ───────────────────────────────────────────────────────────
    def note(f, dur, d=0.4):
        return tone_env(f, dur, 0.006, d) + 0.4 * tone_env(f * 2, dur, 0.006, d * 0.6) + 0.2 * tone_env(f * 3, dur, 0.006, d * 0.4)
    fan = np.concatenate([note(523.3, 0.14), note(659.3, 0.14), note(784.0, 0.14), note(1046.5, 0.7, 0.9)])
    write("victory", fan)
    sad = np.concatenate([note(392.0, 0.3, 0.5), note(349.2, 0.3, 0.5), note(311.1, 0.3, 0.5), note(261.6, 0.9, 1.2)])
    write("defeat", filt(sad, "low", 2500))
    # ── Added 2026-10-11 (kept last so the RNG stream of every earlier cue is unchanged) ──
    # Press / confirm (owner direction 2026-10-11: every press has feedback; a decision has a confirmation).
    press = filt(noise(0.05), "low", 1400) * env(int(SR * 0.05), 0.001, 0.02, 6) + 0.7 * sine(sweep(260, 150, 0.05), 0.05) * env(int(SR * 0.05), 0.001, 0.03)
    write("ui_press", press, 0.5)
    write("ui_confirm", np.concatenate([tone_env(660, 0.07, 0.002, 0.05), tone_env(990, 0.07, 0.002, 0.05), tone_env(1320, 0.22, 0.002, 0.18)]) + 0.2 * filt(noise(0.36), "high", 6000) * env(int(SR * 0.36), 0.002, 0.08))
    write("ui_cancel", np.concatenate([tone_env(740, 0.07, 0.002, 0.05), tone_env(494, 0.16, 0.002, 0.12)]), 0.6)
    # Spell staging: release whoosh, splash, channel hum (owner direction 2026-10-11: rhythm and tempo).
    n = noise(0.32)
    write("whoosh", filt(n, "band", (500, 3500)) * env(len(n), 0.12, 0.16, 2) + 0.3 * sine(sweep(500, 900, 0.32), 0.32) * env(int(SR * 0.32), 0.1, 0.15), 0.6)
    n = noise(0.4)
    write("splash", filt(n, "band", (300, 4000)) * env(len(n), 0.002, 0.2, 3) + 0.5 * sine(sweep(240, 90, 0.4), 0.4) * env(int(SR * 0.4), 0.002, 0.2))
    hum = sine(180, 0.6) * (0.6 + 0.4 * np.sin(2 * np.pi * 6 * t(0.6))) + 0.4 * sine(sweep(360, 720, 0.6), 0.6)
    write("channel", hum * env(int(SR * 0.6), 0.2, 0.3), 0.5)
    # Potions: uncork, glug, glass.
    cork = filt(noise(0.05), "band", (1200, 5000)) * env(int(SR * 0.05), 0.001, 0.01, 6) + 0.6 * sine(sweep(900, 400, 0.05), 0.05) * env(int(SR * 0.05), 0.001, 0.03)
    glugs = np.concatenate([np.zeros(int(SR * 0.05))] + [sine(sweep(260 + 40 * k, 160 + 20 * k, 0.09), 0.09) * env(int(SR * 0.09), 0.005, 0.06) for k in range(3)])
    write("glug", np.concatenate([cork, glugs]), 0.7)
    shards2 = mix(*[np.concatenate([np.zeros(int(SR * d)), tone_env(f, 0.18, 0.001, 0.07)]) for d, f in ((0, 4186), (0.02, 3520), (0.05, 5274), (0.08, 2960))])
    write("glass", mix(filt(noise(0.3), "high", 3000) * env(int(SR * 0.3), 0.001, 0.06, 4), shards2))
    # Role skills: taunt roar, war horn; pickups; bubbling poison.
    n = noise(0.6)
    roar = filt(saw(sweep(140, 90, 0.6), 0.6), "low", 900) * env(int(SR * 0.6), 0.03, 0.4) + 0.5 * filt(n, "band", (200, 1200)) * env(len(n), 0.03, 0.3)
    write("taunt", roar)
    horn = sum(tone_env(f, 0.7, 0.04, 0.5, saw) for f in (196, 293.7, 392))
    write("warcry", filt(horn, "low", 2500))
    write("powerup", np.concatenate([tone_env(f, 0.07, 0.002, 0.05) for f in (784, 988, 1175, 1568)] + [tone_env(2093, 0.25, 0.002, 0.2)]))
    bubbles = mix(*[np.concatenate([np.zeros(int(SR * d)), sine(sweep(500 + 300 * k, 1100 + 300 * k, 0.06), 0.06) * env(int(SR * 0.06), 0.002, 0.04)]) for k, d in enumerate((0, 0.07, 0.15, 0.2))])
    write("bubble", bubbles, 0.6)
    # Armor plates (§13.5): a metallic clang (inharmonic partials, fast attack, ringing tail).
    clang = mix(*[tone_env(f, 0.45, 0.001, 0.35) * a for f, a in ((523, 1.0), (1393, 0.6), (2311, 0.45), (3557, 0.3))])
    write("clang", mix(clang, 0.5 * filt(noise(0.45), "band", (2000, 7000)) * env(int(SR * 0.45), 0.001, 0.04, 6)))
    # The Crucible (§22.10, GG-T95): entering it, each new wave, and a new best-waves record.
    gong = mix(*[tone_env(f, 1.6, 0.004, 1.1) * a for f, a in ((98, 1.0), (196.7, 0.55), (263.1, 0.4), (395.2, 0.3), (531.0, 0.2))])
    rumble = filt(noise(1.6), "low", 260) * env(int(SR * 1.6), 0.25, 0.9, 2)
    rise = sine(sweep(110, 330, 1.6), 1.6) * env(int(SR * 1.6), 0.6, 0.8, 2) * 0.35
    write("crucible_enter", mix(gong, 0.6 * rumble, rise))
    drum = sine(sweep(140, 55, 0.35), 0.35) * env(int(SR * 0.35), 0.001, 0.2) + 0.5 * filt(noise(0.35), "low", 1200) * env(int(SR * 0.35), 0.001, 0.06, 5)
    brass = filt(sum(tone_env(f, 0.42, 0.02, 0.3, saw) for f in (220, 329.6)), "low", 2200)
    write("crucible_wave", mix(drum, np.concatenate([np.zeros(int(SR * 0.08)), 0.55 * brass])))
    def horn(f, dur, d=0.5):
        return filt(tone_env(f, dur, 0.012, d, saw) + 0.6 * tone_env(f * 1.5, dur, 0.012, d * 0.8, saw), "low", 3200)
    record = np.concatenate([horn(392, 0.13), horn(392, 0.13), horn(523.3, 0.16), horn(659.3, 0.85, 1.0)])
    sparkle = mix(*[np.concatenate([np.zeros(int(SR * (0.42 + 0.06 * k))), tone_env(f, 0.4, 0.001, 0.25)]) for k, f in enumerate((2093, 2637, 3136, 4186))])
    write("crucible_best", mix(record, 0.35 * sparkle))
    # Side quests (§22.11, GG-T237): taking one up (a quill-scratch chime) and handing one in (a warm little fanfare).
    write("quest_accept", np.concatenate([tone_env(659.3, 0.12, 0.004, 0.2), tone_env(880.0, 0.3, 0.004, 0.5)]) * 0.7)
    fan = np.concatenate([horn(523.3, 0.11, 0.4), horn(659.3, 0.11, 0.4), horn(784.0, 0.5, 0.9)])
    bells = mix(*[np.concatenate([np.zeros(int(SR * (0.22 + 0.05 * k))), tone_env(f, 0.35, 0.001, 0.25)]) for k, f in enumerate((1568, 2093, 2637))])
    write("quest_complete", mix(fan, 0.3 * bells))
    print("wrote", len([f for f in os.listdir(OUT) if f.endswith('.ogg')]), "files to", OUT)


if __name__ == "__main__":
    main()
