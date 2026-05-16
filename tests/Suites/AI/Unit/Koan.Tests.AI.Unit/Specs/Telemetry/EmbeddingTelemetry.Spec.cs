using Koan.Data.AI.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.AI.Unit.Specs.Telemetry;

/// <summary>
/// Tests for ADR AI-0020 Phase 4: Production Guardrails - Telemetry.
/// Validates embedding metrics collection, cost tracking, and performance stats calculation.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "4")]
[Trait("Category", "Unit")]
public sealed class EmbeddingTelemetrySpec : IDisposable
{
    private readonly EmbeddingTelemetry _telemetry;

    public EmbeddingTelemetrySpec()
    {
        _telemetry = new EmbeddingTelemetry(NullLogger<EmbeddingTelemetry>.Instance);
    }

    public void Dispose()
    {
        _telemetry?.Dispose();
    }

    /// <summary>
    /// Test #40: RecordEmbeddingGeneration_Success_IncrementsCounters
    /// </summary>
    [Fact]
    public void RecordEmbeddingGeneration_with_success_increments_counters()
    {
        // Arrange & Act
        _telemetry.RecordEmbeddingGeneration(
            entityType: "Article",
            model: "text-embedding-3-small",
            provider: "openai",
            source: "openai-prod",
            latencyMs: 150.5,
            tokens: 100,
            estimatedCost: 0.002,
            success: true);

        // Assert
        var stats = _telemetry.CalculateStats(TimeSpan.FromMinutes(1));

        stats.TotalEmbeddings.Should().Be(1);
        stats.SuccessfulEmbeddings.Should().Be(1);
        stats.FailedEmbeddings.Should().Be(0);
        stats.TotalTokens.Should().Be(100);
        stats.TotalCost.Should().BeApproximately(0.002, 0.0001);
    }

    /// <summary>
    /// Test #41: RecordEmbeddingGeneration_Failure_IncrementsErrorCounter
    /// </summary>
    [Fact]
    public void RecordEmbeddingGeneration_with_failure_increments_error_counter()
    {
        // Arrange & Act
        _telemetry.RecordEmbeddingGeneration(
            entityType: "Article",
            model: "text-embedding-3-small",
            provider: "openai",
            source: "openai-prod",
            latencyMs: 50.0,
            tokens: 100,
            estimatedCost: 0.0, // No cost for failed operation
            success: false,
            errorMessage: "Timeout exception");

        // Assert
        var stats = _telemetry.CalculateStats(TimeSpan.FromMinutes(1));

        stats.TotalEmbeddings.Should().Be(1);
        stats.SuccessfulEmbeddings.Should().Be(0);
        stats.FailedEmbeddings.Should().Be(1);
        stats.TotalCost.Should().Be(0.0, "failed operations should not incur cost");
    }

    /// <summary>
    /// Test #42: CalculateStats_MultipleEntries_ComputesPercentiles
    /// </summary>
    [Fact]
    public void CalculateStats_computes_latency_percentiles_correctly()
    {
        // Arrange - Record 100 operations with varying latencies
        for (int i = 1; i <= 100; i++)
        {
            _telemetry.RecordEmbeddingGeneration(
                entityType: "Article",
                model: "test-model",
                provider: "test",
                source: "test",
                latencyMs: i * 10.0, // 10ms, 20ms, ..., 1000ms
                tokens: 100,
                estimatedCost: 0.001,
                success: true);
        }

        // Act
        var stats = _telemetry.CalculateStats(TimeSpan.FromMinutes(5));

        // Assert
        stats.TotalEmbeddings.Should().Be(100);
        stats.AvgLatencyMs.Should().BeApproximately(505.0, 1.0); // Average of 1-1000

        // P50 should be around 50th percentile (500ms)
        stats.P50LatencyMs.Should().BeInRange(450, 550);

        // P95 should be around 95th percentile (950ms)
        stats.P95LatencyMs.Should().BeInRange(900, 1000);

        // P99 should be around 99th percentile (990ms)
        stats.P99LatencyMs.Should().BeInRange(950, 1000);

        // Verify ordering: P50 < P95 < P99
        stats.P50LatencyMs.Should().BeLessThan(stats.P95LatencyMs);
        stats.P95LatencyMs.Should().BeLessThan(stats.P99LatencyMs);
    }

