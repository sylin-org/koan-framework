namespace Koan.Core.Orchestration;

/// <summary>
/// Canonical precedence slots for service-discovery candidates. Lower values are tried first.
/// Adapter-specific discovery code may describe candidates, but it must preserve this shared policy.
/// </summary>
public static class DiscoveryCandidatePriority
{
    /// <summary>Concrete application configuration.</summary>
    public const int ExplicitConfiguration = 0;

    /// <summary>Service-specific environment instructions not already represented in application configuration.</summary>
    public const int Environment = 1;

    /// <summary>Contextual automatic discovery, including Aspire, activated contributors, and local topology.</summary>
    public const int Automatic = 2;

    /// <summary>The container-to-host gateway fallback.</summary>
    public const int HostGateway = 3;

    /// <summary>The final loopback fallback used from a container.</summary>
    public const int LoopbackFallback = 4;
}
