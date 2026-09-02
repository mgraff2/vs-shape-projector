namespace ShapeProjector.Geometry;

/// <summary>
/// Vertical placement of outline columns (spec section 5a) as a pure function over a height-lookup callback.
/// <para>
/// The callback contract: <c>surfaceHeightAt(x, z)</c> receives a column relative to the projector and returns
/// the Y (relative to the projector, same frame as <c>fixedY</c>) of the topmost block that counts as "surface"
/// in that column, or <see langword="null"/> when the column is not loaded. What counts as surface is entirely
/// the caller's decision; in particular the per-layer <c>treatFluidAsSurface</c> rule is implemented inside the
/// callback (on: return the fluid's top block; off: look through fluids and return the bed). This library never
/// inspects blocks.
/// </para>
/// </summary>
public static class DrapeResolver
{
    /// <summary>
    /// Resolves every column to a block position, preserving input order.
    /// FixedY: y = fixedY (the callback is not invoked). Drape: y = surface + 1 + drapeOffset;
    /// an unloaded column (callback returns null) falls back to fixedY, without the offset.
    /// </summary>
    public static IReadOnlyList<BlockXYZ> Resolve(IEnumerable<BlockXZ> columns, int fixedY, VerticalMode mode, int drapeOffset, Func<int, int, int?> surfaceHeightAt)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(surfaceHeightAt);
        var result = columns is ICollection<BlockXZ> col ? new List<BlockXYZ>(col.Count) : new List<BlockXYZ>();
        foreach (var c in columns) result.Add(ResolveColumn(c, fixedY, mode, drapeOffset, surfaceHeightAt));
        return result;
    }

    /// <summary>Resolves a single column; see <see cref="Resolve"/> for the rules.</summary>
    public static BlockXYZ ResolveColumn(BlockXZ column, int fixedY, VerticalMode mode, int drapeOffset, Func<int, int, int?> surfaceHeightAt)
    {
        if (mode == VerticalMode.FixedY) return new BlockXYZ(column.X, fixedY, column.Z);
        int? surface = surfaceHeightAt(column.X, column.Z);
        int y = surface.HasValue ? surface.Value + 1 + drapeOffset : fixedY;
        return new BlockXYZ(column.X, y, column.Z);
    }

    /// <summary>
    /// Which outline columns need their drape Y re-resolved after a block change at column
    /// (<paramref name="changedX"/>, <paramref name="changedZ"/>). A column's surface height depends only on the
    /// blocks in that same column, so the answer is the changed column itself when it is part of the outline,
    /// and nothing otherwise; neighbouring columns are never affected.
    /// </summary>
    public static IReadOnlyList<BlockXZ> AffectedColumns(int changedX, int changedZ, IReadOnlySet<BlockXZ> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var c = new BlockXZ(changedX, changedZ);
        return outline.Contains(c) ? [c] : [];
    }
}
