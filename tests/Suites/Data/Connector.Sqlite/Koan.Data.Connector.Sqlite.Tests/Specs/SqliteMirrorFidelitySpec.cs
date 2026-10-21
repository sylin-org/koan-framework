using Koan.Core;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// Mirror fidelity on SQLite. NK-001: a bidirectional mirror whose destination wins writes the destination's row into
/// the source; it used to journal that row through lossy JSON without type names, so a family variant came back as its
/// root type and lost its own fields, permanently, in the source store. NK-019: see the filtered push below.
/// </summary>
[Collection(nameof(SqliteMirrorFidelitySpec))]
[CollectionDefinition(nameof(SqliteMirrorFidelitySpec), DisableParallelization = true)]
public sealed class SqliteMirrorFidelitySpec
{
    public class Media : Entity<Media>
    {
        public string Title { get; set; } = "";
    }

    public sealed class Anime : Media, IEntityFamilyVariant<Media, Anime, string>
    {
        public int Episodes { get; set; }
    }

    public sealed class Article : Entity<Article>
    {
        public string Title { get; set; } = "";
        public bool Published { get; set; }
    }

    /// <summary>
    /// NK-019. A filtered Push mirror tested replica rows against the authoritative side without its predicate, so
    /// an article unpublished at the source kept being served from the published replica.
    /// </summary>
    [Fact]
    public Task A_filtered_push_removes_replica_rows_that_left_the_predicate()
        => WithHost(async () =>
        {
            var article = await new Article { Title = "Launch", Published = true }.Save();
            await new Article { Title = "Still live", Published = true }.Save();
            (await Article.Mirror(a => a.Published).To(partition: "pub").Run()).CopiedCount.Should().Be(2);

            article.Published = false;
            await article.Save();
            var second = await Article.Mirror(a => a.Published).To(partition: "pub").Run();

            second.DeletedCount.Should().Be(1);
            using (EntityContext.Partition("pub"))
                (await Article.All()).Select(a => a.Title).Should().Equal("Still live");
        });

    [Fact]
    public Task Destination_wins_keeps_the_variant_type_and_fields_in_the_source()
        => WithHost(async () =>
        {
            EntityTypeCatalog.Register(typeof(Anime));
            await Data<Media, string>.UpsertMany([new Anime { Id = "frieren", Title = "Frieren", Episodes = 12 }]);
            using (EntityContext.Partition("replica"))
                await Data<Media, string>.UpsertMany([new Anime { Id = "frieren", Title = "Frieren", Episodes = 28 }]);

            var result = await Media.Mirror(MirrorMode.Bidirectional)
                .To(partition: "replica")
                .Conflict(MirrorConflict.Destination)
                .Run();

            result.Conflicts.Should().BeEmpty();
            var mirrored = await Media.Get("frieren");
            mirrored.Should().BeOfType<Anime>().Which.Episodes.Should().Be(28);
        });

    private static async Task WithHost(Func<Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-mirror-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
                .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();
            await body();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
