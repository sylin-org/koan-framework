using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Data.Cutover.CrossProvider.Tests;

/// <summary>
/// NK-200. No automated test transferred rows between different adapters. This spec does, on live stores: SQLite to
/// PostgreSQL with a predicate, PostgreSQL to MongoDB as a Move, MongoDB to a second SQLite store as a Mirror push, and
/// MongoDB back to SQLite as a Move. After every step each row is compared with the row as SQLite first stored it, field
/// by field and as Koan's canonical document, and the source's state is checked. MongoDB keeps a DateTimeOffset to the
/// millisecond (a BSON date), so a row that has passed through it is compared with the stored row at that precision.
/// </summary>
public sealed class CrossProviderTransferSpec(CrossDatabaseFixture databases)
{
    private const string Postgres = "TransferPostgres";
    private const string Mongo = "TransferMongo";
    private const string SqliteReturn = "TransferSqliteReturn";
    private const string Back = "back";
    private const string PostgresSchema = "koan_transfer_spec";
    private const string MongoDatabase = "koan_transfer";

    public sealed class FidelityRecord : Entity<FidelityRecord>
    {
        public string Value { get; set; } = string.Empty;
        public DateTimeOffset ObservedAt { get; set; }
        public Guid CorrelationId { get; set; }
        public byte[] Evidence { get; set; } = [];
        public decimal Amount { get; set; }
        public IReadOnlyDictionary<string, string?> Attributes { get; set; } = new Dictionary<string, string?>();

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    [Fact]
    public async Task Rows_cross_SQLite_PostgreSQL_and_MongoDB_and_come_back_with_every_value_intact()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "koan-cross-provider-transfer", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        await SpecStores.Drop(databases, PostgresSchema, MongoDatabase);
        var host = await Boot(root);
        try
        {
            AppHost.Current = host.Services;

            foreach (var record in Seed()) await record.Save(ct);
            var stored = (await FidelityRecord.All(ct)).OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
            stored.Should().HaveCount(4);
            var selected = stored.Where(record => record.Amount > 0).ToArray();
            selected.Should().HaveCount(3);

            await Task.Delay(30, ct);   // an OnSave stamp taken by a transfer would now differ from the stored one

            var toPostgres = await FidelityRecord.Copy(record => record.Amount > 0).To(source: Postgres).Batch(2).Run(ct);
            toPostgres.ReadCount.Should().Be(3);
            toPostgres.CopiedCount.Should().Be(3);
            toPostgres.ResolvedTo!.Provider.Should().Be("postgres");
            await AssertHolds(null, stored, ct);
            await AssertHolds(Postgres, selected, ct);

            var toMongo = await FidelityRecord.Move().From(source: Postgres).To(source: Mongo).Batch(2).Run(ct);
            toMongo.CopiedCount.Should().Be(3);
            toMongo.DeletedCount.Should().Be(3);
            toMongo.ResolvedFrom!.Provider.Should().Be("postgres");
            toMongo.ResolvedTo!.Provider.Should().Be("mongo");
            await AssertHolds(Postgres, [], ct);
            var throughMongo = selected.Select(AtMongoPrecision).ToArray();
            await AssertHolds(Mongo, throughMongo, ct);

            var mirrored = await FidelityRecord.Mirror(MirrorMode.Push).From(source: Mongo).To(source: SqliteReturn).Run(ct);
            mirrored.CopiedCount.Should().Be(3);
            mirrored.DeletedCount.Should().Be(0);
            await AssertHolds(SqliteReturn, throughMongo, ct);
            await AssertHolds(Mongo, throughMongo, ct);

            var back = await FidelityRecord.Move().From(source: Mongo).To(source: "", partition: Back).Run(ct);
            back.CopiedCount.Should().Be(3);
            back.DeletedCount.Should().Be(3);
            await AssertHolds(Mongo, [], ct);
            using (EntityContext.Partition(Back)) await AssertHolds(null, throughMongo, ct);
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            await host.DisposeAsync();
            await SpecStores.Drop(databases, PostgresSchema, MongoDatabase);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>The source holds exactly <paramref name="expected"/>, each row equal field by field and as a document.</summary>
    private static async Task AssertHolds(string? source, IReadOnlyList<FidelityRecord> expected, CancellationToken ct)
    {
        using var scope = source is null ? null : EntityContext.Source(source);
        var actual = (await FidelityRecord.All(ct)).OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
        actual.Select(record => record.Id).Should().Equal(expected.Select(record => record.Id), $"source {source ?? "Default"}");
        for (var index = 0; index < actual.Length; index++)
        {
            actual[index].Should().BeEquivalentTo(expected[index], $"row {expected[index].Id} in {source ?? "Default"}");
            JToken.DeepEquals(
                    EntityJsonSerialization.SerializeDocumentToken(actual[index]),
                    EntityJsonSerialization.SerializeDocumentToken(expected[index]))
                .Should().BeTrue($"row {expected[index].Id} in {source ?? "Default"} is the same canonical document");
        }
    }

    /// <summary>The stored row as MongoDB represents it: every DateTimeOffset to the millisecond.</summary>
    private static FidelityRecord AtMongoPrecision(FidelityRecord stored) => new()
    {
        Id = stored.Id,
        Value = stored.Value,
        ObservedAt = Milliseconds(stored.ObservedAt),
        CorrelationId = stored.CorrelationId,
        Evidence = stored.Evidence,
        Amount = stored.Amount,
        Attributes = stored.Attributes,
        UpdatedAt = Milliseconds(stored.UpdatedAt)
    };

    private static DateTimeOffset Milliseconds(DateTimeOffset value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);

    private static IReadOnlyList<FidelityRecord> Seed() =>
    [
        Record("A", "upper", 1.25m, [0, 1, 255]),
        Record("a", "lower", -20.500m, [9, 8, 7]),
        Record("z-last", "punctuation", 12345.123456m, []),
        Record("éclair", "unicode", 0.0000001m, [42])
    ];

    private static FidelityRecord Record(string id, string value, decimal amount, byte[] evidence) => new()
    {
        Id = id,
        Value = value,
        ObservedAt = new DateTimeOffset(2026, 8, 6, 10, id.Length, 0, TimeSpan.FromHours(2)),
        CorrelationId = Guid.Parse("f789e118-7612-48c8-85fa-e6cc9167de46"),
        Evidence = evidence,
        Amount = amount,
        Attributes = new Dictionary<string, string?> { ["zeta"] = value, ["alpha"] = null }
    };

    private Task<IntegrationHost> Boot(string root) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(root, "source.db")};Pooling=False",
            [$"Koan:Data:Sources:{Postgres}:Adapter"] = "postgres",
            [$"Koan:Data:Sources:{Postgres}:ConnectionString"] = databases.PostgresConnectionString,
            [$"Koan:Data:Sources:{Postgres}:SearchPath"] = PostgresSchema,
            [$"Koan:Data:Sources:{Postgres}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{Postgres}:Access"] = "ReadWrite",
            [$"Koan:Data:Sources:{Mongo}:Adapter"] = "mongo",
            [$"Koan:Data:Sources:{Mongo}:ConnectionString"] = databases.MongoConnectionString,
            [$"Koan:Data:Sources:{Mongo}:Database"] = MongoDatabase,
            [$"Koan:Data:Sources:{Mongo}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{Mongo}:Access"] = "ReadWrite",
            [$"Koan:Data:Sources:{SqliteReturn}:Adapter"] = "sqlite",
            [$"Koan:Data:Sources:{SqliteReturn}:ConnectionString"] = $"Data Source={Path.Combine(root, "return.db")};Pooling=False",
            [$"Koan:Data:Sources:{SqliteReturn}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{SqliteReturn}:Access"] = "ReadWrite",
            ["Koan:Data:Postgres:Readiness:EnableReadinessGating"] = "false",
            ["Koan:Data:Route:StatePath"] = Path.Combine(root, "control", "active-route.json")
        })
        .ConfigureServices(static services => services.AddKoan())
        .StartAsync(TestContext.Current.CancellationToken);
}
