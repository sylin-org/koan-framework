using System.Runtime.CompilerServices;
using AiClient = Koan.AI.Client;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.AI.Workers;

namespace Koan.Tests.AI.Unit.Specs.Media;

/// <summary>
/// Tests for MediaAnalysisExecutor per-mode dispatch.
/// Uses FakePipeline via Client.With() to intercept AI calls.
/// </summary>
[Trait("ADR", "AI-0027")]
[Trait("Category", "Unit")]
public sealed class MediaAnalysisExecutorSpec
{
    private static readonly byte[] TestContent = [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG magic bytes

    [Fact]
    public async Task Execute_Describe_sets_description_property()
    {
        // Arrange
        var fake = new FakePipeline { ChatResponse = "A sunset over mountains" };
        var photo = new TestPhoto();
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe,
            DescriptionProperty = nameof(TestPhoto.AiDescription),
        };

        // Act
        Dictionary<MediaAnalysis, ModeStatus> results;
        using (AiClient.With(fake))
        {
            results = await MediaAnalysisExecutor.Execute(photo, metadata, TestContent, CancellationToken.None);
        }

        // Assert
        photo.AiDescription.Should().Be("A sunset over mountains");
        results.Should().ContainKey(MediaAnalysis.Describe);
        results[MediaAnalysis.Describe].Completed.Should().BeTrue();
        results[MediaAnalysis.Describe].CompletedAt.Should().NotBeNull();
        results[MediaAnalysis.Describe].Error.Should().BeNull();
    }

    [Fact]
    public async Task Execute_Ocr_sets_ocr_text_property()
    {
        // Arrange
        var fake = new FakePipeline { ChatResponse = "Invoice #12345\nTotal: $99.00" };
        var photo = new TestPhoto();
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Ocr,
            OcrTextProperty = nameof(TestPhoto.OcrText),
        };

        // Act
        Dictionary<MediaAnalysis, ModeStatus> results;
        using (AiClient.With(fake))
        {
            results = await MediaAnalysisExecutor.Execute(photo, metadata, TestContent, CancellationToken.None);
        }

        // Assert
        photo.OcrText.Should().Be("Invoice #12345\nTotal: $99.00");
        results.Should().ContainKey(MediaAnalysis.Ocr);
        results[MediaAnalysis.Ocr].Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Transcribe_returns_not_implemented()
    {
        // Arrange
        var fake = new FakePipeline();
        var photo = new TestPhoto();
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Transcribe,
            TranscriptProperty = "Transcript",
        };

        // Act
        Dictionary<MediaAnalysis, ModeStatus> results;
        using (AiClient.With(fake))
        {
            results = await MediaAnalysisExecutor.Execute(photo, metadata, TestContent, CancellationToken.None);
        }

        // Assert
        results.Should().ContainKey(MediaAnalysis.Transcribe);
        results[MediaAnalysis.Transcribe].Completed.Should().BeFalse();
        results[MediaAnalysis.Transcribe].Error.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task Execute_returns_partial_on_mixed_results()
    {
        // Arrange — Describe succeeds (call 1), Ocr fails (call 2)
        var fake = new FakePipeline
        {
            ChatResponse = "A photo of a document",
            FailOnCallNumber = 2,
            FailureMessage = "OCR model unavailable",
        };
        var photo = new TestPhoto();
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr,
            DescriptionProperty = nameof(TestPhoto.AiDescription),
            OcrTextProperty = nameof(TestPhoto.OcrText),
        };

        // Act
        Dictionary<MediaAnalysis, ModeStatus> results;
        using (AiClient.With(fake))
        {
            results = await MediaAnalysisExecutor.Execute(photo, metadata, TestContent, CancellationToken.None);
        }

        // Assert
        results.Should().HaveCount(2);
        results[MediaAnalysis.Describe].Completed.Should().BeTrue();
        photo.AiDescription.Should().Be("A photo of a document");

        results[MediaAnalysis.Ocr].Completed.Should().BeFalse();
        results[MediaAnalysis.Ocr].Error.Should().Contain("OCR model unavailable");
        photo.OcrText.Should().BeNull("failed OCR should not set property");
    }

    [Fact]
    public async Task Execute_skips_modes_not_in_flags()
    {
        // Arrange — only Describe in flags, Ocr should not be called
        var fake = new FakePipeline { ChatResponse = "Description only" };
        var photo = new TestPhoto();
        var metadata = new MediaAnalysisMetadata
        {
            Analysis = MediaAnalysis.Describe,
            DescriptionProperty = nameof(TestPhoto.AiDescription),
            OcrTextProperty = nameof(TestPhoto.OcrText),
        };

        // Act
        Dictionary<MediaAnalysis, ModeStatus> results;
        using (AiClient.With(fake))
        {
            results = await MediaAnalysisExecutor.Execute(photo, metadata, TestContent, CancellationToken.None);
        }

        // Assert
        results.Should().ContainKey(MediaAnalysis.Describe);
        results.Should().NotContainKey(MediaAnalysis.Ocr, "Ocr mode not in flags, should be skipped");
        photo.AiDescription.Should().Be("Description only");
        photo.OcrText.Should().BeNull("Ocr was not requested");
    }

    #region Test Entities

    private class TestPhoto
    {
        public string? AiDescription { get; set; }
        public string? OcrText { get; set; }
        public string? Category { get; set; }
    }

    #endregion

    #region Fake Pipeline

    private sealed class FakePipeline : IAiPipeline
    {
        public AiChatRequest? LastChatRequest { get; private set; }
        public AiEmbeddingsRequest? LastEmbedRequest { get; private set; }

        public string ChatResponse { get; set; } = "fake response";
        public float[] EmbedResponse { get; set; } = [0.1f, 0.2f, 0.3f];

        /// <summary>
        /// When set, the Nth call (1-based) to Prompt(AiChatRequest) will throw.
        /// Used to simulate partial failures (e.g., Describe succeeds on call 1, Ocr fails on call 2).
        /// </summary>
        public int? FailOnCallNumber { get; set; }
        public string FailureMessage { get; set; } = "Simulated failure";

        private int _promptCallCount;

        public Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default)
        {
            LastChatRequest = request;
            var callNumber = Interlocked.Increment(ref _promptCallCount);

            if (FailOnCallNumber.HasValue && callNumber == FailOnCallNumber.Value)
            {
                return Task.FromException<AiChatResponse>(
                    new InvalidOperationException(FailureMessage));
            }

            return Task.FromResult(new AiChatResponse { Text = ChatResponse });
        }

        public async IAsyncEnumerable<AiChatChunk> Stream(
            AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastChatRequest = request;
            yield return new AiChatChunk { DeltaText = ChatResponse };
            await Task.CompletedTask;
        }

        public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
        {
            LastEmbedRequest = request;
            return Task.FromResult(new AiEmbeddingsResponse
            {
                Vectors = [EmbedResponse]
            });
        }

        public Task<string> Prompt(
            string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        {
            LastChatRequest = new AiChatRequest
            {
                Messages = [new AiMessage("user", message)],
                Model = model,
            };
            return Task.FromResult(ChatResponse);
        }

        public async IAsyncEnumerable<AiChatChunk> Stream(
            string message, string? model = null, AiPromptOptions? opts = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new AiChatChunk { DeltaText = ChatResponse };
            await Task.CompletedTask;
        }
    }

    #endregion
}
