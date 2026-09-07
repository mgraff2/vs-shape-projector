# Shape Projector — Acceptance Checklist (Gubsy gate)

**Source of truth:** `docs/shape-projector-mod-spec.md` v1.1 (v1.0 plus the user-authorized §4a presets amendment, 2026-09-01), **plus the v2 revision** that adds §10 "v2 — specced additions" (§10a triangle, §10b presets, §10c — **amended 2026-09-01: the GUI preview pane is GONE, replaced by a holographic miniature above the block**, §10d shortcuts, new §10e two-column dialog) and moves the out-of-scope list to §12. STATUS.md **ruling 9** (user-directed, not yet in the spec text) adds the §10c contrast treatment and `holoStyle`; **ruling 10** (STATUS.md 9a) removes the world-space centre marker entirely and adds `holoOffsetY` — S3-10/S3-11/S6-17 are superseded (Q50), S10CH-08/14/15 carry the ruling. **§10b supersedes §4a** — S4A-01..12 are retired; run S10B-01..22 instead. **The §10c amendment supersedes the preview-pane rows** — S10C-01..12 are retired; run S10CH-01..13 and S10E-01..08 instead. Every "Expect" below is quoted or paraphrased from a spec section. Nothing here comes from the code.
**Renumbering note:** the "§10 — Out of scope" section below (S10-01..07) tests what is now spec **§12**; the IDs are kept. New v2 checks use S10A- / S10D- prefixes.
**Who runs it:** the user, in-game, by hand. Fill in the **Result** column with `PASS`, `FAIL` (+ what you saw), or `N/A` (feature not yet built at this step).
**How to read the Step column:** the spec §8 build step at which the check first applies. A check with step *3* is expected to pass from step 3 onward; if it fails at step 3, it fails.
**Bug format:** WHAT I DID / WHAT I EXPECTED (spec §) / WHAT HAPPENED / SPEC SECTION.
**SPEC QUESTION** = the spec does not say what should happen. Report what you saw; the user rules, not me.

---

## 0. Test setup (do once)

- Creative world, VS 1.22.x, single-player unless the check says "2 players".
- `/gamemode creative` (switch to `/gamemode survival` only where a check says so — creative-mode breaking may not drop the item).
- Turn on the coordinate HUD (Settings → Interface → show coordinates, or the F3 debug overlay) so you can read block positions.
- Get the block: `/giveblock shapeprojector:projector-off` (or find it in the creative inventory). Spec §7 also names an `-on` variant.
- Build three test sites near spawn:
  - **FLAT**: a flat area at least 70×70 (r=30 wall needs 60+ wide).
  - **HILL**: a hillside with at least ~10 blocks of height change across ~40 blocks.
  - **POND**: a pond at least ~15 wide and 3+ deep, with a bank.
- Counting rule used everywhere below: stand over the row of ghost cubes that passes through the projector (east–west), count from the westernmost ghost cube to the easternmost, inclusive. That number is the **width**.

### Expected widths (derived from spec §3 centre model + §5 midpoint rasteriser)

The outline is the set of blocks the ideal curve passes through (midpoint circle algorithm generalized to half-block centres, §5). The extreme blocks on the axis are the ones the ideal circle touches at ±r; the outline never pokes outside the circle's bounding box.

| Centre offset (dx, dz) | Centre sits on | Radius | Width east–west | Width north–south | Parity (§3) |
|---|---|---|---|---|---|
| (0, 0) | the projector block itself | 11 | **23** (blocks −11…+11) | 23 | odd |
| (0.5, 0.5) | a block corner (2×2 centre) | 11 | **22** (blocks −11…−1, +1…+11 around the corner) | 22 | even |
| (0.5, 0) | an edge (1×2 centre) | 11 | **22** | **23** | even × odd |
| (0.5, 0.5) | block corner | 11.5 | **24** | 24 | even |
| (0, 0) | block itself | 11.5 | **23** | 23 | odd |
| (0.5, 0.5) | block corner | 20 / 23 (ring) | 40 (inner) / 46 (outer) | same | even |
| (0.5, 0.5) | block corner | 30 | 60 | 60 | even |

> **SPEC QUESTION Q1 (parity mismatch ties):** when the radius parity does not match the centre parity — integer radius at a corner centre (r=11 @ 0.5/0.5) or half-integer radius at a block centre (r=11.5 @ 0/0) — the ideal circle on the axis lands exactly on a block boundary. §5 says both "distance-based midpoint selection" and "the nearest block to the ideal curve at its angle"; the classic midpoint algorithm picks the inner block (widths 22 and 23 above), but a "nearest block centre" reading could give 24 / 25. I have written the midpoint answer as expected. If you count 24 or 25 in those two rows, record it as Q1, not as a bug, and the user decides.

---

## Step 3 — run this now

Single circle layer via the GUI (radius, centre offset, Y offset, colour, enabled). These are the checks you can do today; each is a pointer into the full tables below.

| # | Check | Do this | Expect | Result |
|---|---|---|---|---|
| 1 | S2-03 | Right-click the placed projector | A GUI opens | |
| 2 | S3-01 | Radius 11, offset 0 / 0, count east–west | **23** wide, ring passes through the projector's own row and column | |
| 3 | S3-02 | Radius 11, offset 0.5 / 0.5, count | **22** wide; the circle's centre is the corner touching the projector's block | |
| 4 | S3-03 | Radius 11, offset 0.5 / 0, count both ways | **22** east–west, **23** north–south | |
| 5 | S3-05 | Offset 12.5 / −3, radius 5 | Circle is centred 12.5 blocks in +X and 3 blocks in −Z from the projector; the projector is outside the circle | |
| 6 | S3-06 | Look at the GUI | It shows the resolved centre coordinates (world X/Z of the centre, including the .5) | |
| 7 | S5-02 | Radius 11.5, offset 0.5 / 0.5 | GUI accepts 11.5; **24** wide | |
| 8 | S5-03 | Radius 11.5, offset 0 / 0 | **23** wide (Q1 if 25) | |
| 9 | S4-02 | Y offset +3 then −2 (on FLAT) | Outline moves 3 up / 2 down relative to the projector; −2 is still visible through the ground (§9 see-through) | |
| 10 | S4-03 | Change the colour | Outline changes to the chosen colour; default for a circle is cyan | |
| 11 | S4-04 | Untick enabled | Outline disappears; tick again → returns | |
| 12 | S7-18 | Enabled off → look at the block | The emissive ring is not glowing (`-off` look); enabled on → glowing (`-on`) | |
| 13 | S2-05 | Set r=11.5, offset 0.5/0.5, Y −1, non-default colour → leave to main menu → rejoin | All values exactly as you left them, outline identical | |
| 14 | S6-01 | Walk through the outline, jump on it | No collision — you pass straight through | |
| 15 | S6-04 | Set Y offset −4 then −10 on FLAT, stand on the ground above | −4: visible through the ground; −10: hidden (default seeThroughDepth 6) | |
| 16 | S7-01 | Look in `ModConfig/shapeprojector.json` | File exists with the seven §7 keys and default values | |
| 17 | S7-02 | Edit `maxRadius` to 15, restart, enter radius 30 | No outline larger than r=15 renders (Q26 on what the field shows) | |
| 18 | S7-04 | `renderDistance` 40, client view distance ≥ 128, walk 60 blocks away | Outline gone at 60, back when within 40 | |
| 19 | S9-01 | Projector near the world edge with r=30 crossing it | Outline clipped at the border, no error, no crash | |
| 20 | S9-03 | Walk far enough that the projector's chunk unloads (small client view distance) | Nothing rendered; returns when you come back | |
| 21 | S9-07 | Radius 128 (the max) | Renders, game stays playable; r=129 is clamped to 128 | |
| 22 | S10-01/02 | Look at the outline after 5 minutes | No blocks have been placed by the mod; the circle is a hollow 1-block line, not a filled disc | |
| 23 | S10-03..06 | Look over the whole GUI | No vertical-plane / orientation option, no progress %, no sphere/dome/cylinder. (Presets are IN scope — spec §10b; a preset row may appear; see S10B) | |
| 24 | S9-04 (2 players, if you can) | Both open the GUI; A sets r=11, B sets r=15 after A | Both see r=15; A's open GUI refreshes to 15 | |

Report these 24 in one message and I'll file the step-3 verdict.

---

## §2 — The projector block (placement, GUI, persistence)

| ID | Step | Do this | Expect (spec §2) | Result |
|---|---|---|---|---|
| S2-01 | 1 | Place the projector on grass, on stone, on top of a plank block | Places on "any solid surface" | |
| S2-02 | 1 | Try to place it on water, on tall grass, in mid-air | Placement fails (not a solid surface). **SPEC QUESTION Q2:** does "any solid surface" include the *side* of a wall or the underside of a ceiling? Try both and report | |
| S2-03 | 3 | Right-click the block | GUI opens | |
| S2-04 | 3 | Change a value, close the GUI, reopen | Value kept (server-authoritative; the block entity stores it) | |
| S2-05 | 3 | Configure it, leave to main menu, rejoin | Every parameter and the outline exactly as left (persistence via block entity) | |
| S2-06 | 3 | Dedicated server only: configure, restart the server, rejoin | Same as S2-05 | |
| S2-07 | 6 | `/gamemode survival`, break the projector, pick up the drop | Exactly one projector item drops | |
| S2-08 | 6 | Re-place that item somewhere else, open the GUI | All layers intact — nothing needs re-entering ("retains its configuration in item attributes") | |
| S2-09 | 6 | Break a *fresh* never-configured projector, re-place it | Default (empty / default) config; it did not inherit another projector's layers | |
| S2-10 | 6 | Configure A, break A, then break an unconfigured B; place both | Each item carries its own config — they are not mixed up | |
| S2-11 | 3 (2 players) | Player B stands in a land claim owned by A; B right-clicks A's projector inside the claim and tries to change the radius | Edit refused (vanilla claim permissions govern who may edit); A can edit | |
| S2-12 | 3 (2 players) | B (outside any claim) opens the GUI | B can *view*; **SPEC QUESTION Q3:** should anyone at all be able to edit an unclaimed projector? Spec only says vanilla claim permissions govern | |
| S2-13 | 1 | Join a fresh client that does not have the mod installed (2nd client, or delete the mod locally) | Mod is auto-pushed to the joining client ("server-distributed … auto-pushed to joining clients") | |
| S2-14 | 3 | Build a 48-layer tower with Add Layer Up (one circle, Page Up ×47), close the dial, then look at the projector | Block-info panel is short: one "Layers 1–48: Circle, radius r=…, Y offset 0 to 47" line, never one line per layer. Right-click still opens the dial and tools still work while facing it (bug 2026-09-07: the oversized panel reached the crosshair and swallowed every click) | |
| S2-15 | 3 | Make 12 layers that do NOT stack (mixed shapes, or the same circle at the same Y via Duplicate) and look at the projector | At most 8 layer lines, then "… and 4 more layers (12 in all)". Clicks still reach the world | |

## §3 — Centre model

