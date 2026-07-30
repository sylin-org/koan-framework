using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core;

internal sealed class DataDiagnostics : IDataDiagnostics
{
    private readonly object _gate = new();
    private readonly IEnumerable<Lifecycle.IEntityLifecyclePlan> _lifecyclePlans;
    private readonly int _limit;
    private readonly Dictionary<(string EntityType, string KeyType), EntityConfigInfo> _configs = new();
    private readonly Dictionary<ParticipationKey, DataAdapterParticipationInfo> _participations =
        new(ParticipationKeyComparer.Instance);
    private readonly Dictionary<string, DataSourcePlanInfo> _sourcePlans = new(StringComparer.Ordinal);

    public DataDiagnostics(
        IEnumerable<Lifecycle.IEntityLifecyclePlan> lifecyclePlans,
        IOptions<DataRuntimeOptions>? options = null)
    {
        _lifecyclePlans = lifecyclePlans;
        _limit = options?.Value.DiagnosticEntries ?? Infrastructure.Constants.Defaults.DiagnosticSourceEntries;
        if (_limit <= 0) throw new ArgumentOutOfRangeException(nameof(options), "DiagnosticEntries must be positive.");
    }

    public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot()
    {
        lock (_gate)
            return _configs.Values.OrderBy(static info => info.EntityType, StringComparer.Ordinal)
                .ThenBy(static info => info.KeyType, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<DataAdapterParticipationInfo> GetAdapterParticipationsSnapshot()
    {
        lock (_gate)
            return _participations.Values.OrderBy(static info => info.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static info => info.Source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<DataSourcePlanInfo> GetSourcePlansSnapshot()
    {
        lock (_gate)
            return _sourcePlans.Values.OrderBy(static info => info.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static info => info.Adapter, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static info => info.RouteIdentity, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<Lifecycle.EntityLifecycleInfo> GetLifecyclePlansSnapshot() =>
        _lifecyclePlans.Select(plan =>
            {
                plan.Freeze();
                return new Lifecycle.EntityLifecycleInfo(
                    plan.EntityType.FullName ?? plan.EntityType.Name,
                    plan.HandlerCounts);
            })
            .OrderBy(static info => info.EntityType, StringComparer.Ordinal)
            .ToArray();

    internal void Observe(EntityConfigInfo config)
    {
        lock (_gate)
        {
            var key = (config.EntityType, config.KeyType);
            if (_configs.ContainsKey(key) || _configs.Count < _limit) _configs[key] = config;
        }
    }

    internal void ObserveParticipation(string provider, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var normalizedProvider = provider.Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source.Trim();
        var key = new ParticipationKey(normalizedProvider, normalizedSource);
        lock (_gate)
        {
            if (_participations.ContainsKey(key) || _participations.Count < _limit)
                _participations[key] = new DataAdapterParticipationInfo(normalizedProvider, normalizedSource);
        }
    }

    internal void ObserveSourcePlan(DataSourcePlan plan, DataClaimSet? claims = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            _sourcePlans.TryGetValue(plan.RouteIdentity, out var existing);
            if (existing is null && _sourcePlans.Count >= _limit) return;
            var claimReferences = claims?.Claims.Select(static claim => claim.Reference).ToArray()
                ?? existing?.ClaimReferences ?? [];
            var capabilities = claims?.Capabilities ?? existing?.Capabilities ?? [];
            _sourcePlans[plan.RouteIdentity] = new DataSourcePlanInfo(
                plan.Source,
                plan.Adapter,
                plan.StorageLifecycle,
                plan.Access,
                plan.RouteIdentity,
                plan.ReadLanes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                claimReferences,
                capabilities);
        }
    }

    private readonly record struct ParticipationKey(string Provider, string Source);

    private sealed class ParticipationKeyComparer : IEqualityComparer<ParticipationKey>
    {
        public static ParticipationKeyComparer Instance { get; } = new();

        public bool Equals(ParticipationKey x, ParticipationKey y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Provider, y.Provider) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Source, y.Source);

        public int GetHashCode(ParticipationKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Source));
    }
}
