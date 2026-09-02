using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Regular polygon outline (spec section 5): snapped vertices joined by Bresenham edges, rotated about the centre.</summary>
public static class RegularPolygon
{
    /// <summary>
    /// The snapped vertices, in order. Vertex i sits at angle <c>rotationDegrees + 360*i/sides</c>, measured
    /// from +X toward +Z, at distance <paramref name="circumradius"/> from the centre. Each real-valued vertex
    /// is snapped to the block whose centre is nearest; an exact tie is resolved away from the centre.
    /// </summary>
    public static IReadOnlyList<BlockXZ> Vertices(ShapeCenter center, int sides, double circumradius, double rotationDegrees)
    {
        if (sides < 3) throw new ArgumentOutOfRangeException(nameof(sides), sides, "A polygon needs at least 3 sides.");
        if (!double.IsFinite(circumradius) || circumradius <= 0)
            throw new ArgumentOutOfRangeException(nameof(circumradius), circumradius, "circumradius must be positive.");
        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), rotationDegrees, "rotationDegrees must be finite.");

        var verts = new BlockXZ[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = (rotationDegrees + 360.0 * i / sides) * Math.PI / 180.0;
            double u = circumradius * Math.Cos(angle);
            double v = circumradius * Math.Sin(angle);
            verts[i] = new BlockXZ(Lattice.SnapAwayFromCenter(center.Dx, u), Lattice.SnapAwayFromCenter(center.Dz, v));
        }
        return verts;
    }

    /// <summary>
    /// Rasterizes the closed polygon: every edge, including the one from the last vertex back to the first,
    /// as a Bresenham line. <paramref name="circumradius"/> must not exceed <paramref name="maxRadius"/>.
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int sides, double circumradius, double rotationDegrees, int maxRadius = 128, int outlineThickness = 1)
    {
        var verts = Vertices(center, sides, circumradius, rotationDegrees);
        Lattice.RequireWithinMax(circumradius, nameof(circumradius), maxRadius);
        Lattice.RequireThickness(outlineThickness);

        var outline = new HashSet<BlockXZ>();
        for (int i = 0; i < verts.Count; i++)
            outline.UnionWith(Bresenham.Line(verts[i], verts[(i + 1) % verts.Count], center));
        return Clip.ToWindow(Thickness.ThickenClosed(outline, outlineThickness), maxRadius);
    }
}
