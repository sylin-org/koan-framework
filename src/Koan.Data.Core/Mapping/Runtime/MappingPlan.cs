using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Mapping.Runtime;

namespace Koan.Data.Core;

/// <summary>The single immutable runtime owner of an aggregate's logical-to-physical decisions.</summary>
public sealed class MappingPlan
{
    private readonly Dictionary<MappingPath, MappingBindingPlan[]> _byLogical;
    private readonly Dictionary<MappingPath, MappingUsePlan?[]> _uses;
    private readonly object _useGate = new();
    private readonly Func<object> _entityFactory;
    private readonly MappingBindingPlan? _rootObject;
    private readonly CompositeIdentityPlan? _compositeIdentity;
    private readonly MappingBindingPlan[] _writeBindings;
    private IReadOnlyList<MappingIndexPlan> _indexes = Array.Empty<MappingIndexPlan>();

    internal MappingPlan(
        string id,
        MappingDescriptor descriptor,
        MappingBindingPlan[] bindings,
        Func<object> entityFactory,
        CompositeIdentityPlan? compositeIdentity)
    {
        Id = id;
        Descriptor = descriptor;
        Bindings = Array.AsReadOnly(bindings);
        _entityFactory = entityFactory;
        _compositeIdentity = compositeIdentity;
        _rootObject = bindings.SingleOrDefault(static binding =>
            binding.Descriptor.Role == MappingRole.Object && binding.LogicalPath.IsRoot);
        _writeBindings = bindings.Where(static binding =>
            binding.Descriptor.Direction == MappingDirection.ReadWrite &&
            binding.Descriptor.Generation == MappingGeneration.Application &&
            binding.Descriptor.Authority == MappingAuthority.Canonical).ToArray();
        _byLogical = bindings.GroupBy(static binding => binding.LogicalPath)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        _uses = new Dictionary<MappingPath, MappingUsePlan?[]>();
        foreach (var binding in bindings)
        {
            for (var length = 0; length <= binding.LogicalPath.Segments.Count; length++)
            {
                var path = length == 0
                    ? MappingPath.Root
                    : MappingPath.Of(binding.LogicalPath.Segments.Take(length).ToArray());
                _uses.TryAdd(path, new MappingUsePlan?[Enum.GetValues<MappingConsumer>().Length]);
            }
        }
        _uses.TryAdd(Identity.LogicalPath, new MappingUsePlan?[Enum.GetValues<MappingConsumer>().Length]);
    }

    public string Id { get; }
    public MappingDescriptor Descriptor { get; }
    public string Source => Descriptor.Source;
    public Type EntityType => Descriptor.EntityType;
    public StorageAddress Container => Descriptor.Container;
    public MappingIdentityDescriptor Identity => Descriptor.Identity;
    public IReadOnlyList<MappingBindingPlan> Bindings { get; }
    public IReadOnlyList<MappingIndexPlan> Indexes => _indexes;

    internal void InitializeIndexes(MappingIndexPlan[] indexes)
    {
        if (_indexes.Count != 0) throw new InvalidOperationException($"Mapping plan '{Id}' indexes are already initialized.");
        _indexes = Array.AsReadOnly(indexes);
    }

    public MappingUsePlan Use(MappingPath logicalPath, MappingConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        if (!_uses.TryGetValue(logicalPath, out var slots))
            throw new MappingValueException(Id, logicalPath.ToString(), "Declare a physical binding for this logical path.");
        var slot = (int)consumer;
        if (Volatile.Read(ref slots[slot]) is { } cached) return cached;
        lock (_useGate)
        {
            if (slots[slot] is { } existing) return existing;
            MappingBindingPlan[] bindings;
            if (!_byLogical.TryGetValue(logicalPath, out bindings!))
            {
                bindings = Bindings.Where(binding => logicalPath.IsPrefixOf(binding.LogicalPath)).ToArray();
                if (bindings.Length == 0 && logicalPath.Equals(Identity.LogicalPath))
                    bindings = Identity.Parts.Select(part => Bindings.Single(binding => binding.Id == part.Id)).ToArray();
            }
            if (bindings.Length == 0)
                throw new MappingValueException(Id, logicalPath.ToString(), "Declare a physical binding for this logical path.");
            ValidateUse(logicalPath, consumer, bindings);
            var created = new MappingUsePlan(logicalPath, consumer, bindings, Id);
            Volatile.Write(ref slots[slot], created);
            return created;
        }
    }

    public MappingUsePlan Use(FieldPath logicalPath, MappingConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        return Use(MappingPath.Of(logicalPath.Segments.ToArray()), consumer);
    }

    public MappingReadPlan Read(params MappingPath[] logicalPaths)
    {
        ArgumentNullException.ThrowIfNull(logicalPaths);
        var selected = logicalPaths.Length == 0
            ? Bindings.Where(static binding => binding.Descriptor.Authority == MappingAuthority.Canonical).ToArray()
            : logicalPaths.SelectMany(path => Use(path, MappingConsumer.Projection).Bindings).DistinctBy(static item => item.Id).ToArray();
        return new MappingReadPlan(
            selected,
            new MappingReceipt(Id, MappingConsumer.Projection, selected.Select(static binding => binding.Id), nativeProofRequired: true));
    }

