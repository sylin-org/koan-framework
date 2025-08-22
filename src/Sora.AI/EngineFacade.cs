using Microsoft.Extensions.DependencyInjection;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Models;

namespace Sora.AI;

// Preferred, semantic facade over Ai. Keeps terse defaults and allows targeted selection.
public static class Engine
{
    public static bool IsAvailable => Ai.IsAvailable;
    public static IAi? Try() => Ai.TryResolve();

    // Default engine
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Ai.Embed(req, ct);

    public static Task<AiChatResponse> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Ai.Prompt(message, model, opts, ct);

    public static IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Ai.Stream(message, model, opts, ct);

    // Targeted selection (provider and/or model)
    public static EngineSelector For(string? provider = null, string? model = null) => new(provider, model);
}

public readonly struct EngineSelector
{
    private readonly string? _provider;
    private readonly string? _model;

    internal EngineSelector(string? provider, string? model)
    {
        _provider = provider;
        _model = model;
    }

    // Chat helpers (provider override supported via route hints)
    public Task<string> Prompt(string message, AiPromptOptions? opts = null, CancellationToken ct = default)
    {
        var ai = Ai.TryResolve() ?? throw new InvalidOperationException("AI not available. Configure AddAi() and UseSora().");
        var req = new AiChatRequest
        {
            Messages = new() { new AiMessage("user", message) },
            Model = _model,
            Options = opts,
            Route = string.IsNullOrWhiteSpace(_provider) ? null : new AiRouteHints { AdapterId = _provider }
        };
        return ai.PromptAsync(req, ct).ContinueWith(t => t.Result.Text, ct);
    }

    public IAsyncEnumerable<AiChatChunk> Stream(string message, AiPromptOptions? opts = null, CancellationToken ct = default)
    {
        var ai = Ai.TryResolve() ?? throw new InvalidOperationException("AI not available. Configure AddAi() and UseSora().");
        var req = new AiChatRequest
        {
            Messages = new() { new AiMessage("user", message) },
            Model = _model,
            Options = opts,
            Route = string.IsNullOrWhiteSpace(_provider) ? null : new AiRouteHints { AdapterId = _provider }
        };
        return ai.StreamAsync(req, ct);
    }

    // Embeddings helpers (provider override depends on router; model override applied)
    public Task<AiEmbeddingsResponse> Embed(string input, CancellationToken ct = default)
    {
        var req = new AiEmbeddingsRequest { Input = new() { input }, Model = _model };
        return Ai.Embed(req, ct);
    }

    public Task<AiEmbeddingsResponse> Embed(IEnumerable<string> input, CancellationToken ct = default)
    {
        var req = new AiEmbeddingsRequest { Input = input.ToList(), Model = _model };
        return Ai.Embed(req, ct);
    }

    public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
    {
        // No route on embeddings yet; honor model override if not set
        var effective = req.Model is null && _model is not null
            ? req with { Model = _model }
            : req;
        return Ai.Embed(effective, ct);
    }
}
