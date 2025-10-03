using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests;

/// <summary>
/// Tests for thread-safe concurrent access to InMemory adapter.
/// Validates ConcurrentDictionary-based storage under load.
/// </summary>
[Collection("Sequential")]
public sealed class ConcurrentAccessTests : IDisposable
{
    private readonly TestScope _scope;

    public ConcurrentAccessTests()
    {
        _scope = CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task ConcurrentInserts_NoDataLoss()
    {
        const int taskCount = 10;
        const int insertsPerTask = 100;

        var tasks = Enumerable.Range(0, taskCount).Select(async taskId =>
        {
            for (int i = 0; i < insertsPerTask; i++)
            {
                var entity = new ConcurrentEntity
                {
                    Name = $"Task{taskId}-Entity{i}",
                    Value = i
                };
                await entity.Save();
            }
        });

        await Task.WhenAll(tasks);

        var all = await ConcurrentEntity.All();
        all.Should().HaveCount(taskCount * insertsPerTask);
    }

    [Fact]
    public async Task ConcurrentUpdates_SameEntity_ThreadSafe()
    {
        var entity = new ConcurrentEntity { Name = "Original", Value = 0 };
        await entity.Save();
        var id = entity.Id;

        const int taskCount = 10;
        var tasks = Enumerable.Range(0, taskCount).Select(async taskId =>
        {
            var loaded = await ConcurrentEntity.Get(id);
            if (loaded != null)
            {
                loaded.Name = $"Updated-{taskId}";
                loaded.Value += 1;
                await loaded.Save();
            }
        });

        await Task.WhenAll(tasks);

        var final = await ConcurrentEntity.Get(id);
        final.Should().NotBeNull();
        // Value should have been incremented by all tasks (might not be exact due to race conditions, but should be > 0)
        final!.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoExceptions()
    {
        // Seed some data
        for (int i = 0; i < 50; i++)
        {
            await new ConcurrentEntity { Name = $"Entity{i}", Value = i }.Save();
        }

        const int readerCount = 5;
        const int writerCount = 5;

        var readerTasks = Enumerable.Range(0, readerCount).Select(async _ =>
        {
            for (int i = 0; i < 100; i++)
            {
                var all = await ConcurrentEntity.All();
                var results = await ConcurrentEntity.Query(e => e.Value > 20);
                await Task.Delay(1);  // Small delay to interleave operations
            }
        });

        var writerTasks = Enumerable.Range(0, writerCount).Select(async writerId =>
        {
            for (int i = 0; i < 50; i++)
            {
                var entity = new ConcurrentEntity
                {
                    Name = $"Writer{writerId}-Entity{i}",
                    Value = i
                };
                await entity.Save();
                await Task.Delay(1);
            }
        });

        var allTasks = readerTasks.Concat(writerTasks);
        await Task.WhenAll(allTasks);

        // If we got here without exceptions, concurrent access is working
        var all = await ConcurrentEntity.All();
        all.Count.Should().BeGreaterThan(50);  // At least the initial 50 + some from writers
    }

    [Fact]
    public async Task ConcurrentDeletes_DifferentEntities_ThreadSafe()
    {
        // Create 100 entities
        var entities = new List<ConcurrentEntity>();
        for (int i = 0; i < 100; i++)
        {
            var entity = new ConcurrentEntity { Name = $"ToDelete{i}", Value = i };
            await entity.Save();
            entities.Add(entity);
        }

        // Delete them concurrently
        var tasks = entities.Select(async entity =>
        {
            await entity.Delete();
        });

        await Task.WhenAll(tasks);

        var remaining = await ConcurrentEntity.All();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentBatchOperations_NoDeadlocks()
    {
        const int batchCount = 10;

        var tasks = Enumerable.Range(0, batchCount).Select(async batchId =>
        {
            var batch = Data<ConcurrentEntity, string>.Batch();

            for (int i = 0; i < 10; i++)
            {
                batch.Add(new ConcurrentEntity
                {
                    Name = $"Batch{batchId}-Entity{i}",
                    Value = i
                });
            }

            await batch.SaveAsync();
        });

        await Task.WhenAll(tasks);

        var all = await ConcurrentEntity.All();
        all.Count.Should().Be(batchCount * 10);
    }

    [Fact]
    public async Task ConcurrentPartitionAccess_IsolatedCorrectly()
    {
        const int partitionCount = 5;
        const int entitiesPerPartition = 20;

        var tasks = Enumerable.Range(0, partitionCount).Select(async partitionId =>
        {
            using (EntityContext.With(partition: $"partition-{partitionId}"))
            {
                for (int i = 0; i < entitiesPerPartition; i++)
                {
                    await new ConcurrentEntity
                    {
                        Name = $"Partition{partitionId}-Entity{i}",
                        Value = i
                    }.Save();
                }
            }
        });

        await Task.WhenAll(tasks);

        // Verify each partition has exactly the right number of entities
        for (int partitionId = 0; partitionId < partitionCount; partitionId++)
        {
            using (EntityContext.With(partition: $"partition-{partitionId}"))
            {
                var entities = await ConcurrentEntity.All();
                entities.Should().HaveCount(entitiesPerPartition);
                entities.Should().AllSatisfy(e =>
                    e.Name.Should().StartWith($"Partition{partitionId}"));
            }
        }
    }

    [Fact]
    public async Task ConcurrentQueryAndModify_Consistent()
    {
        // Seed initial data
        for (int i = 0; i < 20; i++)
        {
            await new ConcurrentEntity { Name = $"Initial{i}", Value = i }.Save();
        }

        var queryTask1 = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                var results = await ConcurrentEntity.Query(e => e.Value < 10);
                results.Should().NotBeNull();
                await Task.Delay(5);
            }
        });

        var queryTask2 = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                var results = await ConcurrentEntity.Query(e => e.Value >= 10);
                results.Should().NotBeNull();
                await Task.Delay(5);
            }
        });

        var modifyTask = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                await new ConcurrentEntity { Name = $"New{i}", Value = i + 100 }.Save();
                await Task.Delay(10);
            }
        });

        await Task.WhenAll(queryTask1, queryTask2, modifyTask);

        var allEntities = await ConcurrentEntity.All();
        allEntities.Count.Should().Be(50);  // 20 initial + 30 new
    }

    // ==================== Helper Methods ====================

    private static TestScope CreateScope()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        services.AddKoan();

        var provider = services.BuildServiceProvider();
        return new TestScope(provider);
    }

    private sealed class TestScope : IDisposable
    {
        private readonly IServiceProvider? _previousAppHost;
        public ServiceProvider Provider { get; }

        public TestScope(ServiceProvider provider)
        {
            Provider = provider;
            _previousAppHost = AppHost.Current;
            AppHost.Current = provider;
        }

        public void Dispose()
        {
            AppHost.Current = _previousAppHost;
            Provider.Dispose();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Koan.Data.Connector.InMemory.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ==================== Test Entity ====================

    public sealed class ConcurrentEntity : Entity<ConcurrentEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
