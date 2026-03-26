using System.Runtime.CompilerServices;
using AiClient = Koan.AI.Client;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using FluentAssertions;

namespace Koan.Tests.AI.Unit.Specs.Embedding;

/// <summary>
/// Tests for EntityAi static class (ADR AI-0021).
/// Validates entity-aware AI operations: content extraction, embedding, chat context injection, and OCR byte extraction.
/// </summary>
[Trait("ADR", "AI-0021")]
[Trait("Category", "Unit")]
public sealed class EntityAiSpec
{
    // ========================================================================
    // Content Extraction (no pipeline needed)
    // ========================================================================

    [Fact]
    public void ExtractText_from_undecorated_entity_uses_AllStrings_convention()
    {
        // Arrange
        var note = new SimpleNote();

        // Act
        var text = EntityAi.ExtractText(note);

        // Assert — convention extracts all public string properties (excluding Id)
        text.Should().Contain("Test Title");
        text.Should().Contain("Test Body");
        text.Should().NotContain("note-1", "Id is infrastructure and should be excluded");
        text.Should().NotContain("5", "non-string properties should not appear");
    }

    [Fact]
    public void ExtractText_excludes_Id_property()
    {
        // Arrange
        var note = new SimpleNote
        {
            Id = "should-not-appear",
            Title = "Visible Title",
            Body = "Visible Body"
        };

        // Act
        var text = EntityAi.ExtractText(note);

        // Assert
        text.Should().NotContain("should-not-appear", "Id is excluded by convention");
        text.Should().Contain("Visible Title");
        text.Should().Contain("Visible Body");
    }

    [Fact]
    public void ExtractText_from_decorated_entity_uses_attribute_config()
    {
        // Arrange
        var entity = new DecoratedNote();

        // Act
        var text = EntityAi.ExtractText(entity);

        // Assert — AllStrings policy via attribute behaves the same as convention
        text.Should().Contain("Decorated Title");
        text.Should().Contain("Decorated Body");
        text.Should().NotContain("decorated-1", "Id remains excluded under AllStrings policy");
    }

    [Fact]
    public void ExtractText_from_empty_entity_returns_empty_string()
    {
        // Arrange
        var entity = new EmptyEntity();

        // Act
        var text = EntityAi.ExtractText(entity);

        // Assert — no string properties (only Id + int) → empty
        text.Should().BeEmpty();
    }

    // ========================================================================
    // Embed (needs fake pipeline)
    // ========================================================================

    [Fact]
    public async Task Embed_entity_returns_vector_from_pipeline()
    {
        // Arrange
        var fake = new FakePipeline();
        var note = new SimpleNote { Title = "Embed me", Body = "Please" };

        // Act
        float[] vector;
        using (AiClient.With(fake))
        {
            vector = await EntityAi.Embed(note);
        }

        // Assert
        vector.Should().BeEquivalentTo(fake.EmbedResponse, "pipeline returns canned vector");
        fake.LastEmbedRequest.Should().NotBeNull();
        fake.LastEmbedRequest!.Input.Should().ContainSingle();
        fake.LastEmbedRequest.Input[0].Should().Contain("Embed me");
    }

    [Fact]
    public async Task Embed_entity_with_empty_content_returns_empty_array()
    {
        // Arrange
        var fake = new FakePipeline();
        var entity = new EmptyEntity();

        // Act
        float[] vector;
        using (AiClient.With(fake))
        {
            vector = await EntityAi.Embed(entity);
        }

        // Assert
        vector.Should().BeEmpty("entity with no string content yields empty vector without calling pipeline");
        fake.LastEmbedRequest.Should().BeNull("pipeline should not be invoked for empty content");
    }

    // ========================================================================
    // Chat (needs fake pipeline)
    // ========================================================================

