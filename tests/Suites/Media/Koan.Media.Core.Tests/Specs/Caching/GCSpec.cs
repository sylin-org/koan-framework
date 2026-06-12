using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Caching;

/// <summary>
/// MEDIA-0007 §d: a scheduled sweep reclaims derivations whose source has
/// been deleted, replacing the never-evicted disk cache. The sweep walks
/// derivation rows, probes each source via the same source surface, and
/// deletes only those whose source is gone.
/// </summary>
public sealed class GCSpec
{
    [Fact]
    public async Task Sweep_deletes_derivations_whose_source_was_deleted()
    {
        var source = new StorageBackedMediaSource();
        await SeedSourceAsync(source, "a");
        await SeedSourceAsync(source, "b");
        await SeedSourceAsync(source, "c");

        await SeedDerivationAsync(source, "a", recipe: MediaRecipe.New().Resize(width: 100).Build());
        await SeedDerivationAsync(source, "b", recipe: MediaRecipe.New().Resize(width: 100).Build());
        await SeedDerivationAsync(source, "c", recipe: MediaRecipe.New().Resize(width: 100).Build());

        // Delete source "b" — its derivation is now an orphan.
        source.DeleteSource("b");

        var result = await source.SweepOrphanedDerivationsAsync();

        result.Examined.Should().Be(3, "all three derivation rows were visited");
        result.Deleted.Should().Be(1, "only the orphan was reclaimed");
        source.DerivationCount.Should().Be(2, "live derivations (A1, C1) remain");

        var surviving = source.AllDerivations().Select(kv => kv.Value.SourceMediaId).ToHashSet();
        surviving.Should().BeEquivalentTo(new[] { "a", "c" },
            "derivations of live sources are preserved");
    }

    [Fact]
    public async Task Sweep_leaves_all_rows_intact_when_every_source_is_live()
    {
        var source = new StorageBackedMediaSource();
        await SeedSourceAsync(source, "alpha");
        await SeedSourceAsync(source, "beta");
        await SeedDerivationAsync(source, "alpha", MediaRecipe.New().Resize(width: 50).Build());
        await SeedDerivationAsync(source, "beta", MediaRecipe.New().Resize(width: 50).Build());
        await SeedDerivationAsync(source, "alpha", MediaRecipe.New().Resize(width: 100).Build());

        var result = await source.SweepOrphanedDerivationsAsync();

        result.Examined.Should().Be(3);
        result.Deleted.Should().Be(0, "no orphans means no deletes");
        source.DerivationCount.Should().Be(3);
    }

    [Fact]
    public async Task Sweep_is_idempotent_across_consecutive_runs()
    {
        var source = new StorageBackedMediaSource();
        await SeedSourceAsync(source, "live");
        await SeedSourceAsync(source, "doomed");
        await SeedDerivationAsync(source, "live", MediaRecipe.New().Resize(width: 100).Build());
        await SeedDerivationAsync(source, "doomed", MediaRecipe.New().Resize(width: 100).Build());

        source.DeleteSource("doomed");

        var first = await source.SweepOrphanedDerivationsAsync();
        first.Examined.Should().Be(2);
        first.Deleted.Should().Be(1);
        var afterFirst = source.DerivationCount;

        var second = await source.SweepOrphanedDerivationsAsync();

        second.Examined.Should().Be(1, "only the surviving row is visited the second pass");
        second.Deleted.Should().Be(0, "idempotent: nothing left to delete");
        source.DerivationCount.Should().Be(afterFirst, "running twice does not corrupt the live set");
    }

    [Fact]
    public async Task Sweep_returns_empty_result_when_there_are_no_derivations()
    {
        var source = new StorageBackedMediaSource();
        await SeedSourceAsync(source, "lonely");

        var result = await source.SweepOrphanedDerivationsAsync();

        result.Should().Be(Web.Routing.MediaDerivationSweepResult.Empty,
            "an empty derivation set yields the canonical Empty result");
        source.DerivationCount.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_does_not_touch_derivations_for_a_source_recreated_after_delete()
    {
        // Defends the race: source is deleted, sweep runs and reclaims, then
        // the source is re-uploaded under the same id. A subsequent sweep
        // must not touch any new derivation written against the re-created
        // source.
        var source = new StorageBackedMediaSource();
        await SeedSourceAsync(source, "phoenix");
        await SeedDerivationAsync(source, "phoenix", MediaRecipe.New().Resize(width: 100).Build());

        source.DeleteSource("phoenix");
        var pass1 = await source.SweepOrphanedDerivationsAsync();
        pass1.Deleted.Should().Be(1);

        // Source comes back; a fresh derivation is persisted against it.
        await SeedSourceAsync(source, "phoenix");
        await SeedDerivationAsync(source, "phoenix", MediaRecipe.New().Resize(width: 200).Build());

        var pass2 = await source.SweepOrphanedDerivationsAsync();
        pass2.Deleted.Should().Be(0, "the new derivation has a live source");
        source.DerivationCount.Should().Be(1);
    }

    // ---------- helpers ----------

    private static async Task SeedSourceAsync(StorageBackedMediaSource source, string id)
    {
        await source.AddSourceAsync(id, Fixtures.WideJpeg(width: 200, height: 200));
    }

    private static Task SeedDerivationAsync(StorageBackedMediaSource source, string sourceId, MediaRecipe recipe)
    {
        // Stand in for a freshly rendered MediaOutput. The bytes are opaque
        // to the sweep — only the lineage fields matter.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var output = new MediaOutput(
            Bytes: bytes,
            ContentType: "image/png",
            Format: "png",
            SourceFormat: "jpeg",
            Width: 100,
            Height: 75,
            FrameCount: 1,
            Fingerprint: "stub-fingerprint");
        return source.TryStoreDerivationAsync(
            sourceId, recipe.Fingerprint(), output,
            recipeName: recipe.Name, recipeVersion: recipe.Version.ToString());
    }
}
