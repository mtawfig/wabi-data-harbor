#!/usr/bin/env python3
"""Narration for the walkthrough: one WAV per beat of script.<lang>.json, plus durations.json for record.js and compose.py.

Two engines, chosen by the script:

* English (`"voice"`): Piper, offline neural text-to-speech (MIT licence), with en_US-bryce-medium, a male voice trained
  on a public-domain dataset, so the audio can be used commercially:

      python3 -m venv .venv && .venv/bin/pip install piper-tts
      curl -L -o voices/en_US-bryce-medium.onnx      https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/bryce/medium/en_US-bryce-medium.onnx
      curl -L -o voices/en_US-bryce-medium.onnx.json https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/bryce/medium/en_US-bryce-medium.onnx.json
      .venv/bin/python tts.py --script script.en.json --voices voices --out out/en

* Arabic (`"tts": {"provider": "azure", ...}`): an Azure AI Speech neural voice (licensed for commercial use under the
  Azure terms), through the text-to-speech REST API of the Speech resource's region. The resource key is read from
  AZURE_SPEECH_KEY and never stored here:

      AZURE_SPEECH_KEY=<key> python3 tts.py --script script.ar.json --out out/ar

Words the voice would read badly are spelled for it by the script's "pronounce" map (English has a built-in one);
subtitles keep the written form. Re-running only synthesizes beats whose text, voice or rate changed (they are kept
next to each WAV).
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.request
import wave
from xml.sax.saxutils import escape

# English: how the voice pronounces a few words it would otherwise read badly (text only, never shown on screen).
SAY = {
    "WabiTex": "Wahbee Tex",
    "wabitex": "wahbee tex",
    "Wabi": "Wahbee",
    "KYC": "K Y C",
    "SQL Server": "Sequel Server",
    "SQL": "Sequel",
    "PDF": "P D F",
    "IT ": "I T ",
    "HR ": "H R ",
    "Deira": "Dayra",
}


def spoken(text, say):
    for written, said in say.items():
        text = text.replace(written, said)
    return text


def piper(args, script, scale, text, wav):
    model = os.path.join(args.voices, f"{script['voice']}.onnx")
    subprocess.run([args.piper, "-m", model, "-f", wav, "--sentence-silence", "0.3", "--length-scale", scale],
                   input=text.encode("utf-8"), check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def azure(tts, text, wav):
    key = os.environ.get("AZURE_SPEECH_KEY")
    if not key:
        raise SystemExit("Set AZURE_SPEECH_KEY to the Azure Speech resource key.")
    ssml = (f"<speak version='1.0' xml:lang='{tts['locale']}'><voice name='{tts['voice']}'>"
            f"<prosody rate='{tts.get('rate', '+0%')}'>{escape(text)}</prosody></voice></speak>")
    request = urllib.request.Request(
        f"https://{tts['region']}.tts.speech.microsoft.com/cognitiveservices/v1", data=ssml.encode("utf-8"), method="POST",
        headers={"Ocp-Apim-Subscription-Key": key, "Content-Type": "application/ssml+xml",
                 "X-Microsoft-OutputFormat": "riff-24khz-16bit-mono-pcm", "User-Agent": "wabi-insight-walkthrough"})
    with urllib.request.urlopen(request, timeout=120) as response, open(wav, "wb") as f:
        f.write(response.read())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--script", default="script.en.json")
    parser.add_argument("--voices", default="voices")
    parser.add_argument("--out", default="out/en")
    parser.add_argument("--piper", default=os.path.join(os.path.dirname(sys.executable), "piper"))
    parser.add_argument("--length-scale", default=None, help="Piper: above 1 speaks slower (default: the script's lengthScale, else 1)")
    args = parser.parse_args()

    script = json.load(open(args.script, encoding="utf-8"))
    tts = script.get("tts")
    say = script.get("pronounce", SAY)
    scale = str(args.length_scale or script.get("lengthScale", 1.0))
    voice = f"azure {tts['voice']} {tts.get('rate', '+0%')}" if tts else f"{script['voice']} {scale}"
    audio_dir = os.path.join(args.out, "audio")
    os.makedirs(audio_dir, exist_ok=True)
    durations = {}
    for scene in script["scenes"]:
        for n, text in enumerate(scene["beats"]):
            key = f"{scene['id']}-{n}"
            wav, txt = os.path.join(audio_dir, f"{key}.wav"), os.path.join(audio_dir, f"{key}.txt")
            stamp = f"{voice}\n{text}"
            if not (os.path.exists(wav) and os.path.exists(txt) and open(txt, encoding="utf-8").read() == stamp):
                if tts and tts.get("provider") == "azure":
                    azure(tts, spoken(text, say), wav)
                else:
                    piper(args, script, scale, spoken(text, say), wav)
                open(txt, "w", encoding="utf-8").write(stamp)
            with wave.open(wav) as w:
                durations[key] = round(w.getnframes() / w.getframerate(), 3)
    json.dump(durations, open(os.path.join(args.out, "durations.json"), "w"), indent=1)
    total = sum(durations.values())
    print(f"{len(durations)} beats, {total:.1f} s of narration ({total / 60:.1f} min)")


if __name__ == "__main__":
    main()