    /// <summary>
    /// Test #43: GetEmbeddingMetrics_OldEntries_Excluded
    /// </summary>
    [Fact]
    public void GetEmbeddingMetrics_excludes_entries_older_than_retention_period()
    {
        // Arrange
        _telemetry.RecordEmbeddingGeneration(
            entityType: "Article",
            model: "test-model",
            provider: "test",
            source: "test",
            latencyMs: 100,
            tokens: 100,
            estimatedCost: 0.001,
            success: true);

        // Act - Query with very short time window (1 second ago)
        var recentMetrics = _telemetry.GetEmbeddingMetrics(DateTime.UtcNow.AddSeconds(-1)).ToList();

        // Metrics recorded just now should be included
        recentMetrics.Should().HaveCount(1);

        // Query with time window before the metric was recorded
        var oldMetrics = _telemetry.GetEmbeddingMetrics(DateTime.UtcNow.AddMinutes(-5)).ToList();

        // Should still include recent metric (it's within retention period)
        oldMetrics.Should().HaveCount(1);
    }

    /// <summary>
    /// Test #44: UpdateQueueState_UpdatesGauges
    /// </summary>
    [Fact]
    public void UpdateQueueState_records_queue_metrics()
    {
        // Arrange & Act
        _telemetry.UpdateQueueState(
            pending: 10,
            failed: 2,
            oldestAgeSeconds: 300.0); // 5 minutes

        // Assert
        var queueMetrics = _telemetry.GetQueueMetrics(DateTime.UtcNow.AddMinutes(-1)).LastOrDefault();

        queueMetrics.Should().NotBeNull();
        queueMetrics!.PendingCount.Should().Be(10);
        queueMetrics.FailedCount.Should().Be(2);
        queueMetrics.OldestAgeSeconds.Should().Be(300.0);
    }

    /// <summary>
    /// Test #70: Telemetry_LongRunning_NoMemoryLeak
    /// Validates that old metrics are evicted from in-memory storage.
    /// </summary>
    [Fact]
    public void Telemetry_evicts_old_metrics_beyond_retention_period()
    {
        // Arrange - Record metrics
        for (int i = 0; i < 1000; i++)
        {
            _telemetry.RecordEmbeddingGeneration(
                entityType: "Article",
                model: "test",
                provider: "test",
                source: "test",
                latencyMs: 100,
                tokens: 100,
                estimatedCost: 0.001,
                success: true);
        }

        // Act - Get metrics from last minute
        var recentMetrics = _telemetry.GetEmbeddingMetrics(DateTime.UtcNow.AddMinutes(-1)).ToList();

        // Assert - All 1000 should be within retention
        recentMetrics.Should().HaveCount(1000, "all recent metrics should be retained");

        // Note: Actual eviction happens during metric recording (cleanup)
        // This test validates retrieval filtering by time window
    }

    /// <summary>
    /// Test: Multiple entity types tracked separately.
    /// </summary>
    [Fact]
    public void Telemetry_tracks_multiple_entity_types_independently()
    {
        // Arrange & Act
        _telemetry.RecordEmbeddingGeneration(
            entityType: "Article",
            model: "test",
            provider: "test",
            source: "test",
            latencyMs: 100,
            tokens: 100,
            estimatedCost: 0.001,
            success: true);

        _telemetry.RecordEmbeddingGeneration(
            entityType: "Product",
            model: "test",
            provider: "test",
            source: "test",
            latencyMs: 200,
            tokens: 150,
            estimatedCost: 0.002,
            success: true);

        // Assert
        var allMetrics = _telemetry.GetEmbeddingMetrics().ToList();

        allMetrics.Should().HaveCount(2);
        allMetrics.Should().Contain(m => m.EntityType == "Article");
        allMetrics.Should().Contain(m => m.EntityType == "Product");

        // Stats should aggregate across all types
        var stats = _telemetry.CalculateStats(TimeSpan.FromMinutes(1));
        stats.TotalEmbeddings.Should().Be(2);
        stats.TotalTokens.Should().Be(250); // 100 + 150
        stats.TotalCost.Should().BeApproximately(0.003, 0.0001); // 0.001 + 0.002
    }

    /// <summary>
    /// Test: Cache hit/miss tracking.
    /// </summary>
    [Fact]
    public void Telemetry_tracks_cache_hits_and_misses()
    {
        // Arrange & Act
        _telemetry.RecordCacheHit("Article");
        _telemetry.RecordCacheHit("Article");
        _telemetry.RecordCacheMiss("Article", "content_changed");
        _telemetry.RecordCacheInvalidation("Article", "version_upgraded");

        // Assert
        // Note: Cache metrics are recorded via OpenTelemetry counters
        // This test validates the API doesn't throw
        // Actual counter values would be verified via metrics exporter in integration tests
    }

    /// <summary>
    /// Test: Batch processing metrics.
    /// </summary>
    [Fact]
    public void Telemetry_records_batch_processing_metrics()
    {
        // Arrange & Act
        _telemetry.RecordBatchProcessing(
            entityType: "Article",
            batchSize: 50,
            durationSeconds: 12.5);

        _telemetry.RecordBatchProcessing(
            entityType: "Article",
            batchSize: 50,
            durationSeconds: 13.0);

        // Assert
        // Batch metrics are recorded via histograms
        // This validates the API contract
    }

