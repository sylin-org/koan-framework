using System.Text;
using AwesomeAssertions;
using Koan.Classification.Crypto;
using Xunit;

namespace Koan.Classification.Tests.Crypto;

/// <summary>
/// ARCH-0098 phase 2b — the ephemeral (dev-tier) key provider against the §3a contract: per-tenant isolation,
/// decrypt-by-owning-tenant independent of the ambient tenant, rotation survival (a retired key still decrypts),
/// count-aware rotation, the host bucket (classification works without tenancy), and irreversible/idempotent
/// crypto-shred that isolates to one tenant.
/// </summary>
public sealed class EphemeralKeyProviderSpec
{
    private readonly AesGcmFieldCipher _cipher = new();
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Different_tenants_get_different_keys()
    {
        var p = new EphemeralKeyProvider();
        var a = p.GetActiveKey("tenant-a");
        var b = p.GetActiveKey("tenant-b");

        a.KeyId.Should().NotBe(b.KeyId);
        a.Key.ToArray().Should().NotEqual(b.Key.ToArray());
    }

    [Fact]
    public void The_same_tenant_gets_a_stable_active_key()
    {
        var p = new EphemeralKeyProvider();
        p.GetActiveKey("t").KeyId.Should().Be(p.GetActiveKey("t").KeyId);
    }

    [Fact]
    public void Null_and_empty_tenant_share_the_host_bucket()
    {
        var p = new EphemeralKeyProvider();
        p.GetActiveKey(null).KeyId.Should().Be(p.GetActiveKey("").KeyId);
    }

    [Fact]
    public void Encrypt_under_active_then_decrypt_via_GetForDecrypt_round_trips()
    {
        var p = new EphemeralKeyProvider();
        var plaintext = Utf8("Ada Lovelace");

        var active = p.GetActiveKey("tenant-a");
        var envelope = _cipher.Encrypt(plaintext, active);

        // Decrypt resolves the key purely by the envelope's KeyId — no ambient tenant needed.
        var key = p.GetForDecrypt(envelope.KeyId);
        _cipher.Decrypt(envelope, key).Should().Equal(plaintext);
    }

    [Fact]
    public void GetForDecrypt_resolves_independent_of_any_active_tenant()
    {
        var p = new EphemeralKeyProvider();
        var aEnvKeyId = p.GetActiveKey("tenant-a").KeyId;

        // Make tenant-b the most-recently-active; decrypt of a's key must still resolve a's key.
        p.GetActiveKey("tenant-b");
        p.GetForDecrypt(aEnvKeyId).KeyId.Should().Be(aEnvKeyId);
    }

    [Fact]
    public void A_retired_key_still_decrypts_after_rotation()
    {
        var p = new EphemeralKeyProvider(rotateAfter: 2);
        var first = p.GetActiveKey("t");                 // count 1 → first key
        var envelope = _cipher.Encrypt(Utf8("old data"), first);

        var rotated = p.GetActiveKey("t");               // count 2 → rotation; new active key
        rotated.KeyId.Should().NotBe(first.KeyId);        // rotation actually happened

        // The value written under the retired key still decrypts (the keyId envelope survives rotation).
        _cipher.Decrypt(envelope, p.GetForDecrypt(envelope.KeyId)).Should().Equal(Utf8("old data"));
    }

    [Fact]
    public void GetForDecrypt_throws_for_an_unknown_key()
    {
        var p = new EphemeralKeyProvider();
        var act = () => p.GetForDecrypt("does-not-exist");
        act.Should().Throw<KeyUnavailableException>();
    }

    [Fact]
    public async Task DestroyKey_makes_the_tenants_data_unrecoverable_crypto_shred()
    {
        var p = new EphemeralKeyProvider();
        var key = p.GetActiveKey("tenant-a");
        var envelope = _cipher.Encrypt(Utf8("sensitive"), key);

        var destroyed = await p.DestroyKeyAsync("tenant-a");
        destroyed.Should().Contain(envelope.KeyId);   // the erasure-certificate evidence

        var act = () => p.GetForDecrypt(envelope.KeyId);
        act.Should().Throw<KeyUnavailableException>();
    }

    [Fact]
    public async Task DestroyKey_isolates_to_one_tenant()
    {
        var p = new EphemeralKeyProvider();
        var a = p.GetActiveKey("tenant-a");
        var bEnvelope = _cipher.Encrypt(Utf8("b data"), p.GetActiveKey("tenant-b"));

        await p.DestroyKeyAsync("tenant-a");

        // tenant-b is untouched.
        _cipher.Decrypt(bEnvelope, p.GetForDecrypt(bEnvelope.KeyId)).Should().Equal(Utf8("b data"));
    }

    [Fact]
    public async Task DestroyKey_is_idempotent()
    {
        var p = new EphemeralKeyProvider();
        p.GetActiveKey("tenant-a");

        (await p.DestroyKeyAsync("tenant-a")).Should().NotBeEmpty();
        (await p.DestroyKeyAsync("tenant-a")).Should().BeEmpty();   // second call: nothing left, no throw
    }

    [Fact]
    public async Task A_tenant_can_be_rekeyed_after_a_shred_but_old_data_stays_gone()
    {
        var p = new EphemeralKeyProvider();
        var oldKeyId = p.GetActiveKey("tenant-a").KeyId;
        await p.DestroyKeyAsync("tenant-a");

        var fresh = p.GetActiveKey("tenant-a");        // a new lifecycle for the same tenant id
        fresh.KeyId.Should().NotBe(oldKeyId);
        var actOld = () => p.GetForDecrypt(oldKeyId);
        actOld.Should().Throw<KeyUnavailableException>();   // the shredded key never comes back
    }

    [Fact]
    public void A_nonpositive_rotation_threshold_is_rejected()
    {
        var act = () => new EphemeralKeyProvider(rotateAfter: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
