# Step 6 notes — done tint, centre marker, hide hotkey, see-through (spec §8 step 6, render features)

Renderer. Scope per the coordinator: render features only — item-retains-config is the Archivist's
(BlockShapeProjector.cs untouched; BEShapeProjector.cs edits confined to geometry/tint paths).
Citations: api-notes §d, §f, §g, §h and the "Renderer additions" section (extended this session with a
"Step 6 additions" block — collision boxes, ShowChatMessage, BlockTexturesLoaded/ReloadShader,
shader-program plumbing, FrameBufferRef, the rift include pairing).

## What was built

| File | Change |
|---|---|
| `ProjectorParams.cs` | `GhostPalette.DoneColor` (palette green) and `GhostPalette.MarkerColor` (palette white, alpha 180). `LayerParams.ShowBuildFeedback` (default true; persisted `"buildFeedback"`; in `RenderKey()`). |
| `GuiDialogProjector.cs` | The mode-dependent switch row now always holds exactly one switch: Drape → "Treat fluid as surface" (step 5); Fixed Y → "Show completed blocks" (Curator's predefined `gui-buildfeedback` key — checked, **no lang keys added** this step). |
| `BEShapeProjector.cs` | Done-tint occupancy: initial scan during the geometry rebuild + `BlockChanged`-driven updates through the existing coalesced patch; `IsCellOccupied` = solid-layer block with ≥1 collision box. Centre-marker state pushed to the renderer on rebuild. Second renderer registration at AfterBlit. |
| `ProjectorRenderer.cs` | Hide-flag test (1 bool) at the top of `OnRenderFrame`; centre-marker cube meshed into the same VBO; the AfterBlit see-through pass (`RenderSeeThrough`); Dispose unregisters both stages. |
| `ShapeProjectorModSystem.cs` | Client session state: `ProjectionsHidden` + hotkey registration (config `clientHideHotkey`, default O); see-through shader ownership — created at `BlockTexturesLoaded`, re-created on `ReloadShader`, compile failure logged once and tolerated. |
| `ProjectorConfig.cs` | `seeThroughMode`: `"shader"` (default) / `"off"`. |
| `assets/shapeprojector/shaders/projectorghost.vsh/.fsh` | The §f.4 see-through shader pair (new). |

### 1. Done tint (spec §6)

* Ruling implemented: **Fixed-Y layers only** (a draped ghost follows the surface and can never be
  "filled"); **"solid" = any non-fluid block with a collision box**; gated by config
  `showBuildFeedback` **and** the per-layer switch.
* The solidity test reads the solid layer only (`BlockLayersAccess.SolidBlocks = 1`, §d.9 — fluids live
  in their own layer, so water/lava never qualify) and calls the virtual
  `Block.GetCollisionBoxes(ba, pos)` (Block.cs:683-686), so stairs, slabs, fences and microblocks
  count through their per-state overrides while tallgrass/flowers (no collision boxes) do not.
* Occupancy is tracked entirely off the render loop: an initial scan while cells are built, then the
  step-5 `BlockChanged` handler marks the column dirty **only when the change is at the cell's own Y**
  (occupancy depends on exactly that one block), and the existing coalesced one-tick patch recolours
  the cell and re-uploads the mesh only when the colour actually changed.

### 2. Centre marker (spec §3/§6)

* A **0.5-size palette-white cube** (alpha 180 vs. the layers' 110 — distinct by both size and colour,
  in-palette) centred at the resolved fractional centre `(projector + 0.5 + dx, +1.5, + 0.5 + dz)` —
  one block above the projector base, so a 0.5-offset centre visibly straddles the block corner.
* Shown **while ≥1 layer is enabled** (documented choice: same semantics as the emissive ring, spec §7 —
  a dormant projector casts nothing).
* Meshed into the same VBO as the ghost cells: zero extra draw calls, and it participates in the
  see-through pass for free.

### 3. Client hide hotkey (spec §6)

* `RegisterHotKey("shapeprojectorhide", …, key, HotkeyType.CharacterControls)` +
  `SetHotKeyHandler` in `StartClientSide` (§d.10; placement precedent ModJournal.cs:44-48). Key parsed
  from config `clientHideHotkey` by `GlKeys` enum name, fallback O (GlKeys.O = 97, GlKeys.cs:119).
* Toggles a **session-only** flag on the mod system (never persisted, never sent anywhere) and prints
  the Curator's `msg-projections-hidden`/`-shown` via `ShowChatMessage` ("client side only",
  ICoreClientAPI.cs:199-202).
