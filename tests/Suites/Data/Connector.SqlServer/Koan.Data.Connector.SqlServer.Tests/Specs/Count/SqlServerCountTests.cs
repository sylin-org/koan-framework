using System;
using System.Linq;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Count;

public class SqlServerCountTests : IClassFixture<Support.SqlServerAutoFixture>
{
    private readonly Support.SqlServerAutoFixture _fx;

    public SqlServerCountTests(Support.SqlServerAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        using var partition = BeginPartition("count-default");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

    await new CountTestEntity { Name = "Test1" }.Save();
    await new CountTestEntity { Name = "Test2" }.Save();

        var count = await CountTestEntity.Count;

        count.Should().Be(2);
    }

    [Fact]
    public async Task EntityCount_Exact_ForcesFullScan()
    {
        using var partition = BeginPartition("count-exact");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

    await new CountTestEntity { Name = "Test1" }.Save();
    await new CountTestEntity { Name = "Test2" }.Save();
    await new CountTestEntity { Name = "Test3" }.Save();

        var count = await CountTestEntity.Count.Exact();

        count.Should().Be(3);
    }

    [Fact]
    public async Task EntityCount_Fast_UsesPartitionStats()
    {
        using var partition = BeginPartition("count-fast");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        foreach (var i in Enumerable.Range(0, 10))
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var count = await CountTestEntity.Count.Fast();

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EntityCount_Where_WorksWithStrategies()
    {
        using var partition = BeginPartition("count-where");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

    await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
    await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
    await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

    var all = await repo.QueryAsync(null, default);
    all.Should().HaveCount(3);
    all.Count(x => x.Status == "Active").Should().Be(2);

        var repoCheck = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Predicate = x => x.Status == "Active",
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

    repoCheck.Value.Should().Be(2);
        repoCheck.IsEstimate.Should().BeFalse();

        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        count.Should().Be(2);
    }

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        using var partition = BeginPartition("count-isest-exact");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

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
        using var partition = BeginPartition("count-isest-fast");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        foreach (var i in Enumerable.Range(0, 5))
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

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        using var partition = BeginPartition("count-strategy-exact");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        var expected = 7;
        foreach (var i in Enumerable.Range(0, expected))
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
        using var partition = BeginPartition("count-strategy-fast");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        foreach (var i in Enumerable.Range(0, 10))
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

    [Fact]
    public async Task SqlServer_FastCount_UsesDmDbPartitionStats()
    {
        using var partition = BeginPartition("count-dmdb");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        foreach (var i in Enumerable.Range(0, 20))
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

    [Fact]
    public async Task FastCount_WithPredicate_FallbacksToExact()
    {
        using var partition = BeginPartition("count-fallback");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

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

    [Fact]
    public async Task Count_ReturnsLongType()
    {
        using var partition = BeginPartition("count-long");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

    await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>());

        result.Value.Should().Be(1L);
    }

    [Fact]
    public async Task Count_EmptyTable_ReturnsZero()
    {
        using var partition = BeginPartition("count-empty");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

        var exactResult = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            Options = new DataQueryOptions { CountStrategy = CountStrategy.Exact }
        });

        exactResult.Value.Should().Be(0);
    }

    [Fact]
    public async Task Count_RawQuery_WorksCorrectly()
    {
        using var partition = BeginPartition("count-raw");
        var (available, repo) = await PrepareAsync();
        if (!available) return;

    await new CountTestEntity { Name = "Alpha", Value = 10 }.Save();
    await new CountTestEntity { Name = "Beta", Value = 20 }.Save();
    await new CountTestEntity { Name = "Gamma", Value = 30 }.Save();

        var result = await repo.CountAsync(new CountRequest<CountTestEntity>
        {
            RawQuery = "[Value] > 15"
        });

        result.Value.Should().Be(2);
    }

    private async Task<(bool Available, IDataRepository<CountTestEntity, string> Repo)> PrepareAsync()
    {
        if (_fx.SkipTests)
        {
            return (false, default!);
        }

        AggregateConfigs.Reset();
        EnsureAppHost();
        await _fx.Data.Execute<CountTestEntity, int>(new Instruction("data.clear"));
        return (true, _fx.Data.GetRepository<CountTestEntity, string>());
    }

    private void EnsureAppHost()
    {
        if (!ReferenceEquals(AppHost.Current, _fx.ServiceProvider))
        {
            AppHost.Current = _fx.ServiceProvider;
        }
    }

    private static IDisposable BeginPartition(string prefix)
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        return EntityContext.Partition($"sql-{prefix}-{token}");
    }

    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
