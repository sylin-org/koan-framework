namespace Koan.Data.Vector.Abstractions;

/// <summary>Immutable source-owned vector-space decision passed to one adapter repository.</summary>
public sealed record VectorSpacePlan
{
    public VectorSpacePlan(
        string source,
        string name,
        int dimensions,
        VectorMetric metric,
        VectorVisibility visibility,
        string? model = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));

        Source = source.Trim();
        Name = name.Trim();
        Dimensions = dimensions;
        Metric = metric;
        Visibility = visibility;
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
    }

    public string Source { get; }
    public string Name { get; }
    public int Dimensions { get; }
    public VectorMetric Metric { get; }
    public VectorVisibility Visibility { get; }
    public string? Model { get; }
}
