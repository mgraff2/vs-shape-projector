namespace ShapeProjector.Geometry;

/// <summary>Per-layer vertical placement (spec section 5a).</summary>
public enum VerticalMode
{
    /// <summary>Outline at projector Y + layer Y offset.</summary>
    FixedY,
    /// <summary>Outline follows the terrain: surface height + 1 + optional drape offset per column.</summary>
    Drape,
}

/// <summary>
/// The shape type and its parameters as a value-equal record. This is the unit the block entity syncs
/// and the unit <see cref="LayerGeometry"/> compares to decide whether a recompute is needed.
/// </summary>
public abstract record ShapeSpec
{
    /// <summary>Rasterizes the outline around <paramref name="center"/>. Throws <see cref="ArgumentException"/> on invalid parameters.</summary>
    public abstract IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness);

    /// <summary>Spec section 5a default: Drape for landscape shapes (circle, ring, ellipse, spiral), FixedY for architectural ones.</summary>
    public abstract VerticalMode DefaultVerticalMode { get; }
}

public sealed record CircleSpec(double Radius) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        Circle.Rasterize(center, Radius, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.Drape;
}

public sealed record RingSpec(double InnerRadius, double OuterRadius) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        Ring.Rasterize(center, InnerRadius, OuterRadius, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.Drape;
}

public sealed record EllipseSpec(double RadiusX, double RadiusZ) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        Ellipse.Rasterize(center, RadiusX, RadiusZ, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.Drape;
}

public sealed record RectangleSpec(int Width, int Depth) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        Rectangle.Rasterize(center, Width, Depth, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.FixedY;
}

public sealed record RegularPolygonSpec(int Sides, double Circumradius, double RotationDegrees) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        RegularPolygon.Rasterize(center, Sides, Circumradius, RotationDegrees, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.FixedY;
}

public sealed record SpiralSpec(double Turns, double Spacing, double StartRadius, bool Clockwise) : ShapeSpec
{
    public override IReadOnlySet<BlockXZ> Rasterize(ShapeCenter center, int maxRadius, int outlineThickness) =>
        Spiral.Rasterize(center, Turns, Spacing, StartRadius, Clockwise, maxRadius, outlineThickness);
    public override VerticalMode DefaultVerticalMode => VerticalMode.Drape;
}
