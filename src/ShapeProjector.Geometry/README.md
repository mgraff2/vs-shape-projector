# ShapeProjector.Geometry

Pure rasterization library for the Jonastech Shape Projector (spec §3, §5, §5a, §6 `maxRadius`, §9).
`net10.0`, no package references, no Vintage Story assemblies. Plain data in, plain data out.
Consumers: the block entity (parameter validation), the client renderer (position sets, drape), the GUI
(effective sizes, snapped vertices, error messages).

Tests: `tests/ShapeProjector.Geometry.Tests` (`dotnet test`).

## Coordinate frame

* Everything is relative to the **projector block at (0, 0)**. `BlockXZ(X, Z)` is a horizontal block
  position; `BlockXYZ(X, Y, Z)` a resolved one. X grows east, Z grows south (VS convention). The caller
  adds the projector's world position.
* Radii, sizes and centres are measured in **block-centre coordinates**: block `(x, z)` is the unit square
  centred on the point `(x, z)`. So a block's corner is at a half-integer coordinate.
* `ShapeCenter(dx, dz)`: the shape centre as an offset from the projector's block centre, both components
  finite multiples of 0.5 (validated in the constructor, `ArgumentException` otherwise).
  `(0,0)` = the projector block (odd diameters), `(0.5,0.5)` = the corner shared with the +X/+Z neighbours
  (even diameters, 2×2 centre), `(0.5,0)` = the +X edge (1×2 centre), `(12.5,-3)` = far away.
  **There is one mechanism.** No code path branches on integer-vs-half-integer; every rasterizer works with the
  real-valued centre.

## Rasterizers

All are pure static functions returning `IReadOnlySet<BlockXZ>` (unordered, no duplicates). Every one takes
`ShapeCenter center, <parameters>, int maxRadius = 128, int outlineThickness = 1`. Invalid parameters throw
`ArgumentException` / `ArgumentOutOfRangeException` — validate before syncing, or use `LayerGeometry`, which
catches and reports.

| Function | Parameters | Validation |
|---|---|---|
| `Circle.Rasterize` | `radius` | multiple of 0.5, ≥ 0.5, ≤ maxRadius |
| `Ring.Rasterize` | `innerRadius, outerRadius` | both as circle, inner < outer |
| `Ellipse.Rasterize` | `radiusX, radiusZ` | both as circle |
| `Rectangle.Rasterize` | `width, depth` (blocks) | ≥ 1; effective half-size ≤ maxRadius |
| `RegularPolygon.Rasterize` | `sides, circumradius, rotationDegrees` | sides ≥ 3, circumradius > 0 and ≤ maxRadius, rotation finite |
| `Spiral.Rasterize` / `RasterizePath` | `turns, spacing, startRadius, clockwise` | turns > 0, spacing ≥ 0, startRadius ≥ 0, `startRadius + spacing·turns` ≤ maxRadius |

### Circle — the midpoint criterion with a fractional centre

For every block column the ideal curve crosses, the two crossing points `z = dz ± sqrt(r² − u²)`
(`u = x − dx`) are rounded to the nearest block row; the same is done for every row with the axes swapped.
A rounded block is kept only where the curve is flatter than 45° for that sweep — judged on the rounded
point (`|u| ≤ |v|`) *or* on the ideal point (`|u| ≤ f(u)`). The union of both sweeps is the outline.
This is the classic midpoint circle algorithm with the integer centre replaced by a real one.

* **Ties** (ideal point exactly halfway between two rows/columns) round **toward the centre**. This is what
  makes r = 1.5 at (0,0) the 3×3 ring rather than a 5-wide blob, and r = 0.5 at (0.5,0.5) the 2×2
  (there the two ± branches tie in opposite directions and both survive).
* **r = 1 at (0,0) is the 4-neighbour plus**, not the 8-ring: the diagonal blocks sit at distance 1.41
  (error 0.41) while the axis blocks are exact; the midpoint rule picks the plus, as does textbook Bresenham.
