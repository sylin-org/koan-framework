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
    Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default);

    Task<string> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default);
}
