using System.Runtime.CompilerServices;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// A fake IAiPipeline that returns configurable responses.
/// Used with Client.With() to intercept the pipeline during chain execution tests.
/// </summary>
internal sealed class FakePipeline : IAiPipeline
{
    private readonly string _text;
    private readonly string? _model;
    private readonly int? _tokensIn;
    private readonly int? _tokensOut;
    private readonly float[]? _embedVector;
    private readonly string? _embedModel;
    private readonly Action<AiChatRequest>? _onPrompt;

    public FakePipeline(
        string text = "",
        string? model = null,
        int? tokensIn = null,
        int? tokensOut = null,
        float[]? embedVector = null,
        string? embedModel = null,
        Action<AiChatRequest>? onPrompt = null)
    {
        _text = text;
        _model = model;
        _tokensIn = tokensIn;
        _tokensOut = tokensOut;
        _embedVector = embedVector;
        _embedModel = embedModel;
        _onPrompt = onPrompt;
    }

    public Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default)
    {
        _onPrompt?.Invoke(request);
        return Task.FromResult(new AiChatResponse
        {
            Text = _text,
            Model = _model ?? "fake",
            TokensIn = _tokensIn,
            TokensOut = _tokensOut,
            AdapterId = "fake"
        });
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _onPrompt?.Invoke(request);
        await Task.CompletedTask;
        yield return new AiChatChunk { DeltaText = _text, Index = 0, Model = _model ?? "fake" };
    }

    public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var vectors = request.Input.Select(_ => _embedVector ?? new float[] { 0.1f, 0.2f }).ToList();
        return Task.FromResult(new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = _embedModel ?? request.Model ?? "fake-embed",
            Dimension = vectors.Count > 0 ? vectors[0].Length : 0
        });
    }

    public Task<string> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Task.FromResult(_text);

    public async IAsyncEnumerable<AiChatChunk> Stream(
        string message, string? model = null, AiPromptOptions? opts = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return new AiChatChunk { DeltaText = _text, Index = 0, Model = _model ?? "fake" };
    }
}
