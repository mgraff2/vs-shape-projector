namespace ShapeProjector.Geometry.Tests;

public class CircleTests
{
    const int Max = 128;

    // Where the disc boundary is drawn: a quarter block outside the nominal radius. The
    // constant is spelled out inline in each test below so the derivations read on their own.

    // ---------- exact hand-written references ----------

    [Fact]
    public void Radius_half_at_corner_is_the_2x2()
    {
        // centre (0.5,0.5), r = 0.5, boundary radius 0.75. The four blocks sit at offset
        // (+-0.5,+-0.5), distance 0.707 < 0.75, so all four are inside and none is buried.
        //                          x: 0 1
        //                          z=0:   # #
        //                          z=1:   # #
        var expected = Shape.FromArt("""
            ##
            ##
            """, 0, 0);
        Shape.AssertSameSet(expected, Circle.Rasterize(Shape.Corner, 0.5, Max));
    }

    [Fact]
    public void Radius_half_at_block_is_the_single_block()
    {
        // boundary radius 0.75: (0,0) is inside, (+-1,0) at distance 1 is not.
        Shape.AssertSameSet(Shape.Set((0, 0)), Circle.Rasterize(Shape.Block, 0.5, Max));
    }

    [Fact]
    public void Radius_half_at_edge_is_the_1x2()
    {
        // centre (0.5,0), boundary radius 0.75: (0,0) and (1,0) at distance 0.5 are inside,
        // (0,+-1) at distance 1.118 is not.
        Shape.AssertSameSet(Shape.Set((0, 0), (1, 0)), Circle.Rasterize(Shape.Edge, 0.5, Max));
    }

    [Fact]
    public void Radius_1_at_block_is_the_solid_plus()
    {
        // centre (0,0), r = 1, boundary radius 1.25. Inside: the centre and the four axis
        // blocks (distance 1); the diagonals are at 1.414 and are out. The centre block is
        // therefore NOT buried - its diagonal neighbours are outside the disc - so the
        // disc-boundary rule keeps it. A radius-1 circle is a solid plus, not a hollow one:
        // that is the price of the 8-neighbour exclusion that makes larger circles 4-connected.
        //   x: -1 0 1
        // z=-1:  . # .
        // z= 0:  # # #
        // z= 1:  . # .
        var expected = Shape.FromArt("""
            .#.
            ###
            .#.
            """, -1, -1);
        Shape.AssertSameSet(expected, Circle.Rasterize(Shape.Block, 1, Max));
    }

    [Fact]
    public void Radius_1_5_at_block_is_the_3x3_ring()
    {
        // centre (0,0), r = 1.5, boundary radius 1.75 (squared 3.0625). The whole 3x3 is
        // inside (the diagonals are at 2 < 3.0625) and (+-2,0) at 4 is out, so the centre
        // block is the one block with all eight neighbours inside and is dropped.
        //   x: -1 0 1
        // z=-1:  # # #
        // z= 0:  # . #
        // z= 1:  # # #
        var expected = Shape.FromArt("""
            ###
            #.#
            ###
            """, -1, -1);
        Shape.AssertSameSet(expected, Circle.Rasterize(Shape.Block, 1.5, Max));
    }

    [Fact]
    public void Radius_2_at_block_is_the_4_connected_ring()
    {
        // centre (0,0), r = 2, boundary radius 2.25 (squared 5.0625). Inside is the 5x5 square
        // minus its four corners (distance^2 = 8): 21 blocks. Buried (all eight neighbours
        // inside) are exactly (0,0), (+-1,0) and (0,+-1) - five blocks - leaving 16.
        // Note the corner blocks (+-1,+-1): they are the extra "zig-zag" blocks the 8-neighbour
        // exclusion keeps and the 4-neighbour (thin, 8-connected) rule would have dropped.
        //   x: -2 -1 0 1 2
        // z=-2:  . # # # .
        // z=-1:  # # . # #
        // z= 0:  # . . . #
        // z= 1:  # # . # #
        // z= 2:  . # # # .
        var expected = Shape.FromArt("""
            .###.
            ##.##
            #...#
            ##.##
            .###.
            """, -2, -2);
        var actual = Circle.Rasterize(Shape.Block, 2, Max);
        Shape.AssertSameSet(expected, actual);
        Assert.Equal(16, actual.Count);
    }

    [Fact]
    public void Radius_2_5_at_corner_spans_6_columns()
    {
        // centre (0.5,0.5), r = 2.5, boundary radius 2.75 (squared 7.5625). Offsets are
        // half-integers, so the outer extent is reached at offset 2.5 (6.25 <= 7.5625) while
        // 3.5 (12.25) is out: the outline spans columns -2..3, exactly as it did before.
        //   x: -2 -1 0 1 2 3
        // z=-2:  . . # # . .     (2.5^2 + 1.5^2 = 8.5 > 7.5625, so (-1,-2) is out)
        // z=-1:  . # # # # .
        // z= 0:  # # . . # #
        // z= 1:  # # . . # #
        // z= 2:  . # # # # .
        // z= 3:  . . # # . .
        var expected = Shape.FromArt("""
            ..##..
            .####.
            ##..##
            ##..##
            .####.
            ..##..
            """, -2, -2);
        Shape.AssertSameSet(expected, Circle.Rasterize(Shape.Corner, 2.5, Max));
    }

