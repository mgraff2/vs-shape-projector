namespace ShapeProjector.Geometry.Tests;

public class ShapeCenterTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.5, 0)]
    [InlineData(12.5, -3)]
    [InlineData(-100.5, 64)]
    public void Accepts_half_step_offsets(double dx, double dz)
    {
        var c = new ShapeCenter(dx, dz);
        Assert.Equal(dx, c.Dx);
        Assert.Equal(dz, c.Dz);
    }

    [Theory]
    [InlineData(0.25, 0)]
    [InlineData(0, 0.1)]
    [InlineData(1.0 / 3, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Rejects_non_half_step_offsets(double dx, double dz)
    {
        Assert.Throws<ArgumentException>(() => new ShapeCenter(dx, dz));
    }

    [Fact]
    public void Default_is_origin_and_records_have_value_equality()
    {
        Assert.Equal(new ShapeCenter(0, 0), ShapeCenter.Origin);
        Assert.Equal(new ShapeCenter(0.5, -2), new ShapeCenter(0.5, -2));
        Assert.NotEqual(new ShapeCenter(0.5, -2), new ShapeCenter(0.5, -2.5));
    }
}
