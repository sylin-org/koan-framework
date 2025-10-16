using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Koan.Core.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using S14.AdapterBench.Hubs;
using S14.AdapterBench.Models;

namespace S14.AdapterBench.Services;

public class BenchmarkService : IBenchmarkService
{
    private static readonly KoanLog.KoanLogScope _log = KoanLog.For<BenchmarkService>();
    private readonly ILogger _logger;
    private static readonly string[] DefaultProviders = { "sqlite", "postgres", "mongo", "redis" };

    // Simple constructor - no SignalR dependency
    public BenchmarkService(ILogger<BenchmarkService>? logger = null)
    {
        _logger = logger ?? NullLogger<BenchmarkService>.Instance;
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        BenchmarkRequest request,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            Mode = request.Mode,
            Scale = request.Scale,
            EntityCount = GetEntityCount(request.Scale, request.CustomEntityCount)
        };

        var providers = request.Providers.Count > 0 ? request.Providers : DefaultProviders.ToList();
        _log.ServiceInfo("benchmark.start", null,
            ("mode", request.Mode),
            ("scale", request.Scale),
            ("entityCount", result.EntityCount),
            ("providers", string.Join(", ", providers)),
            ("tiers", string.Join(", ", request.EntityTiers)));

        try
        {
            if (request.Mode == BenchmarkMode.Sequential)
            {
                _log.ServiceInfo("benchmark.execute", "sequential", ("providerCount", providers.Count));
                await RunSequentialBenchmarkAsync(result, providers, request.EntityTiers, progress, cancellationToken);
            }
            else
            {
                _log.ServiceInfo("benchmark.execute", "parallel", ("providerCount", providers.Count));
                await RunParallelBenchmarkAsync(result, providers, request.EntityTiers, progress, cancellationToken);
            }

            result.Status = BenchmarkStatus.Completed;
        }
        catch (Exception ex)
        {
            result.Status = BenchmarkStatus.Failed;
            var elapsed = DateTime.UtcNow - result.StartedAt;
            _log.ServiceError("benchmark.run", "failed", ("elapsedMs", elapsed.TotalMilliseconds), ("error", ex.Message));
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
            var total = result.CompletedAt.Value - result.StartedAt;
            _log.ServiceInfo("benchmark.run", "finished", ("status", result.Status), ("elapsedSeconds", total.TotalSeconds));
        }

