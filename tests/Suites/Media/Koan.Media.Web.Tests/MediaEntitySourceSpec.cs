using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Media;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Web.Routing;
using Koan.Storage;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Koan.Web.Context;
using Xunit;

namespace Koan.Media.Web.Tests;

/// <summary>
/// ARCH-0079 framework-home spec for <see cref="MediaEntitySource{TEntity}"/> — the generic that makes media
/// serving inherit Web-contributed Data read context. A real <c>AddKoan()</c> boot (in-memory record store + real Local
/// blob storage, no Docker) proves the moat framework-side, independent of any sample: (1) serving resolves through
/// the Entity layer so a current request predicate hides cross-scope ids; (2) a matching scope serves stored bytes;
/// (3) the <see cref="MediaDerivation"/> render cache round-trips.
/// </summary>
[Collection("media-web")]
public sealed class MediaEntitySourceSpec
{
    private readonly MediaWebHostFixture _fx;
    public MediaEntitySourceSpec(MediaWebHostFixture fx) => _fx = fx;

    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private static async Task<byte[]> ReadAll(Stream s)
    {
        await using (s)
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            return ms.ToArray();
        }
    }

    private static IDisposable ReadScope<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        var context = new WebContext(new DefaultHttpContext());
        context.Where(predicate);
        return context.EnterPending()!;
    }

    [Fact(DisplayName = "serving inherits request read context: OpenAsync resolves through the entity gate, not raw storage")]
    public async Task Serving_is_access_scoped_and_fail_closed()
    {
        var source = new MediaEntitySource<ScopedMedia>();
        var stamp = Stamp();
        var alice = "alice-" + stamp;
        var bob = "bob-" + stamp;

        // Seed two owners' media with real stored bytes (writes aren't access-gated — that stays at the surface).
        ScopedMedia mine, theirs;
        mine = await ScopedMedia.Upload(new MemoryStream(new byte[] { 1, 2, 3 }), $"{alice}.bin", "image/jpeg");
        mine.OwnerId = alice; await mine.Save();
        theirs = await ScopedMedia.Upload(new MemoryStream(new byte[] { 4, 5, 6 }), $"{bob}.bin", "image/jpeg");
        theirs.OwnerId = bob; await theirs.Save();

        // Outside a Web request there is no invented access policy; the source remains an ordinary Entity projection.
        (await source.OpenAsync(mine.Id)).Should().NotBeNull();

        // Wrong request scope → null (can't serve another owner's bytes by id — the IDOR is closed).
        using (ReadScope<ScopedMedia>(media => media.OwnerId == bob))
            (await source.OpenAsync(mine.Id)).Should().BeNull();

        // Matching request scope → serves the stored bytes.
        using (ReadScope<ScopedMedia>(media => media.OwnerId == alice))
        {
            var handle = await source.OpenAsync(mine.Id);
            handle.Should().NotBeNull();
            handle!.ContentType.Should().Be("image/jpeg");
            (await ReadAll(handle.Bytes)).Should().Equal(new byte[] { 1, 2, 3 });
        }

        (await source.OpenAsync(theirs.Id)).Should().NotBeNull();
        (await source.OpenAsync("no-such-id-" + stamp)).Should().BeNull();
    }

    [Fact(DisplayName = "derivation cache round-trips: TryStore then OpenDerivation serves the render; miss before store; idempotent")]
    public async Task Derivation_cache_round_trips()
    {
        var source = new MediaEntitySource<ScopedMedia>();
        var stamp = Stamp();
        var sourceId = "photo-" + stamp;
        var fingerprint = "fp-" + stamp;
        var rendered = new byte[] { 7, 7, 9, 9, 7 };
        var output = new MediaOutput(rendered, "image/jpeg", "jpeg", "jpeg", 1, 1, 1, "out-" + stamp);

        (await source.OpenDerivationAsync(sourceId, fingerprint)).Should().BeNull();          // miss before store

        await source.TryStoreDerivationAsync(sourceId, fingerprint, output, "gallery", "1");
        var hit = await source.OpenDerivationAsync(sourceId, fingerprint);
        hit.Should().NotBeNull();
        hit!.ContentType.Should().Be("image/jpeg");
        (await ReadAll(hit.Bytes)).Should().Equal(rendered);

        // A distinct fingerprint is a distinct cache entry.
        (await source.OpenDerivationAsync(sourceId, "other-" + stamp)).Should().BeNull();

        // Idempotent: a second store for the same (source, fingerprint) is a no-op, still serves one render.
        await source.TryStoreDerivationAsync(sourceId, fingerprint, output, "gallery", "1");
        (await source.OpenDerivationAsync(sourceId, fingerprint)).Should().NotBeNull();
    }
}

/// <summary>A media entity whose owner predicate is contributed once by the Web request edge.</summary>
[StorageBinding(Profile = "test", Container = "media")]
public sealed class ScopedMedia : MediaEntity<ScopedMedia>
{
    public string OwnerId { get; set; } = "";
}

/// <summary>
/// One real <c>AddKoan()</c> boot shared across the collection — in-memory record store + Local blob storage
/// (temp dir). A single boot is deliberate (the framework caches
/// per-process static state against the booted provider); AppHost.Current is saved/restored around it.
/// </summary>
public sealed class MediaWebHostFixture : IAsyncLifetime
{
    public IntegrationHost Host { get; private set; } = null!;
    private IServiceProvider? _prevAppHost;
    private string _storageRoot = "";

    public async ValueTask InitializeAsync()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "koan-media-web-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_storageRoot);

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            // Real Local storage so the positive + cache legs round-trip real bytes (the working test config).
            ["Koan:Storage:Providers:Local:BasePath"] = _storageRoot,
            ["Koan:Storage:DefaultProfile"] = "test",
            ["Koan:Storage:Profiles:test:Provider"] = "local",
            ["Koan:Storage:Profiles:test:Container"] = "media",
            ["Koan:Media:Recipes:configured-card:Description"] = "Configuration-bound recipe startup proof.",
            ["Koan:Media:Recipes:configured-card:Steps:0:Op"] = "resize",
            ["Koan:Media:Recipes:configured-card:Steps:0:Width"] = "640",
            ["Koan:Media:Recipes:configured-card:Steps:1:Op"] = "encodeAs",
            ["Koan:Media:Recipes:configured-card:Steps:1:Format"] = "jpeg",
        };

        Host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // One concrete MediaEntity is the complete common path; Media Web selects it automatically.
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        _prevAppHost = AppHost.Current;
        AppHost.Current = Host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _prevAppHost;
        await Host.DisposeAsync();
        try { if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}

[CollectionDefinition("media-web")]
public sealed class MediaWebCollection : ICollectionFixture<MediaWebHostFixture> { }
