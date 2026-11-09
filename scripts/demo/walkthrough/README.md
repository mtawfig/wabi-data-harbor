# Product walkthrough video

Generates the Wabi Insight walkthrough video (English, about eight minutes) from the **running application** with the
Wabi Bank sample organization: real pages, real AI answers and real generated files, with narration, title graphics,
subtitles and chapters. The finished video and its script are in `docs/demo/`:

| File | What |
|---|---|
| `docs/demo/wabi-insight-walkthrough-en.mp4` | The video (1920×1080, H.264/AAC, English subtitle track and chapter markers) |
| `docs/demo/wabi-insight-walkthrough-en.srt` | English subtitles |
| `docs/demo/wabi-insight-walkthrough-en.md` | The narration script with the time of every scene and line |

The Platform Administrator is deliberately not shown: the video is for organizations evaluating the product.

## Files here

| File | Role |
|---|---|
| `script.en.json` | The script: scenes in order, chapter titles, what is on screen, the narration lines (one "beat" per line), voice and contact details. Edit this to change what is said. |
| `prepare.mjs` | One-off preparation of a freshly seeded Wabi Bank: first sign-in password change for the eight sample users, read-only connection + test + approval for the five applications, English as preferred language, one KYC look-up and the monitoring rules made due (so the planted alerts exist). |
| `tts.py` | Narration with [Piper](https://github.com/rhasspy/piper) (offline neural TTS, MIT) and the `en_US-bryce-medium` male voice (trained on a public-domain dataset, so the narration may be used commercially), at the rate set by `lengthScale` in the script. One WAV per line plus `durations.json`. |
| `graphics/` | HTML title cards (intro, industries, how it works, Wabi Bank, generated files, outro), animated line by line by `stage.js`. |
| `record.js` | Playwright recorder. Signs in as each sample user, plays every scene (cursor, clicks, typing, spotlight, chapter label, callouts) and records frames with the Chrome DevTools screencast. Narration lines are paced from `durations.json`; waits for the AI are marked as cuts. |
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

Copy `script.en.json` to `script.ar.json` and translate the chapters, screen notes and lines; pick an Arabic Piper
voice (for example `ar_JO-kareem-medium`) in `voice`; run `tts.py --script script.ar.json`. In `record.js`, the scenes
type English questions and keep the interface in English: give each scene its Arabic question and switch the users'
language to Arabic (`--lang ar` reads `script.ar.json`). The graphics are English HTML pages; add Arabic copies with
`dir="rtl"` and point the graphic scenes of `script.ar.json` at them.
