# Jonastech Shape Projector — project instructions

Vintage Story 1.22.x mod, modid `shapeprojector`, **dual-side** (server-authoritative
parameters, client-side rendering). The spec at `docs/shape-projector-mod-spec.md` is the
contract: no agent edits it; ambiguity escalates to the user. The build is executed by the
five agents in `.claude/agents/` (see its README for coordination); `docs/api-notes.md` is
the Archivist's citation ledger — every game-API fact in code cites it.

## Build

The system `dotnet` is an older SDK that refuses the game's `net10.0` references. Build with
the user-scoped SDK:

```
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src\ShapeProjector\ShapeProjector.csproj -c Release
```

Game references resolve from `%APPDATA%\Vintagestory`. Geometry unit tests:
`& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests\ShapeProjector.Geometry.Tests`.

## Testing — two gates, both mandatory (spec §11)

### Gate 1: compat matrix — after ANY code change, before ANY commit

```
.\tools\compat-test.ps1
```

Builds the zip, then boots a headless dedicated server once per mod combination (solo, +each
companion, all together) and fails on any `[Error]`/`[Warning]`, a wrong mod count or load
order, or a violated marker. The "all together" combo is the playbook's heavy-modlist smoke
row.

### Gate 2: game-version sweep — before EVERY release

```
.\tools\version-sweep.ps1
```

`modinfo.json` promises `game: 1.22.0`, and that is a promise to every player on every patch
in the 1.22 line. The sweep builds the zip ONCE and runs the whole matrix against a real
downloaded server for each of 1.22.0–1.22.7 (the CDN-probed line as of 2026-09-01; when a
new patch ships, append it to `-Versions` — the CDN 404s on versions that don't exist,
which is how you find the latest).

### What the gates prove — and what they can NEVER prove

This mod is dual-side. The headless harness proves: the zip unpacks, the assembly loads, the
mod systems instantiate, block/BE/recipe registration ran (exact-count markers below), and
the server boots clean alongside every companion.

**The harness cannot see the client half. The renderer, GUI, preview pane, and hotkeys never
execute on a dedicated server. A green matrix is NEVER evidence for client behavior — that
evidence is Gubsy's manual acceptance pass (docs/acceptance-checklist.md), and only that.**
Citing the gates for client behavior is a rejected claim, not a shortcut.

### Marker policy (dual-side)

Registration logs exact-count server Notification lines, pinned by the harness — each must
appear EXACTLY once in server-main.log, and together with the dependency-sort line and the
server's own instantiation-inventory header they must account for every case-sensitive
mention of the modid, with path/zip-filename echoes blanked (an unexpected extra mention
fails the combo):

- `[shapeprojector] Registered block class …`
- `[shapeprojector] Registered block entity class …`
- `[shapeprojector] Loaded grid recipes: N`

plus `[shapeprojector] Loaded assembly` and `Instantiate mod systems for shapeprojector` in
server-debug.log. Why exact-count and not "appears": a naive "should not appear" / "appears
somewhere" match is fooled by path echoes and by duplicated registration — the count is what
catches an upstream change silently breaking or doubling the integration.

**No `IsModEnabled` branches exist.** Standing rule: the commit that introduces one must, in
the same commit, add its exact-count `require` marker (companion present) and `forbid`
marker (companion absent) to the compat test.

### Companion set (the one designated edit point in compat-test.ps1)

Derived from this mod's real interaction surface, not another project's list:

- **carryon** — the sharpest seam: carrying a placed projector with BE intact vs. the spec's
  break-drops-item-with-config path are two mechanisms for moving a configured block, and
  they can disagree; BOTH must preserve the layer list (both on Gubsy's checklist).
- **chiseltools** — block-manipulation-class mod; exercises block swap/removal around the BE.
- **farseer** — custom-renderer-heavy mod; `IRenderer` registration coexistence.
- **claimsradar** — claims-domain mod (the 1.22-ready pick); the claim-gated GUI-edit path
  itself needs a player and lives on Gubsy's manual pass.
- The **all-together combo** is the heavy-modlist smoke row.

### Things that made these gates lie (keep the failure with the rule)

- **`exit 0` at the end of compat-test.ps1 is load-bearing.** The sweep reads
  `$LASTEXITCODE`, which only native commands and `exit` set; without it a `-SkipBuild` run
  leaves a stale code and a fully passing matrix reports as all-FAIL.
- **BLOCKED must survive the trip up to the sweep.** `compat-test.ps1` exits 2 for BLOCKED, but
  `version-sweep.ps1` originally collapsed every non-zero code into FAIL — so a port race on one
  version reported "VERSION SWEEP FAILED: 1.22.7" for a version the mod was never actually tested
  on. Caught during the 1.0.0 release run (2026-09-02); the re-run passed. The sweep now maps
  exit 2 to BLOCKED and reports it with SETUP as "not tested", never as a mod failure. The
  general rule: an exit code that distinguishes two outcomes is worthless if the caller throws
  the distinction away.
- **SETUP is not FAIL.** "This version could not be tested" (half-extracted server package
  flooding the log with unrelated `[Error]`s) and "the mod is broken" call for different next
  actions. Same for BLOCKED (exit 2): another server on the port — a singleplayer world
  counts — is an environment problem, not a mod failure.
- **Extraction is verified against the archive's own entry count** with a completion stamp;
  `Expand-Archive` was caught truncating a ~9600-file archive to ~1400 without error.
- **Temp data paths are keyed by `$PID`** so a hand-run test and a running sweep can't delete
  each other's directory mid-boot ("server did not start" that isn't).
- **A transient failure is re-run and said out loud**, never quietly re-rolled until green.
- The scripts came from the Tallybook playbook but are adapted to THIS project (nested
  src/ layout, mod-folder packaging with assets/, dual-side markers). Keep them generic:
  they self-discover modid/version/assembly from `modinfo.json` and must never hardcode
  this mod's name — the companion block is where mod-specific choices live.

## Release flow

1. Both gates green. 2. Stage `dist/shapeprojector_X.Y.Z.zip` into
`%APPDATA%\VintagestoryData\Mods\` (removing older copies) for local testing — the user runs
the in-game test. 3. Publish only on explicit go-ahead. Keep the CHANGELOG in the player's
vocabulary, not the code's.
