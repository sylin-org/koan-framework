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
    /// <summary>
    /// Semantic capabilities the resolved service must provide. Layered candidate sources may use these
    /// requirements during resolution; the selected adapter remains responsible for endpoint health.
    /// </summary>
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];
    public IDictionary<string, object>? Parameters { get; init; }

    internal IReadOnlyList<DiscoveryCandidate>? PlannedCandidates { get; init; }
    internal DiscoveryCandidateMode PlannedCandidateMode { get; init; }
}

internal enum DiscoveryCandidateMode
{
    Automatic,
    Required
}
