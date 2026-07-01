using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using Xunit;

namespace S6.SnapVault.Tests;

/// <summary>
/// SnapVault step 5b — the load-bearing <c>#5 /photosets/query</c> session windowing. A real <c>AddKoan()</c> boot
/// (ARCH-0079, in-memory) proves the on-demand materialization the whole grid + lightbox nav ride: a session is a
/// stored query definition (not an id snapshot); the total count is computed once; each range re-windows. The three
/// deterministic contexts are covered — all-photos (sorted), favorites (filtered), collection (manual PhotoIds order
/// preserved, no sort). Reads run under an operator <c>Subject</c> (the studio sees all its own tenant's photos).
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultPhotoSetSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultPhotoSetSpec(SnapVaultHostFixture fx) => _fx = fx;

    private T Svc<T>() where T : notnull => _fx.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    [Fact(DisplayName = "windowing: all-photos (sorted) · favorites (filtered) · collection (manual order) materialize on demand")]
    public async Task Windowing_across_contexts()
    {
        var studio = "studio-" + Stamp();
        var svc = Svc<PhotoSetService>();
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        PhotoAsset p0, p1, p2, p3, p4;
        Collection col;
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var ev = new Event { Name = "Shoot" }; await ev.Save();
            // Five photos with strictly increasing capture dates; favorite the middle two.
            p0 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "0.jpg", CapturedAt = baseDate.AddDays(0) }; await p0.Save();
            p1 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "1.jpg", CapturedAt = baseDate.AddDays(1), IsFavorite = true }; await p1.Save();
            p2 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "2.jpg", CapturedAt = baseDate.AddDays(2), IsFavorite = true }; await p2.Save();
            p3 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "3.jpg", CapturedAt = baseDate.AddDays(3) }; await p3.Save();
            p4 = new PhotoAsset { EventId = ev.Id, OriginalFileName = "4.jpg", CapturedAt = baseDate.AddDays(4) }; await p4.Save();
            // A collection in a deliberate NON-chronological manual order.
            col = new Collection { Name = "Picks", PhotoIds = { p3.Id, p0.Id, p4.Id } }; await col.Save();
        }

        using (Tenant.Use(studio))
        using (Subject.System())
        {
            // all-photos: count once, window on demand, newest-first (capturedAt desc default).
            var all = await svc.CreateSession(new PhotoSetDefinition { Context = "all-photos", SortBy = "capturedAt", SortOrder = "desc" });
            all.TotalCount.Should().Be(5);
            var firstTwo = await svc.ExecuteQuery(all, 0, 2);
            firstTwo.Select(p => p.Id).Should().Equal(p4.Id, p3.Id);          // newest two
            var nextTwo = await svc.ExecuteQuery(all, 2, 2);
            nextTwo.Select(p => p.Id).Should().Equal(p2.Id, p1.Id);           // window advances, still sorted

            // favorites: filtered to the two favorited photos.
            var favs = await svc.CreateSession(new PhotoSetDefinition { Context = "favorites" });
            favs.TotalCount.Should().Be(2);
            (await svc.ExecuteQuery(favs, 0, 10)).Should().OnlyContain(p => p.IsFavorite);

            // collection: total = membership; the manual PhotoIds order is preserved (NOT re-sorted).
            var collection = await svc.CreateSession(new PhotoSetDefinition { Context = "collection", CollectionId = col.Id });
            collection.TotalCount.Should().Be(3);
            (await svc.ExecuteQuery(collection, 0, 10)).Select(p => p.Id).Should().Equal(p3.Id, p0.Id, p4.Id);
        }
    }
}
