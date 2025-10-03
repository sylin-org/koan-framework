using System.Diagnostics;
using Koan.Data.Core;
using S14.AdapterBench.Models;

namespace S14.AdapterBench.Services;

public class BenchmarkService : IBenchmarkService
{
    private static readonly string[] DefaultProviders = { "sqlite", "postgres", "mongo", "redis" };

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        BenchmarkRequest request,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            Mode = request.Mode,
            Scale = request.Scale,
            EntityCount = GetEntityCount(request.Scale)
        };

        var providers = request.Providers.Count > 0 ? request.Providers : DefaultProviders.ToList();

        try
        {
            if (request.Mode == BenchmarkMode.Sequential)
            {
                await RunSequentialBenchmarkAsync(result, providers, request.EntityTiers, progress, cancellationToken);
            }
            else
            {
                await RunParallelBenchmarkAsync(result, providers, request.EntityTiers, progress, cancellationToken);
            }

            result.Status = BenchmarkStatus.Completed;
        }
        catch (Exception ex)
        {
            result.Status = BenchmarkStatus.Failed;
            // Log error - in a real scenario we'd inject ILogger
            Console.WriteLine($"Benchmark failed: {ex.Message}");
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
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
        var totalTests = providers.Count * entityTiers.Count * 3; // 3 test types per tier
        var completedTests = 0;

        foreach (var provider in providers)
        {
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
            }

            providerStopwatch.Stop();
            providerResult.TotalDuration = providerStopwatch.Elapsed;
            result.ProviderResults.Add(providerResult);
        }
    }

    private async Task RunParallelBenchmarkAsync(
        BenchmarkResult result,
        List<string> providers,
        List<string> entityTiers,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalTests = entityTiers.Count * 3; // 3 test types per tier (same tests, all providers in parallel)
        var completedTests = 0;

        // Initialize provider results
        var providerResults = providers.Select(p => new ProviderResult
        {
            ProviderName = p,
            IsContainerized = p != "sqlite"
        }).ToList();

        var overallStopwatch = Stopwatch.StartNew();

        foreach (var tier in entityTiers)
        {
            // Run tests in parallel across all providers
            var singleWriteTasks = providers.Select((p, i) =>
                RunSingleWriteTestAsync(p, tier, result.EntityCount, progress, completedTests, totalTests, cancellationToken)
                    .ContinueWith(t => providerResults[i].Tests.Add(t.Result), cancellationToken));

            await Task.WhenAll(singleWriteTasks);
            completedTests++;

            var batchWriteTasks = providers.Select((p, i) =>
                RunBatchWriteTestAsync(p, tier, result.EntityCount, progress, completedTests, totalTests, cancellationToken)
                    .ContinueWith(t => providerResults[i].Tests.Add(t.Result), cancellationToken));

            await Task.WhenAll(batchWriteTasks);
            completedTests++;

            var readByIdTasks = providers.Select((p, i) =>
                RunReadByIdTestAsync(p, tier, result.EntityCount, progress, completedTests, totalTests, cancellationToken)
                    .ContinueWith(t => providerResults[i].Tests.Add(t.Result), cancellationToken));

            await Task.WhenAll(readByIdTasks);
            completedTests++;
        }

        overallStopwatch.Stop();

        // In parallel mode, all providers run for the same wall-clock time (limited by slowest)
        // Set each provider's TotalDuration to the overall elapsed time
        foreach (var providerResult in providerResults)
        {
            providerResult.TotalDuration = overallStopwatch.Elapsed;
        }

        result.ProviderResults.AddRange(providerResults);
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
        var testResult = new TestResult
        {
            TestName = "Single Writes",
            EntityTier = tier,
            OperationCount = count,
            UsedNativeExecution = true // Entity<T>.Save() is always native
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();

            using (EntityContext.Adapter(provider))
            {
                // Create and save entities one at a time
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entity = CreateEntity(tier, i);
                    await SaveEntityAsync(entity, tier);

                    if (i % 100 == 0)
                    {
                        progress?.Report(new BenchmarkProgress
                        {
                            CurrentProvider = provider,
                            CurrentTest = $"{tier} - Single Writes",
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
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
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

            using (EntityContext.Adapter(provider))
            {
                for (int batchStart = 0; batchStart < count; batchStart += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchCount = Math.Min(batchSize, count - batchStart);
                    var entities = Enumerable.Range(batchStart, batchCount)
                        .Select(i => CreateEntity(tier, i))
                        .ToList();

                    await SaveEntitiesBatchAsync(entities, tier);

                    progress?.Report(new BenchmarkProgress
                    {
                        CurrentProvider = provider,
                        CurrentTest = $"{tier} - Batch Writes",
                        TotalTests = totalTests,
                        CompletedTests = completedTests,
                        CurrentOperationCount = batchStart + batchCount,
                        TotalOperations = count,
                        CurrentOperationsPerSecond = (batchStart + batchCount) / (stopwatch.Elapsed.TotalSeconds + 0.001)
                    });
                }
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed;
            testResult.OperationsPerSecond = count / stopwatch.Elapsed.TotalSeconds;
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
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
        }
        catch (Exception ex)
        {
            testResult.Error = ex.Message;
        }

        return testResult;
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

    private int GetEntityCount(BenchmarkScale scale)
    {
        return scale switch
        {
            BenchmarkScale.Quick => 1000,
            BenchmarkScale.Standard => 5000,
            BenchmarkScale.Full => 10000,
            _ => 1000
        };
    }
}
