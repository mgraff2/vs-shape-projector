namespace ShapeProjector.Geometry;

/// <summary>
/// A horizontal block position relative to the projector block, which is (0, 0).
/// X grows east, Z grows south (Vintage Story convention). Block (x, z) occupies the
/// unit square whose centre is the point (x, z) in <em>block-centre coordinates</em>;
/// the whole library measures shape centres and radii in that same coordinate frame.
/// </summary>
public readonly record struct BlockXZ(int X, int Z) : IComparable<BlockXZ>
{
    /// <summary>Row-major ordering (Z first, then X) used for deterministic output arrays.</summary>
    public int CompareTo(BlockXZ other)
    {
        int c = Z.CompareTo(other.Z);
        return c != 0 ? c : X.CompareTo(other.X);
    }

    /// <summary>Chebyshev (king-move) distance: 1 means 8-adjacent.</summary>
    public int ChebyshevDistance(BlockXZ other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Z - other.Z));

    /// <summary>True when the two blocks share an edge or a corner (8-connectivity).</summary>
    public bool IsAdjacent8(BlockXZ other) => !Equals(other) && ChebyshevDistance(other) == 1;

    /// <summary>The four edge neighbours: +X, -X, +Z, -Z.</summary>
    public IEnumerable<BlockXZ> Neighbours4()
    {
        yield return new BlockXZ(X + 1, Z);
        yield return new BlockXZ(X - 1, Z);
        yield return new BlockXZ(X, Z + 1);
        yield return new BlockXZ(X, Z - 1);
    }

    public override string ToString() => $"({X},{Z})";
}

/// <summary>A resolved block position (horizontal column plus its Y), relative to the projector block.</summary>
public readonly record struct BlockXYZ(int X, int Y, int Z)
{
    public BlockXZ Column => new(X, Z);
    public override string ToString() => $"({X},{Y},{Z})";
}
