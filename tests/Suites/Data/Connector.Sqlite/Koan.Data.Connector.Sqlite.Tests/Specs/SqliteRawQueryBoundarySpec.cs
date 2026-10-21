using Koan.Data.Abstractions.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// WEB-0074 defense in depth. <c>QueryRaw</c> and <c>CountRaw</c> take a predicate the CALLER owns, and a caller can
/// still be wrong: the HTTP and MCP edges once handed them request text. These pin the floor under the escape
/// hatch - one statement, on a connection the engine will not let write - so a mistake costs a read, not the
/// database. (A predicate can still read: this does not make raw text safe for an untrusted caller.)
/// </summary>
public sealed class SqliteRawQueryBoundarySpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: a raw query is one statement - a second statement is refused and nothing runs")]
    public async Task A_second_statement_is_refused_before_the_engine_sees_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-one-statement"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }, new RawBoundaryProbe { Id = "b" }]);
        await using var side = await OpenSide();
        await Execute(side, "CREATE TABLE IF NOT EXISTS KOAN_RAW_SENTINEL (Id INTEGER PRIMARY KEY); " +
                            "INSERT OR IGNORE INTO KOAN_RAW_SENTINEL VALUES (1)");
        var repository = Repository(host);

        // Each payload would have run before the fix: the reader executes the whole batch when it is disposed,
        // including when the first statement returned no rows.
        foreach (var payload in new[]
                 {
                     "1 = 1); DROP TABLE KOAN_RAW_SENTINEL; --",
                     "1 = 0); DROP TABLE KOAN_RAW_SENTINEL; --",
                     "1 = 1; DELETE FROM KOAN_RAW_SENTINEL",
                 })
        {
            var query = async () => await Data<RawBoundaryProbe, string>.QueryRaw(payload);
            await query.Should().ThrowAsync<NotSupportedException>($"QueryRaw({payload}) carries a second statement")
                .WithMessage("*exactly one statement*");

            var count = async () => await repository.CountRaw(payload, null);
            await count.Should().ThrowAsync<NotSupportedException>($"CountRaw({payload}) carries a second statement")
                .WithMessage("*exactly one statement*");
        }

        (await Scalar(side, "SELECT COUNT(*) FROM KOAN_RAW_SENTINEL")).Should().Be(1, "no second statement ran");
        (await Data<RawBoundaryProbe, string>.QueryRaw("1 = 1")).Should().HaveCount(2, "an ordinary predicate still runs");
    }

    [Fact(DisplayName = "Sqlite: raw queries and counts run on a query_only connection that no other caller inherits")]
    public async Task Raw_reads_run_query_only_and_leave_ordinary_connections_alone()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-query-only"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }, new RawBoundaryProbe { Id = "b" }]);
        var repository = Repository(host);

        // The engine reports the mode the predicate itself executes in.
        const string observed = "(SELECT query_only FROM pragma_query_only) = 1";
        (await Data<RawBoundaryProbe, string>.QueryRaw(observed)).Should().HaveCount(2, "QueryRaw runs query_only");
        (await repository.CountRaw(observed, null)).Value.Should().Be(2, "CountRaw runs query_only");

        // Pooled connections keep PRAGMA state, so the mode must never be set on one: an ordinary write after the
        // raw reads would fail with "attempt to write a readonly database".
        await RawBoundaryProbe.Upsert(new RawBoundaryProbe { Id = "c" });
        (await RawBoundaryProbe.Get("c")).Should().NotBeNull();
        (await Data<RawBoundaryProbe, string>.QueryRaw("(SELECT query_only FROM pragma_query_only) = 0"))
            .Should().BeEmpty("the raw path is query_only every time, not only the first");
    }

    [Fact(DisplayName = "Sqlite: a Read-labelled SQL instruction runs query_only, so a mislabelled write fails at the engine")]
    public async Task A_read_labelled_instruction_cannot_write()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-read-instruction"));
        await RawBoundaryProbe.UpsertMany([new RawBoundaryProbe { Id = "a" }]);
        await using var side = await OpenSide();
        await Execute(side, "CREATE TABLE IF NOT EXISTS KOAN_RAW_SENTINEL (Id INTEGER PRIMARY KEY); " +
                            "INSERT OR IGNORE INTO KOAN_RAW_SENTINEL VALUES (1)");

        // NK-037: the effect is the caller's label, and the source policy trusts it. The provider must not.
        var mislabelled = async () => await Data<RawBoundaryProbe, string>.Execute(
            "DELETE FROM KOAN_RAW_SENTINEL", Koan.Data.Abstractions.Sources.DataOperationEffect.Read);
        await mislabelled.Should().ThrowAsync<SqliteException>().WithMessage("*readonly*");
        (await Scalar(side, "SELECT COUNT(*) FROM KOAN_RAW_SENTINEL")).Should().Be(1, "the mislabelled DELETE did not run");

        (await Data<RawBoundaryProbe, string>.Execute<long>(Koan.Data.Abstractions.Instructions.InstructionSql.Scalar(
                "SELECT query_only FROM pragma_query_only", Koan.Data.Abstractions.Sources.DataOperationEffect.Read)))
            .Should().Be(1, "a Read instruction runs on the query_only lane");
        (await Data<RawBoundaryProbe, string>.Execute<long>(Koan.Data.Abstractions.Instructions.InstructionSql.Scalar(
                "SELECT query_only FROM pragma_query_only", Koan.Data.Abstractions.Sources.DataOperationEffect.Write)))
            .Should().Be(0, "a declared write keeps the ordinary connection");
    }

    private static IRawQueryRepository<RawBoundaryProbe, string> Repository(BoundHost host)
        => (IRawQueryRepository<RawBoundaryProbe, string>)host.Services.GetRequiredService<IDataService>()
            .GetRepository<RawBoundaryProbe, string>();

    private async Task<SqliteConnection> OpenSide()
    {
        var connection = new SqliteConnection(Fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [StorageName("KOAN_RAW_BOUNDARY_PROBE")]
    private sealed class RawBoundaryProbe : Entity<RawBoundaryProbe>
    {
        public string Label { get; set; } = "";
    }
}
