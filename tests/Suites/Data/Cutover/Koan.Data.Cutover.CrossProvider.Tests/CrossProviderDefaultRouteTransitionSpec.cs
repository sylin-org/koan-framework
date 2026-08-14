using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Cutover.Options;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Cutover.CrossProvider.Tests;

public sealed class CrossProviderDefaultRouteTransitionSpec(CrossDatabaseFixture databases)
{
    private const string Mongo = "MongoTarget";
    private const string Postgres = "PostgresTarget";
    private const string SqliteReturn = "SqliteReturn";

    [Fact]
    public async Task Sqlite_to_Mongo_to_Postgres_and_back_preserves_logical_records_and_durable_route_continuity()
    {
        var paths = TestPaths.Create();
        try
        {
            await using (var host = await Boot(paths))
            {
                AppHost.Current = host.Services;
                foreach (var record in SeedRecords())
                    await record.Save(TestContext.Current.CancellationToken);

                var toMongo = await Koan.Data.Core.Data.Source(Mongo)
                    .PromoteToDefault()
                    .Run(TestContext.Current.CancellationToken);

                toMongo.Previous.Adapter.Should().Be("sqlite");
                toMongo.Active.Adapter.Should().Be("mongo");
                toMongo.Entities.Should().ContainSingle(entity => entity.Count == 4);
                AssertActive(host, Mongo, "mongo", revision: 1);
                await AssertSeedRecords();

                await new CrossProviderRecord
                {
                    Id = "mongo-only",
                    Value = "written after MongoDB activation",
                    ObservedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
                    CorrelationId = Guid.Parse("8b7505c4-a337-4d95-b6d2-cc8a827d59e8"),
                    Evidence = [4, 3, 2, 1],
                    Amount = 88.125m,
                    Attributes = new Dictionary<string, string?> { ["provider"] = "mongo", ["optional"] = null }
                }.Save(TestContext.Current.CancellationToken);

                using (EntityContext.Source("Default"))
                    (await CrossProviderRecord.Get("mongo-only", TestContext.Current.CancellationToken)).Should().BeNull();

                var toPostgres = await Koan.Data.Core.Data.Source(Postgres)
                    .PromoteToDefault()
                    .Run(TestContext.Current.CancellationToken);

                toPostgres.Previous.Adapter.Should().Be("mongo");
                toPostgres.Active.Adapter.Should().Be("postgres");
                toPostgres.Entities.Should().ContainSingle(entity => entity.Count == 5);
                AssertActive(host, Postgres, "postgres", revision: 2);
                using (EntityContext.Source(Postgres))
                    await AssertSeedRecords();
                await AssertSeedRecords();
                (await CrossProviderRecord.Get("mongo-only", TestContext.Current.CancellationToken))!
                    .Value.Should().Be("written after MongoDB activation");

                await new CrossProviderRecord { Id = "postgres-only", Value = "final active route" }
                    .Save(TestContext.Current.CancellationToken);
                using (EntityContext.Source(Mongo))
                    (await CrossProviderRecord.Get("postgres-only", TestContext.Current.CancellationToken)).Should().BeNull();

                File.Exists(paths.State).Should().BeTrue();
                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            await using (var restarted = await Boot(paths))
            {
                AppHost.Current = restarted.Services;
                AssertActive(restarted, Postgres, "postgres", revision: 2);
                (await CrossProviderRecord.Get("postgres-only", TestContext.Current.CancellationToken))!
                    .Value.Should().Be("final active route");
                (await CrossProviderRecord.Get("mongo-only", TestContext.Current.CancellationToken))!
                    .Amount.Should().Be(88.125m);
                await AssertSeedRecords();

                var backToSqlite = await Koan.Data.Core.Data.Source(SqliteReturn)
                    .PromoteToDefault()
                    .Run(TestContext.Current.CancellationToken);

                backToSqlite.Previous.Adapter.Should().Be("postgres");
                backToSqlite.Active.Adapter.Should().Be("sqlite");
                backToSqlite.Entities.Should().ContainSingle(entity => entity.Count == 6);
                AssertActive(restarted, SqliteReturn, "sqlite", revision: 3);
                await new CrossProviderRecord { Id = "sqlite-return-only", Value = "round-trip active route" }
                    .Save(TestContext.Current.CancellationToken);
                using (EntityContext.Source(Postgres))
                    (await CrossProviderRecord.Get("sqlite-return-only", TestContext.Current.CancellationToken)).Should().BeNull();

                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            await using var finalRestart = await Boot(paths);
            AppHost.Current = finalRestart.Services;
            AssertActive(finalRestart, SqliteReturn, "sqlite", revision: 3);
            (await CrossProviderRecord.Get("sqlite-return-only", TestContext.Current.CancellationToken))!
                .Value.Should().Be("round-trip active route");
            await AssertSeedRecords();
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    private Task<IntegrationHost> Boot(TestPaths paths) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={paths.Sqlite};Pooling=False",
            [$"Koan:Data:Sources:{Mongo}:Adapter"] = "mongo",
            [$"Koan:Data:Sources:{Mongo}:ConnectionString"] = databases.MongoConnectionString,
            [$"Koan:Data:Sources:{Mongo}:Database"] = databases.MongoDatabase,
            [$"Koan:Data:Sources:{Mongo}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{Mongo}:Access"] = "ReadWrite",
            [$"Koan:Data:Sources:{Postgres}:Adapter"] = "postgres",
            [$"Koan:Data:Sources:{Postgres}:ConnectionString"] = databases.PostgresConnectionString,
            [$"Koan:Data:Sources:{Postgres}:SearchPath"] = "public",
            [$"Koan:Data:Sources:{Postgres}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{Postgres}:Access"] = "ReadWrite",
            [$"Koan:Data:Sources:{SqliteReturn}:Adapter"] = "sqlite",
            [$"Koan:Data:Sources:{SqliteReturn}:ConnectionString"] = $"Data Source={paths.SqliteReturn};Pooling=False",
            [$"Koan:Data:Sources:{SqliteReturn}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{SqliteReturn}:Access"] = "ReadWrite",
            ["Koan:Data:Postgres:Readiness:EnableReadinessGating"] = "false",
            ["Koan:Data:Route:StatePath"] = paths.State,
            ["Koan:Data:Cutover:WriterOwnership"] =
                CutoverWriterOwnership.HostExclusiveOrExternallyQuiesced.ToString(),
            ["Koan:Data:Cutover:PageSize"] = "2"
        })
        .ConfigureServices(static services => services.AddKoan())
        .StartAsync(TestContext.Current.CancellationToken);

    private static IReadOnlyList<CrossProviderRecord> SeedRecords() =>
    [
        Record("A", "upper", 1.25m, [0, 1, 255]),
        Record("a", "lower", -20.500m, [9, 8, 7]),
        Record("z-last", "punctuation", 12345.123456m, []),
        Record("éclair", "unicode", 0.0000001m, [42])
    ];

    private static CrossProviderRecord Record(string id, string value, decimal amount, byte[] evidence) => new()
    {
        Id = id,
        Value = value,
        ObservedAt = new DateTimeOffset(2026, 8, 6, 10, id.Length, 0, TimeSpan.Zero),
        CorrelationId = Guid.Parse("f789e118-7612-48c8-85fa-e6cc9167de46"),
        Evidence = evidence,
        Amount = amount,
        Attributes = new Dictionary<string, string?>
        {
            ["zeta"] = value,
            ["alpha"] = null
        }
    };

    private static async Task AssertSeedRecords()
    {
        foreach (var expected in SeedRecords())
        {
            var actual = await CrossProviderRecord.Get(expected.Id, TestContext.Current.CancellationToken);
            actual.Should().BeEquivalentTo(expected);
        }
    }

    private static void AssertActive(IntegrationHost host, string source, string adapter, long revision)
    {
        var active = host.Services.GetRequiredService<DefaultDataRouteAuthority>().Current;
        active.Source.Should().Be(source);
        active.Adapter.Should().Be(adapter);
        active.AuthorityRevision.Should().Be(revision);
    }

    private sealed record TestPaths(string Root, string Sqlite, string SqliteReturn, string State)
    {
        internal static TestPaths Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "koan-data-cutover-cross-provider", Guid.CreateVersion7().ToString("n"));
            Directory.CreateDirectory(root);
            return new TestPaths(
                root,
                Path.Combine(root, "source.db"),
                Path.Combine(root, "return.db"),
                Path.Combine(root, "control", "active-route.json"));
        }

        internal void Delete()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }
}
