using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Analytics;
using Koan.Data.Analytics;
using Koan.Data.SoftDelete;
using Koan.Testing.Integration;

namespace Koan.Data.Analytics.Tests.Specs;

/// <summary>
/// DATA-0128 (NK-041, NK-042): an aggregate is a read, so it crosses the Data boundary with the caller's read scope.
/// Soft delete stands in for every read-filter contributor (data axes and Web row filters fold the same way): a live
/// answer aggregates only visible rows, and a materialized projection, one table for every caller, is neither served
/// to nor refreshed under a scope it was not computed for.
/// </summary>
public sealed class AnalyticsReadScopeSpec(SqliteFixture fixture) : IAsyncDisposable
{
    [SoftDelete]
    public sealed class ScopedProbe : Entity<ScopedProbe>
    {
        public string Name { get; set; } = "";
        public decimal Score { get; set; }
    }

    private IntegrationHost? _host;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    private async Task<IServiceProvider> BootAndSeedAsync(string tag)
    {
        var materialization = Path.Combine(Path.GetTempPath(), $"koan-scope-{tag}.duckdb");
        if (File.Exists(materialization)) File.Delete(materialization);
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", fixture.ConnectionString)
            .WithSetting("Koan:Data:Analytics:MaterializationConnectionString", $"Data Source={materialization}")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();
        _host = host;
        AppHost.Current = host.Services;

        await new ScopedProbe { Name = $"{tag}-a", Score = 10m }.Save();
        await new ScopedProbe { Name = $"{tag}-a", Score = 20m }.Save();
        var hidden = await new ScopedProbe { Name = $"{tag}-b", Score = 40m }.Save();
        (await ScopedProbe.Remove(hidden.Id)).Should().BeTrue();
        return host.Services;
    }

    [Fact]
    public async Task Live_answers_aggregate_only_the_rows_the_caller_can_read()
    {
        var tag = Tag();
        Analytics.Question<ScopedProbe, string>($"scope-{tag}-count", q => q.Where(p => p.Name.StartsWith(tag)).Count());
        Analytics.Question<ScopedProbe, string>($"scope-{tag}-sum-by-name",
            q => q.Where(p => p.Name.StartsWith(tag)).By(p => p.Name).Sum(p => p.Score));
        await BootAndSeedAsync(tag);

        var count = await Analytics.Of<ScopedProbe, string>().Run($"scope-{tag}-count", ct: CancellationToken.None);
        Convert.ToInt64(count.Rows.Single().Values["count"]).Should().Be(2, "a soft-deleted row is not readable, so it is not counted");
        (await ScopedProbe.Query(p => p.Name.StartsWith(tag))).Should().HaveCount(2);

        var grouped = await Analytics.Of<ScopedProbe, string>().Run($"scope-{tag}-sum-by-name", ct: CancellationToken.None);
        grouped.Rows.Select(row => (string?)row.Values["Name"]).Should().Equal($"{tag}-a");
        Convert.ToDecimal(grouped.Rows.Single().Values["sum_Score"]).Should().Be(30m);

        using (ScopedProbe.WithDeleted())
        {
            var all = await Analytics.Of<ScopedProbe, string>().Run($"scope-{tag}-count", ct: CancellationToken.None);
            Convert.ToInt64(all.Rows.Single().Values["count"]).Should().Be(3, "opening the recycle bin lifts the read scope");
        }
    }

    [Fact]
    public async Task A_materialized_projection_is_neither_refreshed_nor_served_under_a_read_scope()
    {
        var tag = Tag();
        var name = $"scope-{tag}-projection";
        var question = Analytics.Question<ScopedProbe, string>(name, q => q
            .Where(p => p.Name.StartsWith(tag))
            .By(p => p.Name)
            .Sum(p => p.Score)
            .Materialize(_ => { }));
        var services = await BootAndSeedAsync(tag);

        var scopedRefresh = () => question.RefreshAsync(services, CancellationToken.None);
        await scopedRefresh.Should().ThrowAsync<NotSupportedException>("the projection would hold only this scope's rows");

        using (ScopedProbe.WithDeleted())
            (await question.RefreshAsync(services, CancellationToken.None)).RowCount.Should().Be(2);

        // Materialized and fresh, yet the scoped caller is answered live under its own scope.
        var answer = await Analytics.Of<ScopedProbe, string>().Run(name, ct: CancellationToken.None);
        answer.ServedFrom.Should().Be("live");
        answer.Rows.Select(row => (string?)row.Values["Name"]).Should().Equal($"{tag}-a");
        (await question.ExplainAsync(services, null, CancellationToken.None)).Would.Should().Be("compute");

        var rows = () => Analytics.Rows(name);
        await rows.Should().ThrowAsync<NotSupportedException>();
        var facets = () => Analytics.Facets(name, "Name");
        await facets.Should().ThrowAsync<NotSupportedException>();
        var history = () => Analytics.History(name);
        await history.Should().ThrowAsync<NotSupportedException>();

        using (ScopedProbe.WithDeleted())
        {
            (await Analytics.Rows(name)).Rows.Should().HaveCount(2, "an unscoped caller reads the projection it computed");
            (await Analytics.Of<ScopedProbe, string>().Run(name, ct: CancellationToken.None)).ServedFrom.Should().Be("materialization");
        }
    }

    private static string Tag() => Guid.CreateVersion7().ToString("N")[..10];
}
