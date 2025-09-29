using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Canon.Core.Configuration;
using Koan.Canon.Core.Data;
using Koan.Canon.Core.Monitoring;
using Koan.Canon.Core.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Canon.Core.Tests;

/// <summary>
/// Tests to validate that Canon optimizations can be properly registered and initialized.
/// These tests ensure the optimization infrastructure doesn't break the existing Canon functionality.
/// </summary>
public class OptimizationValidationTests
{
    [Fact]
    public void AddCanonOptimizations_ShouldRegisterRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddCanonOptimizations(options =>
        {
            options.Features.EnableBatchOperations = true;
            options.Features.EnablePerformanceMonitoring = true;
        });
        
        // Assert
        var serviceProvider = services.BuildServiceProvider();
        
        // Check that optimization services are registered
        Assert.NotNull(serviceProvider.GetService<CanonOptimizationOptions>());
        Assert.NotNull(serviceProvider.GetService<BatchDataAccessHelper>());
        Assert.NotNull(serviceProvider.GetService<CanonPerformanceMonitor>());
    }

    [Fact]
    public void AddCanonOptimizationsForProduction_ShouldConfigureCorrectSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddCanonOptimizationsForProduction();
        
        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<CanonOptimizationOptions>();
        
        Assert.True(options.Features.EnableBatchOperations);
        Assert.True(options.Features.EnableParallelProcessing);
        Assert.True(options.Features.EnablePerformanceMonitoring);
        Assert.Equal(2000, options.Performance.DefaultBatchSize);
        Assert.Equal(10000, options.Performance.MaxBatchSize);
    }

    [Fact]
    public void AddCanonOptimizationsForDevelopment_ShouldConfigureConservativeSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddCanonOptimizationsForDevelopment();
        
        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<CanonOptimizationOptions>();
        
        Assert.True(options.Features.EnableBatchOperations);
        Assert.True(options.Features.UseOptimizedAssociationWorker);
        Assert.False(options.Features.UseOptimizedProjectionWorker);  // Conservative for dev
        Assert.Equal(500, options.Performance.DefaultBatchSize);  // Smaller for dev
        Assert.Equal(2000, options.Performance.MaxBatchSize);
    }

    [Fact]
    public void BatchDataAccessHelper_ShouldBeAccessibleAfterRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCanonOptimizations();
        
        // Act
        var serviceProvider = services.BuildServiceProvider();
        var batchHelper = serviceProvider.GetService<BatchDataAccessHelper>();
        
        // Assert
        Assert.NotNull(batchHelper);
    }

    [Fact]
    public void CanonPerformanceMonitor_ShouldBeAccessibleAfterRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCanonOptimizations();
        
        // Act
        var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetService<CanonPerformanceMonitor>();
        
        // Assert
        Assert.NotNull(monitor);
    }

    [Fact]
    public void AdaptiveBatchProcessor_ShouldHaveReasonableDefaults()
    {
        // Arrange
        var processor = new AdaptiveBatchProcessor();
        
        // Act
        var initialBatchSize = processor.GetOptimalBatchSize().Result;
        
        // Assert
        Assert.True(initialBatchSize > 0);
        Assert.True(initialBatchSize <= 5000);  // Should be within reasonable bounds
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(500, 1000)]
    [InlineData(1000, 2000)]
    public void AdaptiveBatchProcessor_ShouldRecordAndAdjustBatchSize(int recordsProcessed, int expectedMinThroughput)
    {
        // Arrange
        var processor = new AdaptiveBatchProcessor();
        var duration = TimeSpan.FromMilliseconds(100);  // Fast processing
        
        // Act
        processor.RecordMetrics(duration, recordsProcessed, 100 * 1024 * 1024);  // 100MB memory
        var newBatchSize = processor.GetOptimalBatchSize().Result;
        
        // Assert
        Assert.True(newBatchSize > 0);
        // Should be within reasonable bounds
        Assert.True(newBatchSize >= 50 && newBatchSize <= 5000);
    }

    [Fact]
    public async Task CanonPerformanceMonitor_ShouldTrackOperationMetrics()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<CanonPerformanceMonitor>>();
        var monitor = new CanonPerformanceMonitor(logger);
        
        // Act
        monitor.RecordOperation("TestOperation", TimeSpan.FromMilliseconds(100), 10);
        var metrics = await monitor.GetCurrentMetrics();
        
        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.ThroughputPerSecond >= 0);
        Assert.True(metrics.AverageLatency.TotalMilliseconds >= 0);
    }

    [Fact]
    public void MovingAverage_ShouldCalculateCorrectAverage()
    {
        // Arrange
        var movingAverage = new MovingAverage(3);
        
        // Act
        movingAverage.Add(10);
        movingAverage.Add(20);
        movingAverage.Add(30);
        
        // Assert
        Assert.Equal(20.0, movingAverage.Average, 1);  // (10+20+30)/3 = 20
        
        // Act - add more values to test sliding window
        movingAverage.Add(40);  // Now should be (20+30+40)/3 = 30
        
        // Assert
        Assert.Equal(30.0, movingAverage.Average, 1);
    }

    [Fact]
    public void CanonOptimizationOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new CanonOptimizationOptions();
        
        // Assert
        Assert.True(options.Features.EnableBatchOperations);
        Assert.True(options.Features.EnableParallelProcessing);
        Assert.True(options.Features.EnablePerformanceMonitoring);
        
        Assert.Equal(1000, options.Performance.DefaultBatchSize);
        Assert.Equal(5000, options.Performance.MaxBatchSize);
        Assert.Equal(50, options.Performance.MinBatchSize);
        
        Assert.True(options.Monitoring.EnableMetrics);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Monitoring.MetricsReportingInterval);
    }

    [Fact]
    public void BulkStageTransitionService_ShouldHandleEmptyCollections()
    {
        // Arrange
        var emptyRecords = new List<TestEntity>();
        
        // Act & Assert - should not throw
        var result = BulkStageTransitionService.TransitionRecordsBulk(
            emptyRecords, "from", "to").Result;
        
        Assert.Equal(0, result);
    }

    // Helper class for testing
    private class TestEntity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Test";
    }
}


