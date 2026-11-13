#!/usr/bin/env python3
"""Wordmarks of Wabi Insight: the brand mark with the name and tagline, English and Arabic, dark and light.

English: the mark on the left, "Wabi Insight" and the tagline on its right. Arabic: mirrored for a right-to-left
reader, the mark on the right, the Arabic name and tagline on its left. Same canvas (340 x 72), mark and gap in
both; in both the two lines are equally long (English by the tagline's size, Arabic by short kashidas).
The text is drawn as outlines (Inter and IBM Plex Sans Arabic, the application's typefaces), so the files look the
same everywhere: browsers, presentations, video, print. Writes into src/web/public/brand:
wabi-insight-wordmark-{dark,light}.svg, wabi-insight-wordmark-{dark,light}_ar.svg and, with --png, 8x PNG copies
(2720 x 576, transparent).

    pip install uharfbuzz fonttools brotli
    python3 scripts/brand/build_wordmarks.py [--png]     # needs src/web/node_modules (the fonts); --png needs Playwright
"""
import argparse, os, re, subprocess, sys
import uharfbuzz as hb
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
BRAND = os.path.join(ROOT, 'src', 'web', 'public', 'brand')
FONTSOURCE = os.path.join(ROOT, 'src', 'web', 'node_modules', '@fontsource')
ARABIC = os.path.join(FONTSOURCE, 'ibm-plex-sans-arabic', 'files', 'ibm-plex-sans-arabic-arabic-{}-normal.woff2')
LATIN = os.path.join(FONTSOURCE, 'inter', 'files', 'inter-latin-{}-normal.woff2')
COLOURS = {'dark': ('#0B1F3A', '#087DF2'), 'light': ('#FFFFFF', '#22B8CF')}      # name, tagline
EN_NAME, EN_TAGLINE = 'Wabi Insight', 'ASK YOUR DATA. TRUST THE ANSWER.'
EN_NAME_SIZE, EN_NAME_BASE, EN_TAG_SIZE, EN_TAG_BASE, EN_TAG_TRACKING = 33, 39, 12.5, 58, 0.03      # tracking in em; the tagline's size is then fitted to the name
NAME, TAGLINE = 'وابي انسايت', 'اسأل بياناتك، وثق بالإجابة'
# Places where the tagline may take a kashida (ـ) so that it is as long as the name: after these letter pairs, in this order.
KASHIDA = ['اس|أل', 'وث|ق', 'بالإج|ابة', 'بي|اناتك', 'بيان|اتك']
MAX_KASHIDA = 5       # kashida glyphs per place (one is about 0.12 em in this typeface): lengthened, not stretched
WIDTH, HEIGHT, MARK, MARGIN, GAP = 340, 72, 64, 4, 16      # the English wordmark: mark at x 4..68, text from x 84
NAME_SIZE, NAME_BASE, TAG_SIZE, TAG_BASE = 28, 37.5, 13, 58      # the name is as wide as the English name (about 172)


def outline(text, weight, size, right, baseline, family=ARABIC, tracking=0.0, left=None):
    """SVG path of the shaped `text`, its right edge at `right` (or its left edge at `left`); returns (path, width)."""
    file = family.format(weight)
    font = TTFont(file)
    ttf = file.replace('.woff2', '.ttf.tmp')
    font.flavor = None
    font.save(ttf)
    face = hb.Face(open(ttf, 'rb').read())
    os.remove(ttf)
    hbfont = hb.Font(face)
    buf = hb.Buffer()
    buf.add_str(text)
    buf.guess_segment_properties()
    hb.shape(hbfont, buf)
    scale = size / face.upem
    track = tracking * size
    advance = sum(p.x_advance for p in buf.glyph_positions) * scale + track * (len(buf.glyph_positions) - 1)
    glyphs, order = font.getGlyphSet(), font.getGlyphOrder()
    pen = SVGPathPen(glyphs, ntos=lambda v: f'{v:.2f}'.rstrip('0').rstrip('.'))
    x = left if left is not None else right - advance
    for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
        t = TransformPen(pen, (scale, 0, 0, -scale, x + pos.x_offset * scale, baseline - pos.y_offset * scale))
        glyphs[order[info.codepoint]].draw(t)
        x += pos.x_advance * scale + track
    return pen.getCommands(), advance


