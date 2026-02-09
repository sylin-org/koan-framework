using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Options;
using Koan.AI.Context;

namespace Koan.AI.Pipelines;

/// <summary>
/// Pipeline stage for text-based AI operations.
/// Supports transformations to image, embeddings, chat responses, and streaming.
/// </summary>
public sealed class TextPipeline : IAiPipelineStage<string>
{
    private readonly string _input;
    private readonly PipelineContext _context;

    internal TextPipeline(string input, PipelineContext context)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Generate an image from text using AI.
    /// Lazy evaluation - no API call until terminal operation.
    /// </summary>
    /// <param name="model">Optional model override</param>
    /// <param name="options">Optional image generation options</param>
    /// <returns>Image pipeline for further transformations</returns>
    public ImagePipeline ToImage(string? model = null, object? options = null)
    {
        return new ImagePipeline(
            textInput: _input,
            context: _context.WithModel(model).WithOptions(options)
        );
    }

    /// <summary>
    /// Generate embeddings for text.
    /// Terminal operation - executes immediately.
    /// </summary>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Embedding vector</returns>
    public async Task<float[]> ToEmbedding(string? model = null, CancellationToken ct = default)
    {
        using (_context.Model != null || model != null
            ? Client.Scope(all: _context.Source)
            : null)
        {
            return await Client.Embed(_input, ct);
        }
    }

    /// <summary>
    /// Chat with AI using text as user message.
    /// Terminal operation - executes immediately.
    /// </summary>
    /// <param name="model">Optional model override</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public async Task<string> ToResponse(string? model = null, string? systemPrompt = null, CancellationToken ct = default)
    {
        using (_context.Model != null || model != null || _context.Source != null
            ? Client.Scope(all: _context.Source)
            : null)
        {
            return await Client.Chat(_input, new ChatOptions
            {
                SystemPrompt = systemPrompt ?? _context.SystemPrompt,
                Model = model ?? _context.Model
            }, ct);
        }
    }

    /// <summary>
    /// Stream chat responses from AI.
    /// Terminal operation - streams immediately.
    /// </summary>
    /// <param name="model">Optional model override</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of text chunks</returns>
    public async IAsyncEnumerable<string> Stream(
        string? model = null,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using (_context.Model != null || model != null || _context.Source != null
            ? Client.Scope(all: _context.Source)
            : null)
        {
            await foreach (var chunk in Client.Stream(_input, new ChatOptions
            {
                SystemPrompt = systemPrompt ?? _context.SystemPrompt,
                Model = model ?? _context.Model
            }, ct))
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// Execute pipeline and return the text input (passthrough).
    /// </summary>
    public Task<string> ExecuteAsync(CancellationToken ct = default)
        => Task.FromResult(_input);

    /// <summary>
    /// Stream the text input as single item.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return _input;
        await Task.CompletedTask;
    }
}
