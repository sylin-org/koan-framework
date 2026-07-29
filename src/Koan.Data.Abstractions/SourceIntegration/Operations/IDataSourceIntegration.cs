using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions;

/// <summary>Native execution mechanics for one source. It does not require an Entity repository.</summary>
public interface IDataSourceIntegration
{
    SourceIntegrationCapabilities Capabilities { get; }
    IDataSourceInspectorAdapter? Inspector { get; }

    bool Supports(IDataOperationBinding binding, OperationResultKind result);
    bool EnforcesReadLane(DataReadLanePlan lane);

    Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default);

    Task<SourceScalarResult> ExecuteScalar(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default);
}
