using System;
using System.Linq;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Count;

public sealed class SqlServerCountTests(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact]
    public async Task EntityCount_DefaultsToOptimized()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-default");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();

        var count = await CountTestEntity.Count;

        count.Should().Be(2);
    }

    [Fact]
    public async Task EntityCount_Exact_ForcesFullScan()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-exact");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Test1" }.Save();
        await new CountTestEntity { Name = "Test2" }.Save();
        await new CountTestEntity { Name = "Test3" }.Save();

        var count = await CountTestEntity.Count.Exact();

        count.Should().Be(3);
    }

    [Fact]
    public async Task EntityCount_Fast_UsesPartitionStats()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-fast");
        using var lease = Lease(partition);
        var repo = Repo(host);

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
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-where");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Match1", Status = "Active" }.Save();
        await new CountTestEntity { Name = "Match2", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        var all = (await repo.Query(QueryDefinition.All, default)).Items;
        all.Should().HaveCount(3);
        all.Count(x => x.Status == "Active").Should().Be(2);

        var repoCheck = await repo.Count(new QueryDefinition { Filter = LinqFilterCompiler.Compile<CountTestEntity>(x => x.Status == "Active"), CountStrategy = CountStrategy.Exact });

        repoCheck.Value.Should().Be(2);
        repoCheck.IsEstimate.Should().BeFalse();

        var count = await CountTestEntity.Count.Where(x => x.Status == "Active", CountStrategy.Exact);

        count.Should().Be(2);
    }

    [Fact]
    public async Task ExactCount_SetsIsEstimateFalse()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-isest-exact");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Exact });

        result.IsEstimate.Should().BeFalse();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task FastCount_SetsIsEstimateTrue()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-isest-fast");
        using var lease = Lease(partition);
        var repo = Repo(host);

        foreach (var i in Enumerable.Range(0, 5))
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Fast });

        result.IsEstimate.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CountStrategy_Exact_PerformsFullScan()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-strategy-exact");
        using var lease = Lease(partition);
        var repo = Repo(host);

        var expected = 7;
        foreach (var i in Enumerable.Range(0, expected))
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Exact });

        result.Value.Should().Be(expected);
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task CountStrategy_Fast_UsesPartitionStats()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-strategy-fast");
        using var lease = Lease(partition);
        var repo = Repo(host);

        foreach (var i in Enumerable.Range(0, 10))
        {
            await new CountTestEntity { Name = $"Item{i}" }.Save();
        }

        var result = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Fast });

        result.Value.Should().BeGreaterThan(0);
        result.IsEstimate.Should().BeTrue();
    }

    [Fact]
    public async Task SqlServer_FastCount_UsesDmDbPartitionStats()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-dmdb");
        using var lease = Lease(partition);
        var repo = Repo(host);

        foreach (var i in Enumerable.Range(0, 20))
        {
            await new CountTestEntity { Name = $"Item{i}", Value = i }.Save();
        }

        var result = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Fast });

        result.Value.Should().BeGreaterThan(0, "sys.dm_db_partition_stats should report rows");
        result.IsEstimate.Should().BeTrue("Partition stats provide estimates");
    }

    [Fact]
    public async Task FastCount_WithPredicate_FallbacksToExact()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-fallback");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Match", Status = "Active" }.Save();
        await new CountTestEntity { Name = "NoMatch", Status = "Inactive" }.Save();

        var result = await repo.Count(new QueryDefinition { Filter = LinqFilterCompiler.Compile<CountTestEntity>(x => x.Status == "Active"), CountStrategy = CountStrategy.Fast });

        result.Value.Should().Be(1);
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Count_ReturnsLongType()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-long");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Test" }.Save();

        var result = await repo.Count(QueryDefinition.All);

        result.Value.Should().Be(1L);
    }

    [Fact]
    public async Task Count_EmptyTable_ReturnsZero()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-empty");
        using var lease = Lease(partition);
        var repo = Repo(host);

        var exactResult = await repo.Count(new QueryDefinition { CountStrategy = CountStrategy.Exact });

        exactResult.Value.Should().Be(0);
    }

    [Fact]
    public async Task Count_RawQuery_WorksCorrectly()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("count-raw");
        using var lease = Lease(partition);
        var repo = Repo(host);

        await new CountTestEntity { Name = "Alpha", Value = 10 }.Save();
        await new CountTestEntity { Name = "Beta", Value = 20 }.Save();
        await new CountTestEntity { Name = "Gamma", Value = 30 }.Save();

        var result = await ((IRawQueryRepository<CountTestEntity, string>)repo).CountRaw("[Value] > 15", null, default);

        result.Value.Should().Be(2);
    }

    private static IQueryRepository<CountTestEntity, string> Repo(BoundHost host) =>
        (IQueryRepository<CountTestEntity, string>)host.Services
            .GetRequiredService<IDataService>()
            .GetRepository<CountTestEntity, string>();

    public class CountTestEntity : Entity<CountTestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Status { get; set; } = "";
    }
}
