using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Chat/text-generation adapter. Providers that support conversational AI implement this interface.
/// </summary>
public interface IChatAdapter : IAiAdapter
{
    /// <summary>Return true if the adapter believes it can serve the request (model, limits, etc.).</summary>
    bool CanServe(AiChatRequest request);

    /// <summary>Send a chat request and return the complete response.</summary>
    Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default);

    /// <summary>Stream a chat response token-by-token.</summary>
    IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, CancellationToken ct = default);
}
