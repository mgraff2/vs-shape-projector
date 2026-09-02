namespace ShapeProjector.Geometry.Internal;

/// <summary>
/// Circle / ellipse outlines as the <em>boundary layer of a filled disc</em> — the rule the
/// online pixel-circle generators use (donatj/Circle-Generator, "thick" mode).
/// <para>
/// A block is <em>inside</em> when its centre lies within the ellipse; a block is on the
/// <em>outline</em> when it is inside and at least one of its eight neighbours is not. Excluding
/// on all eight neighbours (rather than only the four edge neighbours) keeps the extra corner
/// block at every diagonal step, which is what makes the result 4-connected: the outline never
/// steps corner-to-corner, it turns an L of edge-sharing blocks. The 4-neighbour variant would
/// give the thinner, 8-connected ring that steps diagonally.
/// </para>
/// <para>
/// <b>Where the boundary sits.</b> Radii and centre offsets are multiples of 0.5, so every block's
/// offset from the centre is a multiple of 0.5 too and every squared distance a lattice block can
/// have is a multiple of 0.25. Testing against the nominal radius r would therefore put lattice
/// blocks <em>exactly</em> on the curve (r = 11 at a block centre puts one there and nothing else in
/// that row), which pinches the poles down to a single block. The test radius is instead
/// <c>r + <see cref="BoundaryOffset"/></c>, i.e. one quarter block — exactly halfway between the
/// last offset the lattice can reach (r) and the next one (r + 0.5). Then
/// <c>(r+0.25)^2 = r^2 + 0.5r + 0.0625</c> is congruent to 0.0625 modulo 0.25 while every lattice
/// squared distance is a multiple of 0.25, so <b>no block can ever land exactly on the curve</b> —
/// and the outer extent is unchanged, because the smallest reachable offset above r is r + 0.5,
/// which is still outside. The bounding box of the result is identical to the old midpoint
/// rasterizer's for every radius 0.5..64 at every centre parity: a saved radius still means what
/// it meant.
/// </para>
/// <para>
/// Both passes are pure functions of the real-valued centre, so integer and half-integer centres
/// share one code path (spec section 3) — there is no even/odd branch.
/// </para>
/// </summary>
internal static class ConicBoundary
{
    /// <summary>
    /// How far outside the nominal radius the boundary is drawn, in blocks. A quarter block is
    /// half of the 0.5 lattice pitch, which is what guarantees no block ever lies on the curve.
    /// </summary>
    public const double BoundaryOffset = 0.25;

    public static HashSet<BlockXZ> Rasterize(ShapeCenter c, double rx, double rz)
    {
        double bx = rx + BoundaryOffset, bz = rz + BoundaryOffset;
        // All of bx2, bz2, limit and the terms below are multiples of 1/256 for every legal
        // radius and centre, so the comparison is exact in double; Eps only pins the tie.
        double bx2 = bx * bx, bz2 = bz * bz, limit = bx2 * bz2;

        bool Inside(double u, double v) => bz2 * u * u + bx2 * v * v <= limit + Lattice.Eps;

        int xMin = (int)Math.Ceiling(c.Dx - bx);
        int xMax = (int)Math.Floor(c.Dx + bx);
        int zMin = (int)Math.Ceiling(c.Dz - bz);
        int zMax = (int)Math.Floor(c.Dz + bz);

        // Row spans of the filled disc. The disc is convex, so each row is one contiguous run;
        // lo > hi marks an empty row.
        int rows = zMax - zMin + 1;
        var lo = new int[rows];
        var hi = new int[rows];
        for (int i = 0; i < rows; i++)
        {
            double v = zMin + i - c.Dz;
            double t = 1 - v * v / bz2;
            double half = t > 0 ? bx * Math.Sqrt(t) : 0;
            // Seed one block outside the float estimate on each side, then walk in with the
            // exact predicate, so the span never depends on the sqrt's last bit.
            int h = Math.Min(xMax, (int)Math.Floor(c.Dx + half) + 1);
            while (h >= xMin && !Inside(h - c.Dx, v)) h--;
            int l = Math.Max(xMin, (int)Math.Ceiling(c.Dx - half) - 1);
            while (l <= h && !Inside(l - c.Dx, v)) l++;
            if (h < xMin || l > h) { lo[i] = 1; hi[i] = 0; }   // empty row
            else { lo[i] = l; hi[i] = h; }
        }

        // Covers(i, x) == the three blocks (x-1..x+1) of row i are all inside.
        bool Covers(int i, int x) => i >= 0 && i < rows && x - 1 >= lo[i] && x + 1 <= hi[i];

        var outline = new HashSet<BlockXZ>();
        for (int i = 0; i < rows; i++)
        {
            int z = zMin + i;
            for (int x = lo[i]; x <= hi[i]; x++)
                if (!(Covers(i, x) && Covers(i - 1, x) && Covers(i + 1, x)))
                    outline.Add(new BlockXZ(x, z));
        }
        return outline;
    }
}
