using System.Collections.Immutable;

namespace Koan.Core.Orchestration.Composition;

/// <summary>Immutable structural discovery plan compiled once for one host.</summary>
internal sealed class ServiceDiscoveryPlan
{
    internal static ServiceDiscoveryPlan Empty { get; } = new([]);

    internal ServiceDiscoveryPlan(ImmutableArray<DiscoverySourceRegistration> sources)
    {
        Sources = sources;
    }

    internal ImmutableArray<DiscoverySourceRegistration> Sources { get; }
}

internal sealed record DiscoverySourceRegistration(
    string Owner,
    string Id,
    Type SourceType,
    ImmutableArray<string> IntentSchemes);
