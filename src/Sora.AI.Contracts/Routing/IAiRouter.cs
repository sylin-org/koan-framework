using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sora.AI.Contracts.Models;

namespace Sora.AI.Contracts.Routing;

public interface IAiRouter
{
    Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default);
}
