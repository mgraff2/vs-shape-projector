namespace ShapeProjector.Geometry.Tests;

public class LayerGeometryTests
{
    static LayerParameters Params(double r) => new(new CircleSpec(r), Shape.Corner, 128, 1);

    [Fact]
    public void Recomputes_only_when_parameters_change()
    {
        var geo = new LayerGeometry();
        Assert.True(geo.Update(Params(5)));
        Assert.Equal(1, geo.RecomputeCount);
        Assert.False(geo.Update(Params(5)));            // equal by value, new instance
        Assert.False(geo.Update(Params(5)));
        Assert.Equal(1, geo.RecomputeCount);
        Assert.True(geo.Update(Params(6)));
        Assert.Equal(2, geo.RecomputeCount);
        Assert.True(geo.Update(Params(6) with { Center = Shape.Block }));
        Assert.Equal(3, geo.RecomputeCount);
        Assert.True(geo.Update(Params(6) with { Center = Shape.Block, OutlineThickness = 2 }));
        Assert.Equal(4, geo.RecomputeCount);
    }

    [Fact]
    public void Positions_match_the_rasterizer_and_are_sorted_row_major()
    {
        var geo = new LayerGeometry();
        geo.Update(Params(7.5));
        Shape.AssertSameSet(Circle.Rasterize(Shape.Corner, 7.5), geo.Positions);
        Assert.True(geo.PositionSet.SetEquals(Circle.Rasterize(Shape.Corner, 7.5)));
        for (int i = 1; i < geo.Positions.Count; i++)
            Assert.True(geo.Positions[i - 1].CompareTo(geo.Positions[i]) < 0, "positions must be strictly ascending (Z, then X)");
        Assert.Null(geo.Error);
    }

    [Fact]
    public void Empty_before_first_update()
    {
        var geo = new LayerGeometry();
        Assert.Empty(geo.Positions);
        Assert.Null(geo.Parameters);
        Assert.Equal(0, geo.RecomputeCount);
    }

    [Fact]
    public void Invalid_parameters_are_reported_not_thrown_and_yield_an_empty_set()
    {
        var geo = new LayerGeometry();
        geo.Update(Params(5));
        Assert.True(geo.Update(Params(500)));  // exceeds maxRadius 128
        Assert.NotNull(geo.Error);
        Assert.Empty(geo.Positions);
        Assert.True(geo.Update(Params(5)));
        Assert.Null(geo.Error);
        Assert.NotEmpty(geo.Positions);
    }

    [Fact]
    public void Every_shape_spec_rasterizes_through_the_same_entry_point()
    {
        ShapeSpec[] specs =
        [
            new CircleSpec(4), new RingSpec(3, 6), new EllipseSpec(6, 3), new RectangleSpec(5, 7),
            new RegularPolygonSpec(6, 8, 30), new SpiralSpec(2, 3, 1, true),
        ];
        foreach (var s in specs)
        {
            var set = s.Rasterize(Shape.Block, 128, 1);
            Assert.NotEmpty(set);
            if (s is not RingSpec) Shape.Assert8Connected(set, s.ToString()); // a ring is two disjoint circles by design
        }
        Shape.AssertSameSet(Circle.Rasterize(Shape.Block, 4), new CircleSpec(4).Rasterize(Shape.Block, 128, 1));
        Shape.AssertSameSet(Rectangle.Rasterize(Shape.Block, 5, 7), new RectangleSpec(5, 7).Rasterize(Shape.Block, 128, 1));
        Shape.AssertSameSet(Spiral.Rasterize(Shape.Block, 2, 3, 1, true), new SpiralSpec(2, 3, 1, true).Rasterize(Shape.Block, 128, 1));
    }

    [Fact]
    public void Default_vertical_modes_follow_the_spec()
    {
        Assert.Equal(VerticalMode.Drape, new CircleSpec(4).DefaultVerticalMode);
        Assert.Equal(VerticalMode.Drape, new RingSpec(3, 6).DefaultVerticalMode);
        Assert.Equal(VerticalMode.Drape, new SpiralSpec(2, 3, 1, true).DefaultVerticalMode);
        Assert.Equal(VerticalMode.FixedY, new RectangleSpec(5, 7).DefaultVerticalMode);
        Assert.Equal(VerticalMode.FixedY, new RegularPolygonSpec(6, 8, 30).DefaultVerticalMode);
    }

    [Fact]
    public void Specs_have_value_equality()
    {
        Assert.Equal(new RingSpec(3, 6), new RingSpec(3, 6));
        Assert.NotEqual<ShapeSpec>(new CircleSpec(3), new EllipseSpec(3, 3));
        Assert.Equal(Params(5), Params(5));
    }
}
