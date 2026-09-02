namespace ShapeProjector.Geometry.Tests;

public class RingTests
{
    public static IEnumerable<object[]> Pairs()
    {
        double[] inners = [0.5, 1, 2.5, 7, 20, 31.5];
        double[] outers = [1, 1.5, 3, 9.5, 23, 64];
        foreach (var c in Shape.Centers())
            foreach (var i in inners)
                foreach (var o in outers)
                    if (i < o) yield return [i, o, c[0]];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Ring_is_exactly_the_union_of_its_two_circles(double inner, double outer, ShapeCenter c)
    {
        var expected = Circle.Rasterize(c, inner).Union(Circle.Rasterize(c, outer));
        Shape.AssertSameSet(expected, Ring.Rasterize(c, inner, outer));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Ring_has_the_symmetry_of_its_centre(double inner, double outer, ShapeCenter c)
    {
        Shape.AssertSymmetric(Ring.Rasterize(c, inner, outer), c, Shape.SameFraction(c));
    }

    [Fact]
    public void Moat_example_from_spec_has_two_disjoint_closed_rings()
    {
        // layer 1 of the canonical example: ring r=20..23
        var inner = Circle.Rasterize(Shape.Block, 20);
        var outer = Circle.Rasterize(Shape.Block, 23);
        Assert.Empty(inner.Intersect(outer));
        var ring = Ring.Rasterize(Shape.Block, 20, 23);
        Assert.Equal(inner.Count + outer.Count, ring.Count);
        Assert.Equal(160 + 184, ring.Count);        // 8*20 + 8*23, the 4-connected counts
        Shape.AssertClosedRing(inner);
        Shape.AssertClosedRing(outer);
        // Both walls of the moat inherit the 4-connected look for free: Ring is the union of
        // two Circle.Rasterize calls, so it needed no change of its own.
        Shape.Assert4Connected(inner);
        Shape.Assert4Connected(outer);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(0.25, 3)]
    [InlineData(1, 3.3)]
    [InlineData(0, 2)]
    public void Rejects_bad_radius_pairs(double inner, double outer)
    {
        Assert.ThrowsAny<ArgumentException>(() => Ring.Rasterize(Shape.Block, inner, outer));
    }
}
