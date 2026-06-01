using Koan.Media.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Media.Core.Tests.Specs.Caching;

/// <summary>
/// MEDIA-0007 §b: when the pipeline persists a derivation it stamps the
/// lineage fields on the stored row — <c>SourceMediaId</c>,
/// <c>DerivationKey</c>, <c>RelationshipType</c>, and
/// <c>Tags["recipe-version"]</c>. These let queries find "all derivations
/// of source X" and let the sweep prune by recipe version.
/// </summary>
public sealed class LineageStampingSpec
{
    [Fact]
    public async Task NamedRecipe_writes_full_lineage_onto_the_stored_row()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync(
            configureServices: services =>
            {
                services.PostConfigure<RecipesOptions>(opts =>
                {
                    opts.Recipes["package-card"] = new ConfiguredRecipe
                    {
                        Description = "test package-card",
                        Steps = new List<ConfiguredStep>
                        {
                            new() { Op = "resize", Width = 200 },
                            new() { Op = "encodeAs", Format = "png" },
                        },
                    };
                });
            });
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var response = await server.Client.GetAsync("/media/photo/package-card");
        response.IsSuccessStatusCode.Should().BeTrue();

        var derivation = server.Source.AllDerivations().Single().Value;

        derivation.SourceMediaId.Should().Be("photo",
            "SourceMediaId references the original — the foreign-key for the GC sweep");
        derivation.DerivationKey.Should().NotBeNullOrEmpty(
            "DerivationKey carries the recipe fingerprint for predicate queries");
        derivation.RelationshipType.Should().Be("package-card",
            "named invocations stamp the recipe name (not 'derivation')");
        derivation.RecipeVersion.Should().Be("1",
            "Tags['recipe-version'] carries the recipe Version field for selective sweeps");
    }

    [Fact]
    public async Task AdHoc_render_writes_RelationshipType_derivation()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var response = await server.Client.GetAsync("/media/photo?w=200");
        response.IsSuccessStatusCode.Should().BeTrue();

        var derivation = server.Source.AllDerivations().Single().Value;
        derivation.RelationshipType.Should().Be("derivation",
            "ad-hoc renders default to the generic 'derivation' relationship");
        derivation.SourceMediaId.Should().Be("photo");
    }

    [Fact]
    public async Task DerivationKey_matches_recipe_fingerprint_so_subsequent_lookups_collide()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        await server.Client.GetAsync("/media/photo/png?w=200");

        var entry = server.Source.AllDerivations().Single();
        // The derivation row's storage key is "{sourceId}:{fingerprint}";
        // the stamped DerivationKey is just the fingerprint half.
        entry.Key.Should().Be($"photo:{entry.Value.DerivationKey}",
            "storage key composes (sourceId, fingerprint), and the stamped DerivationKey is the fingerprint half");
    }

    [Fact]
    public async Task Query_for_derivations_of_a_source_returns_only_its_descendants()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("a", Fixtures.WideJpeg(width: 400, height: 300));
        await server.Source.AddSourceAsync("b", Fixtures.WideJpeg(width: 400, height: 300));

        await server.Client.GetAsync("/media/a/png?w=100");
        await server.Client.GetAsync("/media/a/png?w=200");
        await server.Client.GetAsync("/media/b/png?w=100");

        // Equivalent to TMedia.Query(m => m.SourceMediaId == "a") — the
        // contract the GC sweep and operational tooling rely on.
        var derivationsOfA = server.Source.AllDerivations()
            .Where(kv => kv.Value.SourceMediaId == "a")
            .ToList();
        var derivationsOfB = server.Source.AllDerivations()
            .Where(kv => kv.Value.SourceMediaId == "b")
            .ToList();

        derivationsOfA.Should().HaveCount(2, "two distinct recipe runs against source A");
        derivationsOfB.Should().HaveCount(1, "one recipe run against source B");
        derivationsOfA.Select(kv => kv.Value.DerivationKey).Should().OnlyHaveUniqueItems(
            "the two derivations of A address different recipes and thus different rows");
    }

    [Fact]
    public async Task SameRecipe_against_same_source_overwrites_to_one_row()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        await server.Client.GetAsync("/media/photo/png?w=100");
        // Subsequent identical request is served from the stored row;
        // no second write happens.
        await server.Client.GetAsync("/media/photo/png?w=100");

        server.Source.AllDerivations().Should().HaveCount(1,
            "(sourceId, fingerprint) addresses one storage row — repeated renders share it");
        server.Source.DerivationWriteCount.Should().Be(1,
            "only the cold render wrote through; the warm hit skipped the pipeline");
    }
}
