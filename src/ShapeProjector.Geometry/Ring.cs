using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Ring / annulus (spec section 5): the union of two circles sharing the centre.</summary>
public static class Ring
{
    /// <summary>
    /// Union of <see cref="Circle.Rasterize"/> at <paramref name="innerRadius"/> and at <paramref name="outerRadius"/>.
    /// Both radii are multiples of 0.5, at least 0.5, inner strictly smaller than outer, outer at most
    /// <paramref name="maxRadius"/>. Thickness is applied to each circle separately (each grows inward).
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, double innerRadius, double outerRadius, int maxRadius = 128, int outlineThickness = 1)
    {
        Lattice.RequireHalfStepRadius(innerRadius, nameof(innerRadius), maxRadius);
        Lattice.RequireHalfStepRadius(outerRadius, nameof(outerRadius), maxRadius);
        if (innerRadius >= outerRadius)
            throw new ArgumentOutOfRangeException(nameof(innerRadius), innerRadius, "innerRadius must be smaller than outerRadius.");
        var set = new HashSet<BlockXZ>(Circle.Rasterize(center, innerRadius, maxRadius, outlineThickness));
        set.UnionWith(Circle.Rasterize(center, outerRadius, maxRadius, outlineThickness));
        return set;
    }
}
