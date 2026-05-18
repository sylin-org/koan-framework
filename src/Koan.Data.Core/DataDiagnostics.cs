namespace Koan.Data.Core;

internal sealed class DataDiagnostics : IDataDiagnostics
{
    public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot()
    {
        // Reflect on AggregateConfigs.Cache (still the canonical discovery point for entities
        // that have been resolved through Data<T,K>).
        var list = new List<EntityConfigInfo>();
        var cacheField = typeof(AggregateConfigs).GetField("Cache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (cacheField?.GetValue(null) is System.Collections.IDictionary dict)
        {
            foreach (System.Collections.DictionaryEntry de in dict)
            {
                var key = ((Type, Type))de.Key!;
                var cfg = de.Value!;
                var providerProp = cfg.GetType().GetProperty("Provider");
                var idProp = cfg.GetType().GetProperty("Id");
                var provider = providerProp?.GetValue(cfg) as string ?? "";
                var idSpec = idProp?.GetValue(cfg);
                var idName = idSpec?.GetType().GetProperty("Prop")?.GetValue(idSpec)?.GetType().GetProperty("Name")?.GetValue(idSpec)?.ToString();
                list.Add(new EntityConfigInfo(key.Item1.FullName!, key.Item2.FullName!, provider, idName));
            }
        }
        return list;
    }
}
