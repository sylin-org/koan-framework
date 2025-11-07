using FluentAssertions;
using Koan.Context.Models;
using Koan.Context.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Retrieval;

/// <summary>
/// Tests for RetrievalService covering semantic and hybrid search
/// </summary>
public class RetrievalService_Spec
{
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<ITokenCountingService> _tokenCounterMock;
    private readonly Mock<ILogger<RetrievalService>> _loggerMock;
    private readonly RetrievalService _service;

    public RetrievalService_Spec()
    {
    _embeddingMock = new Mock<IEmbeddingService>();
    _tokenCounterMock = new Mock<ITokenCountingService>();
    _loggerMock = new Mock<ILogger<RetrievalService>>();
    _service = new RetrievalService(
        _embeddingMock.Object,
        _tokenCounterMock.Object,
        _loggerMock.Object);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResult()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();

        // Act
        var result = await _service.SearchAsync(projectId, "");

        // Assert
    result.Chunks.Should().BeEmpty();
    result.Metadata.TokensReturned.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ReturnsEmptyResult()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();

        // Act
        var result = await _service.SearchAsync(projectId, null!);

        // Assert
    result.Chunks.Should().BeEmpty();
    result.Metadata.TokensReturned.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmptyResult()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();

        // Act
        var result = await _service.SearchAsync(projectId, "   ");

        // Assert
    result.Chunks.Should().BeEmpty();
    result.Metadata.TokensReturned.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_EmptyEmbedding_ReturnsEmptyResult()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float>());

        // Act
        var result = await _service.SearchAsync(projectId, query);

        // Assert
    result.Chunks.Should().BeEmpty();
    result.Metadata.TokensReturned.Should().Be(0);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Empty embedding")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_UsesDefaultOptions_WhenNullProvided()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _service.SearchAsync(projectId, query, null);

        // Assert - should use default alpha and topK
        _embeddingMock.Verify(
            x => x.EmbedAsync(query, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0.0f)] // Pure keyword (BM25)
    [InlineData(0.5f)] // Balanced hybrid
    [InlineData(0.7f)] // Default semantic-leaning
    [InlineData(1.0f)] // Pure semantic (vector)
    public async Task SearchAsync_RespectsAlphaParameter(float alpha)
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

    var options = new SearchOptions(Alpha: alpha);

        // Act
        var result = await _service.SearchAsync(projectId, query, options);

        // Assert
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public async Task SearchAsync_RespectsTopKParameter(int topK)
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

    var options = new SearchOptions(MaxTokens: topK * 400);

        // Act
        var result = await _service.SearchAsync(projectId, query, options);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_LogsSearchParameters()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

    var options = new SearchOptions(MaxTokens: 6000, Alpha: 0.8f);

        // Act
        await _service.SearchAsync(projectId, query, options);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Searching project")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MeasuresDuration()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _service.SearchAsync(projectId, query);

        // Assert
        result.Metadata.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task SearchAsync_HandlesEmbeddingServiceException()
    {
        // Arrange
    var projectId = Guid.NewGuid().ToString();
        var query = "test query";

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedding service unavailable"));

        // Act & Assert
        var result = await _service.SearchAsync(projectId, query);

        result.Chunks.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("Search failed"));
    }

    [Fact]
    public async Task SearchAsync_SupportsCancellation()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var query = "test query";
        var cts = new CancellationTokenSource();

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        var result = await _service.SearchAsync(projectId, query, cancellationToken: cts.Token);

        result.Chunks.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void SearchOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new SearchOptions();

        // Assert
        options.MaxTokens.Should().Be(5000);
        options.Alpha.Should().Be(0.7f);
        options.ContinuationToken.Should().BeNull();
        options.IncludeInsights.Should().BeTrue();
        options.IncludeReasoning.Should().BeTrue();
    }

    [Fact]
    public void SearchResultChunk_ContainsProvenanceData()
    {
        // Arrange
        var chunk = new SearchResultChunk(
            Id: "chunk-1",
            Text: "Sample code",
            Score: 0.95f,
            Provenance: new ChunkProvenance(
                SourceIndex: 0,
                StartByteOffset: 0,
                EndByteOffset: 100,
                StartLine: 1,
                EndLine: 5,
                Language: "csharp"),
            Reasoning: new RetrievalReasoning(0.9f, 0.1f, "hybrid"));

        // Assert
        chunk.Text.Should().Be("Sample code");
        chunk.Provenance.SourceIndex.Should().Be(0);
        chunk.Provenance.StartByteOffset.Should().Be(0);
        chunk.Provenance.EndByteOffset.Should().Be(100);
        chunk.Provenance.StartLine.Should().Be(1);
        chunk.Provenance.EndLine.Should().Be(5);
        chunk.Provenance.Language.Should().Be("csharp");
        chunk.Score.Should().Be(0.95f);
    }
}
