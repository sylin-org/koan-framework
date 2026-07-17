using System.ComponentModel;

namespace Koan.Data.Vector.Abstractions;

/// <summary>Infrastructure bridge to the host-owned vector provider catalog.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IVectorProviderResolver
{
    IVectorAdapterFactory? Find(string? identity);
    IVectorAdapterFactory? SelectAutomatic();
    IReadOnlyList<string> AvailableProviderIds { get; }
}
