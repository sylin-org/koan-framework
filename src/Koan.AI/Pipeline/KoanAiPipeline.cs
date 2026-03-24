using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

/// <summary>
/// Default Koan AI pipeline backed by Microsoft.Extensions.AI abstractions.
/// </summary>
internal sealed class KoanAiPipeline : IAiPipeline
{
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<KoanAiPipeline> _logger;

    public KoanAiPipeline(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<KoanAiPipeline> logger)
    {
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default)
    {
        var messages = ChatMessageMapper.ToChatMessages(request.Messages);
        var options = ChatOptionsMapper.CreateChatOptions(request);

        var response = await _chatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
        var mapped = ChatResponseMapper.ToAiChatResponse(response);

        _logger.LogDebug("Koan AI pipeline prompt complete (model: {Model}, tokens out: {TokensOut})", mapped.Model ?? "unknown", mapped.TokensOut);
        return mapped;
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = ChatMessageMapper.ToChatMessages(request.Messages);
        var options = ChatOptionsMapper.CreateChatOptions(request);

        var index = 0;
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct).ConfigureAwait(false))
        {
            yield return ChatResponseMapper.ToAiChatChunk(update, index);
            index++;
        }

        _logger.LogDebug("Koan AI pipeline streamed {ChunkCount} chunks", index);
    }

    public async Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var options = ChatOptionsMapper.CreateEmbeddingOptions(request);
        var embeddings = await _embeddingGenerator.GenerateAsync(request.Input, options, ct).ConfigureAwait(false);
        var mapped = ChatResponseMapper.ToAiEmbeddingsResponse(embeddings);

        _logger.LogDebug("Koan AI pipeline generated embeddings (model: {Model}, count: {Count})", mapped.Model ?? "unknown", mapped.Vectors.Count);
        return mapped;
    }

    public async Task<string> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
    {
        var request = new AiChatRequest
        {
            Messages = new List<AiMessage>
            {
                new("user", message),
            },
            Model = model,
            Options = opts,
        };

        var response = await Prompt(request, ct).ConfigureAwait(false);
        return response.Text;
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new AiChatRequest
        {
            Messages = new List<AiMessage>
            {
                new("user", message),
            },
            Model = model,
            Options = opts,
        };

        await foreach (var chunk in Stream(request, ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}
