using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Core;
using Koan.Testing.Integration;

namespace Koan.Tests.Data.Core.Specs.Entity;

/// <summary>
/// NK-015: a variant view shares its root's identity space, so a delete through it must prove membership first. Reads
/// already refuse another variant's row; deletes through the view now agree and leave it in place.
/// </summary>
public sealed class EntityVariantMembershipSpec
{
    [Fact]
    public async Task A_variant_view_deletes_only_rows_of_its_own_variant()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan()).StartAsync(TestContext.Current.CancellationToken);
        using var route = EntityContext.With(adapter: "inmemory", partition: Guid.NewGuid().ToString("N"));
        var manga = await new VariantMembershipManga { Kind = "manga", Volumes = 3 }.Save();
        var anime = await new GeneratedFamilyAnime { Kind = "anime", Episodes = 12 }.Save();

        (await Data<GeneratedFamilyAnime, string>.Delete(manga.Id)).Should().BeFalse("the row belongs to another variant");
        (await Data<GeneratedFamilyAnime, string>.DeleteMany([manga.Id, anime.Id])).Should().Be(1);
        (await GeneratedFamilyMedia.Get(anime.Id)).Should().BeNull();

        var second = await new GeneratedFamilyAnime { Kind = "anime", Episodes = 24 }.Save();
        var batch = await Data<GeneratedFamilyAnime, string>.Batch().Delete(manga.Id).Delete(second.Id).Save();
        batch.Deleted.Should().Be(1);
        if (batch.HasCompleteItemOutcomes)
            batch.Items.Select(item => item.Outcome).Should().Equal(BatchItemOutcome.Missing, BatchItemOutcome.Applied);

        (await GeneratedFamilyMedia.Get(manga.Id)).Should().NotBeNull("a delete through the Anime view never reaches a Manga");
        (await GeneratedFamilyMedia.Get(second.Id)).Should().BeNull();
    }
}

public sealed class VariantMembershipManga : GeneratedFamilyMedia<VariantMembershipManga>
{
    public int? Volumes { get; set; }
}
