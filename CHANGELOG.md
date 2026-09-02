# Changelog

All notable changes to Jonastech Shape Projector.

## 1.0.0 — 2026-09-02

First release.

### The instrument

- **Craft the Jonastech Shape Projector** and set it on the ground: four cupronickel or brass
  ingots, two cupronickel plates, two Jonas parts, a clear quartz and a temporal gear.
- Right-click it to open its dial. The projector marks out figures in light. It builds nothing
  itself — it only shows you where.

### Figures

- **Circle, ring, ellipse, rectangle, regular polygon, triangle and spiral.** Every figure is
  measured exactly, so nothing has to be counted by hand.
- **Layers.** One projector holds up to forty-eight figures at once, all sharing a single centre.
  Add, remove, duplicate and reorder them; each keeps its own size, colour, height and switch.
- **A shared centre you can move**, in half-block steps, on a block or on the seam between blocks
  — which is how you choose an odd or an even diameter. The centre may sit well away from the
  projector itself, so the instrument can stand beside the work.
- **Thickness.** A figure can be marked several blocks broad in one layer instead of several
  layers set one apart. The band grows inward, so the outer edge stays where the radius puts it.
- **Height.** A figure can stand several blocks tall in one layer, so a single layer describes a
  whole wall rather than one course each.

### Standing on the land

- **Fixed height** marks one dead-level line whatever the ground does — wall tops, floors,
  platforms. Where the earth swallows the line, dig down to it; where it hangs in air, build up.
- **Follow terrain** lays the line on the surface like a rope, and follows the floor down as you
  excavate, so a dig line is never lost. Over a slope a tall layer keeps its height.
- **Water** is yours to rule: the line can rest on the surface and mark the bank, or look through
  to the bed and mark the bottom.

### While you build

- **Marks turn green as you fill them**, so the figure completes in front of you.
- **A holographic miniature** floats above the projector showing the whole design at a glance, and
  can be switched off on its own without stopping the projection.
- **Named presets** are saved per player and follow you to any projector — load them in place of
  what is there, or add them to it.
- **Shortcuts** for the work you repeat: add a layer one block up (Page Up) to raise a tower a
  course at a time, add a layer one block out (Page Down) to fill toward a disc, and grow or
  shrink every layer at once with `+` and `-`.
- **Every control explains itself.** Hovering a button says exactly what it will do to the layer
  you have selected, with the arithmetic worked out.
- **Hide every projection** for yourself alone with `O`. Other players still see theirs.
- Carrying a configured projector — broken and replaced, or moved with Carry On — keeps its
  layers.

### For server owners

`ModConfig/shapeprojector.json` holds the limits: largest radius (256), layers per projector (48),
render distance, the ghost-cube budget per projector, and the miniature's size, placement and
style.
