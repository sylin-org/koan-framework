using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Core;

public static class ProjectionResolver
{
    // DATA-0105 phase 3a — the SINGLE converged column-name + exclusion resolver. Column naming and storage
    // exclusion previously had two divergent readers: the live ProjectionResolver ([Column]/[NotMapped], EF
    // DataAnnotations) and the dead RelationalModelBuilder (property [StorageName]/[IgnoreStorage], Koan
    // annotations). Both attribute families now resolve here, so the dead resolver is deleted (consolidation =
    // deletion). [Column] wins over property [StorageName] when both are present (preserves the shipped
    // [Column] behaviour; [StorageName] is the previously-dead Koan-native fallback). Used by Compute below
    // AND by the relational schema orchestrator's index-column resolution — one resolver, no divergence.

    /// <summary>The storage column name for a property: <c>[Column]</c> ?? property <c>[StorageName]</c> ?? the CLR name.</summary>
    public static string ColumnNameOf(PropertyInfo p)
    {
        var col = p.GetCustomAttribute<ColumnAttribute>(inherit: true)?.Name;
        if (!string.IsNullOrWhiteSpace(col)) return col!;
        var sn = p.GetCustomAttribute<StorageNameAttribute>(inherit: true)?.Name;
        if (!string.IsNullOrWhiteSpace(sn)) return sn!;
        return p.Name;
    }

    /// <summary>True when a property is excluded from storage projection: <c>[NotMapped]</c> or <c>[IgnoreStorage]</c>.</summary>
    public static bool IsExcludedFromStorage(PropertyInfo p)
        => p.GetCustomAttribute<NotMappedAttribute>(inherit: true) is not null
        || p.GetCustomAttribute<IgnoreStorageAttribute>(inherit: true) is not null;

    private static readonly HashSet<Type> ScalarTypes = new(
        new[]
        {
            typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
            typeof(DateTime), typeof(DateTimeOffset), typeof(Guid)
        }
    );

    // Type-plane memoization (DATA-0105 §3). The projection is a pure function of the entity type; it is read
    // on every relational schema-ensure and several adapter write paths, so it is computed once per type.
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<ProjectedProperty>> Cache = new();

    public static IReadOnlyList<ProjectedProperty> Get(Type aggregateType)
        => Cache.GetOrAdd(aggregateType, static t => Compute(t));

    private static IReadOnlyList<ProjectedProperty> Compute(Type aggregateType)
    {
        var props = aggregateType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        var id = AggregateMetadata.GetIdSpec(aggregateType)?.Prop;
        var indexes = IndexMetadata.GetIndexes(aggregateType);
        // TTL indexes (§20.4) are honored only by TTL-capable stores (Mongo); a relational adapter must not build a
        // redundant index on the expiry field, so a TTL-only property is not counted as "indexed" here.
        var indexedProps = new HashSet<PropertyInfo>(indexes.Where(i => !i.Ttl).SelectMany(i => i.Properties));

        var list = new List<ProjectedProperty>();
        foreach (var p in props)
        {
            if (p == id) continue;
            if (IsExcludedFromStorage(p)) continue;

            var t = p.PropertyType;
            var isEnum = t.IsEnum;
            var isScalar = ScalarTypes.Contains(t) || isEnum;
            if (!isScalar) continue;

            var isIndexed = indexedProps.Contains(p);
            list.Add(new ProjectedProperty(p, ColumnNameOf(p), isEnum, isIndexed));
        }
        return list.ToArray();
    }
}
