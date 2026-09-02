namespace ShapeProjector.Geometry.Tests;

public class EllipseTests
{
    /// <summary>Where the boundary is drawn, per axis: a quarter block outside the nominal radius.</summary>
    const double Offset = 0.25;

    public static IEnumerable<object[]> Grid()
    {
        foreach (var c in Shape.Centers())
            for (int i = 1; i <= 24; i++)
                for (int j = 1; j <= 24; j++)
                    yield return [i * 0.5, j * 0.5, c[0]];
    }

    [Theory]
    [MemberData(nameof(Shape.RadiiAndCenters), MemberType = typeof(Shape))]
    public void Ellipse_with_equal_radii_is_exactly_the_circle(double r, ShapeCenter c)
    {
        Shape.AssertSameSet(Circle.Rasterize(c, r), Ellipse.Rasterize(c, r, r));
    }

    [Fact]
    public void Ellipse_3_by_1_at_block()
    {
        // Boundary semi-axes 3.25 and 1.25. Inside: |x| <= 3 on row z=0, and on rows z=+-1
        // (1 - 1/1.5625) = 0.36, so |x| <= 3.25*0.6 = 1.95, i.e. x in -1..1.
        // Buried (all eight neighbours inside): only (0,0) - (+-1,+-1) are inside, (+-2,+-1)
        // are not, so (+-1,0) survive. Same 12 blocks as before, arranged 4-connectedly.
        //   x: -3 -2 -1 0 1 2 3
        // z=-1:  . . # # # . .
        // z= 0:  # # # . # # #
        // z= 1:  . . # # # . .
        var expected = Shape.FromArt("""
            ..###..
            ###.###
            ..###..
            """, -3, -1);
        Shape.AssertSameSet(expected, Ellipse.Rasterize(Shape.Block, 3, 1));
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Ellipse_is_a_closed_ring_without_duplicates(double rx, double rz, ShapeCenter c)
    {
        var set = Ellipse.Rasterize(c, rx, rz);
        Shape.AssertNoDuplicates(set);
        Shape.AssertClosedRing(set, $"ellipse {rx}x{rz} {c}");
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Ellipse_outline_is_4_connected(double rx, double rz, ShapeCenter c)
    {
        // The circle change carries to the ellipse: it is the same disc-boundary rule with two
        // semi-axes, so the outline never steps corner-to-corner at any aspect ratio.
        Shape.Assert4Connected(Ellipse.Rasterize(c, rx, rz), $"ellipse {rx}x{rz} {c}");
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Ellipse_has_both_mirror_symmetries_and_the_diagonal_one_when_round(double rx, double rz, ShapeCenter c)
    {
        Shape.AssertSymmetric(Ellipse.Rasterize(c, rx, rz), c, expectDiagonal: rx == rz && Shape.SameFraction(c), $"ellipse {rx}x{rz} {c}");
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Every_block_lies_in_the_boundary_band_of_the_ellipse(double rx, double rz, ShapeCenter c)
    {
        // The old "within 0.5 of the curve along its row or column" was a midpoint-selection
        // guarantee. For the disc-boundary rule the honest statement is in the NORMALISED
        // radius rho = sqrt((u/bx)^2 + (v/bz)^2), where bx = rx+0.25 and bz = rz+0.25 are the
        // boundary semi-axes:
        //   kept  =>  inside the boundary ellipse        =>  rho <= 1
        //   kept  =>  some 8-neighbour q has rho(q) > 1. A neighbour step changes u and v by at
        //             most 1 each, so |rho(p) - rho(q)| <= sqrt(1/bx^2 + 1/bz^2) = step, hence
        //             rho > 1 - step.
        // For a circle (bx = bz = r+0.25) this collapses to the circle test's band
        // r + 0.25 - sqrt(2) < d <= r + 0.25.
        double bx = rx + Offset, bz = rz + Offset;
        double step = Math.Sqrt(1 / (bx * bx) + 1 / (bz * bz));
        foreach (var p in Ellipse.Rasterize(c, rx, rz))
        {
            double u = (p.X - c.Dx) / bx, v = (p.Z - c.Dz) / bz;
            double rho = Math.Sqrt(u * u + v * v);
            Assert.True(rho <= 1 + 1e-9, $"ellipse {rx}x{rz} {c}: {p} has rho={rho:F4} > 1 (outside the boundary ellipse)");
            Assert.True(rho > 1 - step - 1e-9, $"ellipse {rx}x{rz} {c}: {p} has rho={rho:F4}, more than {step:F4} inside the boundary");
        }
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Ellipse_outline_has_no_interior_block(double rx, double rz, ShapeCenter c)
    {
        var set = Ellipse.Rasterize(c, rx, rz);
        foreach (var p in set)
        {
            bool buried = true;
            for (int dx = -1; dx <= 1 && buried; dx++)
                for (int dz = -1; dz <= 1 && buried; dz++)
                    if ((dx != 0 || dz != 0) && !set.Contains(new BlockXZ(p.X + dx, p.Z + dz))) buried = false;
            Assert.False(buried, $"ellipse {rx}x{rz} {c}: {p} has all eight neighbours in the outline.");
        }
    }

    [Fact]
    public void Swapping_radii_transposes_the_outline_at_a_block_centre()
    {
        var a = Ellipse.Rasterize(Shape.Block, 7, 3);
        var b = Ellipse.Rasterize(Shape.Block, 3, 7).Select(p => new BlockXZ(p.Z, p.X));
        Shape.AssertSameSet(a, b);
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Extent_at_a_block_centre_is_unchanged_by_the_new_rule(double rx, double rz, ShapeCenter c)
    {
        // Radius semantics. At the block centre there is a block row and a block column through
        // the centre, so each extent is the largest integer offset within r + 0.25 - that is
        // floor(r), exactly what the old midpoint rasterizer produced, for all 576 radius pairs.
        // At a half-step centre the extent is even along that axis (the 2x2 / 1x2 centre of
        // spec section 3); it can be one block short of the old rule for a very thin ellipse,
        // which Thin_ellipse_at_a_half_step_centre_loses_one_tip_block pins exactly.
        var (w, h) = Shape.BoundingBox(Ellipse.Rasterize(c, rx, rz));
        if (c.Dx == 0) Assert.Equal(2 * (int)Math.Floor(rx) + 1, w); else Assert.Equal(0, w % 2);
        if (c.Dz == 0 && c.Dx == 0) Assert.Equal(2 * (int)Math.Floor(rz) + 1, h);
        else if (c.Dz != 0) Assert.Equal(0, h % 2);
    }

    [Fact]
    public void Thin_ellipse_at_a_half_step_centre_loses_one_tip_block()
    {
        // The one extent the new rule changes, recorded rather than hidden. rx = 1.5 on the
        // edge centre (0.5, 0): the nearest block column is 0.5 off the long axis, and the
        // boundary ellipse loses sqrt(1 - (0.5/1.75)^2) = 95.8% of its long semi-axis there.
        // With rz = 6 that is 6.25 * 0.958 = 5.99 - just short of 6, so each tip drops a block.
        // A circle can never do this (its two semi-axes are equal, so the loss is at most the
        // 0.25 boundary offset), and no ellipse does it once both semi-axes are >= 5.
        var thin = Ellipse.Rasterize(Shape.Edge, 1.5, 6);
        Assert.Equal(5, thin.Max(p => p.Z));     // the old midpoint rule reached 6
        Assert.Equal(-5, thin.Min(p => p.Z));
        Assert.Equal((4, 11), Shape.BoundingBox(thin));

        Assert.Equal(6, Ellipse.Rasterize(Shape.Edge, 5, 6).Max(p => p.Z));      // round-ish: unaffected
        Assert.Equal(6, Ellipse.Rasterize(Shape.Block, 1.5, 6).Max(p => p.Z));   // block centre: unaffected
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0.25)]
    [InlineData(-1, 1)]
    public void Rejects_bad_radii(double rx, double rz)
    {
        Assert.ThrowsAny<ArgumentException>(() => Ellipse.Rasterize(Shape.Block, rx, rz));
    }
}
