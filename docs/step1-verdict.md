# Step 1 verdict — client rendering path for ghost cubes (VS 1.22.7)

Archivist, spec §8 step 1. Everything asserted here was read from the decompiled 1.22.7 assemblies or asset files this session; citations are the `file:line` entries in `docs/api-notes.md` (§d unless noted). What I could not verify without running the game is listed separately and honestly.

## Verdict in five lines

1. **Registration:** a client-side `IRenderer` created by the block entity in `Initialize` when `api is ICoreClientAPI`, registered with `capi.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "shapeprojector")` (api-notes §d.1), disposed from both `OnBlockRemoved` and `OnBlockUnloaded` exactly like vanilla `BlockEntityResonator` (§d.1).
2. **Stage:** `EnumRenderStage.OIT` — this is where the engine itself draws its translucent block highlights (`SystemHighlightBlocks.cs:20`, order 0.89), and the engine sets the GL state for us there: cull-face off, depth-mask off, depth-test on, OIT blending (`ClientPlatformWindows.cs:1681-1699`). Our renderer touches no GL state.
3. **Shader:** the engine's own `Blockhighlights` program via `capi.Render.GetEngineShader(EnumShaderProgram.Blockhighlights)` (§d.2). It needs only two uniforms per draw (`projectionMatrix`, `modelViewMatrix`); everything else is filled by `ShaderProgramBase.Use()`. It is untextured, takes per-vertex RGBA, and biases `gl_Position.w += 0.0004` so highlights win depth ties (`blockhighlights.vsh:27`).
4. **Mesh:** one `MeshData(24n, 36n, withNormals:false, withUv:false, withRgba:true, withFlags:false)` built once with `ModelCubeUtilExt.AddFaceSkipTex` per face (0.9-block cubes, inset 0.05, per-face shading from `CubeMeshUtil.DefaultBlockSideShadingsByFacing`), uploaded once with `capi.Render.UploadMesh`, freed with `DeleteMesh`. Same construction as the engine's `BlockHighlight.cs:245-272`.
5. **Cost:** per projector per frame = 1 sphere-frustum test + (if visible) 2 `UniformMatrix` + 1 `RenderMesh` draw call. Radius-11 circle = 64 cubes = 1,536 vertices; "a few hundred cubes" = ~10k vertices in one static VBO. This is the same order of work as one worldedit highlight; it is not a concern. The mesh is rebuilt only when parameters change (spec §5), never per frame.

## Exact per-frame sequence (`ProjectorRenderer.OnRenderFrame`)

```
if (meshRef == null) return;
if (!capi.Render.DefaultFrustumCuller.SphereInFrustum(pos.X+0.5, pos.InternalY+1.5, pos.Z+0.5, radius+1.5)) return;   // world coords, §d.7
Vec3d cam = capi.World.Player.Entity.CameraPos;                                                                     // §d.5
IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Blockhighlights);                               // §d.2
IShaderProgram prev = capi.Render.CurrentActiveShader; prev?.Stop();      // Use() throws if another shader is active (ShaderProgramBase.cs:265-268)
prog.Use();
prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
modelView.Identity().Translate(pos.X-cam.X, pos.InternalY-cam.Y, pos.Z-cam.Z).ReverseMul(capi.Render.CameraMatrixOriginf);   // = camera * translate, §d.5
prog.UniformMatrix("modelViewMatrix", modelView.Values);
capi.Render.RenderMesh(meshRef);
prog.Stop(); prev?.Use();
```

## Why this and not the alternatives

- **StandardShader at Opaque stage** (vanilla `ResonatorRenderer`, `SupportBeamPlacer`): needs a bound texture atlas, lighting uniforms, and blending would have to be toggled manually inside the opaque pass. Doable, but the Blockhighlights/OIT path is what the engine already uses for exactly this kind of geometry and it composites correctly with water, glass and particles (the OIT merge, `ClientMain.cs:1173`).
- **Custom shader at AfterBlit** (vanilla `RiftRenderer`): the right tool if we later need to read the scene depth texture (`capi.Render.FrameBuffers[0].DepthTextureId`) — see open question 1. Not needed to prove the path.
- **Lines** (`ModSystemMeasuringRope`, Autocamera shader): thin lines, not ghost cubes; rejected for the spec's visual.

## Build result

`dotnet build -c Release` in `src/ShapeProjector`: **Build succeeded, 0 Warning(s), 0 Error(s)** (net10.0, referencing only `VintagestoryAPI.dll`). Output folder `src/ShapeProjector/bin/Release/Mods/shapeprojector/` contains `ShapeProjector.dll`, `ShapeProjector.pdb`, `modinfo.json`, `assets/shapeprojector/blocktypes/projector.json`, `assets/shapeprojector/lang/en.json` — the folder-mod layout `ModContainer` requires (api-notes §a.3). **It has not been installed.**

## What I could NOT verify without running the game

