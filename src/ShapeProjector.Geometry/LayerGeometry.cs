namespace ShapeProjector.Geometry;

/// <summary>Everything that determines a layer's horizontal block set. Value equality drives cache invalidation.</summary>
public sealed record LayerParameters(ShapeSpec Shape, ShapeCenter Center, int MaxRadius = 128, int OutlineThickness = 1);

/// <summary>
/// The cached position set of one layer (spec section 5: "recomputed only on parameter change").
/// Hold one per layer, call <see cref="Update"/> whenever synced parameters arrive, read <see cref="Positions"/>
/// for rendering. Not thread-safe; intended to live on the client render side.
/// </summary>
public sealed class LayerGeometry
{
    private static readonly IReadOnlySet<BlockXZ> EmptySet = new HashSet<BlockXZ>();

    /// <summary>The parameters the current positions were computed from; null before the first update.</summary>
    public LayerParameters? Parameters { get; private set; }

    /// <summary>Horizontal positions relative to the projector, strictly ascending by Z then X, no duplicates.</summary>
    public IReadOnlyList<BlockXZ> Positions { get; private set; } = Array.Empty<BlockXZ>();

    /// <summary>The same positions as a set, for membership tests (build feedback, affected-column queries).</summary>
    public IReadOnlySet<BlockXZ> PositionSet { get; private set; } = EmptySet;

    /// <summary>Validation message when the last parameters were rejected (positions are then empty); null otherwise.</summary>
    public string? Error { get; private set; }

    /// <summary>How many times the set has been rasterized; diagnostics and tests.</summary>
    public int RecomputeCount { get; private set; }

    /// <summary>
    /// Adopts <paramref name="parameters"/>. Returns false (and does nothing) when they equal the current ones by
    /// value; otherwise rasterizes and returns true. Invalid parameters never throw: they set <see cref="Error"/>
    /// and clear the positions.
    /// </summary>
    public bool Update(LayerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Equals(Parameters)) return false;

        Parameters = parameters;
        RecomputeCount++;
        try
        {
            var set = parameters.Shape.Rasterize(parameters.Center, parameters.MaxRadius, parameters.OutlineThickness);
            var sorted = set.ToArray();
            Array.Sort(sorted);
            Positions = sorted;
            PositionSet = set;
            Error = null;
        }
        catch (ArgumentException e)
        {
            Positions = Array.Empty<BlockXZ>();
            PositionSet = EmptySet;
            Error = e.Message;
        }
        return true;
    }
}
