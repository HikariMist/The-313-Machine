"""Procedural soundtrack and sound effects for THE 3:13 MACHINE.

Every sound is synthesized from scratch with numpy/scipy (no samples, no third-party audio),
so the output is original and free of licensing restrictions. Run:

    python3 audiogen.py <output Audio dir>

Writes Music/*.ogg (15 tracks) and Sfx/*.ogg. Levels: music RMS ≈ -20 dBFS, effects peak at
-3 dBFS, jumpscare stingers capped at -6 dBFS to avoid dangerously loud spikes.
"""
import os
import subprocess
import sys
import tempfile
import numpy as np
from scipy.signal import lfilter, butter, sosfilt
from scipy.io import wavfile

SR = 32000
R = np.random.RandomState(313)


# ---------------------------------------------------------------- primitives

def t(d):
    return np.arange(int(d * SR)) / SR


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def env_adsr(n, a=0.01, d=0.1, s=0.7, r=0.3):
    total = n / SR
    out = np.ones(n)
    ai, di, ri = int(a * SR), int(d * SR), int(r * SR)
    if ai: out[:ai] = np.linspace(0, 1, ai)
    if di: out[ai:ai + di] = np.linspace(1, s, min(di, max(0, n - ai)))[: max(0, min(di, n - ai))]
    out[ai + di:] = s
    if ri and ri < n: out[-ri:] *= np.linspace(1, 0, ri)
    return out


def lowpass(x, cutoff, order=2):
    sos = butter(order, min(cutoff, SR / 2 - 100) / (SR / 2), "low", output="sos")
    return sosfilt(sos, x)


def highpass(x, cutoff, order=2):
    sos = butter(order, cutoff / (SR / 2), "high", output="sos")
    return sosfilt(sos, x)


def bandpass(x, lo, hi, order=2):
    sos = butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], "band", output="sos")
    return sosfilt(sos, x)


def saw(f, tt, detune=0.0):
    ph = (f * (1 + detune)) * tt
    return 2 * (ph - np.floor(ph + 0.5))


def sine(f, tt, ph=0):
    return np.sin(2 * np.pi * f * tt + ph)


def noise(n):
    return R.uniform(-1, 1, n)


def reverb(x, wet=0.35, size=1.0):
    """Schroeder reverb: parallel combs + series allpasses."""
    out = np.zeros_like(x)
    for d, g in ((1557, 0.84), (1617, 0.83), (1491, 0.85), (1422, 0.86), (1277, 0.84), (1356, 0.85)):
        D = int(d * size * SR / 44100)
        a = np.zeros(D + 1); a[0] = 1; a[D] = -g
        out += lfilter([1], a, x)
    out /= 6
    for d, g in ((225, 0.5), (556, 0.5), (441, 0.5)):
        D = int(d * SR / 44100)
        b = np.zeros(D + 1); b[0] = -g; b[D] = 1
        a = np.zeros(D + 1); a[0] = 1; a[D] = -g
        out = lfilter(b, a, out)
    out = lowpass(out, 6000)
    return x * (1 - wet) + out * wet


def place(buf, sig, at):
    i = int(at * SR)
    if i >= len(buf): return
    n = min(len(sig), len(buf) - i)
    buf[i:i + n] += sig[:n]


# ---------------------------------------------------------------- instruments

def drone(f, d, voices=4, cutoff=900, lfo=0.07, bright=0.0):
    tt = t(d)
    x = np.zeros_like(tt)
    for v in range(voices):
        det = (v - voices / 2) * 0.0035
        x += saw(f, tt, det) * (0.6 if v % 2 else 1.0)
        x += 0.5 * sine(f / 2, tt, v)
    x /= voices
    mod = 0.5 + 0.5 * np.sin(2 * np.pi * lfo * tt)
    x = lowpass(x, cutoff * (0.6 + 0.6 * mod.mean()) + bright)
    return x * (0.7 + 0.3 * mod)


