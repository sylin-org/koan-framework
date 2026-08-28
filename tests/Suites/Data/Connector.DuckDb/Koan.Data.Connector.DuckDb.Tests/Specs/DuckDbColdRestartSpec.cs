using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Testing.Integration;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

public sealed class DuckDbColdRestartSpec
{
    [Fact]
    public async Task First_root_read_after_host_restart_preserves_each_concrete_variant()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-duckdb-polymorphic-{Guid.CreateVersion7():N}.db");
        try
        {
            await using (var writer = await Boot(path))
            {
                AppHost.Current = writer.Services;
                await new PolyAnime
                {
                    Id = "anime",
                    Kind = "Anime",
                    Title = "Frieren",
                    Episodes = 28
                }.Save();
                await new PolyManga
                {
                    Id = "manga",
                    Kind = "Manga",
                    Title = "Witch Hat Atelier",
                    Volumes = 13,
                    Chapters = 80
                }.Save();
            }

            await using var reader = await Boot(path);
            AppHost.Current = reader.Services;

            (await PolyMedia.Get("anime")).Should().BeOfType<PolyAnime>()
                .Which.Episodes.Should().Be(28);
            (await PolyMedia.Get("manga")).Should().BeOfType<PolyManga>()
                .Which.Chapters.Should().Be(80);
        }
        finally
        {
            AppHost.Current = null;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Task<IntegrationHost> Boot(string path) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "duckdb",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=True"
        })
        .ConfigureServices(static services => services.AddKoan())
        .StartAsync(TestContext.Current.CancellationToken);
}
