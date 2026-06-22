using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Koan.Classification.Crypto;
using Xunit;

namespace Koan.Classification.Tests.Crypto;

/// <summary>
/// ARCH-0098 phase 2a — the ciphertext envelope's storage encoding. The serialized form must round-trip every
/// field exactly, survive a multibyte key id, and — critically for the read-reverse — distinguish a ciphertext
/// value from a legacy plaintext value cheaply and safely (no throw on malformed input). End-to-end: a serialized
/// envelope re-parses and still decrypts.
/// </summary>
public sealed class FieldCipherEnvelopeSpec
{
    private static FieldCipherEnvelope Sample(string keyId = "tenant-3:v2")
        => new(keyId, RandomNumberGenerator.GetBytes(12), RandomNumberGenerator.GetBytes(40), RandomNumberGenerator.GetBytes(16));

    [Fact]
    public void Serialize_then_TryParse_round_trips_every_field()
    {
        var env = Sample();
        var ok = FieldCipherEnvelope.TryParse(env.Serialize(), out var parsed);

        ok.Should().BeTrue();
        parsed.KeyId.Should().Be(env.KeyId);
        parsed.Nonce.Should().Equal(env.Nonce);
        parsed.Ciphertext.Should().Equal(env.Ciphertext);
        parsed.Tag.Should().Equal(env.Tag);
    }

    [Fact]
    public void Serialized_form_carries_the_magic_prefix()
        => Sample().Serialize().Should().StartWith(FieldCipherEnvelope.Magic);

    [Theory]
    [InlineData("Ada Lovelace")]                 // a legacy plaintext name
    [InlineData("")]                              // empty
    [InlineData("kfe1")]                          // prefix-ish but not the magic
    [InlineData("not base64 @@@ even after the prefix")]
    [InlineData("kfe1:!!!not-base64!!!")]         // magic but invalid base64
    [InlineData("kfe1:AAAA")]                      // magic + valid base64 but too short to be a header
    public void TryParse_rejects_non_envelope_values_without_throwing(string value)
    {
        FieldCipherEnvelope.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_null()
        => FieldCipherEnvelope.TryParse(null, out _).Should().BeFalse();

    [Fact]
    public void A_multibyte_key_id_survives_serialization()
    {
        var env = Sample(keyId: "租户-7:λ");   // non-ASCII key id
        FieldCipherEnvelope.TryParse(env.Serialize(), out var parsed).Should().BeTrue();
        parsed.KeyId.Should().Be("租户-7:λ");
    }

    [Fact]
    public void An_empty_ciphertext_round_trips()
    {
        var env = new FieldCipherEnvelope("k", RandomNumberGenerator.GetBytes(12), Array.Empty<byte>(), RandomNumberGenerator.GetBytes(16));
        FieldCipherEnvelope.TryParse(env.Serialize(), out var parsed).Should().BeTrue();
        parsed.Ciphertext.Should().BeEmpty();
        parsed.Nonce.Should().Equal(env.Nonce);
        parsed.Tag.Should().Equal(env.Tag);
    }

    [Fact]
    public void End_to_end_a_serialized_envelope_decrypts()
    {
        var cipher = new AesGcmFieldCipher();
        var key = new FieldDataKey("k1", RandomNumberGenerator.GetBytes(AesGcmFieldCipher.KeySize));
        var plaintext = Encoding.UTF8.GetBytes("4111 1111 1111 1111");

        var stored = cipher.Encrypt(plaintext, key).Serialize();   // what lands in the column

        FieldCipherEnvelope.TryParse(stored, out var reparsed).Should().BeTrue();
        cipher.Decrypt(reparsed, key).Should().Equal(plaintext);
    }
}
