namespace ShapeProjector.Geometry;

/// <summary>Outcome of one radial adjustment (spec section 10d).</summary>
public enum RadialAdjustOutcome
{
    /// <summary>The radial dimension moved by one step; <see cref="RadialAdjustResult.Spec"/> is the new spec.</summary>
    Adjusted,
    /// <summary>
    /// The adjustment would underflow the shape's minimum. The layer stays put <b>entirely</b> — no parameter
    /// moved (a ring never moves only its outer). Callers must surface this visibly: after a clamp,
    /// -1 then +1 is <b>not</b> a round-trip (the layer ends one step bigger).
    /// </summary>
    Clamped,
    /// <summary>The shape has no honest radial dimension (spiral). The spec is returned unchanged.</summary>
    NotApplicable,
}

/// <summary>
/// Result of <see cref="RadialAdjust.Adjust"/>. <paramref name="Spec"/> is always usable: the adjusted spec
/// on <see cref="RadialAdjustOutcome.Adjusted"/>, the original spec otherwise.
/// </summary>
public readonly record struct RadialAdjustResult(ShapeSpec Spec, RadialAdjustOutcome Outcome)
{
    /// <summary>True only when the spec actually changed.</summary>
    public bool Changed => Outcome == RadialAdjustOutcome.Adjusted;
}

/// <summary>
/// Spec section 10d radial-adjustment semantics, shared by "Add Layer Out" (always +1) and
/// "Global radius +1/-1" (both directions). Pure: takes a spec, returns a result; never touches game state.
/// Per-shape semantics: circle radius +-1; ring inner AND outer +-1 (thickness preserved); ellipse both
/// radii +-1; polygon circumradius +-1 (triangle is polygon n=3); rectangle width AND depth +-2 (one block
/// per side); spiral excluded. Minimums are derived from the rasterizer validators, see <see cref="Adjust"/>.
/// </summary>
public static class RadialAdjust
{
    /// <summary>Minimum circle/ring/ellipse radius, from <c>Lattice.RequireHalfStepRadius</c>: at least 0.5.</summary>
    private const double MinHalfStepRadius = 0.5;

    /// <summary>False exactly for shapes where "out" has no honest meaning (spiral) — drives button disabling.</summary>
    public static bool IsAdjustable(ShapeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec is not SpiralSpec;
    }

    /// <summary>
    /// Applies one radial step. <paramref name="direction"/> must be exactly +1 or -1.
    /// Assumes <paramref name="spec"/> itself is valid (it rasterized). Minimums, each derived from the
    /// existing validator of its rasterizer:
    /// circle/ring/ellipse radii &gt;= 0.5 (<c>Lattice.RequireHalfStepRadius</c>);
    /// polygon circumradius &gt; 0 (<c>RegularPolygon.Vertices</c>);
    /// rectangle width/depth &gt;= 1 (<c>Rectangle.RequirePositive</c>).
    /// A step that would cross a minimum returns <see cref="RadialAdjustOutcome.Clamped"/> with the spec unchanged.
    /// The upper bound (maxRadius) is not this function's job: it is enforced at rasterization time.
    /// </summary>
    public static RadialAdjustResult Adjust(ShapeSpec spec, int direction)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (direction is not (1 or -1))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "direction must be exactly +1 or -1.");

        double d = direction;
        return spec switch
        {
            CircleSpec c =>
                c.Radius + d < MinHalfStepRadius
                    ? Clamped(spec)
                    : Adjusted(c with { Radius = c.Radius + d }),

            // both radii move together; inner is the smaller, so it alone decides the clamp,
            // and shifting both preserves thickness and inner < outer
            RingSpec r =>
                r.InnerRadius + d < MinHalfStepRadius
                    ? Clamped(spec)
                    : Adjusted(r with { InnerRadius = r.InnerRadius + d, OuterRadius = r.OuterRadius + d }),

            EllipseSpec e =>
                Math.Min(e.RadiusX, e.RadiusZ) + d < MinHalfStepRadius
                    ? Clamped(spec)
                    : Adjusted(e with { RadiusX = e.RadiusX + d, RadiusZ = e.RadiusZ + d }),

            // RegularPolygon.Vertices: "circumradius must be positive" — strictly greater than 0
            RegularPolygonSpec p =>
                p.Circumradius + d <= 0
                    ? Clamped(spec)
                    : Adjusted(p with { Circumradius = p.Circumradius + d }),

            // +-2 = one block per side; Rectangle.RequirePositive: each side at least 1 block
            RectangleSpec r =>
                Math.Min(r.Width, r.Depth) + 2 * direction < 1
                    ? Clamped(spec)
                    : Adjusted(r with { Width = r.Width + 2 * direction, Depth = r.Depth + 2 * direction }),

            SpiralSpec => new RadialAdjustResult(spec, RadialAdjustOutcome.NotApplicable),

            // a future shape must decide its section-10d semantics explicitly, the way the spiral did
            _ => throw new NotSupportedException($"No radial-adjustment rule for shape type {spec.GetType().Name}."),
        };
    }

    private static RadialAdjustResult Adjusted(ShapeSpec spec) => new(spec, RadialAdjustOutcome.Adjusted);
    private static RadialAdjustResult Clamped(ShapeSpec spec) => new(spec, RadialAdjustOutcome.Clamped);
}
