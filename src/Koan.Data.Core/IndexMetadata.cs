using Koan.Data.Abstractions.Annotations;
using System.Collections.Concurrent;
using System.Reflection;

namespace Koan.Data.Core;

public static class IndexMetadata
{
    // Type-plane memoization (DATA-0105 §3). The index set is a pure function of the entity type; it is read
    // on every relational schema-ensure and on several adapter write paths, so it is computed once per type.
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IndexSpec>> Cache = new();

    public static IReadOnlyList<IndexSpec> GetIndexes(Type aggregateType)
        => Cache.GetOrAdd(aggregateType, static t => Compute(t));

    private static IReadOnlyList<IndexSpec> Compute(Type aggregateType)
    {
        var results = new List<IndexSpec>();

        // Implicit PK/unique from [Identifier] (or Id by convention)
        var idSpec = AggregateMetadata.GetIdSpec(aggregateType);
        if (idSpec?.Prop is not null)
        {
            results.Add(new IndexSpec(
                Name: null, // adapters derive e.g., pk_{storage}
                Properties: new[] { idSpec.Prop },
                Unique: true,
                IsPrimaryKey: true,
                IsImplicit: true
            ));
        }

        // Property-level [Index]. Insertion order is tracked explicitly so iteration is deterministic
        // (a Dictionary's enumeration order is not contractually stable), and an anonymous single-property
        // index is disambiguated by its attribute POSITION — not a per-call Guid — so the group key is stable
        // across calls while two [Index] on the same property still stay distinct.
        var props = aggregateType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var order = new List<string>();
        var groups = new Dictionary<string, List<(int order, PropertyInfo prop, IndexAttribute attr)>>();

        foreach (var p in props)
        {
            var attrs = p.GetCustomAttributes<IndexAttribute>(inherit: true).ToArray();
            for (var i = 0; i < attrs.Length; i++)
            {
                var attr = attrs[i];
                var key = attr.Name ?? attr.Group ?? $"__single__:{p.Name}:{i}";
                if (!groups.TryGetValue(key, out var list))
                {
                    groups[key] = list = new();
                    order.Add(key);
                }
                list.Add((attr.Order, p, attr));
            }
        }

        foreach (var key in order)
        {
            var list = groups[key];
            // Skip explicit single-field index on Identifier (covered by PK)
            if (list.Count == 1 && idSpec?.Prop is not null && list[0].prop == idSpec.Prop) continue;

            var ordered = list.OrderBy(t => t.order).ToList();
            var properties = ordered.Select(t => t.prop).ToArray();
            // If any attribute in the group has Unique=true, treat as unique
            var unique = ordered.Any(t => t.attr.Unique);
            var ttl = ordered.Any(t => t.attr.Ttl);
            var name = ordered.Select(t => t.attr.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            results.Add(new IndexSpec(name, properties, unique, IsPrimaryKey: false, IsImplicit: false, Ttl: ttl));
        }

        // Class-level [Index(Fields=[...])]
        var classIndexes = aggregateType.GetCustomAttributes<IndexAttribute>(inherit: true).ToArray();
        foreach (var idx in classIndexes)
        {
            if (idx.Fields is null || idx.Fields.Length == 0) continue;
            var fields = new List<PropertyInfo>();
            foreach (var fieldName in idx.Fields)
            {
                var p = props.FirstOrDefault(pp => string.Equals(pp.Name, fieldName, StringComparison.Ordinal));
                if (p is null) continue; // ignore invalid
                // Skip explicit Id-only index
                if (p == idSpec?.Prop && idx.Fields.Length == 1) { fields.Clear(); break; }
                fields.Add(p);
            }
            if (fields.Count == 0) continue;
            results.Add(new IndexSpec(idx.Name, fields, idx.Unique, IsPrimaryKey: false, IsImplicit: false, Ttl: idx.Ttl));
        }

        return results.ToArray();
    }
}
