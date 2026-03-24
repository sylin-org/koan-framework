using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Shared;
using Koan.AI.Resolution;
using Koan.Core.AI;
using Koan.Data.Core;

namespace Koan.AI.Models;

/// <summary>
/// Default implementation of <see cref="IModelService"/> that resolves all model operations
/// through adapter capabilities via <see cref="Resolution.AdapterResolver"/>.
/// One resolution pattern for everything: query capability, find adapter, delegate.
/// </summary>
internal sealed class ModelService : IModelService
{
    private readonly IAiAdapterRegistry _registry;
    private const string DefaultCacheDirectory = ".Koan/models";

    public ModelService(IAiAdapterRegistry registry)
    {
        _registry = registry;
    }

    // ── Discovery (catalog operations) ──

    public async Task<IReadOnlyList<ModelEntry>> Search(string query, string? source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ModelEntry.All(ct);
        }

        var adapters = source is not null
            ? new List<IAiAdapter> { AdapterResolver.Resolve(_registry, AiCapability.ModelList, source) }
            : AdapterResolver.ResolveAll(_registry, AiCapability.ModelList).ToList();

        var results = new List<ModelEntry>();

        // Local catalog
        if (string.IsNullOrWhiteSpace(source))
        {
            var localResults = await ModelEntry.Query(
                m => m.HubId.Contains(query) || m.Tags.Contains(query), ct);
            results.AddRange(localResults);
        }

        // Remote adapters with ModelList capability
        foreach (var adapter in adapters)
        {
            var models = await adapter.ListModels(ct);
            results.AddRange(models
                .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(ToModelEntry));
        }

        return results.DistinctBy(m => m.HubId).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<ModelEntry>> Search(ModelQuery query, CancellationToken ct)
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

