using AwesomeAssertions;
using Koan.Classification;
using Koan.Classification.Crypto;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Pipeline;
using Xunit;

namespace Koan.Classification.Tests;

/// <summary>
/// ARCH-0098 phase 3 — the classification field transform in isolation (no data store): encrypt-on-write /
/// decrypt-on-read of <c>[Classified]</c> string properties, the legacy-plaintext and null tolerances, write
/// idempotency, the crypto-shred tombstone, the string-only constraint, and decrypt-by-owning-key independent of
/// the ambient tenant.
/// </summary>
public sealed class ClassificationFieldTransformSpec
{
    private sealed class Patient
    {
        public string Id { get; set; } = "";
        [Pii] public string Name { get; set; } = "";
        [Phi] public string? Diagnosis { get; set; }
        public string Plain { get; set; } = "";
    }

    private sealed class BadEntity
    {
        public string Id { get; set; } = "";
        [Pii] public int Ssn { get; set; }   // non-string classified property — unsupported
    }

    private sealed class FixedTenant : IClassificationTenantAccessor
    {
        public string? CurrentTenantId { get; set; }
    }

    private static ClassificationFieldTransform Transform(
        IClassificationTenantAccessor? tenant = null, IKeyProvider? keys = null)
        => new(new AesGcmFieldCipher(), keys ?? new EphemeralKeyProvider(),
               tenant ?? new NullClassificationTenantAccessor(), new ClassifiedPropertyBag(typeof(Patient)));

    [Fact]
    public void Encrypts_classified_fields_and_leaves_plain_ones()
    {
        var t = Transform();
        var p = new Patient { Name = "Ada", Diagnosis = "influenza", Plain = "ward-3" };

        t.ApplyOnWrite(p);

        FieldCipherEnvelope.TryParse(p.Name, out _).Should().BeTrue();        // now ciphertext at rest
        FieldCipherEnvelope.TryParse(p.Diagnosis, out _).Should().BeTrue();
        p.Plain.Should().Be("ward-3");                                        // unclassified — untouched
        p.Name.Should().NotBe("Ada");
    }

    [Fact]
    public void Round_trips_write_then_read()
    {
        var t = Transform();
        var p = new Patient { Name = "Ada", Diagnosis = "influenza" };

        t.ApplyOnWrite(p);
        t.ApplyOnRead(p);

        p.Name.Should().Be("Ada");
        p.Diagnosis.Should().Be("influenza");
    }

    [Fact]
    public void Leaves_null_values_untouched()
    {
        var t = Transform();
        var p = new Patient { Name = "Ada", Diagnosis = null };

        t.ApplyOnWrite(p);
        p.Diagnosis.Should().BeNull();   // null is not encrypted

        t.ApplyOnRead(p);
        p.Diagnosis.Should().BeNull();
    }

    [Fact]
    public void Leaves_legacy_plaintext_unchanged_on_read()
    {
        // A value written before classification was enabled is plaintext, not an envelope — the reverse must not
        // try to decrypt it (it would fail) and must leave it as-is.
        var t = Transform();
        var p = new Patient { Name = "legacy plaintext name" };

        t.ApplyOnRead(p);

        p.Name.Should().Be("legacy plaintext name");
    }

    [Fact]
    public void Write_is_idempotent_does_not_double_encrypt()
    {
        var t = Transform();
        var p = new Patient { Name = "Ada" };

        t.ApplyOnWrite(p);
        var once = p.Name;
        t.ApplyOnWrite(p);   // the value is already an envelope → skipped

        p.Name.Should().Be(once);
        t.ApplyOnRead(p);
        p.Name.Should().Be("Ada");   // and it still decrypts to the original (not double-wrapped)
    }

    [Fact]
    public async Task A_crypto_shredded_value_reads_as_a_null_tombstone()
    {
        var keys = new EphemeralKeyProvider();
        var tenant = new FixedTenant { CurrentTenantId = "tenant-a" };
        var t = Transform(tenant, keys);
        var p = new Patient { Name = "to be forgotten" };

        t.ApplyOnWrite(p);
        await keys.DestroyKeyAsync("tenant-a");   // the erasure certificate

        t.ApplyOnRead(p);
        p.Name.Should().BeNull();   // unrecoverable → tombstone, never ciphertext
    }

    [Fact]
    public void Decrypt_resolves_the_owning_key_independent_of_the_ambient_tenant()
    {
        var keys = new EphemeralKeyProvider();
        var tenant = new FixedTenant { CurrentTenantId = "tenant-a" };
        var t = Transform(tenant, keys);
        var p = new Patient { Name = "Ada" };

        t.ApplyOnWrite(p);            // encrypted under tenant-a
        tenant.CurrentTenantId = "tenant-b";   // a different ambient tenant now

        t.ApplyOnRead(p);            // decrypt resolves tenant-a's key by the envelope's keyId
        p.Name.Should().Be("Ada");
    }

    [Fact]
    public void A_non_string_classified_property_is_rejected_at_construction()
    {
        var act = () => new ClassificationFieldTransform(
            new AesGcmFieldCipher(), new EphemeralKeyProvider(), new NullClassificationTenantAccessor(),
            new ClassifiedPropertyBag(typeof(BadEntity)));
        act.Should().Throw<NotSupportedException>().WithMessage("*string*");
    }

    [Fact]
    public void An_empty_string_round_trips_distinctly_from_null()
    {
        var t = Transform();
        var p = new Patient { Name = "", Diagnosis = null };

        t.ApplyOnWrite(p);
        FieldCipherEnvelope.TryParse(p.Name, out _).Should().BeTrue();   // "" is encrypted (distinguished from null)
        p.Diagnosis.Should().BeNull();

        t.ApplyOnRead(p);
        p.Name.Should().Be("");
        p.Diagnosis.Should().BeNull();
    }
}
