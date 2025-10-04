using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests;

/// <summary>
/// Comprehensive count tests for InMemory adapter.
/// Verifies CountRequest/CountResult contract, CountStrategy behavior,
/// IsEstimate flag (always false for InMemory), and edge cases.
/// InMemory adapter always performs exact counts (no metadata/estimation).
/// </summary>
[Collection("Sequential")]
public sealed class InMemoryCountTests : IDisposable
{
    private readonly TestScope _scope;

    public InMemoryCountTests()
    {
        _scope = CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Status { get; set; } = "";
    }

    #region P0: Critical - Entity.Count Syntax Tests

    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-default-" + Guid.NewGuid().ToString("N")[..8]);
        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();

        // Act
        var count = await CountTestEntity.Count;

        // Assert
        count.Should().Be(2, "Entity.Count should default to optimized strategy");
    }

    [Fact]
    public async Task EntityCount_Exact_ForcesFullScan()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-exact-" + Guid.NewGuid().ToString("N")[..8]);
        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();
        await new CountTestEntity { Name = "Test3" }.Save();

        // Act
        var count = await CountTestEntity.Count.Exact();

        // Assert
        count.Should().Be(3, "Entity.Count.Exact() should perform accurate count");
    }

    [Fact]
    public async Task EntityCount_Fast_UsesExactInMemory()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-fast-" + Guid.NewGuid().ToString("N")[..8]);

        // Insert test data
        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var count = await CountTestEntity.Count.Fast();

        // Assert
        count.Should().Be(10, "Entity.Count.Fast() in InMemory uses exact count");
    }

    [Fact]
    public async Task EntityCount_Optimized_ChoosesBestStrategy()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-opt-" + Guid.NewGuid().ToString("N")[..8]);
        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var count = await CountTestEntity.Count.Optimized();

        // Assert
        count.Should().Be(1, "Entity.Count.Optimized() should choose appropriate strategy");
    }

    [Fact]
    public async Task EntityCount_Where_WorksWithExactStrategy()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-where-" + Guid.NewGuid().ToString("N")[..8]);
        await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
        await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        // Act
        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        // Assert
        count.Should().Be(2, "Entity.Count.Where should filter correctly");
    }

    #endregion

    #region P0: Critical - IsEstimate Flag Tests (InMemory always exact)

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        // Assert
        result.IsEstimate.Should().BeFalse("Exact count should set IsEstimate = false");
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task FastCount_SetsIsEstimateFalse_InMemoryHasNoMetadata()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        // Insert data
        for (int i = 0; i < 5; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        result.IsEstimate.Should().BeFalse("InMemory has no metadata, Fast fallbacks to exact with IsEstimate = false");
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task OptimizedCount_SetsIsEstimateFalse()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.IsEstimate.Should().BeFalse("InMemory always returns exact counts");
        result.Value.Should().Be(1);
    }

    #endregion

    #region P0: Critical - CountStrategy Behavior Tests

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        var expected = 7;
        for (int i = 0; i < expected; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        // Assert
        result.Value.Should().Be(expected, "Exact strategy should return precise count");
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task CountStrategy_Fast_UsesExactInMemory()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        // Insert data
        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        result.Value.Should().Be(10, "Fast strategy uses exact in InMemory");
        result.IsEstimate.Should().BeFalse("InMemory has no metadata, always exact");
    }

    [Fact]
    public async Task CountStrategy_Optimized_UsesExactInMemory()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.Value.Should().Be(1);
        result.IsEstimate.Should().BeFalse("InMemory optimized uses exact counting");
    }

    #endregion

    #region P1: Predicate-Based Count Tests

    [Fact]
    public async Task Count_WithPredicate_FiltersCorrectly()
    {
        // Arrange
        using var _ = EntityContext.Partition("pred-filter-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Match", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = x => x.Status == "Active"
        });

        // Assert
        result.Value.Should().Be(1, "Predicate should filter correctly");
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Count_ComplexPredicate_WorksCorrectly()
    {
        // Arrange
        using var _ = EntityContext.Partition("pred-complex-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test1", Value = 10, Status = "Active" }.Save();
        await new CountTestEntity { Name = "Test2", Value = 20, Status = "Active" }.Save();
        await new CountTestEntity { Name = "Test3", Value = 30, Status = "Inactive" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = x => x.Value > 15 && x.Status == "Active"
        });

        // Assert
        result.Value.Should().Be(1, "Complex predicate should filter correctly");
    }

    #endregion

    #region P1: Long Count Support Tests

    [Fact]
    public async Task Count_ReturnsLongType()
    {
        // Arrange
        using var _ = EntityContext.Partition("long-type-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>());

        // Assert
        result.Value.Should().Be(1L);
    }

    [Fact]
    public async Task Count_LargeValues_NoOverflow()
    {
        // Arrange
        using var _ = EntityContext.Partition("large-val-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        // Act - Simulate large count scenario
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>());

        // Assert
        // Verify the type can handle large values (structural test)
        long largeValue = 3_000_000_000L; // > int.MaxValue
        Action act = () => { var testResult = CountResult.Exact(largeValue); };
        act.Should().NotThrow("CountResult should handle values > int.MaxValue");
    }

    #endregion

    #region P2: Edge Cases

    [Fact]
    public async Task Count_EmptyStore_ReturnsZero()
    {
        // Arrange
        using var _ = EntityContext.Partition("empty-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        // Act
        var exactResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        var fastResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        exactResult.Value.Should().Be(0, "Exact count on empty store should be 0");
        fastResult.Value.Should().Be(0, "Fast count on empty store should also be 0 in InMemory");
        exactResult.IsEstimate.Should().BeFalse();
        fastResult.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Count_NullPredicate_WorksWithAllStrategies()
    {
        // Arrange
        using var _ = EntityContext.Partition("null-pred-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
        await new CountTestEntity { Name = "Test" }.Save();

        // Act & Assert
        var exact = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });
        exact.Value.Should().Be(1);
        exact.IsEstimate.Should().BeFalse();

        var fast = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });
        fast.Value.Should().Be(1);
        fast.IsEstimate.Should().BeFalse();

        var optimized = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });
        optimized.Value.Should().Be(1);
        optimized.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Count_MultiplePartitions_IsolatesCorrectly()
    {
        // Arrange
        var partition1 = "partition1-" + Guid.NewGuid().ToString("N")[..8];
        var partition2 = "partition2-" + Guid.NewGuid().ToString("N")[..8];

        // Add 2 items to partition1
        using (EntityContext.Partition(partition1))
        {
            await new CountTestEntity { Name = "P1-Item1" }.Save();
            await new CountTestEntity { Name = "P1-Item2" }.Save();
        }

        // Add 3 items to partition2
        using (EntityContext.Partition(partition2))
        {
            await new CountTestEntity { Name = "P2-Item1" }.Save();
            await new CountTestEntity { Name = "P2-Item2" }.Save();
            await new CountTestEntity { Name = "P2-Item3" }.Save();
        }

        // Act & Assert
        using (EntityContext.Partition(partition1))
        {
            var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
            var count1 = await repo.CountAsync(new CountRequest<CountTestEntity>());
            count1.Value.Should().Be(2, "Partition 1 should have 2 items");
        }

        using (EntityContext.Partition(partition2))
        {
            var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();
            var count2 = await repo.CountAsync(new CountRequest<CountTestEntity>());
            count2.Value.Should().Be(3, "Partition 2 should have 3 items");
        }
    }

    [Fact]
    public async Task Count_AfterDelete_ReflectsChanges()
    {
        // Arrange
        using var _ = EntityContext.Partition("after-delete-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        var entity1 = new CountTestEntity { Name = "ToKeep" };
        var entity2 = new CountTestEntity { Name = "ToDelete" };
        await entity1.Save();
        await entity2.Save();

        // Verify initial count
        var initialCount = await repo.CountAsync(new CountRequest<CountTestEntity>());
        initialCount.Value.Should().Be(2);

        // Act - Delete one entity
        await entity2.Delete();

        // Assert - Count should reflect deletion
        var afterDeleteCount = await repo.CountAsync(new CountRequest<CountTestEntity>());
        afterDeleteCount.Value.Should().Be(1, "Count should reflect entity deletion");
    }

    [Fact]
    public async Task Count_ConcurrentAccess_RemainsAccurate()
    {
        // Arrange
        using var _ = EntityContext.Partition("concurrent-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = _scope.Provider.GetRequiredService<IDataService>().GetRepository<CountTestEntity, string>();

        // Act - Add entities concurrently
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await new CountTestEntity { Name = $"Concurrent{i}" }.Save();
        });
        await Task.WhenAll(tasks);

        // Assert
        var count = await repo.CountAsync(new CountRequest<CountTestEntity>());
        count.Value.Should().Be(10, "Count should be accurate even with concurrent writes");
    }

    #endregion

    #region Helper Methods

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

    #endregion
}
