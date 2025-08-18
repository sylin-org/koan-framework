using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core.Configuration;

namespace Sora.Data.Core;

public sealed record EntityConfigInfo(
    string EntityType,
    string KeyType,
    string Provider,
    string? IdProperty,
    IReadOnlyList<(string Key, string Type)> Bags
);

public interface IDataDiagnostics
{
    // Returns a snapshot of known entity configurations in this ServiceProvider.
    IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot();
}

internal sealed class DataDiagnostics : IDataDiagnostics
{
    public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot()
    {
        // We don’t have a public registry of all entities; inspect the constructed repos from IDataService’s cache if available.
        // Fallback: scan AggregateConfigs cache via reflection.
        var list = new List<EntityConfigInfo>();
        var aggType = typeof(AggregateConfigs);
        var cacheField = aggType.GetField("Cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (cacheField?.GetValue(null) is System.Collections.IDictionary dict)
        {
            foreach (System.Collections.DictionaryEntry de in dict)
            {
                var key = ((Type, Type))de.Key!;
                var cfg = de.Value;
                var providerProp = cfg!.GetType().GetProperty("Provider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var idProp = cfg!.GetType().GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var enumerateBags = cfg!.GetType().GetMethod("EnumerateBags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var provider = providerProp?.GetValue(cfg) as string ?? "";
                var idSpec = idProp?.GetValue(cfg);
                var idName = idSpec?.GetType().GetProperty("Prop")?.GetValue(idSpec)?.GetType().GetProperty("Name")?.GetValue(idSpec)?.ToString();
                var bags = new List<(string Key, string Type)>();
                if (enumerateBags is not null)
                {
                    var entries = (System.Collections.IEnumerable)enumerateBags.Invoke(cfg, Array.Empty<object>())!;
                    foreach (var entry in entries)
                    {
                        var et = entry.GetType();
                        var keyVal = et.GetField("Item1")?.GetValue(entry)?.ToString() ?? et.GetProperty("key")?.GetValue(entry)?.ToString() ?? "";
                        var valObj = et.GetField("Item2")?.GetValue(entry) ?? et.GetProperty("value")?.GetValue(entry);
                        bags.Add((keyVal, valObj?.GetType().FullName ?? "null"));
                    }
                }
                list.Add(new EntityConfigInfo(key.Item1.FullName!, key.Item2.FullName!, provider, idName, bags));
            }
        }
        return list;
    }
}
