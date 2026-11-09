#!/usr/bin/env python3
"""Walkthrough composer: turns the recorded scenes (record.js), the narration (tts.py) and the script into the final video.

For every scene it rebuilds the visible timeline (the stretches marked as cuts, such as waiting for the AI, are removed)
and re-times it to the narration: each line starts where record.js marked it and plays over the screen at normal speed;
the stretches between lines (cursor moves, typing, pages loading) are sped up; and where a line is longer than the
recorded gap before the next one, the picture holds still until it ends. So the narration can be regenerated (another
voice, another rate, edited lines) without recording the scenes again. Scenes are joined with short crossfades.
The soundtrack is the narration over a quiet music bed generated here (no third-party music), with soft click and
transition sounds. It also writes English subtitles (SRT, also embedded as a subtitle track) and chapter markers.

    python3 compose.py --script script.en.json --work out/en --video ../../../docs/demo/wabi-insight-walkthrough-en.mp4

Needs ffmpeg (libx264, aac) and numpy.
"""
import argparse
import json
import math
import os
import re
import subprocess
import wave

import numpy as np

RATE = 48000
FPS = 30
FADE = 0.5           # crossfade between scenes, seconds
LEAD = 0.25          # the voice starts this long after its mark (record.js uses the same value)
MUSIC_DB = -27.0     # music bed level under the voice
DUCK_DB = -7.0       # extra music reduction while the voice speaks
GAP_SPEED = 1.8      # speed of the stretches between narration lines in screen recordings
PAUSE = 0.35         # normal-speed margin kept before and after every line
TAIL = 0.9           # normal-speed end of every scene


def read_wav(path):
    with wave.open(path, "rb") as w:
        rate, channels, width = w.getframerate(), w.getnchannels(), w.getsampwidth()
        data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16 if width == 2 else np.int32).astype(np.float32)
    data /= 32768.0 if width == 2 else 2147483648.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    if rate != RATE:
        n = int(len(data) * RATE / rate)
        data = np.interp(np.linspace(0, len(data) - 1, n), np.arange(len(data)), data).astype(np.float32)
    return data


def write_wav(path, stereo):
    pcm = (np.clip(stereo, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


class Scene:
    """One recorded scene on its visible timeline (seconds from the scene start, cuts removed)."""

    def __init__(self, work, spec):
        self.spec = spec
        self.id = spec["id"]
        self.dir = os.path.join(work, "scenes", self.id)
        with open(os.path.join(self.dir, "timeline.json"), encoding="utf-8") as f:
            tl = json.load(f)
        self.start = tl["start"]
        self.cuts = sorted(tl.get("cuts", []))
        self.duration = self.visible(tl["end"])
        self.beats = {int(k): self.visible(v) for k, v in tl["beats"].items()}
        self.clicks = [self.visible(t) for t in tl.get("clicks", [])]
        frames = []
        for f in tl["frames"]:
            v = max(0.0, self.visible(f["t"]))
            if frames and abs(frames[-1][0] - v) < 1e-6:
                frames[-1] = (v, f["file"])  # several frames collapsed by a cut: keep the latest state
            else:
                frames.append((v, f["file"]))
        self.frames = frames

    def visible(self, t):
        cut = 0.0
        for a, b in self.cuts:
            if t >= b:
                cut += b - a
            elif t > a:
                cut += t - a
        return t - self.start - cut

    def retime(self, durations, speed):
        """Plans the output timing: (start, end, speed) segments over the visible timeline plus still holds (at, seconds)."""
        segments, holds, pos = [], [], 0.0

        def play(to, rate=1.0):
            nonlocal pos
            if to > pos + 1e-6:
                segments.append((pos, to, rate))
                pos = to

        order = sorted(self.beats)
        play(self.beats[order[0]] + LEAD)
        for i, n in enumerate(order):
            last = i + 1 == len(order)
            start = self.beats[n] + LEAD
            end = start + durations[f"{self.id}-{n}"]
            boundary = self.duration if last else self.beats[order[i + 1]]
            following = self.duration if last else boundary + LEAD
            margin = TAIL if last else PAUSE
            if end + margin > boundary:
                # The line runs past the next mark: hold the picture there until it has been heard.
                play(boundary)
                holds.append((boundary, end + margin - boundary))
                play(following)
            else:
                fast_from, fast_to = end + PAUSE, following - (TAIL if last else PAUSE)
                if speed > 1 and fast_to - fast_from > 0.4:
                    play(fast_from)
                    play(fast_to, speed)
                play(following)
        play(self.duration)
        self.segments, self.holds = segments, holds
        self.length = self.out(self.duration)

    def out(self, t):
        """Output time (seconds from the start of this scene's clip) of visible time t."""
        o = 0.0
        for a, b, rate in self.segments:
            if t <= a:
                break
            o += (min(t, b) - a) / rate
        return o + sum(h for at, h in self.holds if at <= t + 1e-9)

    def encode(self, path):
        """Encodes the re-timed frames at a constant frame rate (each frame held until the next one)."""
        listing = os.path.join(self.dir, "frames.txt")
        with open(listing, "w", encoding="utf-8") as f:
            times = [self.out(v) for v, _ in self.frames] + [self.length]
            for i, (_, name) in enumerate(self.frames):
                f.write(f"file 'frames/{name}'\nduration {max(times[i + 1] - times[i], 0.0005):.4f}\n")
            f.write(f"file 'frames/{self.frames[-1][1]}'\n")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing,
                        "-vf", f"fps={FPS},scale=1920:1080:flags=lanczos,format=yuv420p", "-t", f"{self.length:.3f}",
                        "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", path], check=True)


