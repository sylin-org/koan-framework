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
        if (!string.IsNullOrWhiteSpace(storage?.Name)) return storage!.Name!.Trim();

        // 2) Shortcut [StorageName]
        var storageName = entityType.GetCustomAttribute<StorageNameAttribute>();
        if (!string.IsNullOrWhiteSpace(storageName?.Name)) return storageName!.Name!.Trim();

        // 3) Per-entity naming hint or adapter defaults
        var naming = entityType.GetCustomAttribute<StorageNamingAttribute>()?.Style ?? defaults.Style;
        if (naming == StorageNamingStyle.FullNamespace)
        {
            var composed = ReplaceDot(FullName(entityType), defaults.Separator);
            return NamingUtils.ApplyCase(composed, defaults.Casing);
        }

        if (naming == StorageNamingStyle.HashedNamespace)
        {
            // {ClassName}_{hash(declaring-context)}. Readable class name + short stable hash of the
            // namespace + declaring types, so distinct types that share a simple name stay distinct while
            // the result stays short (identifier-limit-safe). The join is a fixed underscore — the
            // Separator convention governs namespace-path joining, which this style doesn't have. No
            // namespace key => just the class name.
            var name = NamingUtils.ApplyCase(entityType.Name, defaults.Casing);
            var nsKey = NamespaceKey(entityType);
            return string.IsNullOrEmpty(nsKey) ? name : name + "_" + NamingUtils.ShortHash(nsKey);
        }

        return NamingUtils.ApplyCase(entityType.Name, defaults.Casing);
    }

    private static string FullName(Type entityType)
        => entityType.FullName ?? (string.IsNullOrEmpty(entityType.Namespace)
            ? entityType.Name
            : entityType.Namespace + "." + entityType.Name);

    /// <summary>
    /// The qualifying context that precedes the simple type name in <see cref="Type.FullName"/> — the
    /// namespace plus any declaring types. Two same-named types in different namespaces/declaring contexts
    /// produce different keys (and thus different hashes).
    /// </summary>
    private static string NamespaceKey(Type entityType)
    {
        var full = FullName(entityType);
        var simple = entityType.Name;
        if (full.Length > simple.Length && full.EndsWith(simple, StringComparison.Ordinal))
            return full[..^simple.Length].TrimEnd('.', '+');
        return entityType.Namespace ?? "";
    }

    private static string ReplaceDot(string input, string separator)
    {
        if (string.IsNullOrEmpty(separator)) return input;
        var sepChar = separator[0];
        return input.Replace('.', sepChar);
    }
}
