using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Shared;
using Koan.Data.Core;

namespace Koan.AI.Models;

/// <summary>
/// Default implementation of <see cref="IModelService"/> that resolves model operations
/// through the existing adapter infrastructure (<see cref="IAiAdapterRegistry"/>,
/// <see cref="IAiModelManager"/>). Falls back to <see cref="IModelSourceProvider"/>
/// instances for sources that are not full adapters (e.g., HuggingFace Hub client).
/// </summary>
internal sealed class ModelService : IModelService
{
    private readonly IAiAdapterRegistry _adapterRegistry;
    private readonly IReadOnlyList<IModelSourceProvider> _sourceProviders;
    private readonly IReadOnlyList<IModelRuntime> _runtimes;
    private readonly IReadOnlyList<IFormatConverter> _converters;
    private const string DefaultCacheDirectory = ".Koan/models";

    public ModelService(
        IAiAdapterRegistry adapterRegistry,
        IEnumerable<IModelSourceProvider> sourceProviders,
        IEnumerable<IModelRuntime> runtimes,
        IEnumerable<IFormatConverter> converters)
    {
        _adapterRegistry = adapterRegistry;
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

        // If a specific source is requested, try adapter first, then provider
        if (!string.IsNullOrWhiteSpace(source))
        {
            var adapter = _adapterRegistry.All.FirstOrDefault(a =>
                string.Equals(a.Id, source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Type, source, StringComparison.OrdinalIgnoreCase));

            if (adapter is not null)
            {
                var adapterModels = await adapter.ListModelsAsync(ct);
                return adapterModels
                    .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Select(ToModelEntry)
                    .ToList()
                    .AsReadOnly();
            }

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

        var allResults = new List<ModelEntry>(localResults);

        // Search all adapters
        foreach (var adapter in _adapterRegistry.All)
        {
            var adapterModels = await adapter.ListModelsAsync(ct);
            var matching = adapterModels
                .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(ToModelEntry);
            allResults.AddRange(matching);
        }

        // Search all source providers
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

        // Try adapters with ModelManager
        foreach (var adapter in _adapterRegistry.All)
        {
            var models = await adapter.ListModelsAsync(ct);
            var match = models.FirstOrDefault(m =>
                string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return ToModelEntry(match);
            }
        }

        // Try source providers
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
        // Aggregate local catalog entries
        var all = await ModelEntry.All(ct);
        var allList = new List<ModelEntry>(all);

        // Also include models reported by adapters (not yet in local catalog)
        foreach (var adapter in _adapterRegistry.All)
        {
            var adapterModels = await adapter.ListModelsAsync(ct);
            foreach (var am in adapterModels)
            {
                if (!allList.Any(m => string.Equals(m.HubId, am.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    allList.Add(ToModelEntry(am));
                }
            }
        }

        if (status is null)
        {
            return allList.AsReadOnly();
        }

        return status.Value switch
        {
            ModelStatus.Deployed => allList.Where(m => m.DeployedTo.Count > 0).ToList().AsReadOnly(),
            ModelStatus.Cached => allList.Where(m => m.LocalPath is not null).ToList().AsReadOnly(),
            ModelStatus.Loaded => allList.Where(m => m.DeployedTo.Count > 0).ToList().AsReadOnly(),
            ModelStatus.Standby => allList.Where(m => m.LocalPath is not null && m.DeployedTo.Count == 0).ToList().AsReadOnly(),
            _ => allList.AsReadOnly()
        };
    }

    public async Task<ModelEntry> RegisterAsync(string path, string? name, Lineage? lineage, CancellationToken ct)
    {
        var entry = new ModelEntry
        {
            HubId = name ?? Path.GetFileNameWithoutExtension(path),
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

    // ── Pull (adapters first, then source providers) ──

    public async Task<ModelEntry> PullAsync(
        string id, string? to, ModelFormat? format,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        // When 'to' is specified, route directly to that adapter or provider
        if (!string.IsNullOrWhiteSpace(to))
        {
            return await PullFromTargetAsync(id, to, format, progress, ct);
        }

        // Gather adapters with ModelManager capability
        var managedAdapters = _adapterRegistry.All
            .Where(a => a.ModelManager is not null)
            .ToList();

        // Single adapter with ModelManager → unambiguous
        if (managedAdapters.Count == 1)
        {
            return await PullViaAdapterAsync(managedAdapters[0], id, format, progress, ct);
        }

        // Multiple adapters — try to narrow by model ID pattern
        if (managedAdapters.Count > 1)
        {
            var candidates = new List<IAiAdapter>();

            foreach (var adapter in managedAdapters)
            {
                // Check if the adapter already knows this model
                var models = await adapter.ListModelsAsync(ct);
                if (models.Any(m => string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase) ||
                                     m.Name.StartsWith(id, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(adapter);
                }
            }

            if (candidates.Count == 1)
            {
                return await PullViaAdapterAsync(candidates[0], id, format, progress, ct);
            }

            // If no adapter recognized the model, check source providers before giving up
            if (candidates.Count == 0)
            {
                var provider = _sourceProviders.FirstOrDefault(p => p.CanHandle(id));
                if (provider is not null)
                {
                    return await PullViaProviderAsync(provider, id, format, progress, ct);
                }

                // No one recognized it — try all adapters (first one that succeeds)
                // This handles "pull a new model that no adapter has listed yet"
                if (managedAdapters.Count == 1)
                {
                    return await PullViaAdapterAsync(managedAdapters[0], id, format, progress, ct);
                }

                throw new AmbiguousModelSourceException(id,
                    managedAdapters.Select(a => a.Id).ToList().AsReadOnly());
            }

            // Multiple adapters claim to know the model
            throw new AmbiguousModelSourceException(id,
                candidates.Select(a => a.Id).ToList().AsReadOnly());
        }

        // No adapters with ModelManager — fall back to source providers
        var fallbackProvider = _sourceProviders.FirstOrDefault(p => p.CanHandle(id));

        if (fallbackProvider is not null)
        {
            return await PullViaProviderAsync(fallbackProvider, id, format, progress, ct);
        }

        // Build a descriptive error
        var sources = new List<string>();
        sources.AddRange(_adapterRegistry.All.Select(a => $"{a.Id} (adapter)"));
        sources.AddRange(_sourceProviders.Select(p => $"{p.Name} (provider)"));

        var registered = sources.Count > 0
            ? $"Registered sources: [{string.Join(", ", sources)}]"
            : "No adapters or model source providers registered";

        throw new InvalidOperationException(
            $"No adapter or source provider can handle '{id}'. {registered}.");
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
                $"No converter registered for {model.Format} -> {to}. " +
                $"Registered converters: [{string.Join(", ", _converters.Select(c => $"{string.Join(",", c.SourceFormats)}->{string.Join(",", c.TargetFormats)}"))}].");

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
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        return await ConvertAsync(modelId, model.Format, quantization, ct);
    }

    public Task<ModelEntry> MergeAsync(string baseModelId, string adapterId, string? outputName, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "LoRA merge requires a training runtime. Use Training.Run() with a merge script.");
    }

    // ── Deployment ──

    public async Task DeployAsync(string modelId, string? runtimeId, DeployOptions? options, CancellationToken ct)
    {
        var model = await InspectAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        // Check if an adapter with ModelManager can handle deployment
        if (runtimeId is not null)
        {
            var adapter = _adapterRegistry.Get(runtimeId);
            if (adapter?.ModelManager is { } adapterManager)
            {
                var result = await adapterManager.EnsureInstalledAsync(
                    new AiModelOperationRequest { Model = modelId }, ct);

                if (result.Success)
                {
                    if (!model.DeployedTo.Contains(adapter.Id))
                    {
                        model.DeployedTo = [.. model.DeployedTo, adapter.Id];
                        await model.Save(ct);
                    }
                    return;
                }
            }
        }

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
                    $"Registered runtimes: [{string.Join(", ", _runtimes.Select(r => $"{r.Id} ({string.Join(",", r.SupportedFormats)})"))}].");
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
        var history = await HistoryAsync(name, ct);
        return history.FirstOrDefault(m =>
            m.Lineage?.TrainedAt <= at || m.LastUsed <= at);
    }

    // ── Private Helpers ──

    private async Task<ModelEntry> PullFromTargetAsync(
        string id, string target, ModelFormat? format,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        // Try adapter by Id or Type
        var adapter = _adapterRegistry.All.FirstOrDefault(a =>
            string.Equals(a.Id, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Type, target, StringComparison.OrdinalIgnoreCase));

        if (adapter?.ModelManager is not null)
        {
            return await PullViaAdapterAsync(adapter, id, format, progress, ct);
        }

        // Try source provider by Name
        var provider = _sourceProviders.FirstOrDefault(p =>
            string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));

        if (provider is not null)
        {
            return await PullViaProviderAsync(provider, id, format, progress, ct);
        }

        var available = new List<string>();
        available.AddRange(_adapterRegistry.All
            .Where(a => a.ModelManager is not null)
            .Select(a => a.Id));
        available.AddRange(_sourceProviders.Select(p => p.Name));

        throw new InvalidOperationException(
            $"Target '{target}' not found. " +
            $"Available targets: [{string.Join(", ", available)}].");
    }

    private async Task<ModelEntry> PullViaAdapterAsync(
        IAiAdapter adapter, string id, ModelFormat? format,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        var manager = adapter.ModelManager
            ?? throw new InvalidOperationException(
                $"Adapter '{adapter.Id}' does not support model management.");

        progress?.Report(new ModelPullProgress
        {
            Phase = $"Pulling via {adapter.Name}",
            Percent = 0
        });

        var request = new AiModelOperationRequest { Model = id };
        var result = await manager.EnsureInstalledAsync(request, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to pull '{id}' via {adapter.Name}: {result.Message}");
        }

        progress?.Report(new ModelPullProgress
        {
            Phase = "Complete",
            Percent = 100
        });

        // Create or update catalog entry from the adapter result
        var entry = result.Model is { } descriptor
            ? new ModelEntry
            {
                HubId = descriptor.Name,
                Origin = ModelOrigin.Custom,
                ContextWindow = descriptor.ContextWindow,
                EmbeddingDim = descriptor.EmbeddingDim,
                Version = 1,
                Tags = [adapter.Type]
            }
            : new ModelEntry
            {
                HubId = id,
                Origin = ModelOrigin.Custom,
                Version = 1,
                Tags = [adapter.Type]
            };

        await entry.Save(ct);
        return entry;
    }

    private async Task<ModelEntry> PullViaProviderAsync(
        IModelSourceProvider provider, string id, ModelFormat? format,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        var entry = await provider.PullAsync(id, DefaultCacheDirectory, format, progress, ct);
        await entry.Save(ct);
        return entry;
    }

    private static ModelEntry ToModelEntry(AiModelDescriptor descriptor) => new()
    {
        HubId = descriptor.Name,
        ContextWindow = descriptor.ContextWindow,
        EmbeddingDim = descriptor.EmbeddingDim,
        Origin = ModelOrigin.Custom,
        Tags = string.IsNullOrEmpty(descriptor.AdapterType) ? [] : [descriptor.AdapterType]
    };
}
