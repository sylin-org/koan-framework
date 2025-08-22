using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Core.Naming;

internal sealed class DelegatingStorageNameResolver : IStorageNameResolver
{
    private readonly Func<Type, StorageNameResolver.Convention, string?> _override;
    private readonly IStorageNameResolver _inner;

    public DelegatingStorageNameResolver(Func<Type, StorageNameResolver.Convention, string?> @override)
    {
        _override = @override;
        _inner = new DefaultStorageNameResolver();
    }

    public string Resolve(Type entityType, StorageNameResolver.Convention defaults)
        => _override(entityType, defaults) ?? _inner.Resolve(entityType, defaults);
}