using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Models;

/// <summary>
/// Static facade for the model lifecycle. Platform-agnostic verbs —
/// runtime, format, and compute are resolved, not specified.
///
/// <code>
/// await Model.Pull("BAAI/bge-large-en-v1.5");
/// await Model.Convert(model, to: ModelFormat.GGUF);
/// await Model.Deploy(model);
/// await Model.Rollback("acme-support", to: "v3");
/// </code>
/// </summary>
public static class Model
{
    // ── Discovery & Acquisition ──

    /// <summary>
    /// Search for models across all registries (HF Hub, Ollama library, local catalog).
    /// </summary>
    public static async Task<IReadOnlyList<ModelEntry>> Search(
        string query, string? source = null, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Search(query, source, ct);
    }

    /// <summary>Search with structured filters.</summary>
    public static async Task<IReadOnlyList<ModelEntry>> Search(
        ModelQuery query, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Search(query, ct);
    }

    /// <summary>
    /// Download a model from any source (HF Hub, Ollama, URL, local path).
    /// Auto-detects format. Registers in local catalog.
    /// When multiple adapters can handle the model, specify <paramref name="to"/>
    /// to disambiguate (e.g., "ollama", "lmstudio").
    /// </summary>
    public static async Task<ModelEntry> Pull(
        string id,
        string? to = null,
        ModelFormat? format = null,
        IProgress<ModelPullProgress>? progress = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Pull(id, to, format, progress, ct);
    }

    /// <summary>Get detailed metadata for a model.</summary>
    public static async Task<ModelEntry?> Inspect(
        string id, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Inspect(id, ct);
    }

    // ── Transformation ──

    /// <summary>
    /// Convert a model to a different format. Toolchain resolved automatically.
    /// Submitted as an async job.
    /// </summary>
    public static async Task<JobRef> Convert(
        string modelId,
        ModelFormat to,
        Quantization quantization = Quantization.None,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Convert(modelId, to, quantization, ct);
    }

    /// <summary>Quantize a model without format conversion.</summary>
    public static async Task<JobRef> Quantize(
        string modelId,
        Quantization quantization,
        string? calibrationDataset = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Quantize(modelId, quantization, calibrationDataset, ct);
    }

    /// <summary>Merge a base model with a LoRA adapter.</summary>
    public static async Task<ModelEntry> Merge(
        string baseModelId,
        string adapterId,
        string? outputName = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Merge(baseModelId, adapterId, outputName, ct);
    }

    // ── Deployment ──

    /// <summary>
    /// Deploy a model to a runtime. Auto-selects best runtime based on
    /// format + available compute. Registers as Koan AI source.
    /// </summary>
    public static async Task Deploy(
        string modelId,
        string? runtime = null,
        DeployOptions? options = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        await service.Deploy(modelId, runtime, options, ct);
    }

    /// <summary>Show all viable format → runtime → compute paths for a model.</summary>
    public static async Task<IReadOnlyList<ModelRoute>> Routes(
        string modelId, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Routes(modelId, ct);
    }

    // ── Versioning & History ──

    /// <summary>Version chain with lineage, eval scores, deployment dates.</summary>
    public static async Task<IReadOnlyList<ModelEntry>> History(
        string name, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.History(name, ct);
    }

    /// <summary>Instant swap to a previous model version.</summary>
    public static async Task Rollback(
        string name, string toVersion, CancellationToken ct = default)
    {
        var service = ResolveService();
        await service.Rollback(name, toVersion, ct);
    }

    /// <summary>Full provenance at a point in time.</summary>
    public static async Task<ModelEntry?> Audit(
        string name, DateTime at, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Audit(name, at, ct);
    }

    // ── Registration (escape hatch) ──

    /// <summary>
    /// Register an externally-trained model. No download — just catalog entry + lineage.
    /// </summary>
    public static async Task<ModelEntry> Register(
        string path,
        string? name = null,
        Lineage? lineage = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Register(path, name, lineage, ct);
    }

    // ── Lifecycle Management ──

    /// <summary>List all locally cached models.</summary>
    public static async Task<IReadOnlyList<ModelEntry>> List(
        ModelStatus? status = null, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.List(status, ct);
    }

    /// <summary>Remove a model from local cache.</summary>
    public static async Task Remove(
        string modelId, CancellationToken ct = default)
    {
        var service = ResolveService();
        await service.Remove(modelId, ct);
    }

    /// <summary>Remove least-recently-used models beyond the keep count.</summary>
    public static async Task Prune(
        int keep = 5, CancellationToken ct = default)
    {
        var service = ResolveService();
        await service.Prune(keep, ct);
    }

    /// <summary>Per-model runtime health, latency, errors.</summary>
    public static async Task<IReadOnlyList<ModelHealthReport>> Health(
        CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Health(ct);
    }

    // ── Internal ──

    private static IModelService ResolveService()
    {
        // Resolved via AppHost.Current — same pattern as Client.Resolve()
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Model catalog not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Model.*");

        return provider.GetRequiredService<IModelService>();
    }
}

// ── Supporting types ──

/// <summary>A viable deployment path for a model.</summary>
public sealed record ModelRoute
{
    public required ModelFormat Format { get; init; }
    public required string RuntimeId { get; init; }
    public required string ComputeNode { get; init; }
    public bool RequiresConversion { get; init; }
    public TimeSpan? EstimatedConversionTime { get; init; }
    public long? EstimatedVramBytes { get; init; }
}

/// <summary>Progress during model download.</summary>
public sealed record ModelPullProgress
{
    public string Phase { get; init; } = "";
    public double Percent { get; init; }
    public long BytesDownloaded { get; init; }
    public long? TotalBytes { get; init; }
}

/// <summary>Health report for a deployed model.</summary>
public sealed record ModelHealthReport
{
    public required string ModelId { get; init; }
    public required string RuntimeId { get; init; }
    public ModelDeploymentState State { get; init; }
    public TimeSpan? LatencyP95 { get; init; }
    public int ErrorsLast24h { get; init; }
    public long? MemoryUsedBytes { get; init; }
}

/// <summary>Filter for model listing.</summary>
public enum ModelStatus
{
    Cached,
    Loaded,
    Deployed,
    Standby
}

/// <summary>Structured model search query.</summary>
public sealed record ModelQuery
{
    public string? Keywords { get; init; }
    public ModelCapability? Task { get; init; }
    public long? MinParameters { get; init; }
    public long? MaxParameters { get; init; }
    public ModelFormat? Format { get; init; }
    public Quantization? Quantization { get; init; }
    public string? License { get; init; }
    public int MaxResults { get; init; } = 20;
}
