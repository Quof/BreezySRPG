"""Pull still frames out of a screen recording of the original game, for measuring timing and layout.

    python re/tools/frames.py VIDEO [--start S] [--end S] [--fps N] [--out DIR] [--crop W:H:X:Y] [--sheet]

Frames land in DIR (default: next to the video, in VIDEO_frames/) as 0001.png, 0002.png, ...
with their timestamps in index.txt. --sheet also writes contact sheets (sheet_01.png, ...) of
labelled thumbnails, 6 x 4 per sheet. --crop takes ffmpeg's crop=W:H:X:Y, e.g. to cut the game
window out of a full-desktop recording.

ffmpeg is found on PATH, else ShareX's copy (C:\\Program Files\\ShareX\\ffmpeg.exe).
"""
import argparse
import os
import shutil
import subprocess
import sys

SHAREX = r'C:\Program Files\ShareX\ffmpeg.exe'


def find_ffmpeg():
    exe = shutil.which('ffmpeg')
    if exe:
        return exe
    if os.path.exists(SHAREX):
        return SHAREX
    sys.exit('ffmpeg not found: install it (winget install Gyan.FFmpeg) or put it on PATH')


def extract(video, out, start, end, fps, crop):
    os.makedirs(out, exist_ok=True)
    filters = []
    if crop:
        filters.append(f'crop={crop}')
    filters.append(f'fps={fps}')
    cmd = [find_ffmpeg(), '-hide_banner', '-loglevel', 'error', '-y']
    if start:
        cmd += ['-ss', str(start)]
    cmd += ['-i', video]
    if end:
        cmd += ['-t', str(float(end) - float(start or 0))]
    cmd += ['-vf', ','.join(filters), os.path.join(out, '%04d.png')]
    subprocess.run(cmd, check=True)
    frames = sorted(f for f in os.listdir(out) if f[:4].isdigit() and f.endswith('.png'))
    with open(os.path.join(out, 'index.txt'), 'w', encoding='utf-8') as f:
        for i, name in enumerate(frames):
            f.write(f'{name}\t{float(start or 0) + i / fps:.3f}s\n')
    return frames


def sheets(out, frames, start, fps, cols=6, rows=4, width=320):
    from PIL import Image, ImageDraw
    per = cols * rows
    for s in range(0, len(frames), per):
        batch = frames[s:s + per]
        first = Image.open(os.path.join(out, batch[0]))
        h = first.height * width // first.width
        sheet = Image.new('RGB', (cols * width, rows * (h + 14)), (40, 40, 40))
        draw = ImageDraw.Draw(sheet)
        for k, name in enumerate(batch):
            im = Image.open(os.path.join(out, name)).convert('RGB').resize((width, h))
            x, y = (k % cols) * width, (k // cols) * (h + 14)
            sheet.paste(im, (x, y + 14))
            draw.text((x + 2, y), f'{name}  {float(start or 0) + (s + k) / fps:.2f}s', fill=(255, 255, 0))
        sheet.save(os.path.join(out, f'sheet_{s // per + 1:02}.png'))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('video')
    ap.add_argument('--start', type=float, default=0)
    ap.add_argument('--end', type=float)
    ap.add_argument('--fps', type=float, default=10)
    ap.add_argument('--out')
    ap.add_argument('--crop')
    ap.add_argument('--sheet', action='store_true')
    a = ap.parse_args()
    out = a.out or os.path.splitext(a.video)[0] + '_frames'
    frames = extract(a.video, out, a.start, a.end, a.fps, a.crop)
    print(f'{len(frames)} frames -> {out}')
    if a.sheet and frames:
        sheets(out, frames, a.start, a.fps)
        print('contact sheets written')


if __name__ == '__main__':
    main()
