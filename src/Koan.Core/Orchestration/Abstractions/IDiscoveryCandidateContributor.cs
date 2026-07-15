using Koan.Core.Orchestration;

namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Contributes candidate endpoints into a service's discovery probe from an EXTERNAL discovery source
/// (e.g. Zen Garden / Koi). Contributed candidates are folded into <see cref="ServiceDiscoveryAdapterBase"/>'s
/// health-checked candidate list — tried ahead of the compose/host/local guesses but behind concrete
/// configuration and legacy environment hints — so a present-but-unreachable answer (say, a same-host offering advertised on an interface
/// the app can't reach) simply fails its health probe and the probe falls through to the next candidate.
/// <para>The contract is deliberately "inform, never short-circuit": a contributor never returns an
/// authoritative connection that bypasses the health check. It is registered via
/// <c>TryAddEnumerable</c> and is therefore absent when its module is not referenced — "Koi contributes
/// IF PRESENT", not "Koi decides".</para>
/// <para>Candidate priority uses <see cref="DiscoveryCandidatePriority"/>. Concrete explicit configuration is
/// authoritative, followed by legacy environment instructions and contextual automatic discovery. Aspire,
/// activated contributors, and runtime topology share the automatic slot in that order. Host-gateway and
/// loopback candidates are bounded fallbacks. The shared adapter pipeline normalizes every contributed
/// candidate to <see cref="DiscoveryCandidatePriority.Automatic"/> so an optional engine cannot outrank
/// explicit application intent. Contributed candidates are inserted before runtime topology, so the stable
/// sort tries them first within that shared slot.</para>
/// </summary>
public interface IDiscoveryCandidateContributor
{
    /// <summary>The recommended priority for a contributed candidate (see the ladder in the type remarks).</summary>
    public const int RecommendedPriority = DiscoveryCandidatePriority.Automatic;

    /// <summary>
    /// Return zero or more candidate endpoints for <paramref name="serviceName"/> (e.g. "mongo", "weaviate").
    /// Best-effort: a contributor that cannot resolve returns an empty sequence and must not throw to the caller
    /// (the coordinator guards it, but a well-behaved contributor swallows its own transient failures).
    /// </summary>
    Task<IReadOnlyList<DiscoveryCandidate>> ContributeCandidates(
        string serviceName,
        DiscoveryContext context,
        CancellationToken cancellationToken = default);
}
