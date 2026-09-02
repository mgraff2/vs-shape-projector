namespace ShapeProjector.Geometry.Tests;

public class DrapeTests
{
    static readonly BlockXZ[] Columns = [new(0, 5), new(3, 4), new(5, 0), new(-2, -7)];

    [Fact]
    public void Fixed_y_passes_the_fixed_height_through_and_never_samples_terrain()
    {
        int calls = 0;
        var result = DrapeResolver.Resolve(Columns, fixedY: 70, VerticalMode.FixedY, drapeOffset: 3, (x, z) => { calls++; return 60; });
        Assert.Equal(0, calls);
        Assert.Equal(Columns.Select(c => new BlockXYZ(c.X, 70, c.Z)), result);
    }

    [Fact]
    public void Drape_puts_each_column_one_above_its_surface()
    {
        // synthetic hill: the surface rises one block per block of x
        var result = DrapeResolver.Resolve(Columns, fixedY: 70, VerticalMode.Drape, drapeOffset: 0, (x, z) => 60 + x);
        Assert.Equal(Columns.Select(c => new BlockXYZ(c.X, 61 + c.X, c.Z)), result);
    }

    [Fact]
    public void Drape_offset_is_added_on_top_of_surface_plus_one()
    {
        var result = DrapeResolver.Resolve(Columns, fixedY: 70, VerticalMode.Drape, drapeOffset: 2, (x, z) => 60 + x);
        Assert.Equal(Columns.Select(c => new BlockXYZ(c.X, 63 + c.X, c.Z)), result);
        var down = DrapeResolver.Resolve(Columns, fixedY: 70, VerticalMode.Drape, drapeOffset: -1, (x, z) => 60 + x);
        Assert.Equal(Columns.Select(c => new BlockXYZ(c.X, 60 + c.X, c.Z)), down);
    }

    [Fact]
    public void Unloaded_columns_fall_back_to_the_fixed_y_without_offset()
    {
        var result = DrapeResolver.Resolve(Columns, fixedY: 70, VerticalMode.Drape, drapeOffset: 2, (x, z) => x == 3 ? null : 60);
        BlockXYZ[] expected = [new(0, 63, 5), new(3, 70, 4), new(5, 63, 0), new(-2, 63, -7)];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Single_column_resolution_agrees_with_the_batch()
    {
        Assert.Equal(new BlockXYZ(3, 64, 4), DrapeResolver.ResolveColumn(new BlockXZ(3, 4), 70, VerticalMode.Drape, 1, (x, z) => 62));
        Assert.Equal(new BlockXYZ(3, 70, 4), DrapeResolver.ResolveColumn(new BlockXZ(3, 4), 70, VerticalMode.Drape, 1, (x, z) => null));
        Assert.Equal(new BlockXYZ(3, 70, 4), DrapeResolver.ResolveColumn(new BlockXZ(3, 4), 70, VerticalMode.FixedY, 1, (x, z) => 62));
    }

    [Fact]
    public void Affected_columns_is_the_changed_column_itself_when_it_is_on_the_outline()
    {
        var outline = Circle.Rasterize(Shape.Block, 5);
        Assert.Equal([new BlockXZ(5, 0)], DrapeResolver.AffectedColumns(5, 0, outline));
        Assert.Empty(DrapeResolver.AffectedColumns(0, 0, outline));
        Assert.Empty(DrapeResolver.AffectedColumns(4, 0, outline)); // neighbour of an outline column: not affected
    }
}
