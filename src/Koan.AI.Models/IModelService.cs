using Koan.AI.Contracts.Shared;

namespace Koan.AI.Models;

/// <summary>
/// Internal service interface behind the Model.* static facade.
/// Resolved via DI. Implementations handle catalog operations,
/// format conversion, runtime management, and versioning.
/// </summary>
public interface IModelService
{
    // Discovery
    Task<IReadOnlyList<ModelEntry>> SearchAsync(string query, string? source, CancellationToken ct);
    Task<IReadOnlyList<ModelEntry>> SearchAsync(ModelQuery query, CancellationToken ct);
    Task<ModelEntry> PullAsync(string id, ModelFormat? format, IProgress<ModelPullProgress>? progress, CancellationToken ct);
    Task<ModelEntry?> InspectAsync(string id, CancellationToken ct);

    // Transformation
    Task<JobRef> ConvertAsync(string modelId, ModelFormat to, Quantization quantization, CancellationToken ct);
    Task<JobRef> QuantizeAsync(string modelId, Quantization quantization, string? calibrationDataset, CancellationToken ct);
    Task<ModelEntry> MergeAsync(string baseModelId, string adapterId, string? outputName, CancellationToken ct);

    // Deployment
    Task DeployAsync(string modelId, string? runtime, DeployOptions? options, CancellationToken ct);
    Task<IReadOnlyList<ModelRoute>> RoutesAsync(string modelId, CancellationToken ct);

    // Versioning
    Task<IReadOnlyList<ModelEntry>> HistoryAsync(string name, CancellationToken ct);
    Task RollbackAsync(string name, string toVersion, CancellationToken ct);
    Task<ModelEntry?> AuditAsync(string name, DateTime at, CancellationToken ct);

    // Registration
    Task<ModelEntry> RegisterAsync(string path, string? name, Lineage? lineage, CancellationToken ct);

    // Lifecycle
    Task<IReadOnlyList<ModelEntry>> ListAsync(ModelStatus? status, CancellationToken ct);
    Task RemoveAsync(string modelId, CancellationToken ct);
    Task PruneAsync(int keep, CancellationToken ct);
    Task<IReadOnlyList<ModelHealthReport>> HealthAsync(CancellationToken ct);
}
