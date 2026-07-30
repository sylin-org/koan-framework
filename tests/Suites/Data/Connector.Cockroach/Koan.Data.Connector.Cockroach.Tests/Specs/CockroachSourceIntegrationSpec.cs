using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Cockroach.Tests.Specs;

public sealed class CockroachSourceIntegrationSpec(CockroachFixture fixture)
{
    [Fact]
    public async Task Named_reads_and_neutral_inspection_form_one_read_only_source_journey()
    {
        const string sourceName = "SourceJourney";
        const string tableName = "source_work_items";
        await Execute($"""
            DROP TABLE IF EXISTS "{tableName}";
            CREATE TABLE "{tableName}" (id bigint PRIMARY KEY, title text NOT NULL, priority integer NOT NULL);
            INSERT INTO "{tableName}" VALUES (1, 'polish', 5), (2, 'ship', 9);
            """);

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(Settings(sourceName))
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source(sourceName).Query("work.ready", query => query
                    .Lane("Reports")
                    .Sql($"SELECT id AS \"Id\", title AS \"Title\" FROM \"{tableName}\" WHERE priority >= @minimum ORDER BY id")
                    .Parameter<int>("minimum"));
                koan.Data.Source(sourceName).Scalar<long>("work.count", query => query
                    .Lane("Reports")
                    .Sql($"SELECT COUNT(*) FROM \"{tableName}\""));
                koan.Data.Source(sourceName).Query("work.mutate", query => query
                    .Lane("Reports")
                    .Sql($"UPDATE \"{tableName}\" SET title = 'changed' WHERE id = 2 RETURNING id"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        var source = KoanData.Source(sourceName);
        (await source.Query("work.ready", new { minimum = 7 })).Project<WorkItem>()
            .Should().ContainSingle().Which.Should().Be(new WorkItem(2, "ship"));
        (await source.Scalar<long>("work.count")).Should().Be(2);
        await FluentActions.Invoking(() => source.Query("work.mutate"))
            .Should().ThrowAsync<PostgresException>();

        var inspector = source.Inspect();
        var containers = await AllContainers(inspector);
        var descriptor = containers.Should().ContainSingle(item => item.Address.Name == tableName).Which;
        descriptor.ProviderKind.Should().Be("BASE TABLE");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);
        descriptor.EffectiveOperations.Should().NotHaveFlag(StorageContainerOperations.Write);

        var reference = await inspector.Resolve(StorageAddress.From("public", tableName));
        var described = await inspector.Describe(reference);
        described.RecordShape!.Select(field => field.Name).Should().Equal("id", "title", "priority");

        var complete = await inspector.Sample(reference, 10);
        complete.Records.Should().HaveCount(2);
        complete.Completion.Should().Be(RecordSetCompletion.Complete);

        var bounded = await inspector.Sample(reference, 1);
        bounded.Records.Should().ContainSingle();
        bounded.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
    }

    private Dictionary<string, string?> Settings(string source) =>
        new(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            [$"Koan:Data:Sources:{source}:Adapter"] = "cockroach",
            [$"Koan:Data:Sources:{source}:ConnectionString"] = fixture.ConnectionString,
            [$"Koan:Data:Sources:{source}:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            [$"Koan:Data:Sources:{source}:Access"] = DataSourceAccess.ReadOnly.ToString(),
            [$"Koan:Data:Sources:{source}:ReadLanes:Reports:ConnectionString"] = fixture.ConnectionString
        };

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<StorageContainerDescriptor>> AllContainers(IDataSourceInspector inspector)
    {
        var containers = new List<StorageContainerDescriptor>();
        string? continuation = null;
        do
        {
            var page = await inspector.Containers(10, continuation);
            containers.AddRange(page.Containers);
            continuation = page.Continuation;
        } while (continuation is not null);
        return containers;
    }

    private sealed record WorkItem(long Id, string Title);
}
