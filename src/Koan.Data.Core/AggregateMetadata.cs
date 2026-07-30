using Koan.Data.Abstractions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Koan.Data.Core;

public static class AggregateMetadata
{
    public sealed record IdSpec(PropertyInfo Prop, bool IsString, bool IsGuid);

    private static readonly ConditionalWeakTable<Type, Metadata> IdCache = new();

    public static IdSpec? GetIdSpec(Type aggregateType)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        return IdCache.GetValue(aggregateType, static t => new Metadata(Compute(t))).Id;
    }

    private static IdSpec? Compute(Type aggregateType)
    {
        var prop = aggregateType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => p.GetCustomAttribute<IdentifierAttribute>() is not null)
            ?? aggregateType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        if (prop is null) return null;
        var propertyType = prop.PropertyType;
        return new IdSpec(prop, propertyType == typeof(string), propertyType == typeof(Guid));
    }

    public static object? GetIdValue(object aggregate)
    {
        if (aggregate is null) return null;
        var spec = GetIdSpec(aggregate.GetType());
        return spec?.Prop.GetValue(aggregate);
    }

    private sealed record Metadata(IdSpec? Id);
}
