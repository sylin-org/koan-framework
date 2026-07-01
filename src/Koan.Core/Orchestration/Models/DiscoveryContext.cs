using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration;

/// <summary>Environment and configuration context for discovery</summary>
public sealed record DiscoveryContext
{
    public OrchestrationMode OrchestrationMode { get; init; } = OrchestrationMode.Standalone;
    public IConfiguration Configuration { get; init; } = null!;
    public bool RequireHealthValidation { get; init; } = true;
    public TimeSpan HealthCheckTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRetryAttempts { get; init; } = 2;
    public IDictionary<string, object>? Parameters { get; init; }

    /// <summary>
    /// Candidate endpoints contributed by external discovery sources (e.g. Zen Garden), populated by the
    /// coordinator before delegating to the adapter. Folded into the health-checked probe by
    /// <see cref="ServiceDiscoveryAdapterBase.BuildDiscoveryCandidates"/> — informative, never authoritative.
    /// </summary>
    public IReadOnlyList<DiscoveryCandidate>? ContributedCandidates { get; init; }
}