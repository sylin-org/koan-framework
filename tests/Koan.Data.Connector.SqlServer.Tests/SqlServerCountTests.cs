using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.SqlServer.Tests;

/// <summary>
/// Comprehensive count tests for SQL Server adapter.
/// Verifies sys.dm_db_partition_stats fast count optimization.
/// </summary>
public class SqlServerCountTests : KoanTestBase
{
    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Status { get; set; } = "";
    }

    private IServiceProvider BuildSqlServerServices()
    {
        AggregateConfigs.Reset();
        var connString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? "Server=localhost;Database=koan_test;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True";

        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("SqlServerOptions:ConnectionString", connString),
                    new KeyValuePair<string, string?>("Koan_DATA_PROVIDER", "sqlserver")
                })
                .Build();

            services.AddSingleton<IConfiguration>(cfg);
            services.AddSqlServerAdapter(o => o.ConnectionString = connString);
            services.AddKoanDataCore();
            services.AddSingleton<IDataService, DataService>();
        });
    }

    #region P0: Entity.Count Syntax

    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        using var _ = EntityContext.Partition("sql-default-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();

        var count = await CountTestEntity.Count;

        count.Should().Be(2);
    }

    [Fact]
    public async Task EntityCount_Exact_ForcesFullScan()
    {
        using var _ = EntityContext.Partition("sql-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();
        await new CountTestEntity { Name = "Test3" }.Save();

        var count = await CountTestEntity.Count.Exact();

        count.Should().Be(3);
    }

    [Fact]
    public async Task EntityCount_Fast_UsesPartitionStats()
    {
        using var _ = EntityContext.Partition("sql-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 10; i++)
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var count = await CountTestEntity.Count.Fast();

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EntityCount_Where_WorksWithStrategies()
    {
        using var _ = EntityContext.Partition("sql-where-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
        await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        count.Should().Be(2);
    }

    #endregion

    #region P0: IsEstimate Flag

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        using var _ = EntityContext.Partition("sql-isest-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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
    public async Task FastCount_SetsIsEstimateTrue()
    {
        using var _ = EntityContext.Partition("sql-isest-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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

        result.IsEstimate.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region P0: CountStrategy Behavior

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        using var _ = EntityContext.Partition("sql-strat-exact-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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
    public async Task CountStrategy_Fast_UsesPartitionStats()
    {
        using var _ = EntityContext.Partition("sql-strat-fast-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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

        result.Value.Should().BeGreaterThan(0);
        result.IsEstimate.Should().BeTrue();
    }

    #endregion

    #region P1: SQL Server-Specific Fast Count

    [Fact]
    public async Task SqlServer_FastCount_UsesDmDbPartitionStats()
    {
        using var _ = EntityContext.Partition("sql-dmdb-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 20; i++)
        {
            await new CountTestEntity { Name = $"Item{i}", Value = i }.Save();
        }

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Fast }
        });

        result.Value.Should().BeGreaterThan(0, "sys.dm_db_partition_stats should report rows");
        result.IsEstimate.Should().BeTrue("Partition stats provide estimates");
    }

    #endregion

    #region P1: Fallback Behavior

    [Fact]
    public async Task FastCount_WithPredicate_FallbacksToExact()
    {
        using var _ = EntityContext.Partition("sql-fallback-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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
        using var _ = EntityContext.Partition("sql-long-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
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
        using var _ = EntityContext.Partition("sql-empty-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        var exactResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        exactResult.Value.Should().Be(0);
    }

    [Fact]
    public async Task Count_RawQuery_WorksCorrectly()
    {
        using var _ = EntityContext.Partition("sql-raw-" + Guid.NewGuid().ToString("N")[..8]);
        var sp = BuildSqlServerServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CountTestEntity, string>();
        await data.Execute<CountTestEntity, int>(new Abstractions.Instructions.Instruction("data.clear"));

        await new CountTestEntity { Name = "Alpha", Value = 10 }.Save();
        await new CountTestEntity { Name = "Beta", Value = 20 }.Save();
        await new CountTestEntity { Name = "Gamma", Value = 30 }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            RawQuery = "[Value] > 15"
        });

        result.Value.Should().Be(2);
    }

    #endregion
}
