using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;

namespace Koan.AI.Contracts;

/// <summary>
/// Primary ME.AI-backed pipeline surface for conversational and embedding operations.
/// </summary>
public interface IAiPipeline
{
    Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default);

    Task<string> PromptAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> StreamAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default);
}
