namespace ShapeProjector.Geometry;

/// <summary>
/// Spec section 10a: "Triangle" is a dropdown alias for regular polygon n=3 (circumradius + rotation).
/// There is no triangle rasterizer and no TriangleSpec type — the alias produces a
/// <see cref="RegularPolygonSpec"/> with <see cref="Sides"/> = 3, so triangle output is block-for-block
/// the polygon rasterizer's output and every polygon rule (radial adjustment, vertical-mode default,
/// validation) applies unchanged.
/// </summary>
public static class Triangle
{
    public const int Sides = 3;

    /// <summary>The spec a "Triangle" dropdown entry creates: exactly regular polygon n=3.</summary>
    public static RegularPolygonSpec Spec(double circumradius, double rotationDegrees = 0) =>
        new(Sides, circumradius, rotationDegrees);

    /// <summary>True when <paramref name="spec"/> should display as "Triangle" (polygon with 3 sides).</summary>
    public static bool Is(ShapeSpec spec) => spec is RegularPolygonSpec p && p.Sides == Sides;
}
