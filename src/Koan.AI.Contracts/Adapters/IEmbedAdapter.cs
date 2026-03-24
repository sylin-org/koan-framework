using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Embedding/vector-generation adapter. Providers that support text-to-vector operations implement this interface.
/// </summary>
public interface IEmbedAdapter : IAiAdapter
{
    /// <summary>Generate embeddings for the given input texts.</summary>
    Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default);
}
