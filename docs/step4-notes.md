# Step 4 notes — layers list, all six shapes, emissive variant

Archivist, spec §8 step 4 (+ spec §7 emissive swap). All API calls carry citations into `docs/api-notes.md` (§c, §h, §i, §j) or the decompiled sources named inline.

## Fix shipped first (step-3 bug: "right click results in zero dialog")

`Block.OnBlockInteractStart` returns **false** unless a `BlockBehavior` handled the interaction (`Block.cs:1503-1507`: `if (flag2) return flag; return false;`). The step-3 override called base first and returned on false, so `OpenDialog()` was never reached. Now ordered like vanilla `BlockTicker` (`BlockTicker.cs:8-16`): find the BE → open the dialog on the client → `return true`; base only when there is no BE.

## What was built

| File | Change |
|---|---|
| `ProjectorParams.cs` | `LayerParams` now holds every shape's parameters (Radius/InnerRadius, RadiusX/RadiusZ, Width/Depth, Sides/Circumradius/RotationDeg, Turns/Spacing/StartRadius/Clockwise) + YOffset, VerticalMode, ColorIndex, Enabled. `ApplyShapeDefaults()` = spec §7 colour (cyan / amber / violet; ellipse → cyan) + spec §5a vertical mode from `ShapeSpec.DefaultVerticalMode` (`ShapeSpec.cs:22`). `Clamp(cfg)` mirrors the Geometry library's own contracts (`Lattice.RequireHalfStepRadius` / `RequireWithinMax`, `Ring` inner < outer, `Rectangle` effective side ≤ 2·max+1, `RegularPolygon` sides ≥ 3, `Spiral` end radius ≤ max) so `LayerGeometry.Update` never rejects server-stored data. `ToShapeSpec()` maps all six. `SizeSummary()` for names/HUD. `RenderKey()` covers every field. |
| `GuiDialogProjector.cs` | Layer list as a dropdown ("Layer N: <shape> r=…", "(disabled)" suffix), buttons Add / Remove / Duplicate / Move up / Move down (Add & Duplicate disabled at `maxLayersPerProjector`, Remove disabled with one layer, Up/Down at the ends; the server clamps regardless). Per-layer: shape dropdown (six), shape-specific rows, Y offset, colour, enabled. Rectangle shows "Drawn size: W × D" from `Rectangle.EffectiveSize` (`Rectangle.cs:14`, README "parity rule"). Spiral spacing has a GUI minimum of 2 (Geometer: laps merge below ~2, `Spiral.cs:14-16`); server clamp allows ≥ 0. Direction = clockwise/counter-clockwise dropdown. The dialog re-composes when the selected layer, the list, or the selected layer's shape changes — vanilla precedent `GuiDialogEditAction.onSelectionChanged` calls `Compose()` from a dropdown handler (`GuiDialogEditAction.cs:77-83`). Shape change resets colour + vertical mode to the shape defaults. |
| `BEShapeProjector.cs` | All enabled layers → one `GhostCell` list → one MeshRef (per-cell colour); rebuilt only when `RenderKey()` changes. `SyncEmissiveVariant()` (server, on load and after every accepted apply): `ExchangeBlock` between `projector-off` and `projector-on` when the enabled-layer count crosses 0↔≥1. HUD lists every layer. |
| `BlockShapeProjector.cs` | Interaction order fix (above); `OnPickBlock` returns the `-off` variant (default `OnPickBlock` is `new ItemStack(this)`, `Block.cs:1390`, which would hand out `projector-on`); JSON `drops` already yield `-off`, and `Block.GetDrops` default uses the JSON `Drops` (`Block.cs:1334-1352`) — no C# override needed there. |
| `lang/en.json` | +1 key: `shapeprojector:gui-effective-size` ("Drawn size: {0} × {1}"). Everything else uses the Curator's keys. |

Vertical mode is stored per layer (defaults per §5a) but neither shown nor resolved — step 5. All layers render at Fixed Y (projector Y + layer Y offset).

