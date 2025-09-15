using Koan.Data.Abstractions.Annotations;
using System.Reflection;

namespace Koan.Data.Core;

public static class IndexMetadata
{
    public static IReadOnlyList<IndexSpec> GetIndexes(Type aggregateType)
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

        // Property-level [Index]
        var props = aggregateType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var groups = new Dictionary<string, List<(int order, PropertyInfo prop, IndexAttribute attr)>>();

        foreach (var p in props)
        {
            var attrs = p.GetCustomAttributes<IndexAttribute>(inherit: true).ToArray();
            if (attrs.Length == 0) continue;
            foreach (var attr in attrs)
            {
                var key = attr.Name ?? attr.Group ?? $"__single__:{p.Name}:{Guid.CreateVersion7():N}";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
                list.Add((attr.Order, p, attr));
            }
        }

        foreach (var (key, list) in groups)
        {
            // Skip explicit single-field index on Identifier (covered by PK)
            if (list.Count == 1 && idSpec?.Prop is not null && list[0].prop == idSpec.Prop) continue;

            var ordered = list.OrderBy(t => t.order).ToList();
            var properties = ordered.Select(t => t.prop).ToArray();
            // If any attribute in the group has Unique=true, treat as unique
            var unique = ordered.Any(t => t.attr.Unique);
            var name = ordered.Select(t => t.attr.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            results.Add(new IndexSpec(name, properties, unique, IsPrimaryKey: false, IsImplicit: false));
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
            results.Add(new IndexSpec(idx.Name, fields, idx.Unique, IsPrimaryKey: false, IsImplicit: false));
        }

        return results;
    }
}
