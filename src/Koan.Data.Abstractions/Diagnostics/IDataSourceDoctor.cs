namespace Koan.Data.Abstractions;

/// <summary>Optional adapter seam for active checks that cannot mutate, provision, or repair a source.</summary>
public interface IDataSourceDoctor
{
    Task<DataDoctorReceipt> Doctor(CancellationToken ct = default);
}
