using System.Collections.Concurrent;
using System.Reflection;

namespace Koan.Data.SoftDelete;

/// <summary>Type-plane memoized probe for the <see cref="SoftDeleteAttribute"/> — cheap on the hot read/write path.</summary>
internal static class SoftDeleteMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> _cache = new();

    public static bool IsSoftDelete(Type entityType)
        => _cache.GetOrAdd(entityType, static t => t.GetCustomAttribute<SoftDeleteAttribute>(inherit: true) is not null);
}
