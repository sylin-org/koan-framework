using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Declares one immutable vector space for an Entity on one source.</summary>
public sealed class VectorSpaceBuilder<TEntity>
    where TEntity : class, IEntity<string>
{
    private string? _name;
    private int? _dimensions;
    private VectorMetric _metric = VectorMetric.Cosine;
    private VectorVisibility _visibility = VectorVisibility.Session;
    private string? _model;

    public VectorSpaceBuilder<TEntity> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name.Trim();
        return this;
    }

    public VectorSpaceBuilder<TEntity> Dimensions(int dimensions)
    {
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        _dimensions = dimensions;
        return this;
    }

    public VectorSpaceBuilder<TEntity> Metric(VectorMetric metric)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        _metric = metric;
        return this;
    }

    public VectorSpaceBuilder<TEntity> Visibility(VectorVisibility visibility)
    {
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));
        _visibility = visibility;
        return this;
    }

    public VectorSpaceBuilder<TEntity> Model(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _model = model.Trim();
        return this;
    }

    internal VectorSpacePlan Build(string source)
    {
        if (_name is null)
            throw new InvalidOperationException(
                $"Vector space for '{typeof(TEntity).Name}' on source '{source}' requires Name(...).");
        if (_dimensions is null)
            throw new InvalidOperationException(
                $"Vector space '{_name}' for '{typeof(TEntity).Name}' requires Dimensions(...).");
        return new VectorSpacePlan(source, _name, _dimensions.Value, _metric, _visibility, _model);
    }
}
