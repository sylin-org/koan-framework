using Koan.AI.Contracts.Shared;
using Koan.Data.Core;

namespace Koan.AI.Models;

/// <summary>
/// Default implementation of <see cref="IModelService"/> that manages the local model catalog
/// via Entity&lt;ModelEntry&gt;. Delegates remote operations (pull, search) to registered
/// <see cref="IModelSourceProvider"/> instances discovered via Reference = Intent.
/// </summary>
internal sealed class ModelService : IModelService
{
    private readonly IReadOnlyList<IModelSourceProvider> _sourceProviders;
    private readonly IReadOnlyList<IModelRuntime> _runtimes;
    private readonly IReadOnlyList<IFormatConverter> _converters;
    private const string DefaultCacheDirectory = ".Koan/models";

    public ModelService(
        IEnumerable<IModelSourceProvider> sourceProviders,
        IEnumerable<IModelRuntime> runtimes,
        IEnumerable<IFormatConverter> converters)
    {
        _sourceProviders = sourceProviders.ToList().AsReadOnly();
        _runtimes = runtimes.ToList().AsReadOnly();
        _converters = converters.ToList().AsReadOnly();
    }

    // ── Discovery (catalog operations) ──

    public async Task<IReadOnlyList<ModelEntry>> SearchAsync(string query, string? source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ModelEntry.All(ct);
        }

        // If a specific source is requested, delegate to that provider only
        if (!string.IsNullOrWhiteSpace(source))
        {
            var provider = _sourceProviders.FirstOrDefault(p =>
                string.Equals(p.Name, source, StringComparison.OrdinalIgnoreCase));

            if (provider is not null)
            {
                return await provider.SearchAsync(query, ct: ct);
            }

            // Unknown source — fall through to local catalog
        }

        // Search local catalog
        var localResults = await ModelEntry.Query(
            m => m.HubId.Contains(query) || m.Tags.Contains(query),
            ct);

        if (_sourceProviders.Count == 0)
        {
            return localResults;
        }

        // Search all registered remote providers and merge with local results
        var allResults = new List<ModelEntry>(localResults);

        foreach (var provider in _sourceProviders)
        {
            var remoteResults = await provider.SearchAsync(query, ct: ct);
            allResults.AddRange(remoteResults);
        }

        // Deduplicate by HubId, preferring local entries (which have LocalPath set)
        return allResults
            .GroupBy(m => m.HubId)
            .Select(g => g.FirstOrDefault(m => m.LocalPath is not null) ?? g.First())
            .ToList()
            .AsReadOnly();
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
        // Check local catalog first
        var results = await ModelEntry.Query(m => m.HubId == id, ct);
        var local = results.FirstOrDefault();

        if (local is not null)
        {
            return local;
        }

        // Try remote providers
        var provider = _sourceProviders.FirstOrDefault(p => p.CanHandle(id));

        if (provider is not null)
        {
            return await provider.GetMetadataAsync(id, ct);
        }

        return null;
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

    // ── Pull (delegates to source providers) ──

