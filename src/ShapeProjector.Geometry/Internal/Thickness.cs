namespace ShapeProjector.Geometry.Internal;

/// <summary>
/// Thickens closed outlines <em>inward</em>: the 1-thick outline is layer 0; layer k is the set of
/// interior blocks that have a 4-neighbour in layer k-1 or outside the shape. The outer extent of the
/// shape therefore stays exactly at the configured parameter; extra thickness eats into the interior.
/// </summary>
internal static class Thickness
{
    public static HashSet<BlockXZ> ThickenClosed(HashSet<BlockXZ> outline, int thickness)
    {
        Lattice.RequireThickness(thickness);
        if (thickness == 1 || outline.Count == 0) return outline;

        var remaining = Interior(outline);
        var result = new HashSet<BlockXZ>(outline);
        for (int layer = 1; layer < thickness && remaining.Count > 0; layer++)
        {
            var peel = remaining.Where(p => p.Neighbours4().Any(n => !remaining.Contains(n))).ToHashSet();
            result.UnionWith(peel);
            remaining.ExceptWith(peel);
        }
        return result;
    }

    /// <summary>
    /// Blocks strictly inside a closed 8-connected outline: everything in the outline's bounding box
    /// that a 4-connected flood from outside the box cannot reach. An 8-connected ring is impassable
    /// to 4-moves, so no leak is possible through diagonal steps.
    /// </summary>
    public static HashSet<BlockXZ> Interior(HashSet<BlockXZ> outline)
    {
        if (outline.Count == 0) return new HashSet<BlockXZ>();
        int x0 = outline.Min(p => p.X) - 1, x1 = outline.Max(p => p.X) + 1;
        int z0 = outline.Min(p => p.Z) - 1, z1 = outline.Max(p => p.Z) + 1;
        var outside = new HashSet<BlockXZ>();
        var stack = new Stack<BlockXZ>();
        var start = new BlockXZ(x0, z0);
        outside.Add(start);
        stack.Push(start);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var n in p.Neighbours4())
            {
                if (n.X < x0 || n.X > x1 || n.Z < z0 || n.Z > z1) continue;
                if (outline.Contains(n) || !outside.Add(n)) continue;
                stack.Push(n);
            }
        }
        var interior = new HashSet<BlockXZ>();
        for (int z = z0 + 1; z < z1; z++)
            for (int x = x0 + 1; x < x1; x++)
            {
                var p = new BlockXZ(x, z);
                if (!outline.Contains(p) && !outside.Contains(p)) interior.Add(p);
            }
        return interior;
    }
}
