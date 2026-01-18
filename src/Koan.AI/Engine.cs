using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;

namespace Koan.AI;

// Preferred, semantic facade over Ai. Keeps terse defaults and allows targeted selection.
public static class Engine
{
    public static bool IsAvailable => Client.IsAvailable;
    public static IAiPipeline? Try() => Client.TryResolve();

    // Default engine
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Client.Embed(req, ct);

    public static Task<string> Chat(string message, CancellationToken ct = default)
        => Client.Chat(message, ct);

    public static Task<string> Chat(AiChatOptions options, CancellationToken ct = default)
        => Client.Chat(options, ct);

    public static IAsyncEnumerable<string> Stream(string message, CancellationToken ct = default)
        => Client.Stream(message, ct);

    public static IAsyncEnumerable<string> Stream(AiChatOptions options, CancellationToken ct = default)
        => Client.Stream(options, ct);

    // Targeted selection (provider and/or model)
    public static EngineSelector For(string? provider = null, string? model = null) => new(provider, model);
}