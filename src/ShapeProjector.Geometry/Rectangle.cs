using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Axis-aligned rectangle outline (spec section 5). Width runs along X, depth along Z, both in blocks.</summary>
public static class Rectangle
{
    /// <summary>
    /// The size that is actually drawn. A rectangle can only be centred exactly when the parity of a
    /// side matches the centre on that axis: an integer centre coordinate needs an odd side, a half-step
    /// centre coordinate needs an even side. A mismatching side is rounded <em>outward</em> by one block
    /// (the rectangle grows half a block on each face), each axis independently.
    /// </summary>
    public static (int Width, int Depth) EffectiveSize(ShapeCenter center, int width, int depth)
    {
        RequirePositive(width, nameof(width));
        RequirePositive(depth, nameof(depth));
        return (Fit(center.Dx, width), Fit(center.Dz, depth));
    }

    /// <summary>
    /// Rasterizes the rectangle outline (all blocks of the effective footprint that lie on its boundary).
    /// Sides of 1 block produce a line. Either effective side may be at most 2*maxRadius+1 blocks.
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int width, int depth, int maxRadius = 128, int outlineThickness = 1)
    {
        var (w, d) = EffectiveSize(center, width, depth);
        Lattice.RequireWithinMax((w - 1) / 2.0, nameof(width), maxRadius);
        Lattice.RequireWithinMax((d - 1) / 2.0, nameof(depth), maxRadius);
        Lattice.RequireThickness(outlineThickness);

        var (xMin, xMax) = Span(center.Dx, w);
        var (zMin, zMax) = Span(center.Dz, d);
        var outline = new HashSet<BlockXZ>();
        for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
                if (x == xMin || x == xMax || z == zMin || z == zMax)
                    outline.Add(new BlockXZ(x, z));
        return Clip.ToWindow(Thickness.ThickenClosed(outline, outlineThickness), maxRadius);
    }

    /// <summary>Inclusive block range [min, max] of a side of length <paramref name="size"/> centred on <paramref name="centre"/>.</summary>
    public static (int Min, int Max) Span(double centre, int size)
    {
        int min = (int)Math.Round(centre - size / 2.0 + 0.5);
        return (min, min + size - 1);
    }

    private static int Fit(double centre, int size)
    {
        bool centreIsInteger = Math.Abs(centre - Math.Round(centre)) < Lattice.Eps;
        bool sizeIsOdd = (size & 1) == 1;
        return centreIsInteger == sizeIsOdd ? size : size + 1;
    }

    private static void RequirePositive(int v, string name)
    {
        if (v < 1) throw new ArgumentOutOfRangeException(name, v, $"{name} must be at least 1 block.");
    }
}
