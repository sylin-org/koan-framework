using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using SnapVault.Models;
using SnapVault.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault step 6 — the ingest blob-key collision fix. <c>MediaEntity.Upload</c> keys the blob by the caller's
/// name, so the greenfield ingest now keys by a fresh <c>StringId</c> (+ the original extension) instead of the raw
/// filename. Before the fix a second <c>IMG_1234.jpg</c> overwrote the first's bytes, and BOTH records (distinct
/// ids, distinct events) then served the SAME image on the gallery/download surface — and a partial delete clobbered
/// a sibling's bytes. This spec proves the fix end-to-end through the real ingest service: same-named uploads land on
/// DISTINCT blobs, the display name is preserved, each blob serves its OWN bytes, and deleting one photo reclaims
/// ONLY its blob (the sibling survives) via the structural <c>AfterRemove</c> hook.
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultBlobKeySpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultBlobKeySpec(SnapVaultHostFixture fx) => _fx = fx;

    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private static async Task<byte[]> JpegAsync(int w, int h)
    {
        using var img = new Image<Rgba32>(w, h);
        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms);
        return ms.ToArray();
    }

    private static async Task<byte[]> ReadBlob(PhotoAsset photo)
    {
        await using var stream = await photo.OpenRead(CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    [Fact(DisplayName = "blob-key: two same-named uploads land on distinct blobs, each serving its own bytes; a partial delete is sibling-safe")]
    public async Task Same_named_uploads_do_not_collide()
    {
        var processing = _fx.Host.Services.GetRequiredService<PhotoProcessingService>();
        var studio = "studio-" + Stamp();
        const string sharedName = "IMG_1234.jpg";

        // Two DIFFERENT images (different dimensions ⇒ different encoded bytes) uploaded under the SAME filename.
        var bytesA = await JpegAsync(8, 6);
        var bytesB = await JpegAsync(10, 4);

        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();

            var a = await processing.ProcessUpload(ev.Id, new MemoryStream(bytesA), sharedName, "image/jpeg", null, CancellationToken.None);
            var b = await processing.ProcessUpload(ev.Id, new MemoryStream(bytesB), sharedName, "image/jpeg", null, CancellationToken.None);

            // Distinct records, distinct blob keys — the whole fix.
            a.Id.Should().NotBe(b.Id);
            a.Key.Should().NotBe(b.Key, "same-named uploads must not share a storage key");
            // The human-facing name is preserved on both despite the unique storage key.
            a.OriginalFileName.Should().Be(sharedName);
            b.OriginalFileName.Should().Be(sharedName);

            // Both blobs exist and serve their OWN bytes (no clobber): read each back and compare to its source.
            (await ReadBlob(a)).Should().Equal(bytesA);
            (await ReadBlob(b)).Should().Equal(bytesB);

            // Partial delete is sibling-safe: removing A reclaims A's blob (structural AfterRemove hook) but leaves B's.
            await a.Remove(CancellationToken.None);
            (await PhotoAsset.Head(a.Key, CancellationToken.None)).Should().BeNull("deleting a photo reclaims its own blob");
            (await PhotoAsset.Head(b.Key, CancellationToken.None)).Should().NotBeNull("a sibling's blob must survive a same-named peer's delete");
            (await ReadBlob(b)).Should().Equal(bytesB, "the surviving sibling still serves its own bytes");
        }
    }
}
