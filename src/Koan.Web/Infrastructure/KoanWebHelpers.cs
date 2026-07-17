using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Core;
using System.Reflection;

namespace Koan.Web.Infrastructure;

internal static class KoanWebHelpers
{
    public static Type[] SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch { return []; }
    }

    public static object GetRepository(IServiceProvider sp, Koan.Data.Core.IDataService data, Type entityType, Type keyType)
    {
        var mi = typeof(Koan.Data.Core.IDataService).GetMethod("GetRepository")!;
        var gm = mi.MakeGenericMethod(entityType, keyType);
        return gm.Invoke(data, null)!;
    }

    public static string ResolveObservedProvider(Type entityType, Type keyType, IServiceProvider sp)
    {
        var entityName = entityType.FullName ?? entityType.Name;
        var keyName = keyType.FullName ?? keyType.Name;
        return sp.GetService<IDataDiagnostics>()?
            .GetEntityConfigsSnapshot()
            .FirstOrDefault(config =>
                string.Equals(config.EntityType, entityName, StringComparison.Ordinal)
                && string.Equals(config.KeyType, keyName, StringComparison.Ordinal))
            ?.Provider
            ?? "unresolved";
    }

    public static string[] EnumFlags<T>(T value) where T : Enum
    {
        var vals = Enum.GetValues(typeof(T)).Cast<Enum>().Where(v => Convert.ToInt64(v) != 0 && value.HasFlag(v)).Select(v => v.ToString()).ToArray();
        return vals.Length == 0 ? [] : vals;
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
