using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Routing;

/// <summary>
/// NK-305: the store identity a transfer compares is derived from the connection text, so <c>localhost</c> and
/// <c>127.0.0.1</c> name one deployment two ways. Before NK-305 a Move between them deleted every row it copied; now the
/// adapter asks each deployment who answered and refuses the Move it cannot prove safe.
/// </summary>
[Collection(nameof(MongoTransferStoreProofSpec))]
[CollectionDefinition(nameof(MongoTransferStoreProofSpec), DisableParallelization = true)]
public sealed class MongoTransferStoreProofSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    private const int Rows = 3;

    [Fact]
    public async Task Move_to_another_spelling_of_the_same_deployment_is_refused()
    {
        RequireBackingStore();
        var other = $"koan_proof_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(Settings(other));
        using var lease = Lease(NewPartition("proof-same"));
        await Seed();

        var refusal = await Record.ExceptionAsync(() => ProofParcel.Move().To(source: "Spelled").Run());

        (await ProofParcel.All()).Should().HaveCount(Rows, "a Move onto its own collection must not delete anything");
        refusal.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("could not prove");
    }

    [Fact]
    public async Task Move_to_another_database_on_the_same_deployment_is_proven_and_runs()
    {
        RequireBackingStore();
        var other = $"koan_proof_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(Settings(other));
        try
        {
            using var lease = Lease(NewPartition("proof-other"));
            await Seed();

            var result = await ProofParcel.Move().To(source: "Other").Run();

            result.CopiedCount.Should().Be(Rows);
            (await ProofParcel.All()).Should().BeEmpty();
            using (EntityContext.Source("Other"))
                (await ProofParcel.All()).Should().HaveCount(Rows);
        }
        finally
        {
            await new MongoClient(Fixture.ConnectionString).DropDatabaseAsync(other);
        }
    }

    private static async Task Seed()
    {
        for (var index = 0; index < Rows; index++)
            await new ProofParcel { Label = $"parcel-{index}" }.Save();
    }

    private Dictionary<string, string?> Settings(string otherDatabase) => new(StringComparer.Ordinal)
    {
        ["Koan:Data:Sources:Spelled:Adapter"] = "mongo",
        ["Koan:Data:Sources:Spelled:ConnectionString"] = OtherSpelling(Fixture.ConnectionString),
        ["Koan:Data:Sources:Spelled:Database"] = Fixture.Database,
        ["Koan:Data:Sources:Other:Adapter"] = "mongo",
        ["Koan:Data:Sources:Other:ConnectionString"] = Fixture.ConnectionString,
        ["Koan:Data:Sources:Other:Database"] = otherDatabase,
    };

    private static string OtherSpelling(string connection) => connection.Contains("127.0.0.1", StringComparison.Ordinal)
        ? connection.Replace("127.0.0.1", "localhost", StringComparison.Ordinal)
        : connection.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);

    public sealed class ProofParcel : Entity<ProofParcel>
    {
        public string Label { get; set; } = "";
    }
}
