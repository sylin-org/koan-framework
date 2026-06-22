using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Koan.Classification.Crypto;
using Xunit;

namespace Koan.Classification.Tests.Crypto;

/// <summary>
/// ARCH-0098 phase 2a — the AES-256-GCM field cipher. Authenticated-encryption correctness: round-trip,
/// fail-closed on a wrong key / tampered ciphertext / tag / nonce, fresh nonce per message (no deterministic
/// leakage), the embedded key id, and key-size enforcement. The cryptographic core the whole classification axis
/// rests on, tested in isolation.
/// </summary>
public sealed class AesGcmFieldCipherSpec
{
    private readonly IFieldCipher _cipher = new AesGcmFieldCipher();

    private static FieldDataKey NewKey(string id = "k1")
        => new(id, RandomNumberGenerator.GetBytes(AesGcmFieldCipher.KeySize));

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Round_trips_a_value()
    {
        var key = NewKey();
        var plaintext = Utf8("patient: Ada Lovelace, dob 1815-12-10");

        var envelope = _cipher.Encrypt(plaintext, key);
        var decrypted = _cipher.Decrypt(envelope, key);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Round_trips_an_empty_value()
    {
        var key = NewKey();
        var envelope = _cipher.Encrypt(ReadOnlySpan<byte>.Empty, key);
        envelope.Ciphertext.Should().BeEmpty();                 // GCM is a stream cipher: |ct| == |pt|
        _cipher.Decrypt(envelope, key).Should().BeEmpty();
    }

    [Fact]
    public void Ciphertext_length_equals_plaintext_length()
    {
        var key = NewKey();
        var plaintext = Utf8("0123456789");
        _cipher.Encrypt(plaintext, key).Ciphertext.Length.Should().Be(plaintext.Length);
    }

    [Fact]
    public void Embeds_the_key_id()
    {
        var envelope = _cipher.Encrypt(Utf8("x"), NewKey("tenant-7:v3"));
        envelope.KeyId.Should().Be("tenant-7:v3");
    }

    [Fact]
    public void A_wrong_key_fails_closed()
    {
        var envelope = _cipher.Encrypt(Utf8("secret"), NewKey("k1"));
        var wrong = NewKey("k1");   // same id, different material

        var act = () => _cipher.Decrypt(envelope, wrong);
        act.Should().Throw<FieldDecryptionException>();
    }

    [Fact]
    public void A_tampered_ciphertext_fails_closed()
    {
        var key = NewKey();
        var envelope = _cipher.Encrypt(Utf8("transfer $100"), key);
        envelope.Ciphertext[0] ^= 0xFF;

        var act = () => _cipher.Decrypt(envelope, key);
        act.Should().Throw<FieldDecryptionException>();
    }

    [Fact]
    public void A_tampered_tag_fails_closed()
    {
        var key = NewKey();
        var envelope = _cipher.Encrypt(Utf8("transfer $100"), key);
        envelope.Tag[0] ^= 0xFF;

        var act = () => _cipher.Decrypt(envelope, key);
        act.Should().Throw<FieldDecryptionException>();
    }

    [Fact]
    public void A_tampered_nonce_fails_closed()
    {
        var key = NewKey();
        var envelope = _cipher.Encrypt(Utf8("transfer $100"), key);
        envelope.Nonce[0] ^= 0xFF;

        var act = () => _cipher.Decrypt(envelope, key);
        act.Should().Throw<FieldDecryptionException>();
    }

    [Fact]
    public void Each_encryption_uses_a_fresh_nonce_and_differs()
    {
        var key = NewKey();
        var plaintext = Utf8("same input");

        var a = _cipher.Encrypt(plaintext, key);
        var b = _cipher.Encrypt(plaintext, key);

        a.Nonce.Should().NotEqual(b.Nonce);             // no nonce reuse
        a.Ciphertext.Should().NotEqual(b.Ciphertext);   // so identical plaintext is not deterministically linkable
        _cipher.Decrypt(a, key).Should().Equal(plaintext);
        _cipher.Decrypt(b, key).Should().Equal(plaintext);
    }

    [Fact]
    public void Nonce_is_96_bit_and_tag_is_128_bit()
    {
        var envelope = _cipher.Encrypt(Utf8("x"), NewKey());
        envelope.Nonce.Length.Should().Be(12);
        envelope.Tag.Length.Should().Be(16);
    }

    [Theory]
    [InlineData(16)]   // AES-128 material — rejected; this cipher is AES-256 only
    [InlineData(31)]
    [InlineData(33)]
    public void A_wrong_key_size_is_rejected(int size)
    {
        var badKey = new FieldDataKey("k", RandomNumberGenerator.GetBytes(size));
        var encrypt = () => _cipher.Encrypt(Utf8("x"), badKey);
        encrypt.Should().Throw<ArgumentException>();
    }
}
