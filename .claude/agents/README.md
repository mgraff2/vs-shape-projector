# Shape Projector — agent team

Five subagents for building the Jonastech Shape Projector (Vintage Story 1.22 mod, modid `shapeprojector`) from the spec at `docs/shape-projector-mod-spec.md`.

| Agent | Role | Gate |
|---|---|---|
| `archivist` | API verification; step-1 rendering prototype; item codes; §10c hologram (amendment: in-world, no in-dialog hook) | Runs from the start |
| `geometer` | Pure-function rasterization and center math; §10a triangle alias; §10d radial semantics + clamping — tests first | Runs from the start |
| `renderer` | Client-side ghost-cube `IRenderer`, culling, performance; §10c hologram | Waits on Archivist step-1 verdict; preview waits on the §10c mechanism verdict |
| `curator` | Shape JSON, textures, lang, handbook, recipe, blocktype/itemtype | Waits on Archivist step-1 verdict + verified item codes |
| `gubsy` | Adversarial player; acceptance against the spec (v1 scenario + v2 loop); client-half verification the §11 harness cannot do; definition of done | Last gate on every step |

## Shared rules (in every agent prompt)

- The spec is the contract. No agent edits it. If it is wrong, ambiguous, or blocking, the agent stops and surfaces the question to the user.
- Target is Vintage Story 1.22.x. Class names, method signatures, item codes, and asset formats are never trusted from memory — they come from the Archivist, read from source/docs this session.
- Build order follows spec §8 (v1) and the phased v2 order below. Nothing is "done" until Gubsy has tried it against the spec.
- The §11 compatibility gates are mandatory: the compat matrix (`tools/compat-test.ps1`) runs after any code change and before any commit; the version sweep (`tools/version-sweep.ps1`) runs before any release.

## Coordination

- **Archivist and Geometer run in parallel from the start** — zero dependency between API research and pure math.
- **Renderer, Curator, and everything else wait on the Archivist's step-1 verdict** on the rendering path (spec §8 step 1: block + BE + one hardcoded circle as ghost cubes).
- **The §11 harness validates server-side load and companion coexistence only.** It is never evidence for client behavior (renderer, GUI, preview, hotkeys) — that is Gubsy's manual pass.
- **Disagreements between agents escalate to the user**; the spec is not a tiebreaker to be rewritten.

## Build order — v1 (spec §8)

1. Prototype: block + BE + one hardcoded circle rendered as ghost cubes — Archivist
2. Center offset + circle/ring rasterization with half-block centers; unit tests radius 0.5–64, both parities — Geometer
3. GUI: single layer edit — API via Archivist
4. Layers list; remaining shapes — Geometer (math), Archivist-verified GUI
5. Drape mode (§5a): column-height sampling, block-change recompute, fluid rule — Geometer (pure column logic) + Renderer (wiring); Gubsy tests on a hillside and while excavating
6. Build feedback tint, center marker, client hide hotkey, item-retains-config on break — Renderer / Archivist
7. Recipe + lore integration — Curator

## Build order — v2 (spec §10), gated by the §11 baseline matrix

1. Radial-adjustment semantics + clamping and triangle alias, unit tests first, game closed — Geometer
2. GUI shortcuts (§10d): Add Layer Up / Add Layer Out / Global radius ±1, hotkeys while GUI open; `maxLayersPerProjector` default 8 → 48
3. Presets (§10b): named save/load/delete, replace-or-append, malformed-file resilience, edits through standard server-authoritative packets
4. §10c hologram above the block + §10e two-column dialog reorg — Renderer (hologram) + orchestrator (GUI)

Each numbered item ends with Gubsy's pass on it. Each step ends with Gubsy reproducing it on the spec scenario.
