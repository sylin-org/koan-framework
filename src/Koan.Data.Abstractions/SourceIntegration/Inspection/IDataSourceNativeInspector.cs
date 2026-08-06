namespace Koan.Data.Abstractions;

/// <summary>
/// Marker for an explicit provider-owned, non-mutating inspection view. Native inspection remains source-bound and
/// cannot be used as a schema/admin or business-write escape hatch.
/// </summary>
public interface IDataSourceNativeInspector;
