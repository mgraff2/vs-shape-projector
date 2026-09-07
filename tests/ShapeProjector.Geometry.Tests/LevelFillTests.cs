namespace ShapeProjector.Geometry.Tests;

public class LevelFillTests
{
    [Fact]
    public void Highest_base_ignores_unloaded_columns_and_is_null_when_none_loaded()
    {
        Assert.Equal(3, LevelFill.HighestBase(new int?[] { -5, 3, null, 0 }));
        Assert.Null(LevelFill.HighestBase(new int?[] { null, null }));
        Assert.Null(LevelFill.HighestBase(Array.Empty<int?>()));
    }

    [Fact]
    public void A_pit_column_fills_from_its_floor_to_the_level_plus_the_courses_on_top()
    {
        // The user's example: a pit 6 deep under a 1-high platform at level 1 → ground at -5
        // (surface -6, base = surface + 1), so the column runs -5..1: six blocks of fill plus the
        // platform course itself.
        var (start, count) = LevelFill.ColumnRun(-5, level: 1, height: 1);
        Assert.Equal(-5, start);
        Assert.Equal(7, count);
        Assert.Equal(1, start + count - 1);   // top cell is the level
    }

    [Fact]
    public void Height_stands_on_top_of_the_level_not_inside_the_fill()
    {
        var (start, count) = LevelFill.ColumnRun(-2, level: 0, height: 3);
        Assert.Equal(-2, start);
        Assert.Equal(5, count);   // -2, -1 fill; 0, 1, 2 courses
    }

    [Fact]
    public void Ground_at_or_above_the_level_and_unloaded_columns_get_only_the_courses()
    {
        Assert.Equal((0, 2), LevelFill.ColumnRun(0, level: 0, height: 2));
        Assert.Equal((0, 2), LevelFill.ColumnRun(4, level: 0, height: 2));   // terrain above: nothing to fill
        Assert.Equal((0, 2), LevelFill.ColumnRun(null, level: 0, height: 2));
    }

    [Fact]
    public void Column_run_rejects_a_height_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelFill.ColumnRun(0, 0, 0));
    }

    [Fact]
    public void Total_cells_sums_the_column_runs_over_the_kept_prefix()
    {
        int?[] bases = { -3, 0, null, -1 };
        // level 0, height 1: runs are 4, 1, 1, 2
        Assert.Equal(8, LevelFill.TotalCells(bases, 0, 1, 4));
        Assert.Equal(5, LevelFill.TotalCells(bases, 0, 1, 2));
        Assert.Equal(0, LevelFill.TotalCells(bases, 0, 1, 0));
    }

    [Fact]
    public void Fit_keeps_everything_when_the_budget_allows()
    {
        int?[] bases = { -3, 0, -1 };
        Assert.Equal((2, 3), LevelFill.Fit(bases, level: 0, height: 2, room: 100));
        // Runs at height 2: 5 + 2 + 3 = 10 — a budget of exactly 10 keeps everything.
        Assert.Equal((2, 3), LevelFill.Fit(bases, level: 0, height: 2, room: 10));
    }

    [Fact]
    public void Fit_surrenders_height_before_columns()
    {
        // level 0, height 3, three columns: fill-only cells (excluding the level course) are 3+0+1 = 4;
        // each course costs 3. Height 3 → 4+9 = 13 cells; height 2 → 10; height 1 → 7.
        int?[] bases = { -3, 0, -1 };
        Assert.Equal((3, 3), LevelFill.Fit(bases, 0, 3, 13));
        Assert.Equal((2, 3), LevelFill.Fit(bases, 0, 3, 12));
        Assert.Equal((2, 3), LevelFill.Fit(bases, 0, 3, 10));
        Assert.Equal((1, 3), LevelFill.Fit(bases, 0, 3, 9));
        Assert.Equal((1, 3), LevelFill.Fit(bases, 0, 3, 7));
    }

    [Fact]
    public void Fit_truncates_columns_as_a_prefix_only_when_height_one_still_does_not_fit()
    {
        // height 1 runs: 4, 1, 2 (total 7).
        int?[] bases = { -3, 0, -1 };
        Assert.Equal((1, 2), LevelFill.Fit(bases, 0, 3, 6));   // 4 + 1 fit, the 2-run does not
        Assert.Equal((1, 2), LevelFill.Fit(bases, 0, 3, 5));
        Assert.Equal((1, 1), LevelFill.Fit(bases, 0, 3, 4));
        Assert.Equal((1, 0), LevelFill.Fit(bases, 0, 3, 3));
        Assert.Equal((1, 0), LevelFill.Fit(bases, 0, 3, 0));
    }

    [Fact]
    public void Fit_with_no_columns_is_trivially_whole()
    {
        Assert.Equal((5, 0), LevelFill.Fit(Array.Empty<int?>(), 0, 5, 0));
    }

    [Fact]
    public void Fit_rejects_bad_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelFill.Fit(new int?[] { 0 }, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelFill.Fit(new int?[] { 0 }, 0, 1, -1));
    }
}
