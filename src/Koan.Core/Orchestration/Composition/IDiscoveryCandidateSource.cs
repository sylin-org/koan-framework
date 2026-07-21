using System.ComponentModel;

namespace Koan.Core.Orchestration.Composition;

/// <summary>
/// Supplies live candidates for a structurally compiled service-discovery layer.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IDiscoveryCandidateSource
{
    /// <summary>
    /// Queries current topology for the selected adapter. Structural composition has already completed.
    /// </summary>
    Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken = default);
}
