using System.Text;
using Koan.Classification.Crypto;
using Koan.Classification.Infrastructure;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Classification.Pipeline;

/// <summary>Encrypts classified string properties on a persisted clone and restores them on materialization.</summary>
internal sealed class ClassificationFieldTransform : IFieldTransform
{
    private readonly IFieldCipher _cipher;
    private readonly IClassificationKeyProvider _keys;
    private readonly SegmentationScope _segmentation;
    private readonly ClassifiedPropertyBag _bag;

    public ClassificationFieldTransform(
        IFieldCipher cipher,
        IClassificationKeyProvider keys,
        SegmentationScope segmentation,
        ClassifiedPropertyBag bag)
    {
        _cipher = cipher;
        _keys = keys;
        _segmentation = segmentation;
        _bag = bag;

        foreach (var descriptor in bag.Descriptors)
            if (descriptor.Property.PropertyType != typeof(string))
                throw new NotSupportedException(
                    $"[Classified] property '{descriptor.Property.DeclaringType?.Name}.{descriptor.Property.Name}' is " +
                    $"'{descriptor.Property.PropertyType.Name}', but field-at-rest protection supports writable string properties only.");
    }

    public void ApplyOnWrite(object entity)
    {
        ClassificationDataKey? activeKey = null;
        foreach (var descriptor in _bag.Descriptors)
        {
            if (descriptor.Getter(entity) is not string plaintext) continue;
            if (plaintext.StartsWith(FieldCipherEnvelope.Magic, StringComparison.Ordinal))
            {
                if (!FieldCipherEnvelope.TryParse(plaintext, out _))
                    throw new ClassificationIntegrityException("Classified field uses the reserved envelope prefix but is malformed.");
                continue;
            }

            activeKey ??= _keys.GetActiveKey(ClassificationKeyScope.From(
                _segmentation.Bind("classification field write")));
            var envelope = _cipher.Encrypt(Encoding.UTF8.GetBytes(plaintext), activeKey.Value);
            descriptor.Setter(entity, envelope.Serialize());
        }
    }

    public void ApplyOnRead(object entity)
    {
        foreach (var descriptor in _bag.Descriptors)
        {
            if (descriptor.Getter(entity) is not string stored) continue;
            if (!stored.StartsWith(FieldCipherEnvelope.Magic, StringComparison.Ordinal)) continue;
            if (!FieldCipherEnvelope.TryParse(stored, out var envelope))
                throw new ClassificationIntegrityException("Classified field envelope is malformed.");

            var plaintext = _cipher.Decrypt(envelope, _keys.GetForDecrypt(envelope.KeyId));
            descriptor.Setter(entity, Encoding.UTF8.GetString(plaintext));
        }
    }
}
