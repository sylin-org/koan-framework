using Koan.Data.Abstractions.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0162: a Direct query routed through an entity's adapter obeys the session's MaxRows like any other Direct query:
/// the adapter reads at most one row past it, and the answer says when it was cut.
/// </summary>
public sealed class SqliteDirectEntityQuerySpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    private const string FiveRows = "SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5";

    [Fact(DisplayName = "Sqlite: an entity-backed Direct query past its max rows says it was cut")]
    public async Task Entity_backed_direct_query_says_it_was_cut()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        Koan.Data.Core.Direct.IDirectSession Reads(int maxRows) => data
            .Direct(source: $"entity:{typeof(DirectProbe).FullName}")
            .Effect(DataOperationEffect.Read)
            .WithMaxRows(maxRows);

        var cut = await Reads(3).Query(FiveRows);
        cut.Should().HaveCount(3);
        cut.Completion.Should().Be(RecordSetCompletion.RecordLimit);

        var whole = await Reads(5).Query(FiveRows);
        whole.Should().HaveCount(5);
        whole.IsComplete.Should().BeTrue();
    }

    // Row 6 cannot be produced: abs() of the smallest integer overflows. A read bounded at MaxRows + 1 never asks for it.
    private const string FailsPastFiveRows =
        "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 1000000) " +
        "SELECT CASE WHEN x > 5 THEN abs(-9223372036854775808) ELSE x END AS v FROM n";

    [Fact(DisplayName = "Sqlite: an entity-backed Direct query reads at most one row past its max rows")]
    public async Task Entity_backed_direct_query_reads_no_row_past_the_bound()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var cut = await data
            .Direct(source: $"entity:{typeof(DirectProbe).FullName}")
            .Effect(DataOperationEffect.Read)
            .WithMaxRows(3)
            .Query(FailsPastFiveRows);

        cut.Should().HaveCount(3);
        cut.Completion.Should().Be(RecordSetCompletion.RecordLimit);
    }

    public sealed class DirectProbe : Entity<DirectProbe>
    {
        public string Name { get; set; } = "";
    }
}
