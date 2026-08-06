using Koan.Data.Abstractions;
using Koan.Data.Core.Mapping.Composition;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Mapping.Runtime;

internal sealed class DataMappingPlans : IDataMappingPlans
{
    private readonly object _gate = new();
    private readonly MappingDeclarationCatalog _declarations;
    private readonly int _limit;
    private readonly Dictionary<(string Source, Type Entity), MappingPlan> _plans = new();

    public DataMappingPlans(MappingDeclarationCatalog declarations, IOptions<MappingOptions> options)
    {
        _declarations = declarations;
        _limit = options.Value.PlanEntries;
        if (_limit <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Mapping PlanEntries must be positive.");
    }

    public MappingPlan? Find(string source, Type entityType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entityType);
        return _declarations.TryGet(source, entityType, out _)
            ? Require(source, entityType)
            : null;
    }

    public MappingPlan? Find<TEntity>(string source) => Find(source, typeof(TEntity));

    public MappingPlan Require(string source, Type entityType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entityType);
        var key = (source.Trim().ToUpperInvariant(), entityType);
        lock (_gate)
        {
            if (_plans.TryGetValue(key, out var existing)) return existing;
            if (!_declarations.TryGet(source, entityType, out var descriptor))
                throw new MappingCompilationException(source, entityType, "Declare Map<TEntity>(...) once for this source.");
            if (_plans.Count >= _limit)
                throw new MappingCompilationException(source, entityType,
                    $"This host reached its bounded mapping-plan limit of {_limit}. Increase Koan:Data:Mapping:PlanEntries deliberately.");
            var plan = MappingPlanCompiler.Compile(descriptor);
            _plans.Add(key, plan);
            return plan;
        }
    }

    public MappingPlan Require<TEntity>(string source) => Require(source, typeof(TEntity));

    public MappingPlan GetOrAdd(string source, Type entityType, MappingConvention convention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(convention);
        var key = (source.Trim().ToUpperInvariant(), entityType);
        lock (_gate)
        {
            if (_plans.TryGetValue(key, out var existing)) return existing;
            MappingDescriptor descriptor;
            if (!_declarations.TryGet(source, entityType, out descriptor!))
                descriptor = MappingPlanCompiler.Convention(source, entityType, convention);
            if (_plans.Count >= _limit)
                throw new MappingCompilationException(source, entityType,
                    $"This host reached its bounded mapping-plan limit of {_limit}. Increase Koan:Data:Mapping:PlanEntries deliberately.");
            var plan = MappingPlanCompiler.Compile(descriptor);
            _plans.Add(key, plan);
            return plan;
        }
    }

    public MappingPlan GetOrAdd<TEntity>(string source, MappingConvention convention) =>
        GetOrAdd(source, typeof(TEntity), convention);

    public IReadOnlyList<MappingPlan> Snapshot()
    {
        foreach (var descriptor in _declarations.Snapshot()) Require(descriptor.Source, descriptor.EntityType);
        lock (_gate)
            return _plans.Values.OrderBy(static plan => plan.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plan => plan.EntityType.FullName, StringComparer.Ordinal)
                .ToArray();
    }
}