def pad(notes, d, cutoff=1400, a=2.0, r=2.5):
    tt = t(d)
    x = sum(saw(midi(n), tt, 0.003) + saw(midi(n), tt, -0.004) for n in notes) / (2 * len(notes))
    return lowpass(x, cutoff) * env_adsr(len(tt), a, 0.5, 0.85, r)


def piano(n, d=3.0, vel=0.8):
    f = midi(n)
    tt = t(d)
    x = np.zeros_like(tt)
    for k in range(1, 9):
        inh = 1 + 0.0004 * k * k
        x += np.sin(2 * np.pi * f * k * inh * tt) * (0.6 ** (k - 1)) * np.exp(-tt * (1.2 + k * 0.9))
    hammer = noise(len(tt)) * np.exp(-tt * 120) * 0.15
    x = (x + highpass(hammer, 1500)) * vel
    return x * env_adsr(len(tt), 0.002, 0.05, 1.0, 0.2)


def bell(n, d=4.0, vel=0.6):
    f = midi(n)
    tt = t(d)
    x = sum(np.sin(2 * np.pi * f * m * tt) * a * np.exp(-tt * dec)
            for m, a, dec in ((1, 1, 1.0), (2.76, 0.5, 1.8), (5.4, 0.3, 3.0), (8.93, 0.15, 4.5)))
    return x * vel * 0.5


def musicbox(n, vel=0.5):
    f = midi(n + 24)
    tt = t(1.6)
    x = (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * f * 3 * tt)) * np.exp(-tt * 4)
    return x * vel * 0.6


def tick(vel=0.5, tone=3000):
    n = int(0.04 * SR)
    return bandpass(noise(n), tone * 0.7, tone * 1.4) * np.exp(-np.arange(n) / SR * 140) * vel


def thump(f=55, vel=0.8, d=0.5):
    tt = t(d)
    sweep = f * (1 + 2.5 * np.exp(-tt * 30))
    return np.sin(2 * np.pi * np.cumsum(sweep) / SR) * np.exp(-tt * 8) * vel


def sub_pulse(f, d, rate):
    tt = t(d)
    return sine(f, tt) * (0.5 + 0.5 * np.sin(2 * np.pi * rate * tt)) ** 3


def whisper_tex(d, amount=0.4):
    n = int(d * SR)
    x = bandpass(noise(n), 1800, 5200) * amount
    am = np.abs(lowpass(noise(n), 6)) * 6
    return x * np.clip(am, 0, 1)


# ---------------------------------------------------------------- compositions
# Each function returns a mono float array. Keys and tempos differ deliberately per track.

def scale(root, kind="minor"):
    steps = {"minor": [0, 2, 3, 5, 7, 8, 10], "phrygian": [0, 1, 3, 5, 7, 8, 10], "dorian": [0, 2, 3, 5, 7, 9, 10],
             "major": [0, 2, 4, 5, 7, 9, 11], "whole": [0, 2, 4, 6, 8, 10]}[kind]
    return [root + s + 12 * o for o in range(3) for s in steps]


def track_last_stop(D=80):
    """Main menu: lonely road drone in D minor, sparse piano motif (3-1-3), distant hum."""
    x = drone(midi(38), D, cutoff=700) * 0.5 + drone(midi(45), D, cutoff=500, lfo=0.05) * 0.25
    motif = [62, 65, 69, 65, 62, 57, 62]
    for i, beat in enumerate(np.arange(4, D - 6, 9.5)):
        for j, n in enumerate(motif[: 3 + i % 3]):
            place(x, piano(n, 4, 0.5), beat + j * 0.85)
    x += sub_pulse(60, D, 0.2) * 0.04   # the machine's hum
    return reverb(x, 0.45, 1.2)


def track_graveyard_shift(D=90):
    """Night 1: steady mechanical tick at 66 bpm, warm A-minor pad, music-box fragments."""
    x = pad([57, 60, 64], D, 1100) * 0.35 + pad([45], D, 400) * 0.3
    beat = 60 / 66
    for k, at in enumerate(np.arange(0, D, beat)):
        place(x, tick(0.18 if k % 4 else 0.3, 2200), at)
    tune = [69, 72, 71, 67, 69, 64]
    for i, at in enumerate(np.arange(8, D - 4, 12)):
        for j, n in enumerate(tune):
            place(x, musicbox(n, 0.25), at + j * beat)
    return reverb(x, 0.3)


