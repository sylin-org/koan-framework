using Sora.Data.Abstractions.Annotations;
using System.Collections.Concurrent;
using System.Reflection;

namespace Sora.Data.Core.Metadata;

public static class AggregateMetadata
{
    public sealed record IdSpec(PropertyInfo Prop, bool IsString, bool IsGuid);

    private static readonly ConcurrentDictionary<Type, IdSpec?> IdCache = new();

    public static IdSpec? GetIdSpec(Type aggregateType)
    {
        return IdCache.GetOrAdd(aggregateType, static t =>
        {
            var prop = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => p.GetCustomAttribute<IdentifierAttribute>() is not null)
                ?? t.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
            if (prop is null) return null;
            var pt = prop.PropertyType;
            return new IdSpec(prop, pt == typeof(string), pt == typeof(Guid));
        });
    }

    public static object? GetIdValue(object aggregate)
    {
        if (aggregate is null) return null;
        var spec = GetIdSpec(aggregate.GetType());
        return spec?.Prop.GetValue(aggregate);
    }
}
