using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Direct;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public sealed class SqliteSourceIntegrationSpec(SqliteFixture fixture)
{
    [Fact]
    public async Task Named_reads_and_inspection_form_one_source_first_journey()
    {
        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Default:ReadLanes:Reports:ConnectionString"] = fixture.ConnectionString
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source("Default").Query("work.ready", query => query
                    .Lane("Reports")
                    .Sql("SELECT id AS Id, title AS Title FROM work_items WHERE priority >= @minimum ORDER BY id")
                    .Parameter<long>("minimum"));
                koan.Data.Source("Default").Scalar<long>("work.count", query => query
                    .Lane("Reports")
                    .Sql("SELECT COUNT(*) FROM work_items"));
                koan.Data.Source("Default").Query("work.mutate", query => query
                    .Lane("Reports")
                    .Sql("UPDATE work_items SET title = 'changed' WHERE id = 2 RETURNING id"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        var data = host.Services.GetRequiredService<IDataService>();
        await data.Direct(source: "Default").Effect(DataOperationEffect.SchemaOrAdmin)
            .Execute("CREATE TABLE work_items (id INTEGER PRIMARY KEY, title TEXT NOT NULL, priority INTEGER NOT NULL)");
        await data.Direct(source: "Default").Effect(DataOperationEffect.Write)
            .Execute("INSERT INTO work_items (id, title, priority) VALUES (1, 'polish', 5), (2, 'ship', 9)");

        var source = KoanData.Source("Default");
        var ready = await source.Query("work.ready", new { minimum = 7L });
        ready.Project<WorkItem>().Should().ContainSingle()
            .Which.Should().Be(new WorkItem(2, "ship"));
        (await source.Scalar<long>("work.count")).Should().Be(2);
        await FluentActions.Invoking(() => source.Query("work.mutate"))
            .Should().ThrowAsync<SqliteException>();

        var inspector = source.Inspect();
        var containers = new List<StorageContainerDescriptor>();
        string? continuation = null;
        do
        {
            var page = await inspector.Containers(10, continuation);
            containers.AddRange(page.Containers);
            continuation = page.Continuation;
        } while (continuation is not null);

        var descriptor = containers.Should().ContainSingle(item => item.Address.Name == "work_items").Which;
        descriptor.ProviderKind.Should().Be("table");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);

        var reference = await inspector.Resolve(StorageAddress.From("work_items"));
        var described = await inspector.Describe(reference);
        described.RecordShape!.Select(field => field.Name).Should().Equal("id", "title", "priority");

        var complete = await inspector.Sample(reference, 10);
        complete.Records.Should().HaveCount(2);
        complete.Completion.Should().Be(RecordSetCompletion.Complete);

        var bounded = await inspector.Sample(reference, 1);
        bounded.Records.Should().ContainSingle();
        bounded.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
    }

    private sealed record WorkItem(long Id, string Title);
}
