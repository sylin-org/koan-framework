using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Core.Configuration;
using Koan.Canon.Core.Monitoring;
using Koan.Canon.Core.Services;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Canon.Core.Tests;

public class OptimizationValidationTests
{
    [Fact]
    public void AddCanonOptimizations_ShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCanonOptimizations(options =>
        {
            options.Features.EnableBatchOperations = true;
            options.Features.EnablePerformanceMonitoring = true;
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CanonOptimizationOptions>().Should().NotBeNull();
        provider.GetRequiredService<AdaptiveBatchProcessor>().Should().NotBeNull();
        provider.GetRequiredService<CanonPerformanceMonitor>().Should().NotBeNull();

        var hosted = provider.GetServices<IHostedService>();
        hosted.Any(s => s.GetType().Name.Contains("PerformanceMonitoring", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("performance monitoring hosted service should be registered when monitoring is enabled");
    }

    [Fact]
    public void ProductionPreset_EnablesHighThroughputDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCanonOptimizationsForProduction();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<CanonOptimizationOptions>();

        options.Features.EnableBatchOperations.Should().BeTrue();
        options.Features.EnableParallelProcessing.Should().BeTrue();
        options.Features.EnableAdaptiveBatching.Should().BeTrue();
    options.Performance.DefaultBatchSize.Should().Be(2000);
    options.Performance.MaxBatchSize.Should().Be(10000);
    }

    [Fact]
    public void DevelopmentPreset_RemainsConservative()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCanonOptimizationsForDevelopment();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<CanonOptimizationOptions>();

        options.Features.EnableBatchOperations.Should().BeTrue();
        options.Features.EnableParallelProcessing.Should().BeTrue();
        options.Features.UseOptimizedProjectionWorker.Should().BeFalse();
    options.Performance.DefaultBatchSize.Should().Be(500);
    options.Performance.MaxBatchSize.Should().Be(2000);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(500, 50)]
    public async Task AdaptiveBatchProcessor_RespondsToThroughput(int processed, int expectedMinimum)
    {
        var processor = new AdaptiveBatchProcessor();
        var initial = await processor.GetOptimalBatchSize();
        initial.Should().BeGreaterThan(0);

    processor.RecordMetrics(TimeSpan.FromMilliseconds(200), processed, 50 * 1024D * 1024D);
        var adjusted = await processor.GetOptimalBatchSize();

        adjusted.Should().BeGreaterThanOrEqualTo(expectedMinimum);
    adjusted.Should().BeLessThanOrEqualTo(2000);
    }

    [Fact]
    public async Task PerformanceMonitor_StartsAndStopsReporting()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var monitor = new CanonPerformanceMonitor(loggerFactory.CreateLogger<CanonPerformanceMonitor>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        monitor.StartPerformanceReporting(TimeSpan.FromMilliseconds(50), cts.Token);
        monitor.RecordMetrics("test", TimeSpan.FromMilliseconds(40), 10, 10 * 1024 * 1024);

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        monitor.StopPerformanceReporting();
    }

    [Fact]
    public async Task BulkStageTransitionService_ReturnsZero_ForEmptyBatch()
    {
        var transitioned = await BulkStageTransitionService.TransitionRecordsBulk(TestStageRecord.Empty, "intake", "keyed");
        transitioned.Should().Be(0);
    }

    private sealed record TestStageRecord(string Id) : IEntity<string>
    {
        public static IEnumerable<TestStageRecord> Empty => Array.Empty<TestStageRecord>();
    }
}

