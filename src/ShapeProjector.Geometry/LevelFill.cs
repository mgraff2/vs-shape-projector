namespace ShapeProjector.Geometry;

/// <summary>
/// "Fill up to level" arithmetic (user request 2026-09-07) as pure functions over already-resolved
/// column bases, in the same relative-Y frame as <see cref="DrapeResolver"/>.
/// <para>
/// A filled layer marks, in every outline column, the blocks from that column's ground up to a
/// common level, and then the figure's own <c>height</c> courses on top of the level. The column's
/// base is what the caller resolved for it (surface + 1, plus the drape offset when draping), or
/// <see langword="null"/> for a column whose chunk is not loaded. The level is the caller's
/// business too: the layer's fixed Y, or — Follow terrain — <see cref="HighestBase"/>, so the whole
/// figure levels up to its highest point. This library never inspects blocks.
/// </para>
/// </summary>
public static class LevelFill
{
    /// <summary>The highest resolved base among the loaded columns; null when no column is loaded.</summary>
    public static int? HighestBase(IReadOnlyList<int?> bases)
    {
        ArgumentNullException.ThrowIfNull(bases);
        int? best = null;
        foreach (int? b in bases)
        {
            if (b.HasValue && (!best.HasValue || b.Value > best.Value)) best = b.Value;
        }
        return best;
    }

    /// <summary>
    /// The cells of one column: from <c>min(base, level)</c> up through <c>level + height - 1</c>,
    /// as (first Y, count). A column whose ground is already at or above the level contributes
    /// only the figure's own <c>height</c> courses; an unloaded column (null base) likewise.
    /// </summary>
    public static (int Start, int Count) ColumnRun(int? baseY, int level, int height)
    {
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), "height must be >= 1");
        int start = baseY.HasValue && baseY.Value < level ? baseY.Value : level;
        return (start, level + height - start);
    }

    /// <summary>Cells over the first <paramref name="columns"/> columns at the given height.</summary>
    public static long TotalCells(IReadOnlyList<int?> bases, int level, int height, int columns)
    {
        ArgumentNullException.ThrowIfNull(bases);
        if (columns < 0 || columns > bases.Count) throw new ArgumentOutOfRangeException(nameof(columns));
        long total = 0;
        for (int i = 0; i < columns; i++) total += ColumnRun(bases[i], level, height).Count;
        return total;
    }

    /// <summary>
    /// Fits a filled layer into a cell budget the same way the block entity fits a plain one:
    /// height is surrendered first (down to 1 — a shorter figure still reads as the figure), then
    /// columns are truncated as a prefix, as a last resort. Returns the height and column count to
    /// draw; (height, bases.Count) when everything fits, (1, 0) when nothing does.
    /// </summary>
    public static (int Height, int Columns) Fit(IReadOnlyList<int?> bases, int level, int height, int room)
    {
        ArgumentNullException.ThrowIfNull(bases);
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), "height must be >= 1");
        if (room < 0) throw new ArgumentOutOfRangeException(nameof(room), "room must be >= 0");

        int columns = bases.Count;
        if (TotalCells(bases, level, height, columns) <= room) return (height, columns);

        // The fill part is the same at every height; only the courses on top shrink.
        long fillOnly = TotalCells(bases, level, 1, columns) - columns;
        long extraPerCourse = columns;   // each additional course costs one cell per column
        if (extraPerCourse > 0)
        {
            long fitHeight = (room - fillOnly) / extraPerCourse;   // may be < 1
            if (fitHeight >= 1) return ((int)Math.Min(fitHeight, height), columns);
        }

        // Height 1 still does not fit: keep the longest prefix of columns that does.
        int keep = 0;
        long used = 0;
        for (int i = 0; i < columns; i++)
        {
            int count = ColumnRun(bases[i], level, 1).Count;
            if (used + count > room) break;
            used += count;
            keep = i + 1;
        }
        return (1, keep);
    }
}
