#!/usr/bin/env python
"""
Shape Projector GUI icon generator (Curator, spec section 10e).

Emits the NINE layer-toolbar SVG icons into
  src/ShapeProjector/assets/shapeprojector/textures/icons/
for registration via capi.Gui.Icons.CustomIcons + SvgIconSource
(docs/api-notes.md section p.2).

Rendering constraints baked in (from how IconUtil/DrawSvg consumes these):
  - single colour: everything is #000000 fill or stroke -- the game tints
    the rasterized glyph with the rgba passed to DrawIcon, exactly like the
    vanilla icons (survival/textures/icons/shorten.svg uses black strokes,
    worldmap/x.svg a black fill). No gradients, no other colours.
  - square 24x24 viewBox; glyphs legible at 22-26 px on-screen.
  - stroke width 2.0-2.4 viewBox units (vanilla shorten.svg is ~1.7/24;
    the project constraint floors us at 2).
  - only svg/path/rect/circle/line/polyline elements, simple geometry.

Deterministic: running it twice produces byte-identical files.

Run (any cwd):
    python tools/gen_icons.py
No third-party dependencies.
"""
import os
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "src", "ShapeProjector", "assets", "shapeprojector",
    "textures", "icons"))

VB = 24  # viewBox edge

# Shared stroke styles. Butt/miter is the vanilla default (shorten.svg);
# round caps keep free-standing line ends legible at 22 px.
S_LINE = 'fill="none" stroke="#000000" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"'
S_BOX = 'fill="none" stroke="#000000" stroke-width="2" stroke-linejoin="miter"'
S_FILL = 'fill="#000000" stroke="none"'


def svg(*elements):
    body = "\n".join("  " + e for e in elements)
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" '
        'width="%d" height="%d">\n%s\n</svg>\n' % (VB, VB, VB, VB, body)
    )


def line(x1, y1, x2, y2, style=S_LINE):
    return '<line x1="%g" y1="%g" x2="%g" y2="%g" %s/>' % (x1, y1, x2, y2, style)


def polyline(pts, style=S_LINE):
    p = " ".join("%g,%g" % (x, y) for x, y in pts)
    return '<polyline points="%s" %s/>' % (p, style)


def rect(x, y, w, h, style=S_BOX):
    return '<rect x="%g" y="%g" width="%g" height="%g" %s/>' % (x, y, w, h, style)


def circle(cx, cy, r, style=S_BOX):
    return '<circle cx="%g" cy="%g" r="%g" %s/>' % (cx, cy, r, style)


# ---------------------------------------------------------------------------
# The nine glyphs (spec 10e toolbar: Add, Remove, Duplicate, Move up/down,
# Add layer up, Add layer out, Radius +1/-1).
# ---------------------------------------------------------------------------

ICONS = {
    # Add layer: centered plus.
    "layer-add": svg(
        line(12, 4.5, 12, 19.5),
        line(4.5, 12, 19.5, 12),
    ),

    # Remove layer: centered minus (distinct from radius-minus, which
    # carries the circle).
    "layer-remove": svg(
        line(4.5, 12, 19.5, 12),
    ),

    # Duplicate layer: front square outline, back square peeking out as a
    # top-right L (classic copy silhouette, no crossing lines at 22 px).
    "layer-duplicate": svg(
        polyline([(8.5, 4.5), (19.5, 4.5), (19.5, 15.5)], S_BOX),
        rect(3.5, 8.5, 12, 12),
    ),

    # Move selected layer up: chevron up.
    "layer-moveup": svg(
        polyline([(5, 15.5), (12, 8.5), (19, 15.5)]),
    ),

    # Move selected layer down: chevron down.
    "layer-movedown": svg(
        polyline([(5, 8.5), (12, 15.5), (19, 8.5)]),
    ),

    # Add layer up (duplicate one course up, spec 10d): a low course at the
    # bottom with an up-arrow rising from it.
    "layer-addup": svg(
        rect(5.5, 16.5, 13, 5),
        line(12, 13, 12, 5),
        polyline([(8.5, 8), (12, 4.5), (15.5, 8)]),
    ),

    # Add layer out (duplicate one step out, spec 10d): solid inner square
    # inside a concentric outline square -- the ring grown one step outward.
    "layer-addout": svg(
        rect(9, 9, 6, 6, S_FILL),
        rect(3.5, 3.5, 17, 17),
    ),

    # Global radius +1: circle with a small plus at its top-right shoulder.
    "radius-plus": svg(
        circle(10.5, 13.5, 7),
        line(18, 3, 18, 9),
        line(15, 6, 21, 6),
    ),

    # Global radius -1: circle with a small minus at its top-right shoulder.
    "radius-minus": svg(
        circle(10.5, 13.5, 7),
        line(15, 6, 21, 6),
    ),
}

ALLOWED = {"svg", "path", "rect", "circle", "line", "polyline"}


def sanity(name, text):
    root = ET.fromstring(text)  # raises if not well-formed XML
    assert root.get("viewBox") == "0 0 %d %d" % (VB, VB), name + ": bad viewBox"
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        assert tag in ALLOWED, "%s: disallowed element <%s>" % (name, tag)
        style = (el.get("fill") or "") + (el.get("stroke") or "") + (el.get("style") or "")
        for bad in ("url(", "gradient"):
            assert bad not in style, "%s: non-flat paint" % name
        for c in ("#000000", "none", ""):
            style = style.replace(c, "")
        assert style == "", "%s: colour other than #000000 in %r" % (name, el.attrib)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name in sorted(ICONS):
        text = ICONS[name]
        sanity(name, text)
        path = os.path.join(OUT_DIR, name + ".svg")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print("wrote", path)
    print("%d icons OK" % len(ICONS))


if __name__ == "__main__":
    main()
