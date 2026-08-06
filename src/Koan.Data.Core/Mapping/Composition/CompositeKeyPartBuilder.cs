using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Mapping.Composition;

namespace Koan.Data.Core;

/// <summary>Completes one composite identity component and continues the component list.</summary>
public sealed class CompositeKeyPartBuilder<TEntity, TKey, TPart>
{
    private readonly CompositeKeyBuilder<TEntity, TKey> _owner;
    private readonly MappingBindingDraft _binding;
    private bool _finalized;

    internal CompositeKeyPartBuilder(CompositeKeyBuilder<TEntity, TKey> owner, MappingBindingDraft binding)
    {
        _owner = owner;
        _binding = binding;
    }

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> Name(string name) =>
        Locate(new PhysicalPath(name));

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> Path(string name, params string[] segments)
    {
        if (segments is null || segments.Length == 0)
            throw new ArgumentException("Path requires at least one structured segment.", nameof(segments));
        return Locate(new PhysicalPath(name, segments));
    }

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> ReadOnly()
    {
        _binding.Direction = MappingDirection.ReadOnly;
        return this;
    }

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> Codec(IDataMappingCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _binding.Codec = codec;
        return this;
    }

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> Codec<TPhysical>(
        Func<TPart?, TPhysical?> encode,
        Func<TPhysical?, TPart?> decode,
        string? id = null) => Codec(new DataMappingCodec<TPart, TPhysical>(encode, decode, id));

    public CompositeKeyPartBuilder<TEntity, TKey, TNext> Property<TNext>(Expression<Func<TKey, TNext>> property)
    {
        RequireFinalized();
        return _owner.Property(property);
    }

    private CompositeKeyPartBuilder<TEntity, TKey, TPart> Locate(PhysicalPath path)
    {
        if (_finalized) throw new InvalidOperationException($"Composite identity part '{_binding.LogicalPath}' is already located.");
        _binding.Locate(path, MappingValueShape.Scalar);
        _finalized = true;
        return this;
    }

    private void RequireFinalized()
    {
        if (!_finalized)
            throw new InvalidOperationException($"Composite identity part '{_binding.LogicalPath}' requires Name or Path.");
    }
}
