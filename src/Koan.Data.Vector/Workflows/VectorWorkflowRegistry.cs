using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector;

internal sealed class VectorWorkflowRegistry : IVectorWorkflowRegistry, VectorWorkflowRegistry.IRegistrar
{
    private const int DefaultTopK = 10;
    private const double DefaultAlpha = 0.5d;

    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<VectorWorkflowRegistry>();

    private readonly IVectorService _vectorService;
    private readonly ILogger<VectorWorkflowRegistry> _logger;
    private readonly bool _featureEnabled;

    private readonly ConcurrentDictionary<(System.Type EntityType, string ProfileKey), object> _workflowCache = new();
    private readonly ConcurrentDictionary<(System.Type EntityType, string ProfileKey), VectorProfileConfiguration> _entityProfiles = new();
    private readonly ConcurrentDictionary<string, VectorProfileConfiguration> _globalProfiles = new(System.StringComparer.OrdinalIgnoreCase);

    public VectorWorkflowRegistry(
        IVectorService vectorService,
        IOptions<VectorWorkflowOptions> options,
        ILogger<VectorWorkflowRegistry> logger)
    {
        _vectorService = vectorService ?? throw new System.ArgumentNullException(nameof(vectorService));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));

        var value = options?.Value ?? new VectorWorkflowOptions();
        _featureEnabled = value.EnableWorkflows;
        LoadGlobalProfiles(value);

        if (!_featureEnabled)
        {
            KoanLog.DataDebug(_logger, LogActions.Registry, "disabled",
                ("feature", Constants.Configuration.Keys.EnableWorkflows));
        }

        VectorProfiles.Attach(this);
    }

    public bool IsEnabled => _featureEnabled;

    public IVectorWorkflow<TEntity> GetWorkflow<TEntity>(string? profileName = null)
        where TEntity : class, IEntity<string>
    {
        if (!_featureEnabled)
        {
            throw new System.InvalidOperationException(
                "Vector workflows are disabled. Set Koan:Data:Vector:EnableWorkflows=true to use this feature.");
        }

        var normalized = NormalizeProfile(profileName);
        var cacheKey = (typeof(TEntity), normalized.Key);
        if (_workflowCache.TryGetValue(cacheKey, out var existing))
        {
            return (IVectorWorkflow<TEntity>)existing;
        }

        var created = (object)CreateWorkflow<TEntity>(normalized);
        var workflow = _workflowCache.GetOrAdd(cacheKey, created);
        return (IVectorWorkflow<TEntity>)workflow;
    }

    public bool IsAvailable<TEntity>(string? profileName = null)
        where TEntity : class, IEntity<string>
    {
        if (!_featureEnabled)
        {
            return false;
        }

        return _vectorService.TryGetRepository<TEntity, string>() is not null;
    }

    void IRegistrar.ConfigureProfile(System.Type entityType, string profileName, System.Action<VectorProfileConfiguration> configure)
    {
        System.ArgumentNullException.ThrowIfNull(entityType);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        System.ArgumentNullException.ThrowIfNull(configure);

        var normalized = NormalizeProfile(profileName);
        var cacheKey = (entityType, normalized.Key);
        var profile = _entityProfiles.GetOrAdd(cacheKey, _ => new VectorProfileConfiguration(entityType, normalized.Key, normalized.Display));
        configure(profile);
        _workflowCache.TryRemove(cacheKey, out _);

        KoanLog.DataDebug(_logger, LogActions.Profile, "updated",
            ("entity", entityType.Name),
            ("profile", normalized.Display));
    }

    private Workflow<TEntity> CreateWorkflow<TEntity>(ProfileKey profile)
        where TEntity : class, IEntity<string>
    {
        var runtime = BuildRuntimeProfile(typeof(TEntity), profile);
        return new Workflow<TEntity>(this, runtime, _logger);
    }

    private VectorProfileRuntime BuildRuntimeProfile(System.Type entityType, ProfileKey profile)
    {
        var runtime = new VectorProfileRuntime(profile.Display, DefaultTopK, DefaultAlpha);
        if (_globalProfiles.TryGetValue(profile.Key, out var global))
        {
            runtime.Apply(global);
        }

        if (_entityProfiles.TryGetValue((entityType, profile.Key), out var scoped))
        {
            runtime.Apply(scoped);
        }
        else if (global is null)
        {
            KoanLog.DataDebug(_logger, LogActions.Profile, "default",
                ("entity", entityType.Name),
                ("profile", profile.Display));
        }
        else
        {
            KoanLog.DataDebug(_logger, LogActions.Profile, "global",
                ("entity", entityType.Name),
                ("profile", profile.Display));
        }

        return runtime;
    }

    private void LoadGlobalProfiles(VectorWorkflowOptions options)
    {
        _globalProfiles.Clear();
        foreach (var entry in options.Profiles)
        {
            var normalized = NormalizeProfile(entry.Key);
            var config = new VectorProfileConfiguration(typeof(object), normalized.Key, normalized.Display)
            {
                TopK = entry.Value.TopK,
                Alpha = entry.Value.Alpha,
                VectorName = entry.Value.VectorName,
                EmitMetrics = entry.Value.EmitMetrics
            };

            if (entry.Value.Metadata is { Count: > 0 })
            {
                foreach (var kvp in entry.Value.Metadata)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }

                    config.Metadata[kvp.Key] = kvp.Value;
                }
            }

            _globalProfiles[normalized.Key] = config;
        }
    }

    private static ProfileKey NormalizeProfile(string? profileName)
    {
        var display = string.IsNullOrWhiteSpace(profileName)
            ? Constants.Workflows.DefaultProfileName
            : profileName.Trim();
        var key = display.ToLowerInvariant();
        return new ProfileKey(key, display);
    }

    private IVectorSearchRepository<TEntity, string>? TryGetRepository<TEntity>()
        where TEntity : class, IEntity<string>
        => _vectorService.TryGetRepository<TEntity, string>();

    private readonly record struct ProfileKey(string Key, string Display);

    internal interface IRegistrar
    {
        void ConfigureProfile(System.Type entityType, string profileName, System.Action<VectorProfileConfiguration> configure);
    }

    internal sealed class VectorProfileConfiguration
    {
        private readonly List<System.Action<IDictionary<string, object?>>> _metadataInitializers = new();

        public VectorProfileConfiguration(System.Type entityType, string key, string displayName)
        {
            EntityType = entityType;
            Key = key;
            DisplayName = displayName;
        }

        public System.Type EntityType { get; }
        public string Key { get; }
        public string DisplayName { get; }
        public int? TopK { get; set; }
        public double? Alpha { get; set; }
        public string? VectorName { get; set; }
        public bool EmitMetrics { get; set; }
        public IDictionary<string, object?> Metadata { get; } = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        public IList<System.Action<IDictionary<string, object?>>> MetadataInitializers => _metadataInitializers;
    }

    private sealed class VectorProfileRuntime
    {
        private readonly List<System.Action<IDictionary<string, object?>>> _metadataBuilders = new();

        public VectorProfileRuntime(string name, int topK, double alpha)
        {
            Name = name;
            TopK = topK;
            Alpha = alpha;
            MetadataDefaults = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        public string Name { get; }
        public int TopK { get; private set; }
        public double Alpha { get; private set; }
        public string? VectorName { get; private set; }
        public bool EmitMetrics { get; private set; }
        public IDictionary<string, object?> MetadataDefaults { get; private set; }
        public IReadOnlyList<System.Action<IDictionary<string, object?>>> MetadataBuilders => _metadataBuilders;

        public bool HasMetadata => MetadataDefaults.Count > 0 || _metadataBuilders.Count > 0;

        public void Apply(VectorProfileConfiguration configuration)
        {
            if (configuration.TopK.HasValue)
            {
                TopK = configuration.TopK.Value;
            }

            if (configuration.Alpha.HasValue)
            {
                Alpha = configuration.Alpha.Value;
            }

            if (!string.IsNullOrWhiteSpace(configuration.VectorName))
            {
                VectorName = configuration.VectorName;
            }

            if (configuration.EmitMetrics)
            {
                EmitMetrics = true;
            }

            if (configuration.Metadata.Count > 0)
            {
                var merged = new Dictionary<string, object?>(MetadataDefaults, System.StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in configuration.Metadata)
                {
                    merged[kvp.Key] = kvp.Value;
                }

                MetadataDefaults = merged;
            }

            if (configuration.MetadataInitializers.Count > 0)
            {
                _metadataBuilders.AddRange(configuration.MetadataInitializers);
            }
        }
    }

    private sealed class Workflow<TEntity> : IVectorWorkflow<TEntity>
        where TEntity : class, IEntity<string>
    {
        private readonly VectorWorkflowRegistry _registry;
        private readonly VectorProfileRuntime _profile;
        private readonly ILogger _logger;

        public Workflow(VectorWorkflowRegistry registry, VectorProfileRuntime profile, ILogger logger)
        {
            _registry = registry;
            _profile = profile;
            _logger = logger;
        }

        public string Profile => _profile.Name;

        public bool IsAvailable => _registry.TryGetRepository<TEntity>() is not null;

        public async Task Save(TEntity entity, float[] embedding, object? metadata = null, CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(entity);
            System.ArgumentNullException.ThrowIfNull(embedding);

            var repo = EnsureRepository();
            await Data<TEntity, string>.UpsertAsync(entity, ct).ConfigureAwait(false);
            var payload = MergeMetadata(metadata);
            await repo.UpsertAsync(entity.Id, embedding, payload, ct).ConfigureAwait(false);
            LogOperation("save",
                ("documents", (object?)1),
                ("vectors", (object?)1));
        }

        public async Task<VectorWorkflowSaveManyResult> SaveMany(
            IEnumerable<(TEntity Entity, float[] Embedding, object? Metadata)> items,
            CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(items);

            var materialized = items as IList<(TEntity Entity, float[] Embedding, object? Metadata)> ?? items.ToList();
            if (materialized.Count == 0)
            {
                return new VectorWorkflowSaveManyResult(0, 0);
            }

            var repo = EnsureRepository();
            var entities = materialized.Select(x => x.Entity).ToList();
            var docsAffected = await Data<TEntity, string>.UpsertManyAsync(entities, ct).ConfigureAwait(false);

            var vectorItems = materialized
                .Select(x => (x.Entity.Id, x.Embedding, MergeMetadata(x.Metadata)))
                .ToList();

            var vectorsAffected = await repo.UpsertManyAsync(vectorItems, ct).ConfigureAwait(false);
            LogOperation("save-many",
                ("documents", (object?)docsAffected),
                ("vectors", (object?)vectorsAffected));
            return new VectorWorkflowSaveManyResult(docsAffected, vectorsAffected);
        }

        public async Task<bool> Delete(string id, CancellationToken ct = default)
        {
            System.ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var repo = EnsureRepository();
            var removed = await Data<TEntity, string>.DeleteAsync(id, ct).ConfigureAwait(false);
            var result = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
            LogOperation("delete",
                ("documents", (object?)(removed ? 1 : 0)),
                ("vectors", (object?)(result ? 1 : 0)));
            return removed && result;
        }

        public async Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(ids);
            var repo = EnsureRepository();
            var materialized = ids as IList<string> ?? ids.ToList();
            if (materialized.Count == 0)
            {
                return 0;
            }

            var docs = await Data<TEntity, string>.DeleteManyAsync(materialized, ct).ConfigureAwait(false);
            var result = await repo.DeleteManyAsync(materialized, ct).ConfigureAwait(false);
            LogOperation("delete-many",
                ("documents", (object?)docs),
                ("vectors", (object?)result));
            return result;
        }

        public async Task EnsureCreated(CancellationToken ct = default)
        {
            var repo = EnsureRepository();
            await repo.VectorEnsureCreatedAsync(ct).ConfigureAwait(false);
            LogOperation("ensure-created");
        }

        public async Task<VectorQueryResult<string>> Query(VectorQueryOptions options, CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(options);
            var repo = EnsureRepository();
            var normalized = NormalizeOptions(options);
            var result = await repo.SearchAsync(normalized, ct).ConfigureAwait(false);
            LogOperation("query",
                ("topK", (object?)(normalized.TopK ?? DefaultTopK)),
                ("alpha", (object?)(normalized.Alpha ?? DefaultAlpha)),
                ("matches", (object?)result.Matches.Count));
            return result;
        }

        public Task<VectorQueryResult<string>> Query(
            float[] vector,
            string? text = null,
            int? topK = null,
            double? alpha = null,
            object? filter = null,
            string? vectorName = null,
            CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(vector);
            var options = new VectorQueryOptions(
                Query: vector,
                TopK: topK,
                Filter: filter,
                VectorName: vectorName,
                SearchText: text,
                Alpha: alpha);
            return Query(options, ct);
        }

        private IVectorSearchRepository<TEntity, string> EnsureRepository()
        {
            var repo = _registry.TryGetRepository<TEntity>();
            if (repo is null)
            {
                throw new System.InvalidOperationException(
                    $"No vector adapter configured for entity {typeof(TEntity).Name}. Ensure a provider is registered.");
            }

            return repo;
        }

        private object? MergeMetadata(object? metadata)
        {
            if (!_profile.HasMetadata)
            {
                return metadata;
            }

            var bag = CreateMetadataBag(metadata);
            foreach (var kvp in _profile.MetadataDefaults)
            {
                if (!bag.ContainsKey(kvp.Key))
                {
                    bag[kvp.Key] = kvp.Value;
                }
            }

            foreach (var builder in _profile.MetadataBuilders)
            {
                builder(bag);
            }

            return bag;
        }

        private static IDictionary<string, object?> CreateMetadataBag(object? source)
        {
            if (source is null)
            {
                return new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            }

            if (source is IDictionary<string, object?> dict)
            {
                return new Dictionary<string, object?>(dict, System.StringComparer.OrdinalIgnoreCase);
            }

            if (source is IEnumerable<KeyValuePair<string, object?>> kvPairs)
            {
                return new Dictionary<string, object?>(kvPairs, System.StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = source
            };
        }

        private VectorQueryOptions NormalizeOptions(VectorQueryOptions options)
        {
            var topK = options.TopK ?? _profile.TopK;
            var alpha = options.Alpha ?? _profile.Alpha;
            var vectorName = string.IsNullOrWhiteSpace(options.VectorName)
                ? _profile.VectorName
                : options.VectorName;

            return options with
            {
                TopK = topK,
                Alpha = alpha,
                VectorName = vectorName
            };
        }

        private void LogOperation(string action, params (string Key, object? Value)[] context)
        {
            if (!_profile.EmitMetrics)
            {
                return;
            }

            var data = context
                .Select(tuple => (tuple.Key, tuple.Value))
                .Append(("entity", typeof(TEntity).Name))
                .Append(("profile", _profile.Name))
                .ToArray();

            KoanLog.DataDebug(_logger, LogActions.Workflow, action, data);
        }
    }

    private static class LogActions
    {
        public const string Registry = "vector.workflow.registry";
        public const string Workflow = "vector.workflow";
        public const string Profile = "vector.workflow.profile";
    }
}
