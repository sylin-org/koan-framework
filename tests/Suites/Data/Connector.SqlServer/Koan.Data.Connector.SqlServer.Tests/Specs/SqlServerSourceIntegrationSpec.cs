using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

public sealed class SqlServerSourceIntegrationSpec(SqlServerFixture fixture)
{
    [Fact]
    public async Task Named_reads_and_neutral_inspection_form_one_read_only_source_journey()
    {
        const string sourceName = "SourceJourney";
        const string tableName = "source_work_items";
        await Execute($"""
            DROP TABLE IF EXISTS [dbo].[{tableName}];
            CREATE TABLE [dbo].[{tableName}] ([id] bigint PRIMARY KEY, [title] nvarchar(200) NOT NULL, [priority] int NOT NULL);
            INSERT INTO [dbo].[{tableName}] VALUES (1, N'polish', 5), (2, N'ship', 9);
            """);

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(Settings(sourceName))
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source(sourceName).Query("work.ready", query => query
                    .Lane("Reports")
                    .Sql($"SELECT [id] AS [Id], [title] AS [Title] FROM [dbo].[{tableName}] WHERE [priority] >= @minimum ORDER BY [id]")
                    .Parameter<int>("minimum"));
                koan.Data.Source(sourceName).Scalar<long>("work.count", query => query
                    .Lane("Reports")
                    .Sql($"SELECT COUNT_BIG(*) FROM [dbo].[{tableName}]"));
                koan.Data.Source(sourceName).Query("work.mutate", query => query
                    .Lane("Reports")
                    .Sql($"UPDATE [dbo].[{tableName}] SET [title] = N'changed' OUTPUT inserted.[id] WHERE [id] = 2"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        var source = KoanData.Source(sourceName);
        var ready = await source.Query("work.ready", new { minimum = 7 });
        ready.Project<WorkItem>().Should().ContainSingle()
            .Which.Should().Be(new WorkItem(2, "ship"));
        (await source.Scalar<long>("work.count")).Should().Be(2);

        (await source.Query("work.mutate")).Records.Should().ContainSingle();
        (await Scalar($"SELECT [title] FROM [dbo].[{tableName}] WHERE [id] = 2"))
            .Should().Be("ship", "registered SQL executes inside a rollback-only read lane transaction");

        var inspector = source.Inspect();
        var containers = await AllContainers(inspector);
        var descriptor = containers.Should().ContainSingle(item => item.Address.Name == tableName).Which;
        descriptor.ProviderKind.Should().Be("BASE TABLE");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);
        descriptor.EffectiveOperations.Should().NotHaveFlag(StorageContainerOperations.Write);

        var reference = await inspector.Resolve(StorageAddress.From("dbo", tableName));
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
            [$"Koan:Data:Sources:{source}:Adapter"] = "sqlserver",
            [$"Koan:Data:Sources:{source}:ConnectionString"] = fixture.ConnectionString,
            [$"Koan:Data:Sources:{source}:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            [$"Koan:Data:Sources:{source}:Access"] = DataSourceAccess.ReadOnly.ToString(),
            [$"Koan:Data:Sources:{source}:ReadLanes:Reports:ConnectionString"] = fixture.ConnectionString
        };

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
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
