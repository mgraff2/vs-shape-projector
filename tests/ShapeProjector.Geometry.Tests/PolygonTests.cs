namespace ShapeProjector.Geometry.Tests;

public class BresenhamTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 5, 0)]
    [InlineData(0, 0, 0, -5)]
    [InlineData(0, 0, 3, 3)]
    [InlineData(0, 0, 7, 2)]
    [InlineData(0, 0, -2, 9)]
    [InlineData(4, -3, -6, 5)]
    public void Line_includes_both_ends_and_is_8_connected_with_one_block_per_major_step(int x0, int z0, int x1, int z1)
    {
        var line = Bresenham.Line(new BlockXZ(x0, z0), new BlockXZ(x1, z1));
        Assert.Equal(new BlockXZ(x0, z0), line[0]);
        Assert.Equal(new BlockXZ(x1, z1), line[^1]);
        Assert.Equal(Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0)) + 1, line.Count);
        for (int i = 1; i < line.Count; i++) Assert.True(line[i - 1].IsAdjacent8(line[i]), $"gap between {line[i - 1]} and {line[i]}");
        Shape.AssertNoDuplicates(line);
    }

    [Fact]
    public void Line_is_symmetric_under_reversal()
    {
        var a = Bresenham.Line(new BlockXZ(-3, 2), new BlockXZ(8, -5));
        var b = Bresenham.Line(new BlockXZ(8, -5), new BlockXZ(-3, 2));
        Shape.AssertSameSet(a, b);
    }
}

public class PolygonTests
{
    [Fact]
    public void Square_at_45_degrees_is_the_axis_aligned_3x3_ring()
    {
        // circumradius 2, vertices at 45/135/225/315 degrees -> (1.41,1.41) etc. -> snapped (+-1,+-1)
        Shape.AssertSameSet(Rectangle.Rasterize(Shape.Block, 3, 3), RegularPolygon.Rasterize(Shape.Block, 4, 2, 45));
    }

    [Fact]
    public void Square_at_0_degrees_is_the_diamond()
    {
        //   x: -2 -1 0 1 2
        // z=-2:  . . # . .
        // z=-1:  . # . # .
        // z= 0:  # . . . #
        // z= 1:  . # . # .
        // z= 2:  . . # . .
        var expected = Shape.FromArt("""
            ..#..
            .#.#.
            #...#
            .#.#.
            ..#..
            """, -2, -2);
        Shape.AssertSameSet(expected, RegularPolygon.Rasterize(Shape.Block, 4, 2, 0));
    }

    [Fact]
    public void Square_at_45_degrees_at_corner_centre_is_the_4x4_ring()
    {
        // centre (0.5,0.5), R=2: vertices at (0.5+1.41, 0.5+1.41) -> (2,2), (-1,2), (-1,-1), (2,-1)
        Shape.AssertSameSet(Rectangle.Rasterize(Shape.Corner, 4, 4), RegularPolygon.Rasterize(Shape.Corner, 4, 2, 45));
    }

    [Fact]
    public void Hexagon_vertices_are_distinct_lie_in_the_outline_and_are_mirror_symmetric()
    {
        var verts = RegularPolygon.Vertices(Shape.Block, 6, 6, 0);
        var set = RegularPolygon.Rasterize(Shape.Block, 6, 6, 0);
        Assert.Equal(6, verts.Count);
        Assert.Equal(6, verts.Distinct().Count());
        Assert.All(verts, v => Assert.Contains(v, set));
        Assert.Equal(new BlockXZ(6, 0), verts[0]);           // vertex 0 on +X
        Assert.Equal(new BlockXZ(3, 5), verts[1]);           // 60 degrees toward +Z: (3, 5.196) -> (3,5)
        Shape.AssertSymmetric(set, Shape.Block, expectDiagonal: false, "hexagon");
        Shape.AssertClosedRing(set, "hexagon");
    }

    [Fact]
    public void Rotation_by_one_full_vertex_step_and_by_360_leaves_the_polygon_unchanged()
    {
        var a = RegularPolygon.Rasterize(Shape.Corner, 5, 9, 13);
        Shape.AssertSameSet(a, RegularPolygon.Rasterize(Shape.Corner, 5, 9, 13 + 72));
        Shape.AssertSameSet(a, RegularPolygon.Rasterize(Shape.Corner, 5, 9, 13 - 360));
    }

    [Fact]
    public void Rotation_is_about_the_centre()
    {
        // Rotating a triangle by 180 degrees about a block centre equals the point reflection of the original.
        var a = RegularPolygon.Rasterize(Shape.Block, 3, 8, 10);
        var b = RegularPolygon.Rasterize(Shape.Block, 3, 8, 190).Select(p => new BlockXZ(-p.X, -p.Z));
        Shape.AssertSameSet(a, b);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var c in Shape.Centers())
            foreach (int n in new[] { 3, 4, 5, 6, 8, 12 })
                foreach (double r in new[] { 3, 5.5, 10, 33 })
                    foreach (double rot in new[] { 0, 17, 45, 90 })
                        yield return [n, r, rot, c[0]];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Polygon_is_closed_connected_and_contains_all_its_vertices(int n, double r, double rot, ShapeCenter c)
    {
        var set = RegularPolygon.Rasterize(c, n, r, rot);
        Shape.AssertNoDuplicates(set);
        Shape.AssertClosedRing(set, $"{n}-gon r={r} rot={rot} {c}");
        foreach (var v in RegularPolygon.Vertices(c, n, r, rot)) Assert.Contains(v, set);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_block_lies_within_a_block_of_the_ideal_polygon_edge(int n, double r, double rot, ShapeCenter c)
    {
        // Vertex snapping moves a vertex by at most 0.5*sqrt(2); Bresenham adds at most 0.5. Bound: 1.25.
        var verts = Enumerable.Range(0, n).Select(i =>
        {
            double a = (rot + 360.0 * i / n) * Math.PI / 180;
            return (x: c.Dx + r * Math.Cos(a), z: c.Dz + r * Math.Sin(a));
        }).ToArray();
        foreach (var p in RegularPolygon.Rasterize(c, n, r, rot))
        {
            double best = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var a = verts[i]; var b = verts[(i + 1) % n];
                best = Math.Min(best, DistanceToSegment(p.X, p.Z, a.x, a.z, b.x, b.z));
            }
            Assert.True(best <= 1.25, $"{n}-gon r={r} rot={rot} {c}: {p} is {best:F2} from the nearest edge");
        }
    }

    static double DistanceToSegment(double px, double pz, double ax, double az, double bx, double bz)
    {
        double vx = bx - ax, vz = bz - az, wx = px - ax, wz = pz - az;
        double len2 = vx * vx + vz * vz;
        double t = len2 == 0 ? 0 : Math.Clamp((wx * vx + wz * vz) / len2, 0, 1);
        double dx = px - (ax + t * vx), dz = pz - (az + t * vz);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    [Theory]
    [InlineData(2, 5, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(3, -2, 0)]
    [InlineData(3, double.NaN, 0)]
    [InlineData(3, 5, double.PositiveInfinity)]
    public void Rejects_bad_parameters(int n, double r, double rot)
    {
        Assert.ThrowsAny<ArgumentException>(() => RegularPolygon.Rasterize(Shape.Block, n, r, rot));
    }
}
