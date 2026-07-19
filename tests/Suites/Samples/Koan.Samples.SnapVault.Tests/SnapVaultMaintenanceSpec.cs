using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Media.Web.Routing;
using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SnapVault.Controllers;
using SnapVault.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault step 5d — the trimmed maintenance surface. A real <c>AddKoan()</c> boot proves the two operations
/// that survive the greenfield: <b>stats</b> (photo count + storage sizes summed from the entities' own
/// <c>Size</c>, tenant-scoped, no filesystem walk) and the destructive <b>wipe</b> (clears every entity type in the
/// tenant — photos + their blobs, events, collections, jobs, sessions, render cache — skipping the deleted
/// derivative types). Everything runs under a unique per-test studio so the wipe touches only its own tenant.
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultMaintenanceSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultMaintenanceSpec(SnapVaultHostFixture fx) => _fx = fx;

    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private static async Task<byte[]> TinyJpegAsync()
    {
        using var img = new Image<Rgba32>(8, 6);
        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms);
        return ms.ToArray();
    }

    private static MaintenanceController Controller(Stream? responseBody = null)
    {
        var ctrl = new MaintenanceController(NullLogger<MaintenanceController>.Instance);
        var http = new DefaultHttpContext();
        if (responseBody is not null) http.Response.Body = responseBody;
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    [Fact(DisplayName = "stats: photo count + originals(cold)/render-cache(warm) sizes from entity Size, hot tier gone")]
    public async Task Stats_report_counts_and_sizes()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            // 3 originals of 2 GB each + 2 render-cache rows of 512 MB each (Size is the stored byte count; the
            // stat math needs no real blob).
            for (var i = 0; i < 3; i++)
                await new PhotoAsset { EventId = ev.Id, OriginalFileName = $"{i}.jpg", Size = 2L * 1024 * 1024 * 1024 }.Save();
            for (var i = 0; i < 2; i++)
                await new MediaDerivation { Id = $"d{i}-{Stamp()}", SourceMediaId = "src", Size = 512L * 1024 * 1024 }.Save();

            var result = await Controller().GetStats();
            var stats = (result.Result as OkObjectResult)!.Value.Should().BeOfType<StorageStats>().Subject;

            stats.PhotoCount.Should().Be(3);
            stats.ColdTierGB.Should().Be(6.0);    // 3 × 2 GB originals
            stats.WarmTierGB.Should().Be(1.0);    // 2 × 512 MB render cache
            stats.HotTierGB.Should().Be(0.0);     // greenfield has no separate hot tier
            stats.TotalGB.Should().Be(7.0);
            stats.CacheEntries.Should().Be(2);
            stats.CacheSizeMB.Should().Be(1024);  // 2 × 512 MB
        }
    }

    [Fact(DisplayName = "wipe: clears every entity type in the tenant and streams to 100%")]
    public async Task Wipe_clears_the_repository()
    {
        var studio = "studio-" + Stamp();
        using (Tenant.Use(studio))
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            // A real uploaded photo (blob + record) so the wipe's blob-delete is actually exercised, not just the
            // record removal.
            var uploaded = await PhotoAsset.Upload(new MemoryStream(await TinyJpegAsync()), "wipe-blob.jpg", "image/jpeg");
            uploaded.EventId = ev.Id; await uploaded.Save();
            var blobKey = uploaded.Key;
            (await PhotoAsset.Head(blobKey, CancellationToken.None)).Should().NotBeNull("the upload wrote a blob");

            var p1 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "1.jpg" }; await p1.Save();
            await new Collection { Name = "Picks", PhotoIds = { uploaded.Id, p1.Id } }.Save();
            await new MediaDerivation { Id = MediaDerivation.KeyFor(uploaded.Id, "fp"), SourceMediaId = uploaded.Id, Key = "blob-" + Stamp() }.Save();
            await new PhotoSetSession { Context = "all-photos" }.Save();
            await new PhotoProcessingJob { PhotoId = uploaded.Id }.Save();
            // Guest-lifecycle rows for this studio (5e wipe-fold) — HostScoped, keyed by StudioTenantId.
            await new GalleryGrant { Id = GalleryGrant.KeyFor("g-" + Stamp(), ev.Id), IdentityId = "g", EventId = ev.Id, StudioTenantId = studio }.Save();
            await new ProofSelection { Id = ProofSelection.KeyFor("g", uploaded.Id), GuestIdentityId = "g", EventId = ev.Id, PhotoId = uploaded.Id, StudioTenantId = studio }.Save();

            var body = new MemoryStream();
            await Controller(body).WipeRepository();

            // Every entity RECORD type is gone…
            (await PhotoAsset.All()).Should().BeEmpty();
            (await Event.All()).Should().BeEmpty();
            (await Collection.All()).Should().BeEmpty();
            (await PhotoProcessingJob.All()).Should().BeEmpty();
            (await PhotoSetSession.All()).Should().BeEmpty();
            (await MediaDerivation.All()).Should().BeEmpty();
            // …including the studio's guest-lifecycle rows (5e).
            (await GalleryGrant.Query(g => g.StudioTenantId == studio)).Should().BeEmpty();
            (await ProofSelection.Query(p => p.StudioTenantId == studio)).Should().BeEmpty();

            // …and the original BLOB is gone too — proves the AfterRemove hook reclaims the blob on Remove (the wipe
            // no longer calls photo.Delete directly; blob reclamation is one structural home).
            (await PhotoAsset.Head(blobKey, CancellationToken.None)).Should().BeNull("the wipe deleted the blob");

            Encoding.UTF8.GetString(body.ToArray()).Should().Contain("wiped successfully");
        }
    }
}
