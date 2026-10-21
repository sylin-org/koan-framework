using Koan.Core;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-305, DATA-0148: a SQLite database holds one marker, the single row of the reserved <c>__koan_store</c> table. Two
/// endpoints are one store exactly when they read the same marker, which proves what locks cannot: a URI data source.
/// The marker is created only under the source's DDL consent and is never listed as an entity set.
/// </summary>
[Collection(nameof(SqliteStoreMarkerSpec))]
[CollectionDefinition(nameof(SqliteStoreMarkerSpec), DisableParallelization = true)]
public sealed class SqliteStoreMarkerSpec
{
    private const int Rows = 3;

    public sealed class MarkedParcel : Entity<MarkedParcel>
    {
        public string Label { get; set; } = "";
    }

    [Fact]
    public async Task A_Move_to_a_URI_naming_another_file_is_proven_apart_by_the_markers_and_runs()
    {
        await using var host = await Start();
        await Seed();

        var result = await MarkedParcel.Move().To(source: "UriOther").Run();

        result.CopiedCount.Should().Be(Rows);
        result.DeletedCount.Should().Be(Rows);
        (await MarkedParcel.All()).Should().BeEmpty();
        using (EntityContext.Source("UriOther"))
            (await MarkedParcel.All()).Should().HaveCount(Rows);
    }

    [Fact]
    public async Task A_Move_to_a_URI_naming_the_same_file_is_refused()
    {
        await using var host = await Start();
        await Seed();

        var refusal = await Record.ExceptionAsync(() => MarkedParcel.Move().To(source: "UriSame").Run());

        (await MarkedParcel.All()).Should().HaveCount(Rows, "a Move onto its own file must not delete anything");
        refusal.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain(StoreMarker.Name);
    }

    [Fact]
    public async Task Every_spelling_of_one_file_reads_one_marker_and_another_file_reads_its_own()
    {
        await using var host = await Start();
        var factory = Factory(host.Services);

        var marker = await factory.ReadStoreMarker(host.Services, "Default", create: true);

        marker.Should().NotBeNullOrWhiteSpace();
        (await factory.ReadStoreMarker(host.Services, "Spelled", create: false)).Should().Be(marker);
        (await factory.ReadStoreMarker(host.Services, "UriSame", create: false)).Should().Be(marker);
        (await factory.ReadStoreMarker(host.Services, "Default", create: true)).Should().Be(marker, "it is written once");
        (await factory.ReadStoreMarker(host.Services, "Preview", create: true))
            .Should().NotBeNullOrWhiteSpace().And.NotBe(marker);
    }

    [Fact]
    public async Task A_ReadOnly_source_reads_the_marker_and_never_creates_it_or_its_file()
    {
        await using var host = await Start();
        var factory = Factory(host.Services);

        (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().BeNull();
        File.Exists(host.Shared).Should().BeFalse("a ReadOnly source creates nothing");

        var written = await factory.ReadStoreMarker(host.Services, "Writer", create: true);
        (await factory.ReadStoreMarker(host.Services, "Reader", create: true)).Should().Be(written);
    }

    [Fact]
    public async Task A_source_whose_DDL_policy_forbids_creation_does_not_create_the_marker()
    {
        await using var host = await Start(ddlPolicy: "Validate");
        await using (var connection = new SqliteConnection($"Data Source={host.Primary};Pooling=False"))
            await connection.OpenAsync();

        (await Factory(host.Services).ReadStoreMarker(host.Services, "Default", create: true)).Should().BeNull();
        (await Tables(host.Primary)).Should().NotContain(StoreMarker.Name);
    }

    [Fact]
    public async Task The_marker_is_not_listed_or_resolved_as_a_container()
    {
        await using var host = await Start();
        await Seed();
        var factory = Factory(host.Services);
        (await factory.ReadStoreMarker(host.Services, "Default", create: true)).Should().NotBeNullOrWhiteSpace();
        (await Tables(host.Primary)).Should().Contain(StoreMarker.Name);
        var inspector = ((IDataSourceIntegrationFactory)factory).CreateSource(host.Services, "Default").Inspector!;

        var page = await inspector.Containers(500, null);

        page.Containers.Select(static container => container.Address.Name)
            .Should().NotContain(StoreMarker.Name).And.NotBeEmpty();
        await FluentActions.Invoking(() => inspector.Resolve(StorageAddress.From(StoreMarker.Name)))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    private static IDataAdapterFactory Factory(IServiceProvider services) =>
        services.GetServices<IDataAdapterFactory>().Single(static factory => factory.Provider == "sqlite");

    private static async Task<List<string>> Tables(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table'";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task Seed()
    {
        for (var i = 0; i < Rows; i++)
            await new MarkedParcel { Label = $"parcel-{i}" }.Save();
    }

    private static async Task<MarkerHost> Start(string ddlPolicy = "AutoCreate")
    {
        var primary = TempDatabase("primary");
        var preview = TempDatabase("preview");
        var other = TempDatabase("uri-other");
        var shared = TempDatabase("shared");
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", ddlPolicy)
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={primary}")
            .WithSetting("Koan:Data:Sources:Preview:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Preview:ConnectionString", $"Data Source={preview}")
            .WithSetting("Koan:Data:Sources:Spelled:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Spelled:ConnectionString", $"Data Source={OtherSpelling(primary)}")
            .WithSetting("Koan:Data:Sources:UriSame:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:UriSame:ConnectionString", $"Data Source={Uri(primary)}")
            .WithSetting("Koan:Data:Sources:UriOther:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:UriOther:ConnectionString", $"Data Source={Uri(other)}")
            .WithSetting("Koan:Data:Sources:Reader:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Reader:ConnectionString", $"Data Source={shared}")
            .WithSetting("Koan:Data:Sources:Reader:Access", "ReadOnly")
            .WithSetting("Koan:Data:Sources:Writer:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Writer:ConnectionString", $"Data Source={shared};Pooling=False")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();
        return new MarkerHost(host, primary, shared, preview, other);
    }

    private static string Uri(string path) => new System.Uri(path).AbsoluteUri;

    private static string OtherSpelling(string path)
    {
        if (OperatingSystem.IsWindows()) return @"\\?\" + path;
        var link = Path.Combine(Path.GetTempPath(), $"koan-sqlite-link-{Guid.CreateVersion7():n}");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(path)!);
        return Path.Combine(link, Path.GetFileName(path));
    }

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-marker-{label}-{Guid.CreateVersion7():n}.db");

    private sealed class MarkerHost(IntegrationHost host, string primary, string shared, params string[] files)
        : IAsyncDisposable
    {
        public string Primary => primary;
        public string Shared => shared;
        public IServiceProvider Services => host.Services;

        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var file in files.Append(primary).Append(shared))
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
