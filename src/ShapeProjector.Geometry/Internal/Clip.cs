namespace ShapeProjector.Geometry.Internal;

internal static class Clip
{
    /// <summary>Removes every block outside the square window |x| &lt;= maxRadius, |z| &lt;= maxRadius around the projector.</summary>
    public static HashSet<BlockXZ> ToWindow(HashSet<BlockXZ> set, int maxRadius)
    {
        set.RemoveWhere(p => !InWindow(p, maxRadius));
        return set;
    }

    public static bool InWindow(BlockXZ p, int maxRadius) =>
        Math.Abs(p.X) <= maxRadius && Math.Abs(p.Z) <= maxRadius;
}