def track_something_outside(D=90):
    """Night 2: fog. Wide detuned drone in F#, slow swells, distant low horn calls."""
    x = drone(midi(42), D, voices=6, cutoff=600, lfo=0.04) * 0.5
    for at in np.arange(5, D - 8, 16):
        horn = drone(midi(30), 6, voices=2, cutoff=300) * env_adsr(int(6 * SR), 2, 1, 0.8, 2.5)
        place(x, horn * 0.5, at)
    x += lowpass(noise(len(x)), 400) * 0.05
    for at in np.arange(11, D, 23):
        place(x, bell(78, 5, 0.18), at)
    return reverb(x, 0.55, 1.4)


def track_read_the_rules(D=90):
    """Night 3: typewriter-like ostinato in C phrygian at 92 bpm, rain-like hiss, bass pulse."""
    beat = 60 / 92
    x = np.zeros(int(D * SR))
    sc = scale(48, "phrygian")
    pattern = [0, 1, 0, 3, 0, 1, 4, 3]
    for k, at in enumerate(np.arange(0, D, beat / 2)):
        place(x, piano(sc[7 + pattern[k % 8]], 0.6, 0.25), at)
        if k % 2 == 0: place(x, tick(0.12, 5000), at + 0.01)
    for at in np.arange(0, D, beat * 4):
        place(x, thump(midi(36), 0.4), at)
    x += highpass(noise(len(x)), 3000) * 0.02
    x += pad([48, 49, 55], D, 900) * 0.2
    return reverb(x, 0.25)


def track_false_reflection(D=90):
    """Night 4: reversed-piano feel in E minor, swells that stop abruptly, storm rumble."""
    x = np.zeros(int(D * SR))
    for at in np.arange(2, D - 6, 7):
        n = [64, 67, 71, 74, 76][int(at) % 5]
        p = piano(n, 4, 0.6)[::-1]
        place(x, p, at)
    x += drone(midi(28), D, cutoff=250) * 0.4
    x += lowpass(noise(len(x)), 120) * 0.25
    return reverb(x, 0.5, 1.3)


def track_employee_no_0(D=100):
    """Night 5: Mara's theme. Fragile music box in G minor over a tape-warble pad."""
    tt = t(D)
    warble = 1 + 0.004 * np.sin(2 * np.pi * 0.6 * tt)
    x = lowpass(saw(midi(43) * warble, tt) + saw(midi(50) * warble, tt) * 0.6, 700) * 0.25
    melody = [67, 70, 74, 72, 70, 67, 65, 67, 62]
    for i, at in enumerate(np.arange(3, D - 6, 14)):
        for j, n in enumerate(melody):
            place(x, musicbox(n + (12 if i % 3 == 2 else 0), 0.35), at + j * 0.7)
    x += highpass(noise(len(x)), 4000) * 0.012   # tape hiss
    return reverb(x, 0.4)


