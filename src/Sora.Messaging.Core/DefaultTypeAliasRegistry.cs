using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Sora.Messaging;

internal sealed class DefaultTypeAliasRegistry : ITypeAliasRegistry
{
    private readonly ConcurrentDictionary<Type, string> _to = new();
    private readonly ConcurrentDictionary<string, Type> _from = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _includeVersion;
    public DefaultTypeAliasRegistry(IOptions<MessagingOptions> opts)
    {
        _includeVersion = opts.Value.IncludeVersionInAlias;
    }
    public string GetAlias(Type type)
    {
        return _to.GetOrAdd(type, t =>
        {
            // Special case: Batch<T> => "batch:{alias(T)}"
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Batch<>))
            {
                var inner = t.GetGenericArguments()[0];
                var innerAlias = GetAlias(inner);
                var batchAlias = $"batch:{innerAlias}";
                _from[batchAlias] = t;
                return batchAlias;
            }

            var attr = t.GetCustomAttributes(typeof(MessageAttribute), false).FirstOrDefault() as MessageAttribute;
            var baseName = attr?.Alias ?? t.FullName ?? t.Name;
            string final = baseName;
            if (_includeVersion && attr is not null)
            {
                final = $"{baseName}@v{attr.Version}";
                // Accept both versioned and base aliases for resolution
                _from[baseName] = t;
            }
            _from[final] = t;
            return final;
        });
    }
    public Type? Resolve(string alias)
    {
        if (_from.TryGetValue(alias, out var t)) return t;

        // batch:{alias(T)} => Batch<T>
        if (alias.StartsWith("batch:", StringComparison.OrdinalIgnoreCase))
        {
            var innerAlias = alias.Substring("batch:".Length);
            var innerType = Resolve(innerAlias) ?? TryResolveByFullName(innerAlias);
            if (innerType is not null)
            {
                var closed = typeof(Batch<>).MakeGenericType(innerType);
                _from[alias] = closed;
                _to[closed] = alias;
                return closed;
            }
            return null;
        }

        // Fallback: try to resolve by full name using loaded assemblies
        var byName = TryResolveByFullName(alias);
        if (byName is not null)
        {
            // Cache for future lookups
            _from[alias] = byName;
            _to[byName] = alias;
        }
        return byName;
    }

    private static Type? TryResolveByFullName(string fullName)
    {
        var t = Type.GetType(fullName, throwOnError: false);
        if (t is not null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fullName, throwOnError: false, ignoreCase: false); }
            catch { t = null; }
            if (t is not null) return t;
        }
        return null;
    }
}