### Emissive variant — why it is safe for the BE
`IBlockAccessor.ExchangeBlock(int blockId, BlockPos pos)`: "Set a block at the given position without calling OnBlockRemoved or OnBlockPlaced, which prevents any block entity from being removed or placed" (`IBlockAccessor.cs:262-266`). The accessor writes the id, then `chunk.GetLocalBlockEntityAtBlockPos(pos)?.OnExchanged(block)` (`BlockAccessorRelaxed.cs:97-101`), and `BlockEntity.OnExchanged` updates `Block` and marks it dirty (`BlockEntity.cs:314-321`); the server then sends the ExchangeBlock packet (`BlockAccessorRelaxed.cs:102-103`) which the client applies with a `BlockChanged` event (`GeneralPacketHandler.cs:106-113`). Target block: `world.BlockAccessor.GetBlock(Block.CodeWithVariant("state", "on"|"off"))` (`IBlockAccessor.cs:297`, `RegistryObject.cs:121`; vanilla `BlockCokeOvenDoor.cs:21`). Both variants share `entityClass` in the Curator's `projector.json`.

## In-game test plan

Install: `dotnet build -c Release -t:InstallMod` from `src/ShapeProjector` (game closed), then start the game.

1. **Right-click opens the dialog** (the step-3 bug). Interaction hint "Configure projector" shows when looking at the block.
2. **Layer list.** Dropdown shows "Layer 1: Circle r=11". Add → "Layer 2: Circle r=11" is selected; Duplicate → a copy after it; Move up / Move down reorder (name numbers follow); Remove deletes the selected one (disabled with one layer left). Add is greyed once 8 layers exist; the error toast "This projector holds at most 8 layers." appears if triggered through Duplicate at the cap. Apply → the HUD lists all layers.
3. **Circle** r=11, offset 0/0: odd diameter (23 blocks across through the centre block). Offset 0.5/0.5: even diameter (22 across, 2×2 centre).
4. **Ring** inner 20, outer 23: two concentric circles; between them exactly 2 empty blocks radially (moat of the spec's example). Set inner ≥ outer and Apply → server clamps inner to outer − 0.5.
5. **Ellipse** 11 × 6: 23 blocks along X, 13 along Z; with 11 × 11 identical to the circle.
6. **Rectangle** 10 × 6 at offset 0.5/0.5: exactly 10 × 6 blocks (count the sides). At offset 0/0 the dialog shows "Drawn size: 11 × 7" and the outline is 11 × 7 (parity rule, rounded outward). Width 1 → a line.
7. **Regular polygon** 6 sides, circumradius 11, rotation 0: a hexagon with 6 corners, one corner on +X; rotation 30 → a corner on +Z (the shape turns by 30°). Sides 3 → triangle; 4 with rotation 45 → a diamond; sides 2 is refused (min 3).
8. **Spiral** 3 turns, spacing 3, start 0, clockwise: three laps outward, laps 3 blocks apart, no gaps (8-connected path). Counter-clockwise mirrors it in Z. Spacing below 2 is refused by the dialog (min 2).
9. **Colours** default per shape when the shape is changed: circle/ring/ellipse cyan, rectangle/polygon amber, spiral violet. Picking another colour and Apply → the layer changes colour; changing the shape resets it to the shape's default.
10. **Emissive variant.** With ≥1 enabled layer the block is `projector-on` (emissive ring lit — Curator's shape); disable every layer, Apply → the block swaps to `projector-off` (ring dark), and back. The block-info HUD keeps working across the swap (BE kept). Middle-click (creative pick) on either variant gives `projector-off`; breaking either variant drops `projector-off`.
11. **Persistence/sync:** rejoin → all layers and the variant restored. Second client sees layer changes immediately (not testable here).
12. **Logs:** no `[shapeprojector]` warnings; in particular no "rejected by geometry" lines (would mean a clamp missed a Geometry contract).

## Open questions / follow-ups

- **Dropdown "disabled" entries** are not supported by `AddDropDown`; shapes are all enabled, layer cap is enforced on the buttons.
- **Layer dropdown while typing.** Values typed into the selected layer are read back before any recompose (`ReadInputsInto`), so switching layers keeps unsaved edits in the dialog's copy; nothing reaches the server until Apply.
- **Rotation input** steps by 5° with the +/- buttons; any value can be typed.
- **Spiral turns** are rounded to 0.25; spacing/start radius to 0.5 (Geometry accepts any finite value, the rounding is a GUI/server convention).
- **Spacing minimum 2 in the GUI, 0 on the server** — a client with a modified DLL can still send 0.5 (merged laps); harmless.
- **Variant swap on load:** `SyncEmissiveVariant` runs in the server `Initialize`; a world saved by the step-3 DLL (always `-off`) is corrected on first chunk load.
- **`OnExchanged` → `MarkDirty(true)`** already resyncs the BE, so the client mesh is unaffected by the swap.
