using System.Text;

namespace ShapeProjector.Geometry.Tests;

/// <summary>Reusable property checks and helpers shared by every shape test.</summary>
internal static class Shape
{
    public static readonly ShapeCenter Block = new(0, 0);       // odd diameters
    public static readonly ShapeCenter Corner = new(0.5, 0.5);  // even diameters
    public static readonly ShapeCenter Edge = new(0.5, 0);      // 1x2 centre

    public static IEnumerable<object[]> Centers()
    {
        yield return [Block];
        yield return [Corner];
        yield return [Edge];
    }

    /// <summary>All radii 0.5, 1.0, ..., 64.0 crossed with all three centres.</summary>
    public static IEnumerable<object[]> RadiiAndCenters()
    {
        for (int i = 1; i <= 128; i++)
            foreach (var c in Centers())
                yield return [i * 0.5, c[0]];
    }

    public static HashSet<BlockXZ> Set(params (int x, int z)[] pts) =>
        pts.Select(p => new BlockXZ(p.x, p.z)).ToHashSet();

    /// <summary>
    /// Parses ASCII art where '#' marks a block; <paramref name="originX"/>/<paramref name="originZ"/>
    /// give the block coordinate of the top-left character. Rows are Z ascending downward.
    /// </summary>
    public static HashSet<BlockXZ> FromArt(string art, int originX, int originZ)
    {
        var set = new HashSet<BlockXZ>();
        var lines = art.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToArray();
        for (int row = 0; row < lines.Length; row++)
        {
            var line = lines[row].Trim();
            for (int col = 0; col < line.Length; col++)
                if (line[col] == '#') set.Add(new BlockXZ(originX + col, originZ + row));
        }
        return set;
    }

    public static string ToArt(IEnumerable<BlockXZ> pts)
    {
        var s = pts.ToHashSet();
        if (s.Count == 0) return "(empty)";
        int x0 = s.Min(p => p.X), x1 = s.Max(p => p.X), z0 = s.Min(p => p.Z), z1 = s.Max(p => p.Z);
        var sb = new StringBuilder();
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++) sb.Append(s.Contains(new BlockXZ(x, z)) ? '#' : '.');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static void AssertSameSet(IEnumerable<BlockXZ> expected, IEnumerable<BlockXZ> actual)
    {
        var e = expected.ToHashSet();
        var a = actual.ToHashSet();
        if (!e.SetEquals(a))
            Assert.Fail($"Sets differ.\nExpected:\n{ToArt(e)}\nActual:\n{ToArt(a)}\nMissing: {string.Join(" ", e.Except(a))}\nExtra: {string.Join(" ", a.Except(e))}");
    }

    public static void AssertNoDuplicates(IEnumerable<BlockXZ> pts)
    {
        var list = pts.ToList();
        Assert.Equal(list.Count, list.Distinct().Count());
    }

