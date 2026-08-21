using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Core.Mapping.Runtime;

internal static class MappingIndexCompiler
{
    public static MappingIndexPlan[] Compile(MappingPlan plan)
    {
        var indexes = new List<MappingIndexPlan>
        {
            new(
                $"PK_{plan.Container.Name}",
                plan.Identity.Parts.Select(part => plan.Bindings.Single(binding => binding.Id == part.Id)),
                unique: true,
                primary: true,
                ttl: false,
                plan.Id)
        };
        var properties = plan.EntityType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var groups = new Dictionary<string, List<(int Order, PropertyInfo Property, IndexAttribute Attribute)>>(StringComparer.Ordinal);
        var groupOrder = new List<string>();
        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes<IndexAttribute>(inherit: true).ToArray();
            for (var index = 0; index < attributes.Length; index++)
            {
                var attribute = attributes[index];
                var key = attribute.Name ?? attribute.Group ?? $"__single__:{property.Name}:{index}";
                if (!groups.TryGetValue(key, out var group))
                {
                    group = [];
                    groups.Add(key, group);
                    groupOrder.Add(key);
                }
                group.Add((attribute.Order, property, attribute));
            }
        }

        foreach (var key in groupOrder)
        {
            var group = groups[key].OrderBy(static item => item.Order).ToArray();
            var paths = group.Select(item => MappingPath.Of(item.Property.Name)).ToArray();
            if (paths.Length == 1 && paths[0].Equals(plan.Identity.LogicalPath)) continue;
            indexes.Add(Build(
                plan,
                group.Select(static item => item.Attribute.Name).FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
                    ?? $"IX_{plan.Container.Name}_{string.Join('_', paths.Select(static path => path.Leaf))}",
                paths,
                group.Any(static item => item.Attribute.Unique),
                group.Any(static item => item.Attribute.Ttl),
                group.Any(static item => item.Attribute.Required)));
        }

        foreach (var attribute in plan.EntityType.GetCustomAttributes<IndexAttribute>(inherit: true))
        {
            if (attribute.Fields is null || attribute.Fields.Length == 0) continue;
            var paths = attribute.Fields.Select(field =>
            {
                var property = properties.SingleOrDefault(candidate => string.Equals(candidate.Name, field, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"Index '{attribute.Name ?? "<unnamed>"}' references unknown logical property '{field}'.");
                return MappingPath.Of(property.Name);
            }).ToArray();
            if (paths.Length == 1 && paths[0].Equals(plan.Identity.LogicalPath)) continue;
            indexes.Add(Build(
                plan,
                attribute.Name ?? $"IX_{plan.Container.Name}_{string.Join('_', paths.Select(static path => path.Leaf))}",
                paths,
                attribute.Unique,
                attribute.Ttl,
                attribute.Required));
        }

        return indexes.ToArray();
    }

    private static MappingIndexPlan Build(
        MappingPlan plan,
        string name,
        IEnumerable<MappingPath> paths,
        bool unique,
        bool ttl,
        bool required = false)
    {
        var bindings = paths.SelectMany(path => plan.Use(path, MappingConsumer.Index).Bindings).ToArray();
        if (ttl && (bindings.Length != 1 || !IsTemporal(bindings[0].LogicalType)))
            throw new InvalidOperationException($"TTL index '{name}' requires one mapped temporal scalar.");
        return new MappingIndexPlan(name, bindings, unique, primary: false, ttl, plan.Id, required);
    }

    private static bool IsTemporal(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        return effective == typeof(DateTime) || effective == typeof(DateTimeOffset);
    }
}
