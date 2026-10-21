using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Routing;

/// <summary>
/// NK-305, DATA-0148: a MongoDB store holds one marker, a document in the reserved <c>__koan_store</c> collection of the
/// source's database. <c>localhost</c> and <c>127.0.0.1</c> read one marker, another database holds its own, a
/// ReadOnly source never creates the collection, and no listing reports it.
/// </summary>
[Collection(nameof(MongoStoreMarkerSpec))]
[CollectionDefinition(nameof(MongoStoreMarkerSpec), DisableParallelization = true)]
public sealed class MongoStoreMarkerSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Two_spellings_of_one_database_read_one_marker_and_another_database_reads_its_own()
    {
        RequireBackingStore();
        var other = $"koan_marker_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(Settings(other));
        try
        {
            var factory = Factory(host);
            var marker = await factory.ReadStoreMarker(host.Services, "Default", create: true);
            var spelled = await factory.ReadStoreMarker(host.Services, "Spelled", create: false);
            var elsewhere = await factory.ReadStoreMarker(host.Services, "Other", create: true);

            marker.Should().NotBeNullOrWhiteSpace();
            spelled.Should().Be(marker);
            elsewhere.Should().NotBeNullOrWhiteSpace().And.NotBe(marker);
            (await factory.ReadStoreMarker(host.Services, "Default", create: true)).Should().Be(marker, "it is written once");
        }
        finally
        {
            await new MongoClient(Fixture.ConnectionString).DropDatabaseAsync(other);
        }
    }

    [Fact]
    public async Task A_ReadOnly_source_reads_the_marker_and_never_creates_it()
    {
        RequireBackingStore();
        var other = $"koan_marker_{Guid.CreateVersion7():N}";
        var settings = Settings(other);
        settings["Koan:Data:Sources:Reader:Adapter"] = "mongo";
        settings["Koan:Data:Sources:Reader:ConnectionString"] = Fixture.ConnectionString;
        settings["Koan:Data:Sources:Reader:Database"] = other;
        settings["Koan:Data:Sources:Reader:Access"] = "ReadOnly";
        await using var host = await BootAsync(settings);
        try
        {
            var factory = Factory(host);
            (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().BeNull();
            var names = await (await new MongoClient(Fixture.ConnectionString).GetDatabase(other)
                .ListCollectionNamesAsync()).ToListAsync();
            names.Should().NotContain(StoreMarker.Name, "a ReadOnly source creates nothing");

            var written = await factory.ReadStoreMarker(host.Services, "Other", create: true);
            (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().Be(written);
        }
        finally
        {
            await new MongoClient(Fixture.ConnectionString).DropDatabaseAsync(other);
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
        .Single(static factory => factory.Provider == "mongo");

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
}
