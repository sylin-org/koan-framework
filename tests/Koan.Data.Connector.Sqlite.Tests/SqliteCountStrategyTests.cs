using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests;

/// <summary>
/// Comprehensive count tests for SQLite adapter.
/// SQLite doesn't support metadata-based fast counts, so this verifies
/// proper fallback to exact counts for all strategies.
/// </summary>
public class SqliteCountStrategyTests : KoanTestBase
{
    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Status { get; set; } = "";
    }

    private static void ResetCoreCaches()
    {
        AggregateConfigs.Reset();
    }

    private IServiceProvider BuildSqliteServices(string file)
    {
        ResetCoreCaches();
        var cs = $"Data Source={file}";
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("SqliteOptions:ConnectionString", cs),
                    new KeyValuePair<string, string?>("Koan_DATA_PROVIDER", "sqlite")
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddSqliteAdapter(o => o.ConnectionString = cs);
            services.AddKoanDataCore();
            services.AddSingleton<IDataService, DataService>();
        });
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-count-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    #region P0: Entity.Count Syntax

    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        using var _ = EntityContext.Partition("sqlite-default-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();

        var count = await CountTestEntity.Count;

        count.Should().Be(2);
    }

    [Fact]
    public async Task EntityCount_Exact_PerformsAccurateCount()
    {
        using var _ = EntityContext.Partition("sqlite-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();
        await new CountTestEntity { Name = "Test3" }.Save();

        var count = await CountTestEntity.Count.Exact();

        count.Should().Be(3);
    }

    [Fact]
    public async Task EntityCount_Fast_FallsBackToExact()
    {
        using var _ = EntityContext.Partition("sqlite-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var count = await CountTestEntity.Count.Fast();

        count.Should().Be(10, "SQLite Fast should fallback to exact count");
    }

    [Fact]
    public async Task EntityCount_Where_FiltersCorrectly()
    {
        using var _ = EntityContext.Partition("sqlite-where-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
        await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        count.Should().Be(2);
    }

    #endregion

    #region P0: IsEstimate Flag (SQLite-Specific)

    [Fact]
    public async Task SQLite_ExactCount_SetsIsEstimateFalse()
    {
        using var _ = EntityContext.Partition("sqlite-isest-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        result.IsEstimate.Should().BeFalse();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task SQLite_FastCount_FallsBackToExact_SetsIsEstimateFalse()
    {
        using var _ = EntityContext.Partition("sqlite-isest-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 5; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        // SQLite doesn't have metadata-based fast count, so fallback to exact
        result.IsEstimate.Should().BeFalse("SQLite fallback to exact should set IsEstimate = false");
        result.Value.Should().Be(5);
    }

    #endregion

    #region P0: CountStrategy Behavior

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        using var _ = EntityContext.Partition("sqlite-strat-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        var expected = 7;
        for (int i = 0; i < expected; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        result.Value.Should().Be(expected);
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task CountStrategy_Fast_FallsBackToExactInSQLite()
    {
        using var _ = EntityContext.Partition("sqlite-strat-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        result.Value.Should().Be(10, "Fast strategy should fallback to exact in SQLite");
        result.IsEstimate.Should().BeFalse("Fallback to exact means no estimate");
    }

    [Fact]
    public async Task CountStrategy_Optimized_UsesExactInSQLite()
    {
        using var _ = EntityContext.Partition("sqlite-strat-opt-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        result.Value.Should().Be(1);
    }

    #endregion

    #region P1: SQLite-Specific Fallback Behavior

    [Fact]
    public async Task SQLite_NoMetadata_AllStrategiesUseExact()
    {
        using var _ = EntityContext.Partition("sqlite-nometa-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        var expected = 12;
        for (int i = 0; i < expected; i++)
        {
            await new CountTestEntity { Name = $"Item{i}", Value = i }.Save();
        }

        // Act - All strategies should produce exact count
        var exactResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        var fastResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        var optimizedResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });

        // Assert - All should be exact with same value
        exactResult.Value.Should().Be(expected);
        exactResult.IsEstimate.Should().BeFalse();

        fastResult.Value.Should().Be(expected, "Fast fallback should match exact");
        fastResult.IsEstimate.Should().BeFalse("Fallback means no estimate");

        optimizedResult.Value.Should().Be(expected);
    }

    [Fact]
    public async Task SQLite_WithPredicate_AlwaysExact()
    {
        using var _ = EntityContext.Partition("sqlite-pred-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Match", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = x => x.Status == "Active",
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        result.Value.Should().Be(1);
        result.IsEstimate.Should().BeFalse();
    }

    #endregion

    #region P1: Long Count Support

    [Fact]
    public async Task Count_ReturnsLongType()
    {
        using var _ = EntityContext.Partition("sqlite-long-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>());

        result.Value.Should().Be(1L);
    }

    #endregion

    #region P2: Edge Cases

    [Fact]
    public async Task Count_EmptyTable_ReturnsZero()
    {
        using var _ = EntityContext.Partition("sqlite-empty-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>());

        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task Count_RawQuery_WorksCorrectly()
    {
        using var _ = EntityContext.Partition("sqlite-raw-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Alpha", Value = 10 }.Save();
        await new CountTestEntity { Name = "Beta", Value = 20 }.Save();
        await new CountTestEntity { Name = "Gamma", Value = 30 }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            RawQuery = "Value > 15"
        });

        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task Count_NullPredicate_WorksWithAllStrategies()
    {
        using var _ = EntityContext.Partition("sqlite-null-" + Guid.NewGuid().ToString("N")[..8]);
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test" }.Save();

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
        fast.Value.Should().Be(1);

        var optimized = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = null,
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Optimized }
        });
        optimized.Value.Should().Be(1);
    }

    #endregion
}
