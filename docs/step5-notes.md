# Step 5 notes — Drape mode (spec §5a, §8 step 5)

Renderer. Every API call cited into `docs/api-notes.md` (§d, §g, §h and the new "Renderer additions"
section at the bottom) or inline in the code. All vertical math comes from the Geometer's
`DrapeResolver`; nothing was re-derived.

## What was built

| File | Change |
|---|---|
| `ProjectorParams.cs` | `LayerParams.TreatFluidAsSurface` (bool, default **true** = bank line; spec §5a fluid rule), persisted as `"fluidSurface"`, part of `RenderKey()`. `YOffset` is documented as double-duty: Fixed-Y offset AND the optional drape offset ("optional layer offset for elevated walkways"), and it doubles as the fixed-Y fallback for unloaded columns — one field, one GUI input, meaning depends on the mode. |
| `GuiDialogProjector.cs` | Per-layer **Vertical mode** dropdown (Fixed Y / Follow terrain; Curator's predefined keys `gui-verticalmode`, `gui-verticalmode-fixed`, `gui-verticalmode-drape`) and, only while Drape is selected, the **Treat fluid as surface** switch (`gui-treatfluidassurface`). No new lang keys were needed — the Curator had predefined all four. Mode change re-composes the dialog (the fluid row appears/disappears), same recompose precedent as the shape dropdown (`GuiDialogEditAction.cs:77-83`). The Drape-only switch is read back null-safely: `GuiComposer.GetElement` returns null for a missing key (GuiComposer.cs:834-845, api-notes "Renderer additions"). |
| `BEShapeProjector.cs` | Client-side drape resolution in the geometry rebuild; `BlockChanged` live update; coalesced one-tick mesh patch; 2 s fluid re-sample tick; teardown. Details below. |
| `ProjectorRenderer.cs` | `renderDistance` enforcement added to `OnRenderFrame` (spec §6 "render only within renderDistance of the player"): squared-distance test camera→projector against `ProjectorConfig.renderDistance²`, before the frustum test. Per-frame path remains: distance test, frustum test, 2 uniforms, 1 draw — no geometry. |
| `docs/api-notes.md` | Appended "Renderer additions (step 5)": tick-listener + callback registration (verified in decompiled sources — the ledger had no client tick registration), `GetElement` null behaviour, the 1.22.7 obsoletion of the int-coordinate `GetBlock`, and the API's own `Func` delegate that shadows `System.Func`. |

### Drape resolution (client, off the render loop)

* Per enabled layer, `RebuildGeometry` resolves every outline column with the Geometer's
  `DrapeResolver.ResolveColumn(column, fixedY: YOffset, mode, drapeOffset: YOffset, callback)`
  (`DrapeResolver.cs:31-37`): FixedY → `y = YOffset` (callback never invoked); Drape →
  `y = surface + 1 + YOffset`; unloaded column → `YOffset` (plain fixed-Y fallback, spec §5a).
* The height callback follows api-notes §g.3 exactly:
  * `GetRainMapHeightAt(x, z)` (`IBlockAccessor.cs:506`) — always updated by the engine after block
    changes; water and leaves are not `rainPermeable`, so both count as surface. Returns **0 when the
    map chunk is not loaded** → callback returns null → fixed-Y fallback.
  * `treatFluidAsSurface == false`: descend while
    `GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()` (`IBlockAccessor.cs:119`,
    `BlockLayersAccess.cs:29-32`, `CollectibleObject.cs:3291`). The int-coordinate overload the ledger
    cited is obsolete in 1.22.7 (CS0618, "use BlockPos version"), so a single scratch `BlockPos`
    (copied once from `Pos`, keeping the dimension) is mutated in place — zero allocation per column.
  * Ice: fluid layer but `IsLiquid()` false → surface in both modes (as §g.3 documents).
* The callback is a cached `System.Func` delegate field (one delegate for the BE's lifetime;
  `Vintagestory.API.Common` has its own `Func<,,>`, hence the qualification). Per column there are no
  allocations at all.

### Live update (spec §5a)

* `capi.Event.BlockChanged += OnClientBlockChanged` (§g.1, `IClientEventAPI.cs:69`), unsubscribed in
  `OnBlockRemoved`/`OnBlockUnloaded`. Coverage (§g.2): local place/break, server single + bulk
  solid-layer SetBlocks, ExchangeBlock. **Not** covered: fluid-only changes (never fire it) and
  `SetBlocksMinimal` (unverified) — hence the re-sample tick.
* Handler cost per event: dimension check, a maxRadius window reject, then per drape layer one O(1)
  `PositionSet.Contains` — the exact set the Geometer's `AffectedColumns` contract queries
  (`DrapeResolver.cs:39-50`: only the changed column itself can be affected, never neighbours). No
  scanning, no allocation.
* A hit adds the column to a dirty set and queues **one** 0 ms delayed callback
  (`RegisterDelayedCallback`, BlockEntity.cs:260-269 → `IEventAPI.RegisterCallback`, IEventAPI.cs:158) —
  so a bulk edit that fires BlockChanged per position (§g.2) coalesces into a single re-resolve + mesh
  rebuild on the next game tick, never in the render loop.
* The patch re-resolves **only** the dirty columns (binary search into the layer's sorted `Positions`
  for the cell index), then rebuilds the whole MeshRef once — strategy chosen by measurement, below.
* **Re-sample tick**: while ≥1 drape layer exists, `RegisterGameTickListener(OnResampleTick, 2000)`
  (BlockEntity.cs:195-208; auto-unregistered on remove/unload by the base, 294-296/345-351). It
  re-resolves all drape layers' outline columns and rebuilds the mesh only if some Y changed. All drape
  layers are re-sampled, not only `treatFluidAsSurface` ones, because fluid-layer changes move the
  result under *both* rules: the rain map itself is updated by the fluid-layer setter
  (`BlockAccessorBase.cs:198` → `UpdateRainHeightMap`, §g.3) and ice forming/melting changes where the
  fluid descent stops. Measured cost makes the distinction not worth having (below).

### Defaults (task item 5)

`ShapeSpec.DefaultVerticalMode` (Drape for circle/ring/ellipse/spiral, FixedY for rectangle/polygon)
flows through `LayerParams.ApplyShapeDefaults()`, which runs for: the BE's default layer, the dialog's
Add-layer, and every shape change in the dialog. A freshly placed projector's circle layer and a fresh
"Add"ed circle layer are therefore Drape without touching the dropdown. `FromTree` keeps FixedY as the
missing-attribute default so step-3-era saved trees don't silently change behaviour.

## Measurements

Standalone harness (`scratchpad/meshbench`, net10.0 console referencing `VintagestoryAPI.dll` + the
Geometry sources; `MeshData`/`ModelCubeUtilExt` are pure array code outside GL, so the mod's
`BuildMesh` runs verbatim). Scene = the spec's representative case: ring r=20–23 + circles r=11 and
r=30, centre (0,0). Release build, 2000 iterations after warmup, Ryzen-class desktop:

| Operation | Result |
|---|---|
| Cell counts | ring 20–23 = 244, circle 11 = 64, circle 30 = 168 → **476 cubes, 11,424 verts** |
| Mesh build (`BuildMesh`, full scene) | **220.9 µs** |
| Drape-resolve all 476 columns (synthetic height fn) | **40.1 µs** |
| Full rebuild (resolve + mesh build) | **251.4 µs** |
| One-column patch (resolve 1 column + full mesh build) | **199.2 µs** |
| Rasterize all three shapes (parameter change only) | 77.2 µs |

**Strategy decision:** whole-MeshRef rebuild on the coalesced dirty flag. A one-column patch costs
~0.2 ms of CPU + one `UploadMesh`; a partial `UpdateMesh` (§d.3) would save at most ~0.2 ms per edit
event at the price of vertex-range bookkeeping. At 476 cubes there is nothing to win; even the 2 s
re-sample's worst case (all columns changed) is a 0.25 ms rebuild. The re-sample's steady state (no
changes) is ~40 µs of height lookups every 2 s — the real callback is a `RainHeightMap` array read
(§g.3), same order as the harness's synthetic function.

Per-frame cost is unchanged from step 1: distance test + frustum test + 2 `UniformMatrix` + 1
`RenderMesh`. Nothing new runs per frame.

## Build / install

* `dotnet build -c Release`: **0 warnings, 0 errors**.
* `dotnet build -c Release -t:InstallMod`: installed to `%AppData%\VintagestoryData\Mods\shapeprojector\`
  (DLL not locked — game was closed).

## In-game test plan

1. **Hillside drape.** Place the projector on a slope. Default circle r=11 must appear **draped**
   (following the ground, one block above the surface) without touching the dropdown (defaults test).
   Add a rectangle layer: it must appear **Fixed Y** by default, half buried / half floating on the
   slope — that contrast is the feature.
2. **Dig under the outline.** Excavate a column directly under a draped ghost cube: the cube must drop
   to the new floor within a tick (BlockChanged path). Fill it back → the cube rides up. Dig a
   neighbouring column NOT on the outline → nothing moves (AffectedColumns contract).
3. **Bulk edit.** In creative, use worldedit to remove a slab of terrain crossing the ring: all
   affected columns must drop together after one coalesced rebuild (no per-block stutter).
4. **Pond, fluid rule both ways.** Run a draped circle across a pond. `Treat fluid as surface` **on**
   (default): the outline lies on the water top, marking the bank line. Switch **off** + Apply: the
   columns over water sink to one block above the bed; over land nothing changes. Ice on the pond:
   outline sits on the ice in both modes.
5. **Fluid-only change (re-sample path).** With the rule **on**, drain the pond (e.g. remove the source
   blocks so water recedes): within ~2 s the outline must settle down to the emptied bed even though no
   BlockChanged fires for fluid-only updates (§g.2).
6. **Unloaded-column fallback.** Set a large offset (e.g. dx=120) so part of the outline reaches into
   unloaded chunks, place the projector near a chunk border and step back: far columns render at the
   projector's Y (+ offset), and snap onto the terrain when the chunks load (walk toward them).
7. **Drape offset.** Draped circle with Y offset 3 → the rope floats 3 blocks above the terrain,
   tracking the slope (elevated walkway of spec §5a).
8. **Mode dropdown + fluid switch UI.** The fluid switch is visible only while "Follow terrain" is
   selected; switching modes preserves typed-but-unapplied values in other fields; Apply syncs and the
   second client (or a rejoin) shows the same drape.
9. **renderDistance.** Set `renderDistance: 20` in `ModConfig/shapeprojector.json` (client), restart:
   the projection must vanish when the camera is farther than 20 blocks from the projector block and
   reappear on approach.
10. **Teardown.** Break the projector / walk out of chunk range: no further re-sample activity, no
    errors in `client-main.log` (tick listener + event unsubscription paths).

## Open questions / follow-ups

- **`SetBlocksMinimal` (packet 70) coverage is still Not verified** (§g.2). If some server system
  pushes terrain edits through it, the 2 s re-sample self-heals the outline; no faster reaction is
  possible without that verification. Archivist follow-up if drape ever visibly lags a bulk edit by
  ~2 s.
- **Whole-chunk (re)load (packet 10)** does not raise per-block BlockChanged (§g.2 "Not verified").
  A chunk loading *in* under an existing drape outline is caught by the re-sample tick (unloaded → real
  heights within 2 s); acceptable, but a chunk-loaded event would be cleaner if the Archivist finds one.
- **`GetRainMapHeightAt` has no dimension-aware overload** — a projector inside another dimension
  (e.g. the devastation dimension) would sample the main-dimension rain map. The fluid descent is
  dimension-aware (scratch BlockPos keeps `Pos.dimension`), the rain-map read is not. No vanilla-facing
  scenario places a projector there today; flag for the Archivist if that changes.
- **Leaves count as surface** (not rainPermeable, §g.3): a draped line under a tree sits on the canopy.
  The spec does not ask for canopy-piercing; doing it would need a manual descent through
  `BlockMaterial == Leaves`. Noted as a possible per-layer rule for v2.
- The measured numbers say the whole-mesh rebuild strategy holds comfortably to `maxRadius` scales
  (a r=128 circle is ~1000 columns ≈ 2× the measured scene); if a future step introduces many
  projectors editing simultaneously, revisit with `UpdateMesh` (§d.3).
