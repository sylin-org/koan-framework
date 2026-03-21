using Koan.AI.Contracts.Shared;
using Koan.Data.Core;

namespace Koan.AI.Models;

/// <summary>
/// Default implementation of <see cref="IModelService"/> that manages the local model catalog
/// via Entity&lt;ModelEntry&gt;. Remote operations (pull, convert, deploy) are stubbed until
/// extension packages provide runtime/converter implementations.
/// </summary>
internal sealed class ModelService : IModelService
{
    // ── Discovery (catalog operations) ──

    public async Task<IReadOnlyList<ModelEntry>> SearchAsync(string query, string? source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ModelEntry.All(ct);
        }

        var results = await ModelEntry.Query(
            m => m.HubId.Contains(query) || m.Tags.Contains(query),
            ct);

        return results;
    }

    public async Task<IReadOnlyList<ModelEntry>> SearchAsync(ModelQuery query, CancellationToken ct)
    {
        var all = await ModelEntry.All(ct);

        IEnumerable<ModelEntry> filtered = all;

        if (!string.IsNullOrWhiteSpace(query.Keywords))
        {
            var keywords = query.Keywords;
            filtered = filtered.Where(m =>
                m.HubId.Contains(keywords, StringComparison.OrdinalIgnoreCase) ||
                m.Tags.Any(t => t.Contains(keywords, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.Task is { } capability)
        {
            filtered = filtered.Where(m => m.Capabilities.Contains(capability));
        }

        if (query.MinParameters is { } minParams)
        {
            filtered = filtered.Where(m => m.Parameters >= minParams);
        }

        if (query.MaxParameters is { } maxParams)
        {
            filtered = filtered.Where(m => m.Parameters <= maxParams);
        }

        if (query.Format is { } format)
        {
            filtered = filtered.Where(m => m.Format == format);
        }

        if (query.Quantization is { } quant)
        {
            filtered = filtered.Where(m => m.Quantization == quant);
        }

        if (!string.IsNullOrWhiteSpace(query.License))
        {
            var license = query.License;
            filtered = filtered.Where(m =>
                string.Equals(m.License, license, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.Take(query.MaxResults).ToList().AsReadOnly();
    }

    public async Task<ModelEntry?> InspectAsync(string id, CancellationToken ct)
    {
        var results = await ModelEntry.Query(m => m.HubId == id, ct);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ModelEntry>> HistoryAsync(string name, CancellationToken ct)
    {
        var results = await ModelEntry.Query(m => m.HubId == name, ct);
        return results.OrderByDescending(m => m.Version).ToList().AsReadOnly();
    }

    // ── Lifecycle (catalog operations) ──

    public async Task<IReadOnlyList<ModelEntry>> ListAsync(ModelStatus? status, CancellationToken ct)
    {
        var all = await ModelEntry.All(ct);

        if (status is null)
        {
            return all;
        }

        return status.Value switch
        {
            ModelStatus.Deployed => all.Where(m => m.DeployedTo.Count > 0).ToList().AsReadOnly(),
            ModelStatus.Cached => all.Where(m => m.LocalPath is not null).ToList().AsReadOnly(),
            ModelStatus.Loaded => all.Where(m => m.DeployedTo.Count > 0).ToList().AsReadOnly(),
            ModelStatus.Standby => all.Where(m => m.LocalPath is not null && m.DeployedTo.Count == 0).ToList().AsReadOnly(),
            _ => all
        };
    }

    public async Task<ModelEntry> RegisterAsync(string path, string? name, Lineage? lineage, CancellationToken ct)
    {
        var entry = new ModelEntry
        {
            HubId = name ?? System.IO.Path.GetFileNameWithoutExtension(path),
            LocalPath = path,
            Origin = ModelOrigin.Local,
            Lineage = lineage,
            Version = 1
        };

        await entry.Save(ct);
        return entry;
    }

    public async Task RemoveAsync(string modelId, CancellationToken ct)
    {
        await ModelEntry.Remove(modelId, ct);
    }

    public async Task PruneAsync(int keep, CancellationToken ct)
    {
        var all = await ModelEntry.All(ct);

        var toPrune = all
            .OrderByDescending(m => m.LastUsed ?? DateTime.MinValue)
            .Skip(keep)
            .ToList();

        foreach (var entry in toPrune)
        {
            await entry.Delete(ct);
        }
    }

    public Task<IReadOnlyList<ModelHealthReport>> HealthAsync(CancellationToken ct)
    {
        IReadOnlyList<ModelHealthReport> empty = Array.Empty<ModelHealthReport>();
        return Task.FromResult(empty);
    }

    // ── Stubbed operations (require extension packages) ──

    public Task<ModelEntry> PullAsync(string id, ModelFormat? format, IProgress<ModelPullProgress>? progress, CancellationToken ct)
        => throw new NotImplementedException(
            "Model pulling requires Koan.AI.Models.HuggingFace or a runtime with pull support");

    public Task<JobRef> ConvertAsync(string modelId, ModelFormat to, Quantization quantization, CancellationToken ct)
        => throw new NotImplementedException(
            "Format conversion requires a Koan.AI.Convert.* extension package");

    public Task<JobRef> QuantizeAsync(string modelId, Quantization quantization, string? calibrationDataset, CancellationToken ct)
        => throw new NotImplementedException(
            "Quantization requires a Koan.AI.Convert.* extension package");

    public Task<ModelEntry> MergeAsync(string baseModelId, string adapterId, string? outputName, CancellationToken ct)
        => throw new NotImplementedException(
            "LoRA merge requires a training runtime");

    public Task DeployAsync(string modelId, string? runtime, DeployOptions? options, CancellationToken ct)
        => throw new NotImplementedException(
            "Deployment requires a registered IModelRuntime");

    public Task<IReadOnlyList<ModelRoute>> RoutesAsync(string modelId, CancellationToken ct)
        => throw new NotImplementedException(
            "Routes require registered IModelRuntime instances");

    public Task RollbackAsync(string name, string toVersion, CancellationToken ct)
        => throw new NotImplementedException(
            "Rollback requires version history tracking (not yet implemented)");

    public Task<ModelEntry?> AuditAsync(string name, DateTime at, CancellationToken ct)
        => throw new NotImplementedException(
            "Audit requires version history tracking (not yet implemented)");
}
