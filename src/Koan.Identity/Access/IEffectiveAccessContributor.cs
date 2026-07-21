using Koan.Core;

namespace Koan.Identity.Access;

/// <summary>
/// SEC-0007 Layer 2 — a discovered contributor to an identity's effective access. The contributor-pipeline canon:
/// access is composed from registered contributors, never bespoke per-source logic. Koan.Identity ships the
/// global-role (<see cref="IdentityRole"/>) and agent-grant contributors; adding <c>Koan.Tenancy</c> lights up a
/// Membership contributor over the SAME resolver — graceful degradation, no code-path fork.
/// </summary>
[KoanDiscoverable]
public interface IEffectiveAccessContributor
{
    /// <summary>Contribute the access facts this source confers on <paramref name="identityId"/>.</summary>
    Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default);
}