    /// <summary>Every block is 8-connected to every other block through the set (single component).</summary>
    public static void Assert8Connected(IEnumerable<BlockXZ> pts, string? what = null)
    {
        var set = pts.ToHashSet();
        Assert.NotEmpty(set);
        var seen = new HashSet<BlockXZ> { set.First() };
        var stack = new Stack<BlockXZ>(seen);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var q = new BlockXZ(p.X + dx, p.Z + dz);
                    if (set.Contains(q) && seen.Add(q)) stack.Push(q);
                }
        }
        if (seen.Count != set.Count)
            Assert.Fail($"{what ?? "Set"} is not 8-connected ({seen.Count} of {set.Count} reachable).\n{ToArt(set)}");
    }

    /// <summary>
    /// Every block is 4-connected (edge-sharing only) to every other block through the set.
    /// Strictly stronger than <see cref="Assert8Connected"/>: it forbids the corner-to-corner step.
    /// This is the property builders mean by "the circle does not have diagonal gaps".
    /// </summary>
    public static void Assert4Connected(IEnumerable<BlockXZ> pts, string? what = null)
    {
        var set = pts.ToHashSet();
        Assert.NotEmpty(set);
        var seen = new HashSet<BlockXZ> { set.First() };
        var stack = new Stack<BlockXZ>(seen);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var q in p.Neighbours4())
                if (set.Contains(q) && seen.Add(q)) stack.Push(q);
        }
        if (seen.Count != set.Count)
            Assert.Fail($"{what ?? "Set"} is not 4-connected ({seen.Count} of {set.Count} reachable).\n{ToArt(set)}");
    }

    /// <summary>Width and height of the bounding box, in blocks.</summary>
    public static (int W, int H) BoundingBox(IEnumerable<BlockXZ> pts)
    {
        var s = pts.ToHashSet();
        return (s.Max(p => p.X) - s.Min(p => p.X) + 1, s.Max(p => p.Z) - s.Min(p => p.Z) + 1);
    }

    /// <summary>
    /// A closed ring: connected, and every block has at least two 8-neighbours in the set (no dangling ends).
    /// The only blocks allowed a single neighbour are those on the bounding-box extremes: the tip of a very
    /// thin ellipse (its two sides converge into one column there) and the ends of a one-block-wide shape.
    /// </summary>
    public static void AssertClosedRing(IEnumerable<BlockXZ> pts, string? what = null)
    {
        var set = pts.ToHashSet();
        Assert8Connected(set, what);
        if (set.Count < 3) return; // 1x2 / 2x2 degenerate rings
        int x0 = set.Min(p => p.X), x1 = set.Max(p => p.X), z0 = set.Min(p => p.Z), z1 = set.Max(p => p.Z);
        foreach (var p in set)
        {
            int n = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if ((dx != 0 || dz != 0) && set.Contains(new BlockXZ(p.X + dx, p.Z + dz))) n++;
            bool extreme = p.X == x0 || p.X == x1 || p.Z == z0 || p.Z == z1;
            if (n < 2 && !(n == 1 && extreme)) Assert.Fail($"{what ?? "Ring"} has a dangling block at {p}.\n{ToArt(set)}");
        }
    }

    /// <summary>Reflect block (x,z) about the centre in X: u -> -u where u = x - dx.</summary>
    public static BlockXZ MirrorX(BlockXZ p, ShapeCenter c) => new((int)Math.Round(2 * c.Dx - p.X), p.Z);
    public static BlockXZ MirrorZ(BlockXZ p, ShapeCenter c) => new(p.X, (int)Math.Round(2 * c.Dz - p.Z));
    /// <summary>Swap the axes about the centre: (u,v) -> (v,u). Only a lattice symmetry when frac(dx) == frac(dz).</summary>
    public static BlockXZ SwapUV(BlockXZ p, ShapeCenter c)
    {
        double u = p.X - c.Dx, v = p.Z - c.Dz;
        return new((int)Math.Round(c.Dx + v), (int)Math.Round(c.Dz + u));
    }

    /// <summary>
    /// The lattice symmetry group of the centre: both mirrors always; the diagonal swap
    /// (hence the full 8-way group) only when dx and dz have the same fractional part.
    /// </summary>
    public static void AssertSymmetric(IEnumerable<BlockXZ> pts, ShapeCenter c, bool expectDiagonal, string? what = null)
    {
        var set = pts.ToHashSet();
        AssertInvariant(set, p => MirrorX(p, c), "mirror X", what);
        AssertInvariant(set, p => MirrorZ(p, c), "mirror Z", what);
        if (expectDiagonal) AssertInvariant(set, p => SwapUV(p, c), "diagonal swap", what);
    }

    public static void AssertInvariant(HashSet<BlockXZ> set, Func<BlockXZ, BlockXZ> map, string mapName, string? what = null)
    {
        var image = set.Select(map).ToHashSet();
        if (!image.SetEquals(set))
            Assert.Fail($"{what ?? "Set"} not invariant under {mapName}.\n{ToArt(set)}\nimage:\n{ToArt(image)}");
    }

    public static bool SameFraction(ShapeCenter c) => Math.Abs((c.Dx - Math.Floor(c.Dx)) - (c.Dz - Math.Floor(c.Dz))) < 1e-9;

    public static double Distance(BlockXZ p, ShapeCenter c)
    {
        double u = p.X - c.Dx, v = p.Z - c.Dz;
        return Math.Sqrt(u * u + v * v);
    }
}
