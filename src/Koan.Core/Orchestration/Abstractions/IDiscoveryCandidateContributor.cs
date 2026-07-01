using Koan.Core.Orchestration;

namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Contributes candidate endpoints into a service's discovery probe from an EXTERNAL discovery source
/// (e.g. Zen Garden / Koi). Contributed candidates are folded into <see cref="ServiceDiscoveryAdapterBase"/>'s
/// health-checked candidate list — tried ahead of the compose/host/local guesses but behind explicit
/// env/config — so a present-but-unreachable answer (say, a same-host offering advertised on an interface
/// the app can't reach) simply fails its health probe and the probe falls through to the next candidate.
/// <para>The contract is deliberately "inform, never short-circuit": a contributor never returns an
/// authoritative connection that bypasses the health check. It is registered via
/// <c>TryAddEnumerable</c> and is therefore absent when its module is not referenced — "Koi contributes
/// IF PRESENT", not "Koi decides".</para>
/// <para>Candidate priority ladder (lower = tried first) used by
/// <see cref="ServiceDiscoveryAdapterBase.BuildDiscoveryCandidates"/>: <c>0</c> = service env vars ·
/// <c>1</c> = explicit config / Aspire · <c>2</c> = external contributors (this seam) AND the compose
/// container-instance guess · <c>3</c> = <c>host.docker.internal</c> · <c>4</c> = <c>localhost</c>. A
/// contributor should emit priority <c>2</c> to sit ahead of the compose/host/local guesses but behind
/// explicit config; contributed candidates are inserted before the container-instance guess, so the stable
/// sort tries them first within the shared slot.</para>
/// </summary>
public interface IDiscoveryCandidateContributor
{
    /// <summary>The recommended priority for a contributed candidate (see the ladder in the type remarks).</summary>
    public const int RecommendedPriority = 2;

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
