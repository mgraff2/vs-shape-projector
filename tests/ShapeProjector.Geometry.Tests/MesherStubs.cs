// Stubs so GhostMesher.cs (linked from the mod project) compiles inside the Geometry test suite
// without the game assemblies: the cell record the mod declares in ProjectorRenderer.cs and the
// one engine table the mesher reads (CubeMeshUtil.DefaultBlockSideShadingsByFacing, values from
// CubeMeshUtil.cs: N 0.6, E 0.75, S 0.6, W 0.75, UP 1.0, DOWN 0.45).
namespace ShapeProjector
{
    public readonly record struct GhostCell(int X, int Y, int Z, int Color);
}

namespace Vintagestory.API.Client
{
    public static class CubeMeshUtil
    {
        public static float[] DefaultBlockSideShadingsByFacing = { 0.6f, 0.75f, 0.6f, 0.75f, 1f, 0.45f };
    }
}

namespace Vintagestory.API.MathTools
{
    internal static class StubAnchor { }
}
