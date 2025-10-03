using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Context;
using Koan.Core;
using System.Runtime.CompilerServices;

namespace Koan.AI;

public static class Ai
{
    private static readonly AsyncLocal<IAi?> _override = new();
    private static Func<IServiceProvider, IAi>? _resolver;

    public static IDisposable With(IAi @override)
    {
        var prev = _override.Value;
        _override.Value = @override;
        return new Reset(() => _override.Value = prev);
    }

    // ============================================================================
    // Capability-First API (NEW - ADR-0014)
    // ============================================================================

    /// <summary>
    /// Chat with AI using a simple message. Uses capability-based routing.
    /// </summary>
    /// <param name="message">User message</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public static async Task<string> Chat(string message, CancellationToken ct = default)
    {
        var response = await Resolve().PromptAsync(new AiChatRequest
        {
            Messages = new() { new AiMessage("user", message) }
        }, ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI using detailed options. Uses capability-based routing.
    /// </summary>
    /// <param name="options">Chat options including message, model overrides, temperature, etc.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public static async Task<string> Chat(AiChatOptions options, CancellationToken ct = default)
    {
        var response = await Resolve().PromptAsync(BuildChatRequest(options), ct);
        return response.Text;
    }

    /// <summary>
    /// Stream chat responses from AI. Uses capability-based routing.
    /// </summary>
    /// <param name="message">User message</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of text chunks</returns>
    public static async IAsyncEnumerable<string> Stream(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().StreamAsync(new AiChatRequest
        {
            Messages = new() { new AiMessage("user", message) }
        }, ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    /// <summary>
    /// Stream chat responses from AI with detailed options. Uses capability-based routing.
    /// </summary>
    /// <param name="options">Chat options including message, model overrides, temperature, etc.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of text chunks</returns>
    public static async IAsyncEnumerable<string> Stream(
        AiChatOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().StreamAsync(BuildChatRequest(options), ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    /// <summary>
    /// Generate embeddings for text. Uses capability-based routing.
    /// </summary>
    /// <param name="text">Text to embed</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Embedding vector</returns>
    public static async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        var response = await Resolve().EmbedAsync(new AiEmbeddingsRequest
        {
            Input = new() { text }
        }, ct);
        return response.Vectors.FirstOrDefault() ?? Array.Empty<float>();
    }

    /// <summary>
    /// Generate embeddings with detailed options. Uses capability-based routing.
    /// </summary>
    /// <param name="options">Embed options including text(s), model overrides, etc.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Embedding vectors</returns>
    public static async Task<float[][]> Embed(AiEmbedOptions options, CancellationToken ct = default)
    {
        var texts = options.Texts ?? (options.Text != null ? new[] { options.Text } : Array.Empty<string>());
        if (texts.Length == 0)
            throw new ArgumentException("Either Text or Texts must be provided", nameof(options));

        var response = await Resolve().EmbedAsync(new AiEmbeddingsRequest
        {
            Input = texts.ToList(),
            Model = options.Model
        }, ct);

        return response.Vectors.ToArray();
    }

    /// <summary>
    /// Understand/analyze an image with AI vision. Uses capability-based routing.
    /// </summary>
    /// <param name="imageBytes">Image data</param>
    /// <param name="prompt">Question or instruction about the image</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public static async Task<string> Understand(byte[] imageBytes, string prompt, CancellationToken ct = default)
    {
        // For now, route through chat with a vision-capable model
        // TODO: Add dedicated vision adapter methods with proper multimodal support
        var parts = new List<AiMessagePart>
        {
            new AiMessagePart { Type = "text", Text = prompt },
            new AiMessagePart { Type = "image", Data = imageBytes, MimeType = "image/jpeg" }
        };

        var response = await Resolve().PromptAsync(new AiChatRequest
        {
            Messages = new()
            {
                new AiMessage("user", prompt)
                {
                    Parts = parts
                }
            }
        }, ct);
        return response.Text;
    }

    /// <summary>
    /// Understand/analyze an image with detailed options. Uses capability-based routing.
    /// </summary>
    /// <param name="options">Vision options including image, prompt, model overrides, etc.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public static Task<string> Understand(AiVisionOptions options, CancellationToken ct = default)
    {
        return Understand(options.ImageBytes, options.Prompt, ct);
    }

    /// <summary>
    /// Create a scoped context for AI operations with source/provider/model overrides.
    /// Context flows across async boundaries and can be nested (inner overrides outer).
    /// </summary>
    /// <param name="source">Source or group name to use. Examples: "ollama-primary", "production-ollama"</param>
    /// <param name="provider">Provider type to use. Examples: "ollama", "openai"</param>
    /// <param name="model">Model name to use. Examples: "llama3.2:70b", "gpt-4o"</param>
    /// <returns>Disposable scope that restores previous context when disposed</returns>
    public static AiContextScope Context(
        string? source = null,
        string? provider = null,
        string? model = null)
    {
        return new AiContextScope(source, provider, model);
    }

    // ============================================================================
    // Legacy API (BACKWARD COMPATIBILITY)
    // ============================================================================

    [Obsolete("Use Chat() for capability-based routing. Prompt() will be removed in v1.0")]
    public static Task<AiChatResponse> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Resolve().PromptAsync(BuildChat(message, model, opts), ct);

    [Obsolete("Use Stream() with AiChatOptions for capability-based routing. This overload will be removed in v1.0")]
    public static IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Resolve().StreamAsync(BuildChat(message, model, opts), ct);

    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Resolve().EmbedAsync(req, ct);

    public static AiConversationBuilder Conversation()
        => new(Resolve());

    public static AiConversationBuilder Conversation(this IAi ai)
    {
        if (ai is null) throw new ArgumentNullException(nameof(ai));
        return new AiConversationBuilder(ai);
    }

    // Discovery helpers for optional usage
    public static bool IsAvailable
    {
        get
        {
            if (_override.Value is IAi) return true;
            var sp = Koan.Core.Hosting.App.AppHost.Current;
            if (sp is null) return false;
            var ia = sp.GetService<IAi>();
            if (ia is not null) return true;
            var scopeFactory = sp.GetService<IServiceScopeFactory>();
            if (scopeFactory is null) return false;
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IAi>() is not null;
        }
    }

    public static IAi? TryResolve()
    {
        if (_override.Value is IAi o) return o;
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is null) return null;
        var ia = sp.GetService<IAi>();
        if (ia is not null) return ia;
        var scopeFactory = sp.GetService<IServiceScopeFactory>();
        if (scopeFactory is null) return null;
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IAi>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAi Resolve()
    {
        if (_override.Value is IAi o) return o;
        var sp = Koan.Core.Hosting.App.AppHost.Current ?? throw new InvalidOperationException("AI not configured; call services.AddKoan() or AddAi() and ensure AppHost.Current is set during startup.");
        _resolver ??= CreateResolver(sp);
        return _resolver(sp);
    }

    private static AiChatRequest BuildChat(string message, string? model, AiPromptOptions? opts)
        => new()
        {
            Messages = new() { new AiMessage("user", message) },
            Model = model,
            Options = opts
        };

    private static AiChatRequest BuildChatRequest(AiChatOptions options)
    {
        // Build messages list
        var messages = options.Messages?.ToList() ?? new List<AiMessage>();

        // If no messages but Message is provided, add user message
        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(options.Message))
        {
            messages.Add(new AiMessage("user", options.Message));
        }

        // If SystemPrompt is provided, prepend system message
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            messages.Insert(0, new AiMessage("system", options.SystemPrompt));
        }

        // Build prompt options
        var promptOpts = new AiPromptOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxTokens,
            TopP = options.TopP,
            Stop = options.Stop,
            Seed = options.Seed,
            Think = options.Think
        };

        // Merge context overrides
        var (source, provider, model) = AiContextScope.ResolveMerged(
            options.Source,
            options.Provider,
            options.Model);

        return new AiChatRequest
        {
            Messages = messages,
            Model = model,
            Options = promptOpts,
            Route = source != null || provider != null
                ? new AiRouteHints { AdapterId = source ?? provider }
                : null
        };
    }

    private static Func<IServiceProvider, IAi> CreateResolver(IServiceProvider sp)
    {
        // Cache the delegate per ServiceProvider instance
        return (svc) =>
        {
            var scopeFactory = svc.GetService<IServiceScopeFactory>();
            if (scopeFactory is null)
                throw new InvalidOperationException("ServiceScopeFactory missing; invalid DI container state.");
            // Prefer ambient scope if present (ASP.NET); else create a scope
            var ia = svc.GetService<IAi>();
            if (ia is not null) return ia;
            using var scope = scopeFactory.CreateScope();
            ia = scope.ServiceProvider.GetService<IAi>();
            return ia ?? throw new InvalidOperationException("IAi not registered; call AddKoan() or AddAi().");
        };
    }

    private sealed class Reset : IDisposable
    {
        private readonly Action _onDispose;
        public Reset(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
