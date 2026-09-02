using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Circle outline (spec section 5): the boundary layer of a filled disc around a half-step centre.</summary>
public static class Circle
{
    /// <summary>
    /// Rasterizes the outline of a circle of <paramref name="radius"/> (multiple of 0.5, at least 0.5,
    /// at most <paramref name="maxRadius"/>) around <paramref name="center"/>. Blocks outside the window
    /// |x|,|z| &lt;= maxRadius around the projector are clipped. <paramref name="outlineThickness"/> &gt; 1
    /// grows the outline inward.
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, double radius, int maxRadius = 128, int outlineThickness = 1)
    {
        Lattice.RequireHalfStepRadius(radius, nameof(radius), maxRadius);
        Lattice.RequireThickness(outlineThickness);
        var outline = ConicBoundary.Rasterize(center, radius, radius);
        return Clip.ToWindow(Thickness.ThickenClosed(outline, outlineThickness), maxRadius);
    }
}