def track_machine_remembers(D=100):
    """Night 6: the machine speaks. Square-ish arpeggio in B dorian, glitches, sub drone."""
    beat = 60 / 108
    x = drone(midi(35), D, cutoff=400) * 0.35
    sc = scale(59, "dorian")
    arp = [0, 2, 4, 6, 4, 2]
    for k, at in enumerate(np.arange(0, D, beat / 2)):
        n = sc[arp[k % 6] + (2 if (k // 24) % 2 else 0)]
        tt = t(0.25)
        tone = np.sign(np.sin(2 * np.pi * midi(n) * tt)) * np.exp(-tt * 12) * 0.12
        place(x, lowpass(tone, 2500), at)
        if R.rand() < 0.04: place(x, bandpass(noise(int(0.08 * SR)), 2000, 6000) * 0.2, at)
    return reverb(x, 0.3)


def track_three_thirteen(D=110):
    """Night 7: three-note motif in 13/8, low strings, heartbeat, rising tension."""
    beat = 60 / 120 / 2
    x = np.zeros(int(D * SR))
    bar = 13
    for k, at in enumerate(np.arange(0, D, beat)):
        pos = k % bar
        if pos in (0, 3, 6, 9): place(x, thump(48 if pos == 0 else 60, 0.5), at)
        if pos in (0, 1, 3): place(x, piano([63, 61, 63][(0, 1, 3).index(pos)] - (k // (bar * 4)) % 2, 1.2, 0.35), at)
    x += drone(midi(39), D, voices=6, cutoff=500) * np.linspace(0.2, 0.6, int(D * SR))
    return reverb(x, 0.35)


def track_something_approaches(D=75):
    """Suspense: slow heartbeat, glassy harmonics, low cluster swells."""
    x = np.zeros(int(D * SR))
    for at in np.arange(0, D, 1.1):
        place(x, thump(50, 0.45), at)
        place(x, thump(48, 0.3), at + 0.28)
    for at in np.arange(4, D, 9):
        place(x, bell(86, 6, 0.12), at)
    x += pad([40, 41, 46], D, 500) * 0.3
    return reverb(x, 0.45)


def track_dont_look_behind_you(D=75):
    """High tension: tremolo strings cluster, ticking clock speeding up, whispers."""
    tt = t(D)
    trem = (0.5 + 0.5 * np.sin(2 * np.pi * 9 * tt))
    x = lowpass(saw(midi(61), tt) + saw(midi(62), tt) + saw(midi(68), tt), 2000) * trem * 0.15
    at, gap = 0.0, 0.8
    while at < D:
        place(x, tick(0.3, 3500), at)
        at += gap
        gap = max(0.25, gap * 0.993)
    x += whisper_tex(D, 0.25)
    return reverb(x, 0.35)


def track_wrong_customer(D=75):
    """Supernatural encounter: detuned music box against a dissonant drone (C vs C#)."""
    x = drone(midi(36), D, cutoff=600) * 0.35 + drone(midi(37), D, cutoff=600) * 0.25
    melody = [72, 75, 79, 78, 75, 72, 71]
    for i, at in enumerate(np.arange(2, D - 4, 8)):
        for j, n in enumerate(melody):
            mb = musicbox(n, 0.3)
            tt = np.arange(len(mb)) / SR
            place(x, mb * (1 + 0.3 * np.sin(2 * np.pi * 5 * tt)), at + j * 0.55)
    return reverb(x, 0.5, 1.3)


def track_no_more_change(D=70):
    """Critical danger: pounding 140 bpm, metal hits, distorted sub."""
    beat = 60 / 140
    x = np.zeros(int(D * SR))
    for k, at in enumerate(np.arange(0, D, beat)):
        place(x, thump(44, 0.7), at)
        if k % 2: place(x, bandpass(noise(int(0.15 * SR)), 800, 3000) * np.exp(-np.arange(int(0.15 * SR)) / SR * 25) * 0.35, at)
    tt = t(D)
    sub = np.tanh(3 * saw(midi(31), tt)) * 0.15
    x += lowpass(sub, 300)
    x += pad([55, 56, 62], D, 1500) * 0.2
    return reverb(x, 0.2)


def track_sunrise(D=70):
    """Night completion: soft major pad (D major), gentle piano, birds-like high tones."""
    x = pad([50, 54, 57, 62], D, 1600, a=4, r=6) * 0.35
    tune = [66, 69, 71, 69, 66, 64, 62]
    for i, at in enumerate(np.arange(3, D - 8, 11)):
        for j, n in enumerate(tune):
            place(x, piano(n, 3, 0.4), at + j * 0.9)
    for at in R.uniform(5, D - 2, 18):
        tt = t(0.18)
        place(x, np.sin(2 * np.pi * (2800 + 900 * tt / 0.18) * tt) * np.exp(-tt * 20) * 0.04, at)
    return reverb(x, 0.4)


def track_last_transaction(D=110):
    """Final confrontation: the 3:13 motif slowed into a funeral march over organ-like drone."""
    beat = 60 / 54
    x = drone(midi(27), D, voices=6, cutoff=700) * 0.4 + pad([51, 54, 58, 63], D, 1200) * 0.25
    for k, at in enumerate(np.arange(0, D, beat)):
        if k % 4 == 0: place(x, thump(40, 0.6, 1.0), at)
        if k % 8 in (0, 1, 3): place(x, bell([75, 73, 75][(0, 1, 3).index(k % 8)], 5, 0.25), at)
    x += whisper_tex(D, 0.1)
    return reverb(x, 0.5, 1.5)


def track_end_of_shift(D=90):
    """Credits: hopeful but tired; piano melody in F major over warm pad, the hum fades."""
    x = pad([53, 57, 60], D, 1400, a=3, r=6) * 0.3
    melody = [65, 67, 69, 72, 69, 67, 65, 64, 65]
    for i, at in enumerate(np.arange(2, D - 6, 10)):
        for j, n in enumerate(melody):
            place(x, piano(n + (5 if i % 4 == 3 else 0), 3, 0.45), at + j * 0.75)
    x += sub_pulse(60, D, 0.2) * np.linspace(0.05, 0, int(D * SR))
    return reverb(x, 0.4)


MUSIC = {
    "last_stop": track_last_stop, "graveyard_shift": track_graveyard_shift, "something_outside": track_something_outside,
    "read_the_rules": track_read_the_rules, "false_reflection": track_false_reflection, "employee_no_0": track_employee_no_0,
    "machine_remembers": track_machine_remembers, "three_thirteen": track_three_thirteen,
    "something_approaches": track_something_approaches, "dont_look_behind_you": track_dont_look_behind_you,
    "wrong_customer": track_wrong_customer, "no_more_change": track_no_more_change, "sunrise": track_sunrise,
    "last_transaction": track_last_transaction, "end_of_shift": track_end_of_shift,
}


# ---------------------------------------------------------------- sound effects

def sfx_lib():
    S = {}
    tt = lambda d: t(d)
    S["ui_click"] = tick(0.6, 2500)
    S["ui_tick"] = tick(0.3, 4000)
    S["key_press"] = np.concatenate([tick(0.5, 1800), tick(0.2, 900)])
    S["inspect"] = lowpass(noise(int(0.2 * SR)), 1500) * np.exp(-tt(0.2) * 20) * 0.4
    S["purchase"] = np.concatenate([bell(84, 0.25, 0.5), bell(88, 0.4, 0.5)])
    S["error_beep"] = np.sign(sine(330, tt(0.35))) * 0.2 * (tt(0.35) % 0.17 < 0.12)
    S["machine_beep"] = np.sign(sine(990, tt(0.18))) * 0.15
    S["dispense"] = np.concatenate([lowpass(saw(70, tt(0.6)), 400) * 0.4, thump(90, 0.9, 0.4), tick(0.4, 1200)])
    S["dispense_thunk"] = thump(80, 0.9, 0.5)
    S["receipt"] = bandpass(noise(int(0.9 * SR)), 1500, 5000) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 30 * tt(0.9)))) * 0.25
    S["restock"] = np.concatenate([thump(120, 0.5, 0.2), tick(0.4, 1500), thump(110, 0.5, 0.2)])
    S["can_open"] = np.concatenate([tick(0.7, 3000), highpass(noise(int(0.4 * SR)), 3000) * np.exp(-tt(0.4) * 8) * 0.3])
    S["can_pop"] = S["can_open"]
    S["paper_rustle"] = bandpass(noise(int(0.7 * SR)), 2000, 7000) * np.abs(np.sin(2 * np.pi * 7 * tt(0.7))) * 0.35
    S["clue"] = np.concatenate([bell(76, 0.3, 0.4), bell(83, 1.2, 0.4)])
    S["panel_open"] = np.concatenate([lowpass(saw(55, tt(0.5)), 600) * 0.4 * np.linspace(1, 0, int(0.5 * SR)), thump(70, 0.6, 0.3)])
    S["panel_close"] = thump(65, 0.8, 0.35)
    S["motor_jam"] = lowpass(np.tanh(4 * saw(48 + 20 * np.sin(2 * np.pi * 9 * tt(1.2)), tt(1.2))), 900) * 0.4
    S["motor_reset"] = lowpass(saw(np.linspace(40, 90, int(1 * SR)), tt(1)), 800) * 0.35
    S["machine_hum_start"] = lowpass(saw(np.linspace(20, 60, int(2 * SR)), tt(2)), 300) * 0.5
    S["monitor_on"] = np.concatenate([np.sin(2 * np.pi * 15000 * tt(0.15)) * 0.05, highpass(noise(int(0.4 * SR)), 2000) * 0.15])
    S["cam_switch"] = np.concatenate([highpass(noise(int(0.12 * SR)), 1500) * 0.25, tick(0.3, 2000)])
    S["static_burst"] = highpass(noise(int(1.2 * SR)), 800) * 0.3 * np.exp(-tt(1.2) * 2)
    S["phone_ring"] = np.concatenate([(sine(440, tt(1.0)) + sine(480, tt(1.0))) * 0.15 * (np.sin(2 * np.pi * 20 * tt(1.0)) > 0), np.zeros(int(0.6 * SR))] * 2)
    S["phone_hangup"] = np.concatenate([tick(0.8, 600), thump(150, 0.5, 0.2)])
    step = thump(140, 0.35, 0.12)
    step[: len(tick(0.15, 600))] += tick(0.15, 600)
    S["footsteps"] = np.concatenate([np.concatenate([step, np.zeros(int(0.42 * SR))]) for _ in range(5)])
    S["knock_inside"] = np.concatenate([np.concatenate([thump(95, 0.9, 0.18), np.zeros(int(0.22 * SR))]) for _ in range(3)] + [np.zeros(int(0.6 * SR))] + [np.concatenate([thump(95, 0.9, 0.18), np.zeros(int(0.22 * SR))]) for _ in range(3)])
    S["power_down"] = lowpass(saw(np.linspace(60, 15, int(1.6 * SR)), tt(1.6)), 400) * np.linspace(0.5, 0, int(1.6 * SR))
    S["power_up"] = lowpass(saw(np.linspace(15, 60, int(1.2 * SR)), tt(1.2)), 400) * np.linspace(0, 0.5, int(1.2 * SR))
    S["glitch"] = np.concatenate([bandpass(noise(int(0.05 * SR)), 500 + 400 * i, 4000) * 0.3 for i in range(8)])
    S["margin_drop"] = reverb(np.concatenate([thump(35, 1.0, 2.5), np.zeros(int(2 * SR))]) + drone(midi(25), 4.5, cutoff=200) * 0.3, 0.5)
    S["whisper"] = reverb(whisper_tex(2.0, 0.6), 0.5)
    S["stinger_hard"] = reverb(np.concatenate([np.tanh(3 * (saw(midi(61), tt(1.2)) + saw(midi(67), tt(1.2)) + noise(int(1.2 * SR)) * 0.6)) * np.exp(-tt(1.2) * 2.5), np.zeros(int(0.8 * SR))]), 0.3)
    S["stinger_soft"] = reverb(np.concatenate([(sine(midi(73), tt(1.5)) + sine(midi(74), tt(1.5))) * np.exp(-tt(1.5) * 2) * 0.4, np.zeros(int(1 * SR))]), 0.5)
    # character motifs: short recognizable figures
    S["motif_normal"] = np.zeros(int(0.1 * SR))
    S["motif_del"] = piano(55, 1.2, 0.3)
    S["motif_leash"] = np.concatenate([musicbox(79, 0.2)[: int(0.25 * SR)], musicbox(83, 0.2)])
    S["motif_walter"] = reverb(lowpass(noise(int(2 * SR)), 300) * 0.3 + drone(midi(31), 2, cutoff=200) * 0.3, 0.5)
    S["motif_smile"] = reverb(np.concatenate([musicbox(76, 0.3)[: int(0.3 * SR)], musicbox(80, 0.3)[: int(0.3 * SR)], musicbox(77, 0.3)]), 0.4)
    S["motif_crane"] = reverb(bell(64, 2.5, 0.3), 0.5)
    S["motif_collector"] = reverb(np.concatenate([bell(51, 1.2, 0.4)[: int(0.6 * SR)], bell(50, 2.5, 0.4)]), 0.6)
    S["motif_lily"] = reverb(np.concatenate([musicbox(84, 0.25)[: int(0.2 * SR)], musicbox(86, 0.25)[: int(0.2 * SR)], musicbox(88, 0.25)]), 0.4)
    S["motif_mara"] = reverb(np.concatenate([musicbox(67, 0.3)[: int(0.35 * SR)], musicbox(70, 0.3)[: int(0.35 * SR)], musicbox(74, 0.3)]), 0.5)
    S["motif_hale"] = np.concatenate([tick(0.5, 900), np.zeros(int(0.2 * SR)), tick(0.5, 900)])
    S["motif_mimic"] = reverb(np.concatenate([musicbox(76, 0.3), np.zeros(1)])[::-1], 0.4)
    S["motif_double"] = reverb(S["machine_beep"], 0.6)
    S["motif_crowd"] = reverb(whisper_tex(2.5, 0.5), 0.6)
    # ambient scare sounds
    S["scare_shadow_pass"] = reverb(lowpass(noise(int(1.5 * SR)), 500) * np.sin(np.linspace(0, np.pi, int(1.5 * SR))) * 0.4, 0.5)
    S["scare_reflection_linger"] = S["stinger_soft"] * 0.6
    S["scare_distant_figure"] = reverb(bell(86, 2, 0.15), 0.6)
    S["scare_machine_hum"] = lowpass(saw(55, tt(2)), 300) * 0.35 * np.sin(np.linspace(0, np.pi, int(2 * SR)))
    S["scare_item_shift"] = np.concatenate([np.zeros(int(0.2 * SR)), thump(160, 0.5, 0.2)])
    S["scare_far_headlights"] = lowpass(noise(int(3 * SR)), 250) * np.sin(np.linspace(0, np.pi, int(3 * SR))) * 0.35
    S["scare_light_flicker"] = np.concatenate([tick(0.5, 6000), np.zeros(int(0.1 * SR)), tick(0.4, 6000), highpass(noise(int(0.3 * SR)), 5000) * 0.1])
    S["scare_figure_window"] = S["stinger_soft"] * 0.7
    S["scare_cam_static"] = S["static_burst"]
    S["scare_object_fall"] = np.concatenate([np.zeros(int(0.1 * SR)), thump(200, 0.7, 0.2), tick(0.4, 800), thump(180, 0.4, 0.2)])
    S["scare_stinger_soft"] = S["stinger_soft"]
    S["scare_knock_once"] = thump(95, 0.9, 0.25)
    S["scare_lights_fail"] = S["power_down"]
    S["scare_face_glass"] = np.concatenate([thump(70, 0.9, 0.3), S["stinger_soft"] * 0.8])
    S["scare_rush_figure"] = reverb(highpass(noise(int(1.0 * SR)), 400) * np.linspace(0, 0.6, int(1.0 * SR)), 0.3)
    S["scare_reality_warp"] = reverb(lowpass(saw(np.linspace(80, 30, int(2 * SR)), tt(2)), 600) * 0.4, 0.6)
    S["scare_whisper_burst"] = S["whisper"]
    S["scare_hands_glass"] = np.concatenate([thump(120, 0.6, 0.15), thump(120, 0.6, 0.15), bandpass(noise(int(0.8 * SR)), 2000, 6000) * np.linspace(0.3, 0, int(0.8 * SR))])
    S["scare_margin_surge"] = S["margin_drop"]
    return S


def ambience():
    D = 30
    A = {}
    base_wind = lowpass(noise(int(D * SR)), 500) * (0.6 + 0.4 * np.sin(2 * np.pi * 0.05 * t(D)))
    A["amb_wind"] = base_wind * 0.3
    A["amb_clear"] = base_wind * 0.15 + sub_pulse(60, D, 0.1) * 0.03 + highpass(noise(int(D * SR)), 5000) * 0.004
    A["amb_fog"] = lowpass(noise(int(D * SR)), 250) * 0.25
    rain = highpass(noise(int(D * SR)), 1500) * 0.12
    A["amb_rain"] = rain + lowpass(noise(int(D * SR)), 300) * 0.1
    storm = A["amb_rain"] * 1.4
    for at in (7, 19, 27):
        place(storm, reverb(lowpass(noise(int(3 * SR)), 120) * np.exp(-t(3) * 1.2) * 0.9, 0.5), at)
    A["amb_storm"] = storm
    A["amb_margin"] = lowpass(noise(int(D * SR)), 200) * 0.2 + whisper_tex(D, 0.06) + drone(midi(25), D, cutoff=150) * 0.15
    A["amb_flood"] = lowpass(noise(int(D * SR)), 700) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.15 * t(D))) * 0.3 + rain * 0.6
    birds = np.zeros(int(D * SR))
    for at in R.uniform(0, D - 1, 30):
        tt = t(0.15)
        place(birds, np.sin(2 * np.pi * (3000 + 1500 * tt / 0.15) * tt) * np.exp(-tt * 18) * 0.08, at)
    A["amb_birds"] = birds + lowpass(noise(int(D * SR)), 400) * 0.05
    # crossfade ends so loops are seamless
    for k, v in A.items():
        n = int(1.5 * SR)
        v[:n] = v[:n] * np.linspace(0, 1, n) + v[-n:] * np.linspace(1, 0, n)
        A[k] = v[:-n]
    return A


# ---------------------------------------------------------------- output

def normalize(x, peak=None, rms=None):
    x = x - np.mean(x)
    if rms is not None:
        cur = np.sqrt(np.mean(x ** 2)) + 1e-9
        x = x * (rms / cur)
    p = np.max(np.abs(x)) + 1e-9
    limit = peak if peak is not None else 0.89
    if p > limit:
        x = np.tanh(x / limit * 1.2) / np.tanh(1.2) * limit   # soft limiter, no harsh clipping
    return x


def fade_loop(x, sec=2.0):
    n = int(sec * SR)
    x[:n] *= np.linspace(0, 1, n)
    x[-n:] *= np.linspace(1, 0, n)
    return x


def write_ogg(x, path, quality=3):
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        wavfile.write(f.name, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", f.name, "-c:a", "libvorbis", "-q:a", str(quality), path], check=True)
        os.unlink(f.name)


def main(out, only=None):
    md, sd = os.path.join(out, "Music"), os.path.join(out, "Sfx")
    os.makedirs(md, exist_ok=True)
    os.makedirs(sd, exist_ok=True)
    for name, fn in (MUSIC.items() if only != "sfx" else []):
        x = fade_loop(normalize(fn(), rms=0.1, peak=0.85))
        write_ogg(x, os.path.join(md, name + ".ogg"), 2)
        print("music", name, f"{len(x) / SR:.0f}s")
    if only == "music":
        return
    for name, x in sfx_lib().items():
        peak = 0.5 if name.startswith("stinger") or name.startswith("scare_") else 0.7
        x = normalize(np.asarray(x, np.float64), peak=peak)
        if np.max(np.abs(x)) > 0: x = x / np.max(np.abs(x)) * peak
        write_ogg(x, os.path.join(sd, name + ".ogg"), 3)
    for name, x in ambience().items():
        write_ogg(normalize(x, rms=0.06, peak=0.5), os.path.join(sd, name + ".ogg"), 2)
    print("sfx done")


if __name__ == "__main__":
    # Usage: audiogen.py <Audio dir> [music|sfx]   (music renders slowly: ~7 minutes)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
