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
    Task<IReadOnlyList<ModelEntry>> Search(string query, string? source, CancellationToken ct);
    Task<IReadOnlyList<ModelEntry>> Search(ModelQuery query, CancellationToken ct);
    Task<ModelEntry> Pull(string id, string? to = null, ModelFormat? format = null, IProgress<ModelPullProgress>? progress = null, CancellationToken ct = default);
    Task<ModelEntry?> Inspect(string id, CancellationToken ct);

    // Transformation
    Task<JobRef> Convert(string modelId, ModelFormat to, Quantization quantization, CancellationToken ct);
    Task<JobRef> Quantize(string modelId, Quantization quantization, string? calibrationDataset, CancellationToken ct);
    Task<ModelEntry> Merge(string baseModelId, string adapterId, string? outputName, CancellationToken ct);

    // Deployment
    Task Deploy(string modelId, string? runtime, DeployOptions? options, CancellationToken ct);
    Task<IReadOnlyList<ModelRoute>> Routes(string modelId, CancellationToken ct);

    // Versioning
    Task<IReadOnlyList<ModelEntry>> History(string name, CancellationToken ct);
    Task Rollback(string name, string toVersion, CancellationToken ct);
    Task<ModelEntry?> Audit(string name, DateTime at, CancellationToken ct);

    // Registration
    Task<ModelEntry> Register(string path, string? name, Lineage? lineage, CancellationToken ct);

    // Lifecycle
    Task<IReadOnlyList<ModelEntry>> List(ModelStatus? status, CancellationToken ct);
    Task Remove(string modelId, CancellationToken ct);
    Task Prune(int keep, CancellationToken ct);
    Task<IReadOnlyList<ModelHealthReport>> Health(CancellationToken ct);
}
