using System.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// DATA-0162: a Direct query on SqlServer reads at most one row past its MaxRows. A read-only one outside a caller's transaction cancels
/// its command at the bound, so the driver does not drain the rest: a row past the bound that cannot be produced does not
/// fail the read, and a result of 30,000,000 rows answers in milliseconds, on the entity-routed, untyped and typed paths.
/// One that writes is never cancelled, so its write completes.
/// </summary>
public sealed class SqlServerDirectEntityQuerySpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    private const string FailsPastFiveRows = "SELECT CASE WHEN n > 5 THEN 1 / (n - n) ELSE n END AS v FROM (SELECT TOP 1000000 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS t";
    private const string ThirtyMillionRows = "SELECT TOP 30000000 a.object_id FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b CROSS JOIN sys.all_columns AS c";

    [Fact(DisplayName = "SqlServer: an entity-routed Direct query stops at its bound without draining the result")]
    public async Task Entity_routed_direct_query_stops_at_its_bound()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        Koan.Data.Core.Direct.IDirectSession Reads() => data
            .Direct(source: $"entity:{typeof(DirectBoundProbe).FullName}")
            .Effect(DataOperationEffect.Read)
            .WithMaxRows(3);

        var cut = await Reads().Query(FailsPastFiveRows);
        cut.Should().HaveCount(3);
        cut.Completion.Should().Be(RecordSetCompletion.RecordLimit);

        _ = await Reads().Query("SELECT 1 AS v");
        var clock = Stopwatch.StartNew();
        var large = await Reads().Query(ThirtyMillionRows);
        clock.Stop();
        Output.WriteLine($"30,000,000-row result bounded at 3 rows answered in {clock.ElapsedMilliseconds} ms");
        large.Should().HaveCount(3);
        large.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        clock.ElapsedMilliseconds.Should().BeLessThan(1_000, "the command is cancelled at the bound, not drained");

        (await Reads().Query("SELECT 7 AS v")).Should().ContainSingle("the connection is usable after a cancelled read");
    }

    [Fact(DisplayName = "SqlServer: a bounded Direct read that writes is never cancelled: an UPDATE ... OUTPUT over 500,000 rows updates every row")]
    public async Task A_bounded_write_completes()
    {
        RequireBackingStore();
        await Execute("IF OBJECT_ID(N'dbo.k14_writes') IS NOT NULL DROP TABLE dbo.k14_writes; CREATE TABLE dbo.k14_writes (id int PRIMARY KEY, touched bit NOT NULL); INSERT INTO dbo.k14_writes (id, touched) SELECT TOP 500000 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 0 FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b;");
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var rows = await data
            .Direct(source: $"entity:{typeof(DirectBoundProbe).FullName}")
            .Effect(DataOperationEffect.Write)
            .WithMaxRows(3)
            .Query("UPDATE dbo.k14_writes SET touched = 1 OUTPUT inserted.id");

        rows.Should().HaveCount(3);
        rows.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        Convert.ToInt64(await Scalar("SELECT COUNT_BIG(*) FROM dbo.k14_writes WHERE touched = 1")).Should().Be(500_000L, "the write the bounded read carried completed");
    }

    [Fact(DisplayName = "SqlServer: an untyped and a typed Direct read stop at their bound without draining the result")]
    public async Task Untyped_and_typed_direct_reads_stop_at_their_bound()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        Koan.Data.Core.Direct.IDirectSession Reads() => data.Direct().Effect(DataOperationEffect.Read).WithMaxRows(3);
        _ = await Reads().Query("SELECT 1 AS v");

        var clock = Stopwatch.StartNew();
        var untyped = await Reads().Query(BigRead);
        var untypedMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var typed = await Reads().Query<BigRow>(BigRead);
        var typedMs = clock.ElapsedMilliseconds;
        Output.WriteLine($"30,000,000 rows bounded at 3: untyped {untypedMs} ms, typed {typedMs} ms");

        untyped.Should().HaveCount(3);
        untyped.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        typed.Should().HaveCount(3);
        typed.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        untypedMs.Should().BeLessThan(1_000, "a read-only bounded read cancels its command");
        typedMs.Should().BeLessThan(1_000, "a read-only bounded read cancels its command");
    }

    private const string BigRead = "SELECT TOP 30000000 a.object_id AS v FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b CROSS JOIN sys.all_columns AS c";

    public sealed class BigRow
    {
        public int V { get; set; }
    }

    private async Task Execute(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public sealed class DirectBoundProbe : Entity<DirectBoundProbe>
    {
        public string Name { get; set; } = "";
    }
}
