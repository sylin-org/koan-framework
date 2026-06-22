using System.Text;
using Koan.Classification.Crypto;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Classification;

/// <summary>
/// The classification field transform (ARCH-0098 §0) — the <see cref="IFieldTransform"/> that encrypts a type's
/// <c>[Classified]</c> string properties on write and decrypts them on read, via the AES-GCM
/// <see cref="IFieldCipher"/> and the per-tenant <see cref="IKeyProvider"/>. One instance per entity type (built
/// with that type's <see cref="ClassifiedPropertyBag"/>); the facade clones before <see cref="ApplyOnWrite"/> so
/// the caller keeps plaintext.
///
/// <para>The crypto dependencies are resolved from the <b>running host</b> (<see cref="AppHost.Current"/>) per
/// operation, not captured at construction: the per-type plan is memoized process-globally, so a captured provider
/// would be wrong if more than one host runs in a process (every integration test). A unit-test constructor that
/// supplies the dependencies explicitly is also provided.</para>
/// </summary>
public sealed class ClassificationFieldTransform : IFieldTransform
{
    private readonly ClassifiedPropertyBag _bag;
    private readonly Deps? _explicit;

    private readonly record struct Deps(IFieldCipher Cipher, IKeyProvider Keys, IClassificationTenantAccessor Tenant);

    /// <summary>Production: resolve the crypto dependencies from the running host per operation.</summary>
    public ClassificationFieldTransform(ClassifiedPropertyBag bag)
    {
        _bag = bag;
        ValidateStringOnly();
    }

    /// <summary>Test / explicit-DI: supply the crypto dependencies directly.</summary>
    public ClassificationFieldTransform(
        IFieldCipher cipher, IKeyProvider keys, IClassificationTenantAccessor tenant, ClassifiedPropertyBag bag)
    {
        _bag = bag;
        _explicit = new Deps(cipher, keys, tenant);
        ValidateStringOnly();
    }

    private void ValidateStringOnly()
    {
        // A classified value is stored as an opaque envelope STRING, so the property must be string-typed. Fail fast
        // at construction (first facade build for this type) rather than mid-write.
        foreach (var d in _bag.Descriptors)
            if (d.Property.PropertyType != typeof(string))
                throw new NotSupportedException(
                    $"[Classified] property '{d.Property.DeclaringType?.Name}.{d.Property.Name}' is '{d.Property.PropertyType.Name}', " +
                    "but classification encrypts to a string envelope and so supports only string properties.");
    }

    private Deps Resolve()
    {
        if (_explicit is { } d) return d;
        var sp = AppHost.Current
            ?? throw new InvalidOperationException("Classification field transform has no running host to resolve crypto services from.");
        return new Deps(
            sp.GetRequiredService<IFieldCipher>(),
            sp.GetRequiredService<IKeyProvider>(),
            sp.GetRequiredService<IClassificationTenantAccessor>());
    }

    public void ApplyOnWrite(object entity)
    {
        var deps = Resolve();
        var tenantId = deps.Tenant.CurrentTenantId;
        FieldDataKey? key = null;   // resolved once, lazily, only if there's a value to encrypt
        foreach (var d in _bag.Descriptors)
        {
            if (d.Getter(entity) is not string plaintext) continue;          // null / unset → leave
            if (FieldCipherEnvelope.TryParse(plaintext, out _)) continue;     // already an envelope → don't double-encrypt
            key ??= deps.Keys.GetActiveKey(tenantId);
            var envelope = deps.Cipher.Encrypt(Encoding.UTF8.GetBytes(plaintext), key.Value);
            d.Setter(entity, envelope.Serialize());
        }
    }

    public void ApplyOnRead(object entity)
    {
        Deps? lazy = null;
        foreach (var d in _bag.Descriptors)
        {
            if (d.Getter(entity) is not string stored) continue;             // null → leave
            if (!FieldCipherEnvelope.TryParse(stored, out var envelope)) continue;   // legacy plaintext → leave as-is
            var deps = lazy ??= Resolve();
            try
            {
                var plaintext = deps.Cipher.Decrypt(envelope, deps.Keys.GetForDecrypt(envelope.KeyId));
                d.Setter(entity, Encoding.UTF8.GetString(plaintext));
            }
            catch (KeyUnavailableException)
            {
                // The key was crypto-shredded (erasure certificate) — the value is intentionally unrecoverable.
                // Fail closed: surface null (a tombstone), never the ciphertext.
                d.Setter(entity, null);
            }
            // FieldDecryptionException (tamper / wrong key) is an integrity fault and propagates — not a normal path.
        }
    }
}
