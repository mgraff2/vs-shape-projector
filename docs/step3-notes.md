# Step 3 notes — right-click configuration GUI (single layer)

Archivist, spec §8 step 3. Everything below is implemented in `src/ShapeProjector/` with per-call citations into `docs/api-notes.md` (§c, §d, §h, §i). The Geometer's library is compiled into the mod assembly (see "Build layout").

## What was built

| File | Role |
|---|---|
| `ProjectorConfig.cs` | `ModConfig/shapeprojector.json` with the spec §7 defaults; loaded on both sides in `ModSystem.Start` via `LoadModConfig<T>` / created with `StoreModConfig<T>` when absent (§d.11). |
| `ProjectorParams.cs` | `ProjectorParams { Dx, Dz, List<LayerParams> }`, `LayerParams { Shape, Radius, InnerRadius, YOffset, VerticalMode, ColorIndex, Enabled }`, `GhostPalette` (cyan / amber / violet / green / white / red), tree (de)serialisation, `Clamp(cfg)` (0.5-step rounding, radius ≤ `maxRadius`, layers ≤ `maxLayersPerProjector`), `RenderKey()`. |
| `BEShapeProjector.cs` | Parameter store. `ToTreeAttributes` / `FromTreeAttributes` persist and sync (§c.3/§c.4); `OnReceivedClientPacket` (id 1001, payload = `TreeAttribute` bytes) does reach + claim check (`Claims.TryAccess(..., Use)`, §h.5), clamps, stores, `MarkDirty(true)`; client `FromTreeAttributes` rebuilds geometry via `LayerGeometry.Update` and refreshes the open dialog; `GetBlockInfo` summary. |
| `GuiDialogProjector.cs` | `GuiDialogBlockEntity` subclass modelled on vanilla `GuiDialogBlockEntityTicker` (§h.3): resolved centre (dynamic text), Offset X/Z number inputs (0.5 steps), Shape dropdown (Circle only — see below), Radius (0.5 steps), Y offset (integer), Colour dropdown, Enabled switch, Close / Apply. |
| `BlockShapeProjector.cs` | Right-click → `be.OpenDialog()` on the client only; interaction-help entry uses the Curator's `blockhelp-projector-configure` key. |
| `ProjectorRenderer.cs` | Now geometry-agnostic: `SetCells(IReadOnlyList<GhostCell>)` rebuilds the single MeshRef; frustum sphere radius derived from the cells; ring Y = projector Y + layer `YOffset` (centre +0.5, as the orchestrator set for step 1). |
| `ShapeProjector.csproj` | `ImplicitUsings` on; `<Compile Include="..\ShapeProjector.Geometry\**\*.cs">` (see Build layout). |
| `assets/shapeprojector/lang/en.json` | Added 3 keys only (`gui-layer-disabled`, `info-center`, `info-layer`); all other GUI strings are the Curator's existing keys. |

Shape dropdown: **only "Circle" is listed.** The composer's `AddDropDown` has no per-entry disabled state in the API I read (§h.2), so listing greyed-out shapes was not possible without a custom element; the `ShapeType` enum already carries Ring/Ellipse/Rectangle/Polygon/Spiral and `LayerParams.ToShapeSpec()` already maps Ring, so step 4 only adds inputs.

Vertical mode: stored (`VerticalMode.FixedY` default) but not editable — step 5.

## Build layout decision (Geometry library)

The mod loader accepts exactly one DLL containing a `ModSystem`/`ModInfo` attribute (`ModContainer.cs:502-510`) and resolves further assemblies through an `AssemblyResolve` handler that probes `Path.Combine(searchPath, args.Name + ".dll")` (`ModAssemblyLoader.cs:40-61`). Whether that probe matches `ShapeProjector.Geometry.dll` for a full assembly display name could not be verified from source, so the Geometry sources are compiled into `ShapeProjector.dll` (source include, excluding its bin/obj). The Geometry csproj is untouched and its 8481 unit tests pass (`dotnet test -c Release`, tests/ShapeProjector.Geometry.Tests).

## Data flow (cited)

1. Right-click → `Block.OnBlockInteractStart` (client first, `Block.cs:1479`) → `BEShapeProjector.OpenDialog()` → `GuiDialogProjector.TryOpen()`.
2. Apply → `ReadInputsInto(edit)` → `be.SendApply(edit)` → `capi.Network.SendBlockEntityPacket(Pos, 1001, tree.ToBytes())` (`IClientNetworkAPI.cs:66`, `TreeAttribute.cs:115`).
3. Server `BEShapeProjector.OnReceivedClientPacket` → reach + `Claims.TryAccess(player, Pos, Use)` (`ILandClaimAPI.cs:29`) → `ProjectorParams.FromTree(TreeAttribute.CreateFromBytes(data))` → `Clamp(Config)` → `MarkDirty(true)` (`BlockEntity.cs:463`).
4. Every client: `ClientChunk.AddOrUpdateBlockEntityFromPacket` → `FromTreeAttributes` (`ClientChunk.cs:322-345`) → `RebuildGeometry()` (only when `RenderKey()` changed) → `renderer.SetCells(...)`; open dialog gets `RefreshFrom(Params)`.

