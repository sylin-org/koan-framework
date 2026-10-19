using Koan.Data.Abstractions.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// WEB-0074 defense in depth. <c>QueryRaw</c> and <c>CountRaw</c> take SQL the CALLER owns, and a caller can still be
/// wrong: the HTTP and MCP edges once handed them request text, and Npgsql runs stacked statements. These pin the floor
/// under the escape hatch - one statement, inside a transaction the server will not let write - so a mistake costs a
/// read, not the database. (A predicate can still read: this does not make raw text safe for an untrusted caller.)
/// </summary>
public sealed class PostgresRawQueryBoundarySpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: a raw query is one statement - a second statement is refused and nothing runs")]
    public async Task A_second_statement_is_refused_before_the_server_sees_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-one-statement"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }, new RawBoundaryProbe { Id = "b" }]);
        await using var side = await OpenSide();
        await Execute(side, "CREATE TABLE IF NOT EXISTS koan_raw_sentinel (id int PRIMARY KEY); " +
                            "INSERT INTO koan_raw_sentinel VALUES (1) ON CONFLICT DO NOTHING");
        var repository = Repository(host);

        // Each payload ran before the fix: Npgsql splits the text at ';' and the server executes every part. The
        // "select " form is the verbatim full-statement mode of QueryRaw.
        foreach (var payload in new[]
                 {
                     "1 = 1; DROP TABLE koan_raw_sentinel; --",
                     "1 = 0; DELETE FROM koan_raw_sentinel; --",
                     "select 1; drop table koan_raw_sentinel; --",
                 })
        {
            var query = async () => await Data<RawBoundaryProbe, string>.QueryRaw(payload);
            await query.Should().ThrowAsync<NotSupportedException>($"QueryRaw({payload}) carries a second statement")
                .WithMessage("*exactly one statement*");

            var count = async () => await repository.CountRaw(payload, null);
            await count.Should().ThrowAsync<NotSupportedException>($"CountRaw({payload}) carries a second statement")
                .WithMessage("*exactly one statement*");
        }

        (await Scalar(side, "SELECT COUNT(*) FROM koan_raw_sentinel")).Should().Be(1, "no second statement ran");
        await Execute(side, "DROP TABLE koan_raw_sentinel");
        (await Data<RawBoundaryProbe, string>.QueryRaw("1 = 1")).Should().HaveCount(2, "an ordinary predicate still runs");
    }

    [Fact(DisplayName = "Postgres: raw queries and counts run in a read-only transaction that the predicate cannot undo")]
    public async Task Raw_reads_run_in_a_read_only_transaction()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-read-only"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }, new RawBoundaryProbe { Id = "b" }]);
        var repository = Repository(host);
        await using var side = await OpenSide();

        // The server reports the mode the predicate itself executes in; an ordinary connection is read-write.
        const string observed = "current_setting('transaction_read_only') = 'on'";
        (await Scalar(side, "SELECT CASE WHEN current_setting('transaction_read_only') = 'off' THEN 1 ELSE 0 END"))
            .Should().Be(1, "an ordinary connection is read-write");
        (await Data<RawBoundaryProbe, string>.QueryRaw(observed)).Should().HaveCount(2, "QueryRaw runs read-only");
        (await repository.CountRaw(observed, null)).Value.Should().Be(2, "CountRaw runs read-only");

        // A predicate cannot lift the fence from inside: the mode is fixed once the statement has its snapshot.
        var flip = async () => await Data<RawBoundaryProbe, string>.QueryRaw(
            "(SELECT set_config('transaction_read_only', 'off', true)) = 'off'");
        (await flip.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25001");

        // Parameters still bind inside the read-only transaction.
        (await Data<RawBoundaryProbe, string>.QueryRaw("\"Id\" = @id", new { id = "a" })).Should().ContainSingle();
    }

    [Fact(DisplayName = "Postgres: a raw predicate with a side effect is refused by the server")]
    public async Task A_predicate_with_a_write_side_effect_is_refused()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-side-effect"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }, new RawBoundaryProbe { Id = "b" }]);
        var repository = Repository(host);
        await using var side = await OpenSide();
        await Execute(side, "DROP SEQUENCE IF EXISTS koan_raw_probe_seq; CREATE SEQUENCE koan_raw_probe_seq");

        try
        {
            var query = async () => await Data<RawBoundaryProbe, string>.QueryRaw("nextval('koan_raw_probe_seq') > 0");
            (await query.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25006");

            var count = async () => await repository.CountRaw("nextval('koan_raw_probe_seq') > 0", null);
            (await count.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25006");

            (await Scalar(side, "SELECT CASE WHEN is_called THEN 1 ELSE 0 END FROM koan_raw_probe_seq"))
                .Should().Be(0, "the sequence was never advanced");
        }
        finally
        {
            await Execute(side, "DROP SEQUENCE IF EXISTS koan_raw_probe_seq");
        }
    }

    private static IRawQueryRepository<RawBoundaryProbe, string> Repository(BoundHost host)
        => (IRawQueryRepository<RawBoundaryProbe, string>)host.Services.GetRequiredService<IDataService>()
            .GetRepository<RawBoundaryProbe, string>();

    private async Task<NpgsqlConnection> OpenSide()
    {
        var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [StorageName("koan_raw_boundary_probe")]
    private sealed class RawBoundaryProbe : Entity<RawBoundaryProbe>
    {
        public string Label { get; set; } = "";
    }
}
