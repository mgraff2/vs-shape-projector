namespace ShapeProjector.Geometry.Tests;

public class ThicknessTests
{
    [Fact]
    public void Thick_outlines_grow_inward_and_keep_the_outer_extent()
    {
        // Circle r=3 at (0,0): the disc-boundary rule gives a 24-block outline (2*(7-1)*2, the
        // perimeter of its 7x7 bounding box) around 13 interior blocks, which peel as 8, 4, 1.
        // The old 8-connected outline was 16 blocks around 21 interior; the filled disc is the
        // same 37 blocks either way, which is why the saturation figure is unchanged.
        Assert.Equal(24, Circle.Rasterize(Shape.Block, 3, outlineThickness: 1).Count);
        Assert.Equal(32, Circle.Rasterize(Shape.Block, 3, outlineThickness: 2).Count);
        Assert.Equal(36, Circle.Rasterize(Shape.Block, 3, outlineThickness: 3).Count);
        Assert.Equal(37, Circle.Rasterize(Shape.Block, 3, outlineThickness: 4).Count);
        Assert.Equal(37, Circle.Rasterize(Shape.Block, 3, outlineThickness: 9).Count); // saturates at the filled disc
        var thin = Circle.Rasterize(Shape.Block, 3);
        var thick = Circle.Rasterize(Shape.Block, 3, outlineThickness: 2);
        Assert.True(thin.IsSubsetOf(thick));
        Assert.Equal(thin.Max(p => p.X), thick.Max(p => p.X));
    }

    [Fact]
    public void Thickness_2_on_a_5x5_square_leaves_only_the_centre_block()
    {
        var expected = Shape.FromArt("""
            #####
            #####
            ##.##
            #####
            #####
            """, -2, -2);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Block, 5, 5, outlineThickness: 2));
    }

    [Fact]
    public void Thickness_2_on_the_3x3_circle_fills_it()
    {
        Assert.Equal(9, Circle.Rasterize(Shape.Block, 1.5, outlineThickness: 2).Count);
    }

    [Fact]
    public void Ring_thickens_each_circle_separately()
    {
        var expected = Circle.Rasterize(Shape.Corner, 5, outlineThickness: 2).Union(Circle.Rasterize(Shape.Corner, 9.5, outlineThickness: 2));
        Shape.AssertSameSet(expected, Ring.Rasterize(Shape.Corner, 5, 9.5, outlineThickness: 2));
    }

    [Fact]
    public void Polygon_and_ellipse_thicken_inward()
    {
        var hexThin = RegularPolygon.Rasterize(Shape.Block, 6, 10, 0);
        var hexThick = RegularPolygon.Rasterize(Shape.Block, 6, 10, 0, outlineThickness: 3);
        Assert.True(hexThin.IsProperSubsetOf(hexThick));
        Assert.Equal(hexThin.Max(p => p.X), hexThick.Max(p => p.X));
        // nothing may lie outside the circumcircle beyond the vertex-snapping slack (0.5*sqrt(2))
        Assert.All(hexThick, p => Assert.True(Shape.Distance(p, Shape.Block) <= 10 + 0.7072));

        var ellThin = Ellipse.Rasterize(Shape.Corner, 8, 4);
        var ellThick = Ellipse.Rasterize(Shape.Corner, 8, 4, outlineThickness: 2);
        Assert.True(ellThin.IsProperSubsetOf(ellThick));
        Assert.Equal(ellThin.Max(p => p.Z), ellThick.Max(p => p.Z));
    }

    [Fact]
    public void Spiral_thickens_toward_the_centre()
    {
        var thin = Spiral.Rasterize(Shape.Block, 3, 4, 6, true);
        var thick = Spiral.Rasterize(Shape.Block, 3, 4, 6, true, outlineThickness: 2);
        Assert.True(thin.IsProperSubsetOf(thick));
        Assert.Equal(thin.Max(p => p.X), thick.Max(p => p.X));
        Shape.Assert8Connected(thick);
    }

    [Fact]
    public void Rejects_thickness_below_one()
    {
        Assert.ThrowsAny<ArgumentException>(() => Circle.Rasterize(Shape.Block, 3, outlineThickness: 0));
    }
}

public class ClipTests
{
    [Fact]
    public void Shape_within_max_radius_is_untouched()
    {
        Shape.AssertSameSet(Circle.Rasterize(Shape.Block, 10, maxRadius: 128), Circle.Rasterize(Shape.Block, 10, maxRadius: 10));
    }

    [Fact]
    public void Positions_outside_the_window_around_the_projector_are_clipped()
    {
        var offset = new ShapeCenter(5, 0);
        var full = Circle.Rasterize(offset, 10, maxRadius: 128);
        var clipped = Circle.Rasterize(offset, 10, maxRadius: 10);
        Assert.True(clipped.Count < full.Count);
        Assert.All(clipped, p => Assert.True(Math.Abs(p.X) <= 10 && Math.Abs(p.Z) <= 10));
        Shape.AssertSameSet(full.Where(p => Math.Abs(p.X) <= 10 && Math.Abs(p.Z) <= 10), clipped);
    }

    [Fact]
    public void Parameters_exceeding_max_radius_are_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Circle.Rasterize(Shape.Block, 11, maxRadius: 10));
        Assert.ThrowsAny<ArgumentException>(() => Ring.Rasterize(Shape.Block, 2, 11, maxRadius: 10));
        Assert.ThrowsAny<ArgumentException>(() => Ellipse.Rasterize(Shape.Block, 2, 11, maxRadius: 10));
        Assert.ThrowsAny<ArgumentException>(() => Rectangle.Rasterize(Shape.Block, 23, 3, maxRadius: 10));
        Assert.ThrowsAny<ArgumentException>(() => RegularPolygon.Rasterize(Shape.Block, 6, 10.5, 0, maxRadius: 10));
        Assert.ThrowsAny<ArgumentException>(() => Spiral.Rasterize(Shape.Block, 2, 4, 3, true, maxRadius: 10));
        // and the boundary is inclusive
        Circle.Rasterize(Shape.Block, 10, maxRadius: 10);
        Rectangle.Rasterize(Shape.Block, 21, 21, maxRadius: 10);
        Spiral.Rasterize(Shape.Block, 2, 4, 2, true, maxRadius: 10);
    }
}
