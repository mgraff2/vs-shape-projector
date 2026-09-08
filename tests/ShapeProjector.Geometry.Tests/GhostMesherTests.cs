using ShapeProjector;

namespace ShapeProjector.Geometry.Tests;

/// <summary>
/// GhostMesher.Merge and Corners, exercised on real outlines (user report 2026-09-08: "overlaps
/// with the faces on the edges"). Three invariants: every exposed face is covered exactly once
/// (area accounting, no overlaps within a face/slice), every rectangle lies in the plane its face
/// says, and its four corners walk the rectangle's perimeter in order (no bow-tie triangles).
/// </summary>
public class GhostMesherTests
{
    private static readonly (int dx, int dy, int dz)[] Normal =
    {
        (0, 0, -1), (1, 0, 0), (0, 0, 1), (-1, 0, 0), (0, 1, 0), (0, -1, 0),
    };

    private static List<GhostCell> Cells(IEnumerable<BlockXZ> columns, int y, int color)
        => columns.Select(p => new GhostCell(p.X, y, p.Z, color)).ToList();

    private static void CheckInvariants(List<GhostCell> cells)
    {
        var set = new Dictionary<(int, int, int), int>();
        foreach (var c in cells) set[(c.X, c.Y, c.Z)] = c.Color;

        // Independent count of exposed faces, per (face, slice), with the set of exposed (u, v) cells.
        var expected = new Dictionary<(int face, int slice), HashSet<(int u, int v)>>();
        foreach (var c in cells)
        {
            for (int f = 0; f < 6; f++)
            {
                var (dx, dy, dz) = Normal[f];
                if (set.TryGetValue((c.X + dx, c.Y + dy, c.Z + dz), out int other) && other == c.Color) continue;
                (int u, int v, int slice) = f switch
                {
                    4 or 5 => (c.X, c.Z, c.Y),
                    1 or 3 => (c.Z, c.Y, c.X),
                    _ => (c.X, c.Y, c.Z),
                };
                if (!expected.TryGetValue((f, slice), out var s)) expected[(f, slice)] = s = new HashSet<(int, int)>();
                s.Add((u, v));
            }
        }

        List<FaceQuad> quads = GhostMesher.Merge(cells);

        // Coverage: each exposed cell face covered exactly once, nothing covered that is not exposed.
        var covered = new Dictionary<(int face, int slice), Dictionary<(int u, int v), int>>();
        foreach (var q in quads)
        {
            Assert.True(q.U1 > q.U0 && q.V1 > q.V0, $"degenerate quad {q}");
            if (!covered.TryGetValue((q.Face, q.Slice), out var d)) covered[(q.Face, q.Slice)] = d = new Dictionary<(int, int), int>();
            for (int u = q.U0; u < q.U1; u++)
                for (int v = q.V0; v < q.V1; v++)
                {
                    d[(u, v)] = d.GetValueOrDefault((u, v)) + 1;
                }
        }
        foreach (var kv in expected)
        {
            Assert.True(covered.ContainsKey(kv.Key), $"no quads for {kv.Key}");
            var d = covered[kv.Key];
            foreach (var uv in kv.Value) Assert.True(d.GetValueOrDefault(uv) == 1, $"face {kv.Key} cell {uv} covered {d.GetValueOrDefault(uv)} times");
            Assert.Equal(kv.Value.Count, d.Count);
        }
        Assert.Equal(expected.Count, covered.Count);

        // Geometry: corners in-plane and cyclic (each consecutive pair differs in exactly one axis).
        Span<float> xyz = stackalloc float[12];
        foreach (var q in quads)
        {
            GhostMesher.Corners(q, 0.05f, xyz);
            int planeAxis = q.Face switch { 4 or 5 => 1, 1 or 3 => 0, _ => 2 };
            float plane = xyz[planeAxis];
            for (int i = 0; i < 4; i++) Assert.Equal(plane, xyz[i * 3 + planeAxis]);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                int differing = 0;
                for (int a = 0; a < 3; a++) if (xyz[i * 3 + a] != xyz[j * 3 + a]) differing++;
                Assert.True(differing == 1, $"corners {i}->{j} of {q} differ in {differing} axes");
            }
            // The rectangle's in-plane extents match the quad (at zero inset).
            GhostMesher.Corners(q, 0f, xyz);
            int uAxis = q.Face switch { 4 or 5 => 0, 1 or 3 => 2, _ => 0 };
            int vAxis = q.Face switch { 4 or 5 => 2, _ => 1 };
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                uMin = Math.Min(uMin, xyz[i * 3 + uAxis]); uMax = Math.Max(uMax, xyz[i * 3 + uAxis]);
                vMin = Math.Min(vMin, xyz[i * 3 + vAxis]); vMax = Math.Max(vMax, xyz[i * 3 + vAxis]);
            }
            Assert.Equal(q.U0, uMin); Assert.Equal(q.U1, uMax); Assert.Equal(q.V0, vMin); Assert.Equal(q.V1, vMax);

