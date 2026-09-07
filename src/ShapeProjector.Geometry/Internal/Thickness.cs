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

        // Layer k is the set of interior blocks at 4-connected distance k from the interior's
        // complement (the outline or the outside), so the layers are one multi-source breadth-first
        // pass from every interior block that touches that complement, taking distances 1 .. thickness-1.
        // Everything runs on dense arrays over the outline's bounding box (2026-09-07: the hash-set
        // version of this pass, itself replacing a peel-per-ring loop, still took ~0.6 s at radius 256;
        // arrays make it a few tens of ms).
        var box = Box.Of(outline);
        bool[] solid = box.Mask(outline);            // outline cells
        bool[] inside = box.InteriorMask(solid);     // strictly inside
        var result = new HashSet<BlockXZ>(outline);

        int n = box.W * box.H;
        int[] dist = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (!inside[i]) continue;
            int x = i % box.W, z = i / box.W;
            if ((x > 0 && !inside[i - 1]) || (x + 1 < box.W && !inside[i + 1]) || (z > 0 && !inside[i - box.W]) || (z + 1 < box.H && !inside[i + box.W])
                || x == 0 || x + 1 == box.W || z == 0 || z + 1 == box.H)
            {
                dist[i] = 1;
                queue.Enqueue(i);
            }
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int d = dist[i];
            if (d > thickness - 1) continue;   // deeper rings are not wanted; nothing beyond them is either
            result.Add(box.At(i));
            int x = i % box.W, z = i / box.W;
            Push(i - 1, x > 0); Push(i + 1, x + 1 < box.W); Push(i - box.W, z > 0); Push(i + box.W, z + 1 < box.H);
            void Push(int j, bool ok)
            {
                if (!ok || !inside[j] || dist[j] != 0) return;
                dist[j] = d + 1;
                queue.Enqueue(j);
            }
        }
        return result;
    }

    /// <summary>Dense bounding-box frame for the array passes: cell (x,z) ↔ index (z - Z0) * W + (x - X0), one cell of margin all round.</summary>
    private readonly record struct Box(int X0, int Z0, int W, int H)
    {
        public static Box Of(HashSet<BlockXZ> cells)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            foreach (var p in cells)
            {
                if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                if (p.Z < z0) z0 = p.Z; if (p.Z > z1) z1 = p.Z;
            }
            return new Box(x0 - 1, z0 - 1, x1 - x0 + 3, z1 - z0 + 3);
        }
        public int Index(BlockXZ p) => (p.Z - Z0) * W + (p.X - X0);
        public BlockXZ At(int i) => new BlockXZ(X0 + i % W, Z0 + i / W);
        public bool[] Mask(HashSet<BlockXZ> cells)
        {
            var m = new bool[W * H];
            foreach (var p in cells) m[Index(p)] = true;
            return m;
        }
        /// <summary>Cells that a 4-connected flood from the frame's corner cannot reach and that are not outline: the strict interior.</summary>
        public bool[] InteriorMask(bool[] outline)
        {
            int n = W * H;
            var outside = new bool[n];
            var stack = new Stack<int>();
            outside[0] = true;
            stack.Push(0);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % W, z = i / W;
                Visit(i - 1, x > 0); Visit(i + 1, x + 1 < W); Visit(i - W, z > 0); Visit(i + W, z + 1 < H);
                void Visit(int j, bool ok)
                {
                    if (!ok || outline[j] || outside[j]) return;
                    outside[j] = true;
                    stack.Push(j);
                }
            }
            var inside = new bool[n];
            for (int i = 0; i < n; i++) inside[i] = !outline[i] && !outside[i];
            return inside;
        }
    }

    /// <summary>
    /// Blocks strictly inside a closed 8-connected outline: everything in the outline's bounding box
    /// that a 4-connected flood from outside the box cannot reach. An 8-connected ring is impassable
    /// to 4-moves, so no leak is possible through diagonal steps.
    /// </summary>
    public static HashSet<BlockXZ> Interior(HashSet<BlockXZ> outline)
    {
        if (outline.Count == 0) return new HashSet<BlockXZ>();
        var box = Box.Of(outline);
        bool[] inside = box.InteriorMask(box.Mask(outline));
        var interior = new HashSet<BlockXZ>();
        for (int i = 0; i < inside.Length; i++) if (inside[i]) interior.Add(box.At(i));
        return interior;
    }
}
