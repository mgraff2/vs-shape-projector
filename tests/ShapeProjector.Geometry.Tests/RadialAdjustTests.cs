namespace ShapeProjector.Geometry.Tests;

/// <summary>
/// Spec section 10d: per-shape radial-adjustment semantics and clamping. Every radius-parameterized
/// property runs across the full half-step range 0.5..64 — both parities (integer and half-integer),
/// never a hand-picked few — because +-1 must preserve parity and clamping must trip at exactly the
/// validator minimum on both lattices.
/// </summary>
public class RadialAdjustTests
{
    static double Frac(double v) => v - Math.Floor(v);

    /// <summary>All half-step radii 0.5, 1.0, ..., 64.0.</summary>
    public static IEnumerable<object[]> HalfStepRadii()
    {
        for (int i = 1; i <= 128; i++) yield return [i * 0.5];
    }

    /// <summary>Half-step radii strictly above the shrink minimum: 1.5, 2.0, ..., 64.0 (r-1 >= 0.5).</summary>
    public static IEnumerable<object[]> ShrinkableRadii()
    {
        for (int i = 3; i <= 128; i++) yield return [i * 0.5];
    }

    // ---------- circle ----------

    [Theory]
    [MemberData(nameof(HalfStepRadii))]
    public void Circle_grow_adds_exactly_one_and_preserves_radius_parity(double r)
    {
        var res = RadialAdjust.Adjust(new CircleSpec(r), +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var c = Assert.IsType<CircleSpec>(res.Spec);
        Assert.Equal(r + 1, c.Radius);
        Assert.Equal(Frac(r), Frac(c.Radius));
    }

    [Theory]
    [MemberData(nameof(ShrinkableRadii))]
    public void Circle_shrink_subtracts_exactly_one_above_the_minimum(double r)
    {
        var res = RadialAdjust.Adjust(new CircleSpec(r), -1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var c = Assert.IsType<CircleSpec>(res.Spec);
        Assert.Equal(r - 1, c.Radius);
        Assert.Equal(Frac(r), Frac(c.Radius));
    }

    [Theory]
    [InlineData(0.5)] // half-integer parity at the minimum
    [InlineData(1.0)] // integer parity: 1.0 - 1 = 0.0 < 0.5 would violate RequireHalfStepRadius
    public void Circle_shrink_clamps_whole_layer_below_radius_half(double r)
    {
        var spec = new CircleSpec(r);
        var res = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
        Assert.Equal(spec, res.Spec); // stays put entirely
        Assert.False(res.Changed);
    }

    // ---------- ring ----------

    /// <summary>Inner sweeps the full half-step range; thickness covers both parities of the gap.</summary>
    public static IEnumerable<object[]> RingSpecs()
    {
        double[] thicknesses = [0.5, 1, 3, 10.5];
        for (int i = 1; i <= 128; i++)
            foreach (var t in thicknesses)
                yield return [i * 0.5, i * 0.5 + t];
    }

    [Theory]
    [MemberData(nameof(RingSpecs))]
    public void Ring_grow_moves_inner_and_outer_together_preserving_thickness(double inner, double outer)
    {
        var res = RadialAdjust.Adjust(new RingSpec(inner, outer), +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var ring = Assert.IsType<RingSpec>(res.Spec);
        Assert.Equal(inner + 1, ring.InnerRadius);
        Assert.Equal(outer + 1, ring.OuterRadius);
        Assert.Equal(outer - inner, ring.OuterRadius - ring.InnerRadius);
    }

    [Theory]
    [MemberData(nameof(RingSpecs))]
    public void Ring_shrink_moves_both_radii_or_clamps_both_never_partially(double inner, double outer)
    {
        var spec = new RingSpec(inner, outer);
        var res = RadialAdjust.Adjust(spec, -1);
        if (inner - 1 >= 0.5)
        {
            Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
            var ring = Assert.IsType<RingSpec>(res.Spec);
            Assert.Equal(inner - 1, ring.InnerRadius);
            Assert.Equal(outer - 1, ring.OuterRadius);
            Assert.Equal(outer - inner, ring.OuterRadius - ring.InnerRadius);
        }
        else
        {
            Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
            Assert.Equal(spec, res.Spec); // outer must NOT have moved alone
        }
    }

    [Theory]
    [InlineData(0.5, 23)]
    [InlineData(1.0, 64)]
    public void Ring_shrink_at_minimum_leaves_even_a_large_outer_untouched(double inner, double outer)
    {
        var spec = new RingSpec(inner, outer);
        var res = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
        Assert.Equal(outer, ((RingSpec)res.Spec).OuterRadius);
        Assert.Equal(inner, ((RingSpec)res.Spec).InnerRadius);
    }

    // ---------- ellipse ----------

    /// <summary>radiusX sweeps the full range; radiusZ covers both parities including the minimum.</summary>
    public static IEnumerable<object[]> EllipsePairs()
    {
        double[] others = [0.5, 1, 2.5, 20, 63.5, 64];
        for (int i = 1; i <= 128; i++)
            foreach (var o in others)
                yield return [i * 0.5, o];
    }

    [Theory]
    [MemberData(nameof(EllipsePairs))]
    public void Ellipse_grow_moves_both_radii_by_one_preserving_each_parity(double rx, double rz)
    {
        var res = RadialAdjust.Adjust(new EllipseSpec(rx, rz), +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var e = Assert.IsType<EllipseSpec>(res.Spec);
        Assert.Equal(rx + 1, e.RadiusX);
        Assert.Equal(rz + 1, e.RadiusZ);
        Assert.Equal(Frac(rx), Frac(e.RadiusX));
        Assert.Equal(Frac(rz), Frac(e.RadiusZ));
    }

    [Theory]
    [MemberData(nameof(EllipsePairs))]
    public void Ellipse_shrink_moves_both_radii_or_clamps_both_never_partially(double rx, double rz)
    {
        var spec = new EllipseSpec(rx, rz);
        var res = RadialAdjust.Adjust(spec, -1);
        if (Math.Min(rx, rz) - 1 >= 0.5)
        {
            Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
            var e = Assert.IsType<EllipseSpec>(res.Spec);
            Assert.Equal(rx - 1, e.RadiusX);
            Assert.Equal(rz - 1, e.RadiusZ);
        }
        else
        {
            Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
            Assert.Equal(spec, res.Spec); // the larger radius stays put too
        }
    }

    // ---------- regular polygon (and therefore triangle) ----------

    public static IEnumerable<object[]> PolygonSpecs()
    {
        int[] sides = [3, 4, 5, 6, 12];
        double[] rotations = [0, 30, 222.5];
        for (int i = 1; i <= 128; i++)
            foreach (var n in sides)
                foreach (var rot in rotations)
                    yield return [n, i * 0.5, rot];
    }

    [Theory]
    [MemberData(nameof(PolygonSpecs))]
    public void Polygon_grow_adds_one_to_circumradius_touching_nothing_else(int sides, double r, double rot)
    {
        var res = RadialAdjust.Adjust(new RegularPolygonSpec(sides, r, rot), +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var p = Assert.IsType<RegularPolygonSpec>(res.Spec);
        Assert.Equal(r + 1, p.Circumradius);
        Assert.Equal(sides, p.Sides);
        Assert.Equal(rot, p.RotationDegrees);
        Assert.Equal(Frac(r), Frac(p.Circumradius));
    }

    [Theory]
    [MemberData(nameof(PolygonSpecs))]
    public void Polygon_shrink_subtracts_one_or_clamps_when_result_would_not_be_positive(int sides, double r, double rot)
    {
        var spec = new RegularPolygonSpec(sides, r, rot);
        var res = RadialAdjust.Adjust(spec, -1);
        if (r - 1 > 0) // RegularPolygon.Vertices: "circumradius must be positive"
        {
            Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
            var p = Assert.IsType<RegularPolygonSpec>(res.Spec);
            Assert.Equal(r - 1, p.Circumradius);
            Assert.Equal(sides, p.Sides);
            Assert.Equal(rot, p.RotationDegrees);
        }
        else
        {
            Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
            Assert.Equal(spec, res.Spec);
        }
    }

    // ---------- rectangle ----------

    /// <summary>Width sweeps 1..128 (both parities); depth covers both parities including the minimums.</summary>
    public static IEnumerable<object[]> RectangleSizes()
    {
        int[] depths = [1, 2, 3, 8, 47, 128];
        for (int w = 1; w <= 128; w++)
            foreach (var d in depths)
                yield return [w, d];
    }

    [Theory]
    [MemberData(nameof(RectangleSizes))]
    public void Rectangle_grow_adds_two_to_each_side_preserving_odd_even_parity(int w, int d)
    {
        var res = RadialAdjust.Adjust(new RectangleSpec(w, d), +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
        var r = Assert.IsType<RectangleSpec>(res.Spec);
        Assert.Equal(w + 2, r.Width);
        Assert.Equal(d + 2, r.Depth);
        Assert.Equal(w & 1, r.Width & 1);
        Assert.Equal(d & 1, r.Depth & 1);
    }

    [Theory]
    [MemberData(nameof(RectangleSizes))]
    public void Rectangle_shrink_takes_two_from_each_side_or_clamps_both_never_partially(int w, int d)
    {
        var spec = new RectangleSpec(w, d);
        var res = RadialAdjust.Adjust(spec, -1);
        if (Math.Min(w, d) - 2 >= 1) // Rectangle.RequirePositive: at least 1 block
        {
            Assert.Equal(RadialAdjustOutcome.Adjusted, res.Outcome);
            var r = Assert.IsType<RectangleSpec>(res.Spec);
            Assert.Equal(w - 2, r.Width);
            Assert.Equal(d - 2, r.Depth);
        }
        else
        {
            Assert.Equal(RadialAdjustOutcome.Clamped, res.Outcome);
            Assert.Equal(spec, res.Spec); // the longer side stays put too
        }
    }

    [Theory]
    [InlineData(0.0, 5)]  // integer centre, odd side
    [InlineData(0.5, 6)]  // half-step centre, even side
    public void Rectangle_grow_is_exactly_one_block_per_side_in_block_coordinates(double centre, int size)
    {
        var before = Rectangle.Span(centre, size);
        var after = Rectangle.Span(centre, size + 2);
        Assert.Equal(before.Min - 1, after.Min);
        Assert.Equal(before.Max + 1, after.Max);
    }

    // ---------- spiral ----------

    [Theory]
    [InlineData(+1)]
    [InlineData(-1)]
    public void Spiral_adjustment_is_not_applicable_and_never_changes_the_spec(int direction)
    {
        var spec = new SpiralSpec(Turns: 3, Spacing: 2, StartRadius: 1.5, Clockwise: true);
        var res = RadialAdjust.Adjust(spec, direction);
        Assert.Equal(RadialAdjustOutcome.NotApplicable, res.Outcome);
        Assert.Equal(spec, res.Spec);
        Assert.False(res.Changed);
    }

    [Fact]
    public void IsAdjustable_is_false_exactly_for_the_spiral()
    {
        Assert.False(RadialAdjust.IsAdjustable(new SpiralSpec(2, 1, 0, false)));
        Assert.True(RadialAdjust.IsAdjustable(new CircleSpec(3)));
        Assert.True(RadialAdjust.IsAdjustable(new RingSpec(2, 4)));
        Assert.True(RadialAdjust.IsAdjustable(new EllipseSpec(2, 3.5)));
        Assert.True(RadialAdjust.IsAdjustable(new RegularPolygonSpec(3, 4, 0)));
        Assert.True(RadialAdjust.IsAdjustable(new RectangleSpec(4, 7)));
    }

    // ---------- round-trip properties ----------

    /// <summary>Every adjustable shape family, at every half-step size strictly above its shrink minimum, both parities.</summary>
    public static IEnumerable<object[]> SpecsAboveMinimum()
    {
        for (int i = 3; i <= 128; i++) // radii 1.5 .. 64
        {
            double r = i * 0.5;
            yield return [new CircleSpec(r)];
            yield return [new RingSpec(r, r + 3)];
            yield return [new EllipseSpec(r, r + 1.5)];
            yield return [new RegularPolygonSpec(5, r, 18)];
        }
        for (int w = 3; w <= 128; w++) // both parities of width
            yield return [new RectangleSpec(w, w + 5)];
    }

    /// <summary>Every adjustable shape family at a size where -1 must clamp, both parities.</summary>
    public static IEnumerable<object[]> SpecsAtMinimum()
    {
        yield return [new CircleSpec(0.5)];
        yield return [new CircleSpec(1.0)];
        yield return [new RingSpec(0.5, 23)];
        yield return [new RingSpec(1.0, 2.5)];
        yield return [new EllipseSpec(0.5, 40)];
        yield return [new EllipseSpec(37.5, 1.0)];
        yield return [new RegularPolygonSpec(3, 0.5, 0)];
        yield return [new RegularPolygonSpec(7, 1.0, 45)];
        yield return [new RectangleSpec(1, 50)];
        yield return [new RectangleSpec(2, 2)];
        yield return [new RectangleSpec(9, 2)];
    }

    [Theory]
    [MemberData(nameof(SpecsAboveMinimum))]
    public void Shrink_then_grow_roundtrips_exactly_when_nothing_clamped(ShapeSpec spec)
    {
        var down = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, down.Outcome);
        var up = RadialAdjust.Adjust(down.Spec, +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, up.Outcome);
        Assert.Equal(spec, up.Spec); // exact record equality — halves are exact in doubles
    }

    [Theory]
    [MemberData(nameof(SpecsAtMinimum))]
    public void Shrink_then_grow_after_a_clamp_ends_one_step_bigger_not_roundtripped(ShapeSpec spec)
    {
        var down = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Clamped, down.Outcome);
        Assert.Equal(spec, down.Spec);
        var up = RadialAdjust.Adjust(down.Spec, +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, up.Outcome);
        Assert.NotEqual(spec, up.Spec);                        // NOT a round-trip
        Assert.Equal(RadialAdjust.Adjust(spec, +1).Spec, up.Spec); // the clamped layer ends +1 bigger
    }

    [Theory]
    [MemberData(nameof(SpecsAtMinimum))]
    public void Grow_then_shrink_roundtrips_even_from_the_minimum(ShapeSpec spec)
    {
        var up = RadialAdjust.Adjust(spec, +1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, up.Outcome);
        var down = RadialAdjust.Adjust(up.Spec, -1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, down.Outcome);
        Assert.Equal(spec, down.Spec);
    }

    // ---------- minimums agree with the rasterizer validators ----------

    [Theory]
    [MemberData(nameof(SpecsAboveMinimum))]
    public void A_shrunk_spec_still_rasterizes_at_both_centre_parities(ShapeSpec spec)
    {
        var down = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Adjusted, down.Outcome);
        foreach (var c in new[] { Shape.Block, Shape.Corner })
            Assert.NotEmpty(down.Spec.Rasterize(c, 128, 1));
    }

    [Theory]
    [MemberData(nameof(SpecsAtMinimum))]
    public void A_clamped_spec_is_returned_unchanged_and_still_rasterizes(ShapeSpec spec)
    {
        var down = RadialAdjust.Adjust(spec, -1);
        Assert.Equal(RadialAdjustOutcome.Clamped, down.Outcome);
        foreach (var c in new[] { Shape.Block, Shape.Corner })
            Assert.NotEmpty(down.Spec.Rasterize(c, 128, 1));
    }

    // ---------- argument contract ----------

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void Direction_other_than_plus_or_minus_one_is_rejected(int direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialAdjust.Adjust(new CircleSpec(5), direction));
        // the argument contract precedes semantics, even for the excluded spiral
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialAdjust.Adjust(new SpiralSpec(2, 1, 0, true), direction));
    }

    private sealed record BogusSpec : ShapeSpec
    {
        public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
            new HashSet<BlockXZ>();
        public override VerticalMode DefaultVerticalMode => VerticalMode.FixedY;
    }

    [Fact]
    public void An_unknown_shape_type_is_rejected_loudly_not_silently_skipped()
    {
        // a future shape must decide its 10d semantics explicitly, the way the spiral did
        Assert.Throws<NotSupportedException>(() => RadialAdjust.Adjust(new BogusSpec(), +1));
    }
}