            // Boundary flags: an edge is a boundary exactly when no same-colour cell lies beyond it in-plane.
            (int, int, int) Cell(int u, int v) => q.Face switch { 4 or 5 => (u, q.Slice, v), 1 or 3 => (q.Slice, v, u), _ => (u, v, q.Slice) };
            bool Beyond(int ua, int ub, int va, int vb)
            {
                for (int u = ua; u <= ub; u++) for (int v = va; v <= vb; v++)
                    if (set.TryGetValue(Cell(u, v), out int col) && col == q.Color) return true;
                return false;
            }
            Assert.Equal(!Beyond(q.U0 - 1, q.U0 - 1, q.V0, q.V1 - 1), (q.Boundary & 1) != 0);
            Assert.Equal(!Beyond(q.U1, q.U1, q.V0, q.V1 - 1), (q.Boundary & 2) != 0);
            Assert.Equal(!Beyond(q.U0, q.U1 - 1, q.V0 - 1, q.V0 - 1), (q.Boundary & 4) != 0);
            Assert.Equal(!Beyond(q.U0, q.U1 - 1, q.V1, q.V1), (q.Boundary & 8) != 0);
        }
    }

    [Fact]
    public void A_single_cell_insets_all_edges_so_its_faces_meet_at_the_corners()
    {
        var cells = new List<GhostCell> { new GhostCell(3, 0, 5, 0x11223344) };
        var quads = GhostMesher.Merge(cells);
        Assert.Equal(6, quads.Count);
        Span<float> xyz = stackalloc float[12];
        foreach (var q in quads)
        {
            Assert.Equal(15, q.Boundary);
            GhostMesher.Corners(q, 0.05f, xyz);
            for (int i = 0; i < 12; i += 3)
            {
                // Every corner lies on the inset cube [3.05,3.95] x [0.05,0.95] x [5.05,5.95].
                Assert.True(xyz[i] >= 3.05f - 1e-5 && xyz[i] <= 3.95f + 1e-5, $"x {xyz[i]}");
                Assert.True(xyz[i + 1] >= 0.05f - 1e-5 && xyz[i + 1] <= 0.95f + 1e-5, $"y {xyz[i + 1]}");
                Assert.True(xyz[i + 2] >= 5.05f - 1e-5 && xyz[i + 2] <= 5.95f + 1e-5, $"z {xyz[i + 2]}");
            }
        }
    }

    [Fact]
    public void Circle_outline_r11_is_covered_exactly_once_with_cyclic_in_plane_corners()
    {
        CheckInvariants(Cells(Circle.Rasterize(Shape.Block, 11), 0, 0x11223344));
    }

    [Fact]
    public void Solid_disc_r11_merges_to_few_quads_and_covers_exactly_once()
    {
        var cells = Cells(Circle.Rasterize(Shape.Block, 11, outlineThickness: 12), 0, 0x11223344);
        CheckInvariants(cells);
        Assert.True(GhostMesher.Merge(cells).Count < cells.Count, "a solid disc should merge well below one quad per cell");
    }

    [Fact]
    public void Draped_outline_with_mixed_heights_and_two_colours_is_covered_exactly_once()
    {
        var cells = new List<GhostCell>();
        int i = 0;
        foreach (var p in Circle.Rasterize(Shape.Corner, 7.5))
        {
            cells.Add(new GhostCell(p.X, (i % 3) - 1, p.Z, i % 5 == 0 ? 0x22334455 : 0x11223344));
            i++;
        }
        CheckInvariants(cells);
    }

    [Fact]
    public void Tall_ring_wall_covers_exactly_once()
    {
        var cells = new List<GhostCell>();
        foreach (var p in Ring.Rasterize(Shape.Block, 6, 9))
            for (int y = 0; y < 4; y++) cells.Add(new GhostCell(p.X, y, p.Z, 0x11223344));
        CheckInvariants(cells);
    }
}
