using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Context;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Client;

/// <summary>
/// Tests for the static Client facade: Chat, Embed, Stream, OCR, Scope, and With.
/// </summary>
[Trait("ADR", "AI-0021")]
[Trait("Category", "Unit")]
public sealed class ClientTests
{
    // ========================================================================
    // Chat
    // ========================================================================

    [Fact]
    public async Task Chat_returns_text_from_pipeline()
    {
        var fake = new FakePipeline("Hello from AI");

        using (Koan.AI.Client.With(fake))
        {
            var result = await Koan.AI.Client.Chat("Hi");

            result.Should().Be("Hello from AI");
        }
    }

    [Fact]
    public async Task Chat_with_options_passes_system_prompt()
    {
        AiChatRequest? captured = null;
        var fake = new FakePipeline("OK", onPrompt: r => captured = r);

        using (Koan.AI.Client.With(fake))
        {
            await Koan.AI.Client.Chat("Hello", new ChatOptions
            {
                SystemPrompt = "You are a helpful assistant"
            });

            captured.Should().NotBeNull();
            captured!.Messages.Should().HaveCount(2);
            captured.Messages[0].Role.Should().Be("system");
            captured.Messages[0].Content.Should().Be("You are a helpful assistant");
            captured.Messages[1].Role.Should().Be("user");
            captured.Messages[1].Content.Should().Be("Hello");
        }
    }