    [Fact]
    public void Radius_5_at_block_is_the_reference_zig_zag()
    {
        // The picture a builder recognises: every step from one row to the next is an L of
        // edge-sharing blocks, never a corner-to-corner hop. 40 blocks = 8 * r.
        var expected = Shape.FromArt("""
            ....###....
            ..###.###..
            .##.....##.
            .#.......#.
            ##.......##
            #.........#
            ##.......##
            .#.......#.
            .##.....##.
            ..###.###..
            ....###....
            """, -5, -5);
        var actual = Circle.Rasterize(Shape.Block, 5, Max);
        Shape.AssertSameSet(expected, actual);
        Shape.Assert4Connected(actual);
    }

    // ---------- properties over every radius 0.5..64 and every centre parity ----------

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void No_duplicates(double r, ShapeCenter c)
    {
        Shape.AssertNoDuplicates(Circle.Rasterize(c, r, Max));
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Every_block_lies_in_the_boundary_band_of_the_disc(double r, ShapeCenter c)
    {
        // The old 0.5 bound was a midpoint-selection guarantee and no longer applies. The
        // disc-boundary rule gives an asymmetric band, derived from its two clauses:
        //   kept  =>  inside the boundary circle          =>  d <= r + 0.25
        //   kept  =>  some 8-neighbour q is outside it    =>  d(q) > r + 0.25, and since
        //             |d(p) - d(q)| <= |p - q| <= sqrt(2), d > r + 0.25 - sqrt(2).
        // So d - r lies in (0.25 - sqrt(2), 0.25], i.e. |d - r| < sqrt(2) - 0.25 = 1.16421.
        // The inward half is nearly attained (1.16262 at r=59.5 on the edge centre, block
        // (-41,41)): that block is the inner corner of a 45-degree zig-zag step, which is
        // exactly the block the 4-connected rule adds and the 8-connected one omitted.
        const double outward = 0.25, inward = 1.4142135623730951 - 0.25;
        foreach (var p in Circle.Rasterize(c, r, Max))
        {
            double d = Shape.Distance(p, c);
            Assert.True(d <= r + outward + 1e-9, $"r={r} {c}: block {p} at distance {d:F3} is outside the boundary circle");
            Assert.True(d > r - inward - 1e-9, $"r={r} {c}: block {p} at distance {d:F3} is more than {inward:F5} inside r");
        }
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Outline_is_a_closed_8_connected_ring(double r, ShapeCenter c)
    {
        Shape.AssertClosedRing(Circle.Rasterize(c, r, Max), $"circle r={r} {c}");
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Outline_is_4_connected(double r, ShapeCenter c)
    {
        // The whole point of the disc-boundary rule (spec section 5, matching the pixel-circle
        // generators builders use): the ring never steps corner-to-corner. Strictly stronger
        // than the 8-connected test above, which the old midpoint outline also passed.
        Shape.Assert4Connected(Circle.Rasterize(c, r, Max), $"circle r={r} {c}");
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Outline_has_the_symmetry_group_of_its_centre(double r, ShapeCenter c)
    {
        // (0,0) and (0.5,0.5): full 8-way (mirrors + diagonal swap); (0.5,0): the two mirrors only.
        Shape.AssertSymmetric(Circle.Rasterize(c, r, Max), c, Shape.SameFraction(c), $"circle r={r} {c}");
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Outline_has_no_interior_block(double r, ShapeCenter c)
    {
        // Holds for every radius and is guaranteed by construction: a block is dropped exactly
        // when all eight of its neighbours are inside the disc, so a block that survived has an
        // 8-neighbour outside the disc, which is therefore not in the outline either.
        var set = Circle.Rasterize(c, r, Max);
        foreach (var p in set)
        {
            bool buried = true;
            for (int dx = -1; dx <= 1 && buried; dx++)
                for (int dz = -1; dz <= 1 && buried; dz++)
                    if ((dx != 0 || dz != 0) && !set.Contains(new BlockXZ(p.X + dx, p.Z + dz))) buried = false;
            Assert.False(buried, $"r={r} {c}: {p} has all eight neighbours in the outline.\n{Shape.ToArt(set)}");
        }
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Outline_is_one_block_thick_from_radius_2_up(double r, ShapeCenter c)
    {
        // The old rule let this be asserted from r=1.5; the disc-boundary rule needs r>=2.
        // The three exceptions are exhaustively known (checked below, not merely skipped):
        // r=1 at a block centre is the solid plus, and r=1.5 at the two half-step centres is a
        // 4x4 / 4x3 blob that is entirely boundary. From r=2 on, no block of any circle at any
        // of the three centre parities has all four edge-neighbours in the outline.
        var set = Circle.Rasterize(c, r, Max);
        bool anyBuried = set.Any(p => p.Neighbours4().All(set.Contains));
        bool knownException = (r == 1 && c == Shape.Block) || (r == 1.5 && c != Shape.Block);
        if (r >= 2)
            Assert.False(anyBuried, $"r={r} {c}: a block is buried inside a thick band.\n{Shape.ToArt(set)}");
        else
            Assert.Equal(knownException, anyBuried);
    }

    [Theory]
    [MemberData(nameof(Shape.Centers), MemberType = typeof(Shape))]
    public void Block_count_is_non_decreasing_in_radius(ShapeCenter c)
    {
        int prev = 0;
        for (int i = 1; i <= 128; i++)
        {
            double r = i * 0.5;
            int n = Circle.Rasterize(c, r, Max).Count;
            Assert.True(n >= prev, $"{c}: count dropped from {prev} (r={r - 0.5}) to {n} (r={r})");
            prev = n;
        }
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Block_count_equals_the_perimeter_of_the_bounding_box(double r, ShapeCenter c)
    {
        // This replaces the old "about 4*sqrt(2)*r" sanity band, which was the count of an
        // 8-CONNECTED digital circle (each of the 4 quadrant arcs is a mix of edge and diagonal
        // steps, ~sqrt(2)*r blocks). A 4-connected circle has a different constant, and it is
        // exact rather than approximate: each quadrant arc is a monotone staircase that must
        // travel (W-1)/2 blocks in x and (H-1)/2 in z using edge steps only, so the four arcs
        // together visit exactly the border of the bounding box:
        //         count = 2*(W-1) + 2*(H-1)   (= 2W + 2H - 4, the box perimeter)
        // At a block centre W = H = 2r+1, so count = 8r exactly - a factor sqrt(2) more than
        // the 4*sqrt(2)*r of the old outline, which is precisely the cost of never stepping
        // diagonally. Exceptions, all degenerate blobs rather than rings: r=0.5 and r=1 at a
        // block centre, and r=1.5 at the edge centre.
        var set = Circle.Rasterize(c, r, Max);
        bool degenerate = (r <= 1 && c == Shape.Block) || (r == 1.5 && c == Shape.Edge);
        if (degenerate) return;
        var (w, h) = Shape.BoundingBox(set);
        Assert.Equal(2 * (w - 1) + 2 * (h - 1), set.Count);
    }

    [Fact]
    public void Large_circle_counts_are_exactly_8r_at_a_block_centre()
    {
        // The concrete numbers behind the identity above, pinned so a change is visible.
        Assert.Equal(88, Circle.Rasterize(Shape.Block, 11, Max).Count);   // 8*11
        Assert.Equal(84, Circle.Rasterize(Shape.Corner, 11, Max).Count);  // box 22x22
        Assert.Equal(86, Circle.Rasterize(Shape.Edge, 11, Max).Count);    // box 22x23
        Assert.Equal(512, Circle.Rasterize(Shape.Block, 64, Max).Count);  // 8*64
        Assert.Equal(508, Circle.Rasterize(Shape.Corner, 64, Max).Count); // box 128x128
        Assert.Equal(510, Circle.Rasterize(Shape.Edge, 64, Max).Count);   // box 128x129
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Radius_still_means_the_extent_in_blocks_from_the_centre(double r, ShapeCenter c)
    {
        // The boundary offset of 0.25 is exactly what keeps this true: the smallest offset the
        // lattice can reach beyond r is r+0.5, which is still outside r+0.25, so the outer
        // extent is unchanged from the old midpoint rasterizer. Along a block-centred axis the
        // largest reachable integer offset is floor(r + 0.25) = floor(r), giving 2*floor(r)+1
        // columns; along a half-step axis the largest reachable half-integer offset gives an
        // even column count (2r for integer r, 2r+1 = 2k+2 for r = k+0.5).
        var set = Circle.Rasterize(c, r, Max);
        var (w, h) = Shape.BoundingBox(set);
        int odd = 2 * (int)Math.Floor(r) + 1;
        if (c == Shape.Block)
        {
            Assert.Equal(odd, w);
            Assert.Equal(odd, h);
        }
        else
        {
            Assert.Equal(0, w % 2);                                  // even diameter across X
            if (c == Shape.Corner) Assert.Equal(0, h % 2);           // even across Z too
            else Assert.Equal(odd, h);                               // edge centre: Z is block-aligned
        }
    }

    // ---------- validation ----------

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(-1)]
    [InlineData(1.3)]
    [InlineData(double.NaN)]
    public void Rejects_bad_radii(double r)
    {
        Assert.ThrowsAny<ArgumentException>(() => Circle.Rasterize(Shape.Block, r, Max));
    }
}