# ------------------------------------------------------------------------------------------------ sound
def tone(freq, seconds, kind="sine"):
    t = np.arange(int(seconds * RATE)) / RATE
    if kind == "tri":
        return (2 / np.pi) * np.arcsin(np.sin(2 * np.pi * freq * t))
    return np.sin(2 * np.pi * freq * t)


def envelope(n, attack, release):
    env = np.ones(n, dtype=np.float32)
    a, r = min(int(attack * RATE), n // 2), min(int(release * RATE), n // 2)
    if a:
        env[:a] = np.linspace(0, 1, a)
    if r:
        env[-r:] *= np.linspace(1, 0, r)
    return env


def music_bed(seconds):
    """A calm, original pad-and-arpeggio loop (four chords, 96 bpm), long enough for the whole video."""
    bpm, bar = 96, 4 * 60 / 96
    chords = [[57, 60, 64, 67], [53, 57, 60, 64], [48, 52, 55, 59], [55, 59, 62, 65]]  # Am7 Fmaj7 Cmaj7 G7 (MIDI)
    hz = lambda m: 440 * 2 ** ((m - 69) / 12)
    n = int(seconds * RATE) + RATE
    left, right = np.zeros(n, np.float32), np.zeros(n, np.float32)
    pos, i = 0, 0
    step = 60 / bpm / 2  # eighth notes
    while pos < n:
        notes = chords[i % 4]
        length = int(bar * 2 * RATE)
        seg = min(length, n - pos)
        pad = sum(0.18 * tone(hz(m), bar * 2, "tri")[:seg] for m in notes) + 0.22 * tone(hz(notes[0] - 12), bar * 2)[:seg]
        pad *= envelope(seg, 0.8, 0.8)
        left[pos:pos + seg] += pad * 0.9
        right[pos:pos + seg] += pad
        arp = [notes[0] + 12, notes[1] + 12, notes[2] + 12, notes[3] + 12, notes[2] + 12, notes[1] + 12, notes[2] + 12, notes[3] + 12]
        for k in range(16):
            start = pos + int(k * step * RATE)
            if start >= n:
                break
            m = arp[k % len(arp)]
            note = 0.16 * tone(hz(m), step * 1.6) * np.exp(-np.arange(int(step * 1.6 * RATE)) / RATE * 6)
            end = min(start + len(note), n)
            pan = 0.35 + 0.3 * (k % 2)
            left[start:end] += note[:end - start] * (1 - pan)
            right[start:end] += note[:end - start] * pan
        pos += length
        i += 1
    bed = np.stack([left, right], axis=1)[: int(seconds * RATE)]
    bed /= max(1e-6, np.abs(bed).max())
    return bed


def click_sound():
    n = int(0.05 * RATE)
    t = np.arange(n) / RATE
    return (0.5 * np.sin(2 * np.pi * 1800 * t) * np.exp(-t * 120)).astype(np.float32)


def whoosh(seconds=0.9):
    n = int(seconds * RATE)
    rng = np.random.default_rng(7)
    noise = rng.standard_normal(n).astype(np.float32)
    # Moving low-pass: a one-pole filter whose cutoff rises then falls.
    out = np.zeros(n, np.float32)
    acc = 0.0
    for i in range(n):
        x = i / n
        alpha = 0.02 + 0.25 * math.sin(math.pi * x)
        acc += alpha * (noise[i] - acc)
        out[i] = acc
    out *= np.sin(np.linspace(0, np.pi, n)) ** 2
    return 0.6 * out / max(1e-6, np.abs(out).max())


def add(track, at, sound, gain=1.0, pan=0.5):
    i = int(at * RATE)
    if i >= len(track) or i < 0:
        return
    end = min(len(track), i + len(sound))
    track[i:end, 0] += sound[: end - i] * gain * (1 - pan) * 2
    track[i:end, 1] += sound[: end - i] * gain * pan * 2


# ------------------------------------------------------------------------------------------------ subtitles
def chunks(text, limit=84):
    """Splits a narration line into subtitle cards of at most about two 42-character lines, at sentence or clause breaks where possible."""
    clauses = [c for c in re.split(r"(?<=[.!?:;,])\s+", text.strip()) if c]
    pieces = []
    for clause in clauses:
        if len(clause) <= limit:
            pieces.append(clause)
            continue
        current = ""
        for word in clause.split():
            if len(current) + len(word) + 1 > limit and current:
                pieces.append(current)
                current = word
            else:
                current = f"{current} {word}".strip()
        pieces.append(current)
    cards, current = [], ""
    for piece in pieces:
        ends_sentence = current.endswith((".", "!", "?"))
        if current and (len(current) + len(piece) + 1 > limit or (ends_sentence and len(current) > limit * 0.4)):
            cards.append(current)
            current = piece
        else:
            current = f"{current} {piece}".strip()
    if current:
        cards.append(current)
    return cards


def wrap(card, width=46):
    """One line when it fits, otherwise two lines of similar length."""
    if len(card) <= width:
        return card
    words = card.split()
    best, split = None, 1
    for i in range(1, len(words)):
        a, b = len(" ".join(words[:i])), len(" ".join(words[i:]))
        if best is None or max(a, b) < best:
            best, split = max(a, b), i
    return " ".join(words[:split]) + "\n" + " ".join(words[split:])


def stamp(t):
    ms = int(round(t * 1000))
    return f"{ms // 3600000:02d}:{ms // 60000 % 60:02d}:{ms // 1000 % 60:02d},{ms % 1000:03d}"


def clock(t):
    return f"{int(t // 60)}:{int(t % 60):02d}"


def write_script(path, script, scenes, offsets, total):
    """The narration script with the time each scene and line starts in the finished video."""
    contact = script["contact"]
    lines = [f"# {script['title']} — narration script", "",
             f"Length {clock(total)} · voice `{script['voice']}` (Piper, offline) · contact: {contact['company']}, {contact['website']}, {contact['email']}.",
             "Generated by `scripts/demo/walkthrough/compose.py` from `script.<lang>.json`; edit the JSON, not this file.", ""]
    chapter = None
    for scene, offset in zip(scenes, offsets):
        spec = scene.spec
        if spec["chapter"] != chapter:
            chapter = spec["chapter"]
            lines += [f"## {clock(offset)} — {chapter}", ""]
        lines += [f"**On screen:** {spec.get('screen', '')}", ""]
        for n, text in enumerate(spec["beats"]):
            lines.append(f"- `{clock(offset + scene.out(scene.beats[n] + LEAD))}` {text}")
        lines.append("")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


# ------------------------------------------------------------------------------------------------ main
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--script", default="script.en.json")
    parser.add_argument("--work", default="out/en")
    parser.add_argument("--video", required=True)
    parser.add_argument("--crf", default="26", help="final H.264 quality (lower is better and larger)")
    parser.add_argument("--only", help="comma-separated scene ids, for a quick preview of some scenes")
    parser.add_argument("--plan", action="store_true", help="print the timing of every scene and stop")
    parser.add_argument("--reuse-clips", action="store_true", help="keep scene clips encoded by an earlier run (same recordings and narration)")
    args = parser.parse_args()

    with open(args.script, encoding="utf-8") as f:
        script = json.load(f)
    with open(os.path.join(args.work, "durations.json"), encoding="utf-8") as f:
        durations = json.load(f)
    specs = [s for s in script["scenes"] if not args.only or s["id"] in args.only.split(",")]
    scenes = [Scene(args.work, spec) for spec in specs]

    clips = os.path.join(args.work, "clips")
    os.makedirs(clips, exist_ok=True)
    offsets, at = [], 0.0
    for i, scene in enumerate(scenes):
        scene.retime(durations, 1.0 if scene.spec["kind"] == "graphic" else GAP_SPEED)
        offsets.append(at)
        at += scene.length - (FADE if i + 1 < len(scenes) else 0)
        held = sum(h for _, h in scene.holds)
        print(f"{scene.id:<11} recorded {scene.duration:6.1f} s  final {scene.length:6.1f} s  (held {held:4.1f} s)  starts {offsets[-1]:6.1f} s")
    total = at
    print(f"total {total:.1f} s ({total / 60:.2f} min)")
    if args.plan:
        return
    for i, scene in enumerate(scenes):
        scene.clip = os.path.join(clips, f"{i:02d}-{scene.id}.mp4")
        if not (args.reuse_clips and os.path.exists(scene.clip)):
            scene.encode(scene.clip)

    # Soundtrack: narration, music bed ducked under the voice, clicks and transition sounds.
    voice = np.zeros((int((total + 2) * RATE), 2), np.float32)
    speaking = np.zeros(len(voice), np.float32)
    srt, number = [], 1
    for scene, offset in zip(scenes, offsets):
        for n, text in enumerate(scene.spec["beats"]):
            start = offset + scene.out(scene.beats[n] + LEAD)
            audio = read_wav(os.path.join(args.work, "audio", f"{scene.id}-{n}.wav"))
            add(voice, start, audio, 0.9)
            i = int(start * RATE)
            speaking[i:i + len(audio)] = 1
            length = durations[f"{scene.id}-{n}"]
            cards = chunks(text)
            weights = [len(c) for c in cards]
            t = start
            for card, w in zip(cards, weights):
                d = length * w / sum(weights)
                srt.append(f"{number}\n{stamp(t)} --> {stamp(t + d - 0.05)}\n{wrap(card)}\n")
                number += 1
                t += d
        tick = click_sound()
        for c in scene.clicks:
            add(voice, offset + scene.out(c) + 0.05, tick, 0.18)
    swoosh = whoosh()
    for i, offset in enumerate(offsets[1:], 1):
        add(voice, offset - 0.35, swoosh, 0.10)

    # Smooth the ducking curve so the music breathes between lines.
    kernel = np.ones(int(0.35 * RATE), np.float32) / int(0.35 * RATE)
    duck = np.convolve(speaking, kernel, mode="same")
    gain = 10 ** ((MUSIC_DB + DUCK_DB * duck) / 20)
    bed = music_bed(len(voice) / RATE)
    bed[: int(2 * RATE)] *= np.linspace(0, 1, int(2 * RATE))[:, None]
    tail = int(4 * RATE)
    end = int(total * RATE)
    bed[end - tail:end] *= np.linspace(1, 0, tail)[:, None]
    bed[end:] = 0
    mix = voice + bed * gain[:, None]
    mix = mix[: int(total * RATE)]
    peak = np.abs(mix).max()
    if peak > 0.98:
        mix *= 0.98 / peak
    soundtrack = os.path.join(args.work, "soundtrack.wav")
    write_wav(soundtrack, mix)

    base = os.path.splitext(args.video)[0]
    with open(base + ".srt", "w", encoding="utf-8") as f:
        f.write("\n".join(srt))

    write_script(base + ".md", script, scenes, offsets, total)

    meta = os.path.join(args.work, "chapters.txt")
    with open(meta, "w", encoding="utf-8") as f:
        f.write(";FFMETADATA1\ntitle=" + script["title"] + "\nartist=" + script["contact"]["company"] + "\n")
        chapter_starts = []
        for scene, offset in zip(scenes, offsets):
            if chapter_starts and chapter_starts[-1][1] == scene.spec["chapter"]:
                continue
            chapter_starts.append((offset, scene.spec["chapter"]))
        for i, (start, title) in enumerate(chapter_starts):
            end = chapter_starts[i + 1][0] if i + 1 < len(chapter_starts) else total
            f.write(f"[CHAPTER]\nTIMEBASE=1/1000\nSTART={int(start * 1000)}\nEND={int(end * 1000)}\ntitle={title}\n")

    # Video: scene clips joined with crossfades, fade in from and out to black.
    inputs, filters, last = [], [], "0:v"
    for scene in scenes:
        inputs += ["-i", scene.clip]
    for i in range(1, len(scenes)):
        out = f"x{i}"
        filters.append(f"[{last}][{i}:v]xfade=transition=fade:duration={FADE}:offset={offsets[i]:.3f}[{out}]")
        last = out
    filters.append(f"[{last}]fade=t=in:st=0:d=0.8,fade=t=out:st={total - 1.2:.3f}:d=1.2,format=yuv420p[v]")
    n = len(scenes)
    cmd = ["ffmpeg", "-y", "-loglevel", "error", *inputs, "-i", soundtrack, "-i", base + ".srt", "-i", meta,
           "-filter_complex", ";".join(filters), "-map", "[v]", "-map", f"{n}:a", "-map", f"{n + 1}:s", "-map_metadata", f"{n + 2}",
           "-map_chapters", f"{n + 2}", "-c:v", "libx264", "-preset", "slow", "-crf", args.crf, "-tune", "stillimage",
           "-r", str(FPS), "-c:a", "aac", "-b:a", "160k", "-c:s", "mov_text", "-metadata:s:s:0", "language=eng",
           "-movflags", "+faststart", "-t", f"{total:.3f}", args.video]
    subprocess.run(cmd, check=True)
    size = os.path.getsize(args.video) / 1e6
    print(f"{args.video}: {total / 60:.2f} min, {size:.1f} MB")


if __name__ == "__main__":
    main()
