#!/usr/bin/env python
"""
Shape Projector block shape generator (Curator, spec section 7).

Writes two shape files from ONE cuboid list so the off/on variants can never
drift apart:
  src/ShapeProjector/assets/shapeprojector/shapes/block/projector.json     (ring unlit)
  src/ShapeProjector/assets/shapeprojector/shapes/block/projector-on.json  (ring glow 100)

Schema per docs/api-notes.md e.3 (Shape / ShapeElement / ShapeElementFace):
textureWidth/textureHeight, textures map, elements[] with name/from/to/faces
(uv in 1/16 units), rotationOrigin + rotationX/Y/Z, per-face "glow", per-element
"renderPass" (3 = EnumChunkRenderPass.Transparent, as the riftward "gem" element
and the jonaslens glass elements use).

Run:  python tools/gen_shape.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "src", "ShapeProjector", "assets", "shapeprojector", "shapes", "block"))

TEX_ROOT = "shapeprojector:block/projector/"
GLOW_ON = 100      # riftward.json uses "glow": 100 on its rustyglow faces

# key -> texture path (ring is swapped per variant)
TEXTURES = {
    "cupronickel":     TEX_ROOT + "cupronickel",
    "cupronickeldark": TEX_ROOT + "cupronickel-dark",
    "brass":           TEX_ROOT + "brass",
    "glass":           TEX_ROOT + "glass",
    "ring":            None,  # filled per variant
}

ALL = ("north", "east", "south", "west", "up", "down")


def cuboid(name, frm, to, tex, faces=ALL, uv_seed=0, **extra):
    """Build one element. Faces get a uv box of the face's own pixel size,
    offset deterministically so grain does not repeat identically on every face."""
    dx, dy, dz = (to[i] - frm[i] for i in range(3))
    size = {
        "north": (dx, dy), "south": (dx, dy),
        "east": (dz, dy), "west": (dz, dy),
        "up": (dx, dz), "down": (dx, dz),
    }
    el = {"name": name, "from": list(frm), "to": list(to)}
    el.update(extra)
    fdict = {}
    for i, f in enumerate(ALL):
        if f not in faces:
            continue
        w, h = size[f]
        ox = (uv_seed * 3 + i * 2) % max(1, int(16 - w) + 1)
        oy = (uv_seed * 5 + i * 3) % max(1, int(16 - h) + 1)
        fdict[f] = {"texture": "#" + tex, "uv": [ox, oy, ox + w, oy + h]}
    el["faces"] = fdict
    return el


def elements(ring_glow):
    E = []
    # 1  Base plate: full width, 2 px tall, dark cupronickel
    E.append(cuboid("baseplate", [0, 0, 0], [16, 2, 16], "cupronickeldark", uv_seed=1))
    # 2-4 Pedestal: 8x8 footprint, y 2..7 (5 px), split around a 1 px brass band
    E.append(cuboid("pedestal-lower", [4, 2, 4], [12, 4, 12], "cupronickel",
                    faces=("north", "east", "south", "west"), uv_seed=2))
    E.append(cuboid("brassband", [3.5, 4, 3.5], [12.5, 5, 12.5], "brass", uv_seed=3))
    E.append(cuboid("pedestal-upper", [4, 5, 4], [12, 7, 12], "cupronickel",
                    faces=("north", "east", "south", "west"), uv_seed=4))
    # 5-6 Housing: 10x10, y 7..11 (4 px); chamfer = 9x9 cap on a 10x10 body
    E.append(cuboid("housing-lower", [3, 7, 3], [13, 10, 13], "cupronickel", uv_seed=5))
    E.append(cuboid("housing-upper", [3.5, 10, 3.5], [12.5, 11, 12.5], "cupronickel",
                    faces=("north", "east", "south", "west", "up"), uv_seed=6))
    # 7  Lens: 6x6 glass disc, 1 px thick, y 11..12, translucent (render pass 3)
    E.append(cuboid("lens", [5, 11, 5], [11, 12, 11], "glass", faces=("up",),
                    uv_seed=7, renderPass=3))
    # 8-11 Emissive ring: 1 px band around the lens (outer 8x8, y 11..12)
    ring_faces = ("north", "east", "south", "west", "up")
    ring = [
        cuboid("ring-n", [4, 11, 4], [12, 12, 5], "ring", faces=ring_faces, uv_seed=8),
        cuboid("ring-s", [4, 11, 11], [12, 12, 12], "ring", faces=ring_faces, uv_seed=9),
        cuboid("ring-w", [4, 11, 5], [5, 12, 11], "ring", faces=ring_faces, uv_seed=10),
        cuboid("ring-e", [11, 11, 5], [12, 12, 11], "ring", faces=ring_faces, uv_seed=11),
    ]
    if ring_glow:
        for el in ring:
            for f in el["faces"].values():
                f["glow"] = ring_glow
    E.extend(ring)
    # 12-14 Three struts at 120 degrees: 1 px wide fins on the base plate,
    #       from the brass band outward, rotated about the block centre.
    for k, ang in enumerate((0.0, 120.0, 240.0)):
        E.append(cuboid("strut-%d" % (k + 1), [7.5, 2, 12.5], [8.5, 5, 15.5], "cupronickel",
                        faces=("north", "east", "south", "west", "up"), uv_seed=12 + k,
                        rotationOrigin=[8.0, 2.0, 8.0], rotationY=ang))
    return E


def shape(ring_texture, ring_glow):
    tex = dict(TEXTURES)
    tex["ring"] = ring_texture
    return {
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": tex,
        "elements": elements(ring_glow),
    }


def check(sh):
    """Sanity: all coordinates within the 0..16 block box (rotated struts checked
    by their radius from the rotation origin), every face texture key exists."""
    import math
    keys = set(sh["textures"])
    for el in sh["elements"]:
        f, t = el["from"], el["to"]
        assert all(t[i] >= f[i] for i in range(3)), el["name"]
        if "rotationY" in el:
            ox, oz = el["rotationOrigin"][0], el["rotationOrigin"][2]
            r = max(math.hypot(x - ox, z - oz) for x in (f[0], t[0]) for z in (f[2], t[2]))
            assert ox - r >= 0 and ox + r <= 16, (el["name"], r)
            assert 0 <= f[1] and t[1] <= 16, el["name"]
        else:
            assert all(0 <= f[i] and t[i] <= 16 for i in range(3)), el["name"]
        for face in el["faces"].values():
            assert face["texture"][1:] in keys, (el["name"], face["texture"])
            u1, v1, u2, v2 = face["uv"]
            assert 0 <= u1 <= u2 <= 16 and 0 <= v1 <= v2 <= 16, (el["name"], face["uv"])


def dump(sh, path):
    txt = json.dumps(sh, indent="\t")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(txt + "\n")
    print("wrote", path, "(%d elements)" % len(sh["elements"]))


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    off = shape(TEX_ROOT + "emissive-off", 0)
    on = shape(TEX_ROOT + "emissive", GLOW_ON)
    check(off)
    check(on)
    dump(off, os.path.join(OUT_DIR, "projector.json"))
    dump(on, os.path.join(OUT_DIR, "projector-on.json"))
