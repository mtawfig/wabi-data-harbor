#!/usr/bin/env python3
"""Arabic wordmark of Wabi Insight: the brand mark on the right, the Arabic name and tagline on its left.

Same canvas (340 x 72), mark, gap and baselines as the English wordmark (wabi-insight-wordmark-{dark,light}.svg),
mirrored for a right-to-left reader. The text is drawn as outlines (IBM Plex Sans Arabic, the application's Arabic
typeface), so the files look the same everywhere: browsers, presentations, video, print. Writes into
src/web/public/brand: wabi-insight-wordmark-{dark,light}_ar.svg and, with --png, 8x PNG copies (2720 x 576).

    pip install uharfbuzz fonttools brotli
    python3 scripts/brand/build_arabic_wordmark.py [--png]     # --png needs Node with Playwright (npm ci in src/web is enough for the fonts)
"""
import argparse, os, re, subprocess, sys
import uharfbuzz as hb
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
BRAND = os.path.join(ROOT, 'src', 'web', 'public', 'brand')
FONTS = os.path.join(ROOT, 'src', 'web', 'node_modules', '@fontsource', 'ibm-plex-sans-arabic', 'files')
NAME, TAGLINE = 'وابي انسايت', 'اسأل بياناتك، وثق بالإجابة'
# Places where the tagline may take a kashida (ـ) so that it is as long as the name: after these letter pairs, in this order.
KASHIDA = ['اس|أل', 'وث|ق', 'بالإج|ابة', 'بي|اناتك', 'بيان|اتك']
MAX_KASHIDA = 5       # kashida glyphs per place (one is about 0.12 em in this typeface): lengthened, not stretched
WIDTH, HEIGHT, MARK, MARGIN, GAP = 340, 72, 64, 4, 16      # the English wordmark: mark at x 4..68, text from x 84
NAME_SIZE, NAME_BASE, TAG_SIZE, TAG_BASE = 28, 37.5, 13, 58      # the name is as wide as the English name (about 172)


def outline(text, weight, size, right, baseline):
    """SVG path of `text` shaped right to left, its right edge at `right`; returns (path, width)."""
    file = os.path.join(FONTS, f'ibm-plex-sans-arabic-arabic-{weight}-normal.woff2')
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
    advance = sum(p.x_advance for p in buf.glyph_positions) * scale
    glyphs, order = font.getGlyphSet(), font.getGlyphOrder()
    pen = SVGPathPen(glyphs, ntos=lambda v: f'{v:.2f}'.rstrip('0').rstrip('.'))
    x = right - advance
    for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
        t = TransformPen(pen, (scale, 0, 0, -scale, x + pos.x_offset * scale, baseline - pos.y_offset * scale))
        glyphs[order[info.codepoint]].draw(t)
        x += pos.x_advance * scale
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


def build(theme):
    english = open(os.path.join(BRAND, f'wabi-insight-wordmark-{theme}.svg'), encoding='utf-8').read()
    defs = re.search(r'<defs>.*?</defs>', english, re.S).group(0)
    mark = re.search(r'<g transform="translate\(4 4\)">(.*?)\n  </g>\n  <text', english, re.S).group(1)
    name_fill, tag_fill = re.findall(r'<text [^>]*fill="(#[0-9A-Fa-f]+)"', english)
    right = WIDTH - MARGIN - MARK - GAP
    name, name_width = outline(NAME, 700, NAME_SIZE, right, NAME_BASE)
    tag_text, tag_size = tagline(name_width)
    tag, tag_width = outline(tag_text, 500, tag_size, right, TAG_BASE)
    print(f'{theme}: name {name_width:.1f} wide at {NAME_SIZE}; tagline "{tag_text}" {tag_width:.1f} wide at {tag_size:.2f}')
    assert max(name_width, tag_width) <= right - MARGIN, 'the text does not fit the canvas'
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<!-- Wabi Insight (AP-AIR) brand mark, Arabic wordmark: the mark on the right, the name and tagline (outlined, IBM Plex Sans Arabic) on its left.
     Generated by scripts/brand/build_arabic_wordmark.py from wabi-insight-wordmark-{theme}.svg; edit the script, not this file. -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {WIDTH} {HEIGHT}" width="{WIDTH}" height="{HEIGHT}" role="img" aria-label="{NAME} — {TAGLINE}">
  {defs}
  <g transform="translate({WIDTH - MARGIN - MARK} {MARGIN})">{mark}
  </g>
  <path d="{name}" fill="{name_fill}"/>
  <path d="{tag}" fill="{tag_fill}"/>
</svg>
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--png', action='store_true', help='also write 8x PNG copies with a transparent background')
    args = parser.parse_args()
    for theme in ('dark', 'light'):
        svg = os.path.join(BRAND, f'wabi-insight-wordmark-{theme}_ar.svg')
        open(svg, 'w', encoding='utf-8').write(build(theme))
        print(svg)
        if args.png:
            png = svg.replace('.svg', '.png')
            js = ("const {chromium}=require('playwright');(async()=>{const b=await chromium.launch();"
                  f"const p=await b.newPage({{viewport:{{width:{WIDTH},height:{HEIGHT}}},deviceScaleFactor:8}});"
                  "await p.goto('file://'+process.argv[1]);await p.screenshot({path:process.argv[2],omitBackground:true});await b.close();})()")
            subprocess.run(['node', '-e', js, svg, png], check=True)
            print(png)


if __name__ == '__main__':
    main()
