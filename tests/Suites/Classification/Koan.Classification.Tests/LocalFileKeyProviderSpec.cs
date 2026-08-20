using AwesomeAssertions;
using Koan.Classification.Crypto;
using Xunit;

namespace Koan.Classification.Tests;

/// <summary>
/// The reason the local file provider is the floor: key custody has to outlive the process. An in-memory key
/// makes the ordinary run-stop-run loop destroy everything written before the restart, so "survives a restart"
/// is the property worth proving rather than assuming.
/// </summary>
public sealed class LocalFileKeyProviderSpec : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "koan-keyring-" + Guid.NewGuid().ToString("n"));

    private string Keyring => Path.Combine(_root, ".koan", "keys", "classification.json");

    [Fact(DisplayName = "a key issued before a restart still decrypts after one")]
    public void Key_material_survives_a_restart()
    {
        string issuedId;
        byte[] issuedMaterial;
        using (var before = new LocalFileClassificationKeyProvider(Keyring))
        {
            var issued = before.GetActiveKey("scope-a");
            issued.Key.Length.Should().Be(32);
            issuedId = issued.KeyId;
            // Copy: a returned key aliases the provider's own buffer, and Dispose zeroes it. Holding the
            // reference across the dispose would compare against a blanked array rather than the real key.
            issuedMaterial = issued.Key.ToArray();
        }

        // A second instance over the same keyring is what a restarted host sees.
        using var after = new LocalFileClassificationKeyProvider(Keyring);
        after.GetForDecrypt(issuedId).Key.ToArray()
            .Should().Equal(issuedMaterial, "the restarted host must recover the same material");
        after.GetActiveKey("scope-a").KeyId
            .Should().Be(issuedId, "an existing scope keeps its active key rather than silently re-keying");
    }

    [Fact(DisplayName = "each segmentation scope gets its own key")]
    public void Scopes_do_not_share_key_material()
    {
        using var provider = new LocalFileClassificationKeyProvider(Keyring);

        var a = provider.GetActiveKey("scope-a");
        var b = provider.GetActiveKey("scope-b");

        b.KeyId.Should().NotBe(a.KeyId);
        b.Key.ToArray().Should().NotEqual(a.Key.ToArray());
        provider.GetForDecrypt(a.KeyId).Key.ToArray().Should().Equal(a.Key.ToArray());
    }

    [Fact(DisplayName = "an unknown key id fails as unavailable custody, not as a silent miss")]
    public void Unknown_key_is_reported_as_unavailable()
    {
        using var provider = new LocalFileClassificationKeyProvider(Keyring);
        provider.GetActiveKey("scope-a");

        var read = () => provider.GetForDecrypt("0123456789abcdef0123456789abcdef");
        read.Should().Throw<ClassificationKeyUnavailableException>();
    }

    [Fact(DisplayName = "a corrupt keyring refuses rather than starting fresh over live data")]
    public void Corrupt_keyring_refuses()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Keyring)!);
        File.WriteAllText(Keyring, "{ this is not json");

        using var provider = new LocalFileClassificationKeyProvider(Keyring);
        var read = () => provider.GetActiveKey("scope-a");

        // Starting a fresh keyring here would look like success and quietly strand every existing row.
        read.Should().Throw<InvalidOperationException>().WithMessage("*keyring*");
    }

    [Fact(DisplayName = "the keyring is created on first use, under the application's own .koan directory")]
    public void Keyring_is_created_on_first_use()
    {
        File.Exists(Keyring).Should().BeFalse();

        using (var provider = new LocalFileClassificationKeyProvider(Keyring))
        {
            provider.GetActiveKey("scope-a");
        }

        File.Exists(Keyring).Should().BeTrue();
        File.ReadAllText(Keyring).Should().NotBeEmpty();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leftover temp directory is not a test failure */ }
    }
}
