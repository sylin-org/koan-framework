using FluentAssertions;
using EmbeddingService = Koan.Context.Services.Embedding;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Context.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Embedding;

/// <summary>
/// Comprehensive tests for EmbeddingsService
/// </summary>
/// <remarks>
/// Tests cover:
/// - Cache hit/miss scenarios
/// - Retry on transient failures
/// - Batch processing optimization
/// - Empty text handling
/// - Error scenarios and recovery
/// </remarks>
public class EmbeddingServiceSpec : IDisposable
{
    private readonly Mock<IAi> _aiMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<EmbeddingService>> _loggerMock;
    private readonly EmbeddingService _service;

    private readonly float[] _testEmbedding = Enumerable.Range(0, 384).Select(i => (float)i / 384).ToArray();

    public EmbeddingServiceSpec()
    {
        _aiMock = new Mock<IAi>();
        _cache = new MemoryCache(new MemoryCacheOptions());
    _loggerMock = new Mock<ILogger<EmbeddingService>>();
    _service = new EmbeddingService(_aiMock.Object, _cache, _loggerMock.Object, "test-model");

        // Default mock behavior - return test embedding
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEmbeddingsResponse { Vectors = { _testEmbedding } });
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    #region Cache Tests

    [Fact]
    public async Task EmbedAsync_FirstCall_CallsAiProvider()
    {
        // Arrange
        var text = "test text";

        // Act
        var result = await _service.EmbedAsync(text);

        // Assert
        result.Should().Equal(_testEmbedding);
        _aiMock.Verify(x => x.EmbedAsync(
            It.Is<AiEmbeddingsRequest>(r => r.Input.Contains(text)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmbedAsync_SecondCall_UsesCache()
    {
        // Arrange
        var text = "test text";
        await _service.EmbedAsync(text); // First call

        // Act
        var result = await _service.EmbedAsync(text); // Second call

        // Assert
        result.Should().Equal(_testEmbedding);
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once, "should only call AI provider once, second call should use cache");

        _loggerMock.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cache hit")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EmbedAsync_DifferentText_CreatesSeparateCacheEntries()
    {
        // Arrange & Act
        var result1 = await _service.EmbedAsync("text one");
        var result2 = await _service.EmbedAsync("text two");

        // Assert
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "different texts should not share cache entries");
    }

    [Fact]
    public async Task EmbedAsync_DifferentModels_CreatesSeparateCacheEntries()
    {
        // Arrange
    var service1 = new EmbeddingService(_aiMock.Object, _cache, _loggerMock.Object, "model-a");
    var service2 = new EmbeddingService(_aiMock.Object, _cache, _loggerMock.Object, "model-b");

        // Act
        await service1.EmbedAsync("same text");
        await service2.EmbedAsync("same text");

        // Assert
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "different models should not share cache");
    }

    #endregion

    #region Empty Text Handling Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task EmbedAsync_EmptyText_ReturnsEmptyArray(string? emptyText)
    {
        // Act
        var result = await _service.EmbedAsync(emptyText!);

        // Assert
        result.Should().BeEmpty();
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "should not call AI provider for empty text");

        _loggerMock.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Empty text")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Retry Logic Tests

    [Fact]
    public async Task EmbedAsync_TransientFailure_RetriesAndSucceeds()
    {
        // Arrange
        var callCount = 0;
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                {
                    throw new HttpRequestException("Transient error");
                }
                return new AiEmbeddingsResponse { Vectors = { _testEmbedding } };
            });

        // Act
        var result = await _service.EmbedAsync("test");

        // Assert
        result.Should().Equal(_testEmbedding);
        callCount.Should().Be(2, "should retry once after first failure");

        _loggerMock.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("failed") && v.ToString()!.Contains("Retrying")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EmbedAsync_PersistentFailure_ThrowsAfterRetries()
    {
        // Arrange
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Persistent error"));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await _service.EmbedAsync("test");
        });

        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to generate embedding")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(TimeoutException))]
    public async Task EmbedAsync_TransientExceptions_TriggersRetry(Type exceptionType)
    {
        // Arrange
        var callCount = 0;
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                {
                    throw (Exception)Activator.CreateInstance(exceptionType, "Transient")!;
                }
                return new AiEmbeddingsResponse { Vectors = { _testEmbedding } };
            });

        // Act
        var result = await _service.EmbedAsync("test");

        // Assert
        result.Should().Equal(_testEmbedding);
        callCount.Should().Be(2, "should retry for transient exceptions");
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task EmbedBatchAsync_EmptyList_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _service.EmbedBatchAsync(new List<string>());

        // Assert
        result.Should().BeEmpty();
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmbedBatchAsync_AllCached_DoesNotCallProvider()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };
        foreach (var text in texts)
        {
            await _service.EmbedAsync(text); // Pre-populate cache
        }

        _aiMock.Invocations.Clear(); // Reset mock call tracking

        // Act
        var result = await _service.EmbedBatchAsync(texts);

        // Assert
        result.Should().HaveCount(3);
        _aiMock.Verify(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "all items cached, no AI calls needed");
    }

    [Fact]
    public async Task EmbedBatchAsync_MixedCachedAndUncached_OnlyCallsForUncached()
    {
        // Arrange
        await _service.EmbedAsync("cached1"); // Pre-cache one
        await _service.EmbedAsync("cached2"); // Pre-cache another

        _aiMock.Invocations.Clear();

        var embedding1 = Enumerable.Range(0, 384).Select(i => (float)i).ToArray();
        var embedding2 = Enumerable.Range(0, 384).Select(i => (float)(i + 1)).ToArray();

        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEmbeddingsResponse { Vectors = { embedding1, embedding2 } });

        var texts = new[] { "cached1", "new1", "cached2", "new2" };

        // Act
        var result = await _service.EmbedBatchAsync(texts);

        // Assert
        result.Should().HaveCount(4);
        _aiMock.Verify(x => x.EmbedAsync(
            It.Is<AiEmbeddingsRequest>(r => r.Input.Count == 2), // Only uncached items
            It.IsAny<CancellationToken>()), Times.Once);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("2 from cache") && v.ToString()!.Contains("2 to generate")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EmbedBatchAsync_SkipsEmptyTexts()
    {
        // Arrange
        var texts = new[] { "valid1", "", "valid2", "   ", "valid3" };
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AiEmbeddingsResponse
            {
                Vectors = { _testEmbedding, _testEmbedding, _testEmbedding }
            });

        // Act
        var result = await _service.EmbedBatchAsync(texts);

        // Assert
        result.Should().HaveCount(3, "empty texts should be skipped");
        result.Keys.Should().Contain("valid1");
        result.Keys.Should().Contain("valid2");
        result.Keys.Should().Contain("valid3");
    }

    [Fact]
    public async Task EmbedBatchAsync_PreservesOrder()
    {
        // Arrange
        var texts = new[] { "zebra", "apple", "middle" };
        var embeddings = texts.Select((t, i) =>
            Enumerable.Range(i * 100, 384).Select(x => (float)x).ToArray()).ToList();

        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEmbeddingsResponse
            {
                Vectors = { embeddings[0], embeddings[1], embeddings[2] }
            });

        // Act
        var result = await _service.EmbedBatchAsync(texts);

        // Assert
        result["zebra"].Should().Equal(embeddings[0]);
        result["apple"].Should().Equal(embeddings[1]);
        result["middle"].Should().Equal(embeddings[2]);
    }

    [Fact]
    public async Task EmbedBatchAsync_MismatchedVectorCount_ThrowsException()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEmbeddingsResponse { Vectors = { _testEmbedding } }); // Only 1 vector for 3 texts

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.EmbedBatchAsync(texts);
        });
    }

    #endregion

    #region Error Scenarios Tests

    [Fact]
    public async Task EmbedAsync_ProviderReturnsNoVectors_ThrowsException()
    {
        // Arrange
        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEmbeddingsResponse { Vectors = { } }); // Empty vectors

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.EmbedAsync("test");
        });
    }

    [Fact]
    public async Task EmbedAsync_CancellationRequested_PropagatesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _aiMock.Setup(x => x.EmbedAsync(It.IsAny<AiEmbeddingsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _service.EmbedAsync("test", cts.Token);
        });
    }

    #endregion
}
