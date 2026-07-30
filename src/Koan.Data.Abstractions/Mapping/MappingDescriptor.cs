namespace Koan.Data.Abstractions;

/// <summary>The immutable provider-neutral declaration compiled for one source and aggregate type.</summary>
public sealed record MappingDescriptor
{
    public MappingDescriptor(
        string source,
        Type entityType,
        StorageAddress container,
        MappingIdentityDescriptor identity,
        IEnumerable<MappingBindingDescriptor> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bindings);
        var copy = bindings.ToArray();
        Source = source.Trim();
        EntityType = entityType;
        Container = container;
        Identity = identity;
        Bindings = Array.AsReadOnly(copy);
    }

    public string Source { get; }
    public Type EntityType { get; }
    public StorageAddress Container { get; }
    public MappingIdentityDescriptor Identity { get; }
    public IReadOnlyList<MappingBindingDescriptor> Bindings { get; }
}
