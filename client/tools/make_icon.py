"""BreezySRPG95's icon from a face graphic: the head on a dark navy tile with a thin gold rim (the gilded skin's look).

    python client/tools/make_icon.py "<game folder>/BMP/FACE_003.bmp" [x y size]

x, y, size: the square of the 128 x 128 face to use (default 8 0 112, scaled 2x). Writes client/src/BreezySRPG95/Icon.ico
(16-256 px, the exe's icon) and Icon.bmp (64 px, the window's). The current icon is 昴の騎士's FACE_003.
"""
import math
import os
import sys

from PIL import Image, ImageDraw

S, FACE, RADIUS, RIM = 256, 224, 44, 9


def keyed(path, box):
    """The face with its flat backdrop (the colour of the top-left pixel) made transparent, edges included."""
    im = Image.open(path).convert('RGB')
    bg = im.getpixel((0, 0))
    src, res = im.load(), Image.new('RGBA', im.size)
    out = res.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = src[x, y]
            d = math.dist((r, g, b), bg)
            a = 0 if d < 28 else 255 if d > 80 else int(255 * (d - 28) / 52)
            # the usual backdrop is pure green: green above red and blue at an edge is that backdrop showing through
            if a < 255 or g > max(r, b):
                g = min(g, max(r, b))
            out[x, y] = (r, g, b, a)
    return res.crop(box)


def backdrop():
    """Dark navy, lighter where the head is."""
    g = Image.new('RGBA', (S, S))
    p = g.load()
    for y in range(S):
        for x in range(S):
            t = min(1, math.dist((x, y), (S * 0.5, S * 0.38)) / (S * 0.75))
            p[x, y] = (int(46 - 30 * t), int(58 - 38 * t), int(110 - 70 * t), 255)
    return g


def gold_rim():
    """A rounded-square ring in a gold that brightens and darkens along the diagonal."""
    mask = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle((0, 0, S - 1, S - 1), RADIUS, fill=255)
    d.rounded_rectangle((RIM, RIM, S - 1 - RIM, S - 1 - RIM), RADIUS - RIM, fill=0)
    gold = Image.new('RGBA', (S, S))
    p = gold.load()
    for y in range(S):
        for x in range(S):
            k = 0.5 + 0.5 * math.cos((x + y) / (2 * S) * 3 * math.pi)
            p[x, y] = (int(150 + 105 * k), int(105 + 95 * k), int(30 + 70 * k), 255)
    rim = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    rim.paste(gold, (0, 0), mask)
    return rim


def icon(path, x, y, size):
    face = keyed(path, (x, y, x + size, y + size)).resize((FACE, FACE), Image.NEAREST)   # keep the pixels
    canvas = backdrop()
    canvas.alpha_composite(face, ((S - FACE) // 2, S - FACE))
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), RADIUS, fill=255)
    res = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    res.paste(canvas, (0, 0), mask)
    res.alpha_composite(gold_rim())
    return res


if __name__ == '__main__':
    x, y, size = (int(v) for v in sys.argv[2:5]) if len(sys.argv) >= 5 else (8, 0, 112)
    im = icon(sys.argv[1], x, y, size)
    out = os.path.join(os.path.dirname(__file__), '..', 'src', 'BreezySRPG95')
    im.save(os.path.join(out, 'Icon.ico'), sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
    im.resize((64, 64), Image.LANCZOS).save(os.path.join(out, 'Icon.bmp'))
