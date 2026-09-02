# Acceptance log (Gubsy gate)

Each spec §8 step is recorded here once it has been reproduced in the running mod on the spec scenario. "Passed" means the user (or Gubsy) observed it in-game; nothing else counts.

| Step | Date | Result | Notes |
|---|---|---|---|
| 3–7 + all v2 work, as shipped in 1.0.0 | 2026-09-02 | **Passed** (user in-game, VS 1.22.7, from the release zip in Mods/) | Ran against `shapeprojector_1.0.0.zip` — the packaged artifact, not a loose build. Covers everything from this session: the OpenDialog double-close crash fix, holoOffsetY 0.0, the full tooltip pass, ring inner/outer copy, selected-layer highlight OFF, the toolbar-icon re-registration fix, the "Show hologram" switch, per-layer Thickness and Height, maxCellsPerProjector, maxRadius 256, and the 4-connected circle/ellipse rasterization. Verdict was a general "works great", NOT a line-by-line walk of acceptance-checklist.md — so the checklist's numbered rows (S10CH-01..13, S10E-01..08, S4A-01..12) remain individually unticked. Anything later found broken in those areas is a gap in this pass, not a regression. |
| 1 — block + BE + hardcoded circle as ghost cubes | 2026-09-01 | **Passed** (user in-game, VS 1.22.7, creative world) | Radius-11 cyan ring rendered via `EnumRenderStage.OIT` + engine `Blockhighlights` shader; colour byte order correct; break/unload paths OK. Follow-up: outline moved from Y+1 to the projector's own level (spec §5a Fixed Y = projector Y + offset 0). |
