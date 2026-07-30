using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Mapping.Composition;

namespace Koan.Data.Core;

/// <summary>Declares one aggregate-to-record map using provider-neutral logical and physical terms.</summary>
public sealed class EntityMapBuilder<TEntity>
{
    private readonly MappingDeclarationDraft _draft;

    internal EntityMapBuilder(string source) => _draft = new MappingDeclarationDraft(source, typeof(TEntity));

    public EntityMapBuilder<TEntity> Container(params string[] segments)
    {
        _draft.Container = StorageAddress.From(segments);
        return this;
    }

    public EntityMapBuilder<TEntity> Container(StorageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _draft.Container = address;
        return this;
    }

    public EntityMapBindingBuilder<TEntity, TKey> Key<TKey>(Expression<Func<TEntity, TKey>> property)
    {
        var (path, type) = MappingExpression.PropertyPath(property);
        return new EntityMapBindingBuilder<TEntity, TKey>(this, _draft.BeginKey(path, type));
    }

    public EntityMapBindingBuilder<TEntity, TValue> Property<TValue>(Expression<Func<TEntity, TValue>> property)
    {
        var (path, type) = MappingExpression.PropertyPath(property);
        return new EntityMapBindingBuilder<TEntity, TValue>(this, _draft.BeginProperty(path, type));
    }

    public EntityMapBindingBuilder<TEntity, TEntity> Object(string name)
    {
        var binding = AddRootObject(name);
        return new EntityMapBindingBuilder<TEntity, TEntity>(this, binding, finalized: true);
    }

    internal MappingBindingDraft AddRootObject(string name)
    {
        var binding = _draft.BeginRootObject();
        binding.Locate(new PhysicalPath(name), MappingValueShape.Object);
        _draft.Add(binding);
        return binding;
    }

    internal void Complete(MappingBindingDraft binding) => _draft.Add(binding);

    internal void CompleteComposite(IReadOnlyList<MappingBindingDraft> parts) => _draft.AddComposite(parts);

    internal MappingDescriptor Build() => _draft.Build();
}