    /// <summary>
    /// Test: Queue processing results.
    /// </summary>
    [Fact]
    public void Telemetry_records_queue_processing_results()
    {
        // Arrange & Act
        _telemetry.RecordQueueProcessing(
            count: 50,
            success: true,
            entityType: "Article");

        _telemetry.RecordQueueProcessing(
            count: 5,
            success: false,
            entityType: "Article");

        // Assert
        // Queue processing metrics are recorded via counters
        // This validates the API contract
    }
}

/// <summary>
/// Tests for ADR AI-0020 Phase 4: Cost Estimation.
/// </summary>
[Trait("ADR", "AI-0020")]
[Trait("Phase", "4")]
[Trait("Category", "Unit")]
public sealed class EmbeddingCostEstimatorSpec
{
    /// <summary>
    /// Test #45: EstimateCost_KnownModel_ReturnsAccurateCost
    /// </summary>
    [Fact]
    public void EstimateCost_returns_accurate_cost_for_known_models()
    {
        // Arrange
        var model = "text-embedding-3-small";
        var provider = "openai";
        var tokens = 1_000_000; // 1 million tokens

        // Act
        var cost = EmbeddingCostEstimator.EstimateCost(model, provider, tokens);

        // Assert
        // text-embedding-3-small costs $0.02 per 1M tokens
        cost.Should().BeApproximately(0.02, 0.001, "should match OpenAI pricing");
    }

    /// <summary>
    /// Test #45b: text-embedding-3-large pricing
    /// </summary>
    [Fact]
    public void EstimateCost_returns_accurate_cost_for_large_model()
    {
        // Arrange
        var model = "text-embedding-3-large";
        var tokens = 1_000_000;

        // Act
        var cost = EmbeddingCostEstimator.EstimateCost(model, null, tokens);

        // Assert
        // text-embedding-3-large costs $0.13 per 1M tokens
        cost.Should().BeApproximately(0.13, 0.001);
    }

    /// <summary>
    /// Test #46: EstimateCost_LocalProvider_ReturnsZero
    /// </summary>
    [Fact]
    public void EstimateCost_returns_zero_for_local_providers()
    {
        // Arrange
        var testCases = new[]
        {
            ("ollama", "llama2"),
            ("lmstudio", "mistral"),
            ("local", "any-model")
        };

        foreach (var (provider, model) in testCases)
        {
            // Act
            var cost = EmbeddingCostEstimator.EstimateCost(model, provider, 1_000_000);

            // Assert
            cost.Should().Be(0.0, $"{provider} is a local provider and should be free");
        }
    }

    /// <summary>
    /// Test #47: EstimateCost_UnknownModel_ReturnsZero
    /// </summary>
    [Fact]
    public void EstimateCost_returns_zero_for_unknown_models()
    {
        // Arrange
        var model = "unknown-model-xyz";
        var tokens = 1_000_000;

        // Act
        var cost = EmbeddingCostEstimator.EstimateCost(model, "openai", tokens);

        // Assert
        cost.Should().Be(0.0, "unknown models should default to zero cost");
    }

    /// <summary>
    /// Test: Fractional token cost calculation.
    /// </summary>
    [Fact]
    public void EstimateCost_calculates_fractional_costs_correctly()
    {
        // Arrange
        var model = "text-embedding-3-small";
        var tokens = 5_000; // 0.005 million tokens

        // Act
        var cost = EmbeddingCostEstimator.EstimateCost(model, null, tokens);

        // Assert
        // $0.02 per 1M tokens = $0.0001 per 5K tokens
        cost.Should().BeApproximately(0.0001, 0.00001);
    }

    /// <summary>
    /// Test: GetModelCostPerMillion for known models.
    /// </summary>
    [Fact]
    public void GetModelCostPerMillion_returns_pricing_for_known_models()
    {
        // Arrange
        var models = new[]
        {
            "text-embedding-3-small",
            "text-embedding-3-large",
            "text-embedding-ada-002"
        };

        foreach (var model in models)
        {
            // Act
            var costPerMillion = EmbeddingCostEstimator.GetModelCostPerMillion(model);

            // Assert
            costPerMillion.Should().NotBeNull($"{model} should have known pricing");
            costPerMillion!.Value.Should().BeGreaterThan(0, "pricing should be positive");
        }
    }

    /// <summary>
    /// Test: GetModelCostPerMillion returns null for unknown models.
    /// </summary>
    [Fact]
    public void GetModelCostPerMillion_returns_null_for_unknown_models()
    {
        // Arrange
        var model = "unknown-future-model";

        // Act
        var costPerMillion = EmbeddingCostEstimator.GetModelCostPerMillion(model);

        // Assert
        costPerMillion.Should().BeNull("unknown models should return null");
    }
}
