# Step 6b notes — item retains config, presets (spec §4a), placement rule

Archivist. Final implementation task: three items, all cited into `docs/api-notes.md` (§d.11, §i) or
inline `file:line` from the decompiled 1.22.7 sources. The Renderer's drape/tint/marker/see-through
code in `BEShapeProjector.cs` was not touched — additions are confined to new members
(`StackConfigKey`, `BuildConfigTree`, `OnBlockPlaced`) and the dialog's preset row.

## 1. Item retains configuration (spec §2, §8 step 6 — api-notes §i pattern)

- `BlockShapeProjector.OnPickBlock` attaches the BE's full parameter tree to the stack under
  `"projectorcfg"` (`stack.Attributes[key] = be.BuildConfigTree()`; ITreeAttribute indexer
  `ITreeAttribute.cs:17`; vanilla precedent `BlockShapeFromAttributes.OnPickBlock`,
  `BlockShapeFromAttributes.cs:453-466`). Still returns the `-off` variant.
- `GetDrops` = `new[] { OnPickBlock(world, pos) }` — inside GetDrops the BE still exists
  (`SpawnDropsAndRemoveBlock` removes the block last, `Block.cs:1140-1173`); attributes survive the
  engine's drop clone (`ItemStack.cs:411-435`).
- `BEShapeProjector.OnBlockPlaced(ItemStack byItemStack)` (called with the placing stack —
  `ServerWorldMap.cs:471-489` / `ClientWorldMap.cs:971-986`; "Always called after Initialize()",
  `BlockEntity.cs:373-375`) restores `ProjectorParams.FromTree`, applies the SAME `Clamp(Config)` as
  any edit packet, swaps the emissive variant and `MarkDirty(true)`.
- Round-trip covers everything `ToTree` writes: every layer's shape + parameters, Y offset, vertical
  mode, fluid rule, build-feedback switch, colour, enabled — plus centre offset dx/dz.
- Held-item summary: `GetHeldItemInfo` override (`Block.cs:2402`, base `CollectibleObject.cs:1871`;
  `ItemSlot.Itemstack` `ItemSlot.cs:51`) appends "N layers, centre offset dx, dz"
  (`shapeprojector:info-item-summary`) when the stack carries a config tree.

## 2. Presets (spec §4a, v1.1)

- Storage: `ProjectorPresets` = `{ Dictionary<string, ProjectorParams> Presets }`, client-side file
  `ModConfig/shapeprojector-presets.json` via `LoadModConfig`/`StoreModConfig` (api-notes §d.11) —
  plain Newtonsoft round-trip (`APIBase.cs:94-109`); ProjectorParams/LayerParams are public-field DTOs.
  Per player, follows the player across worlds/servers (spec §4a); the server never sees the file.
- GUI (GuiDialogProjector, existing patterns): "Presets" dropdown + name textbox
  (`AddTextInput` `GuiComposerHelpers.cs:936`; `GetText()` `GuiElementTextBase.cs:100` /
  `GuiElementEditableTextBase.cs:787`) + Save / Load / Delete small buttons.
  - Save: empty name ignored; same name overwrites; dropdown refreshes (recompose precedent
    `GuiDialogEditAction.cs:77-83`).
  - Load: replaces the edit-state layers + centre offset, pre-clamps with the client config, then sends
    the NORMAL apply packet — claims + server clamps apply unchanged (spec §4a "loads clamped").
  - Delete: removes, resets the dropdown selection; Load/Delete disabled when no presets exist.

## 3. Placement rule (user ruling on Q2: solid block beneath only)

`BlockShapeProjector.CanPlaceBlock` (`Block.cs:968`, invoked by base `TryPlaceBlock` `Block.cs:955-959`
after replaceability/entity/claim checks): ground test = vanilla `BlockRequireSolidGround.HasSolidGround`
(`BlockRequireSolidGround.cs:17-22`) — the block below's UP side must be solid
(`SideIsSolid(IBlockAccessor, BlockPos, int)` `Block.cs:599`; non-mutating `BlockPos.DownCopy`
`BlockPos.cs:452`). On failure `failureCode = "requiresolidground"` (vanilla code,
`BlockBehaviorUnstableFalling.cs:105`), which the client renders as
"Cannot place this block here. Requires a solid ground"
(`SystemMouseInWorldInteractions.cs:442` → `placefailure-requiresolidground`,
`ASSETS/game/lang/en.json:5509`). Placement against a wall side or under a ceiling therefore fails
with that message; placement on top of any solid block succeeds.

**Q30 confirmation (report only, assets untouched):** `blocktypes/projector.json` has no `lightHsv`
(only `lightAbsorption: 0`); the `-on` look comes from `"glow"` face properties in
`shapes/block/projector-on.json` (20 occurrences). Glow is emissive-look only — baked vertex flags
(`ShapeTesselator.cs:469`), no block light emitted. Matches the ruling.

## In-game test plan

1. **Config round-trip.** Configure the moat trio (ring 20–23 drape, circle 11 drape, circle 30 fixed
   Y+6) + centre offset 0.5/0.5, a custom colour, one layer disabled, fluid rule off on the ring.
   Break the projector → the drop's tooltip shows "3 layers, centre offset 0.5, 0.5". Re-place it
   elsewhere → identical projection (all modes/colours/switches restored), emissive ring lit, HUD lists
   the same layers. Middle-click-pick in creative carries the config the same way.
2. **Presets.** With that config open the dialog, type "moat" in the preset box, Save → dropdown shows
   "moat". Wipe: remove layers down to one default circle, Apply. Load "moat" → all three layers +
   offset return (server echo confirms; outlines redraw). Relog → "moat" still listed. Enter a
   DIFFERENT world → "moat" still listed (file is per player, not per world:
   `%AppData%\VintagestoryData\ModConfig\shapeprojector-presets.json`). Save with an empty name → nothing
   happens. Delete "moat" → dropdown shows "(no presets)", Load/Delete grey out.
3. **Clamped preset.** In a world whose server config has `maxRadius` lowered (edit
   `ModConfig/shapeprojector.json`, restart), load "moat" → radii arrive clamped to the cap, no errors.
4. **Placement rule.** Attempt to place the projector against a wall side or the underside of an
   overhang → red "Cannot place this block here. Requires a solid ground" and no block placed. On the
   ground → places. Break the block beneath a placed projector → the projector stays (rule is
   placement-time only, like vanilla `BlockRequireSolidGround` — worldgen aside, it has no
   OnNeighbourBlockChange pop-off; report if the user wants one).
5. **Regression.** Right-click dialog still opens; drape still follows excavation; done tint,
   marker, hotkey hide and see-through unchanged (Renderer's paths untouched).

## Open questions

- **Pop-off on ground removal:** vanilla `BlockRequireSolidGround` enforces solid ground at placement
  (and worldgen) only; the projector does not break when its ground is later removed. If wanted, that
  is an `OnNeighbourBlockUpdated` addition — not in the ruling as given.
- **Creative inventory stack merging:** whether the creative backpack preserves stack attributes was
  already **Not verified** (api-notes §i.3); drops/pick paths are verified.
- **Preset file locking:** `StoreModConfig` writes synchronously (`APIBase.cs:104-109`); two game
  instances sharing one data folder could race — out of scope.
- The dropped stack's tree also contains `"posx/posy/posz"`? No — `BuildConfigTree` writes only
  `ProjectorParams.ToTree` fields (dx, dz, layerCount, layerN), not the BlockEntity base tree.
