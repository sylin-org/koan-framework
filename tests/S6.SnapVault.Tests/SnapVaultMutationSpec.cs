using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Media.Web.Routing;
using Koan.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using S6.SnapVault.Configuration;
using S6.SnapVault.Controllers;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using Xunit;

namespace S6.SnapVault.Tests;

/// <summary>
/// SnapVault step 5c — the studio mutation surface. A real <c>AddKoan()</c> boot (ARCH-0079, in-memory) proves the
/// two things that carry the most risk: (1) the §9.7 structural delete-cleanup — a photo removal evicts its cached
/// recipe renders (<see cref="MediaDerivation"/>) and prunes its id from every collection, on the data layer so it
/// fires on any delete path; and (2) the controller guards that the SPA depends on — rate clamping, INV-1 lowercase
/// fact keys, the collection cap error carrying the word "limit", and the sealed raw EntityController write/delete
/// verbs (405). Controllers are exercised directly (the guards touch no HttpContext); entity work runs under an
/// operator subject (the studio sees its own tenant).
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultMutationSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultMutationSpec(SnapVaultHostFixture fx) => _fx = fx;

    private T Svc<T>() where T : notnull => _fx.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private PhotosController Photos() => new(Svc<PhotoSetService>(), Svc<IPhotoProcessingService>());

    [Fact(DisplayName = "delete cleanup: removing a photo evicts its cached renders and prunes it from collections")]
    public async Task Delete_evicts_renders_and_prunes_collections()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            var p0 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "0.jpg" }; await p0.Save();
            var p1 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "1.jpg" }; await p1.Save();
            var p2 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "2.jpg" }; await p2.Save();

            // A cached recipe render whose source is p0 (record only — the blob delete is best-effort in the hook).
            var render = new MediaDerivation
            {
                Id = MediaDerivation.KeyFor(p0.Id, "fingerprint-1"),
                Key = "render-blob-" + Stamp(),
                SourceMediaId = p0.Id,
                DerivationKey = "fingerprint-1",
            };
            await render.Save();

            // A collection referencing all three in a deliberate order.
            var col = new Collection { Name = "Picks", PhotoIds = { p2.Id, p0.Id, p1.Id } }; await col.Save();

            // Act — the ONE delete. The AfterRemove hook (PhotoAssetCleanup) does the rest.
            await p0.Remove();

            // 1. p0's cached render is gone (targeted eviction by SourceMediaId).
            (await MediaDerivation.Query(d => d.SourceMediaId == p0.Id)).Should().BeEmpty();

            // 2. p0 is pruned from the collection; the surviving members keep their relative order.
            var reloaded = await Collection.Get(col.Id, CancellationToken.None);
            reloaded!.PhotoIds.Should().Equal(p2.Id, p1.Id);
        }
    }

    [Fact(DisplayName = "rate: out-of-range is rejected; a valid rating persists")]
    public async Task Rate_validates_and_persists()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            var photo = new PhotoAsset { EventId = ev.Id, OriginalFileName = "r.jpg" }; await photo.Save();
            var ctrl = Photos();

            (await ctrl.Rate(photo.Id, new RateRequest { Rating = 6 })).Should().BeOfType<BadRequestObjectResult>();
            (await ctrl.Rate(photo.Id, new RateRequest { Rating = -1 })).Should().BeOfType<BadRequestObjectResult>();

            (await ctrl.Rate(photo.Id, new RateRequest { Rating = 4 })).Should().BeOfType<OkObjectResult>();
            (await PhotoAsset.Get(photo.Id, CancellationToken.None))!.Rating.Should().Be(4);
        }
    }

    [Fact(DisplayName = "fact lock: the fact key is normalized to lowercase (INV-1) regardless of request casing")]
    public async Task Fact_lock_normalizes_key_to_lowercase()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            var photo = new PhotoAsset
            {
                EventId = ev.Id,
                OriginalFileName = "f.jpg",
                AiAnalysis = new AiAnalysis { Summary = "s", Facts = { ["mood"] = "calm" } },
            };
            await photo.Save();

            // Request the lock with UPPERCASE — the stored fact key is lowercase, so the controller must normalize.
            (await Photos().ToggleFactLock(photo.Id, "MOOD")).Should().BeOfType<OkObjectResult>();

            var reloaded = await PhotoAsset.Get(photo.Id, CancellationToken.None);
            reloaded!.AiAnalysis!.LockedFactKeys.Should().Contain("mood").And.NotContain("MOOD");
        }
    }

    [Fact(DisplayName = "collection cap: adding beyond MaxPhotosPerCollection is refused with a 'limit' message")]
    public async Task Collection_cap_is_enforced_with_limit_message()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            var p0 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "0.jpg" }; await p0.Save();
            var p1 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "1.jpg" }; await p1.Save();
            var col = new Collection { Name = "Tiny" }; await col.Save();

            // A cap of 1 with two new (existing) photos → breach.
            var ctrl = new CollectionsController(Options.Create(new CollectionOptions { MaxPhotosPerCollection = 1 }));
            var result = await ctrl.AddPhotos(col.Id, new CollectionPhotosRequest { PhotoIds = { p0.Id, p1.Id } });

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            var error = bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value)!.ToString();
            error.Should().Contain("limit");

            // Nothing was added (the whole batch is refused, not partially applied).
            (await Collection.Get(col.Id, CancellationToken.None))!.PhotoIds.Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "seal: the raw EntityController write/delete verbs return 405 (photos enter via /upload only)")]
    public async Task Raw_write_verbs_are_sealed()
    {
        var ctrl = Photos();

        static void Assert405(IActionResult r) =>
            r.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(405);

        Assert405(await ctrl.Upsert(new PhotoAsset(), CancellationToken.None));
        Assert405(await ctrl.UpsertMany(new[] { new PhotoAsset() }, CancellationToken.None));
        Assert405(await ctrl.Patch("any-id", CancellationToken.None));
        Assert405(await ctrl.Delete("any-id", CancellationToken.None));
        Assert405(await ctrl.DeleteMany(new[] { "any-id" }, CancellationToken.None));
        Assert405(await ctrl.DeleteByQuery("q", CancellationToken.None));
        Assert405(await ctrl.DeleteAll(CancellationToken.None));
    }
}