        return result;
    }

    private async Task RunSequentialBenchmarkAsync(
        BenchmarkResult result,
        List<string> providers,
        List<string> entityTiers,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 11 test types per provider per tier + cross-provider migrations
        var testsPerProviderPerTier = 11;
        var regularTests = providers.Count * entityTiers.Count * testsPerProviderPerTier;
        var migrationTests = providers.Count * (providers.Count - 1) * entityTiers.Count; // N*(N-1) provider pairs
        var totalTests = regularTests + migrationTests;
        var completedTests = 0;

        foreach (var provider in providers)
        {
            _logger.LogInformation("Provider {Provider} starting sequential benchmark across tiers [{Tiers}]",
                provider,
                string.Join(", ", entityTiers));

            var providerResult = new ProviderResult
            {
                ProviderName = provider,
                IsContainerized = provider != "sqlite"
            };

            var providerStopwatch = Stopwatch.StartNew();

            foreach (var tier in entityTiers)
            {
                // Single Write Test
                var singleWriteResult = await RunSingleWriteTestAsync(
                    provider, tier, result.EntityCount, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(singleWriteResult);

                // Batch Write Test
                var batchWriteResult = await RunBatchWriteTestAsync(
                    provider, tier, result.EntityCount, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(batchWriteResult);

                // Read By ID Test
                var readByIdResult = await RunReadByIdTestAsync(
                    provider, tier, result.EntityCount, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(readByIdResult);

                // Update Operations Test
                var updateResult = await RunUpdateTestAsync(
                    provider, tier, result.EntityCount, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(updateResult);

                // Query Simple Filter Test
                var querySimpleResult = await RunQueryTestAsync(
                    provider, tier, "Simple Filter", progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(querySimpleResult);

                // Query Range Filter Test
                var queryRangeResult = await RunQueryTestAsync(
                    provider, tier, "Range Filter", progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(queryRangeResult);

                // Query Complex Filter Test
                var queryComplexResult = await RunQueryTestAsync(
                    provider, tier, "Complex Filter", progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(queryComplexResult);

                // Pagination Test
                var paginationResult = await RunPaginationTestAsync(
                    provider, tier, result.EntityCount, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(paginationResult);

                // RemoveAll Safe Test
                var removeAllSafeResult = await RunRemoveAllTestAsync(
                    provider, tier, result.EntityCount, "Safe", RemoveStrategy.Safe, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(removeAllSafeResult);

                // RemoveAll Fast Test
                var removeAllFastResult = await RunRemoveAllTestAsync(
                    provider, tier, result.EntityCount, "Fast", RemoveStrategy.Fast, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(removeAllFastResult);

                // RemoveAll Optimized Test
                var removeAllOptimizedResult = await RunRemoveAllTestAsync(
                    provider, tier, result.EntityCount, "Optimized", RemoveStrategy.Optimized, progress, completedTests++, totalTests, cancellationToken);
                providerResult.Tests.Add(removeAllOptimizedResult);
            }

            providerStopwatch.Stop();
            providerResult.TotalDuration = providerStopwatch.Elapsed;
            result.ProviderResults.Add(providerResult);

            _logger.LogInformation("Provider {Provider} completed sequential benchmark in {Duration:F2}s",
                provider,
                providerStopwatch.Elapsed.TotalSeconds);
        }

        // Cross-Provider Migration Tests
        _logger.LogInformation("Starting cross-provider migration tests between {Count} providers", providers.Count);
        foreach (var sourceProvider in providers)
        {
            foreach (var destProvider in providers)
            {
                if (sourceProvider == destProvider) continue; // Skip same-provider migration

                foreach (var tier in entityTiers)
                {
                    var migrationResult = await RunCrossProviderMigrationTestAsync(
                        sourceProvider, destProvider, tier, result.EntityCount,
                        progress, completedTests++, totalTests, cancellationToken);

                    // Add to both source and dest provider results for visibility
                    var sourceResult = result.ProviderResults.FirstOrDefault(p => p.ProviderName == sourceProvider);
                    sourceResult?.Tests.Add(migrationResult);
                }
            }
        }
    }

    private async Task RunParallelBenchmarkAsync(
        BenchmarkResult result,
        List<string> providers,
        List<string> entityTiers,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 11 test types per provider per tier + cross-provider migrations
        var testsPerProviderPerTier = 11;
        var regularTests = providers.Count * entityTiers.Count * testsPerProviderPerTier;
        var migrationTests = providers.Count * (providers.Count - 1) * entityTiers.Count;
        var totalTests = regularTests + migrationTests;
        var testsPerProvider = entityTiers.Count * testsPerProviderPerTier;
        var completedTests = 0;

        var providerResults = providers.Select(p => new ProviderResult
        {
            ProviderName = p,
            IsContainerized = p != "sqlite"
        }).ToArray();

        // Thread-safe per-provider progress tracking
        var providerProgressMap = new ConcurrentDictionary<string, Models.ProviderProgress>();
        foreach (var provider in providers)
        {
            providerProgressMap[provider] = new Models.ProviderProgress
            {
                ProviderName = provider,
                TotalTests = testsPerProvider,
                CompletedTests = 0,
                Status = "pending"
            };
        }

        var overallStopwatch = Stopwatch.StartNew();

        var providerTasks = providers.Select((provider, index) =>
            Task.Run(async () =>
            {
                _log.ServiceInfo("parallel.task.start", null,
                    ("provider", provider),
                    ("threadId", Thread.CurrentThread.ManagedThreadId));

                var providerResult = providerResults[index];
                var providerStopwatch = Stopwatch.StartNew();
                var providerTestsCompleted = 0;

                // Mark provider as running
                providerProgressMap[provider].Status = "running";
                ReportProgress();

                _log.ServiceInfo("parallel.status", "running", ("provider", provider));

                foreach (var tier in entityTiers)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _log.ServiceInfo("parallel.tier.start", null, ("provider", provider), ("tier", tier));

                    _log.ServiceInfo("parallel.test.start", "single-write", ("provider", provider), ("tier", tier));
                    var singleWrite = await RunSingleWriteTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        null, // Don't report to global progress from individual tests
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    _log.ServiceInfo("parallel.test.end", "single-write", ("provider", provider), ("tier", tier));
                    providerResult.Tests.Add(singleWrite);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Single Writes", ++providerTestsCompleted);

                    var batchWrite = await RunBatchWriteTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(batchWrite);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Batch Writes", ++providerTestsCompleted);

                    var readById = await RunReadByIdTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(readById);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Read By ID", ++providerTestsCompleted);

                    var update = await RunUpdateTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(update);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Update Operations", ++providerTestsCompleted);

                    var querySimple = await RunQueryTestAsync(
                        provider,
                        tier,
                        "Simple Filter",
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(querySimple);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Query (Simple)", ++providerTestsCompleted);

                    var queryRange = await RunQueryTestAsync(
                        provider,
                        tier,
                        "Range Filter",
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(queryRange);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Query (Range)", ++providerTestsCompleted);

                    var queryComplex = await RunQueryTestAsync(
                        provider,
                        tier,
                        "Complex Filter",
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(queryComplex);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Query (Complex)", ++providerTestsCompleted);

                    var pagination = await RunPaginationTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(pagination);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "Pagination", ++providerTestsCompleted);

                    var removeSafe = await RunRemoveAllTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        "Safe",
                        RemoveStrategy.Safe,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(removeSafe);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "RemoveAll (Safe)", ++providerTestsCompleted);

                    var removeFast = await RunRemoveAllTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        "Fast",
                        RemoveStrategy.Fast,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(removeFast);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "RemoveAll (Fast)", ++providerTestsCompleted);

                    var removeOptimized = await RunRemoveAllTestAsync(
                        provider,
                        tier,
                        result.EntityCount,
                        "Optimized",
                        RemoveStrategy.Optimized,
                        null,
                        Volatile.Read(ref completedTests),
                        totalTests,
                        cancellationToken);
                    providerResult.Tests.Add(removeOptimized);
                    Interlocked.Increment(ref completedTests);
                    UpdateProviderProgress(provider, tier, "RemoveAll (Optimized)", ++providerTestsCompleted);
                }

                providerStopwatch.Stop();
                providerResult.TotalDuration = providerStopwatch.Elapsed;

                // Mark provider as completed
                providerProgressMap[provider].Status = "completed";
                ReportProgress();

                _logger.LogInformation("Provider {Provider} completed parallel benchmark in {Duration:F2}s",
                    provider,
                    providerStopwatch.Elapsed.TotalSeconds);
            }, cancellationToken)).ToList();

        await Task.WhenAll(providerTasks);

        // Add all provider results (keep their actual durations, don't override)
        result.ProviderResults.AddRange(providerResults);

        // Cross-Provider Migration Tests (run sequentially after parallel provider tests)
        _logger.LogInformation("Starting cross-provider migration tests between {Count} providers", providers.Count);
        foreach (var sourceProvider in providers)
        {
            foreach (var destProvider in providers)
            {
                if (sourceProvider == destProvider) continue; // Skip same-provider migration

                foreach (var tier in entityTiers)
                {
                    var migrationResult = await RunCrossProviderMigrationTestAsync(
                        sourceProvider, destProvider, tier, result.EntityCount,
                        progress, completedTests++, totalTests, cancellationToken);

                    // Add to source provider results for visibility
                    var sourceResult = result.ProviderResults.FirstOrDefault(p => p.ProviderName == sourceProvider);
                    sourceResult?.Tests.Add(migrationResult);
                }
            }
        }

        overallStopwatch.Stop();

        var maxDuration = providerResults.Max(pr => pr.TotalDuration);
        _logger.LogInformation("Parallel benchmark finished. Total wall-clock time: {WallClockTime:F2}s, slowest provider: {MaxDuration:F2}s",
            overallStopwatch.Elapsed.TotalSeconds,
            maxDuration.TotalSeconds);

        // Log individual provider durations for transparency
        foreach (var pr in providerResults)
        {
            _logger.LogInformation("Provider {Provider} completed in {Duration:F2}s", pr.ProviderName, pr.TotalDuration.TotalSeconds);
        }

        // Local helper functions
        void UpdateProviderProgress(string providerName, string tier, string testName, int completed)
        {
            providerProgressMap[providerName].CompletedTests = completed;
            providerProgressMap[providerName].CurrentTest = $"{tier} - {testName}";
            ReportProgress();
        }

        void ReportProgress()
        {
            var progressData = new BenchmarkProgress
            {
                CurrentProvider = "Multiple", // Parallel mode
                CurrentTest = "Running in parallel",
                TotalTests = totalTests,
                CompletedTests = Volatile.Read(ref completedTests),
                ProviderProgress = new Dictionary<string, Models.ProviderProgress>(providerProgressMap)
            };

            // Report progress - the Job will forward this to SignalR
            progress?.Report(progressData);
        }
    }

    private async Task<TestResult> RunSingleWriteTestAsync(
        string provider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        _log.ServiceInfo("test.entry", "single-write",
            ("provider", provider),
            ("tier", tier),
            ("threadId", Thread.CurrentThread.ManagedThreadId));

        var testResult = new TestResult
        {
            TestName = "Single Writes",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true // Entity<T>.Save() is always native
        };

        try
        {
            _log.ServiceInfo("test.start", testResult.TestName,
                ("provider", provider),
                ("tier", tier),
                ("count", count));
            var stopwatch = Stopwatch.StartNew();

            _log.ServiceInfo("test.context.switch", "before", ("provider", provider));
            using (EntityContext.Adapter(provider))
            {
                _log.ServiceInfo("test.context.switch", "after", ("provider", provider));

                // Create and save entities one at a time
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entity = CreateEntity(tier, i);
                    await SaveEntityAsync(entity, tier);

                    if (i % 100 == 0 && i > 0)
                    {
                        var currentOpsPerSec = i / stopwatch.Elapsed.TotalSeconds;
                        _logger.LogDebug("Provider {Provider} - {Tier} Single Writes: {Current}/{Total} ({OpsPerSec:F0} ops/sec)",
                            provider, tier, i, count, currentOpsPerSec);

                        progress?.Report(new BenchmarkProgress
                        {
                            CurrentProvider = provider,
                            CurrentTest = $"{tier} - Single Writes",
                            TotalTests = totalTests,
                            CompletedTests = completedTests,
                            CurrentOperationCount = i,
                            TotalOperations = count,
                            CurrentOperationsPerSecond = currentOpsPerSec
                        });
                    }
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = count / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s ({OpsPerSecond:F0} ops/sec)",
                provider,
                tier,
                testResult.TestName,
                stopwatch.Elapsed.TotalSeconds,
                testResult.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunBatchWriteTestAsync(
        string provider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = "Batch Writes",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            const int batchSize = 500;
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test} ({Count} operations, batch size {BatchSize})",
                provider,
                tier,
                testResult.TestName,
                count,
                batchSize);

            _logger.LogDebug("Switching to provider context: {Provider}", provider);
            using (EntityContext.Adapter(provider))
            {
                var batchNumber = 0;
                for (int batchStart = 0; batchStart < count; batchStart += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchCount = Math.Min(batchSize, count - batchStart);
                    var batchStopwatch = Stopwatch.StartNew();

                    var entities = Enumerable.Range(batchStart, batchCount)
                        .Select(i => CreateEntity(tier, i))
                        .ToList();

                    await SaveEntitiesBatchAsync(entities, tier);
                    batchStopwatch.Stop();

                    batchNumber++;
                    var currentOpsPerSec = (batchStart + batchCount) / stopwatch.Elapsed.TotalSeconds;
                    _logger.LogDebug("Provider {Provider} - {Tier} Batch #{BatchNum}: saved {Count} entities in {Ms:F0}ms ({OpsPerSec:F0} ops/sec overall)",
                        provider, tier, batchNumber, batchCount, batchStopwatch.Elapsed.TotalMilliseconds, currentOpsPerSec);

                    progress?.Report(new BenchmarkProgress
                    {
                        CurrentProvider = provider,
                        CurrentTest = $"{tier} - Batch Writes",
                        TotalTests = totalTests,
                        CompletedTests = completedTests,
                        CurrentOperationCount = batchStart + batchCount,
                        TotalOperations = count,
                        CurrentOperationsPerSecond = currentOpsPerSec
                    });
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = count / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s ({OpsPerSecond:F0} ops/sec)",
                provider,
                tier,
                testResult.TestName,
                stopwatch.Elapsed.TotalSeconds,
                testResult.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunReadByIdTestAsync(
        string provider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = "Read By ID",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test} ({Count} reads)",
                provider,
                tier,
                testResult.TestName,
                count);
            List<string> ids;

            // First, get the IDs from the entities we wrote
            using (EntityContext.Adapter(provider))
            {
                ids = await GetEntityIdsAsync(tier, count);
            }

            if (ids.Count == 0)
            {
                testResult.Error = "No entities found to read";
                return testResult;
            }

            var stopwatch = Stopwatch.StartNew();

            using (EntityContext.Adapter(provider))
            {
                for (int i = 0; i < Math.Min(count, ids.Count); i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await GetEntityByIdAsync(tier, ids[i]);

                    if (i % 100 == 0)
                    {
                        progress?.Report(new BenchmarkProgress
                        {
                            CurrentProvider = provider,
                            CurrentTest = $"{tier} - Read By ID",
                            TotalTests = totalTests,
                            CompletedTests = completedTests,
                            CurrentOperationCount = i,
                            TotalOperations = count,
                            CurrentOperationsPerSecond = i / (stopwatch.Elapsed.TotalSeconds + 0.001)
                        });
                    }
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = count / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s ({OpsPerSecond:F0} ops/sec)",
                provider,
                tier,
                testResult.TestName,
                stopwatch.Elapsed.TotalSeconds,
                testResult.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunRemoveAllTestAsync(
        string provider,
        string tier,
        int count,
        string strategyName,
        RemoveStrategy strategy,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = $"RemoveAll ({strategyName})",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test} ({Count} entities)",
                provider,
                tier,
                testResult.TestName,
                count);

            _logger.LogDebug("Switching to provider context: {Provider}", provider);
            using (EntityContext.Adapter(provider))
            {
                // First, seed data using batch write
                const int batchSize = 500;
                var seedStopwatch = Stopwatch.StartNew();
                _logger.LogDebug("Provider {Provider} - {Tier} {Test}: Seeding {Count} entities before removal",
                    provider, tier, testResult.TestName, count);

                for (int batchStart = 0; batchStart < count; batchStart += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batchCount = Math.Min(batchSize, count - batchStart);
                    var entities = Enumerable.Range(batchStart, batchCount)
                        .Select(i => CreateEntity(tier, i))
                        .ToList();
                    await SaveEntitiesBatchAsync(entities, tier);
                }

                seedStopwatch.Stop();
                _logger.LogDebug("Provider {Provider} - {Tier} {Test}: Seeded {Count} entities in {Seconds:F2}s",
                    provider, tier, testResult.TestName, count, seedStopwatch.Elapsed.TotalSeconds);

                // Now measure RemoveAll with the specified strategy
                _logger.LogDebug("Provider {Provider} - {Tier} {Test}: Starting removal with strategy {Strategy}",
                    provider, tier, testResult.TestName, strategyName);
                var stopwatch = Stopwatch.StartNew();

                var deletedCount = await RemoveAllByStrategyAsync(tier, strategy);

                stopwatch.Stop();
                _logger.LogDebug("Provider {Provider} - {Tier} {Test}: Removed {Deleted} entities in {Ms:F0}ms",
                    provider, tier, testResult.TestName, deletedCount == -1 ? count : deletedCount, stopwatch.Elapsed.TotalMilliseconds);

                testResult.Duration = stopwatch.Elapsed;
                // For RemoveAll, ops/sec is based on count removed (or estimated if -1)
                var effectiveCount = deletedCount == -1 ? count : deletedCount;
                testResult.OperationsPerSecond = effectiveCount / stopwatch.Elapsed.TotalSeconds;

                progress?.Report(new BenchmarkProgress
                {
                    CurrentProvider = provider,
                    CurrentTest = $"{tier} - RemoveAll ({strategyName})",
                    TotalTests = totalTests,
                    CompletedTests = completedTests,
                    CurrentOperationCount = (int)effectiveCount,
                    TotalOperations = count,
                    CurrentOperationsPerSecond = testResult.OperationsPerSecond
                });

                _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s (removed {Removed} entities, {OpsPerSecond:F0} ops/sec)",
                    provider,
                    tier,
                    testResult.TestName,
                    stopwatch.Elapsed.TotalSeconds,
                    effectiveCount,
                    testResult.OperationsPerSecond);
            }
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<long> RemoveAllByStrategyAsync(string tier, RemoveStrategy strategy)
    {
        return tier switch
        {
            "Minimal" => await BenchmarkMinimal.RemoveAll(strategy),
            "Indexed" => await BenchmarkIndexed.RemoveAll(strategy),
            "Complex" => await BenchmarkComplex.RemoveAll(strategy),
            _ => 0
        };
    }

    private object CreateEntity(string tier, int index)
    {
        return tier switch
        {
            "Minimal" => new BenchmarkMinimal
            {
                CreatedAt = DateTime.UtcNow
            },
            "Indexed" => new BenchmarkIndexed
            {
                UserId = $"user_{index % 100}",
                Category = $"category_{index % 10}",
                Title = $"Benchmark Item {index}",
                Amount = (decimal)(index * 1.5),
                CreatedAt = DateTime.UtcNow
            },
            "Complex" => new BenchmarkComplex
            {
                UserId = $"user_{index % 100}",
                Email = $"user{index}@benchmark.test",
                FirstName = $"First{index}",
                LastName = $"Last{index}",
                Address = new Address
                {
                    Street = $"{index} Main St",
                    City = "Benchmark City",
                    State = "CA",
                    ZipCode = "90000",
                    Country = "US"
                },
                Tags = new List<string> { $"tag{index % 5}", $"tag{index % 10}" },
                Metadata = new Dictionary<string, string>
                {
                    ["key1"] = $"value{index}",
                    ["key2"] = $"value{index * 2}"
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            _ => throw new ArgumentException($"Unknown tier: {tier}")
        };
    }

    private async Task SaveEntityAsync(object entity, string tier)
    {
        switch (tier)
        {
            case "Minimal":
                await ((BenchmarkMinimal)entity).Save();
                break;
            case "Indexed":
                await ((BenchmarkIndexed)entity).Save();
                break;
            case "Complex":
                await ((BenchmarkComplex)entity).Save();
                break;
        }
    }

    private async Task SaveEntitiesBatchAsync(List<object> entities, string tier)
    {
        switch (tier)
        {
            case "Minimal":
                await entities.Cast<BenchmarkMinimal>().ToList().Save();
                break;
            case "Indexed":
                await entities.Cast<BenchmarkIndexed>().ToList().Save();
                break;
            case "Complex":
                await entities.Cast<BenchmarkComplex>().ToList().Save();
                break;
        }
    }

    private async Task<List<string>> GetEntityIdsAsync(string tier, int limit)
    {
        return tier switch
        {
            "Minimal" => (await BenchmarkMinimal.All()).Take(limit).Select(e => e.Id).ToList(),
            "Indexed" => (await BenchmarkIndexed.All()).Take(limit).Select(e => e.Id).ToList(),
            "Complex" => (await BenchmarkComplex.All()).Take(limit).Select(e => e.Id).ToList(),
            _ => new List<string>()
        };
    }

    private async Task<object?> GetEntityByIdAsync(string tier, string id)
    {
        return tier switch
        {
            "Minimal" => await BenchmarkMinimal.Get(id),
            "Indexed" => await BenchmarkIndexed.Get(id),
            "Complex" => await BenchmarkComplex.Get(id),
            _ => null
        };
    }

    private async Task<TestResult> RunUpdateTestAsync(
        string provider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = "Update Operations",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test} ({Count} updates)",
                provider, tier, testResult.TestName, count);

            List<string> ids;
            using (EntityContext.Adapter(provider))
            {
                ids = await GetEntityIdsAsync(tier, count);
            }

            if (ids.Count == 0)
            {
                testResult.Error = "No entities found to update";
                return testResult;
            }

            var stopwatch = Stopwatch.StartNew();
            using (EntityContext.Adapter(provider))
            {
                for (int i = 0; i < Math.Min(count, ids.Count); i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Load entity
                    var entity = await GetEntityByIdAsync(tier, ids[i]);
                    if (entity == null) continue;

                    // Modify entity based on tier
                    switch (tier)
                    {
                        case "Indexed":
                            var indexed = (BenchmarkIndexed)entity;
                            indexed.Amount += 10;
                            indexed.Title = $"Updated {i}";
                            await indexed.Save();
                            break;
                        case "Complex":
                            var complex = (BenchmarkComplex)entity;
                            complex.UpdatedAt = DateTime.UtcNow;
                            complex.FirstName = $"Updated{i}";
                            await complex.Save();
                            break;
                        case "Minimal":
                            var minimal = (BenchmarkMinimal)entity;
                            minimal.CreatedAt = DateTime.UtcNow;
                            await minimal.Save();
                            break;
                    }

                    if (i % 100 == 0 && i > 0)
                    {
                        progress?.Report(new BenchmarkProgress
                        {
                            CurrentProvider = provider,
                            CurrentTest = $"{tier} - Update Operations",
                            TotalTests = totalTests,
                            CompletedTests = completedTests,
                            CurrentOperationCount = i,
                            TotalOperations = count,
                            CurrentOperationsPerSecond = i / (stopwatch.Elapsed.TotalSeconds + 0.001)
                        });
                    }
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = Math.Min(count, ids.Count) / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s ({OpsPerSecond:F0} ops/sec)",
                provider, tier, testResult.TestName, stopwatch.Elapsed.TotalSeconds, testResult.OperationsPerSecond);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunQueryTestAsync(
        string provider,
        string tier,
        string filterType,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = $"Query ({filterType})",
            EntityTier = tier,
            OperationCount = 1, // Single query operation
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test}",
                provider, tier, testResult.TestName);

            var stopwatch = Stopwatch.StartNew();
            int resultCount = 0;

            using (EntityContext.Adapter(provider))
            {
                switch (tier)
                {
                    case "Indexed":
                        resultCount = filterType switch
                        {
                            "Simple Filter" => (await BenchmarkIndexed.Query(e => e.UserId == "user_50")).Count,
                            "Range Filter" => (await BenchmarkIndexed.Query(e => e.Amount > 100 && e.Amount < 500)).Count,
                            "Complex Filter" => (await BenchmarkIndexed.Query(e =>
                                e.Category == "category_5" &&
                                e.Amount > 200 &&
                                e.CreatedAt > DateTime.UtcNow.AddDays(-7))).Count,
                            _ => 0
                        };
                        break;
                    case "Complex":
                        resultCount = filterType switch
                        {
                            "Simple Filter" => (await BenchmarkComplex.Query(e => e.UserId == "user_50")).Count,
                            "Range Filter" => (await BenchmarkComplex.Query(e => e.Email.Contains("50"))).Count,
                            "Complex Filter" => (await BenchmarkComplex.Query(e =>
                                e.UserId == "user_50" &&
                                e.FirstName.StartsWith("First") &&
                                e.CreatedAt > DateTime.UtcNow.AddDays(-7))).Count,
                            _ => 0
                        };
                        break;
                    case "Minimal":
                        resultCount = filterType switch
                        {
                            "Simple Filter" => (await BenchmarkMinimal.Query(e => e.CreatedAt > DateTime.UtcNow.AddDays(-1))).Count,
                            "Range Filter" => (await BenchmarkMinimal.Query(e => e.CreatedAt > DateTime.UtcNow.AddDays(-7))).Count,
                            "Complex Filter" => (await BenchmarkMinimal.Query(e => e.CreatedAt > DateTime.UtcNow.AddDays(-30))).Count,
                            _ => 0
                        };
                        break;
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = 1 / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s (found {Count} results)",
                provider, tier, testResult.TestName, stopwatch.Elapsed.TotalSeconds, resultCount);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunPaginationTestAsync(
        string provider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = "Pagination",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Provider {Provider} starting {Tier} - {Test} (skip {Skip}, take {Take})",
                provider, tier, testResult.TestName, count / 2, 100);

            var stopwatch = Stopwatch.StartNew();
            int resultCount = 0;

            using (EntityContext.Adapter(provider))
            {
                // Test pagination: skip half, take 100
                var skip = Math.Max(0, count / 2);
                var take = 100;

                switch (tier)
                {
                    case "Minimal":
                        resultCount = (await BenchmarkMinimal.All()).Skip(skip).Take(take).Count();
                        break;
                    case "Indexed":
                        resultCount = (await BenchmarkIndexed.All()).Skip(skip).Take(take).Count();
                        break;
                    case "Complex":
                        resultCount = (await BenchmarkComplex.All()).Skip(skip).Take(take).Count();
                        break;
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = resultCount / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Provider {Provider} finished {Tier} - {Test} in {Duration:F2}s (retrieved {Count} entities)",
                provider, tier, testResult.TestName, stopwatch.Elapsed.TotalSeconds, resultCount);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Provider {Provider} failed {Tier} - {Test}", provider, tier, testResult.TestName);
        }

        return testResult;
    }

    private async Task<TestResult> RunCrossProviderMigrationTestAsync(
        string sourceProvider,
        string destProvider,
        string tier,
        int count,
        IProgress<BenchmarkProgress>? progress,
        int completedTests,
        int totalTests,
        CancellationToken cancellationToken)
    {
        var testResult = new TestResult
        {
            TestName = $"Migration ({sourceProvider} → {destProvider})",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true
        };

        try
        {
            _logger.LogInformation("Starting cross-provider migration: {Source} → {Dest}, tier {Tier} ({Count} entities)",
                sourceProvider, destProvider, tier, count);

            List<object> entities;
            var readStopwatch = Stopwatch.StartNew();

            // Read from source provider
            using (EntityContext.Adapter(sourceProvider))
            {
                switch (tier)
                {
                    case "Minimal":
                        entities = (await BenchmarkMinimal.All()).Take(count).Cast<object>().ToList();
                        break;
                    case "Indexed":
                        entities = (await BenchmarkIndexed.All()).Take(count).Cast<object>().ToList();
                        break;
                    case "Complex":
                        entities = (await BenchmarkComplex.All()).Take(count).Cast<object>().ToList();
                        break;
                    default:
                        testResult.Error = $"Unknown tier: {tier}";
                        return testResult;
                }
            }

            readStopwatch.Stop();
            var readOpsPerSec = entities.Count / readStopwatch.Elapsed.TotalSeconds;
            _logger.LogDebug("Read {Count} entities from {Source} in {Duration:F2}s ({OpsPerSec:F0} ops/sec)",
                entities.Count, sourceProvider, readStopwatch.Elapsed.TotalSeconds, readOpsPerSec);

            if (entities.Count == 0)
            {
                testResult.Error = $"No entities found in {sourceProvider}";
                return testResult;
            }

            // Write to destination provider
            var writeStopwatch = Stopwatch.StartNew();
            using (EntityContext.Adapter(destProvider))
            {
                const int batchSize = 500;
                for (int i = 0; i < entities.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = entities.Skip(i).Take(batchSize).ToList();
                    await SaveEntitiesBatchAsync(batch, tier);
                }
            }

            writeStopwatch.Stop();
            var writeOpsPerSec = entities.Count / writeStopwatch.Elapsed.TotalSeconds;
            _logger.LogDebug("Wrote {Count} entities to {Dest} in {Duration:F2}s ({OpsPerSec:F0} ops/sec)",
                entities.Count, destProvider, writeStopwatch.Elapsed.TotalSeconds, writeOpsPerSec);

            // Total duration includes both read and write
            testResult.Duration = readStopwatch.Elapsed + writeStopwatch.Elapsed;
            testResult.OperationsPerSecond = entities.Count / testResult.Duration.TotalSeconds;

            _logger.LogInformation("Completed migration {Source} → {Dest}, tier {Tier}: {Count} entities in {Duration:F2}s ({OpsPerSec:F0} ops/sec) [Read: {ReadOps:F0} ops/sec, Write: {WriteOps:F0} ops/sec]",
                sourceProvider, destProvider, tier, entities.Count, testResult.Duration.TotalSeconds,
                testResult.OperationsPerSecond, readOpsPerSec, writeOpsPerSec);
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
            _logger.LogError(ex, "Migration failed {Source} → {Dest}, tier {Tier}", sourceProvider, destProvider, tier);
        }

        return testResult;
    }

    private int GetEntityCount(BenchmarkScale scale, int? customCount = null)
    {
        if (customCount.HasValue && customCount.Value > 0)
        {
            return customCount.Value;
        }

        return scale switch
        {
            BenchmarkScale.Micro => 100,
            BenchmarkScale.Quick => 1000,
            BenchmarkScale.Standard => 5000,
            BenchmarkScale.Full => 10000,
            BenchmarkScale.Large => 100000,
            BenchmarkScale.Massive => 1000000,
            BenchmarkScale.Custom => customCount ?? 1000,
            _ => 1000
        };
    }
}
