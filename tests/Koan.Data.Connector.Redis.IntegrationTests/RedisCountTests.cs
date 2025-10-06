using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing;
using Koan.Testing.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Redis.IntegrationTests;

/// <summary>
/// Comprehensive count tests for Redis adapter.
/// Verifies CountRequest/CountResult contract, CountStrategy behavior,
/// IsEstimate flag (always false for Redis), and edge cases.
/// Redis has no metadata system, so all counts are exact.
/// </summary>
[Collection("Redis")]
public class RedisCountTests : KoanTestBase, IClassFixture<RedisAutoFixture>
{
    private readonly RedisAutoFixture _fixture;

    public RedisCountTests(RedisAutoFixture fixture)
    {
        _fixture = fixture;
    }

    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Status { get; set; } = "";
    }

    private IServiceProvider BuildRedisServices()
    {
        if (_fixture.ConnectionString is null)
        {
            Skip.If(true, "Redis not available for testing");
        }

        AggregateConfigs.Reset();

        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Koan:Data:Redis:ConnectionString", _fixture.ConnectionString)
                })
                .Build();

            services.AddSingleton<IConfiguration>(cfg);
            services.AddKoan();
        });
    }

    #region P0: Critical - Entity.Count Syntax Tests

    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-default-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();
        await new CountTestEntity { Name = "Test3" }.Save();

        // Act
        var count = await CountTestEntity.Count.Exact();

        // Assert
        count.Should().Be(3, "Entity.Count.Exact() should perform accurate count");
    }

    [Fact]
    public async Task EntityCount_Fast_FallbacksToExactInRedis()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        // Insert test data
        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var count = await CountTestEntity.Count.Fast();

        // Assert
        count.Should().Be(10, "Entity.Count.Fast() in Redis fallbacks to exact count");
    }

    [Fact]
    public async Task EntityCount_Optimized_ChoosesBestStrategy()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
        await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        // Act
        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        // Assert
        count.Should().Be(2, "Entity.Count.Where should filter correctly");
    }

    #endregion

    #region P0: Critical - IsEstimate Flag Tests (Redis always exact)

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
    public async Task FastCount_SetsIsEstimateFalse_RedisHasNoMetadata()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        result.IsEstimate.Should().BeFalse("Redis has no metadata, Fast fallbacks to exact with IsEstimate = false");
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task OptimizedCount_SetsIsEstimateFalse()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.IsEstimate.Should().BeFalse("Redis always returns exact counts");
        result.Value.Should().Be(1);
    }

    #endregion

    #region P0: Critical - CountStrategy Behavior Tests

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
    public async Task CountStrategy_Fast_FallbacksToExactInRedis()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        result.Value.Should().Be(10, "Fast strategy fallbacks to exact in Redis");
        result.IsEstimate.Should().BeFalse("Redis has no metadata, always exact");
    }

    [Fact]
    public async Task CountStrategy_Optimized_UsesExactInRedis()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.Value.Should().Be(1);
        result.IsEstimate.Should().BeFalse("Redis optimized uses exact counting");
    }

    #endregion

    #region P1: Predicate-Based Count Tests

    [Fact]
    public async Task Count_WithPredicate_FiltersCorrectly()
    {
        // Arrange
        using var _ = EntityContext.Partition("pred-filter-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();

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
    public async Task Count_EmptySet_ReturnsZero()
    {
        // Arrange
        using var _ = EntityContext.Partition("empty-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        exactResult.Value.Should().Be(0, "Exact count on empty set should be 0");
        fastResult.Value.Should().Be(0, "Fast count on empty set should also be 0 in Redis");
        exactResult.IsEstimate.Should().BeFalse();
        fastResult.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Count_NullPredicate_WorksWithAllStrategies()
    {
        // Arrange
        using var _ = EntityContext.Partition("null-pred-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

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
        var sp = BuildRedisServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();

        var partition1 = "partition1-" + Guid.NewGuid().ToString("N")[..8];
        var partition2 = "partition2-" + Guid.NewGuid().ToString("N")[..8];

        // Add 2 items to partition1
        using (EntityContext.Partition(partition1))
        {
            await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));
            await new CountTestEntity { Name = "P1-Item1" }.Save();
            await new CountTestEntity { Name = "P1-Item2" }.Save();
        }

        // Add 3 items to partition2
        using (EntityContext.Partition(partition2))
        {
            await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));
            await new CountTestEntity { Name = "P2-Item1" }.Save();
            await new CountTestEntity { Name = "P2-Item2" }.Save();
            await new CountTestEntity { Name = "P2-Item3" }.Save();
        }

        // Act & Assert
        using (EntityContext.Partition(partition1))
        {
            var count1 = await repo.CountAsync(new CountRequest<CountTestEntity>());
            count1.Value.Should().Be(2, "Partition 1 should have 2 items");
        }

        using (EntityContext.Partition(partition2))
        {
            var count2 = await repo.CountAsync(new CountRequest<CountTestEntity>());
            count2.Value.Should().Be(3, "Partition 2 should have 3 items");
        }
    }

    #endregion
}
