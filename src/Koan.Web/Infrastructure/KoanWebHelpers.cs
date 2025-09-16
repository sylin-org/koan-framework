using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using System.Reflection;

namespace Koan.Web.Infrastructure;

internal static class KoanWebHelpers
{
    public static Type[] SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    public static object GetRepository(IServiceProvider sp, Koan.Data.Core.IDataService data, Type entityType, Type keyType)
    {
        var mi = typeof(Koan.Data.Core.IDataService).GetMethod("GetRepository")!;
        var gm = mi.MakeGenericMethod(entityType, keyType);
        return gm.Invoke(data, null)!;
    }

    public static string ResolveProvider(Type entityType, IServiceProvider sp)
    {
        var attr = entityType.GetCustomAttribute<DataAdapterAttribute>();
        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Provider)) return attr.Provider!;
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
        if (factories.Count == 0) return "json";
        var rankedName = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), inherit: false).FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .First().Factory.GetType().Name;
        const string suffix = "AdapterFactory";
        if (rankedName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) rankedName = rankedName[..^suffix.Length];
        return rankedName.ToLowerInvariant();
    }

    public static string[] EnumFlags<T>(T value) where T : Enum
    {
        var vals = Enum.GetValues(typeof(T)).Cast<Enum>().Where(v => Convert.ToInt64(v) != 0 && value.HasFlag(v)).Select(v => v.ToString()).ToArray();
        return vals.Length == 0 ? Array.Empty<string>() : vals;
    }

    public static string ToKeyName(Type keyType)
    {
        if (keyType == typeof(string)) return "string";
        if (keyType == typeof(Guid)) return "guid";
        if (keyType == typeof(int)) return "int";
        if (keyType == typeof(long)) return "long";
        return keyType.Name;
    }
}
