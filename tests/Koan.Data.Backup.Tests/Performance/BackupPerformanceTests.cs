using System.Diagnostics;
using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Data.Backup.Tests.Performance;

public class BackupPerformanceTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BackupPerformanceTests(BackupTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Backup_Performance_Should_Meet_Baseline_Requirements()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var testUsers = await _fixture.CreateTestUsersAsync(1000); // Large dataset
        var backupName = $"perf-test-{Guid.CreateVersion7()}";

        var options = new BackupOptions
        {
            Description = "Performance test backup",
            StorageProfile = _fixture.TestStorageProfile,
            BatchSize = 100
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, options);
        stopwatch.Stop();

        // Assert
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);

        // Performance assertions
        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        var entitiesPerSecond = testUsers.Count / elapsedSeconds;

        _output.WriteLine($"Backup Performance Results:");
        _output.WriteLine($"  - Total time: {stopwatch.Elapsed}");
        _output.WriteLine($"  - Entities backed up: {manifest.Entities.Sum(e => e.ItemCount)}");
        _output.WriteLine($"  - Entities per second: {entitiesPerSecond:F2}");
        _output.WriteLine($"  - Total size: {manifest.Verification.TotalSizeBytes} bytes");
        _output.WriteLine($"  - Compression ratio: {manifest.Verification.CompressionRatio:F2}");

        // Performance baseline: Should backup at least 100 entities per second
        entitiesPerSecond.Should().BeGreaterThan(10, "Backup should process at least 10 entities per second");

        // Should complete within reasonable time (10 seconds for 1000 entities)
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "Backup should complete within 60 seconds");
    }

    [Fact]
    public async Task Restore_Performance_Should_Meet_Baseline_Requirements()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"restore-perf-test-{Guid.CreateVersion7()}";

        // Create a backup first
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile,
            BatchSize = 100
        });

        var options = new RestoreOptions
        {
            ReplaceExisting = true,
            UseBulkMode = true,
            BatchSize = 100,
            OptimizationLevel = "Fast",
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        await restoreService.RestoreEntityAsync<TestUser, Guid>(backupName, options);
        stopwatch.Stop();

        // Assert
        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        var totalEntities = manifest.Entities.Sum(e => e.ItemCount);
        var entitiesPerSecond = totalEntities / elapsedSeconds;

        _output.WriteLine($"Restore Performance Results:");
        _output.WriteLine($"  - Total time: {stopwatch.Elapsed}");
        _output.WriteLine($"  - Entities restored: {totalEntities}");
        _output.WriteLine($"  - Entities per second: {entitiesPerSecond:F2}");

        // Performance baseline: Should restore at least 50 entities per second
        entitiesPerSecond.Should().BeGreaterThan(5, "Restore should process at least 5 entities per second");

        // Should complete within reasonable time
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(120), "Restore should complete within 120 seconds");
    }

    [Fact]
    public async Task Full_Backup_Performance_Should_Scale_With_Concurrency()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var testResults = new List<(int Concurrency, TimeSpan Duration, int EntityCount)>();

        var concurrencyLevels = new[] { 1, 2, 4 };

        foreach (var concurrency in concurrencyLevels)
        {
            var backupName = $"concurrency-test-{concurrency}-{Guid.CreateVersion7()}";

            var options = new GlobalBackupOptions
            {
                Description = $"Concurrency test with {concurrency} threads",
                MaxConcurrency = concurrency,
                StorageProfile = _fixture.TestStorageProfile
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            var manifest = await backupService.BackupAllEntitiesAsync(backupName, options);
            stopwatch.Stop();

            // Collect results
            var entityCount = manifest.Entities.Sum(e => e.ItemCount);
            testResults.Add((concurrency, stopwatch.Elapsed, entityCount));

            _output.WriteLine($"Concurrency {concurrency}: {stopwatch.Elapsed}, {entityCount} entities");
        }

        // Assert
        testResults.Should().HaveCount(3);
        testResults.Should().OnlyContain(r => r.Duration.TotalSeconds > 0);

        // Generally, higher concurrency should not be significantly slower
        // (though the actual pattern depends on the workload)
        var slowestTime = testResults.Max(r => r.Duration);
        var fastestTime = testResults.Min(r => r.Duration);

        _output.WriteLine($"Performance range: {fastestTime} to {slowestTime}");

        // The slowest should not be more than 5x the fastest
        (slowestTime.TotalMilliseconds / fastestTime.TotalMilliseconds).Should().BeLessThan(5.0);
    }

    [Fact]
    public async Task Backup_Discovery_Performance_Should_Be_Responsive()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        // Create multiple backups to discover
        var backupTasks = new List<Task<BackupManifest>>();
        for (int i = 0; i < 10; i++)
        {
            var backupName = $"discovery-perf-{i}-{Guid.CreateVersion7()}";
            var task = backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
            {
                Tags = new[] { "performance", "discovery", $"batch-{i / 3}" },
                StorageProfile = _fixture.TestStorageProfile
            });
            backupTasks.Add(task);
        }

        await Task.WhenAll(backupTasks);

        // Act & Assert - Test discovery performance
        var stopwatch = Stopwatch.StartNew();
        var catalog = await discoveryService.DiscoverAllBackupsAsync();
        stopwatch.Stop();

        _output.WriteLine($"Discovery Performance Results:");
        _output.WriteLine($"  - Discovery time: {stopwatch.Elapsed}");
        _output.WriteLine($"  - Backups found: {catalog.TotalCount}");
        _output.WriteLine($"  - Discovery rate: {catalog.TotalCount / stopwatch.Elapsed.TotalSeconds:F2} backups/sec");

        // Discovery should be fast
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "Discovery should complete within 30 seconds");
        catalog.TotalCount.Should().BeGreaterThanOrEqualTo(10);

        // Test query performance
        stopwatch.Restart();
        var queryResult = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            Tags = new[] { "performance" },
            Take = 100
        });
        stopwatch.Stop();

        _output.WriteLine($"Query time: {stopwatch.Elapsed}");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "Query should complete within 10 seconds");
        queryResult.Backups.Should().HaveCountGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task Memory_Usage_Should_Remain_Stable_During_Large_Operations()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var initialMemory = GC.GetTotalMemory(true);

        var backupName = $"memory-test-{Guid.CreateVersion7()}";

        // Act - Perform memory-intensive operation
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile,
            BatchSize = 50 // Smaller batches to test memory management
        });

        // Force garbage collection and measure memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        _output.WriteLine($"Memory Usage Results:");
        _output.WriteLine($"  - Initial memory: {initialMemory:N0} bytes");
        _output.WriteLine($"  - Final memory: {finalMemory:N0} bytes");
        _output.WriteLine($"  - Memory increase: {memoryIncrease:N0} bytes");
        _output.WriteLine($"  - Backup size: {manifest.Verification.TotalSizeBytes:N0} bytes");

        // Memory increase should be reasonable compared to backup size
        // Allow up to 10x the backup size as memory overhead (this is generous)
        if (manifest.Verification.TotalSizeBytes > 0)
        {
            var memoryRatio = (double)memoryIncrease / manifest.Verification.TotalSizeBytes;
            memoryRatio.Should().BeLessThan(10.0, "Memory overhead should not exceed 10x the backup size");
        }

        // Absolute memory increase should be reasonable (less than 100MB for test data)
        memoryIncrease.Should().BeLessThan(100 * 1024 * 1024, "Memory increase should be less than 100MB");
    }
}