- The actual look: whether alpha 110/255 cyan reads as "translucent ghost" under OIT weighting, whether the per-face shading is pleasing, whether the 0.05 inset is visually right. Only a launch answers this.
- That the ghost is hidden by terrain in front of it and visible when the plane is in open view. By construction (depth test on, `w += 0.0004` bias) it should be; not observed.
- That the colour byte order lands as R,G,B,A in the shader. I derived it from `ColorUtil.ColorFromRgba` (r | g<<8 | b<<16 | a<<24) and the engine's own `ToRgba(96, b, g, r)` workaround (`BlockHighlight.cs:25`); if the ring shows up orange instead of cyan, swap to `ColorUtil.ToRgba(a, b, g, r)`.
- That `Use()` on the engine shader from mod code has no side effect on the engine's later highlight draw in the same stage (it is the same program instance; we `Stop()` it after drawing).
- Dimension handling: I used `pos.InternalY - cameraPos.Y` (as `ModSystemSupportBeamPlacer.cs:352` does); vanilla `ResonatorRenderer.cs:76` uses plain `Y`. They are equal in the main dimension; untested elsewhere.
- That `//` comments in `projector.json` are accepted. Vanilla asset files contain them (`ASSETS/survival/itemtypes/resource/gem-rough.json` line 3), so I expect yes.

## Manual test instructions

1. **Install** (one of):
   - Copy the folder `src\ShapeProjector\bin\Release\Mods\shapeprojector\` to `%AppData%\VintagestoryData\Mods\shapeprojector\`, or
   - `cd src\ShapeProjector && dotnet build -c Release -t:InstallMod` (does the same copy; never runs on a plain build).
2. **Launch** Vintage Story 1.22.7. On the main menu open **Mod Manager** and confirm "Jonastech Shape Projector 0.1.0" is listed and enabled. If it is missing or shows an error, read `%AppData%\VintagestoryData\Logs\client-main.log` (search for `shapeprojector`).
3. **World:** Singleplayer → New World → choose game mode *Creative* (or any world; then `/gamemode creative`, alias `/gm creative` — `CmdPlayer.cs:247`). Create/enter the world.
4. **Get the block:** either open the creative inventory and search "Shape Projector" (it is in the `general` and `decorative` tabs), or run `/giveblock shapeprojector:projector` (syntax `giveblock <block code> [quantity] [target]`, requires the gamemode privilege — `CmdGive.cs:19-21`).
5. **Place it** on open, roughly flat ground and step back ~15 blocks.
6. **Expected:** a ring of translucent cyan cubes, diameter 23 (radius 11), centred on the projector, floating at the level **one block above** the projector's base (cube centres at Y+1.5). Cubes are slightly smaller than a block (gap of ~0.05 on every side). The ring is occluded by hills in front of it and visible from above/at distance; it should blend with water and glass rather than z-fight. Placing a real block on a ring position should cover the ghost completely (it is inside the block).
7. **Teardown checks:** break the projector → ring disappears immediately (Dispose path). Walk far enough that its chunk unloads, then return → ring disappears and reappears (unload/Initialize path). No errors in `client-main.log` during either.
8. **Startup check for the registration path:** `client-main.log`/`server-main.log` must not contain "has defined a block class BlockShapeProjector, no such class registered" (the message `BlockType.CreateBlock` logs when `RegisterBlockClass` did not happen — api-notes §b).

## Open API questions I could not answer from source (do not guess)

1. **See-through depth tolerance (spec §6 "always visible through terrain within a short depth tolerance", §9).** There is no engine API for a depth-tolerant test. `IRenderAPI` has `GLDisableDepthTest()`/`GLDepthMask()` but **no `GLDepthFunc`/depth-function setter (Not found in `IRenderAPI.cs`)**. Disabling the depth test entirely inside OIT would show the outline through *everything* (the wallhack the spec rejects). Achieving a bounded tolerance requires a mod-supplied shader that samples the scene depth texture — the precedent is `RiftRenderer` (custom `capi.Shader.NewShaderProgram()` + `RegisterFileShaderProgram("rift", prog)`, binding `capi.Render.FrameBuffers[0].DepthTextureId`, drawn at `AfterBlit`). Whether a mod's `.vsh/.fsh` under `assets/shapeprojector/shaders/` is picked up by `RegisterFileShaderProgram`, and whether the depth texture is readable at the OIT stage (as opposed to AfterBlit), I have not verified. **This is the one real unknown left in the rendering path; it affects steps 5/6, not step 1.**
2. **Client `BlockChanged` coverage (spec §5a drape live-update).** The event is fired from `ClientWorldMap.cs:1099,1105` and `ClientMain.cs:1811,1841`; I did not read the enclosing methods, so I cannot yet say whether every server-pushed block change (other players digging, bulk chunk updates) reaches it. Needs reading before the Drape implementation relies on it.
3. **GUI composer API** (spec §8 step 3) and **item-attribute persistence on break** (step 6): not researched in this task; must be looked up before those steps.
4. **Multiplayer join order:** `StartClientSide` doc says blocks are not registered yet in multiplayer; our registration is in `Start` (correct per `ICoreAPICommon.cs:50-54`), but I have not traced whether a BE class registered by a client-required mod is created correctly on a dedicated server + remote client pair. Test on a dedicated server before calling networking done.
5. **Nothing in the 1.22.7 API was found for a depth-function toggle, and nothing for "render this mesh with a tolerance against depth".** If someone believes such a call exists, it must be located in source before it is used.
