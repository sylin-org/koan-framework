using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Reranking adapter (query + documents → scored documents). Protocol category.
/// Providers: Infinity, Cohere, cross-encoders, FlashRank.
/// </summary>
public interface IRerankAdapter : IAiAdapter
{
    /// <summary>Rerank documents by relevance to a query.</summary>
    Task<RerankResponse> Rerank(RerankRequest request, CancellationToken ct = default);
}
