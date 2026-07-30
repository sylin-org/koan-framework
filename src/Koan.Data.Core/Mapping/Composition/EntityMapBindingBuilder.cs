using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Mapping.Composition;

namespace Koan.Data.Core;

/// <summary>Completes one selected logical value, then continues the enclosing entity map.</summary>
public sealed class EntityMapBindingBuilder<TEntity, TValue>
{
    private readonly EntityMapBuilder<TEntity> _owner;
    private readonly MappingBindingDraft _binding;
    private MappingBindingDraft _behaviorBinding;
    private bool _finalized;

    internal EntityMapBindingBuilder(
        EntityMapBuilder<TEntity> owner,
        MappingBindingDraft binding,
        bool finalized = false)
    {
        _owner = owner;
        _binding = binding;
        _behaviorBinding = binding;
        _finalized = finalized;
    }

    public EntityMapBindingBuilder<TEntity, TValue> Name(string name) =>
        Locate(new PhysicalPath(name), MappingValueShape.Scalar);

    public EntityMapBindingBuilder<TEntity, TValue> Path(string name, params string[] segments)
    {
        if (segments is null || segments.Length == 0)
            throw new ArgumentException("Path requires at least one structured segment.", nameof(segments));
        return Locate(new PhysicalPath(name, segments), MappingValueShape.Scalar);
    }

    public EntityMapBindingBuilder<TEntity, TValue> Object(string name)
    {
        if (!_finalized) return Locate(new PhysicalPath(name), MappingValueShape.Object);
        _behaviorBinding = _owner.AddRootObject(name);
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TValue> Generated()
    {
        _behaviorBinding.Generation = MappingGeneration.Provider;
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TValue> ReadOnly()
    {
        _behaviorBinding.Direction = MappingDirection.ReadOnly;
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TValue> Codec(IDataMappingCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _behaviorBinding.Codec = codec;
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TValue> Codec<TPhysical>(
        Func<TValue?, TPhysical?> encode,
        Func<TPhysical?, TValue?> decode,
        string? id = null)
    {
        if (!ReferenceEquals(_behaviorBinding, _binding))
            throw new InvalidOperationException("Use a non-generic IDataMappingCodec overload for a chained root Object.");
        return Codec(new DataMappingCodec<TValue, TPhysical>(encode, decode, id));
    }

    public EntityMapBindingBuilder<TEntity, TValue> Parts(
        Action<CompositeKeyBuilder<TEntity, TValue>> configure)
    {
        if (_binding.Role != MappingRole.Key)
            throw new InvalidOperationException("Parts is available only after Key.");
        if (_finalized)
            throw new InvalidOperationException("A key cannot have both a direct physical location and Parts.");
        ArgumentNullException.ThrowIfNull(configure);
        var parts = new CompositeKeyBuilder<TEntity, TValue>(_binding.LogicalPath);
        configure(parts);
        _owner.CompleteComposite(parts.Build());
        _finalized = true;
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TNext> Property<TNext>(Expression<Func<TEntity, TNext>> property)
    {
        RequireFinalized();
        return _owner.Property(property);
    }

    public EntityMapBindingBuilder<TEntity, TKey> Key<TKey>(Expression<Func<TEntity, TKey>> property)
    {
        RequireFinalized();
        return _owner.Key(property);
    }

    public EntityMapBuilder<TEntity> Container(params string[] segments)
    {
        RequireFinalized();
        return _owner.Container(segments);
    }

    private EntityMapBindingBuilder<TEntity, TValue> Locate(PhysicalPath path, MappingValueShape shape)
    {
        if (_finalized)
            throw new InvalidOperationException($"Logical value '{_binding.LogicalPath}' is already located.");
        _binding.Locate(path, shape);
        _owner.Complete(_binding);
        _finalized = true;
        return this;
    }

    private void RequireFinalized()
    {
        if (!_finalized)
            throw new InvalidOperationException($"Logical value '{_binding.LogicalPath}' requires Name, Path, Object, or Parts.");
    }
}
