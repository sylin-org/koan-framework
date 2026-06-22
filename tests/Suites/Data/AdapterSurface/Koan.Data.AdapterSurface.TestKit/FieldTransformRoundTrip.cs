using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Cross-adapter ORACLE for the field-transform round-trip seam (ARCH-0098 §0, the classification substrate;
/// ARCH-0079). Registers a GENERIC (non-crypto) reversible transform — write wraps the value, read unwraps it —
/// and proves, through the real adapter, that the framework: persists the <b>transformed</b> value (at rest),
/// returns the <b>original</b> on read (the reverse), and never corrupts the caller's instance (clone-then-write).
/// Because the transform runs <i>above</i> the adapter (the entity is already transformed before the adapter
/// serializes it), every store passes — the same universality the AES-GCM classification transform relies on.
/// Generic (a string wrap, not encryption) so the seam is validated independent of the crypto module; the actual
/// at-rest ciphertext is proven separately on SQLite (raw read + crypto-shred).
/// </summary>
public static class FieldTransformRoundTrip
{
    private const string Prefix = "WRAP:";

    public sealed class TransformedDoc : Entity<TransformedDoc>
    {
        public string Secret { get; set; } = "";
        public string Plain { get; set; } = "";
    }

    /// <summary>A reversible field transform whose read side can be gated off to observe the raw at-rest value.</summary>
    private sealed class WrapTransform : IFieldTransform
    {
        public static bool ReverseOnRead = true;

        public void ApplyOnWrite(object entity)
        {
            if (entity is TransformedDoc d && !d.Secret.StartsWith(Prefix, StringComparison.Ordinal))
                d.Secret = Prefix + d.Secret;
        }

        public void ApplyOnRead(object entity)
        {
            if (ReverseOnRead && entity is TransformedDoc d && d.Secret.StartsWith(Prefix, StringComparison.Ordinal))
                d.Secret = d.Secret[Prefix.Length..];
        }
    }

    /// <summary>Runs the round-trip matrix against whatever adapter the ambient host resolves. Boot first.</summary>
    public static async Task AssertRoundTripAsync()
    {
        StorageFieldTransformRegistry.Reset();
        WrapTransform.ReverseOnRead = true;
        StorageFieldTransformRegistry.Register(new FieldTransformContributor(
            "wrap", t => t == typeof(TransformedDoc) ? new WrapTransform() : null));
        try
        {
            using var _part = EntityContext.Partition("ftrt-" + Guid.CreateVersion7().ToString("n"));

            // Write: the caller's instance keeps its plaintext (the persisted clone is wrapped).
            var doc = new TransformedDoc { Secret = "classified-value", Plain = "ordinary" };
            await doc.Save();
            doc.Secret.Should().Be("classified-value");   // clone-then-write: original not corrupted

            // Read: the value is restored to the original (the reverse ran on this adapter).
            var loaded = await TransformedDoc.Get(doc.Id);
            loaded.Should().NotBeNull();
            loaded!.Secret.Should().Be("classified-value");
            loaded.Plain.Should().Be("ordinary");          // an untransformed field is untouched

            // At rest: gate the reverse off and read again — the stored value is the WRAPPED form, never the plaintext.
            WrapTransform.ReverseOnRead = false;
            var raw = await TransformedDoc.Get(doc.Id);
            raw!.Secret.Should().Be("WRAP:classified-value");   // transformed at rest, on this real adapter
            WrapTransform.ReverseOnRead = true;

            // Query also reverses.
            (await TransformedDoc.All()).Single().Secret.Should().Be("classified-value");
        }
        finally
        {
            WrapTransform.ReverseOnRead = true;
            StorageFieldTransformRegistry.Reset();
        }
    }
}
