Amendment to the spec (docs updated — re-read §10c and new §10e): The GUI preview pane is removed. The preview becomes a holographic miniature floating above the projector block, rendered in-world by the existing IRenderer path — same cached position sets, auto-scaled to holoSize (default 1.5³), world-aligned and never rotating (north in mini = north in world; walking around it is the orbit), layer colors with selected-layer brightening while the GUI is open, draped layers shown at their sampled heights, offset marker inside the mini when center ≠ block, holoMode: always|guiOpen|off, budgeted by previewMaxBlocks. The in-dialog 3D render hook is no longer needed — remove it from the Archivist's open questions. Delete the preview pane and reorganize the dialog per §10e: two columns of titled groups (Projector + Presets left; Layers toolbar + Layer settings right), management buttons as compact icon buttons with tooltips, and shape parameters rendered dynamically — only the selected shape's fields appear. Gubsy's acceptance updates accordingly: hologram matches world outlines exactly, stays world-aligned from all sides, and the reorganized dialog fits without scrolling at default UI scale.# Jonastech Shape Projector — Build-Guide Outlines for Vintage Story

**Spec v1.0 — final for implementation**
Target: Vintage Story 1.22.x. **Server-distributed content + code mod** (block + block entity; server-authoritative parameters, client-side rendering of the projection). Auto-pushed to joining clients.
Proposed modid: `shapeprojector`

---

## 1. Design intent

