namespace Koan.Data.Core;

internal sealed class DataDiagnostics : IDataDiagnostics
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string EntityType, string KeyType),
        EntityConfigInfo> _configs = new();

    public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot() =>
        _configs.Values
            .OrderBy(info => info.EntityType, StringComparer.Ordinal)
            .ThenBy(info => info.KeyType, StringComparer.Ordinal)
            .ToArray();

    internal void Observe(EntityConfigInfo config) =>
        _configs[(config.EntityType, config.KeyType)] = config;
}
