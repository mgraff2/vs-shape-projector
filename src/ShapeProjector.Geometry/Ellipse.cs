using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Axis-aligned ellipse outline (spec section 5): the disc-boundary criterion generalized to two radii.</summary>
public static class Ellipse
{
    /// <summary>
    /// Rasterizes an ellipse with semi-axes <paramref name="radiusX"/> (along X) and <paramref name="radiusZ"/>
    /// (along Z), both multiples of 0.5 and at least 0.5, neither above <paramref name="maxRadius"/>.
    /// With equal radii the output is identical to <see cref="Circle.Rasterize"/> (same code path).
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, double radiusX, double radiusZ, int maxRadius = 128, int outlineThickness = 1)
    {
        Lattice.RequireHalfStepRadius(radiusX, nameof(radiusX), maxRadius);
        Lattice.RequireHalfStepRadius(radiusZ, nameof(radiusZ), maxRadius);
        Lattice.RequireThickness(outlineThickness);
        var outline = ConicBoundary.Rasterize(center, radiusX, radiusZ);
        return Clip.ToWindow(Thickness.ThickenClosed(outline, outlineThickness), maxRadius);
    }
}
