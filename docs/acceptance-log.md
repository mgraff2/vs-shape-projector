# Acceptance log (Gubsy gate)

Each spec §8 step is recorded here once it has been reproduced in the running mod on the spec scenario. "Passed" means the user (or Gubsy) observed it in-game; nothing else counts.

| Step | Date | Result | Notes |
|---|---|---|---|
| 1 — block + BE + hardcoded circle as ghost cubes | 2026-09-01 | **Passed** (user in-game, VS 1.22.7, creative world) | Radius-11 cyan ring rendered via `EnumRenderStage.OIT` + engine `Blockhighlights` shader; colour byte order correct; break/unload paths OK. Follow-up: outline moved from Y+1 to the projector's own level (spec §5a Fixed Y = projector Y + offset 0). |
