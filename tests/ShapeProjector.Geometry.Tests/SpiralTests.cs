namespace ShapeProjector.Geometry.Tests;

public class SpiralTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var c in Shape.Centers())
        {
            yield return [3.0, 3.0, 2.0, true, c[0]];
            yield return [1.0, 2.0, 0.0, false, c[0]];
            yield return [5.5, 2.5, 4.0, true, c[0]];
            yield return [0.5, 10.0, 20.0, false, c[0]];
            yield return [10.0, 5.0, 1.0, true, c[0]];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Path_is_deduplicated_and_8_connected_in_order(double turns, double spacing, double r0, bool cw, ShapeCenter c)
    {
        var path = Spiral.RasterizePath(c, turns, spacing, r0, cw);
        Shape.AssertNoDuplicates(path);
        for (int i = 1; i < path.Count; i++)
            Assert.True(path[i - 1].IsAdjacent8(path[i]), $"gap between {path[i - 1]} and {path[i]} at index {i}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Radius_grows_monotonically_along_the_path_up_to_snapping_jitter(double turns, double spacing, double r0, bool cw, ShapeCenter c)
    {
        var path = Spiral.RasterizePath(c, turns, spacing, r0, cw);
        double runningMax = 0;
        foreach (var p in path)
        {
            double d = Shape.Distance(p, c);
            Assert.True(d >= runningMax - Math.Sqrt(2) - 1e-9, $"{p} at {d:F2} retreats below running max {runningMax:F2}");
            runningMax = Math.Max(runningMax, d);
        }
        Assert.InRange(Shape.Distance(path[0], c), r0 - 0.75, r0 + 0.75);
        Assert.InRange(Shape.Distance(path[^1], c), r0 + spacing * turns - 0.75, r0 + spacing * turns + 0.75);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Set_is_the_distinct_path(double turns, double spacing, double r0, bool cw, ShapeCenter c)
    {
        Shape.AssertSameSet(Spiral.RasterizePath(c, turns, spacing, r0, cw), Spiral.Rasterize(c, turns, spacing, r0, cw));
    }

    [Fact]
    public void Spacing_below_two_may_repeat_a_block_in_the_path_but_the_set_stays_clean_and_connected()
    {
        // Laps that touch at block resolution: the ordered path is still 8-connected in order, the set has no duplicates.
        foreach (var c in new[] { Shape.Block, Shape.Corner, Shape.Edge })
        {
            var path = Spiral.RasterizePath(c, 3, 1, 0, true);
            for (int i = 1; i < path.Count; i++) Assert.True(path[i - 1].IsAdjacent8(path[i]));
            var set = Spiral.Rasterize(c, 3, 1, 0, true);
            Assert.Equal(path.Distinct().Count(), set.Count);
            Shape.Assert8Connected(set);
        }
    }

    [Fact]
    public void Gap_bridging_keeps_the_path_connected_even_with_a_coarse_sample_step()
    {
        var path = Spiral.RasterizePath(Shape.Block, 3, 4, 2, true, sampleStep: 2.5);
        Shape.AssertNoDuplicates(path);
        for (int i = 1; i < path.Count; i++)
            Assert.True(path[i - 1].IsAdjacent8(path[i]), $"gap between {path[i - 1]} and {path[i]}");
    }

    [Fact]
    public void Direction_flips_are_mirror_images_in_z()
    {
        var cw = Spiral.RasterizePath(Shape.Block, 2, 3, 1, clockwise: true);
        var ccw = Spiral.RasterizePath(Shape.Block, 2, 3, 1, clockwise: false);
        Assert.Equal(cw.Select(p => Shape.MirrorZ(p, Shape.Block)).ToList(), ccw.ToList());
    }

    [Fact]
    public void Clockwise_means_from_plus_x_toward_plus_z()
    {
        // Start on +X; a quarter turn later the point is on +Z for clockwise (viewed from above with north = -Z up).
        var path = Spiral.RasterizePath(Shape.Block, 1, 0, 10, clockwise: true).ToList();
        Assert.Equal(new BlockXZ(10, 0), path[0]);
        int iZ = path.IndexOf(new BlockXZ(0, 10)), iNegX = path.IndexOf(new BlockXZ(-10, 0));
        Assert.True(iZ > 0, "quarter-turn point (0,10) missing");
        Assert.True(iNegX > iZ, "half-turn point (-10,0) must come after (0,10)");
    }

    [Fact]
    public void Zero_spacing_one_turn_is_a_closed_loop_close_to_the_circle()
    {
        var path = Spiral.RasterizePath(Shape.Block, 1, 0, 6, true);
        Assert.True(path[0].IsAdjacent8(path[^1]) || path[0].Equals(path[^1]));
        foreach (var p in path) Assert.InRange(Shape.Distance(p, Shape.Block), 6 - 0.75, 6 + 0.75);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, -1)]
    [InlineData(double.NaN, 1, 1)]
    public void Rejects_bad_parameters(double turns, double spacing, double r0)
    {
        Assert.ThrowsAny<ArgumentException>(() => Spiral.RasterizePath(Shape.Block, turns, spacing, r0, true));
    }
}
