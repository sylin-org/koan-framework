using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;

namespace Koan.AI;

// Preferred, semantic facade over Ai. Keeps terse defaults and allows targeted selection.
public static class Engine
{
    public static bool IsAvailable => Ai.IsAvailable;
    public static IAi? Try() => Ai.TryResolve();

    // Default engine
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Ai.Embed(req, ct);

    public static Task<string> Chat(string message, CancellationToken ct = default)
        => Ai.Chat(message, ct);

    public static Task<string> Chat(AiChatOptions options, CancellationToken ct = default)
        => Ai.Chat(options, ct);

    public static IAsyncEnumerable<string> Stream(string message, CancellationToken ct = default)
        => Ai.Stream(message, ct);

    public static IAsyncEnumerable<string> Stream(AiChatOptions options, CancellationToken ct = default)
        => Ai.Stream(options, ct);

    // Targeted selection (provider and/or model)
    public static EngineSelector For(string? provider = null, string? model = null) => new(provider, model);
}