using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Server.Keys;
using Koan.Web.Auth.Server.Options;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 D1 — the persisted, encrypted-at-rest, rotating ES256 key store, exercised over a real in-memory
/// data adapter (ARCH-0079). Proves: keys persist + survive a "restart" (load, not regenerate); rotation mints a
/// new active key while the previous key keeps validating (JWKS overlap); and a fully-elapsed key is purged.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class PersistedIssuerKeyStoreSpec : IClassFixture<PersistedKeyFixture>
{
    public PersistedIssuerKeyStoreSpec(PersistedKeyFixture _) { }

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PersistedIssuerKeyStore NewStore(IDataProtectionProvider dp, TimeProvider time, AuthServerOptions? opts = null)
        => new(dp, Microsoft.Extensions.Options.Options.Create(opts ?? new AuthServerOptions()), time, NullLogger<PersistedIssuerKeyStore>.Instance);

    private static async Task ClearKeys()
    {
        foreach (var r in await IssuerSigningKeyRecord.All(Ct))
            await r.Remove(Ct);
    }

    [Fact]
    public async Task Generates_and_persists_an_active_key_encrypted_at_rest()
    {
        await ClearKeys();
        var dp = new EphemeralDataProtectionProvider();
        var store = NewStore(dp, new MutableTimeProvider(T0));

        await store.InitializeAsync(Ct);
        var ring = store.GetKeyRing();

        ring.Active.KeyId.Should().NotBeNullOrEmpty();

        var records = await IssuerSigningKeyRecord.All(Ct);
        records.Should().ContainSingle();
        var record = records[0];
        record.IsActive.Should().BeTrue();
        record.ProtectedPkcs8.Should().NotBeNullOrEmpty();
        // Encrypted-at-rest: the stored blob is the protected ciphertext, NOT a readable PKCS#8 PEM.
        record.ProtectedPkcs8.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task Survives_restart_loads_the_persisted_key_instead_of_regenerating()
    {
        await ClearKeys();
        var dp = new EphemeralDataProtectionProvider(); // same provider instance = same at-rest protection key

        var first = NewStore(dp, new MutableTimeProvider(T0));
        await first.InitializeAsync(Ct);
        var kid1 = first.GetKeyRing().Active.KeyId;

        // a fresh store instance (= a process restart) over the same data + protector
        var second = NewStore(dp, new MutableTimeProvider(T0));
        await second.InitializeAsync(Ct);
        var kid2 = second.GetKeyRing().Active.KeyId;

        kid2.Should().Be(kid1);
        // ...and it did NOT mint a duplicate: the persisted key was LOADED, not regenerated.
        (await IssuerSigningKeyRecord.All(Ct)).Should().ContainSingle().Which.Id.Should().Be(kid1!);
    }

    [Fact]
    public async Task Rotation_mints_a_new_active_keeps_the_old_published_then_purges_it()
    {
        await ClearKeys();
        var dp = new EphemeralDataProtectionProvider();

        var time = new MutableTimeProvider(T0);
        var opts = new AuthServerOptions { KeyRotationInterval = TimeSpan.FromDays(1), KeyOverlap = TimeSpan.FromHours(1) };
        var store = NewStore(dp, time, opts);

        await store.InitializeAsync(Ct);
        var kidOriginal = store.GetKeyRing().Active.KeyId;

        // not yet due → no rotation
        await store.RotateIfDueAsync(Ct);
        store.GetKeyRing().Active.KeyId.Should().Be(kidOriginal);

        // past the interval → rotate: a NEW active key, the old one still in the ring (JWKS overlap)
        time.Advance(TimeSpan.FromDays(2));
        await store.RotateIfDueAsync(Ct);
        var ringAfter = store.GetKeyRing();
        ringAfter.Active.KeyId.Should().NotBe(kidOriginal);
        ringAfter.All.Select(k => k.KeyId).Should().Contain(kidOriginal, "the retiring key stays published during overlap");

        // past the overlap → the old key is purged from the ring and the store
        time.Advance(TimeSpan.FromHours(2));
        await store.RotateIfDueAsync(Ct);
        store.GetKeyRing().All.Select(k => k.KeyId).Should().NotContain(kidOriginal);
        (await IssuerSigningKeyRecord.All(Ct)).Select(r => r.Id).Should().NotContain(kidOriginal!);
    }
}
