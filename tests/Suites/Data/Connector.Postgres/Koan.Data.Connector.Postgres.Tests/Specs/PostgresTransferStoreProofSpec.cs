namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// NK-305: <c>localhost</c> and <c>127.0.0.1</c> spell one PostgreSQL database two ways, so a Move between them used to
/// delete every row it copied. The adapter now asks the servers, through an advisory lock keyed by database, and
/// refuses the Move it cannot prove safe; a different search path on one database is proven apart. Container-backed.
/// </summary>
[Collection(nameof(PostgresTransferStoreProofSpec))]
[CollectionDefinition(nameof(PostgresTransferStoreProofSpec), DisableParallelization = true)]
public sealed class PostgresTransferStoreProofSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    private const int Rows = 3;

    [Fact]
    public async Task Move_to_another_spelling_of_the_same_database_is_refused()
    {
        RequireBackingStore();
        await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Spelled:Adapter"] = "postgres",
            ["Koan:Data:Sources:Spelled:ConnectionString"] = OtherSpelling(Fixture.ConnectionString),
        });
        using var lease = Lease(NewPartition("proof-same"));
        await Seed();

        var refusal = await Record.ExceptionAsync(() => PgProofParcel.Move().To(source: "Spelled").Run());

        (await PgProofParcel.All()).Should().HaveCount(Rows, "a Move onto its own table must not delete anything");
        refusal.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("could not prove");
    }

    [Fact]
    public async Task Move_to_another_schema_of_the_same_database_is_proven_and_runs()
    {
        RequireBackingStore();
        await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Archive:Adapter"] = "postgres",
            ["Koan:Data:Sources:Archive:ConnectionString"] = OtherSpelling(Fixture.ConnectionString),
            ["Koan:Data:Sources:Archive:SearchPath"] = $"proof_{Guid.CreateVersion7():N}",
        });
        using var lease = Lease(NewPartition("proof-schema"));
        await Seed();

        var result = await PgProofParcel.Move().To(source: "Archive").Run();

        result.CopiedCount.Should().Be(Rows);
        (await PgProofParcel.All()).Should().BeEmpty();
    }

    private static async Task Seed()
    {
        for (var index = 0; index < Rows; index++)
            await new PgProofParcel { Label = $"parcel-{index}" }.Save();
    }

    private static string OtherSpelling(string connection) => connection.Contains("127.0.0.1", StringComparison.Ordinal)
        ? connection.Replace("127.0.0.1", "127.1", StringComparison.Ordinal)
        : connection.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);

    public sealed class PgProofParcel : Entity<PgProofParcel>
    {
        public string Label { get; set; } = "";
    }
}
