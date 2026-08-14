using Koan.Core.Providers;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Routing;

/// <summary>Redacted host truth for the currently active logical default Data route.</summary>
public sealed record DefaultDataRouteSnapshot
{
    internal DefaultDataRouteSnapshot(
        DataSourcePlan plan,
        long authorityRevision,
        long contentGeneration,
        DateTimeOffset activatedAt,
        ProviderSelectionReceipt selectionReceipt,
        IReadOnlySet<string> quarantinedRouteIdentities,
        IReadOnlyDictionary<string, long> contentGenerations)
    {
        Plan = plan;
        Source = plan.Source;
        Adapter = plan.Adapter;
        RouteIdentity = plan.RouteIdentity;
        ConnectionIdentity = plan.ConnectionIdentity;
        AuthorityRevision = authorityRevision;
        ContentGeneration = contentGeneration;
        ActivatedAt = activatedAt;
        SelectionReceipt = selectionReceipt;
        QuarantinedRouteIdentities = quarantinedRouteIdentities;
        ContentGenerations = contentGenerations;
    }

    internal DataSourcePlan Plan { get; init; }
    public string Source { get; init; }
    public string Adapter { get; init; }
    public string RouteIdentity { get; init; }
    public string ConnectionIdentity { get; init; }
    public long AuthorityRevision { get; init; }
    public long ContentGeneration { get; init; }
    public DateTimeOffset ActivatedAt { get; init; }
    public ProviderSelectionReceipt SelectionReceipt { get; init; }
    public IReadOnlySet<string> QuarantinedRouteIdentities { get; init; }
    public IReadOnlyDictionary<string, long> ContentGenerations { get; init; }
}
