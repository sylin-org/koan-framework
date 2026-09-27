namespace Koan.Data.Abstractions;

/// <summary>
/// Optional adapter seam for describing and testing candidate source settings before source selection.
/// A probe must not create provider databases, schemas, buckets, tables, collections, or Entity data.
/// </summary>
public interface IDataAdapterSetup
{
    DataAdapterSetupDescriptor DescribeSetup();

    Task<DataProviderProbeResult> Probe(
        IServiceProvider services,
        DataProviderProbeContext candidate,
        CancellationToken ct = default);
}
