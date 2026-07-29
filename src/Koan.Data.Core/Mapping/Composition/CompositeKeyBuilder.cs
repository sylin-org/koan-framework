using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Mapping.Composition;

namespace Koan.Data.Core;

/// <summary>Declares every physical component of an immutable composite identity.</summary>
public sealed class CompositeKeyBuilder<TEntity, TKey>
{
    private readonly MappingPath _keyPath;
    private readonly List<MappingBindingDraft> _parts = [];
    private MappingBindingDraft? _pending;

    internal CompositeKeyBuilder(MappingPath keyPath) => _keyPath = keyPath;

    public CompositeKeyPartBuilder<TEntity, TKey, TPart> Property<TPart>(Expression<Func<TKey, TPart>> property)
    {
        if (_pending is { IsLocated: false })
            throw new InvalidOperationException($"Composite identity part '{_pending.LogicalPath}' requires Name or Path.");
        var (relative, type) = MappingExpression.PropertyPath(property);
        _pending = new MappingBindingDraft(_keyPath.Append(relative), MappingRole.Key, type);
        _parts.Add(_pending);
        return new CompositeKeyPartBuilder<TEntity, TKey, TPart>(this, _pending);
    }

    internal IReadOnlyList<MappingBindingDraft> Build()
    {
        if (_pending is { IsLocated: false })
            throw new InvalidOperationException($"Composite identity part '{_pending.LogicalPath}' requires Name or Path.");
        return _parts.ToArray();
    }
}
