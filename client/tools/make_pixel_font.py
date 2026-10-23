"""DotGothic16 rebuilt dot for dot, for the Pixel: DotGothic16 font option.

    python client/tools/make_pixel_font.py DotGothic16-Regular.ttf

DotGothic16 draws each dot as a separate small square with gaps between them. Windows' GDI, drawing it without
anti-aliasing (as a pixel font must be), fills the gaps in dense kanji and 魔's top half becomes a solid block. FreeType
draws it right at 16 px; this takes that bitmap and makes a TrueType font of it with every dot a whole pixel's square
(unitsPerEm 1024, 64 units a pixel), which GDI then draws exactly. Writes
client/src/BreezySRPG95/fonts/DotGothic16Pixel.ttf, family "DotGothic16 Pixel", under DotGothic16's licence (SIL OFL 1.1).

Needs Pillow and fontTools (pip install pillow fonttools).
"""
import os
import sys

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from PIL import ImageFont

PX = 64            # font units a pixel
SIZE = 16          # the pixel size it's drawn at
FAMILY = 'DotGothic16 Pixel'
OUT = os.path.join(os.path.dirname(__file__), '..', 'src', 'BreezySRPG95', 'fonts', 'DotGothic16Pixel.ttf')


def bitmaps(path):
    """{codepoint: (advance px, [(x, y) dots, y up, 0 the row just above the baseline])}, the highest and lowest row."""
    font = ImageFont.truetype(path, SIZE)
    glyphs, top, bottom = {}, 0, 0
    for cp in sorted(TTFont(path, lazy=True).getBestCmap()):
        if cp < 0x20:
            continue
        ch = chr(cp)
        mask, (ox, oy) = font.getmask2(ch, mode='1', anchor='ls')
        w, h = mask.size
        dots = [(ox + x, -(oy + y) - 1) for y in range(h) for x in range(w) if mask.getpixel((x, y))]
        for _, y in dots:
            top, bottom = max(top, y + 1), min(bottom, y)
        glyphs[cp] = (round(font.getlength(ch)), dots)
    return glyphs, top, -bottom


def build(glyphs, ascent, descent, out):
    name = lambda cp: 'uni%04X' % cp if cp <= 0xFFFF else 'u%05X' % cp
    fb = FontBuilder(SIZE * PX, isTTF=True)
    fb.setupGlyphOrder(['.notdef'] + [name(cp) for cp in sorted(glyphs)])
    fb.setupCharacterMap({cp: name(cp) for cp in glyphs})
    glyf, metrics = {'.notdef': TTGlyphPen(None).glyph()}, {'.notdef': (8 * PX, 0)}
    for cp, (adv, dots) in glyphs.items():
        pen = TTGlyphPen(None)
        rows = {}
        for x, y in dots:
            rows.setdefault(y, []).append(x)
        for y, xs in rows.items():
            xs.sort()
            start = prev = xs[0]
            for x in xs[1:] + [None]:   # each run of dots in a row as one rectangle, clockwise
                if x is not None and x == prev + 1:
                    prev = x
                    continue
                x0, x1, y0, y1 = start * PX, (prev + 1) * PX, y * PX, (y + 1) * PX
                pen.moveTo((x0, y0)); pen.lineTo((x0, y1)); pen.lineTo((x1, y1)); pen.lineTo((x1, y0)); pen.closePath()
                if x is not None:
                    start = prev = x
        glyf[name(cp)] = pen.glyph()
        metrics[name(cp)] = (adv * PX, min((x for x, _ in dots), default=0) * PX)
    fb.setupGlyf(glyf)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=ascent * PX, descent=-descent * PX)
    fb.setupNameTable({
        'familyName': FAMILY, 'styleName': 'Regular', 'fullName': FAMILY, 'psName': 'DotGothic16Pixel-Regular',
        'uniqueFontIdentifier': FAMILY + ' Regular',
        'copyright': 'Copyright 2020 The DotGothic16 Project Authors (https://github.com/fontworks-fonts/DotGothic16/). '
                     'Modified: drawn at 16 px and rebuilt with a square per dot, for BreezySRPG95.',
        'licenseDescription': 'This Font Software is licensed under the SIL Open Font License, Version 1.1.',
        'licenseInfoURL': 'https://openfontlicense.org',
    })
    fb.setupOS2(sTypoAscender=ascent * PX, sTypoDescender=-descent * PX, sTypoLineGap=0, usWinAscent=ascent * PX,
                usWinDescent=descent * PX, ulCodePageRange1=(1 << 17) | 1, fsType=0)
    fb.setupPost()
    fb.save(out)


if __name__ == '__main__':
    glyphs, ascent, descent = bitmaps(sys.argv[1])
    build(glyphs, ascent, descent, os.path.normpath(OUT))
    print(os.path.normpath(OUT), len(glyphs), 'glyphs')
