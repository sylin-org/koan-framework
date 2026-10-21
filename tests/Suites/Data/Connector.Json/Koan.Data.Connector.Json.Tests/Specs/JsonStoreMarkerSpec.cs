using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Json.Tests.Specs;

/// <summary>
/// NK-305, DATA-0148: a JSON directory holds one marker, the reserved <c>__koan_store</c> file. Before markers, a Move
/// between two existing directories was refused because a link or a path prefix can name one directory two ways; the
/// markers now prove two directories apart and one directory the same.
/// </summary>
[Collection(nameof(JsonStoreMarkerSpec))]
[CollectionDefinition(nameof(JsonStoreMarkerSpec), DisableParallelization = true)]
public sealed class JsonStoreMarkerSpec
{
    private const int Rows = 3;

    [Fact]
    public async Task A_Move_between_two_existing_directories_is_proven_apart_and_runs()
    {
        await using var host = await Start();
        using (EntityContext.Source("Archive"))
            await new MarkedNote { Text = "already-there" }.Save(TestContext.Current.CancellationToken);
        await Seed();

        var result = await MarkedNote.Move().To(source: "Archive").Run(TestContext.Current.CancellationToken);

        result.CopiedCount.Should().Be(Rows);
        (await MarkedNote.All(TestContext.Current.CancellationToken)).Should().BeEmpty();
        using (EntityContext.Source("Archive"))
            (await MarkedNote.All(TestContext.Current.CancellationToken)).Should().HaveCount(Rows + 1);
        File.Exists(Path.Combine(host.Primary, StoreMarker.Name)).Should().BeTrue();
        File.Exists(Path.Combine(host.Archive, StoreMarker.Name)).Should().BeTrue();
    }

    [Fact]
    public async Task A_Move_to_another_spelling_of_the_same_directory_is_refused()
    {
        await using var host = await Start();
        await Seed();

        var refusal = await Record.ExceptionAsync(
            () => MarkedNote.Move().To(source: "Spelled").Run(TestContext.Current.CancellationToken));

        (await MarkedNote.All(TestContext.Current.CancellationToken)).Should().HaveCount(Rows);
        refusal.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain(StoreMarker.Name);
    }

    [Fact]
    public async Task A_ReadOnly_source_reads_the_marker_and_never_creates_it_or_its_directory()
    {
        await using var host = await Start();
        var factory = host.Services.GetServices<IDataAdapterFactory>().Single(static f => f.Provider == "json");

        (await factory.ReadStoreMarker(host.Services, "Reader", create: true, TestContext.Current.CancellationToken))
            .Should().BeNull();
        Directory.Exists(host.Shared).Should().BeFalse();

        var written = await factory.ReadStoreMarker(host.Services, "Writer", create: true, TestContext.Current.CancellationToken);
        written.Should().NotBeNullOrWhiteSpace();
        (await factory.ReadStoreMarker(host.Services, "Reader", create: false, TestContext.Current.CancellationToken))
            .Should().Be(written);
    }

    private static async Task Seed()
    {
        for (var index = 0; index < Rows; index++)
            await new MarkedNote { Text = $"note-{index}" }.Save(TestContext.Current.CancellationToken);
    }

    private static async Task<MarkerHost> Start()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-marker-{Guid.CreateVersion7():N}");
        var primary = Path.Combine(root, "primary");
        var archive = Path.Combine(root, "archive");
        var shared = Path.Combine(root, "shared");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(archive);
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "json",
                ["Koan:Data:Sources:Default:json:DirectoryPath"] = primary,
                ["Koan:Data:Sources:Archive:Adapter"] = "json",
                ["Koan:Data:Sources:Archive:json:DirectoryPath"] = archive,
                ["Koan:Data:Sources:Spelled:Adapter"] = "json",
                ["Koan:Data:Sources:Spelled:json:DirectoryPath"] = OtherSpelling(primary),
                ["Koan:Data:Sources:Reader:Adapter"] = "json",
                ["Koan:Data:Sources:Reader:json:DirectoryPath"] = shared,
                ["Koan:Data:Sources:Reader:Access"] = "ReadOnly",
                ["Koan:Data:Sources:Writer:Adapter"] = "json",
                ["Koan:Data:Sources:Writer:json:DirectoryPath"] = shared
            })
            .ConfigureServices(static services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
        return new MarkerHost(host, root, primary, archive, shared);
    }

    private static string OtherSpelling(string path)
    {
        if (OperatingSystem.IsWindows()) return @"\\?\" + path;
        var link = path + "-link";
        Directory.CreateSymbolicLink(link, path);
        return link;
    }

    private sealed class MarkerHost(IntegrationHost host, string root, string primary, string archive, string shared)
        : IAsyncDisposable
    {
        public IServiceProvider Services => host.Services;
        public string Primary => primary;
        public string Archive => archive;
        public string Shared => shared;

        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    public sealed class MarkedNote : Entity<MarkedNote>
    {
        public string Text { get; set; } = "";
    }
}
