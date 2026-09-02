namespace ShapeProjector.Geometry.Tests;

/// <summary>
/// Spec section 10a: Triangle is exactly regular polygon n=3 — same spec type, same rasterizer,
/// no parallel code path. Radii run the full half-step range 0.5..64 at all three centre parities.
/// </summary>
public class TriangleTests
{
    /// <summary>All half-step circumradii 0.5..64 crossed with rotations and all three centres.</summary>
    public static IEnumerable<object[]> RadiiRotationsCenters()
    {
        double[] rotations = [0, 45, 90, 137.5];
        for (int i = 1; i <= 128; i++)
            foreach (var rot in rotations)
                foreach (var c in Shape.Centers())
                    yield return [i * 0.5, rot, c[0]];
    }

    [Theory]
    [MemberData(nameof(RadiiRotationsCenters))]
    public void Triangle_alias_is_value_equal_to_polygon_n3(double r, double rot, ShapeCenter _)
    {
        Assert.Equal(new RegularPolygonSpec(3, r, rot), Triangle.Spec(r, rot));
    }

    [Theory]
    [MemberData(nameof(RadiiRotationsCenters))]
    public void Triangle_rasterizes_block_for_block_identical_to_polygon_n3(double r, double rot, ShapeCenter c)
    {
        var polygon = RegularPolygon.Rasterize(c, 3, r, rot);
        var triangle = Triangle.Spec(r, rot).Rasterize(c, maxRadius: 128, outlineThickness: 1);
        Shape.AssertSameSet(polygon, triangle);
    }

    [Fact]
    public void Triangle_default_vertical_mode_is_the_polygon_default()
    {
        Assert.Equal(VerticalMode.FixedY, Triangle.Spec(7, 30).DefaultVerticalMode);
        Assert.Equal(new RegularPolygonSpec(3, 7, 30).DefaultVerticalMode, Triangle.Spec(7, 30).DefaultVerticalMode);
    }

    [Fact]
    public void Triangle_Is_recognizes_polygon_n3_and_nothing_else()
    {
        Assert.True(Triangle.Is(Triangle.Spec(5, 10)));
        Assert.True(Triangle.Is(new RegularPolygonSpec(3, 5, 10)));
        Assert.False(Triangle.Is(new RegularPolygonSpec(4, 5, 10)));
        Assert.False(Triangle.Is(new CircleSpec(5)));
        Assert.False(Triangle.Is(new SpiralSpec(2, 1, 0, true)));
    }

    /// <summary>Circumradii at both parities including the clamp region, both directions.</summary>
    public static IEnumerable<object[]> AdjustCases()
    {
        double[] radii = [0.5, 1.0, 1.5, 2.0, 20.5, 64];
        int[] directions = [+1, -1];
        foreach (var r in radii)
            foreach (var d in directions)
                yield return [r, d];
    }

    [Theory]
    [MemberData(nameof(AdjustCases))]
    public void Triangle_radial_adjustment_is_exactly_the_polygon_rule(double r, int direction)
    {
        var viaAlias = RadialAdjust.Adjust(Triangle.Spec(r, 15), direction);
        var viaPolygon = RadialAdjust.Adjust(new RegularPolygonSpec(3, r, 15), direction);
        Assert.Equal(viaPolygon.Outcome, viaAlias.Outcome);
        Assert.Equal(viaPolygon.Spec, viaAlias.Spec);
        Assert.True(RadialAdjust.IsAdjustable(Triangle.Spec(r, 15)));
    }
}