Vintage Story offers no in-game geometry tools. Circular bases, moats, and curved paths are built by hand-counting blocks — and mistakes compound (one player relocated an entire base to realign a circle's quadrants). The Shape Projector is a placeable endgame Jonastech block that projects a **non-solid outline** of a configured shape onto a horizontal plane, so players build along it.

Lore placement: Jonastech, alongside the resonator and night-vision mask — an artifact of precise measurement, gated late-game.

## 2. The projector block

- **Placement:** any solid surface. Single block, Jonastech aesthetic (cupronickel/glass, faint emissive).
- **Right-click → GUI** (server-authoritative; edits sent via standard block-entity packets; vanilla claim permissions govern who may edit).
- **Persistence:** all parameters live on the block entity (`ToTreeAttributes`/`FromTreeAttributes`). Breaking the block drops an item that **retains its configuration in item attributes**, so a projector can be picked up and re-placed without re-entering layers.

## 3. Center model — the even/odd solution

The shape center is **the projector's position plus a half-block-resolution offset (dx, dz)**, in steps of 0.5:

- Offset (0, 0) → center is the block itself → **odd** diameters (single-block center)
- Offset (0.5, 0.5) → center is a block corner → **even** diameters (2×2 center)
- Offset (0.5, 0) → center on an edge (1×2 center)
- Larger offsets (e.g., 12.5, −3) → the projector sits **beside** the feature; the true center may be in water, air, or an existing structure

One mechanism covers every centering case and frees the anchor from needing to occupy the center. The GUI shows the resolved center coordinates and marks it visually (a small distinct ghost marker) so alignment is verifiable at a glance.

## 4. Layers — multiple shapes, one center

The GUI holds an **ordered list of shape layers**, all sharing the projector's center. Each layer: shape type, shape parameters, **Y offset** (relative to the projector, so an outline can sit at moat-floor or wall-top level), **color**, **enabled** toggle. Add / remove / duplicate / reorder.

Canonical use: one projector at a circular base's center → layer 1 ring r=20–23 (moat), layer 2 circle r=11 (path), layer 3 circle r=30 (wall). Concentric by construction.

Config caps layers per projector (default 8).

## 5. Shapes (v1 — horizontal plane only)

| Shape                | Parameters                                                               |
| -------------------- | ------------------------------------------------------------------------ |
| Circle               | radius (integer or half-integer)                                         |
| Ring / annulus       | inner radius, outer radius (a moat in one layer)                         |
| Ellipse              | radius X, radius Z                                                       |
| Rectangle            | width, depth (square = equal)                                            |
| Regular polygon      | sides (3+), circumradius, rotation degrees (triangle, hexagon, octagon…) |
| Spiral (Archimedean) | turns, spacing, start radius, direction                                  |

All outlines are **1 block thick** by default (config for thicker "wall" outlines optional). Rasterization:

- **Circle/ellipse/ring:** distance-based midpoint selection against the fractional center — a block is on the outline if it is the nearest block to the ideal curve at its angle (midpoint circle algorithm generalized to half-block centers; ring = two circles).
- **Rectangle/polygon:** vertices computed from center + parameters, edges rasterized with Bresenham lines; polygons rotate about the center.
- **Spiral:** parametric sampling at sub-block step, snapped to grid, deduplicated, gaps bridged.
- Output is a cached block-position set per layer, recomputed only on parameter change.

## 5a. Vertical placement per layer — uneven terrain

A fixed-Y outline on sloped ground is half buried, half floating. Each layer therefore has a **vertical mode**:

- **Fixed Y** — outline at projector Y + layer offset. For _level_ construction: wall tops, floors, platforms. Where it's buried or floating is itself information ("fill to here / cut to here").
- **Follow terrain (drape)** — for each outline column, the ghost renders at the surface height + 1 (+ optional layer offset for elevated walkways that track the slope). The outline lies on the land like a rope. This is the moat dig-line, the hillside path, the fence line.
  - **Live update:** surface heights recompute for affected columns on block-change events near the outline (cheap — only outline columns), so as the moat is excavated the outline follows down into the pit, always marking the current floor's edge.
  - **Fluid rule (per layer):** `treatFluidAsSurface` — on: outline sits on the water top (marks the bank line); off: sees through fluids to the bed (marks the bottom).
  - **Unloaded columns** fall back to fixed Y until the chunk loads; drape needs only the client's loaded chunk data (column height lookups), no server work.

Default mode: **Fixed Y** for rectangle/polygon (usually architectural), **Drape** for circle/ring/spiral (usually landscape) — overridable per layer.

## 6. Rendering (client-side)

- Outline blocks render as **translucent colored ghost cubes** (slightly inset so real blocks placed on them remain visible), no collision, no lighting interaction, always visible through terrain within a short depth tolerance (config) so a moat outline reads even when the plane is below the player.
- **Build feedback (v1, cheap and satisfying):** an outline position occupied by a solid block renders in a distinct "done" tint (e.g., green) — the shape visibly completes as you build. Optional per-layer.
- **Center marker** rendered distinctly.
- **Performance:** positions precomputed and cached; render only within `renderDistance` of the player; frustum-culled; `maxRadius` cap (default 128) keeps block sets bounded. Rendering requires the projector's chunk to be loaded (BE present) — the outline itself needs no chunk data.
- **Client toggle** (hotkey) to hide all projections locally without affecting other players.

## 7. Configuration (`ModConfig/shapeprojector.json`)

```jsonc
{
  "maxRadius": 128,
  "maxLayersPerProjector": 48,
  "renderDistance": 160,
  "outlineThickness": 1,
  "showBuildFeedback": true,
  "seeThroughDepth": 6, // blocks of terrain the outline stays visible through
  "clientHideHotkey": "O",
}
```

Recipe lives in JSON assets (tunable by server admins). **Acquisition decision: crafted, not found.** Discovery is governed upstream by the Jonas parts economy (locust nests, ruin junk piles — uncommon, several for a dedicated explorer), exactly like the vanilla night-vision mask and rift ward. Default recipe, Jonastech tier:

- 2× Jonas parts
- 2× cupronickel plate
- 1× clear quartz (the projection lens)
- 1× temporal gear
- 4× cupronickel or brass ingot for the housing (grid-crafted)

Available in creative. **Verify Jonas part / cupronickel item codes against 1.22 assets.** Not placed in loot tables or trader inventories (considered and rejected — the parts economy already provides the "found in the world" beat).

**Handbook lore text (draft):**

> _A surveyor's instrument of the old world. Once calibrated, the Jonastech Shape Projector casts a luminous guide onto the ground — circles, rings, and figures of exact measure — so that walls and waterways may be laid true. It builds nothing itself. It only shows you where._

Handbook entry should explain center offset (half-block steps for 2×2 centers), layers, and the two vertical modes in plain language with a worked moat example.

**Visual identity (authored in-project — VS shapes are JSON cuboid lists, textures are small PNGs; both are Claude Code tasks):**

Block shape `shapes/block/projector.json`, Jonastech family — roughly 10–14 cuboid elements:

- **Base plate** — full-width, 2 px tall, dark cupronickel
- **Pedestal** — 8×8 px footprint, ~5 px tall, cupronickel with a brass band
- **Housing** — 10×10 px, ~4 px tall, sitting on the pedestal; slight chamfer via two stacked cuboids
- **Lens** — 6×6 px glass disc on top, 1 px thick, translucent
- **Emissive ring** — 1 px band around the lens, glow when ≥1 layer enabled (two block variants: `-off` / `-on`, or a `glow` face property toggled via BE state)
- **Three small struts/fins** at 120° for the Jonastech silhouette (rotated cuboids)

Textures `textures/block/projector/*.png`, 16×16, generated procedurally: cupronickel (muted warm grey), brass (dull gold), glass (light cyan, alpha), emissive (bright cyan). Reference vanilla Jonastech items for palette consistency. Item uses the block shape.

Ghost outlines: per-layer color from a small curated palette (defaults: cyan circles/rings, amber rectangles/polygons, violet spirals), "done" tint green.

## 8. Implementation architecture

- C# `ModSystem` (both sides): block class `BlockShapeProjector`, block entity `BEShapeProjector` (parameter store + sync), GUI dialog (client, sends edit packets), client renderer (`IRenderer` drawing cached ghost geometry).
- Server owns parameters; client derives geometry from synced parameters (never trust client-computed positions server-side — the server stores only params, so there's nothing to trust anyway).
- **Build order:**
  1. **Prototype: block + BE + one hardcoded circle rendered as ghost cubes.** This validates the single biggest unknown — an efficient 1.22 client rendering path for a few hundred translucent cubes. Everything else is UI and math.
  2. Center offset + circle/ring rasterization with half-block centers; unit-test the rasterizer (radius 0.5 through 64, both parities).
  3. GUI: single layer edit.
  4. Layers list; remaining shapes.
  5. **Drape mode** (§5a): column-height sampling from loaded chunks, block-change-driven recompute, fluid rule. Test on a hillside and while excavating.
  6. Build feedback tint, center marker, client hide hotkey, item-retains-config on break.
  7. Recipe + lore integration.
- **Standing API caveat:** renderer registration, BE sync, GUI composer, and item attribute persistence must be verified against 1.22 docs/source at implementation time, never from memory. Step 1 exists to surface the rendering path specifically.

## 9. Edge cases

- Shapes exceeding world bounds → clipped silently.
- Projector in an unloaded chunk → projection not rendered (expected; no ticking, no cost).
- Two players editing the same projector → last write wins via server; GUI refreshes on BE update.
- Y offset placing the outline underground → visible via see-through depth; beyond that, hidden (intended — not a wallhack).
- Enormous radii on weak clients → `maxRadius` and `renderDistance` are the guardrails; document them.

## 10. v2 — specced additions

### 10a. Triangle as first-class shape

Dropdown entry "Triangle" aliasing regular polygon n=3 (circumradius + rotation). No new rasterizer; pure UI affordance.

### 10b. Presets — named save/load of full layer sets

- A preset = the projector's **complete layer list** (all shapes, parameters, vertical modes, colors, Y offsets) under a player-chosen name.
- **Client-side global library** at `ModData/shapeprojector/presets.json` — available across projectors and worlds, shareable as a file between players.
- GUI: Save as preset… / Load preset (choice: **replace** layers or **append**) / delete. Loading writes layers through the standard server-authoritative edit packets — the preset system never bypasses BE authority.
- Malformed preset file → skip bad entries, report, never crash.

### 10c. Holographic preview (replaces the earlier GUI preview pane)

The projector renders a **live holographic miniature of all enabled layers floating above the block** — in the world, not in the GUI. The player pivots by walking around it.

- **Data source:** the same cached per-layer position sets as the full-size outlines — the hologram never computes its own geometry.
- **Scale & framing:** auto-scaled so the bounding box of all enabled layers fits a hologram volume (default 1.5³ blocks, config `holoSize`), floating above the projector. Tiny translucent cubes/points in **layer colors**; while the GUI is open, the **selected layer renders brighter** so Up/Out chaining reads as visible growth.
- **World-aligned, never rotating:** north in the miniature = north in the world. This is what makes it a usable maquette — the player orients the real build against it at a glance. Walking around it is the orbit control.
- **Center honesty:** the hologram volume centers above the block for legibility; if the shape center offset ≠ 0, a small marker inside the mini shows the projector's own position within the model.
- **Drape shown truly:** draped layers render with their sampled terrain heights, so the mini shows the outline hugging the actual hillside in miniature.
- **Visibility:** config `holoMode`: `always` (while ≥1 layer enabled) | `guiOpen` | `off`; plus the existing client hide hotkey suppresses it locally.
- **Performance:** same budget approach — `previewMaxBlocks` (default 20000) now budgets the hologram; uniform decimation past it. Rendered by the existing world `IRenderer` path — **this amendment removes the in-dialog 3D render hook from the project's API unknowns entirely.**

### 10e. GUI layout (replaces the single vertical list)

With the preview pane gone, the dialog reorganizes into **titled groups in two columns** instead of one long scroll:

- **Left column — Projector:** on/off toggle, resolved center readout, Offset X/Z. Below it, **Presets:** dropdown, name field, Save/Delete, Load (replace) / Load (append).
- **Right column — Layers:** layer selector with the management toolbar (Add, Remove, Duplicate, Move up/down; Add layer up, Add layer out, Radius +1/−1) consolidated as compact icon buttons with tooltips. Below it, **Layer settings:** shape dropdown and **only the parameters relevant to the selected shape** (Sides appears for polygon, Turns/Spacing for spiral, etc. — dynamic fields, not a fixed stack), then Y offset, vertical mode, color, show-completed toggle, enabled.
- **Apply / Close** persistent bottom-right.
- Rationale: the vertical list scrolled past a full screen; grouping by _what the setting belongs to_ (the projector vs. the layer) matches the data model and halves the height.

### 10d. Layer-duplication & global adjustment shortcuts (GUI buttons + hotkeys while GUI open)

Duplication acts on the **selected** layer, and the **new layer becomes selected**, so repeated presses chain:

- **Add Layer Up** — duplicate selected, Y offset **+1**. Pressed repeatedly: a circle becomes a cylinder guide — a tower, one press per course.
- **Add Layer Out** — duplicate selected, radial dimension **+1**. Per-shape semantics: circle radius +1; ring inner **and** outer +1 (thickness preserved); ellipse both radii +1; polygon circumradius +1; rectangle width **and** depth +2 (one block per side); **spiral: button disabled** ("out" has no honest meaning).
  Pressed repeatedly: a circle becomes a filled disc guide — a plaza floor.
- Alternating Up/Out steps manually toward cones and domes — deliberate stepping-stone to (still-v3) true 3D shapes.
- **Global radius +1 / −1** — projector-level buttons applying the Add-Layer-Out radial semantics to **every layer at once** (circle r±1; ring inner _and_ outer ±1; ellipse both radii ±1; polygon circumradius ±1; rectangle width and depth ±2; spirals skipped and noted). Y offsets untouched — a 30-layer cylinder at r=19 becomes r=20 in one click, still a cylinder. Clamped at each shape's minimum (a layer that would underflow stays put and is reported, so −1 then +1 is a safe round-trip only when nothing clamped — the report makes clamping visible). Applies to disabled layers too, so hidden layers never fall out of sync with the building.
- `maxLayersPerProjector` default raised **8 → 48** to accommodate towers; position-set caching keeps many layers cheap.

## 11. Compatibility & release gates (adopted from the Tallybook playbook)

Two mandatory gates, same as the author's other mods: **compat matrix** (`tools/compat-test.ps1`) after any code change and before any commit; **version sweep** (`tools/version-sweep.ps1`) before every release, versions defaulting to the game line modinfo.json promises, probing the CDN for the newest patch.

**Setup rules (corrections to the source prompt baked in):**

- Copy `compat-test.ps1` and `version-sweep.ps1` from the Tallybook repo **verbatim except the companion-list constant, which is the one designated edit point.** The scripts self-discover the project via modinfo.json (modid, version, assembly) and must not otherwise be edited to name this mod. Keep the explicit `exit 0` ending compat-test.ps1 (the sweep reads `$LASTEXITCODE`; a `-SkipBuild` run leaves it stale without it).
- Copy the playbook itself into this repo (`docs/vs-mod-playbook.md`) so the project is self-contained.
- Carry over .gitignore entries: `tools/compat-cache/`, `tools/server-cache/`, `dist/`.
- Scaffold modinfo.json + csproj **first** if absent — both scripts locate the project through modinfo.json. Build uses the user-scoped SDK: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build ...`.

**Marker policy — this mod is dual-side, so neither of the playbook's two stock branches applies verbatim:**

- Pin **exact-count server Notification lines** logged at registration: one each for block class, block entity, and recipe load (e.g., `[shapeprojector] Registered block, BE, recipes` — exactly once in server-main.log), plus "Loaded assembly" / "Instantiate mod systems" in server-debug.log.
- **The headless harness cannot see the client half.** Renderer, GUI, preview, and hotkeys never execute on a dedicated server; the gates validate server-side load and companion coexistence only. Client behavior is the Gubsy agent's manual acceptance pass — the gates must never be cited as evidence for it.
- **No `IsModEnabled` branches exist in this spec.** None are pinned. Standing rule: the commit that introduces one must, in the same commit, add its exact-count `require` marker (companion present) and `forbid` marker (companion absent).

**Companion set — derived from this mod's real interaction surface, one line each in CLAUDE.md:**

- **CarryOn** — the sharpest seam: carrying a placed projector with BE intact vs. the spec's break-drops-item-with-config path are two mechanisms for moving a configured block, and they can disagree. Must verify config survives both.
- **A block-manipulation mod (chiseltools / worldedit-class)** — exercises block swap/removal around the BE.
- **A custom-renderer-heavy mod** — IRenderer registration coexistence.
- **A claims/permissions mod** — the GUI-edit permission path under claimed land.
- Plus the standard heavy-modlist smoke row from the playbook.

**Paste-ready setup prompt** for the Claude Code project (the source prompt with the corrections applied):

> Set up the compatibility-testing process from the Tallybook mod in this project. Read `C:\Projects\vs-tallybook\docs\vs-mod-playbook.md` fully (§1 copy list, §3 gates, §4 invariants, "Things that made these gates lie"), then: copy `tools/compat-test.ps1` and `tools/version-sweep.ps1` here **verbatim except the companion-list constant** (the one designated edit); copy the playbook to `docs/`; carry the .gitignore cache entries. If modinfo.json or the csproj are missing, scaffold them first — the scripts discover the project through modinfo.json. This mod is **dual-side**: pin exact-count server Notification lines for block/BE/recipe registration plus the assembly-load lines; the harness cannot test the client half and CLAUDE.md must say so. No IsModEnabled branches exist; add require/forbid marker pairs in the same commit as any future one. Companion set: CarryOn (BE-carry vs. break-drop config survival), one block-manipulation mod, one custom-renderer-heavy mod, one claims mod, plus the playbook's heavy-modlist row — one-line rationale each in CLAUDE.md. Set version-sweep's default to the 1.22 line, CDN-probed. Write CLAUDE.md Build/Testing sections from playbook §2–4, keeping the failure behind each rule; state both gates as mandatory (matrix per code change/commit, sweep per release). Run `.\tools\compat-test.ps1` once and show the summary — SETUP means the server couldn't be tested, not that the mod failed.

## 12. Out of scope (v3 candidates)

- **Vertical planes** (arches, circular windows) and **full 3D** (domes, spheres, cylinders as volumes) — the layer model extends to a plane-orientation field; the v2 Up/Out shortcuts already approximate stepped volumes.
- Build-progress percentage per layer
- Non-outline fills as a single computed region (v2's repeated Add Layer Out covers the practical case)
- Any block auto-placement — this projects intent; the player builds.
