"""Loudness measurement and normalization (GG-T254, docs/POLISH.md POL-F2/F3).

ITU-R BS.1770-4 integrated loudness (K-weighting at the file's own rate, 400 ms blocks with 75% overlap,
-70 LUFS absolute and -10 LU relative gates) and 4x-oversampled true peak, decoded through ffmpeg.
A clip shorter than one 400 ms block has no gated measure, so it is measured ungated over its whole length.

    python tools/loudness.py                 # report every SFX and music file (exit 1 if any is off target)
    python tools/loudness.py --json          # machine-readable, read by tools/polish.py
    python tools/loudness.py --music-apply   # re-encode the music tracks to the music target (two-pass loudnorm)

SFX are normalized where they are made: tools/make_sfx.py calls normalize() on every synthesized cue.
"""
import json
import math
import os
import subprocess
import sys

import numpy as np
from scipy.signal import lfilter, resample_poly

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SFX_DIR = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Audio", "Sfx")
MUSIC_DIR = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Audio", "Music")

SFX_TARGET, MUSIC_TARGET, TOLERANCE = -16.0, -18.0, 2.0
SFX_PEAK_CEILING, MUSIC_PEAK_CEILING = -1.0, -1.0
# Normalize a little under the ceiling: lossy encoding adds a few tenths of a dB of overshoot.
PEAK_HEADROOM = 0.8


def _biquads(rate):
    """K-weighting filter stages for any sample rate (BS.1770 shelf + RLB high-pass, RBJ forms)."""
    g, q, fc = 3.999843853973347, 0.7071752369554196, 1681.974450955533
    a_ = 10 ** (g / 40)
    w0 = 2 * math.pi * fc / rate
    al = math.sin(w0) / (2 * q)
    c, s = math.cos(w0), 2 * math.sqrt(a_) * al
    shelf = ([a_ * ((a_ + 1) + (a_ - 1) * c + s), -2 * a_ * ((a_ - 1) + (a_ + 1) * c), a_ * ((a_ + 1) + (a_ - 1) * c - s)],
             [(a_ + 1) - (a_ - 1) * c + s, 2 * ((a_ - 1) - (a_ + 1) * c), (a_ + 1) - (a_ - 1) * c - s])
    q, fc = 0.5003270373238773, 38.13547087602444
    w0 = 2 * math.pi * fc / rate
    al = math.sin(w0) / (2 * q)
    c = math.cos(w0)
    hp = ([(1 + c) / 2, -(1 + c), (1 + c) / 2], [1 + al, -2 * c, 1 - al])
    return shelf, hp


def loudness(x, rate):
    """Integrated loudness in LUFS of x (samples, or samples x channels)."""
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 1:
        x = x[:, None]
    shelf, hp = _biquads(rate)
    z = lfilter(hp[0], hp[1], lfilter(shelf[0], shelf[1], x, axis=0), axis=0)
    block, step = int(0.4 * rate), int(0.1 * rate)
    if len(z) < block:
        p = float(np.mean(np.sum(z ** 2, axis=1)))
        return -0.691 + 10 * math.log10(p) if p > 0 else -math.inf
    powers = np.array([np.mean(np.sum(z[i:i + block] ** 2, axis=1)) for i in range(0, len(z) - block + 1, step)])
    with np.errstate(divide="ignore"):
        lj = -0.691 + 10 * np.log10(powers)
    kept = powers[lj > -70]
    if not len(kept):
        return -math.inf
    rel = -0.691 + 10 * math.log10(np.mean(kept)) - 10
    kept = powers[(lj > -70) & (lj > rel)]
    return -0.691 + 10 * math.log10(np.mean(kept))


def true_peak(x):
    """True peak in dBTP (4x oversampled)."""
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 1:
        x = x[:, None]
    m = max(float(np.max(np.abs(resample_poly(x[:, ch], 4, 1)))) for ch in range(x.shape[1]))
    return 20 * math.log10(m) if m > 0 else -math.inf


def limit(x, rate, ceiling_db):
    """Look-ahead peak limiter: a gain envelope that never lets |x| pass the ceiling, opened 1.5 ms before each
    peak and released smoothly, so transients are tamed without the hard-clip crackle."""
    from scipy.ndimage import minimum_filter1d, uniform_filter1d
    c = 10 ** (ceiling_db / 20)
    g = np.minimum(1.0, c / np.maximum(np.abs(x), 1e-9))
    win = max(3, int(0.003 * rate))
    g = minimum_filter1d(g, win)
    g = np.minimum(g, uniform_filter1d(g, max(3, int(0.008 * rate))))
    return x * g


