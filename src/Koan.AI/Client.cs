using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Context;
using Koan.Core;

namespace Koan.AI;

/// <summary>
/// Static client for Koan AI operations.
/// Single facade for Chat, Embed, OCR, and Streaming with category-aware routing.
/// </summary>
public static class Client
{
    private static readonly AsyncLocal<IAiPipeline?> _override = new();
    private static Func<IServiceProvider, IAiPipeline>? _resolver;

    /// <summary>
    /// Override the AI pipeline for the current async context (useful for testing).
    /// </summary>
    public static IDisposable With(IAiPipeline @override)
    {
        var prev = _override.Value;
        _override.Value = @override;
        return new Reset(() => _override.Value = prev);
    }

    // ========================================================================
    // Chat
    // ========================================================================

    /// <summary>
    /// Chat with AI using a simple message.
    /// </summary>
    public static async Task<string> Chat(string message, CancellationToken ct = default)
    {
        var response = await Resolve().Prompt(BuildChatRequest(message, null), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI using detailed options.
    /// </summary>
    public static async Task<string> Chat(string message, ChatOptions options, CancellationToken ct = default)
    {
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI and return a rich result with metadata.
    /// </summary>
    public static async Task<ChatResult> ChatResult(string message, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await Resolve().Prompt(BuildChatRequest(message, null), ct);
        sw.Stop();

        return new ChatResult
        {
            Text = response.Text,
            Model = response.Model,
            TokensIn = response.TokensIn,
            TokensOut = response.TokensOut,
            TokensUsed = (response.TokensIn ?? 0) + (response.TokensOut ?? 0),
            Latency = sw.Elapsed,
            AdapterId = response.AdapterId,
            FinishReason = response.FinishReason
        };
    }

    /// <summary>
    /// Chat with AI using detailed options and return a rich result with metadata.
    /// </summary>
    public static async Task<ChatResult> ChatResult(string message, ChatOptions options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        sw.Stop();

        return new ChatResult
        {
            Text = response.Text,
            Model = response.Model,
            TokensIn = response.TokensIn,
            TokensOut = response.TokensOut,
            TokensUsed = (response.TokensIn ?? 0) + (response.TokensOut ?? 0),
            Latency = sw.Elapsed,
            AdapterId = response.AdapterId,
            FinishReason = response.FinishReason
        };
    }

    // ========================================================================
    // Chat with Prompt
    // ========================================================================

    /// <summary>
    /// Chat with AI using a Prompt and variables for resolution.
    /// </summary>
    public static async Task<string> Chat(
        Prompt.Prompt prompt, object? variables = null, CancellationToken ct = default)
    {
        var message = prompt.Resolve(variables);
        var options = BuildOptionsFromPrompt(prompt);
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return response.Text;
    }

    /// <summary>
    /// Chat with AI using a Prompt and return a typed, parsed response.
    /// JSON schema constraint is sent to the model from Prompt.OutputFormat.
    /// </summary>
    public static async Task<T> Chat<T>(
        Prompt.Prompt prompt, object? variables = null, CancellationToken ct = default)
    {
        var message = prompt.Resolve(variables);
        var options = BuildOptionsFromPrompt(prompt);

        // Add JSON response format constraint if OutputSpec has a schema
        if (prompt.OutputFormat?.JsonSchema is not null)
        {
            options = options with { ResponseFormat = "json_object" };
        }

        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Text)
            ?? throw new InvalidOperationException($"Failed to parse AI response as {typeof(T).Name}");
    }

    /// <summary>
    /// Chat with AI using a Prompt and return a typed, parsed response.
    /// Convenience overload: uses the prompt's raw text as the message.
    /// </summary>
    public static async Task<T> Chat<T>(string message, CancellationToken ct = default)
    {
        var options = new ChatOptions { ResponseFormat = "json_object" };
        var response = await Resolve().Prompt(BuildChatRequest(message, options), ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Text)
            ?? throw new InvalidOperationException($"Failed to parse AI response as {typeof(T).Name}");
    }

    private static ChatOptions BuildOptionsFromPrompt(Prompt.Prompt prompt)
    {
        return new ChatOptions
        {
            SystemPrompt = prompt.System
        };
    }

    // ========================================================================
    // Stream
    // ========================================================================

    /// <summary>
    /// Stream chat responses from AI token-by-token.
    /// </summary>
    public static async IAsyncEnumerable<string> Stream(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().Stream(BuildChatRequest(message, null), ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    /// <summary>
    /// Stream chat responses from AI with detailed options.
    /// </summary>
    public static async IAsyncEnumerable<string> Stream(
        string message,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in Resolve().Stream(BuildChatRequest(message, options), ct))
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                yield return chunk.DeltaText;
        }
    }

    // ========================================================================
    // Embed
    // ========================================================================

    /// <summary>
    /// Generate an embedding vector for text.
    /// </summary>
    public static async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = new() { text }
        }, ct);
        return response.Vectors.FirstOrDefault() ?? Array.Empty<float>();
    }

    /// <summary>
    /// Generate an embedding vector for text with options.
    /// </summary>
    public static async Task<float[]> Embed(string text, EmbedOptions options, CancellationToken ct = default)
    {
        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = new() { text },
            Model = options.Model
        }, ct);
        return response.Vectors.FirstOrDefault() ?? Array.Empty<float>();
    }

    /// <summary>
    /// Generate embeddings for multiple texts in a single batch.
    /// </summary>
    public static async Task<float[][]> EmbedBatch(string[] texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Length == 0)
            throw new ArgumentException("At least one text must be provided", nameof(texts));

        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = texts.ToList()
        }, ct);

        return response.Vectors.ToArray();
    }

    /// <summary>
    /// Generate an embedding and return a rich result with metadata.
    /// </summary>
    public static async Task<EmbedResult> EmbedResult(string text, CancellationToken ct = default)
    {
        var response = await Resolve().Embed(new AiEmbeddingsRequest
        {
            Input = new() { text }
        }, ct);

        var vector = response.Vectors.FirstOrDefault() ?? Array.Empty<float>();
        return new EmbedResult
        {
            Vector = vector,
            Model = response.Model,
            Dimension = vector.Length
        };
    }

    /// <summary>
    /// Low-level embed access for pipeline/internal use.
    /// </summary>
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Resolve().Embed(req, ct);

    // ========================================================================
    // OCR
    // ========================================================================

    /// <summary>
    /// Extract text from an image using OCR (delegates through Chat with vision model).
    /// </summary>
    public static async Task<string> Ocr(byte[] image, CancellationToken ct = default)
    {
        return await Ocr(image, new OcrOptions(), ct);
    }

    /// <summary>
    /// Extract text from an image using OCR with options.
    /// </summary>
    public static async Task<string> Ocr(byte[] image, OcrOptions options, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            throw new ArgumentException("Image data is required", nameof(image));

        var prompt = GetOcrPrompt(options.Format);
        var chatOptions = new ChatOptions
        {
            Image = image,
            ImageMimeType = options.MimeType,
            Model = options.Model,
            Source = options.Source
        };

        return await Chat(prompt, chatOptions, ct);
    }

    /// <summary>
    /// Extract text from an image and return a rich result.
    /// </summary>
    public static async Task<OcrResult> OcrResult(byte[] image, CancellationToken ct = default)
    {
        var text = await Ocr(image, ct);
        return new OcrResult { Text = text };
    }

    /// <summary>
    /// Extract text from an image with options and return a rich result.
    /// </summary>
    public static async Task<OcrResult> OcrResult(byte[] image, OcrOptions options, CancellationToken ct = default)
    {
        var text = await Ocr(image, options, ct);
        return new OcrResult
        {
            Text = text,
            Format = options.Format,
            Model = options.Model
        };
    }

    // ========================================================================
    // Scope (replaces Context — per-category routing)
    // ========================================================================

    /// <summary>
    /// Create a scoped routing context with per-category overrides.
    /// Categories: "all" applies to all categories; "chat"/"embed"/"ocr" target specific categories.
    /// </summary>
    public static AiCategoryScope Scope(
        string? all = null,
        string? chat = null,
        string? embed = null,
        string? ocr = null)
    {
        return new AiCategoryScope(
            all: all,
            chatSource: chat,
            embedSource: embed,
            ocrSource: ocr);
    }

    // ========================================================================
    // Conversation Builder
    // ========================================================================

    public static AiConversationBuilder Conversation()
        => new(Resolve());

    // ========================================================================
    // Discovery
    // ========================================================================

    public static bool IsAvailable
    {
        get
        {
            if (_override.Value is IAiPipeline) return true;
            var sp = Koan.Core.Hosting.App.AppHost.Current;
            if (sp is null) return false;
            var ia = sp.GetService<IAiPipeline>();
            if (ia is not null) return true;
            var scopeFactory = sp.GetService<IServiceScopeFactory>();
            if (scopeFactory is null) return false;
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IAiPipeline>() is not null;
        }
    }

    public static IAiPipeline? TryResolve()
    {
        if (_override.Value is IAiPipeline o) return o;
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is null) return null;
        var ia = sp.GetService<IAiPipeline>();
        if (ia is not null) return ia;
        var scopeFactory = sp.GetService<IServiceScopeFactory>();
        if (scopeFactory is null) return null;
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IAiPipeline>();
    }

    // ========================================================================
    // Internal
    // ========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAiPipeline Resolve()
    {
        if (_override.Value is IAiPipeline o) return o;
        var sp = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "AI not configured; call services.AddKoan() or AddAi() and ensure AppHost.Current is set during startup.");
        _resolver ??= CreateResolver(sp);
        return _resolver(sp);
    }

    private static AiChatRequest BuildChatRequest(string message, ChatOptions? options)
    {
        var messages = new List<AiMessage>();

        if (options?.Messages is { Count: > 0 })
        {
            messages.AddRange(options.Messages);
        }
        else
        {
            messages.Add(new AiMessage("user", message));
        }

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            messages.Insert(0, new AiMessage("system", options.SystemPrompt));
        }

        // Handle multimodal (image)
        if (options?.Image is { Length: > 0 })
        {
            var idx = messages.FindLastIndex(m => m.Role == "user");
            if (idx >= 0)
            {
                var userMsg = messages[idx];
                messages[idx] = userMsg with
                {
                    Parts = new List<AiMessagePart>
                    {
                        new() { Type = "text", Text = userMsg.Content },
                        new() { Type = "image", Data = options.Image, MimeType = options.ImageMimeType ?? "image/jpeg" }
                    }
                };
            }
        }

        AiPromptOptions? promptOpts = null;
        if (options is not null)
        {
            promptOpts = new AiPromptOptions
            {
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxTokens,
                TopP = options.TopP,
                Stop = options.Stop,
                Seed = options.Seed,
                Think = options.Think,
                ResponseFormat = options.ResponseFormat
            };
        }

        var (scopeSource, _) = AiCategoryScope.ResolveMerged("Chat", options?.Source);
        var model = options?.Model;

        return new AiChatRequest
        {
            Messages = messages,
            Model = model,
            Options = promptOpts,
            Route = scopeSource is not null
                ? new AiRouteHints { AdapterId = scopeSource }
                : null
        };
    }

    private static string GetOcrPrompt(OcrFormat format) => format switch
    {
        OcrFormat.Markdown =>
            "Extract all text from this image. Format the output as Markdown, preserving headings, lists, and structure.",
        OcrFormat.Structured =>
            "Extract all text from this image. Return a JSON object with regions, each containing: text, confidence (0-1), and bounding_box (x, y, width, height).",
        _ =>
            "Extract all text from this image. Return only the extracted text, preserving the original formatting."
    };

    private static Func<IServiceProvider, IAiPipeline> CreateResolver(IServiceProvider sp)
    {
        return (svc) =>
        {
            var scopeFactory = svc.GetService<IServiceScopeFactory>();
            if (scopeFactory is null)
                throw new InvalidOperationException("ServiceScopeFactory missing; invalid DI container state.");
            var ia = svc.GetService<IAiPipeline>();
            if (ia is not null) return ia;
            using var scope = scopeFactory.CreateScope();
            ia = scope.ServiceProvider.GetService<IAiPipeline>();
            return ia ?? throw new InvalidOperationException("IAiPipeline not registered; call AddKoan() or AddAi().");
        };
    }

    private sealed class Reset : IDisposable
    {
        private readonly Action _onDispose;
        public Reset(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
