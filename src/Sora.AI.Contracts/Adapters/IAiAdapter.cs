using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sora.AI.Contracts.Models;

namespace Sora.AI.Contracts.Adapters;

/// <summary>
/// An AI provider adapter (e.g., Ollama, OpenAI).
/// Must be stateless and safe for concurrent use.
/// </summary>
public interface IAiAdapter
{
    /// <summary>Stable identifier for the adapter instance (e.g., "ollama:local:11434").</summary>
    string Id { get; }
    /// <summary>Human-friendly name, for logs/observability.</summary>
    string Name { get; }
    /// <summary>Optional provider type (e.g., "ollama", "openai").</summary>
    string Type { get; }

    /// <summary>Return true if the adapter believes it can serve the request (model, limits, etc.).</summary>
    bool CanServe(AiChatRequest request);

    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default);
}
