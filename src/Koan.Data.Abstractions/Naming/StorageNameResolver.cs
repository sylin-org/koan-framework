using Koan.Data.Abstractions.Annotations;
using System.Reflection;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Adapter-agnostic resolver for deriving table/collection names from an entity type.
/// Honors explicit attributes first, then applies convention based on provided options.
/// </summary>
public static class StorageNameResolver
{
    public readonly record struct Convention(StorageNamingStyle Style, string Separator, NameCasing Casing);

    /// <summary>
    /// Resolve the storage name for an entity type using the given convention defaults.
    /// Precedence:
    /// 1) [Storage(Name=...)]
    /// 2) [StorageName("...")]
    /// 3) [StorageNaming(Style=...)] or the provided <paramref name="defaults"/>
    /// </summary>
    public static string Resolve(Type entityType, Convention defaults)
    {
        // 1) Explicit [Storage(Name=...)]
        var storage = entityType.GetCustomAttribute<StorageAttribute>();
        if (!string.IsNullOrWhiteSpace(storage?.Name)) return storage!.Name!;

        // 2) Shortcut [StorageName]
        var storageName = entityType.GetCustomAttribute<StorageNameAttribute>();
        if (!string.IsNullOrWhiteSpace(storageName?.Name)) return storageName!.Name!;

        // 3) Per-entity naming hint or adapter defaults
        var naming = entityType.GetCustomAttribute<StorageNamingAttribute>()?.Style ?? defaults.Style;
        if (naming == StorageNamingStyle.FullNamespace)
        {
            var full = entityType.FullName ?? (string.IsNullOrEmpty(entityType.Namespace)
                ? entityType.Name
                : entityType.Namespace + "." + entityType.Name);
            var composed = ReplaceDot(full, defaults.Separator);
            return NamingUtils.ApplyCase(composed, defaults.Casing);
        }

        return NamingUtils.ApplyCase(entityType.Name, defaults.Casing);
    }

    private static string ReplaceDot(string input, string separator)
    {
        if (string.IsNullOrEmpty(separator)) return input;
        var sepChar = separator[0];
        return input.Replace('.', sepChar);
    }
}
