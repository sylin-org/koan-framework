using Koan.Core;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-001. A bidirectional mirror whose destination wins writes the destination's row into the source. It used to
/// journal that row through lossy JSON without type names, so a family variant came back as its root type and lost
/// its own fields, permanently, in the source store.
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

    [Fact]
    public async Task Destination_wins_keeps_the_variant_type_and_fields_in_the_source()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-mirror-{Guid.CreateVersion7():n}.db");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                             .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
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
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
