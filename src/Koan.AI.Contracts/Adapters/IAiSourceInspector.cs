using Koan.AI.Contracts.Sources;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Optional provider contract for protocol-correct inspection of an unregistered endpoint.
/// </summary>
public interface IAiSourceInspector
{
    Task<AiSourceInspection> InspectAsync(
        AiSourceCandidate candidate,
        CancellationToken ct = default);
}