* Renderers consult it as the first per-frame check — one bool read, the only per-frame addition.

### 4. See-through depth tolerance (spec §6/§9; api-notes §f recipe, implemented in full)

* **Pass structure:** the step-1 OIT draw stays the primary (normal, depth-tested); the same renderer
  instance is registered a second time at **AfterBlit** (stage precedent RiftRenderer.cs:41) and draws
  the **same VBO** with the mod shader `projectorghost`.
* **Shader:** `assets/shapeprojector/shaders/projectorghost.{vsh,fsh}`, `AssetDomain =
  "shapeprojector"` (§f.1 lookup rule), registered with `RegisterFileShaderProgram` + `Compile()` at
  `BlockTexturesLoaded` and re-created on every `ReloadShader` (RiftRenderer.cs:45-57 pattern). The
  include pairing is copied verbatim from the proven rift shaders: vsh = `vertexflagbits.ash` +
  `fogandlight.vsh`, fsh = `fogandlight.fsh` (gives `linearDepth` and the `zNear`/`zFar` uniforms that
  `ShaderProgramBase.Use()` fills). Vertex layout = the mod's RGBA-only mesh (xyz@0, rgba@1, as
  blockhighlights.vsh).
* **Fragment logic** (rift.fsh:26-33 with the constant replaced by the `seeThroughDepth` uniform):
  `behind = (linearDepth(gl_FragCoord.z) − linearDepth(depthTex)) * zFar` — the view-space depth
  difference in blocks, i.e. **depth along the line of sight per the design ruling**. `behind ≤ 0.05`
  → discard (fragment is in open view; the OIT pass already drew it — the 0.05 matches the cubes'
  inset, avoiding double-drawn silhouette pixels); `behind > seeThroughDepth` → discard (spec §9: not
  a wallhack); otherwise draw at `alpha × 0.55 × (1 − behind/seeThroughDepth)` — dimmer than the
  direct view and fading out with burial depth.
* **Failure containment:** compile failure logs one warning and leaves the program null — renderers
  skip the pass (outline stays plainly depth-tested; no fallback wallhack). Config
  `seeThroughMode: "off"` disables the whole machinery. `seeThroughDepth: 0` also skips the pass.

## Per-frame cost (spec §6 discipline)

* OIT path unchanged from step 5: hide-bool + distance test + frustum test + 2 matrix uniforms + 1 draw.
* AfterBlit adds, **per visible projector per frame**: the same three cheap culls, then 1 shader bind,
  2 matrix uniforms, 2 small uniforms (`invFrameSize`, `seeThroughDepth`), 1 texture bind
  (primary depth texture), 1 draw call of the existing VBO, 2 `GLDepthMask` toggles. No geometry, no
  allocation, no per-frame recompute anywhere. Mesh CPU rebuild cost is unchanged from step 5's
  measurement (476-cube reference scene: 221 µs build; the marker adds one cube = +24 verts; the done
  tint adds one solid-layer `GetBlock` + `GetCollisionBoxes` per Fixed-Y cell per rebuild — array reads,
  same order as the drape height lookups measured at 40 µs for 476 columns).

## Build / install