    [Fact]
    public async Task ChatResult_returns_rich_metadata()
    {
        var fake = new FakePipeline("answer", model: "test-model", tokensIn: 10, tokensOut: 5);

        using (Koan.AI.Client.With(fake))
        {
            var result = await Koan.AI.Client.ChatResult("question");

            result.Text.Should().Be("answer");
            result.Model.Should().Be("test-model");
            result.TokensIn.Should().Be(10);
            result.TokensOut.Should().Be(5);
            result.TokensUsed.Should().Be(15);
            result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    // ========================================================================
    // Stream
    // ========================================================================

    [Fact]
    public async Task Stream_yields_chunks()
    {
        var fake = new FakePipeline("streamed text");

        using (Koan.AI.Client.With(fake))
        {
            var chunks = new List<string>();
            await foreach (var chunk in Koan.AI.Client.Stream("Go"))
            {
                chunks.Add(chunk);
            }

            chunks.Should().NotBeEmpty();
            string.Join("", chunks).Should().Be("streamed text");
        }
    }

    // ========================================================================
    // Embed
    // ========================================================================

    [Fact]
    public async Task Embed_returns_vector_from_pipeline()
    {
        var expectedVector = new float[] { 0.1f, 0.2f, 0.3f };
        var fake = new FakePipeline(embedVector: expectedVector);

        using (Koan.AI.Client.With(fake))
        {
            var vector = await Koan.AI.Client.Embed("test text");

            vector.Should().BeEquivalentTo(expectedVector);
        }
    }

    [Fact]
    public async Task EmbedBatch_returns_multiple_vectors()
    {
        var fake = new FakePipeline(embedVector: new float[] { 1f, 2f });

        using (Koan.AI.Client.With(fake))
        {
            var vectors = await Koan.AI.Client.EmbedBatch(new[] { "a", "b", "c" });

            vectors.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task EmbedResult_returns_rich_metadata()
    {
        var vector = new float[] { 0.5f, 0.6f, 0.7f };
        var fake = new FakePipeline(embedVector: vector, embedModel: "embed-v2");

        using (Koan.AI.Client.With(fake))
        {
            var result = await Koan.AI.Client.EmbedResult("text");

            result.Vector.Should().BeEquivalentTo(vector);
            result.Model.Should().Be("embed-v2");
            result.Dimension.Should().Be(3);
        }
    }

    // ========================================================================
    // OCR (delegates through Chat)
    // ========================================================================

    [Fact]
    public async Task Ocr_delegates_through_Chat()
    {
        var fake = new FakePipeline("Extracted: Hello World");

        using (Koan.AI.Client.With(fake))
        {
            var image = new byte[] { 0xFF, 0xD8, 0xFF };
            var text = await Koan.AI.Client.Ocr(image);

            text.Should().Be("Extracted: Hello World");
        }
    }

    [Fact]
    public async Task Ocr_with_markdown_format_uses_markdown_prompt()
    {
        AiChatRequest? captured = null;
        var fake = new FakePipeline("# Title", onPrompt: r => captured = r);

        using (Koan.AI.Client.With(fake))
        {
            var image = new byte[] { 0xFF, 0xD8 };
            await Koan.AI.Client.Ocr(image, new OcrOptions { Format = OcrFormat.Markdown });

            captured.Should().NotBeNull();
            var userMessage = captured!.Messages.Last(m => m.Role == "user");
            userMessage.Content.Should().Contain("Markdown");
        }
    }

    [Fact]
    public async Task Ocr_attaches_image_to_request()
    {
        AiChatRequest? captured = null;
        var fake = new FakePipeline("text", onPrompt: r => captured = r);

        using (Koan.AI.Client.With(fake))
        {
            var image = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
            await Koan.AI.Client.Ocr(image, new OcrOptions { MimeType = "image/png" });

            captured.Should().NotBeNull();
            var userMessage = captured!.Messages.Last(m => m.Role == "user");
            userMessage.Parts.Should().NotBeNull();
            userMessage.Parts.Should().Contain(p => p.Type == "image");
        }
    }

    [Fact]
    public void Ocr_with_null_image_throws()
    {
        var fake = new FakePipeline("x");

        using (Koan.AI.Client.With(fake))
        {
            var act = () => Koan.AI.Client.Ocr(null!, CancellationToken.None);

            act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Image data*");
        }
    }

    [Fact]
    public async Task OcrResult_returns_rich_result()
    {
        var fake = new FakePipeline("OCR output");

        using (Koan.AI.Client.With(fake))
        {
            var result = await Koan.AI.Client.OcrResult(new byte[] { 1, 2, 3 });

            result.Text.Should().Be("OCR output");
        }
    }

    // ========================================================================
    // With / Scope
    // ========================================================================

    [Fact]
    public void With_makes_IsAvailable_true()
    {
        var fake = new FakePipeline("x");

        using (Koan.AI.Client.With(fake))
        {
            Koan.AI.Client.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void With_disposes_restoring_previous_state()
    {
        var fake = new FakePipeline("x");

        using (Koan.AI.Client.With(fake))
        {
            Koan.AI.Client.IsAvailable.Should().BeTrue();
        }

        // After dispose, no AppHost configured → pipeline not available
        // (only if no AppHost.Current is set; in test context this should be false)
        Koan.AI.Client.TryResolve().Should().BeNull();
    }

    [Fact]
    public void Scope_creates_disposable_category_scope()
    {
        using (Koan.AI.Client.Scope(chat: "alpha", embed: "beta"))
        {
            AiCategoryScope.ResolveSource("Chat").Should().Be("alpha");
            AiCategoryScope.ResolveSource("Embed").Should().Be("beta");
        }

        AiCategoryScope.ResolveSource("Chat").Should().BeNull();
        AiCategoryScope.ResolveSource("Embed").Should().BeNull();
    }

    [Fact]
    public void Scope_all_overrides_every_category()
    {
        using (Koan.AI.Client.Scope(all: "universal"))
        {
            AiCategoryScope.ResolveSource("Chat").Should().Be("universal");
            AiCategoryScope.ResolveSource("Embed").Should().Be("universal");
            AiCategoryScope.ResolveSource("Ocr").Should().Be("universal");
        }
    }

    [Fact]
    public void Scope_category_override_takes_precedence_over_all()
    {
        using (Koan.AI.Client.Scope(all: "default", chat: "special"))
        {
            AiCategoryScope.ResolveSource("Chat").Should().Be("special",
                "explicit category should override 'all'");
            AiCategoryScope.ResolveSource("Embed").Should().Be("default",
                "non-overridden categories should use 'all'");
        }
    }

    // ========================================================================
    // Fake Pipeline
    // ========================================================================

    private sealed class FakePipeline : IAiPipeline
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

        public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
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

        public async IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new AiChatChunk { DeltaText = _text, Index = 0, Model = _model ?? "fake" };
        }
    }
}
