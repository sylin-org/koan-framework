using System.ComponentModel;
using Koan.Core.Naming;
using Koan.Core.Semantics.Segmentation;
using Koan.Storage.Keys;

namespace Koan.Storage.Identity;

/// <summary>Storage-owned physical realization of the host's hard segmentation dimensions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class StorageIdentityPlan(SegmentationPlan segmentation) : ISegmentationRealization
{
    private const string Separator = "/";
    private static readonly CompositionPolicy Policy = new(Separator, StorageKeyParticleFormatter.Instance);
    private static readonly SegmentationRealizationDescriptor Realization = new(
        "storage",
        "path-prefix",
        [
            "host.explicit",
            "list",
            "presign",
            "raw.key",
            "transfer",
            "typed.key"
        ]);

    public SegmentationRealizationDescriptor SegmentationRealization => Realization;

    /// <summary>Bind the operation once and compose its logical key or list prefix into a physical path.</summary>
    public StorageIdentityBinding Bind(string logicalKey, string operation)
    {
        ArgumentNullException.ThrowIfNull(logicalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        // Explicit infrastructure/control-plane operations deliberately bypass ambient segmentation.
        if (StorageScope.IsHostScope) return StorageIdentityBinding.Unchanged(logicalKey);

        var subject = StorageScope.CurrentType;
        var scope = subject is null ? segmentation.Untyped : segmentation.For(subject);
        var bindings = scope.Bind(operation);
        if (bindings.IsEmpty) return StorageIdentityBinding.Unchanged(logicalKey);

        var particles = new Particle[bindings.Length];
        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];
            particles[index] = new Particle(
                index,
                binding.DimensionId,
                binding.Value,
                ParticlePosition.Leading,
                Separator);
        }

        return new StorageIdentityBinding(
            logicalKey,
            IdentifierComposer.Compose(logicalKey, particles, Policy));
    }
}

/// <summary>One operation's logical/physical Storage identity translation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal readonly record struct StorageIdentityBinding(string LogicalKey, string PhysicalKey)
{
    public static StorageIdentityBinding Unchanged(string key) => new(key, key);

    public string ProjectKey(string physicalKey)
    {
        ArgumentNullException.ThrowIfNull(physicalKey);
        if (PhysicalKey == LogicalKey) return physicalKey;

        var prefixLength = PhysicalKey.Length - LogicalKey.Length;
        if (prefixLength < 1 || !physicalKey.StartsWith(PhysicalKey[..prefixLength], StringComparison.Ordinal))
            throw new InvalidOperationException("A storage provider returned an object outside the bound segmentation prefix.");

        return physicalKey[prefixLength..];
    }

    public StorageObject Project(StorageObject physical)
    {
        ArgumentNullException.ThrowIfNull(physical);
        var logicalKey = ProjectKey(physical.Key);
        var logicalId = physical.Id.EndsWith(physical.Key, StringComparison.Ordinal)
            ? physical.Id[..^physical.Key.Length] + logicalKey
            : physical.Id;
        return new StorageObject
        {
            Id = logicalId,
            Key = logicalKey,
            Name = string.Equals(physical.Name, physical.Key, StringComparison.Ordinal) ? logicalKey : physical.Name,
            ContentType = physical.ContentType,
            Size = physical.Size,
            ContentHash = physical.ContentHash,
            CreatedAt = physical.CreatedAt,
            UpdatedAt = physical.UpdatedAt,
            Provider = physical.Provider,
            Container = physical.Container,
            Tags = physical.Tags
        };
    }
}
