namespace Koan.Data.Abstractions;

/// <summary>Provider-owned, non-mutating classification of the physical storage behind one configured source.</summary>
public interface IDataSourceStatusInspector : IDataSourceNativeInspector
{
    Task<DataSourceStorageState> Status(CancellationToken ct = default);
}
