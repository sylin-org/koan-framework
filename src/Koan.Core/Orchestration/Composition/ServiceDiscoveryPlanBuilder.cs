using System.Collections.Immutable;
using Koan.Core.Semantics;

namespace Koan.Core.Orchestration.Composition;

/// <summary>Stages owner-bound source declarations and publishes one validated immutable plan.</summary>
internal sealed class ServiceDiscoveryPlanBuilder
{
    private readonly List<PendingSource> _sources = [];
    private readonly Dictionary<SemanticId, int> _ownerOrder = [];
    private ServiceDiscoveryPlan? _compiled;

    internal DiscoveryContributionTarget ForOwner(string owner)
    {
        EnsureMutable();
        var ownerId = new SemanticId(owner);
        if (!_ownerOrder.ContainsKey(ownerId))
        {
            _ownerOrder.Add(ownerId, _ownerOrder.Count);
        }

        return new DiscoveryContributionTarget(this, ownerId.Value);
    }

    internal void AddSource(string owner, string id, Type sourceType, IEnumerable<string>? intentSchemes)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(sourceType);
        if (!typeof(IDiscoveryCandidateSource).IsAssignableFrom(sourceType))
        {
            throw new ArgumentException(
                $"Discovery source '{sourceType.FullName}' must implement {nameof(IDiscoveryCandidateSource)}.",
                nameof(sourceType));
        }

        var ownerId = new SemanticId(owner);
        if (!_ownerOrder.TryGetValue(ownerId, out var order))
        {
            throw new InvalidOperationException(
                $"Discovery contribution owner '{ownerId}' was not bound through {nameof(ForOwner)}.");
        }

        var sourceId = new SemanticId(id);
        var schemes = (intentSchemes ?? [])
            .Select(NormalizeScheme)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        _sources.Add(new PendingSource(ownerId, order, sourceId, sourceType, schemes));
    }

    internal ServiceDiscoveryPlan Build()
    {
        if (_compiled is not null) return _compiled;

        var duplicateId = _sources
            .GroupBy(static source => source.Id, SemanticIdEqualityComparer.Instance)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            var owners = string.Join(", ", duplicateId.Select(static source => source.Owner.Value));
            throw new InvalidOperationException(
                $"Discovery source id '{duplicateId.Key}' is declared more than once by: {owners}. " +
                "Give every source one stable host-wide id.");
        }

        var schemeOwners = new Dictionary<string, PendingSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            foreach (var scheme in source.IntentSchemes)
            {
                if (schemeOwners.TryGetValue(scheme, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Discovery intent scheme '{scheme}' is claimed by both '{existing.Id}' and '{source.Id}'. " +
                        "One scheme must select exactly one source.");
                }

                schemeOwners.Add(scheme, source);
            }
        }

        var registrations = _sources
            .OrderBy(static source => source.OwnerOrder)
            .ThenBy(static source => source.Id.Value, StringComparer.Ordinal)
            .Select(static source => new DiscoverySourceRegistration(
                source.Owner.Value,
                source.Id.Value,
                source.SourceType,
                source.IntentSchemes))
            .ToImmutableArray();

        _compiled = registrations.Length == 0
            ? ServiceDiscoveryPlan.Empty
            : new ServiceDiscoveryPlan(registrations);
        return _compiled;
    }

    private static string NormalizeScheme(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var scheme = value.Trim().ToLowerInvariant();
        if (!Uri.CheckSchemeName(scheme))
        {
            throw new ArgumentException(
                $"Discovery intent scheme '{value}' is invalid. Use a URI scheme such as 'zen-garden'.",
                nameof(value));
        }

        return scheme;
    }

    private void EnsureMutable()
    {
        if (_compiled is not null)
        {
            throw new InvalidOperationException(
                "The service-discovery plan is already compiled and cannot accept more sources.");
        }
    }

    private sealed record PendingSource(
        SemanticId Owner,
        int OwnerOrder,
        SemanticId Id,
        Type SourceType,
        ImmutableArray<string> IntentSchemes);

    private sealed class SemanticIdEqualityComparer : IEqualityComparer<SemanticId>
    {
        internal static SemanticIdEqualityComparer Instance { get; } = new();

        public bool Equals(SemanticId x, SemanticId y) => x == y;

        public int GetHashCode(SemanticId obj) => obj.GetHashCode();
    }
}
