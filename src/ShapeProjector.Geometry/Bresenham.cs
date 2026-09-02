namespace ShapeProjector.Geometry;

/// <summary>Integer line rasterization: one block per step along the major axis, 8-connected, both ends included.</summary>
public static class Bresenham
{
    /// <summary>
    /// Rasterizes the segment from <paramref name="a"/> to <paramref name="b"/> (inclusive, in order).
    /// The minor coordinate of every step is the nearest integer to the ideal line; an exact tie
    /// (only possible when the major length is even and the minor length is odd, or similar) is
    /// resolved toward the block nearer to <paramref name="tieToward"/> (the shape centre; the projector
    /// when omitted), and when both candidates are equidistant toward the lexicographically smaller
    /// block (Z, then X). Because the rule never looks at the direction of travel, Line(a,b) and
    /// Line(b,a) cover the same blocks and the result mirrors correctly about the centre.
    /// </summary>
    public static IReadOnlyList<BlockXZ> Line(BlockXZ a, BlockXZ b, ShapeCenter tieToward = default)
    {
        int dx = b.X - a.X, dz = b.Z - a.Z;
        int adx = Math.Abs(dx), adz = Math.Abs(dz);
        int n = Math.Max(adx, adz);
        var result = new List<BlockXZ>(n + 1);
        if (n == 0)
        {
            result.Add(a);
            return result;
        }

        bool xMajor = adx >= adz;
        int majorStart = xMajor ? a.X : a.Z, minorStart = xMajor ? a.Z : a.X;
        int majorSign = Math.Sign(xMajor ? dx : dz), minorSign = Math.Sign(xMajor ? dz : dx);
        int minorLen = xMajor ? adz : adx;

        for (int i = 0; i <= n; i++)
        {
            int major = majorStart + i * majorSign;
            long num = (long)i * minorLen;
            int q = (int)(num / n);
            long rem = num % n;
            int offset;
            if (2 * rem < n) offset = q;
            else if (2 * rem > n) offset = q + 1;
            else
            {
                var lo = Make(xMajor, major, minorStart + q * minorSign);
                var hi = Make(xMajor, major, minorStart + (q + 1) * minorSign);
                offset = Prefer(lo, hi, tieToward) ? q : q + 1;
            }
            result.Add(Make(xMajor, major, minorStart + offset * minorSign));
        }
        return result;
    }

    private static BlockXZ Make(bool xMajor, int major, int minor) => xMajor ? new BlockXZ(major, minor) : new BlockXZ(minor, major);

    /// <summary>True when <paramref name="p"/> should win the tie against <paramref name="q"/>.</summary>
    private static bool Prefer(BlockXZ p, BlockXZ q, ShapeCenter c)
    {
        double dp = Sq(p.X - c.Dx) + Sq(p.Z - c.Dz);
        double dq = Sq(q.X - c.Dx) + Sq(q.Z - c.Dz);
        if (Math.Abs(dp - dq) > 1e-9) return dp < dq;
        return p.CompareTo(q) < 0;
    }

    private static double Sq(double v) => v * v;
}
