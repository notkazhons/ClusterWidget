"""Builds an animated GIF from the frames recorded by record-demo.ps1.

    python tools/make_gif.py <frames-folder> <out.gif> [scale]

Identical consecutive frames are merged (their delays add up), so the still parts cost almost nothing.
"""
import os
import sys

from PIL import Image, ImageChops

src, out = sys.argv[1], sys.argv[2]
scale = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
times = [int(t) for t in open(os.path.join(src, "times.txt")).read().strip().split(",")]
names = sorted(n for n in os.listdir(src) if n.endswith(".png"))

frames, delays = [], []
prev = None
for i, name in enumerate(names):
    img = Image.open(os.path.join(src, name)).convert("RGB")
    if scale != 1.0:
        img = img.resize((int(img.width * scale), int(img.height * scale)), Image.LANCZOS)
    dur = (times[i + 1] - times[i]) if i + 1 < len(times) else 800
    if prev is not None and ImageChops.difference(img, prev).getbbox() is None:
        delays[-1] += dur  # same picture as before: just hold it longer
    else:
        frames.append(img)
        delays.append(dur)
    prev = img

# a shared palette keeps colours steady between frames
palette_src = frames[len(frames) // 2].quantize(colors=255, method=Image.Quantize.MEDIANCUT)
paletted = [f.quantize(palette=palette_src, dither=Image.Dither.NONE) for f in frames]
# GIF delays are in 1/100 s and most viewers clamp very short ones, so keep a floor of 20 ms
delays = [max(20, d) for d in delays]
delays[-1] = max(delays[-1], 1500)  # rest on the last frame before it loops
paletted[0].save(out, save_all=True, append_images=paletted[1:], duration=delays, loop=0, optimize=False, disposal=1)
print(f"{len(frames)} frames, {sum(delays) / 1000:.1f} s, {os.path.getsize(out) / 1024:.0f} KB -> {out}")
