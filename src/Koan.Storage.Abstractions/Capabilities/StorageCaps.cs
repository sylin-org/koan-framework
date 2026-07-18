using Koan.Core.Capabilities;

namespace Koan.Storage.Abstractions.Capabilities;

/// <summary>
/// Provider guarantees understood by the Storage pillar. Providers declare these once through
/// <see cref="IDescribesCapabilities"/>; routing, diagnostics, and optional-operation guards consume the same set.
/// </summary>
public static class StorageCaps
{
    public static readonly Capability SequentialRead = new("storage.read.sequential");
    public static readonly Capability Seek = new("storage.read.seek");
    public static readonly Capability PresignedRead = new("storage.presign.read");
    public static readonly Capability PresignedWrite = new("storage.presign.write");
    public static readonly Capability ServerSideCopy = new("storage.copy.serverSide");
    public static readonly Capability Stat = new("storage.object.stat");
    public static readonly Capability List = new("storage.object.list");

    public static CapabilitySet Describe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CapabilityResolver.TryDescribe(source, owner) ?? new CapabilitySet(owner);
    }
}
