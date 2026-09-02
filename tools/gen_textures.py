#!/usr/bin/env python
"""
Shape Projector texture generator (Curator, spec section 7).

Generates the 16x16 block textures under
  src/ShapeProjector/assets/shapeprojector/textures/block/projector/
from a palette SAMPLED from the vanilla Vintage Story 1.22.7 Jonastech textures
that docs/api-notes.md section e.2 cites (riftward / jonaslens shape texture lists).
No hue is introduced that those textures do not already contain; every colour is
either a sampled value or a linear mix of two sampled values.

Run (from the repo root, any cwd works):
    python tools/gen_textures.py
Optional: --vs <path to Vintagestory install> to re-sample the palette from the
PNGs instead of using the checked-in sampled values (prints the values it finds).

Requires Pillow (tested with Pillow 12.1.1, Python 3.14).
"""
import argparse
import math
import os
import random
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "src", "ShapeProjector", "assets", "shapeprojector",
    "textures", "block", "projector"))

SIZE = 16
SEED = 1922  # deterministic grain

# ---------------------------------------------------------------------------
# Palette sources. Values were read from the vanilla 32x32 PNGs on 2026-09-01
# with sample_palette() below (luminance-sorted darkest / median / brightest,
# plus alpha).  Paths are relative to <VS install>/assets/survival/textures/.
# All of these textures are referenced by the vanilla Jonastech shapes cited in
# api-notes e.2 (block/machine/riftward.json, ward/small-ground.json,
# machine/jonas/jonaslens-aged.json) or listed there directly.
# ---------------------------------------------------------------------------
SOURCES = {
    # riftward.json "iron1" - the muted warm grey of the Jonastech chassis sheet
    "iron1":       ("block/metal/sheet/iron1.png",
                    {"dark": "#423b38", "mid": "#655c57", "light": "#817975", "alpha": 255}),
    # api-notes e.2 directory listing: metal/ingot/cupronickel.png (warm tan)
    "cupro_ingot": ("block/metal/ingot/cupronickel.png",
                    {"dark": "#684c37", "mid": "#a08672", "light": "#d7b097", "alpha": 255}),
    # riftward.json "device-plate" - the dark Jonastech base/chassis plate
    "deviceplate": ("block/machine/device-plate.png",
                    {"dark": "#2e211b", "mid": "#4f392b", "light": "#6e543c", "alpha": 255}),
    # riftward.json / ward "brass" - dull gold
    "brass_ingot": ("block/metal/ingot/brass.png",
                    {"dark": "#311600", "mid": "#7f642b", "light": "#aa8f56", "alpha": 255}),
    # riftward.json "diamond" - the riftward gem, pale cyan (renderPass 3 element)
    "diamond":     ("block/stone/gem/diamond.png",
                    {"dark": "#94c2cc", "mid": "#e4fafa", "light": "#fbfbfb", "alpha": 230}),
    # jonaslens-aged.json "quartz" - the Jonastech lens glass (alpha reference)
    "glass_quartz": ("block/glass/quartz.png",
                    {"dark": "#7a6f6a", "mid": "#bfb4af", "light": "#ffffff", "alpha": 89}),
    # riftward.json / ward / jonaslens "rustyglow" - the Jonastech emissive (glow 100)
    "rustyglow":   ("block/machine/statictranslocator/rustyglow.png",
                    {"dark": "#007c46", "mid": "#18c897", "light": "#47ffc6", "alpha": 255}),
}


def hex2rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgb2hex(c):
    return "#%02x%02x%02x" % tuple(int(round(v)) for v in c[:3])


def mix(a, b, t):
    """Linear mix of two rgb tuples, t in [0,1] -> a*(1-t) + b*t."""
    return tuple(a[i] * (1 - t) + b[i] * t for i in range(3))


def clamp(v):
    return max(0, min(255, int(round(v))))


def ramp(dark, mid, light):
    return {"dark": hex2rgb(dark), "mid": hex2rgb(mid), "light": hex2rgb(light)}


def mix_ramp(r1, r2, t):
    return {k: mix(r1[k], r2[k], t) for k in ("dark", "mid", "light")}


def src_ramp(name):
    d = SOURCES[name][1]
    return ramp(d["dark"], d["mid"], d["light"])


# ---------------------------------------------------------------------------
# Derived palette (every entry is a sampled value or a mix of two sampled values)
# ---------------------------------------------------------------------------
def build_palette():
    iron1 = src_ramp("iron1")
    cupro = src_ramp("cupro_ingot")
    plate = src_ramp("deviceplate")
    brass = src_ramp("brass_ingot")
    diamond = src_ramp("diamond")
    glow = src_ramp("rustyglow")

    return {
        # muted warm grey: chassis sheet warmed 35% toward the cupronickel ingot
        "cupronickel":      mix_ramp(iron1, cupro, 0.35),
        # dark cupronickel base plate: device-plate and the dark end of iron1, 50/50
        "cupronickel-dark": mix_ramp(plate, iron1, 0.50),
        # dull gold, straight from the brass ingot
        "brass":            brass,
        # light cyan glass: diamond gem colours (alpha chosen below)
        "glass":            diamond,
        # emissive: rustyglow as-is (Jonastech's own glow hue)
        "emissive":         glow,
        # unlit tube: rustyglow dimmed 65% toward the device plate
        "emissive-off":     mix_ramp(glow, plate, 0.65),
    }


# Alpha for the glass: between the quartz lens pane (89) and the diamond gem (230).
GLASS_ALPHA = 150


