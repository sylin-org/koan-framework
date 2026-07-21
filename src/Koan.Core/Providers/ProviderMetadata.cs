using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace Koan.Core.Providers;

/// <summary>Memoized framework metadata shared by typed provider catalogs.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ProviderMetadata
{
    private static readonly ConcurrentDictionary<Type, int> Priorities = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>> ReferenceIdentities = new();

    public static int Priority(Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        return Priorities.GetOrAdd(
            providerType,
            static type => type.GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0);
    }

    public static IReadOnlyList<string> References(Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        return ReferenceIdentities.GetOrAdd(providerType, static type =>
        {
            var assembly = type.Assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assembly)) return [];

            if (assembly.Equals("Koan", StringComparison.OrdinalIgnoreCase)
                || assembly.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase))
            {
                return [assembly, $"Sylin.{assembly}"];
            }

            if (assembly.Equals("Sylin.Koan", StringComparison.OrdinalIgnoreCase)
                || assembly.StartsWith("Sylin.Koan.", StringComparison.OrdinalIgnoreCase))
            {
                return [assembly[6..], assembly];
            }

            return [assembly];
        });
    }
}
