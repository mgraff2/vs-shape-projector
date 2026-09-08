using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>
    /// One merged, exposed face rectangle of a cell set. <see cref="Face"/> is the BlockFacing index
    /// (NORTH 0 = -Z, EAST 1 = +X, SOUTH 2 = +Z, WEST 3 = -X, UP 4 = +Y, DOWN 5 = -Y — BlockFacing.cs);
    /// <see cref="Slice"/> is the cell coordinate along the face normal (the face lies on that cell's
    /// far side for +X/+Y/+Z, its near side for the others); U/V are half-open cell ranges in the
    /// two in-plane axes: UP/DOWN u = x, v = z; EAST/WEST u = z, v = y; NORTH/SOUTH u = x, v = y.
    /// </summary>
    public readonly record struct FaceQuad(int Face, int Slice, int U0, int V0, int U1, int V1, int Color);

    /// <summary>
    /// Greedy face merging for ghost cells (user request 2026-09-07: "full detail even with up to
    /// 256 radius and 256 thickness without a huge hit"). A cell set of N unit cubes drawn one cube
    /// at a time costs 6N quads and the faces between neighbouring cubes are never seen; this emits
    /// only faces whose neighbour is absent or a different colour, and merges coplanar same-colour
    /// faces into rectangles (per face direction, per slice: run along u, then extend across v while
    /// every row matches). A radius-256 solid disc — 206,000 cells, 1.2 million cube quads — becomes
    /// a few hundred rectangles. Grid lines along the cell boundaries inside each rectangle keep the
    /// block-by-block reading the inset cubes used to give.
    /// <para>
    /// Pure over the cell list: no world, no GL. Consumers (ProjectorRenderer, HologramRenderer) turn
    /// the rectangles into vertices in their own spaces via <see cref="Corners"/>.
    /// </para>
    /// </summary>
    public static class GhostMesher
    {
        /// <summary>Neighbour offset and the face it exposes, by BlockFacing index.</summary>
        private static readonly (int dx, int dy, int dz)[] Normal =
        {
            (0, 0, -1),   // NORTH
            (1, 0, 0),    // EAST
            (0, 0, 1),    // SOUTH
            (-1, 0, 0),   // WEST
            (0, 1, 0),    // UP
            (0, -1, 0),   // DOWN
        };

        private const int Bias = 1 << 20;   // coordinates are block-local and |c| < 2^20 by a wide margin

        private static long Key(int x, int y, int z) => ((long)(x + Bias) << 42) | ((long)(y + Bias) << 21) | (long)(z + Bias);

        /// <summary>(u, v) of a cell in the plane of a face, and the slice coordinate along its normal.</summary>
        private static (int u, int v, int slice) Project(int face, int x, int y, int z) => face switch
        {
            4 or 5 => (x, z, y),
            1 or 3 => (z, y, x),
            _ => (x, y, z),
        };

        /// <summary>
        /// Merges the exposed faces of <paramref name="cells"/>. A face is exposed when the neighbour
        /// across it is absent or carries a different colour (a done-tinted cell beside a plain one keeps
        /// both faces, so the tint boundary stays exact).
        /// </summary>
        public static List<FaceQuad> Merge(IReadOnlyList<GhostCell> cells)
        {
            var quads = new List<FaceQuad>();
            if (cells.Count == 0) return quads;

            var colour = new Dictionary<long, int>(cells.Count);
            foreach (GhostCell c in cells) colour[Key(c.X, c.Y, c.Z)] = c.Color;

            // Exposed faces grouped per (face, slice); each entry is (u, v, colour).
            var groups = new Dictionary<(int face, int slice), List<(int u, int v, int color)>>();
            foreach (GhostCell c in cells)
            {
                for (int f = 0; f < 6; f++)
                {
                    var (dx, dy, dz) = Normal[f];
                    if (colour.TryGetValue(Key(c.X + dx, c.Y + dy, c.Z + dz), out int other) && other == c.Color) continue;
                    var (u, v, slice) = Project(f, c.X, c.Y, c.Z);
                    if (!groups.TryGetValue((f, slice), out var list))
                    {
                        list = new List<(int, int, int)>();
                        groups[(f, slice)] = list;
                    }
                    list.Add((u, v, c.Color));
                }
            }

            int[] grid = Array.Empty<int>();
            foreach (var kv in groups)
            {
                var (face, slice) = kv.Key;
                List<(int u, int v, int color)> faces = kv.Value;

                int u0 = int.MaxValue, v0 = int.MaxValue, u1 = int.MinValue, v1 = int.MinValue;
                foreach (var fc in faces)
                {
                    if (fc.u < u0) u0 = fc.u;
                    if (fc.v < v0) v0 = fc.v;
                    if (fc.u > u1) u1 = fc.u;
                    if (fc.v > v1) v1 = fc.v;
                }
                int w = u1 - u0 + 1, h = v1 - v0 + 1;
                if (grid.Length < w * h) grid = new int[w * h];
                foreach (var fc in faces) grid[(fc.v - v0) * w + (fc.u - u0)] = fc.color;   // colours are never 0: alpha >= 13

                // Greedy: for each unconsumed cell, run right while the colour matches, then extend
                // down while the whole row matches; consumed cells are zeroed (grid doubles as the
                // "used" mask and comes back clean for the next group).
                for (int v = 0; v < h; v++)
                {
                    for (int u = 0; u < w; u++)
                    {
                        int col = grid[v * w + u];
                        if (col == 0) continue;
                        int run = 1;
                        while (u + run < w && grid[v * w + u + run] == col) run++;
                        int rows = 1;
                        while (v + rows < h)
                        {
                            bool match = true;
                            int row = (v + rows) * w;
                            for (int i = 0; i < run; i++)
                            {
                                if (grid[row + u + i] != col) { match = false; break; }
                            }
                            if (!match) break;
                            rows++;
                        }
                        for (int r = 0; r < rows; r++)
                        {
                            int row = (v + r) * w;
                            for (int i = 0; i < run; i++) grid[row + u + i] = 0;
                        }
                        quads.Add(new FaceQuad(face, slice, u0 + u, v0 + v, u0 + u + run, v0 + v + rows, col));
                    }
                }
            }
            return quads;
        }

        /// <summary>
        /// The four corners of a rectangle in block-local coordinates, in the engine's vertex order for
        /// that face (CubeMeshUtil.CubeVertices, face rows of 4 — the same order ModelCubeUtilExt.AddFaceSkipTex
        /// walks), so an index pattern of 0,1,2 / 0,2,3 makes two triangles. <paramref name="inset"/>
        /// pulls the plane inward along the normal (the inset cubes used 0.05: a ghost face coplanar with
        /// a real block face would z-fight); in-plane extents stay flush so rectangles that share a seam
        /// join without a gap.
        /// </summary>
        public static void Corners(in FaceQuad q, float inset, Span<float> xyz)
        {
            float a0 = q.U0, a1 = q.U1, b0 = q.V0, b1 = q.V1;
            switch (q.Face)
            {
                case 0:   // NORTH, z = slice (near side), (u,v) = (x,y): (-,-) (-,+) (+,+) (+,-)
                {
                    float p = q.Slice + inset;
                    Set(xyz, 0, a0, b0, p); Set(xyz, 1, a0, b1, p); Set(xyz, 2, a1, b1, p); Set(xyz, 3, a1, b0, p);
                    break;
                }
                case 1:   // EAST, x = slice + 1, (u,v) = (z,y): (y-,z-) (y+,z-) (y+,z+) (y-,z+)
                {
                    float p = q.Slice + 1 - inset;
                    Set(xyz, 0, p, b0, a0); Set(xyz, 1, p, b1, a0); Set(xyz, 2, p, b1, a1); Set(xyz, 3, p, b0, a1);
                    break;
                }
                case 2:   // SOUTH, z = slice + 1, (u,v) = (x,y): (x+,y-) (x+,y+) (x-,y+) (x-,y-)
                {
                    float p = q.Slice + 1 - inset;
                    Set(xyz, 0, a1, b0, p); Set(xyz, 1, a1, b1, p); Set(xyz, 2, a0, b1, p); Set(xyz, 3, a0, b0, p);
                    break;
                }
                case 3:   // WEST, x = slice, (u,v) = (z,y): (z+,y-) (z+,y+) (z-,y+) (z-,y-)
                {
                    float p = q.Slice + inset;
                    Set(xyz, 0, p, b0, a1); Set(xyz, 1, p, b1, a1); Set(xyz, 2, p, b1, a0); Set(xyz, 3, p, b0, a0);
                    break;
                }
                case 4:   // UP, y = slice + 1, (u,v) = (x,z): (x-,z-) (x-,z+) (x+,z+) (x+,z-)
                {
                    float p = q.Slice + 1 - inset;
                    Set(xyz, 0, a0, p, b0); Set(xyz, 1, a0, p, b1); Set(xyz, 2, a1, p, b1); Set(xyz, 3, a1, p, b0);
                    break;
                }
                default:  // DOWN, y = slice, (u,v) = (x,z): (x-,z+) (x-,z-) (x+,z-) (x+,z+)
                {
                    float p = q.Slice + inset;
                    Set(xyz, 0, a0, p, b1); Set(xyz, 1, a0, p, b0); Set(xyz, 2, a1, p, b0); Set(xyz, 3, a1, p, b1);
                    break;
                }
            }
        }

        private static void Set(Span<float> xyz, int i, float x, float y, float z)
        {
            xyz[i * 3] = x; xyz[i * 3 + 1] = y; xyz[i * 3 + 2] = z;
        }

        /// <summary>
        /// Block-local segments of the cell-boundary grid inside a rectangle plus its outline, on the
        /// same (inset) plane: with the inset cubes gone, these lines are what still lets a player count
        /// blocks along a mark. Calls <paramref name="segment"/> with (x0,y0,z0,x1,y1,z1) per line.
        /// </summary>
        public static void GridLines(in FaceQuad q, float inset, Action<float, float, float, float, float, float> segment)
        {
            // Lines at every integer u in [U0, U1] spanning V0..V1, and every integer v in [V0, V1]
            // spanning U0..U1 — the outline is the first/last of each.
            for (int u = q.U0; u <= q.U1; u++) Line(q, inset, u, q.V0, u, q.V1, segment);
            for (int v = q.V0; v <= q.V1; v++) Line(q, inset, q.U0, v, q.U1, v, segment);
        }

        /// <summary>
        /// Only the rectangle's outline (4 segments), for figures too big for a per-cell grid
        /// (2026-09-08: at 250,000 marks the grid is ~500,000 segments; packed into the miniature that
        /// is thousands of line fragments per pixel, a GPU stall that changes with the view angle).
        /// </summary>
        public static void OutlineLines(in FaceQuad q, float inset, Action<float, float, float, float, float, float> segment)
        {
            Line(q, inset, q.U0, q.V0, q.U1, q.V0, segment);
            Line(q, inset, q.U0, q.V1, q.U1, q.V1, segment);
            Line(q, inset, q.U0, q.V0, q.U0, q.V1, segment);
            Line(q, inset, q.U1, q.V0, q.U1, q.V1, segment);
        }

        private static void Line(in FaceQuad q, float inset, float ua, float va, float ub, float vb, Action<float, float, float, float, float, float> segment)
        {
            switch (q.Face)
            {
                case 0: { float p = q.Slice + inset; segment(ua, va, p, ub, vb, p); break; }
                case 2: { float p = q.Slice + 1 - inset; segment(ua, va, p, ub, vb, p); break; }
                case 1: { float p = q.Slice + 1 - inset; segment(p, va, ua, p, vb, ub); break; }
                case 3: { float p = q.Slice + inset; segment(p, va, ua, p, vb, ub); break; }
                case 4: { float p = q.Slice + 1 - inset; segment(ua, p, va, ub, p, vb); break; }
                default: { float p = q.Slice + inset; segment(ua, p, va, ub, p, vb); break; }
            }
        }

        /// <summary>Per-face brightness the engine applies to a cube's sides (CubeMeshUtil.DefaultBlockSideShadingsByFacing).</summary>
        public static float Shading(int face) => CubeMeshUtil.DefaultBlockSideShadingsByFacing[face];
    }
}
