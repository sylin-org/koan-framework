using FluentAssertions;
using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
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
    private readonly Mock<ILogger<RetrievalService>> _loggerMock;
    private readonly RetrievalService _service;

    public RetrievalService_Spec()
    {
        _embeddingMock = new Mock<IEmbeddingService>();
        _loggerMock = new Mock<ILogger<RetrievalService>>();
        _service = new RetrievalService(_embeddingMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act
        var result = await _service.SearchAsync(projectId, "");

        // Assert
        result.Chunks.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ReturnsEmptyResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act
        var result = await _service.SearchAsync(projectId, null!);

        // Assert
        result.Chunks.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmptyResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act
        var result = await _service.SearchAsync(projectId, "   ");

        // Assert
        result.Chunks.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_EmptyEmbedding_ReturnsEmptyResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = "test query";

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float>());

        // Act
        var result = await _service.SearchAsync(projectId, query);

        // Assert
        result.Chunks.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
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
        var projectId = Guid.NewGuid();
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
        var projectId = Guid.NewGuid();
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
        var projectId = Guid.NewGuid();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        var options = new SearchOptions(TopK: topK);

        // Act
        var result = await _service.SearchAsync(projectId, query, options);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_LogsSearchParameters()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        var options = new SearchOptions(Alpha: 0.8f, TopK: 15);

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
        var projectId = Guid.NewGuid();
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _service.SearchAsync(projectId, query);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task SearchAsync_HandlesEmbeddingServiceException()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = "test query";

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedding service unavailable"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await _service.SearchAsync(projectId, query);
        });

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Search failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_SupportsCancellation()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = "test query";
        var cts = new CancellationTokenSource();

        _embeddingMock
            .Setup(x => x.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _service.SearchAsync(projectId, query, cancellationToken: cts.Token);
        });
    }

    [Fact]
    public void SearchOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new SearchOptions();

        // Assert
        options.Alpha.Should().Be(0.7f);
        options.TopK.Should().Be(10);
        options.OffsetStart.Should().BeNull();
        options.OffsetEnd.Should().BeNull();
    }

    [Fact]
    public void SearchResultChunk_ContainsProvenanceData()
    {
        // Arrange
        var chunk = new SearchResultChunk(
            Text: "Sample code",
            FilePath: "src/Program.cs",
            CommitSha: "abc123",
            ChunkRange: "0:100",
            Title: "Main Program",
            Language: "csharp",
            Score: 0.95f);

        // Assert
        chunk.Text.Should().Be("Sample code");
        chunk.FilePath.Should().Be("src/Program.cs");
        chunk.CommitSha.Should().Be("abc123");
        chunk.ChunkRange.Should().Be("0:100");
        chunk.Title.Should().Be("Main Program");
        chunk.Language.Should().Be("csharp");
        chunk.Score.Should().Be(0.95f);
    }
}
