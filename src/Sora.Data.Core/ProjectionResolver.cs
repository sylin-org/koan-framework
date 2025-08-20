using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Metadata;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace Sora.Data.Core;

public sealed record ProjectedProperty(
    PropertyInfo Property,
    string ColumnName,
    bool IsEnum,
    bool IsIndexed
);

public static class ProjectionResolver
{
    private static readonly HashSet<Type> ScalarTypes = new(
        new[]
        {
            typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
            typeof(DateTime), typeof(DateTimeOffset), typeof(Guid)
        }
    );

    public static IReadOnlyList<ProjectedProperty> Get(Type aggregateType)
    {
        var props = aggregateType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        var id = AggregateMetadata.GetIdSpec(aggregateType)?.Prop;
        var indexes = IndexMetadata.GetIndexes(aggregateType);
        var indexedProps = new HashSet<PropertyInfo>(indexes.SelectMany(i => i.Properties));

        var list = new List<ProjectedProperty>();
        foreach (var p in props)
        {
            if (p == id) continue;
            if (p.GetCustomAttribute<NotMappedAttribute>(inherit: true) is not null) continue;

            var t = p.PropertyType;
            var isEnum = t.IsEnum;
            var isScalar = ScalarTypes.Contains(t) || isEnum;
            if (!isScalar) continue;

            var colName = p.GetCustomAttribute<ColumnAttribute>(inherit: true)?.Name;
            if (string.IsNullOrWhiteSpace(colName))
            {
                colName = p.Name;
            }
            var isIndexed = indexedProps.Contains(p);
            list.Add(new ProjectedProperty(p, colName!, isEnum, isIndexed));
        }
        return list;
    }
}
