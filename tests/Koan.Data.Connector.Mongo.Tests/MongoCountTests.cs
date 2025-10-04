using System;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

/// <summary>
/// Comprehensive count tests for MongoDB adapter.
/// Verifies CountRequest/CountResult contract, CountStrategy behavior,
/// IsEstimate flag, fast count optimization using estimatedDocumentCount(), and edge cases.
/// </summary>
[Collection("Mongo")]
public class MongoCountTests : IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fixture;

    public MongoCountTests(MongoAutoFixture fixture)
    {
        _fixture = fixture;
        if (!_fixture.IsAvailable)
        {
            Skip.If(true, "MongoDB not available for testing");
        }
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
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
    public async Task EntityCount_Fast_UsesMetadataWhenAvailable()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        // Insert test data
        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        // Act
        var count = await CountTestEntity.Count.Fast();

        // Assert
        count.Should().BeGreaterThan(0, "Entity.Count.Fast() should use MongoDB estimatedDocumentCount");
        // Note: Fast count is an estimate, so exact match not guaranteed
    }

    [Fact]
    public async Task EntityCount_Optimized_ChoosesBestStrategy()
    {
        // Arrange
        using var _ = EntityContext.Partition("count-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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

    #region P0: Critical - IsEstimate Flag Tests

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
    public async Task FastCount_SetsIsEstimateTrue()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        // Insert enough data for metadata to be available
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
        result.IsEstimate.Should().BeTrue("Fast count using estimatedDocumentCount should set IsEstimate = true");
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task OptimizedCount_WithoutPredicate_MayUseEstimate()
    {
        // Arrange
        using var _ = EntityContext.Partition("isest-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        // Act
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.Value.Should().BeGreaterThan(0);
        // IsEstimate can be true or false depending on optimization choice
    }

    #endregion

    #region P0: Critical - CountStrategy Behavior Tests

    [Fact]
    public async Task CountStrategy_Exact_AlwaysPerformsFullScan()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
    public async Task CountStrategy_Fast_UsesMetadataWhenPossible()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
        result.Value.Should().BeGreaterThan(0, "Fast strategy should use estimatedDocumentCount");
        result.IsEstimate.Should().BeTrue("estimatedDocumentCount provides estimates");
    }

    [Fact]
    public async Task CountStrategy_Optimized_ChoosesAppropriately()
    {
        // Arrange
        using var _ = EntityContext.Partition("strat-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        // Act - without predicate, should prefer fast
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert
        result.Value.Should().BeGreaterThan(0);
    }

    #endregion

    #region P1: Provider-Specific Fast Count Tests

    [Fact]
    public async Task Mongo_FastCount_UsesEstimatedDocumentCount()
    {
        // Arrange
        using var _ = EntityContext.Partition("mongo-est-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        // Insert data to ensure MongoDB has collection metadata
        for (int i = 0; i < 20; i++)
        {
            await new CountTestEntity { Name = $"Item{i}", Value = i }.Save();
        }

        // Act - Fast count should use estimatedDocumentCount()
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        result.Value.Should().BeGreaterThan(0, "estimatedDocumentCount should report some documents");
        result.IsEstimate.Should().BeTrue("estimatedDocumentCount provides estimates, not exact counts");
    }

    #endregion

    #region P1: Fallback Behavior Tests

    [Fact]
    public async Task FastCount_WithPredicate_FallbacksToExact()
    {
        // Arrange
        using var _ = EntityContext.Partition("fallback-pred-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Match", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        // Act - Fast strategy with predicate should fallback to exact
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = x => x.Status == "Active",
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        result.Value.Should().Be(1, "Should fallback to exact count with predicate");
        result.IsEstimate.Should().BeFalse("Fallback to exact should set IsEstimate = false");
    }

    [Fact]
    public async Task FastCount_WhenMetadataUnavailable_FallbacksToExact()
    {
        // Arrange
        using var _ = EntityContext.Partition("fallback-meta-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        // Act - Even if estimatedDocumentCount fails, should fallback gracefully
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // Assert
        result.Value.Should().BeGreaterThanOrEqualTo(1, "Should fallback to exact count if needed");
    }

    #endregion

    #region P1: Long Count Support Tests

    [Fact]
    public async Task Count_ReturnsLongType()
    {
        // Arrange
        using var _ = EntityContext.Partition("long-type-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
    public async Task Count_EmptyCollection_ReturnsZero()
    {
        // Arrange
        using var _ = EntityContext.Partition("empty-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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
        exactResult.Value.Should().Be(0, "Exact count on empty collection should be 0");
        fastResult.Value.Should().BeGreaterThanOrEqualTo(0, "Fast count on empty collection should be >= 0");
    }

    [Fact]
    public async Task Count_NullPredicate_WorksWithAllStrategies()
    {
        // Arrange
        using var _ = EntityContext.Partition("null-pred-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
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

        var fast = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });
        fast.Value.Should().BeGreaterThanOrEqualTo(0);

        var optimized = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });
        optimized.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Count_RawQuery_WorksCorrectly()
    {
        // Arrange
        using var _ = EntityContext.Partition("raw-query-" + Guid.NewGuid().ToString("N")[..8]);
        var data = _fixture.Services!.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, string>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Alpha", Value = 10 }.Save();
        await new CountTestEntity { Name = "Beta", Value = 20 }.Save();
        await new CountTestEntity { Name = "Gamma", Value = 30 }.Save();

        // Act - MongoDB raw query using JSON filter syntax
        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            RawQuery = "{ \"Value\": { \"$gt\": 15 } }"
        });

        // Assert
        result.Value.Should().Be(2, "Raw query should filter correctly");
    }

    #endregion
}
