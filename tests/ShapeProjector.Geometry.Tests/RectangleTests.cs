namespace ShapeProjector.Geometry.Tests;

public class RectangleTests
{
    [Fact]
    public void Square_3_at_block_is_the_3x3_ring()
    {
        var expected = Shape.FromArt("""
            ###
            #.#
            ###
            """, -1, -1);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Block, 3, 3));
    }

    [Fact]
    public void Rectangle_5x3_at_block()
    {
        //   x: -2 -1 0 1 2
        // z=-1:  # # # # #
        // z= 0:  # . . . #
        // z= 1:  # # # # #
        var expected = Shape.FromArt("""
            #####
            #...#
            #####
            """, -2, -1);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Block, 5, 3));
    }

    [Fact]
    public void Rectangle_4x2_at_corner_is_all_eight_blocks()
    {
        //   x: -1 0 1 2
        // z= 0:  # # # #
        // z= 1:  # # # #
        var expected = Shape.FromArt("""
            ####
            ####
            """, -1, 0);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Corner, 4, 2));
    }

    [Fact]
    public void Square_4_at_corner()
    {
        //   x: -1 0 1 2
        // z=-1:  # # # #
        // z= 0:  # . . #
        // z= 1:  # . . #
        // z= 2:  # # # #
        var expected = Shape.FromArt("""
            ####
            #..#
            #..#
            ####
            """, -1, -1);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Corner, 4, 4));
    }

    [Fact]
    public void Rectangle_2x3_at_edge_centre()
    {
        // centre (0.5,0): width 2 spans x=0..1, depth 3 spans z=-1..1
        var expected = Shape.FromArt("""
            ##
            ##
            ##
            """, 0, -1);
        Shape.AssertSameSet(expected, Rectangle.Rasterize(Shape.Edge, 2, 3));
    }

    [Fact]
    public void One_by_one_is_the_single_block_and_one_by_five_is_a_line()
    {
        Shape.AssertSameSet(Shape.Set((0, 0)), Rectangle.Rasterize(Shape.Block, 1, 1));
        Shape.AssertSameSet(Shape.Set((0, -2), (0, -1), (0, 0), (0, 1), (0, 2)), Rectangle.Rasterize(Shape.Block, 1, 5));
    }

    [Fact]
    public void Parity_mismatch_rounds_outward_to_the_next_size_that_fits()
    {
        // An even width cannot be centred on a block; a 2x2 at (0,0) becomes 3x3.
        Assert.Equal((3, 3), Rectangle.EffectiveSize(Shape.Block, 2, 2));
        Shape.AssertSameSet(Rectangle.Rasterize(Shape.Block, 3, 3), Rectangle.Rasterize(Shape.Block, 2, 2));
        // An odd width cannot be centred on a corner; 3x3 at (0.5,0.5) becomes 4x4.
        Assert.Equal((4, 4), Rectangle.EffectiveSize(Shape.Corner, 3, 3));
        Shape.AssertSameSet(Rectangle.Rasterize(Shape.Corner, 4, 4), Rectangle.Rasterize(Shape.Corner, 3, 3));
        // Edge centre (0.5,0): even width fits, odd depth fits; each axis rounds independently.
        Assert.Equal((2, 3), Rectangle.EffectiveSize(Shape.Edge, 2, 3));
        Assert.Equal((4, 5), Rectangle.EffectiveSize(Shape.Edge, 3, 4));
        // Matching parities are untouched.
        Assert.Equal((7, 3), Rectangle.EffectiveSize(Shape.Block, 7, 3));
        Assert.Equal((8, 2), Rectangle.EffectiveSize(Shape.Corner, 8, 2));
    }

    public static IEnumerable<object[]> Sizes()
    {
        foreach (var c in Shape.Centers())
            foreach (int w in new[] { 3, 4, 7, 10, 25, 64 })
                foreach (int d in new[] { 3, 5, 8, 31 })
                    yield return [w, d, c[0]];
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Block_count_matches_the_perimeter_formula_of_the_effective_size(int w, int d, ShapeCenter c)
    {
        var (we, de) = Rectangle.EffectiveSize(c, w, d);
        var set = Rectangle.Rasterize(c, w, d);
        Assert.Equal(2 * (we + de) - 4, set.Count);
        Assert.Equal(we, set.Max(p => p.X) - set.Min(p => p.X) + 1);
        Assert.Equal(de, set.Max(p => p.Z) - set.Min(p => p.Z) + 1);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Has_exactly_four_corners_and_is_a_closed_ring(int w, int d, ShapeCenter c)
    {
        var set = Rectangle.Rasterize(c, w, d);
        Shape.AssertClosedRing(set);
        Shape.AssertSymmetric(set, c, expectDiagonal: w == d && Shape.SameFraction(c));
        // A corner has exactly one edge-neighbour along X and exactly one along Z.
        int corners = set.Count(p =>
            (set.Contains(new BlockXZ(p.X + 1, p.Z)) ^ set.Contains(new BlockXZ(p.X - 1, p.Z))) &&
            (set.Contains(new BlockXZ(p.X, p.Z + 1)) ^ set.Contains(new BlockXZ(p.X, p.Z - 1))));
        Assert.Equal(4, corners);
    }

    [Fact]
    public void Centre_offset_moves_the_rectangle_as_a_whole()
    {
        var at = Rectangle.Rasterize(new ShapeCenter(12.5, -3), 4, 5);
        var expected = Rectangle.Rasterize(Shape.Edge, 4, 5).Select(p => new BlockXZ(p.X + 12, p.Z - 3));
        Shape.AssertSameSet(expected, at);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, -1)]
    public void Rejects_non_positive_sizes(int w, int d)
    {
        Assert.ThrowsAny<ArgumentException>(() => Rectangle.Rasterize(Shape.Block, w, d));
    }
}
