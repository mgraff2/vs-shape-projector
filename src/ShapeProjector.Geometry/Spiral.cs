using ShapeProjector.Geometry.Internal;

namespace ShapeProjector.Geometry;

/// <summary>Archimedean spiral (spec section 5): r = startRadius + spacing * theta / (2 pi).</summary>
public static class Spiral
{
    /// <summary>Default arc-length distance between successive samples, in blocks.</summary>
    public const double DefaultSampleStep = 0.25;

    /// <summary>
    /// The spiral as an ordered path of blocks, from the start radius outward.
    /// The curve is sampled every <paramref name="sampleStep"/> blocks of arc length, each sample is snapped to
    /// the nearest block (ties away from the centre), consecutive repeats are dropped, and any gap between
    /// successive blocks is bridged with a Bresenham line so the path is 8-connected in order. Where two laps
    /// touch at block resolution (spacing below about 2) a block can legitimately occur twice in the path;
    /// the set returned by <see cref="Rasterize"/> is always duplicate-free.
    /// <para>
    /// Angle 0 is +X. <paramref name="clockwise"/> advances from +X toward +Z, which is clockwise when the world is
    /// viewed from above with north (-Z) at the top; the opposite direction mirrors the path in Z.
    /// </para>
    /// <paramref name="turns"/> must be positive, <paramref name="spacing"/> and <paramref name="startRadius"/>
    /// non-negative, and the end radius (startRadius + spacing*turns) at most <paramref name="maxRadius"/>.
    /// </summary>
    public static IReadOnlyList<BlockXZ> RasterizePath(ShapeCenter center, double turns, double spacing, double startRadius, bool clockwise, int maxRadius = 128, double sampleStep = DefaultSampleStep)
    {
        if (!double.IsFinite(turns) || turns <= 0) throw new ArgumentOutOfRangeException(nameof(turns), turns, "turns must be positive.");
        if (!double.IsFinite(spacing) || spacing < 0) throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "spacing must be non-negative.");
        if (!double.IsFinite(startRadius) || startRadius < 0) throw new ArgumentOutOfRangeException(nameof(startRadius), startRadius, "startRadius must be non-negative.");
        if (!double.IsFinite(sampleStep) || sampleStep <= 0) throw new ArgumentOutOfRangeException(nameof(sampleStep), sampleStep, "sampleStep must be positive.");
        double endRadius = startRadius + spacing * turns;
        Lattice.RequireWithinMax(endRadius, "startRadius + spacing * turns", maxRadius);

        double k = spacing / (2 * Math.PI);        // dr/dtheta
        double thetaEnd = 2 * Math.PI * turns;
        double zSign = clockwise ? 1 : -1;

        var path = new List<BlockXZ>();
        double theta = 0;
        while (true)
        {
            double r = startRadius + k * theta;
            var p = new BlockXZ(
                Lattice.SnapAwayFromCenter(center.Dx, r * Math.Cos(theta)),
                Lattice.SnapAwayFromCenter(center.Dz, zSign * r * Math.Sin(theta)));
            Append(path, p, center);

            if (theta >= thetaEnd) break;
            double speed = Math.Sqrt(r * r + k * k);   // ds/dtheta
            double dTheta = speed > 1e-9 ? sampleStep / speed : Math.PI / 8;
            theta = Math.Min(thetaEnd, theta + dTheta);
        }
        // A lap that closes on its own start (spacing 0) must not list the start twice.
        if (path.Count > 1 && path[^1].Equals(path[0])) path.RemoveAt(path.Count - 1);
        return path;
    }

    /// <summary>
    /// The spiral as a block set. <paramref name="outlineThickness"/> &gt; 1 adds the paths of the same spiral
    /// with the start radius reduced by 1, 2, ... (thickness - 1) blocks, i.e. the band grows toward the centre.
    /// </summary>
    public static IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, double turns, double spacing, double startRadius, bool clockwise, int maxRadius = 128, int outlineThickness = 1, double sampleStep = DefaultSampleStep)
    {
        Lattice.RequireThickness(outlineThickness);
        var set = new HashSet<BlockXZ>(RasterizePath(center, turns, spacing, startRadius, clockwise, maxRadius, sampleStep));
        for (int t = 1; t < outlineThickness; t++)
            set.UnionWith(RasterizePath(center, turns, spacing, Math.Max(0, startRadius - t), clockwise, maxRadius, sampleStep));
        return Clip.ToWindow(set, maxRadius);
    }

    /// <summary>Consecutive dedupe, then bridge any gap to the previous block with a Bresenham line.</summary>
    private static void Append(List<BlockXZ> path, BlockXZ p, ShapeCenter center)
    {
        if (path.Count == 0)
        {
            path.Add(p);
            return;
        }
        var last = path[^1];
        if (last.Equals(p)) return;
        if (!last.IsAdjacent8(p))
        {
            var bridge = Bresenham.Line(last, p, center);
            for (int i = 1; i < bridge.Count - 1; i++) path.Add(bridge[i]);
        }
        path.Add(p);
    }
}