| ID | Step | Do this | Expect (spec §3) | Result |
|---|---|---|---|---|
| S3-01 | 3 | Circle r=11, offset (0, 0); count east–west and north–south | 23 × 23 — odd diameter, "center is the block itself"; the projector sits in the middle row and column | |
| S3-02 | 3 | Circle r=11, offset (0.5, 0.5); count | 22 × 22 — even diameter, "center is a block corner"; the four blocks around that corner form the 2×2 centre, and the projector is one of those four | |
| S3-03 | 3 | Circle r=11, offset (0.5, 0); count both ways | 22 east–west, 23 north–south — "center on an edge (1×2 center)" | |
| S3-04 | 3 | Circle r=11, offset (0, 0.5); count both ways | 23 east–west, 22 north–south | |
| S3-05 | 3 | Offset (12.5, −3), r=5 | Projector "sits beside the feature"; circle centred 12.5 blocks in +X and 3 in −Z of the projector | |
| S3-06 | 3 | Any offset; read the GUI | GUI "shows the resolved center coordinates" — world coordinates of the centre, matching projector position + offset (check against the coordinate HUD) | |
| S3-07 | 3 | Enter offset 0.3 | **SPEC QUESTION Q4:** spec says "steps of 0.5"; it does not say whether 0.3 is rejected, rounded, or accepted. Report what happens | |
| S3-08 | 3 | Enter offset 200 / −200 | **SPEC QUESTION Q5:** no maximum offset is stated. Report whether it is accepted and whether the outline renders that far away (interacts with renderDistance) | |
| S3-09 | 3 | Place the projector on the bank; offset so the centre is out in the POND | Accepted — "the true center may be in water, air, or an existing structure"; circle renders centred on the water | |
| S3-10 | 6 | ~~Any offset — look for the world-space centre marker~~ | **SUPERSEDED by ruling 10 (STATUS.md 9a):** the world-space centre marker is REMOVED entirely. Do NOT fail the build for its absence; its *presence* is now the failure — see S10CH-14. Centre indication is the GUI resolved-centre readout (S3-06) + the dot inside the mini (S10CH-08). Spec §3 text not yet edited — that mismatch is **Q50**, the user's to resolve. Q6a is moot | |
| S3-11 | 6 | ~~Offset (12.5, −3) — marker at the offset centre~~ | **SUPERSEDED by ruling 10** — no world marker anywhere, including at the offset centre. Successor: S10CH-08 (dot inside the mini points the other way — it marks the *projector* within the model) | |

## §4 — Layers

| ID | Step | Do this | Expect (spec §4) | Result |
|---|---|---|---|---|
| S4-01 | 3 | Single layer: set shape params | Each layer has shape type, parameters, Y offset, colour, enabled toggle | |
| S4-02 | 3 | Y offset +3, then −2 | Outline sits 3 above / 2 below the projector ("relative to the projector") | |
| S4-03 | 3 | Change colour | Outline recolours; default circle colour is cyan (§7) | |
| S4-04 | 3 | Enabled off / on | Outline hidden / shown | |
| S4-05 | 4 | Add a second layer | Two outlines, both centred on the same centre ("all sharing the projector's center") | |
| S4-06 | 4 | Add ring 20–23, circle 11, circle 30 (offset 0.5/0.5) | Three concentric outlines, "concentric by construction"; widths 40/46, 22, 60 | |
| S4-07 | 4 | Remove the middle layer | Only that outline disappears; others untouched | |
| S4-08 | 4 | Duplicate a layer, then change the copy's radius | Copy starts identical; editing the copy leaves the original unchanged | |
| S4-09 | 4 | Reorder layers | Order in the list changes and persists after reopen/relog. **SPEC QUESTION Q7:** does order affect anything visible (e.g. which colour wins where two outlines overlap)? Spec only says "ordered list" | |
| S4-10 | 4 | Add layers until you have **48**, then try a 49th | 49th is refused (default cap **48** since v2 §10d; was 8 — S10D-07 is the fast way to reach the cap). **SPEC QUESTION Q8:** refused *how* — greyed button, message, silent? | |
| S4-11 | 4 | Set `maxLayersPerProjector` to 3 in config, restart, try a 4th | Refused at 3 | |
| S4-12 | 4 | Set cap to 3 on a projector that already has 8 | **SPEC QUESTION Q9:** what happens to existing extra layers? Spec doesn't say | |
| S4-13 | 4 | Disable one of three layers | Only that outline hidden; the block still glows (≥1 layer enabled) | |
| S4-14 | 4 | Disable all layers | All outlines hidden; block shows the `-off` look (§7 emissive "glow when ≥1 layer enabled") | |
| S4-15 | 4 | Two layers same shape, different Y offset (0 and +5) | Two rings, one 5 blocks above the other — "an outline can sit at moat-floor or wall-top level" | |
| S4-16 | 4 | Configure 8 layers, relog | All 8 intact, same order | |

## §4a — Presets (spec v1.1) — SUPERSEDED by §10b. Do not run.

> The v2 spec revision removed §4a; presets are now **§10b**, which changed two things: the storage location — a **client-side GLOBAL library** at `ModData/shapeprojector/presets.json` (shareable as a file between players), not the v1.1 ModConfig file — and the load control, now **two buttons: Load (replace) and Load (append)**. Every S4A check has an S10B successor; run those. Presets you saved while testing §4a are themselves test input now — see **S10B-01 (migration)**, and do NOT delete the old `ModConfig\shapeprojector-presets.json` file before running it.
>
> **ID mapping (S4A → S10B):** 01 → 02/05 (save) · 02/03 → 06–08 (replace) · 04 → 12 (overwrite) · 05 → 16 (delete) · 06 → 14 (maxRadius clamp) · 07 → 15 (layer cap, Q33) · 08 → 20 (claims) · 09/10 → 03/04 (relog / different world / server) · 11 → 21 (different terrain) · 12 → 13 (name rules, Q35).

## §5 — Shapes (each at both parities: offset 0/0 and 0.5/0.5)

| ID | Step | Do this | Expect (spec §5) | Result |
|---|---|---|---|---|
| S5-01 | 3 | Circle r=11 @ (0,0) | 23 wide, 1 block thick everywhere, no gaps, no doubled blocks along the diagonals beyond what a midpoint circle produces (a clean 8-way symmetric circle) | |
| S5-02 | 3 | Circle r=11.5 @ (0.5,0.5) | GUI accepts a half-integer; 24 wide | |
| S5-03 | 3 | Circle r=11.5 @ (0,0) | 23 wide (Q1 if 25) | |
| S5-04 | 3 | Circle r=11.3 | **SPEC QUESTION Q10:** "integer or half-integer" — rejected, rounded, or accepted? Report | |
| S5-05 | 3 | Circle r=0.5 @ (0.5,0.5) | Renders — this is the 2×2 block centre itself (the smallest circle the rasteriser is unit-tested to, §8 step 2) | |
| S5-06 | 3 | Circle r=0 or negative | **SPEC QUESTION Q11:** not stated. Report | |
| S5-07 | 3 | Circle r=64 @ both parities | 129 wide / 128 wide; symmetric in all 4 quadrants (rotate the world 90° in your head — the same block pattern) | |
| S5-08 | 4 | Ring inner 20, outer 23 @ (0.5,0.5) | **Two** 1-block lines, 40 and 46 wide, with exactly 2 empty blocks between them on the axis row; NOT a filled band ("ring = two circles") | |
| S5-09 | 4 | Ring inner 23, outer 20 (swapped) | **SPEC QUESTION Q12:** rejected, auto-swapped, or renders? Not stated | |
| S5-10 | 4 | Ring inner = outer | **SPEC QUESTION Q13:** one circle, or rejected? | |
| S5-11 | 4 | Ellipse rX=10, rZ=5 @ (0,0) | 21 wide east–west, 11 north–south, 1 block thick, symmetric | |
| S5-12 | 4 | Ellipse rX=10, rZ=5 @ (0.5,0.5) | 20 × 10 | |
| S5-13 | 4 | Ellipse rX = rZ = 11 | Identical block set to circle r=11 | |
| S5-14 | 4 | Rectangle width 11, depth 7 @ (0,0) | Outline 11 × 7 blocks, sharp corners, straight 1-block edges, centred on the projector | |
| S5-15 | 4 | Rectangle width 10, depth 6 @ (0.5,0.5) | 10 × 6, centred on the corner | |
| S5-16 | 4 | Rectangle width 10 @ (0,0) (parity mismatch) | **SPEC QUESTION Q14:** an even width can't centre on a block; spec doesn't say whether it becomes 10 (shifted), 11, or 9. Report | |
| S5-17 | 4 | Rectangle width = depth = 9 | A square | |
| S5-18 | 4 | Polygon sides 6, circumradius 10, rotation 0 | Hexagon; each vertex 10 blocks from the centre; 6 straight Bresenham edges | |
| S5-19 | 4 | Same, rotation 30 | Hexagon rotated 30° about the centre (vertices where edge-midpoints were) | |
| S5-20 | 4 | Rotation 360 | Identical to rotation 0 | |
| S5-21 | 4 | Sides 3 / 4 / 8 | Triangle / square (diamond at rotation 0 or 45 — note which) / octagon. **SPEC QUESTION Q15:** where is 0° — a vertex pointing +X, +Z, or north? Not stated; just report which | |
| S5-22 | 4 | Sides 2 | Rejected ("sides (3+)") | |
| S5-23 | 4 | Sides 64, circumradius 20 | Renders; looks near-circular | |
| S5-24 | 4 | Spiral turns 3, spacing 2, start radius 2, direction A | Continuous 1-block line, no gaps ("gaps bridged"), no duplicated blocks, 3 turns, successive arms 2 blocks apart, starts 2 blocks from centre | |
| S5-25 | 4 | Same, direction B | Mirror image (winds the other way) | |
| S5-26 | 4 | Spiral turns 0.5 | **SPEC QUESTION Q16:** fractional turns allowed? Not stated | |
| S5-27 | 4 | Each shape: change a parameter, watch the outline | Updates promptly ("recomputed only on parameter change"); no relog needed | |
| S5-28 | 4 | All six shapes @ (0.5,0.5) with the default Y | All are centred on the same corner (place a marker block at the corner and check each shape is symmetric about it) | |
| S5-29 | 4 | Set `outlineThickness` 2 in config (optional feature) | If implemented: outlines 2 thick; if not: still 1 (spec calls this "optional") | |

## §5a — Vertical placement (drape / fixed Y / fluid rule)