## In-game test plan

Install: `dotnet build -c Release -t:InstallMod` from `src/ShapeProjector` (or copy `bin/Release/Mods/shapeprojector/` to `%AppData%\VintagestoryData\Mods\shapeprojector\`). Restart the game if it was running (mods load at start).

1. **Config file.** After the first start, `%AppData%\VintagestoryData\ModConfig\shapeprojector.json` exists with the spec defaults (maxRadius 128, maxLayersPerProjector 8, …). Delete it → it is recreated.
2. **Place a projector** (creative: search "Shape Projector" or `/giveblock shapeprojector:projector-off`). A cyan ring of radius 11 appears at the projector's level. Looking at the block, the block-info HUD shows "Center offset 0, 0 — resolved center X, Z" and "Layer 1: Circle, radius 11, Y offset 0".
3. **Right-click** the block: the dialog opens on the right side of the screen, titled "Shape Projector", showing the resolved centre and the fields pre-filled (0 / 0 / Circle / 11 / 0 / Cyan / on). The interaction hint "Configure projector" shows when looking at the block.
4. **Radius:** set 20 (typing or the +/- buttons, which step by 0.5), Apply → ring grows to r=20 without the dialog closing. Set 5.5 → half-integer circle. Set 999 → clamped to 128 by the server; the dialog field snaps back to 128 when the resync arrives.
5. **Offset:** set Offset X = 0.5, Offset Z = 0.5, Apply → the resolved centre reads "X.5, Z.5" and the circle is now even-diameter (2×2 centre) — count the blocks across. Set Offset X = 12.5 → the circle moves 12.5 blocks in +X; the projector sits beside it (spec §3).
6. **Y offset:** set 3, Apply → ring rises three blocks; -2 → sinks (it is hidden where terrain covers it: expected, depth test).
7. **Colour / Enabled:** choose Amber, Apply → ring turns amber. Switch Enabled off, Apply → ring disappears (dialog stays open); on → back.
8. **Persistence:** leave the world and rejoin (or walk far away and back): the ring reappears with the edited parameters (tree persisted through `ToTreeAttributes`).
9. **Break/replace:** break the block → ring and dialog vanish. Place a new one → defaults (radius 11). *Configuration is not carried in the dropped item yet — that is step 6 (§i).*
10. **Dialog range:** walk >~5 blocks away with the dialog open → it closes itself (`GuiDialogBlockEntity.OnFinalizeFrame`, §h.1).
11. **Second client (caveat).** On a dedicated server with two clients: player A edits, player B should see the ring change immediately (the resync goes to all clients, §c.4). In a claimed area where B has no `Use` access, B's Apply must be rejected silently on the server (audit line in `server-main.log`: "sent a packet to shapeprojector … has no claim access. Rejected.") and B's dialog snaps back to the server state only on the next resync. **Not tested by me — no second client here.**
12. **Log check:** no `[shapeprojector]` warnings in `client-main.log` / `server-main.log`; no "Layer … rejected by geometry" lines.

## Open questions / follow-ups

- **Reach test simplification.** `IPlayer.IsInInteractionRangeOf(BlockPos, float)` is `internal` (`IPlayer.cs:100`), and the engine's `CachedAccessPerms` helper is marked `[Obsolete("This signature will change in 1.23")]` by the compiler (the decompiled listing did not show the attribute). The server check therefore re-implements `ServerPlayer.isInInteractionRangeOf` (`ServerPlayer.cs:309-330`) against the block centre with +0.87 slack instead of the selection boxes — never stricter than vanilla, slightly more lenient at the block's corners.
- **Coordinates shown.** The dialog and HUD show absolute block coordinates (`Pos.X + dx`). The vanilla HUD shows coordinates relative to the world's default spawn; if the user wants that convention, it is a display change only (`capi.World.DefaultSpawnPosition` — not yet verified in source).
- **`-on` variant.** The Curator's blocktype now has `projector-off` / `projector-on`; switching to `-on` when a layer is enabled (spec §7 emissive ring) is §j Option A (`ExchangeBlock`) and is not part of step 3.
- **Apply keeps the dialog open** so the ring can be seen moving; the fields refresh from the server's clamped values. If the user prefers "Apply closes", it is one line (`TryClose()` in `OnApply`).
- **Dropdown "disabled" entries** are not supported by `AddDropDown`; other shapes are omitted rather than greyed.
- **Client config.** Each side reads its own `shapeprojector.json` (§d.11); the dialog clamps to the *client's* `maxRadius`, the server to its own. A mismatched client simply gets clamped by the server.
