using System.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// DATA-0162: a Direct query on Postgres reads at most one row past its MaxRows. A read-only one outside a caller's transaction cancels
/// its command at the bound, so the driver does not drain the rest: a row past the bound that cannot be produced does not
/// fail the read, and a result of 30,000,000 rows answers in milliseconds, on the entity-routed, untyped and typed paths.
/// One that writes is never cancelled, so its write completes.
/// </summary>
public sealed class PostgresDirectEntityQuerySpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    private const string FailsPastFiveRows = "SELECT CASE WHEN x > 5 THEN 1 / (x - x) ELSE x END AS v FROM generate_series(1, 1000000) AS g(x)";
    private const string ThirtyMillionRows = "SELECT a.x FROM generate_series(1, 6000) AS a(x) CROSS JOIN generate_series(1, 5000) AS b(y)";

    [Fact(DisplayName = "Postgres: an entity-routed Direct query stops at its bound without draining the result")]
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

    [Fact(DisplayName = "Postgres: a bounded Direct read that writes is never cancelled: a SELECT then an INSERT under a Write effect runs the INSERT")]
    public async Task A_bounded_write_completes()
    {
        RequireBackingStore();
        await Execute("DROP TABLE IF EXISTS k14_marks; CREATE TABLE k14_marks (id int);");
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var rows = await data
            .Direct(source: $"entity:{typeof(DirectBoundProbe).FullName}")
            .Effect(DataOperationEffect.Write)
            .WithMaxRows(3)
            .Query("SELECT g FROM generate_series(1, 2000000) AS g; INSERT INTO k14_marks VALUES (1)");

        rows.Should().HaveCount(3);
        rows.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM k14_marks")).Should().Be(1L, "the write the bounded read carried completed");
    }

    [Fact(DisplayName = "Postgres: an untyped and a typed Direct read stop at their bound without draining the result")]
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

    private const string BigRead = "SELECT a.x AS v FROM generate_series(1, 6000) AS a(x) CROSS JOIN generate_series(1, 5000) AS b(y)";

    public sealed class BigRow
    {
        public int V { get; set; }
    }

    private async Task Execute(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Fixture.ConnectionString);
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