* `dotnet build -c Release`: **0 warnings, 0 errors**.
* `-t:InstallMod`: **FAILED — DLL locked** (`MSB3021` on `%AppData%\VintagestoryData\Mods\shapeprojector\ShapeProjector.dll`; the game is running). Not retried, game not touched. The finished build sits in `src\ShapeProjector\bin\Release\Mods\shapeprojector\`; copy it (or rerun `-t:InstallMod`) once the game is closed. The currently installed DLL is the step-5 build.

## In-game test plan

1. **Done tint.** Fixed-Y rectangle at Y offset 0 on flat ground: place stone on an outline cell → that
   ghost turns green immediately; break it → back to amber. Slabs, stairs, fence posts → green
   (collision boxes); tallgrass or a flower → NOT green; a water source flowed onto the cell → NOT
   green (fluid layer). Pre-existing walls crossing the outline are green on first render (initial
   scan). A block placed one Y above or below the outline changes nothing.
2. **Per-layer + config gates.** Turn "Show completed blocks" off on the layer → all green reverts to
   the layer colour after Apply. Set `showBuildFeedback: false` in the client config, restart → no
   green anywhere regardless of layer switches. Drape layers never show green.
3. **Centre marker.** Offset 0/0 → small white cube floating centred over the projector. Offset
   0.5/0.5 → it sits exactly on the block corner shared with the +X/+Z neighbours (the even-diameter
   check, spec §3). Offset 12.5/−3 → it stands away from the instrument at the true centre. Disable
   all layers → marker (and everything) disappears with the emissive ring.
4. **Hide hotkey.** Press O → chat says "Shape projections hidden", all ghosts (marker included)
   vanish; press again → back. Check the Settings → Controls list shows "Toggle shape projections".
   A second client must still see the projections (local-only). Rejoin → projections visible again
   (session-only).
5. **See-through.** Ring r=20–23 draped across terrain; stand in a dug pit 2–3 blocks deep inside the
   ring: the ring reads through the pit walls, dimmer than direct view, fading with burial. Stand
   behind a hill ~20 blocks thick: the outline does NOT show through. Set `seeThroughDepth: 0` →
   reveal gone entirely; `seeThroughMode: "off"` → same, and no compile warning. Change shadow/SSAO
   quality in graphics settings (fires ReloadShader) → reveal still works afterwards.
6. **Logs.** `client-main.log`: no `[shapeprojector]` shader warning on a healthy install; exactly one
   clear warning (and a still-functional depth-tested outline) if the shader is broken.

## Verified vs needs in-game eyes

**Verified from source this session:** every API call and its behaviour (see the ledger additions);
the shader include pairing and vertex layout match the working vanilla precedents byte-for-concept;
the mesh/color plumbing compiles clean and reuses the measured step-5 paths.

**Needs in-game eyes (cannot be verified without running the game):**
- That `projectorghost` **compiles on the user's GPU/driver** — the include pairing is rift's, but our
  exact combination has never been through a real GLSL compiler. Failure is contained (one warning,
  pass skipped).
- Whether **water occludes the reveal**: if VS draws water without writing the primary depth buffer,
  a submerged outline will show at full OIT alpha through water (fine) and the AfterBlit pass will
  judge burial against the pond bed, not the water surface. Cosmetic either way; note what it looks like.
- The **0.05 epsilon** at silhouettes (double-draw shimmer vs. gaps) and whether 0.55 max alpha reads
  well — both are single-constant tweaks in the .fsh.
- Depth-precision behaviour at long range (zFar = 1500): the blocks-conversion is §f.3's inference;
  distant outlines may fade slightly early/late.
- That the marker's +1.5 Y placement reads well on projectors placed on walls/ceilings (not a spec
  case, but worth a glance).

## Open questions / follow-ups

- The AfterBlit pass binds/stops the program once **per projector**. With many visible projectors a
  shared "bind once, draw many" loop (a small static render registry) would save N−1 binds; not worth
  it at current scale.
- `IShaderProgram.LoadError` (IShaderProgram.cs:53) could distinguish "files missing" from "compile
  failed" in the warning; kept to one generic message for now.
- If the in-game shader check fails irrecoverably on some platform, §f.6's cited stopgap (low-alpha
  depth-test-off second OIT draw) exists but is the spec-§9 wallhack — deliberately NOT implemented.
