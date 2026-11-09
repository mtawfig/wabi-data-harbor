# Product walkthrough video

Generates the Wabi Insight walkthrough video, in English and in Arabic (about eight to nine minutes each), from the
**running application** with the Wabi Bank sample organization: real pages, real AI answers and real generated files,
with narration, title graphics, subtitles and chapters. The finished videos and their scripts are in `docs/demo/`:

| File | What |
|---|---|
| `docs/demo/wabi-insight-walkthrough-en.mp4` | The video (1920×1080, H.264/AAC, English subtitle track and chapter markers) |
| `docs/demo/wabi-insight-walkthrough-en.srt` | English subtitles |
| `docs/demo/wabi-insight-walkthrough-en.md` | The narration script with the time of every scene and line |
| `docs/demo/wabi-insight-walkthrough-ar.mp4`, `.srt`, `.md` | The Arabic video (Arabic interface, Arabic questions and answers, Arabic subtitle track), its subtitles and script |

The Platform Administrator is deliberately not shown: the video is for organizations evaluating the product.

## Files here

| File | Role |
|---|---|
| `script.en.json`, `script.ar.json` | The script per language: scenes in order, chapter titles, what is on screen, the narration lines (one "beat" per line), voice, pronunciation help and contact details. Edit these to change what is said. |
| `prepare.mjs` | One-off preparation of a freshly seeded Wabi Bank: first sign-in password change for the eight sample users, read-only connection + test + approval for the five applications, the video's language (`WI_DEMO_LANGUAGE`, `en` or `ar`) as every user's preferred language, one KYC look-up and the monitoring rules made due (so the planted alerts exist). |
| `tts.py` | Narration, one WAV per line plus `durations.json`. English: [Piper](https://github.com/rhasspy/piper) (offline neural TTS, MIT) with the `en_US-bryce-medium` male voice (trained on a public-domain dataset, so the narration may be used commercially), at the rate set by `lengthScale`. Arabic: the Azure AI Speech neural voice `ar-AE-HamdanNeural` (UAE, male; licensed for commercial use under the Azure terms) through the text-to-speech REST API of the Speech resource's region, key from `AZURE_SPEECH_KEY`. |
| `graphics/` | HTML title cards (intro, industries, how it works, Wabi Bank, generated files, outro), animated line by line by `stage.js`; `graphics/ar/` holds the Arabic pages (right to left, IBM Plex Sans Arabic like the app), used over the English ones for `--lang ar`. |
| `record.js` | Playwright recorder. Signs in as each sample user, plays every scene (cursor, clicks, typing, spotlight, chapter label, callouts) and records frames with the Chrome DevTools screencast. Narration lines are paced from `durations.json`; waits for the AI are marked as cuts. `--lang` picks the script, the interface language, and the questions, typed text and callouts (`TEXT` in the file). |
| `compose.py` | Rebuilds each scene without the cuts, places the narration, adds a music bed generated in the script (no third-party music), click and transition sounds, crossfades the scenes, and writes the MP4, SRT and the timecoded script. |

## Run it

Prerequisites: SQL Server with the Wabi Bank sample (`scripts/sql/samples/wabi-bank/01`–`06`) on a migrated and
bootstrapped Wabi Insight, an AI provider configured for the organization, the API on `http://localhost:5080` and the
web app on `http://localhost:4200` (`npm start`); Node with Playwright; Python 3 with numpy; ffmpeg (libx264, aac),
`pdftoppm` (poppler) and LibreOffice (`soffice`, for the Word/Excel previews).

```bash
cd scripts/demo/walkthrough
OUT=out/en   # git-ignored work folder: audio, frames, clips

# 1. once per fresh database
WI_DEMO_PASSWORD='<new password for the sample users>' \
WI_SQLCMD="sqlcmd -S localhost,1433 -U sa -P '<sa password>' -C -d WabiInsightDB" node prepare.mjs

# 2. narration (only changed lines are re-synthesized)
python3 -m venv .venv && .venv/bin/pip install piper-tts
mkdir -p voices && for f in en_US-bryce-medium.onnx en_US-bryce-medium.onnx.json; do
  curl -L -o voices/$f https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/bryce/medium/$f; done
.venv/bin/python tts.py --script script.en.json --voices voices --out $OUT

# 3. record every scene (about 25 minutes: the AI answers run live; re-record one scene with --only <id>)
WI_DEMO_PASSWORD='<same password>' node record.js --out $OUT

# 4. compose
python3 compose.py --script script.en.json --work $OUT --video ../../../docs/demo/wabi-insight-walkthrough-en.mp4
```

Notes:

- Scenes depend on earlier ones in the same database: *history*, *share* and *periodic* open the analyses asked in
  *ask* and *study*; the *files* card shows the PDF, Word and Excel files downloaded in *study* and *history*.
- *alerts* acknowledges the cash-deposit alert. To record it again, reopen that alert first (or re-run step 1 on a
  fresh database).
- `compose.py --only intro,admin` builds a quick preview of some scenes; `--plan` prints the timing of every scene
  without encoding; `--reuse-clips` keeps the scene clips of an earlier run (to change only the final quality, `--crf`).
- The narration can change without recording again: `compose.py` re-times each scene to the current `durations.json`
  (idle stretches between lines are sped up, and the picture holds still where a line is longer than the recorded gap).
- *periodic* removes Victor's earlier "Weekly cheque returns for the board" report first (names are unique per owner).
- The sign-in rate limit (10 per minute) is respected: the recorder keeps one signed-in browser per user.

## Arabic version

The same steps with the Arabic script, on a fresh database prepared in Arabic (scenes acknowledge an alert and create
analyses, so record each language on its own fresh copy of the sample):

```bash
OUT=out/ar
WI_DEMO_LANGUAGE=ar WI_DEMO_PASSWORD='<password>' WI_SQLCMD="sqlcmd …" node prepare.mjs
AZURE_SPEECH_KEY='<Azure Speech key>' python3 tts.py --script script.ar.json --out $OUT
WI_DEMO_PASSWORD='<password>' node record.js --lang ar --out $OUT
python3 compose.py --script script.ar.json --work $OUT --video ../../../docs/demo/wabi-insight-walkthrough-ar.mp4
```

The Arabic narration needs an Azure AI Speech resource (the free F0 tier covers it) in the region named in
`script.ar.json` (`tts.region`, UAE North). The Arabic video's "other language" chapter switches the interface to
English and asks in English. The scenes are recorded independently of the voice, so the narration can be regenerated
(another voice, rate or wording) and the video composed again without recording.
