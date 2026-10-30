using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// NK-305, DATA-0148: a PostgreSQL store holds one marker, a row of the reserved <c>__koan_store</c> table in the schema
/// the source writes to. Two spellings of one database read one marker, another schema holds its own, a ReadOnly source
/// never creates one, and no listing reports it. Container-backed.
/// </summary>
[Collection(nameof(PostgresStoreMarkerSpec))]
[CollectionDefinition(nameof(PostgresStoreMarkerSpec), DisableParallelization = true)]
public sealed class PostgresStoreMarkerSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task Two_spellings_of_one_database_read_one_marker_and_another_schema_reads_its_own()
    {
        RequireBackingStore();
        var schema = $"marker_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Spelled:Adapter"] = "postgres",
            ["Koan:Data:Sources:Spelled:ConnectionString"] = OtherSpelling(Fixture.ConnectionString),
            ["Koan:Data:Sources:Archive:Adapter"] = "postgres",
            ["Koan:Data:Sources:Archive:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:Archive:SearchPath"] = schema,
        });
        var factory = Factory(host);
        try
        {
            var marker = await factory.ReadStoreMarker(host.Services, "Default", create: true);
            var spelled = await factory.ReadStoreMarker(host.Services, "Spelled", create: false);
            var archive = await factory.ReadStoreMarker(host.Services, "Archive", create: true);

            marker.Should().NotBeNullOrWhiteSpace();
            spelled.Should().Be(marker);
            archive.Should().NotBeNullOrWhiteSpace().And.NotBe(marker);
            (await factory.ReadStoreMarker(host.Services, "Default", create: true)).Should().Be(marker, "it is written once");
        }
        finally
        {
            await Execute($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }

    [Fact]
    public async Task A_ReadOnly_source_reads_the_marker_and_never_creates_it()
    {
        RequireBackingStore();
        var schema = $"marker_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Reader:Adapter"] = "postgres",
            ["Koan:Data:Sources:Reader:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:Reader:SearchPath"] = schema,
            ["Koan:Data:Sources:Reader:Access"] = "ReadOnly",
            ["Koan:Data:Sources:Writer:Adapter"] = "postgres",
            ["Koan:Data:Sources:Writer:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:Writer:SearchPath"] = schema,
        });
        var factory = Factory(host);
        try
        {
            (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().BeNull();
            (await Scalar($"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}'"))
                .Should().Be(0L, "a ReadOnly source creates nothing");

            var written = await factory.ReadStoreMarker(host.Services, "Writer", create: true);
            (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().Be(written);
        }
        finally
        {
            await Execute($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }

    [Fact]
    public async Task The_marker_is_not_listed_or_resolved_as_a_container()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var factory = Factory(host);
        (await factory.ReadStoreMarker(host.Services, "Default", create: true)).Should().NotBeNullOrWhiteSpace();
        var inspector = ((IDataSourceIntegrationFactory)factory).CreateSource(host.Services, "Default").Inspector!;

        var names = new List<string>();
        string? continuation = null;
        do
        {
            var page = await inspector.Containers(500, continuation);
            names.AddRange(page.Containers.Select(static container => container.Address.Name));
            continuation = page.ProviderContinuation;
        } while (continuation is not null);

        names.Should().NotContain(StoreMarker.Name);
        await FluentActions.Invoking(() => inspector.Resolve(StorageAddress.From(StoreMarker.Name)))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    private static IDataAdapterFactory Factory(BoundHost host) => host.Services.GetServices<IDataAdapterFactory>()
        .Single(static factory => factory.Provider == "postgres");

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static string OtherSpelling(string connection) => connection.Contains("127.0.0.1", StringComparison.Ordinal)
        ? connection.Replace("127.0.0.1", "127.1", StringComparison.Ordinal)
        : connection.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
