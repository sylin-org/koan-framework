using Koan.Data.Abstractions.Annotations;
using System.Linq;
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

        // 2.5) Closed generic over its type arguments (DATA-0104). A generic-over-entity like
        // EmbeddingState<Todo> must NOT fall into the style branches below — Type.Name/Type.FullName mangle to
        // "EmbeddingState`1" (arg dropped -> all closures collide) or the assembly-qualified
        // "EmbeddingState`1[[Todo, asm, Version=…]]" (illegal/version-fragile identifier). Instead, anchor the
        // name on the type arguments and append the generic's bare simple name:
        //   Resolve(G<a1..aN>) = join("--", ai => Resolve(ai)) + "-" + ApplyCase(SimpleName(G))
        // The recursion gives each argument the adapter's FULL style/casing treatment (so the inner entity's
        // namespace/hash/casing is honored), "-" joins the spine (single-arg wrapper) and "--" the branch
        // (sibling args), the entity stays the leftmost anchor, and casing is uniform per convention. Closures
        // stay distinct (different arg -> different anchor) and the name is legal on every backend. Explicit
        // [Storage]/[StorageName] above still win (and collapse all closures — intentional, author's choice).
        if (entityType.IsGenericType && !entityType.IsGenericTypeDefinition)
        {
            var anchor = string.Join("--", entityType.GetGenericArguments().Select(a => Resolve(a, defaults)));
            var wrapper = NamingUtils.ApplyCase(SimpleName(entityType), defaults.Casing);
            return anchor + "-" + wrapper;
        }

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

    /// <summary>
    /// The type's simple name with the CLR generic-arity marker (<c>`N</c>) stripped:
    /// <c>EmbeddingState`1</c> → <c>EmbeddingState</c>. Non-generic names pass through unchanged.
    /// </summary>
    private static string SimpleName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
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
