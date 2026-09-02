# Jonastech Shape Projector

A surveyor's instrument for [Vintage Story](https://www.vintagestory.at/). Set it down, dial in a
figure, and it casts a luminous guide onto the world — circles, rings, ellipses, rectangles,
regular polygons, triangles and spirals, in layers around one shared centre. It builds nothing
itself. It only shows you where.

**Version 1.0.0**, for Vintage Story 1.22.0–1.22.7. Dual-side: the server owns the parameters, the
client draws them, so **everyone on the server needs it installed**. The design is in
[docs/shape-projector-mod-spec.md](docs/shape-projector-mod-spec.md).

### How it works

1. **Craft it** — four cupronickel or brass ingots, two cupronickel plates, two Jonas parts, a
   clear quartz and a temporal gear:

   ```
   ingot    plate x2   ingot
   part     quartz     part
   ingot    gear       ingot
   ```

2. **Place it and right-click** — the dial opens. Pick a shape, give it a size, press **Apply**.
   The figure appears on the ground around the projector, and a holographic miniature of the
   whole design floats above the block.

3. **Build to it.** Marks whose block you have filled turn green, so the figure completes in
   front of you.

### What one projector can hold

- **Up to forty-eight layers**, all sharing a single centre, each with its own shape, size,
  colour, height offset and on/off switch.
- **A centre you can move** in half-block steps — on a block for an odd diameter, on the seam
  between two for an even one — and as far from the projector as you like, so the instrument can
  stand beside the work instead of in the middle of it.
- **Thickness**, so one layer marks a band several blocks broad rather than several layers set one
  apart. It grows inward: the outer edge stays exactly where the radius puts it.
- **Height**, so one layer stands several blocks tall and describes a whole wall instead of one
  course each.

### Level, or laid on the land

Every layer stands one of two ways.

**Fixed height** draws at one dead-level height whatever the ground does — wall tops, floors,
platforms. Where the earth swallows the line, cut down to it; where it hangs in air, fill up to
it. That, too, is information.

**Follow terrain** sets each block of the outline on the surface beneath it, like a rope laid out
by hand — the moat's dig line, the hillside path, the fence line. As you excavate, the line
follows the floor down, so the edge is never lost. Water is yours to rule: the line can rest on
the surface and mark the bank, or look through to the bed and mark the bottom.

### The worked example: a moat

Set the projector at the centre of a round holding and give it three layers.

1. A **ring**, inner radius 20, outer radius 23, following the terrain — the moat, three blocks
   wide, both lines lying on the land wherever it goes. Dig between them.
2. A **circle**, radius 11, following the terrain — the path that rings the house.
3. A **circle**, radius 30, at fixed height, offset to the wall's intended top — level all the way
   round, hanging in air where the ground falls away.

Three figures, one centre, concentric by construction. Nothing was counted.

### While you work

- **Named presets**, saved per player, that follow you to any projector — load one in place of
  what is there, or add it on top.
- **Shortcuts** for the repetitive part: **Page Up** copies the selected layer one block higher
  (a tower, a course per press), **Page Down** copies it one block outward (filling toward a
  disc), and **+** / **-** grow or shrink every layer at once.
- **Every control explains itself** — hovering a button tells you exactly what it will do to the
  layer you have selected, with the arithmetic worked out.
- **O** hides every projection for you alone; other players still see theirs.
- A configured projector keeps its layers when broken and replaced, or carried with Carry On.

### For server owners

`ModConfig/shapeprojector.json`:

| Setting | Default | What it does |
|---|---|---|
| `maxRadius` | 256 | Largest radius and centre offset a layer may use |
| `maxLayersPerProjector` | 48 | Layers one projector may hold |
| `maxOutlineThickness` | 32 | Largest per-layer thickness |
| `maxLayerHeight` | 256 | Tallest a single layer may stand |
| `maxCellsPerProjector` | 60000 | Ghost-cube budget per projector; over it, a layer loses height before it loses shape |
| `renderDistance` | 160 | Blocks beyond which a projection is not drawn |
| `showBuildFeedback` | true | The green "already built" tint |
| `seeThroughDepth` | 6 | How deep through terrain an outline stays visible |
| `clientHideHotkey` | `O` | The personal hide key |
| `holoSize` / `holoOffsetY` | 1.5 / 0.0 | Size and height of the miniature above the block |
| `holoMode` / `holoStyle` | `always` / `layered` | When the miniature shows, and whether it keeps layer colours |
| `previewMaxBlocks` | 20000 | Cube budget for the miniature |

### License

MIT — see [LICENSE](LICENSE).