def normalize(x, rate, target=SFX_TARGET, ceiling=SFX_PEAK_CEILING):
    """Gain x to the target loudness under the true-peak ceiling. Percussive cues whose peaks would pass the
    ceiling go through the limiter, re-gained a few times so the loudness converges on the target."""
    ceil = ceiling - PEAK_HEADROOM
    y = np.asarray(x, dtype=np.float64)
    for _ in range(12):
        lu = loudness(y, rate)
        if not math.isfinite(lu):
            return y
        y = limit(y * 10 ** ((target - lu) / 20), rate, ceil)
        if abs(loudness(y, rate) - target) < 0.5:
            break
    tp = true_peak(y)
    if tp > ceil:
        y = y * 10 ** ((ceil - tp) / 20)
    return y


def decode(path):
    """Decode any audio file to float32 at its native rate via ffmpeg."""
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                            "stream=sample_rate,channels", "-of", "csv=p=0", path], capture_output=True, text=True, check=True)
    rate, ch = (int(v) for v in probe.stdout.strip().split(","))
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-acodec", "pcm_f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, ch), rate


def measure_dir(folder, ext, target, ceiling):
    rows = []
    for name in sorted(os.listdir(folder)):
        if not name.endswith(ext):
            continue
        x, rate = decode(os.path.join(folder, name))
        lu, tp = loudness(x, rate), true_peak(x)
        ok = abs(lu - target) <= TOLERANCE and tp <= ceiling
        rows.append({"file": name, "lufs": round(lu, 1), "dbtp": round(tp, 1), "seconds": round(len(x) / rate, 2), "ok": ok})
    return rows


def music_apply():
    """Two-pass loudnorm of every music track to the music target, re-encoded at 320 kbps."""
    for name in sorted(os.listdir(MUSIC_DIR)):
        if not name.endswith(".mp3"):
            continue
        src = os.path.join(MUSIC_DIR, name)
        tgt = f"I={MUSIC_TARGET}:TP={MUSIC_PEAK_CEILING - PEAK_HEADROOM}:LRA=11"
        first = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", src, "-af", f"loudnorm={tgt}:print_format=json",
                                "-f", "null", "-"], capture_output=True, text=True)
        tail = first.stderr[first.stderr.rindex("{"):]
        m = json.loads(tail[:tail.index("}") + 1])
        af = (f"loudnorm={tgt}:measured_I={m['input_i']}:measured_TP={m['input_tp']}:measured_LRA={m['input_lra']}"
              f":measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true")
        tmp = src + ".tmp.mp3"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", src, "-af", af, "-ar", "44100", "-c:a", "libmp3lame",
                        "-b:a", "320k", tmp], check=True)
        os.replace(tmp, src)
        print(f"{name}: {m['input_i']} LUFS -> {MUSIC_TARGET}")


def report():
    sfx = measure_dir(SFX_DIR, ".ogg", SFX_TARGET, SFX_PEAK_CEILING)
    music = measure_dir(MUSIC_DIR, ".mp3", MUSIC_TARGET, MUSIC_PEAK_CEILING)

    def spread(rows):
        v = [r["lufs"] for r in rows if math.isfinite(r["lufs"])]
        return {"min": min(v), "max": max(v), "range": round(max(v) - min(v), 1)} if v else None

    return {
        "sfx": {"target": SFX_TARGET, "ceiling": SFX_PEAK_CEILING, "tolerance": TOLERANCE, "spread": spread(sfx),
                "passed": sum(r["ok"] for r in sfx), "checked": len(sfx), "files": sfx},
        "music": {"target": MUSIC_TARGET, "ceiling": MUSIC_PEAK_CEILING, "tolerance": TOLERANCE, "spread": spread(music),
                  "passed": sum(r["ok"] for r in music), "checked": len(music), "files": music},
    }


def main():
    if "--music-apply" in sys.argv:
        music_apply()
        return 0
    rep = report()
    if "--json" in sys.argv:
        print(json.dumps(rep, indent=1))
    else:
        for kind in ("sfx", "music"):
            r = rep[kind]
            print(f"{kind}: {r['passed']}/{r['checked']} within {r['target']} +/-{r['tolerance']} LUFS and <= {r['ceiling']} dBTP; "
                  f"spread {r['spread']}")
            for f in r["files"]:
                if not f["ok"]:
                    print(f"   off: {f['file']:28} {f['lufs']:7} LUFS {f['dbtp']:6} dBTP {f['seconds']}s")
    bad = sum(rep[k]["checked"] - rep[k]["passed"] for k in ("sfx", "music"))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