    public async Task<ModelEntry?> Inspect(string id, CancellationToken ct)
    {
        // Check local catalog first
        var results = await ModelEntry.Query(m => m.HubId == id, ct);
        var local = results.FirstOrDefault();

        if (local is not null)
        {
            return local;
        }

        // Try all adapters
        foreach (var adapter in _registry.All)
        {
            var models = await adapter.ListModels(ct);
            var match = models.FirstOrDefault(m =>
                string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return ToModelEntry(match);
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<ModelEntry>> History(string name, CancellationToken ct)
    {
        var results = await ModelEntry.Query(m => m.HubId == name, ct);
        return results.OrderByDescending(m => m.Version).ToList().AsReadOnly();
    }

    // ── Lifecycle (catalog operations) ──

    public async Task<IReadOnlyList<ModelEntry>> List(ModelStatus? status, CancellationToken ct)
    {
        var all = await ModelEntry.All(ct);
        var allList = new List<ModelEntry>(all);

        // Also include models reported by adapters with ModelList capability
        var listAdapters = AdapterResolver.ResolveAll(_registry, AiCapability.ModelList);
        foreach (var adapter in listAdapters)
        {
            var adapterModels = await adapter.ListModels(ct);
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

    public async Task<ModelEntry> Register(string path, string? name, Lineage? lineage, CancellationToken ct)
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

    public async Task Remove(string modelId, CancellationToken ct)
    {
        // Try adapter with ModelRemove capability first
        var removeAdapters = AdapterResolver.ResolveAll(_registry, AiCapability.ModelRemove);
        foreach (var adapter in removeAdapters)
        {
            if (adapter.ModelManager is { } manager)
            {
                await manager.Flush(new AiModelOperationRequest { Model = modelId }, ct);
            }
        }

        await ModelEntry.Remove(modelId, ct);
    }

    public async Task Prune(int keep, CancellationToken ct)
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

    public Task<IReadOnlyList<ModelHealthReport>> Health(CancellationToken ct)
    {
        IReadOnlyList<ModelHealthReport> empty = [];
        return Task.FromResult(empty);
    }

    // ── Pull (via adapter with Pull capability) ──

    public async Task<ModelEntry> Pull(
        string id, string? to, ModelFormat? format,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        var adapter = AdapterResolver.Resolve(_registry, AiCapability.Pull, to);
        var manager = adapter.ModelManager
            ?? throw new InvalidOperationException(
                $"Adapter '{adapter.Id}' has Pull capability but no ModelManager.");

        progress?.Report(new ModelPullProgress
        {
            Phase = $"Pulling via {adapter.Name}",
            Percent = 0
        });

        var result = await manager.EnsureInstalled(
            new AiModelOperationRequest { Model = id }, ct);

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

        var entry = result.Model is { } descriptor
            ? new ModelEntry
            {
                HubId = descriptor.Name,
                Origin = MapOrigin(adapter.Type),
                ContextWindow = descriptor.ContextWindow,
                EmbeddingDim = descriptor.EmbeddingDim,
                Version = 1,
                Tags = [adapter.Type]
            }
            : new ModelEntry
            {
                HubId = id,
                Origin = MapOrigin(adapter.Type),
                Version = 1,
                Tags = [adapter.Type]
            };

        await entry.Save(ct);
        return entry;
    }

    // ── Format Conversion (via adapter with Convert capability) ──

    public async Task<JobRef> Convert(string modelId, ModelFormat to, Quantization quantization, CancellationToken ct)
    {
        var model = await Inspect(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        var adapter = AdapterResolver.Resolve(_registry, AiCapability.Convert);
        var manager = adapter.ModelManager
            ?? throw new InvalidOperationException(
                $"Adapter '{adapter.Id}' has Convert capability but no ModelManager.");

        // Delegate conversion to the adapter's model manager
        var result = await manager.EnsureInstalled(
            new AiModelOperationRequest
            {
                Model = modelId,
                Parameters = new Dictionary<string, string>
                {
                    ["targetFormat"] = to.ToString(),
                    ["quantization"] = quantization.ToString()
                }
            }, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Conversion of '{modelId}' to {to} failed via {adapter.Name}: {result.Message}");
        }

        // Register converted model in catalog
        var converted = new ModelEntry
        {
            HubId = model.HubId,
            Version = model.Version,
            Base = model.ToRef(),
            Format = to,
            Parameters = model.Parameters,
            ContextWindow = model.ContextWindow,
            EmbeddingDim = model.EmbeddingDim,
            Quantization = quantization,
            Capabilities = [.. model.Capabilities],
            Origin = model.Origin,
            License = model.License
        };
        await converted.Save(ct);

        return new JobRef(converted.Id, JobStatus.Completed);
    }

    public async Task<JobRef> Quantize(string modelId, Quantization quantization, string? calibrationDataset, CancellationToken ct)
    {
        var model = await Inspect(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        return await Convert(modelId, model.Format, quantization, ct);
    }

    public Task<ModelEntry> Merge(string baseModelId, string adapterId, string? outputName, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "LoRA merge requires a training runtime. Use Training.Run() with a merge script.");
    }

    // ── Deployment (via adapter with Serve.* capability) ──

    public async Task Deploy(string modelId, string? runtimeId, DeployOptions? options, CancellationToken ct)
    {
        var model = await Inspect(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        var serveCapability = $"Serve.{model.Format}";
        var adapter = AdapterResolver.Resolve(_registry, serveCapability, runtimeId);
        var manager = adapter.ModelManager
            ?? throw new InvalidOperationException(
                $"Adapter '{adapter.Id}' can serve {model.Format} but has no ModelManager for deployment.");

        var result = await manager.EnsureInstalled(
            new AiModelOperationRequest { Model = modelId }, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to deploy '{modelId}' via {adapter.Name}: {result.Message}");
        }

        if (!model.DeployedTo.Contains(adapter.Id))
        {
            model.DeployedTo = [.. model.DeployedTo, adapter.Id];
            await model.Save(ct);
        }
    }

    public async Task<IReadOnlyList<ModelRoute>> Routes(string modelId, CancellationToken ct)
    {
        var model = await Inspect(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found in catalog.");

        var routes = new List<ModelRoute>();

        foreach (var adapter in _registry.All)
        {
            var serveCapability = $"Serve.{model.Format}";
            if (adapter.HasCapability(serveCapability))
            {
                routes.Add(new ModelRoute
                {
                    Format = model.Format,
                    RuntimeId = adapter.Id,
                    ComputeNode = adapter.Id,
                    RequiresConversion = false,
                    EstimatedVramBytes = model.EstimatedVramBytes
                });
            }

            // Check conversion paths: adapter can convert and serve other formats
            if (adapter.HasCapability(AiCapability.Convert))
            {
                foreach (var cap in adapter.Capabilities.Where(c => c.StartsWith("Serve.")))
                {
                    var targetFormat = cap.Replace("Serve.", "");
                    if (Enum.TryParse<ModelFormat>(targetFormat, out var fmt) && fmt != model.Format)
                    {
                        routes.Add(new ModelRoute
                        {
                            Format = fmt,
                            RuntimeId = adapter.Id,
                            ComputeNode = adapter.Id,
                            RequiresConversion = true,
                            EstimatedVramBytes = model.EstimatedVramBytes
                        });
                    }
                }
            }
        }

        return routes.AsReadOnly();
    }

    // ── Versioning ──

    public async Task Rollback(string name, string toVersion, CancellationToken ct)
    {
        if (!int.TryParse(toVersion.TrimStart('v'), out var version))
            throw new ArgumentException($"Invalid version format: '{toVersion}'. Expected 'v3' or '3'.");

        var target = (await ModelEntry.Query(
            m => m.HubId == name && m.Version == version, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Model '{name}' version {version} not found in catalog.");

        var current = (await ModelEntry.Query(
            m => m.HubId == name && m.DeployedTo.Count > 0, ct))
            .OrderByDescending(m => m.Version)
            .FirstOrDefault();

        if (current is not null && current.Version != version)
        {
            var deployedRuntimes = current.DeployedTo.ToList();
            current.DeployedTo = [];
            current.Tags = [.. current.Tags.Where(t => t != "production"), "standby"];
            await current.Save(ct);

            // Redeploy target to the same runtimes via adapter capabilities
            foreach (var runtimeId in deployedRuntimes)
            {
                var adapter = _registry.Get(runtimeId);
                if (adapter?.ModelManager is { } manager)
                {
                    await manager.EnsureInstalled(
                        new AiModelOperationRequest { Model = target.HubId }, ct);
                }
            }

            target.DeployedTo = deployedRuntimes;
            target.Tags = [.. target.Tags.Where(t => t != "standby"), "production"];
            await target.Save(ct);
        }
    }

    public async Task<ModelEntry?> Audit(string name, DateTime at, CancellationToken ct)
    {
        var history = await History(name, ct);
        return history.FirstOrDefault(m =>
            m.Lineage?.TrainedAt <= at || m.LastUsed <= at);
    }

    // ── Private Helpers ──

    private static ModelOrigin MapOrigin(string adapterType) => adapterType.ToLowerInvariant() switch
    {
        "ollama" => ModelOrigin.Ollama,
        "huggingface" => ModelOrigin.HuggingFace,
        _ => ModelOrigin.Custom
    };

    private static ModelEntry ToModelEntry(AiModelDescriptor descriptor) => new()
    {
        HubId = descriptor.Name,
        ContextWindow = descriptor.ContextWindow,
        EmbeddingDim = descriptor.EmbeddingDim,
        Origin = ModelOrigin.Custom,
        Tags = string.IsNullOrEmpty(descriptor.AdapterType) ? [] : [descriptor.AdapterType]
    };
}
