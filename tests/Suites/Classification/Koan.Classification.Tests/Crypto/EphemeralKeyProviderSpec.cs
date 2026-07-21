using System.Text;
using AwesomeAssertions;
using Koan.Classification.Crypto;
using Xunit;

namespace Koan.Classification.Tests.Crypto;

public sealed class EphemeralKeyProviderSpec
{
    private readonly AesGcmFieldCipher _cipher = new();
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    [Fact]
    public void Different_scopes_get_different_keys()
    {
        using var provider = new EphemeralClassificationKeyProvider();
        var first = provider.GetActiveKey("scope-a");
        var second = provider.GetActiveKey("scope-b");
        first.KeyId.Should().NotBe(second.KeyId);
        first.Key.ToArray().Should().NotEqual(second.Key.ToArray());
    }

    [Fact]
    public void One_scope_keeps_a_stable_active_key()
    {
        using var provider = new EphemeralClassificationKeyProvider();
        provider.GetActiveKey("scope").KeyId.Should().Be(provider.GetActiveKey("scope").KeyId);
    }

    [Fact]
    public void Decrypt_resolves_only_from_the_envelope_key_id()
    {
        using var provider = new EphemeralClassificationKeyProvider();
        var plaintext = Utf8("Ada Lovelace");
        var envelope = _cipher.Encrypt(plaintext, provider.GetActiveKey("scope-a"));
        _ = provider.GetActiveKey("scope-b");
        _cipher.Decrypt(envelope, provider.GetForDecrypt(envelope.KeyId)).Should().Equal(plaintext);
    }

    [Fact]
    public void Retired_keys_remain_available_after_count_rotation()
    {
        using var provider = new EphemeralClassificationKeyProvider(rotateAfter: 2);
        var first = provider.GetActiveKey("scope");
        var envelope = _cipher.Encrypt(Utf8("old data"), first);
        provider.GetActiveKey("scope").KeyId.Should().Be(first.KeyId);
        var rotated = provider.GetActiveKey("scope");
        rotated.KeyId.Should().NotBe(first.KeyId);
        _cipher.Decrypt(envelope, provider.GetForDecrypt(envelope.KeyId)).Should().Equal(Utf8("old data"));
    }

    [Fact]
    public void Unknown_keys_fail_loudly()
    {
        using var provider = new EphemeralClassificationKeyProvider();
        var act = () => provider.GetForDecrypt("does-not-exist");
        act.Should().Throw<ClassificationKeyUnavailableException>();
    }

    [Fact]
    public void Disposal_zeros_returned_material_and_rejects_future_use()
    {
        var provider = new EphemeralClassificationKeyProvider();
        var material = provider.GetActiveKey("scope").Key;
        material.Span.ToArray().Should().Contain(value => value != 0);
        provider.Dispose();
        material.Span.ToArray().Should().OnlyContain(value => value == 0);
        var act = () => provider.GetActiveKey("scope");
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Invalid_scope_and_rotation_threshold_are_rejected()
    {
        var create = () => new EphemeralClassificationKeyProvider(0);
        create.Should().Throw<ArgumentOutOfRangeException>();
        using var provider = new EphemeralClassificationKeyProvider();
        var resolve = () => provider.GetActiveKey("");
        resolve.Should().Throw<ArgumentException>();
    }
}
