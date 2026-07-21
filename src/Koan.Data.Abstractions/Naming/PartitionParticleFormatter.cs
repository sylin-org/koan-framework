using Koan.Core.Naming;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Renders the partition axis into a storage-name token using the adapter's announced
/// <see cref="PartitionTokenPolicy"/> (GUID formatting, character sanitization, casing). An empty partition is
/// omitted (no suffix). This bridges the data pillar's partition rendering to the shared ARCH-0096
/// <see cref="IdentifierComposer"/>, so the storage-name composition (ordering, separator, byte clamp) is the
/// one shared algorithm and only the per-particle rendering stays adapter-specific.
/// </summary>
internal sealed class PartitionParticleFormatter : IParticleFormatter
{
    private readonly PartitionTokenPolicy _policy;

    public PartitionParticleFormatter(PartitionTokenPolicy policy) => _policy = policy;

    public string? Format(string? value) => string.IsNullOrEmpty(value) ? null : _policy.Format(value);
}