    public async Task<ModelEntry> PullAsync(string id, ModelFormat? format, IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        var provider = _sourceProviders.FirstOrDefault(p => p.CanHandle(id));

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"No model source provider can handle '{id}'. " +
                $"Registered providers: [{string.Join(", ", _sourceProviders.Select(p => p.Name))}]. " +
                "Add a provider package (e.g., Koan.AI.Models.HuggingFace) to enable pulling.");
        }

        var entry = await provider.PullAsync(id, DefaultCacheDirectory, format, progress, ct);

        // Persist to local catalog
        await entry.Save(ct);

        return entry;
    }

    // ── Format Conversion ──

    public async Task<JobRef> ConvertAsync(string modelId, ModelFormat to, Quantization quantization, CancellationToken ct)
    {
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        var converter = _converters.FirstOrDefault(c =>
            c.SourceFormats.Contains(model.Format) && c.TargetFormats.Contains(to));

        if (converter is null)
            throw new InvalidOperationException(
                $"No converter registered for {model.Format} → {to}. " +
                $"Install a Koan.AI.Convert.* extension package that supports this conversion.");

        var result = await converter.ConvertAsync(new ConversionRequest
        {
            SourcePath = model.LocalPath ?? throw new InvalidOperationException(
                $"Model '{modelId}' has no local path. Pull it first with Model.Pull()."),
            SourceFormat = model.Format,
            TargetFormat = to,
            OutputDirectory = Path.Combine(DefaultCacheDirectory, modelId, to.ToString().ToLowerInvariant()),
            Quantization = quantization
        }, ct: ct);

        // Register converted model in catalog
        var converted = new ModelEntry
        {
            HubId = model.HubId,
            Version = model.Version,
            Base = model.ToRef(),
            Format = result.Format,
            Parameters = model.Parameters,
            ContextWindow = model.ContextWindow,
            EmbeddingDim = model.EmbeddingDim,
            Quantization = result.Quantization,
            Capabilities = [.. model.Capabilities],
            LocalPath = result.OutputPath,
            DiskSizeBytes = result.FileSizeBytes,
            Origin = model.Origin,
            License = model.License
        };
        await converted.Save(ct);

        return new JobRef(converted.Id, JobStatus.Completed);
    }

    public async Task<JobRef> QuantizeAsync(string modelId, Quantization quantization, string? calibrationDataset, CancellationToken ct)
    {
        // Quantization is a conversion to the same format with quantization applied
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        return await ConvertAsync(modelId, model.Format, quantization, ct);
    }

    public Task<ModelEntry> MergeAsync(string baseModelId, string adapterId, string? outputName, CancellationToken ct)
    {
        // LoRA merge requires Python runtime — delegated via Training context
        throw new InvalidOperationException(
            "LoRA merge requires a training runtime. Use Training.Run() with a merge script, " +
            "or install Koan.AI.Training.Python for managed merge operations.");
    }

    // ── Deployment ──

    public async Task DeployAsync(string modelId, string? runtimeId, DeployOptions? options, CancellationToken ct)
    {
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        IModelRuntime runtime;
        if (runtimeId is not null)
        {
            runtime = _runtimes.FirstOrDefault(r => r.Id == runtimeId)
                ?? throw new InvalidOperationException($"Runtime '{runtimeId}' not registered.");
        }
        else
        {
            // Auto-select: find a runtime that supports this model's format
            runtime = _runtimes.FirstOrDefault(r =>
                r.SupportedFormats.Contains(model.Format) &&
                r.SupportedCapabilities.Intersect(model.Capabilities.Select(c => (ModelCapability)c)).Any())
                ?? throw new InvalidOperationException(
                    $"No runtime supports format {model.Format}. " +
                    $"Registered runtimes: [{string.Join(", ", _runtimes.Select(r => $"{r.Id} ({string.Join(",", r.SupportedFormats)})"))}]. " +
                    "Install a runtime package (e.g., Koan.AI.Connector.Ollama) or convert the model first.");
        }

        if (!await runtime.IsAvailableAsync(ct))
            throw new InvalidOperationException($"Runtime '{runtime.Id}' is not available.");

        await runtime.DeployAsync(model, options, ct);

        // Update catalog
        if (!model.DeployedTo.Contains(runtime.Id))
        {
            model.DeployedTo = [.. model.DeployedTo, runtime.Id];
            await model.Save(ct);
        }
    }

    public async Task<IReadOnlyList<ModelRoute>> RoutesAsync(string modelId, CancellationToken ct)
    {
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        var routes = new List<ModelRoute>();

        foreach (var runtime in _runtimes)
        {
            if (!await runtime.IsAvailableAsync(ct)) continue;

            // Direct deployment (format matches)
            if (runtime.SupportedFormats.Contains(model.Format))
            {
                routes.Add(new ModelRoute
                {
                    Format = model.Format,
                    RuntimeId = runtime.Id,
                    ComputeNode = runtime.Location.ToString(),
                    RequiresConversion = false,
                    EstimatedVramBytes = model.EstimatedVramBytes
                });
            }

            // Conversion paths
            foreach (var converter in _converters)
            {
                if (!converter.SourceFormats.Contains(model.Format)) continue;

                foreach (var targetFormat in converter.TargetFormats)
                {
                    if (runtime.SupportedFormats.Contains(targetFormat))
                    {
                        routes.Add(new ModelRoute
                        {
                            Format = targetFormat,
                            RuntimeId = runtime.Id,
                            ComputeNode = runtime.Location.ToString(),
                            RequiresConversion = true,
                            EstimatedVramBytes = model.EstimatedVramBytes
                        });
                    }
                }
            }
        }

        return routes;
    }

    // ── Versioning ──

    public async Task RollbackAsync(string name, string toVersion, CancellationToken ct)
    {
        if (!int.TryParse(toVersion.TrimStart('v'), out var version))
            throw new ArgumentException($"Invalid version format: '{toVersion}'. Expected 'v3' or '3'.");

        var target = (await ModelEntry.Query(
            m => m.HubId == name && m.Version == version, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Model '{name}' version {version} not found in catalog.");

        // Find the currently deployed version and undeploy it
        var current = (await ModelEntry.Query(
            m => m.HubId == name && m.DeployedTo.Count > 0, ct))
            .OrderByDescending(m => m.Version)
            .FirstOrDefault();

        if (current is not null && current.Version != version)
        {
            // Move current to standby
            var deployedRuntimes = current.DeployedTo.ToList();
            current.DeployedTo = [];
            current.Tags = [.. current.Tags.Where(t => t != "production"), "standby"];
            await current.Save(ct);

            // Deploy target to the same runtimes
            foreach (var runtimeId in deployedRuntimes)
            {
                var runtime = _runtimes.FirstOrDefault(r => r.Id == runtimeId);
                if (runtime is not null && await runtime.IsAvailableAsync(ct))
                {
                    await runtime.DeployAsync(target, null, ct);
                }
            }

            target.DeployedTo = deployedRuntimes;
            target.Tags = [.. target.Tags.Where(t => t != "standby"), "production"];
            await target.Save(ct);
        }
    }

    public async Task<ModelEntry?> AuditAsync(string name, DateTime at, CancellationToken ct)
    {
        // Find the model version that was active at the given point in time
        // by finding the latest version created before 'at'
        var history = await HistoryAsync(name, ct);
        return history.FirstOrDefault(m =>
            m.Lineage?.TrainedAt <= at || m.LastUsed <= at);
    }
}
