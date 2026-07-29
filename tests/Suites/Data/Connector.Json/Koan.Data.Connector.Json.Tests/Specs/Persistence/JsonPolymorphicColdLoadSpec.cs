using Koan.Core.Semantics.Segmentation;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json.Tests.Specs.Persistence;

public sealed class JsonPolymorphicColdLoadSpec
{
    [Fact]
    public async Task First_typed_get_classifies_each_row_from_storage_during_eager_file_hydration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-polymorphic-{Guid.CreateVersion7():N}");

        try
        {
            var anime = new PolyAnime
            {
                Id = "anime",
                Kind = "Anime",
                Title = "Frieren",
                Episodes = 28
            };
            var manga = new PolyManga
            {
                Id = "manga",
                Kind = "Manga",
                Title = "Witch Hat Atelier",
                Volumes = 13,
                Chapters = 80
            };
            var writer = Repository(root);
            await writer.Upsert(anime);
            await writer.Upsert(manga);

            var cold = Repository(root);
            using (EntityMaterializationScope.Enter(typeof(PolyMedia), typeof(PolyAnime)))
            {
                var loaded = await cold.Get(anime.Id);
                loaded.Should().BeOfType<PolyAnime>()
                    .Which.Episodes.Should().Be(28);
            }

            (await cold.Get(manga.Id)).Should().BeOfType<PolyManga>()
                .Which.Chapters.Should().Be(80);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static JsonRepository<PolyMedia, string> Repository(string root)
        => new(
            new JsonRoute("Test", root, Koan.Data.Abstractions.Sources.StorageLifecycle.Managed,
                Koan.Data.Abstractions.Sources.DataSourceAccess.ReadWrite),
            new DataSegmentationPlan(SegmentationPlan.Empty),
            new JsonAdapterFactory(),
            EmptyServiceProvider.Instance);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        internal static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