# ---------------------------------------------------------------------------
# Painters
# ---------------------------------------------------------------------------
def grain(rng, r, amount=0.18):
    """Pick a colour on the dark..mid..light ramp with gaussian jitter around mid."""
    t = rng.gauss(0.0, amount)
    t = max(-1.0, min(1.0, t))
    if t < 0:
        return mix(r["mid"], r["dark"], -t)
    return mix(r["mid"], r["light"], t)


def paint_metal(r, rng, streak=0.0, amount=0.18):
    im = Image.new("RGBA", (SIZE, SIZE))
    px = im.load()
    for y in range(SIZE):
        # optional horizontal brushing: rows share a bias
        row_bias = rng.gauss(0.0, streak) if streak else 0.0
        for x in range(SIZE):
            c = grain(rng, r, amount)
            if row_bias:
                c = mix(c, r["light"] if row_bias > 0 else r["dark"], min(1.0, abs(row_bias)))
            px[x, y] = (clamp(c[0]), clamp(c[1]), clamp(c[2]), 255)
    return im


def paint_baseplate(r, rng):
    im = paint_metal(r, rng, amount=0.14)
    px = im.load()
    # a 1px darker rim and four lighter rivets, in the device-plate manner
    for i in range(SIZE):
        for (x, y) in ((i, 0), (i, SIZE - 1), (0, i), (SIZE - 1, i)):
            c = mix(px[x, y][:3], r["dark"], 0.5)
            px[x, y] = (clamp(c[0]), clamp(c[1]), clamp(c[2]), 255)
    for (x, y) in ((2, 2), (13, 2), (2, 13), (13, 13)):
        c = r["light"]
        px[x, y] = (clamp(c[0]), clamp(c[1]), clamp(c[2]), 255)
    return im


def paint_glass(r, rng):
    im = Image.new("RGBA", (SIZE, SIZE))
    px = im.load()
    cx = cy = (SIZE - 1) / 2.0
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - cx, y - cy) / (SIZE / 2.0)  # 0 centre .. ~1 edge
            # pale core, cooler cyan toward the rim (diamond's own dark end)
            c = mix(r["mid"], r["dark"], min(1.0, d * 0.8))
            # a soft specular arc top-left
            spec = max(0.0, 1.0 - math.hypot(x - 5, y - 5) / 4.0)
            c = mix(c, r["light"], spec * 0.7)
            c = mix(c, grain(rng, r, 0.06), 0.3)
            a = GLASS_ALPHA + int(round(30 * d))  # slightly denser at the rim
            px[x, y] = (clamp(c[0]), clamp(c[1]), clamp(c[2]), clamp(a))
    return im


def paint_emissive(r, rng, core=True):
    im = Image.new("RGBA", (SIZE, SIZE))
    px = im.load()
    cx = cy = (SIZE - 1) / 2.0
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - cx, y - cy) / (SIZE / 2.0)
            if core:
                c = mix(r["light"], r["mid"], min(1.0, d))       # bright core, mid rim
            else:
                c = mix(r["mid"], r["dark"], min(1.0, d * 0.6))  # dull, darker rim
            c = mix(c, grain(rng, r, 0.10), 0.35)
            px[x, y] = (clamp(c[0]), clamp(c[1]), clamp(c[2]), 255)
    return im


def generate(out_dir):
    pal = build_palette()
    rng = random.Random(SEED)
    os.makedirs(out_dir, exist_ok=True)

    files = {
        "cupronickel.png":      paint_metal(pal["cupronickel"], rng, streak=0.10, amount=0.16),
        "cupronickel-dark.png": paint_baseplate(pal["cupronickel-dark"], rng),
        "brass.png":            paint_metal(pal["brass"], rng, streak=0.22, amount=0.14),
        "glass.png":            paint_glass(pal["glass"], rng),
        "emissive.png":         paint_emissive(pal["emissive"], rng, core=True),
        "emissive-off.png":     paint_emissive(pal["emissive-off"], rng, core=False),
    }
    for name, im in files.items():
        path = os.path.join(out_dir, name)
        im.save(path, "PNG", optimize=True)
        print("wrote", path)

    print("\nPalette (dark / mid / light):")
    for k, r in pal.items():
        print("  %-17s %s %s %s" % (k, rgb2hex(r["dark"]), rgb2hex(r["mid"]), rgb2hex(r["light"])))
    print("  glass alpha %d..%d" % (GLASS_ALPHA, GLASS_ALPHA + 30))


def sample_palette(vs_root):
    """Re-read the vanilla PNGs and print darkest/median/brightest + alpha."""
    base = os.path.join(vs_root, "assets", "survival", "textures")
    for key, (rel, cached) in SOURCES.items():
        path = os.path.join(base, rel)
        im = Image.open(path).convert("RGBA")
        px = [im.getpixel((x, y)) for y in range(im.height) for x in range(im.width)]
        opaque = [p for p in px if p[3] > 0]
        lum = sorted(opaque, key=lambda p: 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2])
        found = {"dark": rgb2hex(lum[0]), "mid": rgb2hex(lum[len(lum) // 2]),
                 "light": rgb2hex(lum[-1]), "alpha": max(p[3] for p in px)}
        flag = "" if found == cached else "   <-- differs from checked-in values"
        print("%-13s %-50s %s%s" % (key, rel, found, flag))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--vs", default=None,
                    help="Vintage Story install folder; re-sample the vanilla palette and print it")
    args = ap.parse_args()
    if args.vs:
        sample_palette(args.vs)
        sys.exit(0)
    generate(args.out)
