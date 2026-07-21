namespace Koan.Data.Core;

internal sealed class DataDiagnostics(IEnumerable<Lifecycle.IEntityLifecyclePlan> lifecyclePlans) : IDataDiagnostics
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string EntityType, string KeyType),
        EntityConfigInfo> _configs = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        ParticipationKey,
        DataAdapterParticipationInfo> _participations = new(ParticipationKeyComparer.Instance);

    public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot() =>
        _configs.Values
            .OrderBy(info => info.EntityType, StringComparer.Ordinal)
            .ThenBy(info => info.KeyType, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<DataAdapterParticipationInfo> GetAdapterParticipationsSnapshot() =>
        _participations.Values
            .OrderBy(info => info.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<Lifecycle.EntityLifecycleInfo> GetLifecyclePlansSnapshot() =>
        lifecyclePlans
            .Select(plan =>
            {
                plan.Freeze();
                return new Lifecycle.EntityLifecycleInfo(
                    plan.EntityType.FullName ?? plan.EntityType.Name,
                    plan.HandlerCounts);
            })
            .OrderBy(info => info.EntityType, StringComparer.Ordinal)
            .ToArray();

    internal void Observe(EntityConfigInfo config) =>
        _configs[(config.EntityType, config.KeyType)] = config;

    internal void ObserveParticipation(string provider, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var normalizedProvider = provider.Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source.Trim();
        var info = new DataAdapterParticipationInfo(normalizedProvider, normalizedSource);
        _participations.TryAdd(new ParticipationKey(normalizedProvider, normalizedSource), info);
    }

    private readonly record struct ParticipationKey(string Provider, string Source);

    private sealed class ParticipationKeyComparer : IEqualityComparer<ParticipationKey>
    {
        public static ParticipationKeyComparer Instance { get; } = new();

        public bool Equals(ParticipationKey x, ParticipationKey y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Provider, y.Provider)
               && StringComparer.OrdinalIgnoreCase.Equals(x.Source, y.Source);

        public int GetHashCode(ParticipationKey value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Source));
    }
}