* Guarantees proven by the tests for every radius 0.5…64 at (0,0), (0.5,0.5), (0.5,0): no duplicates,
  every block within **0.5** of the ideal radius (tighter than 0.5·√2), 8-connected closed ring, exactly the
  symmetry group of the centre (8-way for (0,0) and (0.5,0.5), the two mirrors for (0.5,0)), one block thick,
  block count non-decreasing in r.
* Reference sets (r=0.5 corner → 2×2; r=1 → plus; r=1.5 → 3×3 ring; r=2 → textbook 12-block circle;
  r=2.5 corner → 6-wide ring) are drawn as ASCII art in `CircleTests.cs`.

### Ring
Exactly `Circle(inner) ∪ Circle(outer)`. Two disjoint components whenever the radii differ by ≥ 1.5.

### Ellipse
Same algorithm with `f(u) = rz·sqrt(1 − u²/rx²)` and the flat-region test `rz²·|u| ≤ rx²·|v|`.
`Ellipse(r, r)` is bit-for-bit `Circle(r)` (same code path). Very thin ellipses (e.g. 1×8) converge into a
one-column spike at the tips; that spike is the curve, and the outline spans the full `2·rz`.

### Rectangle — parity rule
A side can be centred exactly only when its parity matches the centre on that axis: integer centre
coordinate → odd side, half-step centre coordinate → even side. A mismatching side is **rounded outward by
one block** (each axis independently): `Rectangle(2,2)` at (0,0) draws 3×3; `Rectangle(3,3)` at (0.5,0.5)
draws 4×4. `Rectangle.EffectiveSize(center, w, d)` returns what will be drawn — the GUI should display it.
`Rectangle.Span(centre, size)` gives the inclusive block range of one axis. Sides of 1 produce a line.
Block count is `2(w+d) − 4` for w, d ≥ 2.

### RegularPolygon
Vertex *i* is at angle `rotationDegrees + 360·i/sides`, measured from +X toward +Z, at `circumradius` from
the centre. Each real vertex snaps to the nearest block (exact ties away from the centre; a coordinate that
lands exactly on a half-step centre snaps to the + side). Edges, including last→first, are `Bresenham.Line`s
with ties resolved toward the shape centre, so `Line(a,b) == Line(b,a)` as sets and the polygon mirrors
correctly. `RegularPolygon.Vertices(...)` exposes the snapped vertices for the GUI.
A 4-gon at 45° with circumradius 2 is exactly `Rectangle(3,3)`; at 0° it is the diamond.

### Spiral
Archimedean `r(θ) = startRadius + spacing·θ/2π`, θ ∈ [0, 2π·turns]. Sampled every `sampleStep` (default
0.25) blocks of arc length, snapped (ties away from the centre), **consecutive repeats dropped**, gaps between
successive blocks bridged with Bresenham so the ordered path is **8-connected in order**; a lap that closes on
its start (spacing 0) does not list the start twice. `clockwise` advances from +X toward +Z (clockwise when
viewed from above with north = −Z at the top); the other direction is the mirror image in Z.
Where laps touch at block resolution (spacing below about 2) a block may occur twice in the *path*; the
*set* from `Spiral.Rasterize` is always duplicate-free. Sub-block spacing is allowed but produces merged laps.

## `outlineThickness`
Default 1. For closed shapes (circle, ring, ellipse, rectangle, polygon) thickness *t* grows the outline
**inward**: the interior is flood-filled and peeled *t − 1* times (4-neighbourhood), so the outer face stays
exactly at the configured parameter and the band eats into the interior, saturating at the filled shape.
A ring thickens each of its two circles separately. A spiral adds the same spiral with `startRadius`
reduced by 1, 2, … *t − 1* (clamped at 0), i.e. the band grows toward the centre.