    [Fact]
    public async Task Chat_with_entity_injects_context_as_system_prompt()
    {
        // Arrange
        var fake = new FakePipeline { ChatResponse = "AI says hello" };
        var note = new SimpleNote { Title = "Meeting Notes", Body = "Discuss roadmap" };

        // Act
        string response;
        using (AiClient.With(fake))
        {
            response = await EntityAi.Chat("Summarize this", note);
        }

        // Assert
        response.Should().Be("AI says hello");
        fake.LastChatRequest.Should().NotBeNull();

        var systemMessage = fake.LastChatRequest!.Messages
            .FirstOrDefault(m => m.Role == "system");

        systemMessage.Should().NotBeNull("entity context should be injected as system prompt");
        systemMessage!.Content.Should().Contain("Meeting Notes");
        systemMessage.Content.Should().Contain("Discuss roadmap");
        systemMessage.Content.Should().Contain("SimpleNote", "context includes entity type name");
    }

    [Fact]
    public async Task Chat_with_entity_merges_with_existing_options()
    {
        // Arrange
        var fake = new FakePipeline { ChatResponse = "merged response" };
        var note = new SimpleNote { Title = "Topic", Body = "Details" };
        var options = new ChatOptions { SystemPrompt = "You are an expert assistant." };

        // Act
        string response;
        using (AiClient.With(fake))
        {
            response = await EntityAi.Chat("Question?", note, options);
        }

        // Assert
        response.Should().Be("merged response");
        fake.LastChatRequest.Should().NotBeNull();

        var systemMessage = fake.LastChatRequest!.Messages
            .FirstOrDefault(m => m.Role == "system");

        systemMessage.Should().NotBeNull();
        systemMessage!.Content.Should().Contain("Topic", "entity content is present");
        systemMessage.Content.Should().Contain("You are an expert assistant.", "original system prompt is preserved");
    }

    // ========================================================================
    // OCR (needs fake pipeline)
    // ========================================================================

    [Fact]
    public async Task Ocr_extracts_bytes_from_Data_property()
    {
        // Arrange
        var fake = new FakePipeline { ChatResponse = "extracted text from image" };
        var media = new MediaItem { Data = [10, 20, 30, 40] };

        // Act
        string ocrText;
        using (AiClient.With(fake))
        {
            ocrText = await EntityAi.Ocr(media);
        }

        // Assert
        ocrText.Should().Be("extracted text from image");
        fake.LastChatRequest.Should().NotBeNull("OCR delegates through Chat with vision");
    }

    [Fact]
    public async Task Ocr_with_no_byte_property_returns_empty()
    {
        // Arrange
        var fake = new FakePipeline();
        var entity = new NoBytesEntity { Name = "no binary here" };

        // Act
        string ocrText;
        using (AiClient.With(fake))
        {
            ocrText = await EntityAi.Ocr(entity);
        }

        // Assert
        ocrText.Should().BeEmpty("entity with no byte[] property yields empty string");
        fake.LastChatRequest.Should().BeNull("pipeline should not be invoked when no bytes found");
    }

    // ========================================================================
    // Test Entities
    // ========================================================================

    private class SimpleNote
    {
        public string Id { get; set; } = "note-1";
        public string Title { get; set; } = "Test Title";
        public string Body { get; set; } = "Test Body";
        public int Priority { get; set; } = 5;
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings)]
    private class DecoratedNote
    {
        public string Id { get; set; } = "decorated-1";
        public string Title { get; set; } = "Decorated Title";
        public string Body { get; set; } = "Decorated Body";
    }

    private class MediaItem
    {
        public string Id { get; set; } = "media-1";
        public string Name { get; set; } = "photo.jpg";
        public byte[] Data { get; set; } = [1, 2, 3];
    }

    private class EmptyEntity
    {
        public string Id { get; set; } = "empty-1";
        public int Count { get; set; }
    }

    private class NoBytesEntity
    {
        public string Id { get; set; } = "no-bytes-1";
        public string Name { get; set; } = "text only";
    }

    // ========================================================================
    // Fake Pipeline
    // ========================================================================

    private sealed class FakePipeline : IAiPipeline
    {
        public AiChatRequest? LastChatRequest { get; private set; }
        public AiEmbeddingsRequest? LastEmbedRequest { get; private set; }

        public string ChatResponse { get; set; } = "fake response";
        public float[] EmbedResponse { get; set; } = [0.1f, 0.2f, 0.3f];

        public Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default)
        {
            LastChatRequest = request;
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
                Model = model
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
}
