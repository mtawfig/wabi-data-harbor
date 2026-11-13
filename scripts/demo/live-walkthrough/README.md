# Walkthrough of the deployed site

A narrated walkthrough (16:9, 1920×1080, about ten minutes) of the **deployed** Wabi Insight site, recorded on a
desktop screen with a phone shown in a few scenes, for the sample organization GAS: about twenty seconds as the
organization administrator, then the management user through every organization-user feature. It closes with the
company's contact card and a QR code that saves the contact on a phone.

It reuses the generator of the product walkthrough ([`../walkthrough`](../walkthrough/README.md)): the same narration
script format, `tts.py` (Azure Speech) and `compose.py`. Only the recorder and the graphic pages are specific.

| File | Purpose |
|---|---|
| `script.ar.json` | Scenes, narration lines, voice (`ar-AE-FatimaNeural`), contact details of the closing frame |
| `record.js` | Drives the live site with Playwright and records every scene (frames + timeline) |
| `graphics/` | Opening frame (laptop + phone), security summary, hosting and assistance card, closing contact card |
| `make_qr.py` | The QR code: a vCard with the company name, mobile and e-mail (`pip install segno`) |

```bash
cd scripts/demo/live-walkthrough
OUT=/path/to/work
AZURE_SPEECH_KEY=… python3 ../walkthrough/tts.py --script script.ar.json --out $OUT
WI_DEMO_PASSWORD='<password of the two sample accounts>' node record.js --out $OUT --lang ar     # --only ask,how re-records scenes
python3 ../walkthrough/compose.py --script script.ar.json --work $OUT --video ../../../docs/demo/wabi-insight-live-ar.mp4
```

- `WI_BASE_URL` points the recorder at another deployment (default `https://insight.wabitex.com`); the sample
  organization GAS must be loaded there, with its applications connected.
- The recording leaves a few demo records in that organization: new answers, one more version of an answer, a
  favourite, a collection and a share between two sample users. Nothing is e-mailed and no rule or periodic report is
  saved.
- The recorder stops with an error if a page it records shows a word listed in `NEVER` (owner's instruction for this
  video): archive the history entry, or change the question, and record the scene again.
- The spoken form of a name can differ from the written one (`pronounce` in the script); subtitles keep the written form.
- Phone views are real phone-sized screenshots of the same site (390×844), shown in a frame over the desktop page.