## `maxRadius` (spec §6/§9)
Two rules, both documented in the table above:
1. **Reject**: a shape whose own extent exceeds `maxRadius` throws (`ArgumentOutOfRangeException`).
2. **Clip**: after rasterizing, every block outside the square window `|x| ≤ maxRadius, |z| ≤ maxRadius`
   *around the projector* is dropped silently. This bounds the set to `(2·maxRadius+1)²` regardless of how
   far the centre offset moves the shape. World-bounds clipping (spec §9) is the caller's job — it knows the
   world size; the library never does.

## Drape (spec §5a) — `DrapeResolver`
```csharp
IReadOnlyList<BlockXYZ> Resolve(IEnumerable<BlockXZ> columns, int fixedY, VerticalMode mode,
                                int drapeOffset, Func<int,int,int?> surfaceHeightAt)
BlockXYZ ResolveColumn(BlockXZ column, ...same...)
IReadOnlyList<BlockXZ> AffectedColumns(int changedX, int changedZ, IReadOnlySet<BlockXZ> outline)
```
* `VerticalMode.FixedY` → `y = fixedY` (projector Y + layer Y offset, supplied by the caller); the callback
  is never invoked.
* `VerticalMode.Drape` → `y = surfaceHeightAt(x, z) + 1 + drapeOffset`; when the callback returns `null`
  (column not loaded) → `y = fixedY` (no offset).
* Output preserves input order (feed it `LayerGeometry.Positions` to get a stable, sorted list).
* **Callback contract**: `surfaceHeightAt(x, z)` gets a column relative to the projector and returns, in the
  same Y frame as `fixedY`, the Y of the topmost block that counts as surface — or `null` if unloaded.
  **What counts as surface is the caller's decision.** The per-layer `treatFluidAsSurface` rule lives inside
  the callback: on → return the fluid's top block; off → look through fluids and return the bed. The library
  never inspects blocks and has no notion of fluids.
* `AffectedColumns`: a column's surface height depends only on blocks in that column, so a block change at
  (x, z) invalidates exactly that column if it is on the outline, and nothing otherwise. Neighbouring columns
  are never affected. Callers re-resolve the returned columns only.

## Cached position set — `LayerGeometry`
```csharp
record LayerParameters(ShapeSpec Shape, ShapeCenter Center, int MaxRadius = 128, int OutlineThickness = 1);
class LayerGeometry { bool Update(LayerParameters p); IReadOnlyList<BlockXZ> Positions;
                      IReadOnlySet<BlockXZ> PositionSet; string? Error; LayerParameters? Parameters; int RecomputeCount; }
```
* `ShapeSpec` is an abstract record with `CircleSpec(Radius)`, `RingSpec(Inner, Outer)`,
  `EllipseSpec(RadiusX, RadiusZ)`, `RectangleSpec(Width, Depth)`, `RegularPolygonSpec(Sides, Circumradius,
  RotationDegrees)`, `SpiralSpec(Turns, Spacing, StartRadius, Clockwise)`. Records → value equality.
  Each has `Rasterize(center, maxRadius, thickness)` and `DefaultVerticalMode` (Drape for circle, ring,
  ellipse, spiral; FixedY for rectangle, polygon — spec §5a).
* `Update` returns `false` and does nothing when the new parameters equal the current ones by value;
  otherwise it rasterizes once and returns `true`. That is the "recomputed only on parameter change"
  contract; `RecomputeCount` proves it.
* `Positions` is **strictly ascending by Z then X**, duplicate-free, relative to the projector; `PositionSet`
  is the same data for membership tests (build feedback, `AffectedColumns`). Both are replaced wholesale on
  recompute — a consumer may hold a reference safely.
* Invalid parameters never throw from `Update`: `Error` carries the message and the positions are empty.
* Not thread-safe; one instance per layer on the client render side.

## Bresenham (public utility)
`Bresenham.Line(a, b, tieToward = default)`: inclusive, in order, one block per major-axis step,
8-connected, symmetric under reversal. Exact ties go to the candidate nearer `tieToward`, then to the
lexicographically smaller block (Z, then X).
