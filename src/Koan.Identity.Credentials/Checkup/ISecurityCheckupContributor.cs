using Koan.Core;

namespace Koan.Identity.Credentials.Checkup;

/// <summary>
/// SEC-0007 P3-grp4 — a discovered contributor to a person's Security Checkup. Each factor package contributes its
/// own signal (password set? MFA on? recovery configured?) over the SAME read-model — the contributor-pipeline canon,
/// graceful degradation: only the referenced factors contribute, so the Checkup grows richer as factors are added.
/// </summary>
[KoanDiscoverable]
public interface ISecurityCheckupContributor
{
    /// <summary>The posture signals this source contributes for <paramref name="identityId"/>.</summary>
    Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default);
}
