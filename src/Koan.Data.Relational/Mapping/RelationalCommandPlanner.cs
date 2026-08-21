using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Relational;

/// <summary>Builds complete symbolic relational commands from one compiled mapping plan.</summary>
public sealed class RelationalCommandPlanner
{
    private readonly MappingPlan _mapping;
    private readonly Dictionary<string, RelationalPathBinding> _bindings;

    public RelationalCommandPlanner(MappingPlan mapping)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _bindings = mapping.Bindings.ToDictionary(
            static binding => binding.Id,
            binding => ToRelational(binding, mapping.Identity.Parts.Any(part => part.Id == binding.Id)),
            StringComparer.Ordinal);
    }

    public MappingPlan Mapping => _mapping;

    public RelationalCommandPlan Insert(object entity) => Write(entity, RelationalOperationKind.Insert);

    public RelationalCommandPlan Update(object entity) => Write(entity, RelationalOperationKind.Update);

    public RelationalCommandPlan Delete(object identity)
    {
        var keys = Values(_mapping.WriteIdentity(identity));
        return Plan(RelationalOperationKind.Delete, null, keys, null, null, null, null, null, MappingConsumer.Write, keys);
    }

    public RelationalCommandPlan Get(object identity, params MappingPath[] projection)
    {
        var keys = Values(_mapping.WriteIdentity(identity));
        var read = _mapping.Read(projection).Bindings.Select(Binding).ToArray();
        return Plan(RelationalOperationKind.Get, null, keys, null, read, null, null, null, MappingConsumer.Projection,
            keys.Concat(read.Select(static binding => new RelationalValue(binding, null))));
    }

    public RelationalCommandPlan Query(QueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reads = _mapping.Read().Bindings.Select(Binding).ToArray();
        var filters = query.Filter is null
            ? Array.Empty<RelationalPathBinding>()
            : FilterPaths(query.Filter).SelectMany(path => _mapping.Use(path, MappingConsumer.Filter).Bindings)
                .Select(Binding).DistinctBy(static binding => binding.BindingId, StringComparer.Ordinal).ToArray();
        var orders = query.Sort.SelectMany(sort =>
                _mapping.Use(MappingPath.Of(sort.Path.Members.Select(static member => member.Name).ToArray()), MappingConsumer.Order).Bindings)
            .Select(Binding).DistinctBy(static binding => binding.BindingId, StringComparer.Ordinal).ToArray();
        return Plan(
            RelationalOperationKind.Query,
            null,
            null,
            null,
            reads,
            filters,
            orders,
            query,
            MappingConsumer.Projection,
            reads.Concat(filters).Concat(orders).Select(static binding => new RelationalValue(binding, null)));
    }

    public RelationalCommandPlan Patch(object identity, IReadOnlyDictionary<MappingPath, object?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var keys = Values(_mapping.WriteIdentity(identity));
        var values = changes.Select(pair =>
        {
            var use = _mapping.Use(pair.Key, MappingConsumer.Patch);
            var binding = use.Bindings[0];
            return new RelationalValue(Binding(binding), binding.Encode(pair.Value));
        }).ToArray();
        return Plan(RelationalOperationKind.Patch, values, keys, null, null, null, null, null, MappingConsumer.Patch,
            values.Concat(keys));
    }

    public RelationalCommandPlan ConditionalWrite(object entity, MappingPath logicalPath, object? expected)
    {
        var mapped = _mapping.Write(entity, MappingWriteOperation.ConditionalWrite);
        var all = Values(mapped);
        var keys = IdentityForExistingEntity(entity, all);
        var values = all.Where(static value => !value.Binding.IsIdentity).ToArray();
        var use = _mapping.Use(logicalPath, MappingConsumer.ConditionalWrite);
        var conditionBinding = use.Bindings[0];
        var conditions = new[] { new RelationalValue(Binding(conditionBinding), conditionBinding.Encode(expected)) };
        return Plan(RelationalOperationKind.ConditionalWrite, values, keys, conditions, null, conditions.Select(static value => value.Binding),
            null, null, MappingConsumer.ConditionalWrite, values.Concat(keys).Concat(conditions));
    }

    public object Materialize(IEnumerable<MappedValue> values) => _mapping.Hydrate(values);

    public TEntity Materialize<TEntity>(IEnumerable<MappedValue> values) => _mapping.Hydrate<TEntity>(values);

    private RelationalCommandPlan Write(object entity, RelationalOperationKind operation)
    {
        var all = Values(_mapping.Write(entity, operation == RelationalOperationKind.Insert
            ? MappingWriteOperation.Insert
            : MappingWriteOperation.Update));
        var keys = operation == RelationalOperationKind.Insert
            ? all.Where(static value => value.Binding.IsIdentity).ToArray()
            : IdentityForExistingEntity(entity, all);
        var values = all.Where(static value => !value.Binding.IsIdentity).ToArray();
        if (keys.Length != _mapping.Identity.Parts.Count && !_mapping.Identity.IsGenerated)
            throw new MappingValueException(_mapping.Id, _mapping.Identity.LogicalPath.ToString(),
                "A relational write requires every non-generated identity part.");
        return Plan(operation, values, keys, null, null, null, null, null, MappingConsumer.Write, all.Concat(keys));
    }

    private RelationalValue[] IdentityForExistingEntity(object entity, IReadOnlyList<RelationalValue> mapped)
    {
        var present = mapped.Where(static value => value.Binding.IsIdentity).ToArray();
        if (present.Length == _mapping.Identity.Parts.Count) return present;
        if (!_mapping.Identity.IsGenerated) return present;
        return _mapping.Identity.Parts.Select(part =>
        {
            var binding = _mapping.Bindings.Single(candidate => candidate.Id == part.Id);
            return new RelationalValue(Binding(binding), binding.Encode(binding.Read(entity)));
        }).ToArray();
    }

    private RelationalCommandPlan Plan(
        RelationalOperationKind operation,
        IEnumerable<RelationalValue>? values,
        IEnumerable<RelationalValue>? identity,
        IEnumerable<RelationalValue>? conditions,
        IEnumerable<RelationalPathBinding>? reads,
        IEnumerable<RelationalPathBinding>? filters,
        IEnumerable<RelationalPathBinding>? orders,
        QueryDefinition? query,
        MappingConsumer consumer,
        IEnumerable<RelationalValue> evidence)
    {
        var materialized = evidence.ToArray();
        var plan = new RelationalCommandPlan(
            operation,
            _mapping.Container,
            values,
            identity,
            conditions,
            reads,
            filters,
            orders,
            query,
            new MappingReceipt(_mapping.Id, consumer, materialized.Select(static value => value.Binding.BindingId),
                nativeProofRequired: consumer is MappingConsumer.Projection or MappingConsumer.Index));
        RelationalPlanGuard.Validate(_mapping, plan);
        return plan;
    }

    private RelationalValue[] Values(MappedRecord record) => record.Values.Select(value =>
        new RelationalValue(_bindings[value.BindingId], value.Value)).ToArray();

    private RelationalPathBinding Binding(MappingBindingPlan binding) => _bindings[binding.Id];

    private static RelationalPathBinding ToRelational(MappingBindingPlan binding, bool identity) => new(
        binding.Id,
        binding.LogicalPath,
        binding.PhysicalPath,
        binding.Shape,
        binding.PhysicalType,
        binding.Descriptor.Codec?.Id ?? $"clr:{binding.PhysicalType.AssemblyQualifiedName}",
        identity);

    private MappingPath Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var requested = FieldPath.Of(value.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var resolved = FieldPathResolver.Resolve(_mapping.EntityType, requested);
        return MappingPath.Of((resolved.CanonicalPath ?? requested).Segments.ToArray());
    }

    private static IEnumerable<FieldPath> FilterPaths(Filter filter) => filter switch
    {
        FieldFilter field => [field.Field],
        AllOf all => all.Operands.SelectMany(FilterPaths),
        AnyOf any => any.Operands.SelectMany(FilterPaths),
        Not not => FilterPaths(not.Operand),
        ClrFilter => throw new NotSupportedException("A relational command plan cannot claim an opaque CLR filter."),
        _ => throw new NotSupportedException($"Unknown filter node '{filter.GetType().FullName}'.")
    };
}
