using Koan.Canon.Attributes;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Koan.Canon.Infrastructure;

public static class AdapterIdentity
{
    public static (string System, string Adapter)? FromType(Type t)
    {
        if (t is null) return null;
        var attr = t.GetCustomAttribute<CanonAdapterAttribute>(inherit: true);
        if (attr is null) return null;
        return (attr.System, attr.Adapter);
    }

    public static void Stamp(IDictionary<string, object?> bag, Type hostType)
    {
        if (bag is null) return;
        var meta = FromType(hostType);
        if (meta is null) return;
        if (!bag.ContainsKey(Constants.Envelope.System)) bag[Constants.Envelope.System] = meta.Value.System;
        if (!bag.ContainsKey(Constants.Envelope.Adapter)) bag[Constants.Envelope.Adapter] = meta.Value.Adapter;
    }
}


