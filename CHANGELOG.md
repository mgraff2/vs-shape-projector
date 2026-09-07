# Changelog

All notable changes to Jonastech Shape Projector.

## 1.1.0 — 2026-09-07

### Added

- **Mark opacity.** Each projector has a slider-style setting, 5 to 100 percent, for how solid
  its marks look out in the world. The hologram keeps its own contrast whatever you choose.
- **World marks switch.** Turn the full-size marks off while the hologram above the projector
  keeps showing every figure. A projector can now be hologram-only.
- **Model surroundings.** The hologram can also show what is already standing around the
  projector, within a radius (up to 256) and height you set, with the coloured figures drawn in
  their true places among it, and reaching up to 256 blocks above and below the projector. Beyond a radius of 64 the model is sampled coarser, in wider tiles,
  so a large radius stays quick. Every block in the model wears its real colour: grass green,
  stone grey, water blue, your own timber and brick as they are. Buildings appear as they
  are seen from outside: roofs, walls with their doorways and windows, and the walls beneath
  eaves and overhangs, but never the inside of a closed room. Land is green and water surfaces are blue by default, and each has its own
  colour picker. A model of your home and of the work at once. It lives in the hologram only,
  whether the world marks are on or off.
- **Figures in hologram switch.** Hide the figures inside the hologram while the surroundings
  model stays, and the miniature becomes a survey of the land alone.
- **Fill up to level.** A layer can mark every block from the ground up to its level under each
  column of the figure. A pit under a platform shows exactly the fill it needs, and with the
  fluid rule off the fill starts at the lake bed, for a causeway. In Follow terrain the level is
  the highest ground under the figure, so the whole figure levels up to its highest point.
- **Colour picker.** A layer's colour is now chosen from a list of twenty-four colours, each
  entry showing the colour itself beside its hex code. Beside it a hex field takes an exact code
  and a preview square always shows the colour in use.
  Older projectors and presets keep their colours.
- **Thickness up to the radius.** A figure's thickness now runs all the way to its own radius, at
  which point it is solid: a disc, a filled square, a filled polygon. The field tells you the
  number that means solid for the figure you are editing.

### Fixed

- **A tall stack of layers no longer locks you out of the projector.** Looking at a projector
  with many layers (a forty-eight-course tower, say) used to raise an info panel so tall it ran
  off the screen and swallowed every click, so you could neither open the dial nor use a tool
  while facing it. The panel now folds identical courses into one line ("Layers 1–48: Circle,
  radius r=5, Y offset 0 to 47") and shows at most eight lines, with "… and N more layers" for
  the rest. The dial itself still lists every layer.
- **Seeing marks through the ground works now.** The through-terrain reveal had never
  actually switched on: its shader failed to compile on every client, quietly, because of two
  typographic dashes in its comments. Buried marks within the see-through depth now show, dimmed.
- **The surroundings model is centred on the projector.** Figures reaching past the model's
  radius, or an unloaded edge, no longer push the projector off the middle of the miniature.
- **Huge solid figures no longer freeze the game on Apply.** Thickening a figure inward is now
  a single pass instead of one pass per block of thickness; a radius 256 disc at full thickness
  used to stall the client for seconds.
- **The mark budget is explained where you can see it.** When a figure is too large to draw in
  full, the dial now says which layer was cut and by how much, and the Thickness, Height and
  Fill tooltips explain the limit. Before, the only word of it was a line in the log.
- **The centre readout uses map coordinates.** The dial and the block-info panel showed the raw
  world position, about 512000 off from what the coordinate display and the map say. They now
  match the game's own numbers.
- **Full detail at any size.** Marks are now drawn as merged faces instead of one little cube
  each: only the faces you could see are drawn, and neighbouring faces of one colour become a
  single rectangle. A radius 256 disc at full thickness, over 200,000 blocks, is a few hundred
  rectangles and renders in full in the world and in the hologram, with no trimming and no
  gaps. Grid lines along the block boundaries keep every mark countable (a config switch turns
  them off for smooth sheets). The per-projector mark limit rises from 60,000 to 300,000 with it.
- **Separate budgets.** The surroundings model has a budget of its own, so figures and
  surroundings never take room from each other, and a projector showing neither world marks nor
  figures in its hologram no longer computes its figures at all.
- **Tall layers on water.** A Follow-terrain layer several blocks tall used to move only its
  bottom course when the water under it changed. The whole layer now moves together.
- **Build feedback on tall layers.** Filling a course above the bottom one of a tall Fixed-Y
  layer now turns that mark green right away, not only after something else changed.

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