| ID | Step | Do this | Expect (spec §5a) | Result |
|---|---|---|---|---|
| S5A-01 | 5 | New circle / ring / spiral layer — look at the vertical mode | Default **Drape** | |
| S5A-02 | 5 | New rectangle / polygon layer | Default **Fixed Y** | |
| S5A-03 | 5 | New ellipse layer | **SPEC QUESTION Q17:** §5a lists circle/ring/spiral → drape and rectangle/polygon → fixed; ellipse is not listed. Report the default | |
| S5A-04 | 5 | Switch a rectangle to Drape and a circle to Fixed | Both accepted ("overridable per layer") and persist after relog | |
| S5A-05 | 5 | HILL: circle r=15, **Fixed Y**, offset 0 | Flat ring at the projector's Y: buried on the uphill side, floating on the downhill side (this is intended: "fill to here / cut to here") | |
| S5A-06 | 5 | HILL: same circle, **Drape** | Every ghost cube sits on the ground — one above the top surface block of its column ("surface height + 1"), "like a rope" | |
| S5A-07 | 5 | HILL: Drape, Y offset +2 | Rope shape follows the slope but 2 blocks above the ground everywhere ("elevated walkways that track the slope") | |
| S5A-08 | 5 | Drape on FLAT | Identical to Fixed Y offset 0 (surface + 1 = the projector's own level on flat ground) | |
| S5A-09 | 5 | Drape over a column with tall grass / flowers / a snow layer on top | **SPEC QUESTION Q18:** does "surface" mean the top *solid* block, or does a plant/snow layer count? Report what it does | |
| S5A-10 | 5 | Drape where an outline column has a tree over it | **SPEC QUESTION Q19:** does the ghost sit on the leaves/canopy or the ground? Not stated | |
| S5A-11 | 5 | Drape; dig one block out of the ground directly under a ghost cube | That ghost drops 1 block immediately ("Live update … on block-change events") | |
| S5A-12 | 5 | Keep digging that column 3 more blocks | Ghost follows down each time, always one above the current floor | |
| S5A-13 | 5 | Place a block back in that column | Ghost rises back onto it | |
| S5A-14 | 5 | Dig a block *next to* (not under) an outline column | That outline column does not move | |
| S5A-15 | 5 | Dig a 2-block-deep trench along a ring's outer line for ~10 blocks | The whole run of ghosts is now on the trench floor, "always marking the current floor's edge" | |
| S5A-16 | 5 | POND: circle crossing the water, `treatFluidAsSurface` **on** | Over water the ghosts sit on the water top (the block above the water surface) — "marks the bank line"; the line is level across the water | |
| S5A-17 | 5 | Same, `treatFluidAsSurface` **off** | Over water the ghosts drop to the pond bed (one above the bottom block, inside the water) — "marks the bottom" | |
| S5A-18 | 5 | Toggle on/off repeatedly | Switches promptly, no relog | |
| S5A-19 | 5 | Two layers on POND: one on, one off | Each layer obeys its own setting ("per layer") | |
| S5A-20 | 5 | Fluid off, ghost at the bed; dig the bed 1 deeper | Ghost follows the bed down (live update through water) | |
| S5A-21 | 5 | Drape circle r=100, client view distance small (e.g. 4 chunks / 128 blocks, so part of the circle is in unloaded chunks); set `renderDistance` 300 in config | Loaded columns: draped. Unloaded columns: rendered at the projector's Y + offset ("fall back to fixed Y until the chunk loads"); walking toward them they snap onto the terrain | |
| S5A-22 | 5 | Drape, then relog | Still draped, correct heights on first render | |
| S5A-23 | 5 | Drape with Y offset −1 in a dug moat | Ghosts sit *in* the floor block (one below surface+1) — still visible via see-through | |

## §6 — Rendering

| ID | Step | Do this | Expect (spec §6) | Result |
|---|---|---|---|---|
| S6-01 | 1 | Walk and jump through the outline; stand where a ghost is | No collision | |
| S6-02 | 1 | Look at a ghost cube edge-on and through it | Translucent — blocks behind are visible; coloured | |
| S6-03 | 1 | Place a real block on an outline position | The real block's faces are fully visible, no flicker/z-fighting (ghost is "slightly inset") | |
| S6-04 | 3 | Y offset −4 on FLAT, stand on the ground above | Outline visible through the ground (within default `seeThroughDepth` 6) | |
| S6-05 | 3 | Y offset −10 | Hidden ("beyond that, hidden — not a wallhack") | |
| S6-06 | 3 | Y offset −4, walk down into a 5-deep pit next to it so you're level with it | Visible (you're no longer 4 blocks of terrain away). **SPEC QUESTION Q20:** is "depth" measured vertically from the player, or as terrain thickness along your line of sight? On HILL, look at an outline buried 3 blocks *sideways* into the slope and report | |
| S6-07 | 3 | Stand at the centre of a ring r=20–23 (drape) that lies in a dug moat 4 deep, look outward | Moat outline reads from the centre platform even though the plane is below you | |
| S6-08 | 1 | Outline at night, and in a dark cave | Same colour and clearly visible regardless of light ("no lighting interaction"); the ghosts do not cast light onto neighbouring blocks | |
| S6-09 | 6 | Place a solid block (e.g. cobblestone) on an outline position | That ghost turns the "done" tint (green) — "the shape visibly completes as you build" | |
| S6-10 | 6 | Break that block again | Back to the layer colour | |
| S6-11 | 6 | Build 10 blocks along the outline | Exactly those 10 are green; neighbours off the line are ignored | |
| S6-12 | 6 | Place a block one row *outside* the outline | Not green; the outline itself doesn't change | |
| S6-13 | 6 | Place a slab / stairs / fence / dirt / glass / water on an outline position | **SPEC QUESTION Q21:** "occupied by a solid block" — do partial blocks, glass, or fluids count as done? Report each | |
| S6-14 | 6 | Per-layer build feedback toggle | Exists and, when off, that layer never shows green ("Optional per-layer"). **SPEC QUESTION Q22:** does "optional per-layer" mean the per-layer toggle is required, or that the whole feature is optional? | |
| S6-15 | 6 | `showBuildFeedback: false` in config | No green anywhere, on any layer | |
| S6-16 | 6 | Done tint on a drape layer while digging | A ghost at surface+1 is by definition empty; after you place a block there it goes green — and does *not* jump up a block. **SPEC QUESTION Q23:** drape moves the ghost to surface+1, so placing a block on a draped ghost changes the surface; should the ghost rise (drape rule) or stay and turn green (feedback rule)? Report what it does | |
| S6-17 | 6 | ~~Look at the centre marker~~ | **SUPERSEDED by ruling 10 (STATUS.md 9a):** §6's "center marker rendered distinctly" no longer exists in the world — do NOT fail the build against the spec text (unedited; **Q50**). The check is inverted: no marker renders anywhere — S10CH-14 | |
| S6-18 | 6 | Press the hide hotkey (default `O`) | *All* projections from *all* projectors vanish for you; press again → back | |
| S6-19 | 6 (2 players) | A presses `O` | B still sees everything ("without affecting other players") | |
| S6-20 | 6 | Press `O`, relog | **SPEC QUESTION Q24:** does the hidden state persist across sessions? Not stated; report | |
| S6-21 | 6 | Change `clientHideHotkey` to `P` in config, restart | `P` toggles, `O` no longer does | |
| S6-22 | 3 | Radius 128 (max), stand at centre | Renders the whole circle (256 wide) and the game stays playable; frame rate roughly as before | |
| S6-23 | 3 | Same, set client view distance to 64 blocks | Fixed Y outline still renders all the way out, including over unloaded terrain ("the outline itself needs no chunk data"), as long as you're within `renderDistance` (Q25) | |
| S6-24 | 3 | `renderDistance` 40; stand 60 from the projector but with the outline's far edge 20 from you | **SPEC QUESTION Q25:** is `renderDistance` measured player→projector or player→each ghost cube? Report which you observe | |
| S6-25 | 4 | 8 layers at r=100+ each | Still playable (positions "precomputed and cached") | |
| S6-26 | 3 | Turn around so the outline is behind you, then back | No visual glitch when it re-enters view (frustum culling) | |

## §7 — Config, recipe, handbook, visual identity