    public MappedRecord Write(object entity, MappingWriteOperation operation = MappingWriteOperation.Insert)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!EntityType.IsInstanceOfType(entity))
            throw new ArgumentException($"Mapping plan '{Id}' expects '{EntityType.FullName}'.", nameof(entity));
        var values = new List<MappedValue>(_writeBindings.Length);
        foreach (var binding in _writeBindings)
        {
            try
            {
                values.Add(new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, binding.Encode(binding.Read(entity))));
            }
            catch (Exception error) when (error is not MappingValueException)
            {
                throw new MappingValueException(Id, binding.Id, "The logical value could not be encoded.", error);
            }
        }
        var consumer = operation switch
        {
            MappingWriteOperation.Patch => MappingConsumer.Patch,
            MappingWriteOperation.ConditionalWrite => MappingConsumer.ConditionalWrite,
            _ => MappingConsumer.Write
        };
        return new MappedRecord(values, new MappingReceipt(Id, consumer, values.Select(static value => value.BindingId)));
    }

    public MappedRecord WriteIdentity(object identity, MappingConsumer consumer = MappingConsumer.Filter)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var parts = _compositeIdentity is null ? [identity] : _compositeIdentity.Split(identity);
        if (parts.Length != Identity.Parts.Count || parts.Any(static part => part is null))
            throw new MappingValueException(Id, Identity.LogicalPath.ToString(), "Composite identity must provide every non-null declared part.");
        var values = new MappedValue[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var binding = Bindings.Single(candidate => candidate.Id == Identity.Parts[index].Id);
            values[index] = new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, binding.Encode(parts[index]));
        }
        return new MappedRecord(values, new MappingReceipt(Id, consumer, values.Select(static value => value.BindingId)));
    }

    public object Hydrate(IEnumerable<MappedValue> values) => HydrateWithReceipt(values).Entity;

    public TEntity Hydrate<TEntity>(IEnumerable<MappedValue> values) => (TEntity)Hydrate(values);

    public MappingMaterialization HydrateWithReceipt(IEnumerable<MappedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var supplied = values.ToArray();
        var byPath = new Dictionary<PhysicalPath, MappedValue>();
        foreach (var value in supplied)
            if (!byPath.TryAdd(value.Path, value))
                throw new MappingValueException(Id, value.BindingId, $"Physical path '{value.Path}' is ambiguous.");

        object entity;
        if (_rootObject is not null && byPath.TryGetValue(_rootObject.PhysicalPath, out var root))
        {
            try { entity = _rootObject.Decode(root.Value) ?? _entityFactory(); }
            catch (Exception error) when (error is not MappingValueException)
            {
                throw new MappingValueException(Id, _rootObject.Id, "The structured aggregate value could not be hydrated.", error);
            }
        }
        else entity = _entityFactory();

        foreach (var binding in Bindings)
        {
            if (binding.Descriptor.Authority != MappingAuthority.Canonical ||
                ReferenceEquals(binding, _rootObject) ||
                Identity.IsComposite && Identity.Parts.Any(part => part.Id == binding.Id))
                continue;
            if (!byPath.TryGetValue(binding.PhysicalPath, out var value)) continue;
            try { binding.Assign(entity, value.Value); }
            catch (Exception error) when (error is not MappingValueException)
            {
                throw new MappingValueException(Id, binding.Id, "The physical value could not be assigned.", error);
            }
        }

        if (_compositeIdentity is not null)
        {
            var present = _compositeIdentity.Bindings
                .Select(binding => byPath.TryGetValue(binding.PhysicalPath, out var value) ? value : null)
                .ToArray();
            if (present.Any(static value => value is not null))
            {
                if (present.Any(static value => value is null))
                    throw new MappingValueException(Id, Identity.LogicalPath.ToString(), "Composite identity hydration requires every declared part.");
                var decoded = new object?[present.Length];
                for (var index = 0; index < present.Length; index++)
                {
                    decoded[index] = _compositeIdentity.Bindings[index].Decode(present[index]!.Value);
                    if (decoded[index] is null)
                        throw new MappingValueException(Id, _compositeIdentity.Bindings[index].Id, "Composite identity parts cannot be null.");
                }
                _compositeIdentity.Assign(entity, decoded);
            }
        }

        return new MappingMaterialization(
            entity,
            new MappingReceipt(Id, MappingConsumer.Hydration, supplied.Select(static value => value.BindingId)));
    }

    private void ValidateUse(MappingPath path, MappingConsumer consumer, MappingBindingPlan[] bindings)
    {
        if (consumer is MappingConsumer.Filter or MappingConsumer.Order or MappingConsumer.Patch or
            MappingConsumer.ConditionalWrite or MappingConsumer.Index)
        {
            if (bindings.Length != 1)
                throw new MappingValueException(Id, path.ToString(), $"{consumer} requires one physical binding.");
            if (consumer is MappingConsumer.Order or MappingConsumer.ConditionalWrite or MappingConsumer.Index &&
                bindings[0].Shape != MappingValueShape.Scalar)
                throw new MappingValueException(Id, path.ToString(), $"{consumer} requires one scalar physical binding.");
        }
        if (consumer is MappingConsumer.Patch or MappingConsumer.ConditionalWrite)
        {
            var descriptor = bindings[0].Descriptor;
            if (descriptor.Direction != MappingDirection.ReadWrite || descriptor.Generation == MappingGeneration.Provider ||
                descriptor.Authority != MappingAuthority.Canonical)
                throw new MappingValueException(Id, bindings[0].Id, $"{consumer} requires a writable canonical binding.");
        }
        if (consumer is MappingConsumer.Filter or MappingConsumer.Order or MappingConsumer.ConditionalWrite or MappingConsumer.Index)
        {
            foreach (var binding in bindings)
                if (binding.Descriptor.Codec is { CanEncode: false })
                    throw new MappingValueException(Id, binding.Id, $"{consumer} requires codec encoding.");
        }
    }
}