def tagline(target):
    """The tagline with kashidas added one at a time until it is about as long as `target`; returns (text, size)."""
    counts = [0] * len(KASHIDA)
    def text():
        words = TAGLINE.split(' ')
        for i, word in enumerate(words):
            # insert from the end of the word backwards, so earlier positions stay valid
            for place, n in sorted(zip(KASHIDA, counts), key=lambda x: -len(x[0].split('|')[0])):
                before, after = place.split('|')
                if word.startswith(before + after):
                    word = word[:len(before)] + 'ـ' * n + word[len(before):]
            words[i] = word
        return ' '.join(words)
    width = lambda: outline(text(), 500, TAG_SIZE, 0, 0)[1]
    step = 0
    while width() < target and step < len(KASHIDA) * MAX_KASHIDA:
        counts[step % len(KASHIDA)] += 1
        if width() > target * 1.02:          # one too many: take it back and stop
            counts[step % len(KASHIDA)] -= 1
            break
        step += 1
    return text(), TAG_SIZE * target / width()   # the last few per cent by size


def mark_of(theme):
    """The drawing of the mark and its gradients, kept in the English wordmark file."""
    english = open(os.path.join(BRAND, f'wabi-insight-wordmark-{theme}.svg'), encoding='utf-8').read()
    defs = re.search(r'<defs>.*?</defs>', english, re.S).group(0)
    mark = re.search(r'<g transform="translate\(4 4\)">(.*?)\n  </g>', english, re.S).group(1)
    return defs, mark


def svg(theme, label, note, mark_x, name, tag):
    defs, mark = mark_of(theme)
    name_fill, tag_fill = COLOURS[theme]
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<!-- Wabi Insight (AP-AIR) brand mark: lighthouse, data waves, connectivity marks. Colours: Navy #0B1F3A, Ocean #087DF2, Aqua #22B8CF.
     {note} Generated by scripts/brand/build_wordmarks.py; edit the script, not the text outlines. -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {WIDTH} {HEIGHT}" width="{WIDTH}" height="{HEIGHT}" role="img" aria-label="{label}">
  {defs}
  <g transform="translate({mark_x} {MARGIN})">{mark}
  </g>
  <path d="{name}" fill="{name_fill}"/>
  <path d="{tag}" fill="{tag_fill}"/>
</svg>
'''


def build_english(theme):
    left = MARGIN + MARK + GAP
    name, name_width = outline(EN_NAME, 700, EN_NAME_SIZE, None, EN_NAME_BASE, LATIN, -0.017, left)
    natural = outline(EN_TAGLINE, 500, EN_TAG_SIZE, None, EN_TAG_BASE, LATIN, EN_TAG_TRACKING, left)[1]
    tag_size = EN_TAG_SIZE * name_width / natural
    tag, tag_width = outline(EN_TAGLINE, 500, tag_size, None, EN_TAG_BASE, LATIN, EN_TAG_TRACKING, left)
    assert left + name_width <= WIDTH - MARGIN, 'the text does not fit the canvas'
    print(f'{theme} en: name {name_width:.1f} wide at {EN_NAME_SIZE}; tagline {tag_width:.1f} wide at {tag_size:.2f}')
    return svg(theme, 'Wabi Insight — Ask Your Data. Trust the Answer.', 'English wordmark: the mark on the left, the name and tagline (outlined, Inter) on its right, equally long.', MARGIN, name, tag)


def build_arabic(theme):
    right = WIDTH - MARGIN - MARK - GAP
    name, name_width = outline(NAME, 700, NAME_SIZE, right, NAME_BASE)
    tag_text, tag_size = tagline(name_width)
    tag, tag_width = outline(tag_text, 500, tag_size, right, TAG_BASE)
    assert name_width <= right - MARGIN, 'the text does not fit the canvas'
    print(f'{theme} ar: name {name_width:.1f} wide at {NAME_SIZE}; tagline "{tag_text}" {tag_width:.1f} wide at {tag_size:.2f}')
    return svg(theme, f'{NAME} — {TAGLINE}', 'Arabic wordmark: the mark on the right, the name and tagline (outlined, IBM Plex Sans Arabic) on its left, equally long.', WIDTH - MARGIN - MARK, name, tag)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--png', action='store_true', help='also write 8x PNG copies with a transparent background')
    args = parser.parse_args()
    for theme, suffix, build in [(t, x, b) for t in ('dark', 'light') for x, b in (('', build_english), ('_ar', build_arabic))]:
        file = os.path.join(BRAND, f'wabi-insight-wordmark-{theme}{suffix}.svg')
        content = build(theme)
        open(file, 'w', encoding='utf-8').write(content)
        if args.png:
            png = file.replace('.svg', '.png')
            js = ("const {chromium}=require('playwright');(async()=>{const b=await chromium.launch();"
                  f"const p=await b.newPage({{viewport:{{width:{WIDTH},height:{HEIGHT}}},deviceScaleFactor:8}});"
                  "await p.goto('file://'+process.argv[1]);await p.screenshot({path:process.argv[2],omitBackground:true});await b.close();})()")
            subprocess.run(['node', '-e', js, file, png], check=True)


if __name__ == '__main__':
    main()
