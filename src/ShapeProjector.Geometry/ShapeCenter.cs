namespace ShapeProjector.Geometry;

/// <summary>
/// The shape centre, expressed as an offset (dx, dz) from the projector block's centre in
/// steps of 0.5 blocks (spec section 3). (0,0) is the projector block itself; (0.5,0.5) is the
/// corner shared by the projector and its +X/+Z neighbours; (0.5,0) is the edge shared with
/// the +X neighbour. There is exactly one mechanism: every rasterizer works with the real
/// number centre and never branches on integer-vs-half-integer.
/// </summary>
public readonly record struct ShapeCenter
{
    public double Dx { get; }
    public double Dz { get; }

    public ShapeCenter(double dx, double dz)
    {
        Validate(dx, nameof(dx));
        Validate(dz, nameof(dz));
        Dx = dx;
        Dz = dz;
    }

    /// <summary>The projector block itself, offset (0, 0).</summary>
    public static ShapeCenter Origin => default;

    /// <summary>True when the given value is a finite multiple of 0.5.</summary>
    public static bool IsHalfStep(double value) =>
        double.IsFinite(value) && Math.Abs(value * 2 - Math.Round(value * 2)) < 1e-9;

    private static void Validate(double value, string name)
    {
        if (!IsHalfStep(value))
            throw new ArgumentException($"Centre offset must be a finite multiple of 0.5, got {value}.", name);
    }

    public override string ToString() => $"centre({Dx},{Dz})";
}
