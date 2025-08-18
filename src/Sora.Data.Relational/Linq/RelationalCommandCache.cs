using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Sora.Data.Relational.Linq;

/// <summary>
/// Tiny cache for composed SQL fragments per (entity,dialect,shape) to avoid repeated string building.
/// </summary>
public static class RelationalCommandCache
{
    private static readonly ConcurrentDictionary<string, string> _selectCache = new();

    public static string GetOrAddSelect<TEntity>(ILinqSqlDialect dialect, Func<string> factory)
    {
        var key = $"select|{typeof(TEntity).FullName}|{dialect.GetType().FullName}";
        return _selectCache.GetOrAdd(key, _ => factory());
    }
}
