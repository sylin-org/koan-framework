using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

internal sealed class AdapterBackedChatClient : IChatClient
{
    private readonly AiRoutingEngine _routing;
    private readonly ILogger<AdapterBackedChatClient> _logger;

    public AdapterBackedChatClient(AiRoutingEngine routing, ILogger<AdapterBackedChatClient> logger)
    {
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (messages is null) throw new ArgumentNullException(nameof(messages));

        var request = ChatOptionsMapper.CreateChatRequest(messages, options);
        var resolution = _routing.ResolveChat(request);

        request.InternalConnectionString = resolution.Member.ConnectionString;
        if (!string.IsNullOrWhiteSpace(resolution.EffectiveModel))
        {
            request = request with { Model = resolution.EffectiveModel };
        }

        var response = await resolution.Adapter.ChatAsync(request, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "AI prompt served by adapter {Adapter} ({Capability}) via {Source}/{Member}",
            resolution.Adapter.Id,
            resolution.Capability,
            resolution.Source.Name,
            resolution.Member.Name);

        return ChatResponseMapper.FromAiChatResponse(response with { AdapterId = resolution.Adapter.Id });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages is null) throw new ArgumentNullException(nameof(messages));

        var request = ChatOptionsMapper.CreateChatRequest(messages, options);
        var resolution = _routing.ResolveChat(request);

        request.InternalConnectionString = resolution.Member.ConnectionString;
        if (!string.IsNullOrWhiteSpace(resolution.EffectiveModel))
        {
            request = request with { Model = resolution.EffectiveModel };
        }

        var index = 0;
        await foreach (var chunk in resolution.Adapter.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug(
                "Streaming chunk {ChunkIndex} from adapter {Adapter}",
                index++,
                resolution.Adapter.Id);
            yield return ChatResponseMapper.FromAiChatChunk(chunk with { AdapterId = resolution.Adapter.Id });
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // No managed resources.
    }
}
