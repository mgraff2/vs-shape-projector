namespace ShapeProjector.Geometry.Internal;

/// <summary>Rounding and validation helpers shared by the rasterizers.</summary>
internal static class Lattice
{
    /// <summary>Tolerance for detecting exact ties and exact half-steps after floating point.</summary>
    public const double Eps = 1e-7;

    /// <summary>
    /// Nearest integer to <paramref name="value"/>. An exact tie (fraction 0.5) is resolved
    /// downward when <paramref name="tieDown"/> is true, upward otherwise. Callers pass the
    /// direction that points toward the shape centre.
    /// </summary>
    public static int RoundTie(double value, bool tieDown)
    {
        double fl = Math.Floor(value);
        double frac = value - fl;
        if (Math.Abs(frac - 0.5) < Eps) return tieDown ? (int)fl : (int)fl + 1;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Snaps a centre-relative coordinate <paramref name="rel"/> to the block whose centre is nearest
    /// to <c>centre + rel</c>. Exact ties are resolved away from the centre; a value that lies exactly
    /// on the centre of a half-step lattice (rel == 0 on a half-step centre) snaps to the + side.
    /// </summary>
    public static int SnapAwayFromCenter(double centre, double rel)
    {
        double world = centre + rel;
        double fl = Math.Floor(world);
        double frac = world - fl;
        if (Math.Abs(frac - 0.5) < Eps)
        {
            if (rel > Eps) return (int)fl + 1;
            if (rel < -Eps) return (int)fl;
            return (int)fl + 1;
        }
        return (int)Math.Round(world, MidpointRounding.AwayFromZero);
    }

    public static bool IsHalfStep(double v) => ShapeCenter.IsHalfStep(v);

    public static void RequireHalfStepRadius(double r, string name, int maxRadius)
    {
        if (!IsHalfStep(r) || r < 0.5)
            throw new ArgumentOutOfRangeException(name, r, $"{name} must be a multiple of 0.5 and at least 0.5.");
        RequireWithinMax(r, name, maxRadius);
    }

    public static void RequireWithinMax(double extent, string name, int maxRadius)
    {
        if (maxRadius < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "maxRadius must be at least 1.");
        if (!double.IsFinite(extent) || extent > maxRadius)
            throw new ArgumentOutOfRangeException(name, extent, $"{name} must not exceed maxRadius ({maxRadius}).");
    }

    public static void RequireThickness(int thickness)
    {
        if (thickness < 1)
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "outlineThickness must be at least 1.");
    }
}