| ID | Step | Do this | Expect (spec §7) | Result |
|---|---|---|---|---|
| S7-01 | 3 | Open `ModConfig/shapeprojector.json` after first launch | Exists with exactly: `maxRadius` 128, `maxLayersPerProjector` **48** (was 8; raised by v2 §10d — see S10D-24), `renderDistance` 160, `outlineThickness` 1, `showBuildFeedback` true, `seeThroughDepth` 6, `clientHideHotkey` "O" | |
| S7-02 | 3 | `maxRadius` 15, restart, enter radius 30 | No outline bigger than r=15 renders. **SPEC QUESTION Q26:** does the GUI clamp the field to 15, refuse 30, or show 30 while rendering 15? Not stated | |
| S7-03 | 3 | `maxRadius` 15, ring 10–30 | Outer clamped (whatever Q26 says); inner still 10 | |
| S7-04 | 3 | `renderDistance` 40, walk 60 away, come back | Gone at 60, back within 40 (subject to Q25) | |
| S7-05 | 3 | `seeThroughDepth` 2, Y offset −4 | Now hidden (was visible at default 6) | |
| S7-06 | 3 | Delete the config file, restart | Regenerated with defaults | |
| S7-07 | 3 | Put a broken value (e.g. `"maxRadius": "lots"`) | **SPEC QUESTION Q27:** not stated; report — at minimum the game should not crash | |
| S7-08 | 7 | Survival mode, crafting grid: 2× Jonas parts, 2× cupronickel plate, 1× clear quartz, 1× temporal gear, 4× cupronickel ingot | Produces 1 projector | |
| S7-09 | 7 | Same with 4× brass ingot instead of cupronickel | Produces 1 projector ("cupronickel *or* brass ingot") | |
| S7-10 | 7 | Same with 2 cupronickel + 2 brass ingots | **SPEC QUESTION Q28:** mixed housing — allowed or not? Not stated | |
| — | 7 | — | **SPEC QUESTION Q29 (blocking for step 7):** the listed recipe totals 2+2+1+1+4 = **10** ingredients, but a grid recipe has at most 9 cells (3×3). The spec cannot be crafted as written. Also, the grid *layout* is not specified. User must decide before step 7 is testable | |
| S7-11 | 7 | Leave out the temporal gear / use rose quartz instead of clear / use copper plate | No output | |
| S7-12 | 7 | Creative inventory search "projector" | Available in creative | |
| S7-13 | 7 | Handbook (H) → the projector | Entry exists; contains the lore paragraph beginning "A surveyor's instrument of the old world…" and ending "…It only shows you where." verbatim | |
| S7-14 | 7 | Same entry | Explains, in plain language: centre offset (half-block steps for 2×2 centres), layers, both vertical modes, with a worked moat example | |
| S7-15 | 7 | Same entry | Shows the crafting recipe | |
| S7-16 | 7 | Spend 10 min looting ruins / check a trader | Not found in loot or trader stock ("Not placed in loot tables or trader inventories") — informational, can't prove a negative | |
| S7-17 | 1 | Look closely at the placed block | Recognisable elements: full-width thin dark base plate, an 8×8-ish pedestal with a brass band, a slightly wider housing on top, a translucent glass lens disc on top, an emissive band around the lens, three small fins at 120° | |
| S7-18 | 3 | Enabled on vs all layers off | Emissive ring glows only when ≥1 layer enabled (`-on` / `-off`) | |
| S7-19 | 1 | Compare next to a vanilla Jonastech item (resonator, night-vision mask) | Palette reads as the same family: muted warm grey cupronickel, dull-gold brass, light cyan glass, bright cyan emissive | |
| S7-20 | 1 | Hold the item, look at it in inventory | "Item uses the block shape" — it looks like the block, not a flat icon | |
| S7-21 | 1 | Block at night | "Faint emissive" — a dim glow from the ring, not a light source that lights the area (Q30: spec doesn't say whether it emits *block light*; report) | |
| S7-22 | 4 | Add a new circle, rectangle, polygon, spiral layer without touching colour | Defaults: cyan (circle/ring), amber (rectangle/polygon), violet (spiral). **SPEC QUESTION Q31:** ellipse default colour not stated | |
| S7-23 | 4 | Open the colour control | A "small curated palette" to pick from (not necessarily free RGB) | |
| S7-24 | 6 | Done tint | Green | |

## §9 — Edge cases

| ID | Step | Do this | Expect (spec §9) | Result |
|---|---|---|---|---|
| S9-01 | 3 | Small custom world (e.g. 128×128) or `/tp` to the world edge; projector 10 blocks from the edge, r=30 | Outline "clipped silently" at the border; no error, no log spam, no crash | |
| S9-02 | 3 | Y offset so the outline is below Y=0 or above the world height (e.g. −400 / +400) | Clipped silently; nothing rendered where the world doesn't exist | |
| S9-03 | 3 | Client view distance minimal; walk until the projector's chunk unloads | Nothing rendered; no cost; returns when the chunk reloads | |
| S9-04 | 3 (2 players) | A and B both have the GUI open; A sets r=11, then B sets r=15 | Both see r=15 ("last write wins via server"); A's still-open GUI refreshes to 15 ("GUI refreshes on BE update") | |
| S9-05 | 3 (2 players) | B changes the value while A's GUI is closed; A opens | A sees B's value | |
| S9-06 | 3 | Y offset −5 | Visible through the ground (within see-through 6); −10 hidden | |
| S9-07 | 3 | r=128 | Renders; r=129 clamped to 128 (`maxRadius` guardrail) | |
| S9-08 | 4 | 8 layers, r=128 each, walk around | Playable ("guardrails") — note frame rate | |
| S9-09 | 3 | Break the projector while the GUI is open | GUI closes or becomes inert; no crash. **SPEC QUESTION Q32:** not stated; report | |
| S9-10 | 3 | Break the projector while standing far away with the outline in view (2 players: B breaks, A watches) | A's outline disappears | |
| S9-11 | 3 | Place two projectors 5 blocks apart, both r=11 | Two independent circles; editing one doesn't touch the other | |
| S9-12 | 5 | Drape layer in a column where a player is standing / an entity is | Entities ignored — ghost sits on the ground block, not on the player | |

## §10a — Triangle as first-class shape (v2, Phase 1 item 2 — run now)

**Site for all S10A/S10D checks:** the standard scenario — HILL, projector on one block of a chosen 2×2, centre offset **0.5 / 0.5** unless a row says otherwise. Step column "v2-1" = v2 Phase 1 item 2; these are runnable now. **Every row in S10A and S10D is client-half (GUI, hotkeys, renderer). A green §11 compat matrix is NEVER evidence for any of them — this manual pass is the only evidence that exists.**

| ID | Step | Do this | Expect (spec §10a) | Result |
|---|---|---|---|---|
| S10A-01 | v2-1 | Open the GUI, add a layer, open the shape dropdown | A **"Triangle"** entry is in the list | |
| S10A-02 | v2-1 | Select Triangle; read its parameter fields | **Circumradius + rotation** (plus the standard Y offset / colour / enabled / vertical mode) and **no sides field** — "aliasing regular polygon n=3 (circumradius + rotation)"; n is fixed at 3 | |
| S10A-03 | v2-1 | Layer 1: Triangle, circumradius 10, rotation 0, Fixed Y, colour A. Layer 2: **Polygon, sides 3**, circumradius 10, rotation 0, Fixed Y, colour B. Enable only layer 1, note every ghost position (walk the three edges); enable only layer 2, compare | **Identical block sets** — every ghost of one sits exactly where a ghost of the other was; not one block differs. "No new rasterizer; pure UI affordance." Also note which way the rotation-0 vertex points (feeds Q15) | |
| S10A-04 | v2-1 | Set both layers to rotation 30 and re-compare; then set offset to 0 / 0 and re-compare | Still identical at the other rotation and at the other parity | |
| S10A-05 | v2-1 | On the Polygon layer, leave sides at 3; look at how the layer names itself in the list/dropdown | Build team says it displays as **Triangle**. **SPEC QUESTION Q36:** §10a says only "aliasing" — the reverse display is not clearly required. If it still shows "Polygon", record Q36, not a bug | |
| S10A-06 | v2-1 | Add a fresh Triangle layer without touching colour or vertical mode | Polygon defaults, because it *is* polygon n=3: vertical mode **Fixed Y** (§5a), colour **amber** (§7). A deviation is Q36 territory — report, don't guess | |
| S10A-07 | v2-1 | Configure a Triangle layer (r=10, rot 15, non-default colour), leave to menu, rejoin | Layer returns still displayed as Triangle, same values, same outline blocks | |

## §10b — Presets (v2, Phase 1 item 3 — run now)

Step column "v2-3". Replaces the retired S4A-01..12 (mapping in the §4a stub above).

**Before you start:**
- **Run S10B-01 FIRST, on the first launch of this build, before saving any new preset** — it tests migration, and a Save might rewrite the file you are inspecting.
- File locations (Windows default game data folder): new library `%APPDATA%\VintagestoryData\ModData\shapeprojector\presets.json`; old §4a file `%APPDATA%\VintagestoryData\ModConfig\shapeprojector-presets.json`. Do not delete the old file.
- You will need: the 30-layer tower from S10D-02 (rebuild if gone: fresh projector on HILL, offset 0.5/0.5, one circle r=5 Fixed Y, press Add Layer Up 29 times), two or three more fresh projectors, and a text editor for rows 17–19.
- Rows 14–19 involve config edits, restarts, and hand-editing the presets file with the game **closed**. Take a backup copy of `presets.json` before row 17 and restore it after row 19; restore `shapeprojector.json` defaults after row 15.
- **Gate rule (same as S10D-26):** every row here is GUI/file/client behaviour except the claim rejection in row 20, and even that is verified only by playing. A green §11 compat matrix is NEVER evidence for any S10B row.

| ID | Step | Do this | Expect (spec §10b) | Result |
|---|---|---|---|---|
| S10B-01 | v2-3 | **FIRST, before saving anything:** launch this build, open any projector, open the preset list | Every preset you saved during §4a testing is already there, with no action from you, and loads correctly. On disk: the new `ModData\shapeprojector\presets.json` exists and contains them; the old `ModConfig\shapeprojector-presets.json` is **still present and unchanged** (kept as backup). **Q46:** §10b never mentions migration — this expectation is the build's contract; if your old presets do NOT appear, report it (it is user-data loss) and the user rules. N/A if you never saved a §4a preset | |
| S10B-02 | v2-3 | On the 30-layer tower projector: type **Tower30** in the preset name box, press **Save** | Name appears in the preset list. The preset controls are: name box, Save, **two Load buttons (replace / append)**, Delete — §10b's "Load preset (choice: **replace** layers or **append**)" as two buttons. On disk, `presets.json` now contains Tower30; open it in a text editor — plain readable JSON ("shareable as a file between players") | |
| S10B-03 | v2-3 | Leave to main menu, rejoin the same world | Tower30 still listed and still loads | |
| S10B-04 | v2-3 | Create or join a **different world** (and a different **server**, if you have one); place a projector, open the preset list | Tower30 and the migrated presets are all there and load — "client-side global library … available across projectors and worlds" — not per-world storage | |
| S10B-05 | v2-3 | Back on HILL: configure the **moat trio** on a projector at offset 0.5/0.5 — L1 ring 20–23, Drape, fluid **on**; L2 circle 11, Drape, fluid **off**; L3 circle 30, Fixed Y, Y offset +4, a non-default colour, **disabled**. Save as **MoatTrio** | Saved and listed. This preset carries every per-layer field §10b names — "all shapes, parameters, vertical modes, colors, Y offsets" — plus the fluid rules and the disabled flag; rows 6–9 and 21–22 verify they all round-trip | |
| S10B-06 | v2-3 | **Load (replace):** second projector, well away, with a decoy config: offset 0/0, L1 rectangle 11×7, L2 ellipse rX 10 rZ 5. Select **Tower30**, press **Load (replace)** | The decoy layers are GONE; the preset's **complete layer list** replaces them: 30 circle-r=5 layers, Y offsets 0..29, colours and modes identical to the original. Centre offset: the build also replaces it (becomes 0.5/0.5) — **Q42:** §10b's preset definition lists only layer fields, not the centre; record what the offset field shows | |
| S10B-07 | v2-3 | The moment you press Load, watch the world (then close the GUI and look again) | The world ghosts change to the tower **immediately** — no relog, no re-place. The load goes "through the standard server-authoritative edit packets", so the server echo re-renders the world. Bottom course **10 wide** (circle r=5 at a corner centre) | |
| S10B-08 | v2-3 | Compare the loaded projector's GUI with the original tower's, side by side (screenshots help), spot-checking layers 1, 15, 30 | Every field equal: radius, Y offset, colour, vertical mode, enabled | |
| S10B-09 | v2-3 | **Load (append):** third fresh projector, offset 0/0, two layers of your own — L1 rectangle 11×7, L2 ellipse 10/5. **Select L1.** Select **MoatTrio**, press **Load (append)** | Now **5 layers**: your two stay at positions 1–2 untouched; MoatTrio's three join at the end (3–5) in preset order with every field intact — Drape/Drape/Fixed, fluid on/off, layer 5 still disabled with its non-default colour. The **first appended layer (3, the ring) is now selected**. Centre offset is **still 0/0** — appended shapes render around the CURRENT centre. **Q43:** the spec is silent on whose centre append uses; the build keeps the current one — record | |
| S10B-10 | v2-3 | On a projector with exactly 30 layers (the one from S10B-06), press **Load (append)** with Tower30 | Only 18 fit: layer count goes to **48** (the §10d cap), layers 31–48 are the preset's first 18 courses in order, and the **report line states the dropped count — 12** (record the exact wording). **Q44:** the spec never addresses append meeting the cap | |
| S10B-11 | v2-3 | Immediately press **Load (append)** with Tower30 again (the projector is now full at 48) | **Refused with the cap message**; layer count stays 48, no layer changed (Q44) | |
| S10B-12 | v2-3 | On the original tower projector, press Global radius **+1** (all layers become r=6), then Save as **Tower30** again | Still exactly one Tower30 entry (no duplicate); replace-loading it elsewhere now gives r=6 layers — same-name Save overwrites | |
| S10B-13 | v2-3 | Clear the name box so it is empty, press **Save** | Nothing saved — no nameless entry in the list or the file. **Q35** (still open — the user has not ruled): this is the build's default; record anything different | |
| S10B-14 | v2-3 | Save **BigCircle** (one circle, r=128). Set `maxRadius` 64 in `ModConfig\shapeprojector.json`, restart, **Load (replace)** BigCircle onto a fresh projector | Loads **clamped to 64** — the load path is "the standard server-authoritative edit packets", so server caps apply exactly as for manual edits (display per Q26) | |
| S10B-15 | v2-3 | Set `maxLayersPerProjector` 10, restart, **Load (replace)** Tower30 (30 layers) onto a fresh projector. Restore both config values to defaults afterwards and restart | Build default under **Q33** (user has not ruled): truncated to the first 10 layers, and no crash. Record what you see, including whether it is reported | |
| S10B-16 | v2-3 | Press **Delete** on Tower30 | Gone from the list; every projector that previously loaded it **keeps its layers** (the preset was copied in, not linked); the file no longer contains Tower30 (MoatTrio still there) | |
| S10B-17 | v2-3 | **Quit the game. Back up `presets.json`.** In a text editor, break ONE entry: replace its whole value with the quoted word `"garbage"` (e.g. `"MoatTrio": "garbage"`), leaving at least one other entry valid. Start the game, enter the world, open a projector | A chat line reports **1 preset skipped**; the valid preset(s) are still listed and still load; **no crash** — "skip bad entries, report, never crash" | |
| S10B-18 | v2-3 | Still in that session, save a new preset **AfterBreak**. Quit and reopen `presets.json` in the editor | AfterBreak was added AND the `"garbage"` entry is **still in the file** — skipped ≠ deleted; a Save never silently destroys a broken entry you might want to repair by hand | |
| S10B-19 | v2-3 | Quit. **Truncate** `presets.json` (delete the second half of the file so it is not valid JSON at all). Start, enter the world, open a projector. Afterwards quit and restore your backup | No crash; the preset list is **empty**; **Save and Delete are disabled** (the mod refuses to write over a file it could not read); after quitting, the truncated file on disk is **unchanged** — the mod never wrote to it. **Q45:** the spec promises only "report, never crash" for this case; the disabled buttons and untouched file are the build's protective contract — record | |
| S10B-20 | v2-3 (2 players) | A's projector inside A's land claim; B has no edit rights. B opens the GUI and presses **Load (replace)** with one of B's own presets | **Rejected like any edit** — projector unchanged for both players; "loading writes layers through the standard server-authoritative edit packets — the preset system never bypasses BE authority". Pair with the claims companion mod if convenient, but **the §11 matrix is never evidence for this row** — only this manual check is | |
| S10B-21 | v2-3 | **Load (replace)** MoatTrio onto a projector on different terrain (the other side of HILL, or after digging) | Draped layers drape on the terrain **there** — a preset stores configuration, not block positions | |
| S10B-22 | v2-3 | On a projector: set the master switch ("Projector on") **OFF**, with the MoatTrio layers configured underneath. Save as **AllFields**. Load (replace) onto a fresh projector | Everything arrives, including the master switch state: the loaded projector is dark and projects nothing until you flip it on; underneath, every layer field matches — shapes, parameters, vertical modes, fluid rules, colours, Y offsets, the disabled flag. The master switch is user ruling 7 (STATUS.md), not yet spec text; whether it belongs in a preset is part of **Q42** — record what you see | |



## §10c — Preview pane — SUPERSEDED by the hologram amendment. Do not run.

> The 2026-09-01 spec amendment removed the GUI preview pane entirely. §10c is now a **holographic miniature floating above the projector block, in the world** — walking around it is the orbit — and the dialog reorganizes per the new **§10e** (two columns of titled groups, icon toolbar). If any preview pane, or any drag/scroll orbit control, still appears in the dialog, that is itself a FAIL (S10E-08 / S10-07 — the spec no longer asks for it). Run S10CH-01..13 and S10E-01..08 instead.
>
> **ID mapping (S10C → new):** 01 → S10E-01/02 (layout; nothing dropped) · 02 → S10CH-02 (matches world) · 03 → S10CH-05 (current dug state) · 04 → S10CH-03 (tower framing) · 05 → S10CH-06 (selected-layer brightening) · 06 → S10CH-08 (in-mini dot; the world marker itself is gone — ruling 10, S10CH-14) · 07 → **retired outright** — there is no drag-rotate or scroll-zoom any more; world alignment is tested at S10CH-04 · 08 → S10CH-07 (updates) · 09 → S10CH-07 (**Q47** remapped to the hologram) · 10 → S10CH-11 (master switch) · 11/12 → S10CH-13 (`previewMaxBlocks` / performance).

## §10c — Holographic miniature (v2 amendment — run now)

Step column "v2-4". You need: the **moat-trio** projector (S10B-05 / C-01..05) on HILL with POND in reach, and the **tower** (S10D-02; S10D-07 grows it to 48 for row 13). Config keys: the amendment adds `holoSize` (default 1.5) and `holoMode` (`always`|`guiOpen`|`off`); STATUS.md **ruling 9** (user-directed, not yet spec text) adds `holoStyle` (`layered`|`mono`); **ruling 10** (STATUS.md 9a) adds `holoOffsetY` (default 0.5) and **removes the world-space centre marker entirely** (rows 8, 14, 15); `previewMaxBlocks` (default 20000) now budgets the hologram. This supersedes S7-01's key count again. Rows 10, 13 and 15 edit the config and restart — restore defaults afterwards. **Every S10CH row is client-half (renderer + GUI). The S10D-26 gate rule applies in full: a green §11 matrix is NEVER evidence for any row here — this manual pass is the only evidence that exists.**

**Quick pass (hand to the user verbatim):**
- The GUI preview pane is gone, and so is the old full-size white centre cube (ruling 10): the hologram is the ONLY thing above the block, hovering just over the lens so block + projection read as one device — no white cube from any angle. It shows every enabled layer in its layer colour: the moat trio reads as the moat trio, the 30-layer tower reads as a stack of 30 courses, and draped lines hug the hillside in miniature and follow your digging.
- Walk a full circle around it: north in the mini stays north — it must NEVER rotate to face you. Line a mini feature up with its full-size twin (the pond side of the ring) and check it stays lined up from every side.
- Open the GUI: the selected layer brightens in the mini and follows every click and every Add Layer Up press; close the GUI and the brightening is gone. Apply, add/remove layer, and both preset loads move the mini and the world together; a value merely typed does not move it until Apply (Q47 if it does).
- Stand so a full-size SAME-coloured world outline is directly behind the mini (the screenshot scenario): it must stay legible — dark cube edges, brighter than the world ghosts, dark halo behind it (ruling 9). `holoStyle` "mono" turns the whole mini single cyan.
- `holoMode` always/guiOpen/off, the `O` hide hotkey, and the "Projector on" master switch all control it; off/hidden/off means no mini. None of this is covered by the compat matrix; only this manual pass counts.

| ID | Step | Do this | Expect (spec §10c amended) | Result |
|---|---|---|---|---|
| S10CH-01 | v2-4 | Open the moat-trio projector's GUI, then close it; look above the block both times | **No preview pane anywhere in the dialog.** Above the block floats a **live holographic miniature of all enabled layers** — tiny translucent cubes in **layer colours**, inside a volume of roughly `holoSize` (default 1.5) blocks. Disabled layers do not appear in it | |
| S10CH-02 | v2-4 | Enable all three moat-trio layers. Compare the mini against the world: count-in-proportion the ring pair, the path, the wall along one axis direction | **They cannot disagree** — "the same cached per-layer position sets as the full-size outlines — the hologram never computes its own geometry." Same relative widths (ring 40/46 with the 2-block gap, path 22, wall 60, all in scale), same layer colours, same centre. Any block on the mini's axis row that has no full-size twin is a FAIL | |
| S10CH-03 | v2-4 | Open the 30-layer tower projector; touch nothing; look at the mini | The whole tower fits the hologram volume — "auto-scaled so the bounding box of all enabled layers fits" — and **reads as a stack**: 30 distinct courses, one above the other, not collapsed into one circle. No input needed to frame it | |
| S10CH-04 | v2-4 | Pick a mini feature with a world twin (the pond-crossing arc of the ring). Walk a **full slow circle** around the projector, watching the mini | **World-aligned, never rotating: north in the miniature = north in the world.** The pond-side arc of the mini stays on the pond side from every viewing angle — the mini never turns to face you; your walking IS the orbit. Any billboard-style rotation is a FAIL | |
| S10CH-05 | v2-4 | Look at the draped ring and path in the mini on HILL; then dig a 3-deep trench under ~10 outer-ring ghosts (C-08/09 style) and look again | "Draped layers render with their sampled terrain heights" — the mini's ring and path **hug the hillside in miniature**, rising and falling with the real slope, level at the bank line over POND (fluid on). After digging, the mini shows those courses **down on the trench floor** — current dug state, not a snapshot | |
| S10CH-06 | v2-4 | GUI closed: note the mini. Open the GUI; click layers 1, 15, 30; press Add Layer Up three times; close the GUI | Brightening exists **only while the GUI is open**: "while the GUI is open, the selected layer renders brighter." It follows every click, and during the Up presses the bright course is always the newest, one above the last — "Up/Out chaining reads as visible growth." GUI closed → all layers back to equal brightness | |
| S10CH-07 | v2-4 | In one sitting: change a radius and Apply; add a layer; remove a layer; Load (replace) MoatTrio; Load (append) anything. Separately: type a radius but do NOT Apply, watch the mini, then Apply | Mini and world ghosts change **together on every applied edit and both preset loads** — same server-echo path (S10B-07), no reopen needed. The typed-but-unapplied value does **not** move the mini until Apply — **Q47** (remapped from the pane to the hologram): a deviation either way is Q47, NOT a bug | |
| S10CH-08 | v2-4 | Moat trio at offset 0.5/0.5: find the projector's position in the mini. Then set offset 12.5 / −3 (S3-05 style) and look again. Then offset 0 / 0 | "**Center honesty**": the hologram volume centres above the block, and when the centre offset ≠ 0 **a small marker inside the mini shows the projector's own position within the model** — per ruling 10 a **dot** (~0.4 of a mini cell), **never cube-like**; if it reads as another hologram cube, FAIL. At 0.5/0.5 it is barely off the mini's centre; at 12.5/−3 clearly to one side, same direction and proportion as the real projector stands from the real circle. **At offset 0/0: no dot** (offset ≠ 0 only) | |
| S10CH-09 | v2-4 | Set offset **60 / 0** with a single circle r=5 (maxRadius default). Look at the mini. Restore the offset after | **OBSERVE-AND-RULE (Q48):** build interpretation — the projector's own position is **included in the mini's framing** so the marker stays inside the volume; with a huge offset the circle shrinks to a small figure near one edge, marker near the other. The spec says only "bounding box of all enabled layers", which would exclude the projector and leave the marker outside. Record what you see. If the user dislikes the shrink, it is a **spec question for the user**, not a bug for an agent | |
| S10CH-10 | v2-4 | Quit; set `holoMode` to `guiOpen`, restart: check with GUI closed and open. Then `off`, restart. Then back to `always`, restart | `always` (default): mini shows whenever ≥1 layer enabled. `guiOpen`: mini only while the GUI is open, gone when closed. `off`: never, GUI open or not. World outlines unaffected in all three | |
| S10CH-11 | v2-4 | With the mini showing: press the hide hotkey (`O`), then again. Then flip "Projector on" OFF, then ON | `O`: the mini vanishes **along with** all projections, locally ("the existing client hide hotkey suppresses it"); `O` again → back. Master switch OFF: world ghosts AND the mini all empty — no enabled positions, nothing to draw (ruling 7; the world centre marker in ruling 7's wording no longer exists per ruling 10); ON → all back together | |
| S10CH-12 | v2-4 | **Ruling 9 screenshot scenario:** stand so a **full-size same-coloured world outline is directly behind the mini** from your eye (the cyan draped ring rising behind the mini's cyan ring on the uphill side) | Mini stays **legible**: layer hues preserved but treated — thin dark edge outlines on every mini cube, visibly brighter/more saturated/more opaque than the world ghosts behind it, and a faint dark translucent backdrop halo behind the volume. **OBSERVE-AND-RULE (Q49):** the constants (brighten factor, halo darkness, edge thickness) are build choices — the acceptance is ONLY "legible in this scenario". Legible = PASS whatever the numbers; if the user dislikes the look, that is a spec question for the user, not a bug | |
| S10CH-13 | v2-4 | Quit; set `holoStyle` to `mono`, restart, look at the moat trio's mini. Then restore `layered`; set `previewMaxBlocks` to **500**, restart, open the **48-layer tower** (grow per S10D-07); watch the mini while walking a circle for ~30 s. Restore defaults after | `mono`: the whole mini renders **single cyan** — max separation, layer correspondence deliberately lost (ruling 9); `layered` restores per-layer hues. At `previewMaxBlocks` 500: mini **uniformly decimated** — thinned everywhere but still recognisably the whole tower, top included. At the default 20000: no frame-rate collapse, no stutter, while the world outline renders too. Stutter is a **FAIL**, not a question | |
| S10CH-14 | v2-4 | **Ruling 10 acceptance line.** Projector with layers enabled, any offset (try 0/0, 0.5/0.5, and 12.5/−3): stand at the block and walk a full circle around it, looking above it from every angle, near and far | **NO white cube from any angle** — the world-space centre marker is REMOVED entirely (it was the full-size white cube above the block). **The hologram is the only thing rendered above the block.** Centre indication is now the GUI resolved-centre readout (S3-06) plus the in-mini dot (S10CH-08) — nothing in the world. A surviving white cube is a FAIL against ruling 10, even though spec §3/§6 still ask for a marker (**Q50** — the spec text is the user's to fix, not the build's to follow) | |
| S10CH-15 | v2-4 | Look at where the mini sits relative to the block at default config. Then quit; set `holoOffsetY` to **0.1**, restart, look; then **1.5**, restart, look. Restore 0.5 after | New config **`holoOffsetY` (default 0.5)**: hologram volume base = **block top face + holoOffsetY** — at the default the mini **hovers close over the lens, so block + projection read as one device**, not a detached object floating high above. At 0.1 it sits nearly on the lens; at 1.5 it floats a block and a half up — the config visibly moves it both ways | |

## §10e — Dialog layout (v2 amendment — run now)

Step column "v2-4". Everything here is GUI-only; the S10D-26 gate rule applies — no §11 evidence, only this pass.

| ID | Step | Do this | Expect (spec §10e) | Result |
|---|---|---|---|---|
| S10E-01 | v2-4 | Open the GUI; read the overall layout | **Two columns of titled groups**, no single long scroll: **left — "Projector"** (on/off master switch, resolved centre readout, Offset X/Z) with **"Presets"** below it (dropdown, name field, Save/Delete, Load replace / Load append); **right — "Layers"** (layer selector + toolbar) with **"Layer settings"** below (shape dropdown, parameters, Y offset, vertical mode, colour, show-completed, enabled). **Apply / Close bottom-right.** No preview pane (its absence is part of this row) | |
| S10E-02 | v2-4 | Walk the whole dialog and exercise one of everything | **Nothing was dropped in the reorganization** — every v1+v2 function reachable: offsets and centre readout, master switch, full preset row incl. BOTH Load buttons, and all **nine toolbar actions as compact icon buttons**: Add, Remove, Duplicate, Move up, Move down, Add Layer Up, Add Layer Out, Radius +1, Radius −1. Each one still does what its S4/S10B/S10D row says | |
| S10E-03 | v2-4 | Hover the mouse over each of the nine icon buttons in turn, without clicking | **Every icon button shows a tooltip** naming its action — an icon you cannot identify by hovering is a FAIL ("compact icon buttons with tooltips") | |
| S10E-04 | v2-4 | Select a spiral layer and look at the Out icon; then grow to the 48-layer cap (S10D-07) and look at Add / Duplicate / Add Up / Add Out; click each greyed icon a few times | **Disabled states read as greyed icons**: Out greyed on a spiral (S10D-14), the add-family greyed at the 48 cap — and a greyed icon **does nothing when clicked**: layer count and layers unchanged. (Ruling 8 had cap refusal as a red toast; if the greyed icon replaces or accompanies the toast, record which under **Q8/Q38** — either is a visible refusal) | |
| S10E-05 | v2-4 | On one layer, switch the shape dropdown through circle → ring → ellipse → rectangle → polygon → triangle → spiral, watching the parameter fields | **Only the selected shape's parameters appear** — "dynamic fields, not a fixed stack": Radius for circle; Inner/Outer for ring; Radius X/Z for ellipse; Width/Depth for rectangle; Sides/Circumradius/Rotation for polygon; Circumradius/Rotation with NO Sides for triangle (S10A-02); Turns/Spacing/Start radius/Direction for spiral. No orphaned field from the previous shape ever remains visible | |
| S10E-06 | v2-4 | At **default UI scale**, on your normal monitor: open the dialog on the 48-layer tower with the longest preset name saved | **The whole dialog fits on screen with nothing cut off and no scrolling needed to reach any control** — the two-column regrouping "halves the height". (A scrollbar *inside* the layer selector list is fine; a scrollbar on the dialog itself, or Apply/Close off-screen, is a FAIL) | |
| S10E-07 | v2-4 | GUI open, focus not in a text field: press PageUp, PageDown, +, −. Then click into a text field and repeat, and type "-2" and an "o" | The four hotkeys still work exactly as before in the new layout (S10D-03/15/21: duplicate-up, duplicate-out, global ±1), and are still suppressed while typing in a field (S10D-22/23, Q41). The reorganization changed the layout, not the behaviour | |
| S10E-08 | v2-4 | Read every control in the reorganized dialog once more | **Nothing beyond §10e + the amended §10c**: no preview pane, no orbit/zoom controls, no hologram settings in the GUI (holoMode/holoStyle/holoSize are config-file keys, unless the spec grows a control), and nothing else new — anything extra is flagged under S10-07 and rejected however nice it is | |

## §10d — Layer-duplication & global adjustment shortcuts (v2, Phase 1 item 1)

Run top to bottom; later rows reuse earlier state where noted. Hotkey names (PageUp / PageDown / + / −) are the build team's — **the spec promises "GUI buttons + hotkeys while GUI open" but names no keys (Q37)**: if different keys work, record Q37, not a bug.

### Add Layer Up

| ID | Step | Do this | Expect (spec §10d) | Result |
|---|---|---|---|---|
| S10D-01 | v2-1 | Fresh projector on HILL, offset 0.5/0.5, one layer: circle r=5, Fixed Y, Y 0. Select it. Click **Add Layer Up** once | Layer count 2; the new layer is an identical circle r=5, same colour and mode, **Y offset +1**; and the **new layer is now the selected one** in the list ("the new layer becomes selected") | |
| S10D-02 | v2-1 | Without touching the layer list or any field, press Add Layer Up **28 more times**, as fast as you like | **30 layers**, Y offsets 0 through 29, all circle r=5 — a 30-course tower guide standing on the hillside, each course exactly one above the last, all concentric on the 2×2 corner. Repeated presses chained because selection followed the new layer — you never touched the dropdown | |
| S10D-03 | v2-1 | On a projector with a few layers, select one and press **PageUp** with the GUI open | Same behaviour as the button: duplicate, Y+1, new layer selected (Q37 on the key name) | |
| S10D-04 | v2-1 | Close the GUI. Press PageUp, PageDown, +, − while looking at the projector | **Nothing changes** — layer count and radii untouched ("hotkeys **while GUI open**"). Reopen the GUI to confirm | |
| S10D-05 | v2-1 | Make a layer with a non-default colour, mode **Drape**, and **enabled off**; select it; press Add Layer Up | The copy has the same colour, Drape, and is also disabled, at Y+1, and is selected — "duplicate selected" copies the whole layer | |
| S10D-06 | v2-1 | Select a **spiral** layer (turns 3, spacing 2, start 2); press Add Layer Up | Works — a spiral copy at Y+1. Only *Out* is disabled for spirals, not Up | |
| S10D-07 | v2-1 | From the 30-layer tower, keep pressing Up to **48** layers. At 48, press the button again, and try PageUp | Refused: layer count stays 48 (`maxLayersPerProjector` default 48, §10d/§7) with a **visible message** per build notes. **Q8/Q38:** the spec never says *how* refusal shows — record what you see | |

### Add Layer Out

| ID | Step | Do this | Expect (spec §10d per-shape semantics) | Result |
|---|---|---|---|---|
| S10D-08 | v2-1 | Fresh projector: circle r=5, Fixed Y, selected. Press **Add Layer Out** once | New selected layer: circle **r=6**, **same Y offset** (Y untouched — only Up changes Y), same colour and mode | |
| S10D-09 | v2-1 | Press Out 3 more times | Layers r=5, 6, 7, 8, 9, all at the same Y — concentric rings marching outward, "a circle becomes a filled disc guide — a plaza floor" | |
| S10D-10 | v2-1 | Ring inner 20 outer 23, selected; press Out | New ring **21–24** — inner **and** outer +1, thickness still 3 ("thickness preserved") | |
| S10D-11 | v2-1 | Ellipse rX=10 rZ=5, selected; press Out | New ellipse **11 / 6** — both radii +1 | |
| S10D-12 | v2-1 | Polygon sides 6, circumradius 10; press Out. Then a Triangle circumradius 10; press Out | Polygon copy circumradius **11**, sides still 6, rotation unchanged; Triangle copy circumradius **11** (alias follows polygon semantics) | |
| S10D-13 | v2-1 | Rectangle width 11 depth 7; press Out | New rectangle **13 × 9** — width and depth **+2**, "one block per side": in the world the new outline stands exactly one block outside the old on all four sides | |
| S10D-14 | v2-1 | Select the spiral layer; look at the Out button, then press PageDown | The **Out button is disabled** (greyed / unpressable) — "'out' has no honest meaning" for a spiral; PageDown does nothing. Add Layer Up remains available | |
| S10D-15 | v2-1 | Select a circle layer; press **PageDown** | Same as the Out button (Q37 on the key name) | |

### Global radius +1 / −1

| ID | Step | Do this | Expect (spec §10d) | Result |
|---|---|---|---|---|
| S10D-16 | v2-1 | Rebuild (or keep) the 30-layer tower: circles r=5, Y 0..29. Find the Global radius **+1 / −1** controls — they are **projector-level**, not on a layer row. Press **+1** once | **Every** layer is now r=6; **every Y offset unchanged** (still 0..29) — "a 30-layer cylinder at r=19 becomes r=20 in one click, still a cylinder." Spot-check the GUI values of layers 1, 15, and 30, and eyeball the world: the tower is one block wider top to bottom, no course moved up or down | |
| S10D-17 | v2-1 | Disable layers 10, 11, 12. Press +1 again | All 30 layers now r=7 **including the three disabled ones** (open their rows and read the value) — "applies to disabled layers too, so hidden layers never fall out of sync with the building" | |
| S10D-18 | v2-1 | Press −1 twice | Everything back to r=5; the report showed **no clamp**, so −1/+1 was a safe round-trip; the world outline is identical to before S10D-16 | |
| S10D-19 | v2-1 | **Mixed-clamp projector**, offset 0.5/0.5, four layers: ① circle **r=0.5**, ② ring 20–23, ③ spiral turns 3 spacing 2 start 2, ④ triangle circumradius 10. Press **Global −1** once | Ring → **19–22**; triangle → **9**; **spiral untouched and visibly noted as skipped** ("spirals skipped and noted"); **circle stays at 0.5 and the clamp is visibly reported** — "a layer that would underflow stays put and is reported … the report makes clamping visible." Where the report appears and what the exact minimums are: Q39/Q40 — record what you see | |
| S10D-20 | v2-1 | Now press **Global +1** once | Ring back to 20–23, triangle back to 10, spiral still untouched — but the circle is now **1.5**, NOT 0.5: the projector did **not** return to its starting state, because "−1 then +1 is a safe round-trip **only when nothing clamped**." Verify in the world: the centre circle is now 4 wide on the axis (r=1.5 at a corner centre), where it was 2 wide. This bigger-after-round-trip outcome is spec-intended; the −1 clamp report was the warning | |
| S10D-21 | v2-1 | GUI open, focus NOT in any text field: press **+** and **−** on the main key row, then on the **numeric keypad** | All four keys act exactly like the Global buttons (Q37 on key names) | |

### Hotkeys vs. text fields, config, and the gate rule

| ID | Step | Do this | Expect | Result |
|---|---|---|---|---|
| S10D-22 | v2-1 | Note the layer count and one other layer's radius. Click into a radius box and type **"3"**, then **"+"**, then **"-"**. Check the count and that radius again. Then click into the Y-offset box and type **"-2"** | The characters land in the field and nowhere else: **no layer appears, no global resize fires, no other layer changed**. This follows from the spec even though §10d doesn't say it: §4 Y offsets can be negative, so "-" **must** be typeable without resizing the projector | |
| S10D-23 | v2-1 | With the cursor still in a text field, press **PageUp** and **PageDown**. Also type a name containing the letter **"o"** into any text field (preset name box if present) | Expected: no layer is added and projections do not toggle hidden (`clientHideHotkey` "O") — no hotkey fires while typing. If PageUp/PageDown DO fire from inside a field, record it as **Q41** (spec is silent for those two keys) with exact steps — the user rules | |
| S10D-24 | v2-1 | Back up `ModConfig/shapeprojector.json`, delete it, restart, open the fresh file | Regenerated with `"maxLayersPerProjector": 48` — the §7 default as raised by §10d ("default raised 8 → 48"). This supersedes the old value 8 in S7-01/S4-10; S4-11's set-cap-to-3 test is unchanged | |
| S10D-25 | v2-1 | While running everything above, read the whole GUI once more | **No controls beyond what §10a/§10d (and any built §10b/§10c) specify.** Triangle, Add Layer Up, Add Layer Out, Global ±1 are in scope; anything else new gets flagged under S10-07 — rejected however nice it is | |
| S10D-26 | every | Standing rule, not a step: if anyone cites the §11 compat matrix / version sweep as evidence for ANY row in S10A or S10D | Rejected. The headless harness never executes the GUI, hotkeys, or renderer (spec §11: "the gates must never be cited as evidence for it"). The only evidence is this manual pass | |

## 2026-09-07 user requests — opacity, hologram-only, surroundings model, fill, solid thickness (run now)

Not in the spec; user requests recorded in docs/STATUS.md ruling 9f. Client-half only — nothing here is provable by the harness.

| ID | Step | Do this | Expect | Result |
|---|---|---|---|---|
| U07-01 | 3 | Projector group: set "Mark opacity %" to 100, Apply; then 5, Apply; then 43 | World marks go opaque, then barely visible, then back to the original look. The hologram looks the same at all three | |
| U07-02 | 3 | Set opacity to 100 on a layer with build feedback; fill one of its blocks | The filled mark is green at the same opacity as its neighbours | |
| U07-03 | 3 | Look through a hill at a mark (see-through) at opacity 100 and at 20 | The buried reveal follows the opacity too (it is dimmer than the open-view mark at every setting) | |
| U07-04 | 3 | Switch "World marks" OFF, Apply | No ghost cubes anywhere in the world; the hologram above the block still shows every figure; the emissive ring stays lit; "Show hologram" still hides the hologram on its own | |
| U07-05 | 3 | With World marks OFF, switch "Projector on" OFF | Hologram gone too (master switch beats both). Back on: hologram returns, world still empty | |
| U07-06 | 3 | Switch "Model surroundings" ON, radius 16, height 8, Apply (World marks ON) | The hologram now contains a miniature of the ground, walls, pits and buildings within 16 blocks and ±8 in height — land green, water surfaces blue — with the coloured figures in their true places among it. Nothing of the model appears in the world; the world marks are unchanged | |
| U07-07 | 3 | With the model on, place and break a block inside the radius; then one outside it | Inside: the model updates within a tick. Outside: nothing changes | |
| U07-08 | 3 | Model on, switch World marks OFF, then ON again | The grey model stays in the hologram both ways; only the world marks come and go | |
| U07-09 | 3 | Model radius 64, height 32 on a built-up area | Block-exact and still usable; the log may report the model truncated at the cell budget — it must never freeze the client | |
| U07-26 | 3 | Model radius 256, height 32; then place and break blocks near the projector | A coarse survey in 4-block tiles, framed on the projector; the client does not hitch per placement (the remodel follows about a third of a second after the last change) | |
| U07-10 | 3 | Circle r=6, thickness 6 (the field's tooltip names the number for "solid"), Fixed Y at +1 over a pit ~6 deep; switch "Fill up to level" ON, Apply | A solid disc at +1 AND every block from the pit floor up to it, in the same colour, column by column. No fill where the ground is already at or above +1 | |
| U07-11 | 3 | Fill one pit column with blocks up to the level | The fill marks in that column vanish as the ground rises under them; the disc mark on top turns green when its block is placed | |
| U07-12 | 3 | Same layer, "Follow terrain" instead of Fixed Y, Fill ON | Fill from every column's ground up to the HIGHEST ground under the disc. Place a block on that highest column: the level climbs by one (expected — Follow terrain climbs; the tooltip says to pin with Fixed Y) | |
| U07-13 | 3 | Fill ON over a pond, Fixed Y at bank level, fluid rule ON then OFF | ON: fill starts on the water surface (no marks in the water). OFF: fill starts at the pond bed — the water is full of marks | |
| U07-14 | 3 | Fill ON, thickness 1 (an outline only) | Only the outline's columns are filled — a ring of pillars, not a solid — because fill follows the figure's columns | |
| U07-15 | 3 | Type thickness 999 into a circle of radius 11 | The field clamps to 12 (the tooltip's "solid" number); the figure is a solid disc. Radius 3: clamps to 4. Ring 8–11: clamps to 4 | |
| U07-16 | 3 | Break and re-place a projector carrying opacity 20, World marks OFF, model ON, a filled layer | Everything comes back exactly as set (item attributes carry the new fields); a 1.0.0 projector loads with opacity 43, marks ON, model OFF, no fill | |
| U07-17 | 3 | Save a preset from a projector with a filled layer; load it into another | The loaded layer is filled | |
| U07-18 | 3 | Layer settings: open the Colour dropdown; pick several entries, Apply after each | Every entry shows four squares in its own colour beside its hex code, and nothing draws outside the group box. Each pick updates the preview square beside "Hex code" and the hex text, and after Apply the layer's world marks AND its hologram cubes are that colour | |
| U07-19 | 3 | Type #FF00FF in the layer's Hex code field (no entry is magenta), Apply | The preview square turns magenta, a "(custom)" magenta entry appears at the top of the list and is selected, the marks are magenta. Type #3CDC5A (the done green): the layer comes out one step off it, never identical to a built mark | |
| U07-23 | 3 | Model on with a figure whose radius is larger than the model radius, and a centre offset | The projector's own cell sits at the horizontal middle of the miniature; the figure may run past the model's edge | |
| U07-24 | 6 | Any layer at opacity 43; stand so a hill hides part of a mark | The buried part shows dimmed through the hill within the see-through depth (client-main.log must NOT contain "projectorghost shader failed to compile") | |
| U07-25 | 3 | Model on; switch "Figures in hologram" OFF, Apply | The miniature shows only the land, buildings and water — no figures — framed on the projector; the world marks are unchanged. Back ON: figures return | |
| U07-28 | 3 | Model on, block-exact radius (≤ 64), beside a tower or house whose roof overhangs its walls by a block; also a sealed room and a room with an open doorway | The walls under the eaves show in the miniature all the way down; the sealed room's inside is NOT drawn; through the open doorway the room's inner walls show. A hollow tower shows its sides, not a solid block | |
| U07-29 | 3 | Circle radius 256, thickness 256 (solid), Apply | Apply returns within about a second, no multi-second freeze; the figure draws in part; a line under the layer buttons reads "Layer 1 cut by the mark budget: N of M columns drawn ..."; the Thickness tooltip carries the Performance paragraph | |
| U07-30 | 3 | Stand on the projector, open the coordinate display (the game's own), then read the dial's resolved centre and the block-info panel | All three agree (offset 0,0: the centre equals the coordinate display's X and Z; offset 0.5 adds .5). Not ~512000 apart | |
| U07-31 | 3 | Circle radius 256, thickness 256, world marks on, model off; look at the world and the hologram | The whole disc, in the world and in the hologram, with no trimming line and no gaps; grid lines every block over it; frame rate unchanged. Model on beside it: the model appears in full, the disc unchanged (separate budgets). World marks off + Figures in hologram off: the hologram shows only the surroundings | |
| U07-32 | 3 | A plain radius 11 circle, thickness 1, and a 3-high wall with build feedback | The outline still reads block by block (grid lines), a filled block turns green with an exact one-block boundary, nothing z-fights with real block faces; the see-through reveal through a hill still works | |
| U07-22 | 3 | Model on, radius covering a pond or lake | The water shows as one flat sheet of blue cells at its surface; the bed is not modelled; the shore shows as a drop in the ground's own colours beside it. Ice is not water here (it shows as nothing if it has no collision box in the solid layer) | |
| U07-21 | 3 | Load a preset saved by 1.0.0; place a 1.0.0-era projector item | Old layers show their old colours (cyan/amber/violet…) and the matching list entry is selected | |

## §10 — Out of scope: these must NOT be present (now spec §12 after the v2 renumbering; IDs kept)

| ID | Step | Do this | Expect (spec §10) | Result |
|---|---|---|---|---|
| S10-01 | every | Configure any shape; wait 5 minutes; walk the line | The mod placed **zero** blocks ("Any block auto-placement — this projects intent; the player builds") | |
| S10-02 | every | Circle r=11 / ring 20–23 / rectangle | Hollow outlines only. No filled disc, no filled band between 20 and 23, no filled rectangle ("Non-outline fills") | |
| S10-03 | every | Read every GUI control | No plane orientation / vertical / wall-mounted option ("Vertical planes") | |
| S10-04 | every | Read every GUI control | No sphere, dome, cylinder-as-volume, or any shape with a height parameter ("full 3D") | |
| S10-05 | every | Read every GUI control | Presets (save / load replace / load append / delete) are IN scope (v2 §10b) and cross-projector copying via presets is expected to work — tested at S10B-01..22, not forbidden here. Still not required/expected: any *separate* copy mechanism beyond the §10b preset row | |
| S10-06 | every | Read every GUI control and the HUD | No build-progress percentage per layer (the green tint is fine; a number is not) | |
| S10-07 | every | Anything else the spec didn't ask for (extra shapes, extra commands, chat spam, HUD overlays) | Flag it. It is rejected however nice it is | |

---

## Canonical scenario — full run-through (the moat)

Run this end-to-end at step 5 (drape) and again at step 6/7. Record each line.

**Site:** HILL, with POND within ~25 blocks of where the centre will be so the ring 20–23 crosses the water.

| # | Do this | Expect | Result |
|---|---|---|---|
| C-01 | Choose a 2×2 of blocks on the hillside for the base centre. Place the projector on one of those four blocks. | Places. | |
| C-02 | Open GUI. Offset 0.5 / 0.5 (towards the other three blocks of the 2×2). | GUI shows the resolved centre. ~~the marker (step 6) sits on the shared corner~~ — **superseded by ruling 10**: no world marker exists; verify the corner via the GUI readout against the coordinate HUD, and the in-mini dot (S10CH-08). | |
| C-03 | Layer 1: **ring** inner 20, outer 23, mode **Drape** (default), fluid **on**, colour default. | Two lines, 40 and 46 wide, cyan, lying on the hillside like two ropes; over the pond, level on the water top (bank line). Two clear blocks between the lines on the axis. | |
| C-04 | Layer 2: **circle** r=11, Drape. | 22 wide, cyan, draped on the hill, concentric with layer 1. | |
| C-05 | Layer 3: **circle** r=30, mode **Fixed Y**, Y offset +4 (wall top). | 60 wide, flat ring 4 above the projector: buried into the uphill slope, floating over the downhill side. | |
| C-06 | Count: ring outer 46, ring inner 40, path 22, wall 60 — all along the row through the 2×2 centre. | Those exact widths (Q1 if not). | |
| C-07 | Walk the whole ring: is any ghost floating or buried on land? | None — every draped ghost is one above its column's top block. | |
| C-08 | Start digging the moat: remove the top block under 10 consecutive outer-ring ghosts. | Each ghost drops onto the new floor as its block is removed. | |
| C-09 | Dig those 10 columns 3 deep. | Ghosts are now 3 lower, on the moat floor, still one continuous line. | |
| C-10 | Dig the *inside* of the moat (between the two lines) 3 deep for a stretch. | The two lines stay on the moat floor edges; the middle has no ghosts (no fill). | |
| C-11 | Over-dig: remove a block *outside* the outer line. | The outer line does not move sideways — it still marks radius 23 and now sits on the lowered floor there only if that column *is* an outline column. | |
| C-12 | At the pond: toggle layer 1 fluid **off**. | The ring drops from the water top to the pond bed. Toggle on: back to the bank line. | |
| C-13 | Fluid off; dig the pond bed 1 deeper under a ghost. | Ghost follows down under water. | |
| C-14 | Place cobblestone on 5 wall-ring (layer 3) positions where it floats. | Those 5 turn green (step 6). | |
| C-15 | Press `O`. | Everything vanishes for you; a second player still sees it (if available). `O` again → back. | |
| C-16 | Disable layer 2. | Path circle gone; ring and wall remain; block still glows. | |
| C-17 | Leave to menu, rejoin. | All three layers, modes, fluid setting, colours, offsets exactly as left; drape heights reflect the current dug state. | |
| C-18 | `/gamemode survival`, break the projector, re-place it on a *different* one of the four centre blocks, adjust offset to point at the same corner. | All three layers intact from the item; after fixing the offset the outlines land on the exact same blocks as before. | |
| C-19 | Two players (if available): B changes wall r=30 → 32 while A watches. | A sees the wall ring grow to 64 wide without doing anything. | |
| C-20 | Look at everything once more. | No block was placed by the mod. No fill. No vertical anything. | |

---

## SPEC QUESTIONS raised (for the user to rule on — none of these are bugs)

| Q | Section | Question |
|---|---|---|
| Q1 | §3/§5 | Parity-mismatch ties (integer r at corner, half-integer r at block centre): inner block (22 / 23) or outer (24 / 25)? Written as inner per the midpoint algorithm. |
| Q2 | §2 | Does "any solid surface" include wall sides and ceilings? |
| Q3 | §2 | Outside any claim, can anyone edit? |
| Q4 | §3 | Offset not on a 0.5 step (e.g. 0.3): reject, round, or accept? |
| Q5 | §3 | Is there a maximum centre offset? |
| Q6a | §3/§6 | ~~At what height is the centre marker drawn?~~ **Moot since ruling 10** — the world marker no longer exists (see Q50). |
| Q7 | §4 | Does layer order affect anything visible (overlap priority)? |
| Q8 | §4 | How is the 9th layer refused (greyed / message / silent)? |
| Q9 | §4 | Lowering `maxLayersPerProjector` below an existing projector's layer count — what happens to the extras? |
| Q10 | §5 | Radius not integer/half-integer (11.3): reject, round, accept? |
| Q11 | §5 | Radius 0 or negative? |
| Q12 | §5 | Ring with inner > outer? |
| Q13 | §5 | Ring with inner = outer? |
| Q14 | §5 | Rectangle even width at a block centre (parity mismatch): which way does it shift? |
| Q15 | §5 | Polygon rotation 0° — which direction does the first vertex point? |
| Q16 | §5 | Fractional spiral turns allowed? |
| Q17 | §5a | Ellipse default vertical mode (not listed in §5a). |
| Q18 | §5a | Do plants / snow layers count as "surface" for drape? |
| Q19 | §5a | Tree canopy over an outline column — ghost on leaves or on ground? |
| Q20 | §6/§9 | Is `seeThroughDepth` vertical distance or terrain thickness along line of sight? |
| Q21 | §6 | Do slabs / stairs / fences / glass / fluids count as "solid" for the done tint? |
| Q22 | §6 | "Optional per-layer" — is a per-layer feedback toggle required? |
| Q23 | §6/§5a | Drape + done tint conflict: placing a block on a draped ghost — does it turn green and stay, or rise one block? |
| Q24 | §6 | Does the hide-hotkey state persist across relog? |
| Q25 | §6 | Is `renderDistance` measured to the projector or to each ghost cube? |
| Q26 | §7 | `maxRadius` — does the GUI clamp the field, refuse the value, or show the raw value while rendering the clamp? |
| Q27 | §7 | Malformed config file — behaviour? |
| Q28 | §7 | Housing: mixed cupronickel + brass ingots allowed? |
| Q29 | §7 | **Blocking for step 7:** the recipe lists 10 ingredients; a 3×3 grid has 9 cells. Also no grid layout is given. |
| Q30 | §7 | Does the block emit actual block light, or only look emissive? |
| Q31 | §7 | Ellipse default colour (not listed in the palette defaults). |
| Q32 | §9 | Breaking the projector while its GUI is open — expected behaviour? |
| Q33 | §4a→§10b | Loading a preset with more layers than `maxLayersPerProjector`: truncate or refuse the load? Build default (user has NOT ruled): replace-load truncates to the cap (S10B-15). Sibling of Q44 for append. |
| Q34 | §4a/§8 | ~~At which build step do the §4a preset checks first apply?~~ **Moot since v2:** presets are §10b, landing at v2 Phase 1 item 3 (S10B step "v2-3"). |
| Q35 | §4a→§10b | Preset name rules: empty name, names differing only by case, very long names? Still open; build default: empty-name Save is ignored (S10B-13), names otherwise free-form, overwrite matches exact name. |
| Q36 | §10a | "Aliasing" direction: must a polygon manually set to sides=3 re-display as "Triangle"? And does Triangle inherit polygon's defaults (Fixed Y, amber)? Written as yes; a deviation is Q36, not a bug. |
| Q37 | §10d | The spec promises "hotkeys while GUI open" but names **no keys**, and §7 config gained no rebind entries for them. Build team says PageUp (Up), PageDown (Out), +/− main row and keypad (Global). Which keys actually work, and do they appear in VS Controls / are they rebindable? |
| Q38 | §10d/§4 | Refusal mechanism when Add Layer Up/Out hits the 48 cap — same gap as Q8. Build notes say "visible message"; the spec is silent. |
| Q39 | §10d | "Clamped at each shape's minimum" — the minimums are nowhere stated. Circle floor 0.5 (the smallest §8-step-2 unit-tested circle)? Ring inner floor? Ellipse radii floor? Polygon/triangle circumradius floor? Rectangle floor (1×1? does the −2 step skip it from even widths)? Record the observed floor per shape. |
| Q40 | §10d | The clamp report / spiral skip note — "is reported", "noted" — WHERE does it appear (chat line, GUI banner, tooltip) and does it name which layers clamped/were skipped? Any clearly visible report that identifies the affected layers should pass; record the form. |
| Q41 | §10d | Hotkeys while the cursor is in a text field: "+/−/digits must not fire" is derivable (negative Y offsets must be typeable, §4), but the spec is silent on PageUp/PageDown inside a field. If they fire there, record it under Q41 for the user to rule — I will argue it is a bug, but the spec does not settle it. |
| Q42 | §10b | §10b defines a preset as the "**complete layer list** (all shapes, parameters, vertical modes, colors, Y offsets)" — **projector-level fields are not listed**. The build stores and applies on replace-load the **centre offset** (which the retired §4a explicitly included) and the **master switch** (itself only user ruling 7, not yet spec text). Which projector-level fields belong in a preset? Tested at S10B-06 and S10B-22; record, don't fail. |
| Q43 | §10b | **Append: whose centre?** The spec offers append with no word on the centre offset. The build keeps the CURRENT projector's centre and ignores the preset's (S10B-09) — written as expected; the user rules. |
| Q44 | §10b | **Append meeting the 48-layer cap:** spec silent. Build: appends what fits and reports the dropped count on the report line (S10B-10); on an already-full projector, refuses outright with the cap message (S10B-11). Confirm both halves, and the exact report wording. Sibling of Q33 (replace truncation). |
| Q45 | §10b | **Fully unreadable preset file** (not one bad entry — the whole file): the spec promises only "skip bad entries, report, never crash". Build: empty list, Save/Delete disabled, file left byte-for-byte untouched (S10B-19) — a protective contract the spec doesn't state. Confirm. |
| Q46 | §10b | **Migration from the §4a location** (`ModConfig\shapeprojector-presets.json` → `ModData\shapeprojector\presets.json`): the spec says nothing. Build: migrates on first run, leaves the old file untouched as backup (S10B-01). If old presets do NOT appear, that is user-data loss — report it; and confirm the old file must never be deleted by the mod. |
| Q47 | §10c | **Applied vs typed** (remapped from the retired preview pane to the hologram): the recompute is event-driven on **applied** edits, so the mini shows the applied state and a typed-but-unapplied value does not move it until Apply (S10CH-07). Which reading is intended? A deviation either way is recorded here, not filed as a bug. |
| Q48 | §10c | **OBSERVE-AND-RULE — mini framing with centre offset ≠ 0:** the build includes the projector's own position in the mini's framing so the offset marker stays inside the volume — with a huge offset the shape shrinks to a small figure near one edge (S10CH-09). The amended spec says only "bounding box of all enabled layers", which excludes the projector. If the user dislikes the shrink, this is a spec question for the user, not a bug. |
| Q49 | §10c / ruling 9 | **OBSERVE-AND-RULE — contrast constants:** ruling 9 names the treatments (dark cube edges, boosted brightness/saturation/opacity vs. world ghosts, dark backdrop halo) but no numbers. The exact constants (brighten factor, halo darkness, edge thickness) are build choices; the acceptance is ONLY "legible in the screenshot scenario" (S10CH-12). Dissatisfaction with the look goes to the user as a spec question, never to an agent as a bug. |
| Q50 | §3/§6 vs ruling 10 | **Spec text vs ruling 10 mismatch:** §3 still says the centre is marked visually by "a small distinct ghost marker" and §6 still lists "Center marker rendered distinctly" — but ruling 10 (STATUS.md 9a) REMOVED the world-space marker entirely; centre indication is now the GUI readout + the in-mini dot. The build follows the ruling (S10CH-14 inverts the check); the spec file has not been edited, and no agent may edit it. The user resolves the text on the next spec edit. Until then: absent marker = correct; a marker's presence = FAIL. |

## Check counts

| Section | Checks |
|---|---|
| Step 3 — run this now | 24 (pointers into the sections below) |
| §2 block | 13 |
| §3 centre model | 11 |
| §4 layers | 16 |
| §4a presets (spec v1.1) | 0 — superseded by §10b; ID mapping kept in the §4a stub |
| §5 shapes | 29 |
| §5a vertical modes | 23 |
| §6 rendering | 26 |
| §7 config / recipe / handbook / visual | 24 (+1 blocking spec question row) |
| §9 edge cases | 12 |
| §10a triangle (v2) | 7 |
| §10b presets (v2) | 22 (S10B-01 must run first, before any Save) |
| §10c preview pane (v2) | 0 — superseded by the hologram amendment; ID mapping kept in the §10c stub |
| §10c holographic miniature (v2 amendment) | 15 (+ 5-bullet quick pass; rows 10, 13, 15 need config edits + restarts; rows 8/14/15 carry ruling 10) |
| §10e dialog layout (v2 amendment) | 8 |
| §10d shortcuts (v2) | 26 (incl. the S10D-26 standing gate rule) |
| §10 out of scope (spec §12 after renumbering) | 7 (S10-05 amended by v1.1: presets no longer forbidden) |
| Canonical moat run-through | 20 |
