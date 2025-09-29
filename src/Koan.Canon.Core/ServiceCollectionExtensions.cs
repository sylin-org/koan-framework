using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Canon.Diagnostics;
using Koan.Canon.Options;
using Koan.Canon.Runtime;
using Koan.Canon.Core.Orchestration;
using Koan.Data.Core.Naming;
using System.Reflection;
using Koan.Canon.Materialization;
using Koan.Core.Json;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using Koan.Canon.Monitoring;
using Koan.Messaging;
using Koan.Canon.Actions;
using Microsoft.Extensions.Configuration;
using Koan.Canon.Core.Services;

namespace Koan.Canon;

public static class ServiceCollectionExtensions
{
    // Serialize writes for JSON adapter to avoid file sharing collisions
    private static readonly SemaphoreSlim s_refItemLock = new(1, 1);
    private static readonly SemaphoreSlim s_projTaskLock = new(1, 1);
    private static readonly SemaphoreSlim s_keyIndexLock = new(1, 1);

    // Shared helper: normalize a dynamic payload (Dictionary or JObject) into a dictionary
    private static IDictionary<string, object?>? ExtractDict(object? payload)
    {
        if (payload is null) return null;
        if (payload is IDictionary<string, object?> d) return d;
        if (payload is JObject jo)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in jo.Properties())
            {
                dict[prop.Name] = prop.Value?.ToObject<object?>();
            }
            return dict;
        }

        // Handle JObject from DynamicCanonEntity.Model
        if (payload is JObject jObjectPayload)
        {
            return Koan.Canon.Model.DynamicCanonEntityExtensions.FlattenJObject(jObjectPayload);
        }

        // Handle DynamicCanonEntity objects - extract the Model property which contains the actual data
        if (payload is IDynamicCanonEntity dynamicEntity)
        {
            if (dynamicEntity.Model is JObject jObjectModel)
            {
                // Use FlattenJObject from DynamicCanonExtensions to convert nested structure
                // to flattened dot-notation keys (e.g., "identifier.code", "identifier.name")
                var flattened = Koan.Canon.Model.DynamicCanonEntityExtensions.FlattenJObject(jObjectModel);
                return flattened;
            }
            else
            {
                return null;
            }
        }

        // Handle strongly-typed CanonEntity objects by converting to dictionary via JSON serialization
        try
        {
            // Serialize the object to JSON, then deserialize to JObject to get consistent property handling
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var jObj = JObject.Parse(json);
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in jObj.Properties())
            {
                dict[prop.Name] = prop.Value?.ToObject<object?>();
            }
            return dict;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail completely - this preserves existing error handling  
            return null;
        }
    }

    /// <summary>
    /// Registers Koan Canon core services and background workers.
    /// </summary>
    public static IServiceCollection AddKoanCanon(this IServiceCollection services)
    {
        // Options
        services.AddOptions<CanonOptions>();
        services.AddOptions<CanonMaterializationOptions>();
        // Install global naming policy for Canon entities
        services.AddKoanCanonNaming();

        // Initialize Canon interceptor registry manager
        var provider = services.BuildServiceProvider();
        Koan.Canon.Core.Interceptors.CanonInterceptorRegistryManager.Initialize(provider);

        // Register MessagingInterceptors for Canon entities
        Extensions.CanonEntityExtensions.RegisterCanonInterceptors();

        // Materialization engine
        services.TryAddSingleton<ICanonMaterializer, Materialization.CanonMaterializer>();

        // Hosted workers - only register if CanonOrchestrator services are present
        if (HasCanonOrchestrators())
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModelAssociationWorkerHostedService>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModelProjectionWorkerHostedService>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ParentKeyResolutionService>());
            services.TryAddSingleton<ParentKeyResolutionService>(); // For service poke access
        }
        else
        {
        }

        // Identity stamping and actions
        services.AddCanonActions();

        // Register CanonEntity message handler for "Koan.Canon.CanonEntity" queue
        // This connects the queue to any registered CanonOrchestrator services
        services.On<string>(async (payload) =>
        {
            try
            {
                // Get any registered CanonOrchestrator service and process the message
                var serviceProvider = Koan.Core.Hosting.App.AppHost.Current;
                var orchestrator = serviceProvider?.GetService<ICanonOrchestrator>();
                if (orchestrator != null)
                {
                    await orchestrator.ProcessCanonEntity(payload);
                }
                else
                {
                    // DefaultCanonOrchestrator will handle it as a fallback
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        });

        return services;
    }

    /// <summary>
    /// Installs the global storage naming override used by Canon to map generic wrappers to "{ModelFullName}#Canon.*".
    /// Safe to use in publisher-only processes; does not register workers.
    /// </summary>
    public static IServiceCollection AddKoanCanonNaming(this IServiceCollection services)
    {
        services.OverrideStorageNaming((type, defaults) =>
        {
            if (!type.IsGenericType) return null;
            var def = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            if (args.Length == 0) return null;
            var model = args[0];
            var modelFull = model.FullName ?? model.Name;

            // Root-scoped logical docs
            if (def == typeof(Koan.Canon.Model.IdentityLink<>)) return modelFull + "#Canon.identityLink";
            if (def == typeof(Koan.Canon.Model.KeyIndex<>)) return modelFull + "#Canon.keyIndex";
            if (def == typeof(Koan.Canon.Model.ReferenceItem<>)) return modelFull + "#Canon.reference";
            if (def == typeof(Koan.Canon.Model.ProjectionTask<>)) return modelFull + "#Canon.tasks";
            if (def == typeof(Koan.Canon.Model.PolicyState<>)) return modelFull + "#Canon.policies";
            // Root container should be just the model name (no suffix)
            if (def == typeof(Koan.Canon.Model.DynamicCanonEntity<>)) return modelFull;

            // Stage/View docs (set suffix appended via DataSetContext -> StorageNameRegistry)
            if (def == typeof(Koan.Canon.Model.StageRecord<>)) return modelFull;
            if (def == typeof(Koan.Canon.Model.ParkedRecord<>)) return modelFull;
            if (def == typeof(Koan.Canon.Model.CanonicalProjection<>)) return modelFull;
            if (def == typeof(Koan.Canon.Model.LineageProjection<>)) return modelFull;

            // Unknown generic → let defaults resolve
            return null;
        });
        return services;
    }

    // vNext model-aware projection worker
    internal sealed class ModelProjectionWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<CanonOptions> _opts;
        private readonly ILogger<ModelProjectionWorkerHostedService> _log;

        public ModelProjectionWorkerHostedService(IServiceProvider sp, IOptionsMonitor<CanonOptions> opts, ILogger<ModelProjectionWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("[Canon.projection] ModelProjectionWorker started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    var models = DiscoverModels();
                    if (models.Count == 0)
                    {
                        _log.LogDebug("[Canon.projection] No models discovered");
                    }
                    foreach (var modelType in models)
                    {
                        _log.LogTrace($"[Canon.projection] Processing model: {modelType.Name}");
                        var taskType = typeof(ProjectionTask<>).MakeGenericType(modelType);
                        var pageMethod = typeof(Data<,>).MakeGenericType(taskType, typeof(string))
                            .GetMethod("Page", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(int), typeof(CancellationToken) });
                        if (pageMethod is null) continue;
                        int pageNum = 1;
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            _log.LogTrace($"[Canon.projection] Fetching projection tasks page {pageNum} (batch={batch})");
                            var task = (Task)pageMethod.Invoke(null, new object?[] { pageNum, batch, stoppingToken })!;
                            await task.ConfigureAwait(false);
                            var enumerable = (System.Collections.IEnumerable)GetTaskResult(task)!;
                            var tasks = enumerable.Cast<object>().ToList();
                            if (tasks.Count == 0)
                            {
                                _log.LogTrace($"[Canon.projection] No projection tasks found for model {modelType.Name} on page {pageNum}");
                                break;
                            }
                            _log.LogDebug($"[Canon.projection] Processing {tasks.Count} projection tasks for model {modelType.Name} on page {pageNum}");
                            foreach (var t in tasks)
                            {
                                var refUlid = t.GetType().GetProperty("ReferenceId")?.GetValue(t) as string;
                                var refId = refUlid ?? string.Empty;
                                // Pull recent stage records for this reference from keyed, fallback to intake
                                var keyedSet = CanonSets.StageShort(CanonSets.Keyed);
                                var intakeSet = CanonSets.StageShort(CanonSets.Intake);
                                var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
                                var all = new List<object>();
                                using (DataSetContext.With(keyedSet))
                                {
                                    var firstPage = typeof(Data<,>).MakeGenericType(recordType, typeof(string))
                                        .GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                                    var tpage = (Task)firstPage.Invoke(null, new object?[] { 500, stoppingToken })!; await tpage.ConfigureAwait(false);
                                    var itemsEnumKeyed = (System.Collections.IEnumerable)GetTaskResult(tpage)!;
                                    all = itemsEnumKeyed.Cast<object>()
                                        .Where(r => string.Equals(r.GetType().GetProperty("ReferenceId")?.GetValue(r) as string, refId, StringComparison.Ordinal))
                                        .ToList();
                                }
                                if (all.Count == 0)
                                {
                                    using (DataSetContext.With(intakeSet))
                                    {
                                        var firstPage = typeof(Data<,>).MakeGenericType(recordType, typeof(string))
                                            .GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                                        var tpage = (Task)firstPage.Invoke(null, new object?[] { 500, stoppingToken })!; await tpage.ConfigureAwait(false);
                                        var itemsEnumIntake = (System.Collections.IEnumerable)GetTaskResult(tpage)!;
                                        all = itemsEnumIntake.Cast<object>()
                                            .Where(r => string.Equals(r.GetType().GetProperty("ReferenceId")?.GetValue(r) as string, refId, StringComparison.Ordinal))
                                            .ToList();
                                    }
                                }

                                // Canonical (range structure) and lineage
                                var Canonical = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
                                var lineage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
                                // Optional: exclude tag prefixes from Canonical/lineage (e.g., "reading.")
                                var exclude = (_opts.CurrentValue.CanonicalExcludeTagPrefixes ?? Array.Empty<string>())
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .Select(p => p.Trim())
                                    .ToArray();

                                foreach (var r in all)
                                {
                                    var src = (string)(r.GetType().GetProperty("SourceId")!.GetValue(r) ?? "unknown");
                                    var payload = r.GetType().GetProperty("Data")!.GetValue(r);
                                    var sourceMetadata = r.GetType().GetProperty("Source")?.GetValue(r);
                                    var dict = ExtractDict(payload);
                                    var sourceDict = ExtractDict(sourceMetadata);
                                    if (dict is null) continue;

                                    // ✅ FIXED: Auto-populate external ID from source system + source entity ID
                                    if (sourceDict != null && dict != null)
                                    {
                                        var systemName = GetSourceSystem(sourceDict);

                                        // The SourceId field in StageRecord already contains the source entity's ID
                                        // This is the original entity ID from the source system (e.g., "D5", "S1")
                                        // NOT the aggregation key value
                                        var sourceEntityId = src; // src is the SourceId from the StageRecord


                                        if (!string.IsNullOrEmpty(systemName) && !string.IsNullOrEmpty(sourceEntityId) && sourceEntityId != "unknown")
                                        {
                                            var externalIdKey = $"identifier.external.{systemName}";

                                            if (!Canonical.TryGetValue(externalIdKey, out var externalIdList))
                                            {
                                                externalIdList = new List<string?>();
                                                Canonical[externalIdKey] = externalIdList;
                                            }

                                            externalIdList.Add(sourceEntityId);

                                            // Also add to lineage tracking
                                            if (!lineage.TryGetValue(externalIdKey, out var externalIdLineage))
                                            {
                                                externalIdLineage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                                                lineage[externalIdKey] = externalIdLineage;
                                            }
                                            if (!externalIdLineage.TryGetValue(sourceEntityId, out var sources))
                                            {
                                                sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                                externalIdLineage[sourceEntityId] = sources;
                                            }
                                            sources.Add(src);
                                        }
                                        else
                                        {
                                        }
                                    }

                                    // ✅ NEW: Resolve ParentKey fields to Canonical ULIDs before Canonical projection
                                    var resolvedParentKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                                    // Check if this model has ParentKey properties that need resolution
                                    var entityParent = Infrastructure.CanonRegistry.GetEntityParent(modelType);
                                    if (entityParent.HasValue && sourceDict != null)
                                    {
                                        var sourceSystem = GetSourceSystem(sourceDict);
                                        if (!string.IsNullOrWhiteSpace(sourceSystem))
                                        {
                                            var parentKeyPath = entityParent.Value.ParentKeyPath;
                                            if (dict.TryGetValue(parentKeyPath, out var parentKeyRaw))
                                            {
                                                var parentKey = ToValuesFlexible(parentKeyRaw).FirstOrDefault();
                                                if (!string.IsNullOrWhiteSpace(parentKey))
                                                {
                                                    // Try to resolve parent via external ID
                                                    var parentResolved = await TryResolveParentViaExternalId(
                                                        entityParent.Value.Parent,
                                                        sourceSystem,
                                                        parentKey,
                                                        stoppingToken);

                                                    if (!string.IsNullOrWhiteSpace(parentResolved))
                                                    {
                                                        // Store resolved Canonical ULID for this ParentKey
                                                        resolvedParentKeys[parentKeyPath] = parentResolved;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    foreach (var kv in dict!)
                                    {
                                        var tag = kv.Key;

                                        // Skip source-specific ID fields - they should only be in identifier.external.{source}
                                        if (string.Equals(tag, "id", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(tag, "Id", StringComparison.OrdinalIgnoreCase))
                                            continue;

                                        if (exclude.Length > 0 && exclude.Any(p => tag.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                                            continue;

                                        var values = ToValuesFlexible(kv.Value);
                                        if (values.Count == 0) continue;

                                        // ✅ NEW: Replace ParentKey source values with resolved Canonical ULIDs
                                        if (resolvedParentKeys.TryGetValue(tag, out var resolvedParentUlid))
                                        {
                                            // Use resolved Canonical ULID instead of source value
                                            values = new List<string?> { resolvedParentUlid };
                                        }
                                        if (!Canonical.TryGetValue(tag, out var list)) { list = new List<string?>(); Canonical[tag] = list; }
                                        foreach (var v in values) list.Add(v);
                                        if (!lineage.TryGetValue(tag, out var m)) { m = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); lineage[tag] = m; }
                                        foreach (var v in values)
                                        {
                                            if (v is null) continue;
                                            if (!m.TryGetValue(v, out var sources)) { sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase); m[v] = sources; }
                                            sources.Add(src);
                                        }
                                    }
                                }

                                // Build nested Canonical object with range arrays per dotted path
                                var ranges = new Dictionary<string, Newtonsoft.Json.Linq.JToken?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var kv in Canonical)
                                {
                                    var dedup = kv.Value.Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                                    ranges[kv.Key] = new JArray(dedup);
                                }
                                var CanonicalExpanded = JsonPathMapper.Expand(ranges);
                                // Convert to provider-safe nested object (JObject/primitives/arrays)
                                var CanonicalView = JObject.FromObject(CanonicalExpanded);
                                var lineageView = lineage.ToDictionary(
                                    kv => kv.Key,
                                    kv => kv.Value.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                                    StringComparer.OrdinalIgnoreCase);

                                var canType = typeof(CanonicalProjection<>).MakeGenericType(modelType);
                                var canDoc = Activator.CreateInstance(canType)!;
                                canType.GetProperty("Id")!.SetValue(canDoc, $"{Constants.Views.Canonical}::{refId}");
                                // legacy ReferenceId removed; identifiers are CanonicalId and ReferenceId
                                if (!string.IsNullOrWhiteSpace(refUlid)) canType.GetProperty("ReferenceId")?.SetValue(canDoc, refUlid);
                                canType.GetProperty("ViewName")!.SetValue(canDoc, Constants.Views.Canonical);
                                // Canonical now publishes nested ranges under Model
                                var modelProp = canType.GetProperty("Model") ?? canType.GetProperty("View");
                                modelProp!.SetValue(canDoc, CanonicalView);
                                var canData = typeof(Data<,>).MakeGenericType(canType, typeof(string));
                                var upsertSet = canData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { canType, typeof(string), typeof(CancellationToken) })!;
                                await (Task)upsertSet.Invoke(null, new object?[] { canDoc, CanonSets.ViewShort(Constants.Views.Canonical), stoppingToken })!;

                                var linType = typeof(LineageProjection<>).MakeGenericType(modelType);
                                var linDoc = Activator.CreateInstance(linType)!;
                                linType.GetProperty("Id")!.SetValue(linDoc, $"{Constants.Views.Lineage}::{refId}");
                                // legacy ReferenceId removed; identifier is ReferenceId
                                if (!string.IsNullOrWhiteSpace(refUlid)) linType.GetProperty("ReferenceId")?.SetValue(linDoc, refUlid);
                                linType.GetProperty("ViewName")!.SetValue(linDoc, Constants.Views.Lineage);
                                linType.GetProperty("View")!.SetValue(linDoc, lineageView);
                                var linData = typeof(Data<,>).MakeGenericType(linType, typeof(string));
                                await (Task)linData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { linType, typeof(string), typeof(CancellationToken) })!
                                    .Invoke(null, new object?[] { linDoc, CanonSets.ViewShort(Constants.Views.Lineage), stoppingToken })!;

                                // ✅ Phase 4: Auto-Index Management - Create/Update IdentityLink entries for external IDs
                                await CreateOrUpdateIdentityLinks(modelType, refUlid, Canonical, stoppingToken);

                                // Materialized snapshot via policy engine → give monitors a chance to adjust before commit
                                var materializer = _sp.GetRequiredService<ICanonMaterializer>();
                                // preserve insertion order for paths
                                var ordered = new Dictionary<string, IReadOnlyCollection<string?>>(StringComparer.OrdinalIgnoreCase);
                                foreach (var kv in Canonical)
                                {
                                    ordered[kv.Key] = kv.Value.AsReadOnly();
                                }
                                var modelName = Infrastructure.CanonRegistry.GetModelName(modelType);
                                var (materializedValues, materializedPolicies) = await materializer.MaterializeAsync(modelName, ordered, stoppingToken);
                                // Build a mutable model dictionary from materialized dotted values (flat) → expanded below to nested
                                var mutableModel = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var kvp in materializedValues) mutableModel[kvp.Key] = kvp.Value;
                                var mutablePolicies = new Dictionary<string, string>(materializedPolicies, StringComparer.OrdinalIgnoreCase);
                                // Invoke typed monitors first, then untyped
                                var monitorsObj = _sp.GetServices(typeof(ICanonMonitor));
                                var typedMonitorType = typeof(ICanonMonitor<>).MakeGenericType(modelType);
                                var typedMonitors = _sp.GetServices(typedMonitorType);
                                var ctx = new Koan.Canon.Monitoring.CanonMonitorContext(modelName, refId, mutableModel, mutablePolicies);
                                foreach (var tm in typedMonitors)
                                {
                                    var m = typedMonitorType.GetMethod("OnProjectedAsync")!;
                                    var monitorTask = (Task)m.Invoke(tm, new object?[] { ctx, stoppingToken })!; await monitorTask.ConfigureAwait(false);
                                }
                                foreach (var um in monitorsObj)
                                {
                                    var m = typeof(ICanonMonitor).GetMethod("OnProjectedAsync")!;
                                    var monitorTask2 = (Task)m.Invoke(um, new object?[] { modelType, ctx, stoppingToken })!; await monitorTask2.ConfigureAwait(false);
                                }
                                // Upsert root entity with flat storage (no Model wrapper) in ROOT scope (no set)
                                using (DataSetContext.With(null))
                                {
                                    // For flat root storage, create instance of the actual modelType directly
                                    // This eliminates the Model wrapper for both CanonEntity<T> and DynamicCanonEntity<T>
                                    var rootEntity = Activator.CreateInstance(modelType)!;
                                    modelType.GetProperty("Id")!.SetValue(rootEntity, refId);
                                    if (!string.IsNullOrWhiteSpace(refUlid))
                                        modelType.GetProperty("ReferenceId")?.SetValue(rootEntity, refUlid);

                                    // DEBUG: Log materialized data
                                    foreach (var kvp in mutableModel)
                                    {
                                    }

                                    // Handle different entity types differently
                                    if (typeof(IDynamicCanonEntity).IsAssignableFrom(modelType))
                                    {
                                        // For DynamicCanonEntity<T>, build nested JSON object from dotted paths and set Model property
                                        var pathMap = new Dictionary<string, JToken?>(StringComparer.OrdinalIgnoreCase);
                                        foreach (var kvp in mutableModel)
                                        {
                                            pathMap[kvp.Key] = kvp.Value is null ? JValue.CreateNull() : new JValue(kvp.Value);
                                        }
                                        var nested = JsonPathMapper.Expand(pathMap);
                                        modelType.GetProperty("Model")!.SetValue(rootEntity, nested);
                                    }
                                    else
                                    {
                                        // For CanonEntity<T>, set properties directly from flat materialized dictionary
                                        foreach (var kvp in mutableModel)
                                        {
                                            if (kvp.Key != "Id" && kvp.Key != "ReferenceId")
                                            {
                                                // Try exact match first, then case-insensitive match
                                                var prop = modelType.GetProperty(kvp.Key) ??
                                                          modelType.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                                if (prop != null && prop.CanWrite)
                                                {
                                                    prop.SetValue(rootEntity, kvp.Value);
                                                }
                                                else
                                                {
                                                }
                                            }
                                        }
                                    }

                                    // DEBUG: Log final entity before saving
                                    try
                                    {
                                        var json = Newtonsoft.Json.JsonConvert.SerializeObject(rootEntity, Newtonsoft.Json.Formatting.Indented);
                                    }
                                    catch (Exception ex)
                                    {
                                    }

                                    // Store the entity directly without DynamicCanonEntity wrapper
                                    var entityData = typeof(Data<,>).MakeGenericType(modelType, typeof(string));
                                    await (Task)entityData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { modelType, typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { rootEntity, stoppingToken })!;
                                    // Upsert policy state
                                    var polType = typeof(PolicyState<>).MakeGenericType(modelType);
                                    var pol = Activator.CreateInstance(polType)!;
                                    polType.GetProperty("Id")!.SetValue(pol, refId);
                                    // Policy state now stores ReferenceId instead of legacy ReferenceId
                                    polType.GetProperty("ReferenceId")!.SetValue(pol, refId);
                                    polType.GetProperty("Policies")!.SetValue(pol, new Dictionary<string, string>(mutablePolicies, StringComparer.OrdinalIgnoreCase));
                                    var polData = typeof(Data<,>).MakeGenericType(polType, typeof(string));
                                    await (Task)polData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { polType, typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { pol, stoppingToken })!;
                                }
                                // clear RequiresProjection if set
                                var refType = typeof(ReferenceItem<>).MakeGenericType(modelType);
                                var refData = typeof(Data<,>).MakeGenericType(refType, typeof(string));
                                var getRef = refData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                                var refTask = (Task)getRef.Invoke(null, new object?[] { refId, stoppingToken })!;
                                await refTask.ConfigureAwait(false);
                                var refItem = GetTaskResult(refTask);
                                if (refItem is not null && (bool)(refItem.GetType().GetProperty("RequiresProjection")!.GetValue(refItem) ?? false))
                                {
                                    refItem.GetType().GetProperty("RequiresProjection")!.SetValue(refItem, false);
                                    await (Task)refData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { refType, typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { refItem, stoppingToken })!;
                                }

                                // delete task
                                var taskId = (string)t.GetType().GetProperty("Id")!.GetValue(t)!;
                                var taskData = typeof(Data<,>).MakeGenericType(taskType, typeof(string));
                                var deleteById = taskData.GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                                await (Task)deleteById.Invoke(null, new object?[] { taskId, stoppingToken })!;
                            }

                            pageNum++;
                        }
                    }
                }
                catch (Exception ex)
                { _log.LogError(ex, "[Canon.projection] ModelProjectionWorker iteration failed (will retry)"); }

                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }
    }

    // vNext model-aware association worker
    internal sealed class ModelAssociationWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<CanonOptions> _opts;
        private readonly ILogger<ModelAssociationWorkerHostedService> _log;

        public ModelAssociationWorkerHostedService(IServiceProvider sp, IOptionsMonitor<CanonOptions> opts, ILogger<ModelAssociationWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("[Canon.association] ModelAssociationWorker started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    var models = DiscoverModels();
                    if (models.Count == 0)
                    {
                        _log.LogDebug("[Canon.association] No models discovered");
                    }
                    foreach (var modelType in models)
                    {
                        _log.LogTrace($"[Canon.association] Processing model: {modelType.Name}");
                        var intakeSet = CanonSets.StageShort(CanonSets.Intake);
                        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
                        List<object> page;
                        using (DataSetContext.With(intakeSet))
                        {
                            var dataType = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
                            var pageM = dataType.GetMethod("Page", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(int), typeof(CancellationToken) })!;
                            var task = (Task)pageM.Invoke(null, new object?[] { 1, batch, stoppingToken })!;
                            await task.ConfigureAwait(false);
                            var resultEnum = (System.Collections.IEnumerable)GetTaskResult(task)!;
                            page = resultEnum.Cast<object>().ToList();
                        }
                        if (page.Count == 0)
                        {
                            _log.LogTrace($"[Canon.association] No intake records found for model {modelType.Name}");
                            continue;
                        }
                        _log.LogDebug($"[Canon.association] Processing {page.Count} intake records for model {modelType.Name}");

                        var voParent = Infrastructure.CanonRegistry.GetValueObjectParent(modelType);
                        var entityParent = Infrastructure.CanonRegistry.GetEntityParent(modelType);
                        var hasParentKey = voParent is not null || entityParent is not null;
                        var isValueObject = voParent is not null;
                        var tags = Infrastructure.CanonRegistry.GetAggregationTags(modelType);
                        if (!hasParentKey && tags.Length == 0) tags = _opts.CurrentValue.AggregationTags ?? Array.Empty<string>();
                        foreach (var rec in page)
                        {
                            using var _root = DataSetContext.With(null);
                            var dict = ExtractDict(rec.GetType().GetProperty("Data")!.GetValue(rec));
                            if (dict is null || (!hasParentKey && tags.Length == 0))
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = dict is null ? "no-payload" : "no-config-tags", tags }, rec, modelType, intakeSet, stoppingToken);
                                continue;
                            }

                            var candidates = new List<(string tag, string value)>();

                            // Priority 1: Handle ParentKey resolution if present
                            if (hasParentKey)
                            {
                                // Get the correct parent info based on type
                                var parentInfo = isValueObject ? voParent!.Value : entityParent!.Value;
                                var parentKeyPath = parentInfo.ParentKeyPath;

                                if (!dict.TryGetValue(parentKeyPath, out var raw))
                                {
                                    // Fallback: accept any reserved reference.* entry
                                    var refKvp = dict.FirstOrDefault(kv => kv.Key.StartsWith(Constants.Reserved.ReferencePrefix, StringComparison.OrdinalIgnoreCase));
                                    if (refKvp.Key is null)
                                    {
                                        var reason = isValueObject ? "vo-parent-key-missing" : "entity-parent-key-missing";
                                        await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = reason, path = parentKeyPath }, rec, modelType, intakeSet, stoppingToken);
                                        continue;
                                    }
                                    raw = refKvp.Value;
                                }
                                var parentKey = ToValuesFlexible(raw).FirstOrDefault();
                                if (string.IsNullOrWhiteSpace(parentKey))
                                {
                                    var reason = isValueObject ? "vo-parent-key-empty" : "entity-parent-key-empty";
                                    await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = reason, path = parentKeyPath }, rec, modelType, intakeSet, stoppingToken);
                                    continue;
                                }

                                // For ParentKey resolution, try to find parent via external ID lookup
                                // The parentKey value (e.g., "oemD2") is the source-specific ID
                                // We need to find the parent whose identifier.external.{source} matches this value
                                var sourceSystem = ToValuesFlexible(dict.TryGetValue(Constants.Envelope.System, out var sysVal) ? sysVal : null).FirstOrDefault();
                                if (!string.IsNullOrWhiteSpace(sourceSystem))
                                {
                                    // Try to resolve parent via IdentityLink using external ID
                                    var parentResolved = await TryResolveParentViaExternalId(
                                        parentInfo.Parent,
                                        sourceSystem,
                                        parentKey,
                                        stoppingToken);

                                    if (!string.IsNullOrWhiteSpace(parentResolved))
                                    {
                                        // Successfully resolved parent - use Canonical ULID
                                        candidates.Add((parentKeyPath, parentResolved));
                                    }
                                    else
                                    {
                                        // Parent not yet available - park this record
                                        var reason = isValueObject ? "vo-parent-not-resolved" : "entity-parent-not-resolved";
                                        await this.SaveRejectAndDrop(
                                            "PARENT_NOT_FOUND",
                                            new
                                            {
                                                reason = reason,
                                                parentType = parentInfo.Parent.Name,
                                                parentKey = parentKey,
                                                source = sourceSystem
                                            },
                                            rec, modelType, intakeSet, stoppingToken);

                                        // Trigger ParentKeyResolutionService for immediate processing
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var resolutionService = _sp.GetService<ParentKeyResolutionService>();
                                                if (resolutionService != null)
                                                {
                                                    await resolutionService.TriggerResolutionAsync();
                                                }
                                            }
                                            catch
                                            {
                                                // Fire and forget - don't fail the main processing loop
                                            }
                                        });

                                        continue;
                                    }
                                }
                                else
                                {
                                    // No source system - fallback to original behavior
                                    candidates.Add((parentKeyPath, parentKey));
                                }
                            }

                            // Priority 2: Handle aggregation keys (for entities that also have aggregation keys)
                            if (tags.Length > 0)
                            {
                                foreach (var tag in tags)
                                {
                                    if (!dict.TryGetValue(tag, out var raw)) continue;
                                    foreach (var v in ToValuesFlexible(raw))
                                    {
                                        if (!string.IsNullOrWhiteSpace(v))
                                        {
                                            candidates.Add((tag, v));
                                        }
                                    }
                                }
                            }
                            // Optional composite candidate: system|adapter|externalId for safer ownership, when present
                            if (dict.TryGetValue(Constants.Envelope.System, out var sys) &&
                                dict.TryGetValue(Constants.Envelope.Adapter, out var adp))
                            {
                                // Discover external-id field names from [EntityLink] metadata
                                var extKeys = Infrastructure.CanonRegistry.GetExternalIdKeys(modelType);
                                foreach (var extKey in extKeys)
                                {
                                    if (!dict.TryGetValue(extKey, out var extRaw)) continue;
                                    var sysV = ToValuesFlexible(sys).FirstOrDefault();
                                    var adpV = ToValuesFlexible(adp).FirstOrDefault();
                                    foreach (var v in ToValuesFlexible(extRaw))
                                    {
                                        if (string.IsNullOrWhiteSpace(sysV) || string.IsNullOrWhiteSpace(adpV) || string.IsNullOrWhiteSpace(v)) continue;
                                        var composite = string.Join('|', sysV, adpV, v);
                                        candidates.Add(($"env.{Constants.Envelope.System}|{Constants.Envelope.Adapter}|{extKey}", composite));
                                    }
                                }
                                // Also accept contractless reserved identifier.external.* bag keys
                                foreach (var kv in dict)
                                {
                                    if (!kv.Key.StartsWith(Constants.Reserved.IdentifierExternalPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                                    var sysV = ToValuesFlexible(sys).FirstOrDefault();
                                    var adpV = ToValuesFlexible(adp).FirstOrDefault();
                                    foreach (var v in ToValuesFlexible(kv.Value))
                                    {
                                        if (string.IsNullOrWhiteSpace(sysV) || string.IsNullOrWhiteSpace(adpV) || string.IsNullOrWhiteSpace(v)) continue;
                                        var composite = string.Join('|', sysV, adpV, v);
                                        candidates.Add(($"env.{Constants.Envelope.System}|{Constants.Envelope.Adapter}|{kv.Key}", composite));
                                    }
                                }
                            }
                            if (candidates.Count == 0)
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = "no-values", tags }, rec, modelType, intakeSet, stoppingToken);
                                continue;
                            }

                            var kiType = typeof(KeyIndex<>).MakeGenericType(hasParentKey ? (isValueObject ? voParent!.Value.Parent : entityParent!.Value.Parent) : modelType);
                            var kiData = typeof(Data<,>).MakeGenericType(kiType, typeof(string));
                            var getKi = kiData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                            var owners = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var c in candidates)
                            {
                                var kiTask = (Task)getKi.Invoke(null, new object?[] { c.value, stoppingToken })!;
                                await kiTask.ConfigureAwait(false);
                                var ki = GetTaskResult(kiTask);
                                var rid = (string?)ki?.GetType().GetProperty("ReferenceId")?.GetValue(ki);
                                if (!string.IsNullOrWhiteSpace(rid)) owners.Add(rid!);
                            }

                            string ReferenceId;
                            if (owners.Count > 1)
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.MultiOwnerCollision, new { owners = owners.ToArray(), keys = candidates }, rec, modelType, intakeSet, stoppingToken);
                                goto NextRecord;
                            }
                            else if (owners.Count == 1)
                            { ReferenceId = owners.First(); }
                            else
                            {
                                // Try identity map first using envelope fields
                                var refFromIdentity = await TryResolveIdentityAsync(modelType, dict, stoppingToken);
                                ReferenceId = refFromIdentity ?? Guid.CreateVersion7().ToString("n");
                            }

                            // Save/verify keys ownership
                            foreach (var c in candidates)
                            {
                                var kiTask = (Task)getKi.Invoke(null, new object?[] { c.value, stoppingToken })!;
                                await kiTask.ConfigureAwait(false);
                                var ki = GetTaskResult(kiTask);
                                if (ki is null)
                                {
                                    var newKi = Activator.CreateInstance(kiType)!;
                                    kiType.GetProperty("AggregationKey")!.SetValue(newKi, c.value);
                                    kiType.GetProperty("ReferenceId")!.SetValue(newKi, ReferenceId);
                                    await (Task)kiData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { kiType, typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { newKi, stoppingToken })!;
                                }
                                else if (!string.Equals((string)kiType.GetProperty("ReferenceId")!.GetValue(ki)!, ReferenceId, StringComparison.Ordinal))
                                {
                                    await this.SaveRejectAndDrop(Constants.Rejections.KeyOwnerMismatch, new { key = c.value, existing = kiType.GetProperty("ReferenceId")!.GetValue(ki), incoming = ReferenceId }, rec, modelType, intakeSet, stoppingToken);
                                    goto NextRecord;
                                }
                            }

                            var refType = typeof(ReferenceItem<>).MakeGenericType(hasParentKey ? (isValueObject ? voParent!.Value.Parent : entityParent!.Value.Parent) : modelType);
                            var refData = typeof(Data<,>).MakeGenericType(refType, typeof(string));
                            var getRef = refData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                            var refTask = (Task)getRef.Invoke(null, new object?[] { ReferenceId, stoppingToken })!;
                            await refTask.ConfigureAwait(false);
                            var ri = GetTaskResult(refTask) ?? Activator.CreateInstance(refType)!;
                            // Mint a ULID for the reference if it's a new item (Id empty)
                            var currId = (string?)refType.GetProperty("Id")!.GetValue(ri);
                            if (string.IsNullOrWhiteSpace(currId))
                            {
                                refType.GetProperty("Id")!.SetValue(ri, ReferenceId);
                            }
                            var nextVersion = (ulong)((refType.GetProperty("Version")!.GetValue(ri) as ulong?) ?? 0) + 1UL;
                            refType.GetProperty("Version")!.SetValue(ri, nextVersion);
                            refType.GetProperty("RequiresProjection")!.SetValue(ri, true);
                            await (Task)refData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { refType, typeof(CancellationToken) })!
                                .Invoke(null, new object?[] { ri, stoppingToken })!;

                            // Create projection task for Canonical projections
                            var refUlid = (string)refType.GetProperty("Id")!.GetValue(ri)!;
                            // Create tasks for: 1) entities without parents, 2) CanonEntity with parents (but not CanonValueObject with parents)
                            if (!hasParentKey || (hasParentKey && !isValueObject))
                            {
                                var taskType = typeof(ProjectionTask<>).MakeGenericType(modelType);
                                var newTask = Activator.CreateInstance(taskType)!;
                                taskType.GetProperty("Id")!.SetValue(newTask, $"{refUlid}::{nextVersion}::{Constants.Views.Canonical}");
                                taskType.GetProperty("ReferenceId")?.SetValue(newTask, refUlid);
                                taskType.GetProperty("Version")!.SetValue(newTask, nextVersion);
                                taskType.GetProperty("ViewName")!.SetValue(newTask, Constants.Views.Canonical);
                                taskType.GetProperty("CreatedAt")!.SetValue(newTask, DateTimeOffset.UtcNow);
                                var taskDataType = typeof(Data<,>).MakeGenericType(taskType, typeof(string));
                                await (Task)taskDataType.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { taskType, typeof(CancellationToken) })!
                                    .Invoke(null, new object?[] { newTask, stoppingToken })!;
                            }

                            // Move record to keyed set and drop from intake
                            var keyedSet = CanonSets.StageShort(CanonSets.Keyed);
                            // also propagate ULID on stage record for downstream consumers
                            rec.GetType().GetProperty("ReferenceId")?.SetValue(rec, refUlid);
                            var recData = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
                            await (Task)recData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!
                                .Invoke(null, new object?[] { rec, keyedSet, stoppingToken })!;
                            var delGeneric = typeof(Data<,>).MakeGenericType(recordType, typeof(string))
                                .GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                            await (Task)delGeneric.Invoke(null, new object?[] { (string)rec.GetType().GetProperty("Id")!.GetValue(rec)!, intakeSet, stoppingToken })!;

                        NextRecord:;
                        }
                    }
                }
                catch (Exception ex)
                { _log.LogError(ex, "[Canon.association] ModelAssociationWorker iteration failed"); }

                try { await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        private static async Task<string?> TryResolveIdentityAsync(Type modelType, IDictionary<string, object?> dict, CancellationToken ct)
        {
            if (!dict.TryGetValue(Constants.Envelope.System, out var sysRaw) || !dict.TryGetValue(Constants.Envelope.Adapter, out var adpRaw)) return null;
            var sys = ToValuesFlexible(sysRaw).FirstOrDefault();
            var adp = ToValuesFlexible(adpRaw).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sys) || string.IsNullOrWhiteSpace(adp)) return null;
            var idType = typeof(Koan.Canon.Model.IdentityLink<>).MakeGenericType(modelType);
            var idData = typeof(Data<,>).MakeGenericType(idType, typeof(string));
            // Probe for externalId fields discovered via [EntityLink]
            foreach (var extKey in Infrastructure.CanonRegistry.GetExternalIdKeys(modelType))
            {
                if (!dict.TryGetValue(extKey, out var extRaw)) continue;
                foreach (var ext in ToValuesFlexible(extRaw))
                {
                    if (string.IsNullOrWhiteSpace(ext)) continue;
                    var composite = string.Join('|', sys, adp, ext);
                    var getM = idData.GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) });
                    if (getM is null) continue;
                    var task = (Task)getM.Invoke(null, new object?[] { composite, ct })!; await task.ConfigureAwait(false);
                    var link = GetTaskResult(task);
                    if (link is not null)
                    {
                        var rulid = idType.GetProperty("ReferenceId")!.GetValue(link) as string;
                        if (!string.IsNullOrWhiteSpace(rulid)) return rulid!;
                    }
                    else
                    {
                        // Issue a Canonical ULID immediately and create a provisional identity link to it
                        var ulid = Guid.CreateVersion7().ToString("n");
                        var provisional = Activator.CreateInstance(idType)!;
                        idType.GetProperty("Id")!.SetValue(provisional, composite);
                        idType.GetProperty("System")!.SetValue(provisional, sys);
                        idType.GetProperty("Adapter")!.SetValue(provisional, adp);
                        idType.GetProperty("ExternalId")!.SetValue(provisional, ext);
                        idType.GetProperty("ReferenceId")!.SetValue(provisional, ulid);
                        idType.GetProperty("Provisional")!.SetValue(provisional, true);
                        // TTL hint via ExpiresAt; reuse intake TTL window as soft expiry for provisional
                        idType.GetProperty("ExpiresAt")!.SetValue(provisional, DateTimeOffset.UtcNow.AddDays(2));
                        var upsertM = idData.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { idType, typeof(CancellationToken) })!;
                        await (Task)upsertM.Invoke(null, new object?[] { provisional, ct })!;
                        return ulid;
                    }
                }
            }
            // Also accept contractless reserved identifier.external.* entries
            foreach (var kv in dict)
            {
                if (!kv.Key.StartsWith(Constants.Reserved.IdentifierExternalPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var ext in ToValuesFlexible(kv.Value))
                {
                    if (string.IsNullOrWhiteSpace(ext)) continue;
                    var composite = string.Join('|', sys, adp, ext);
                    var getM = idData.GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) });
                    if (getM is null) continue;
                    var task = (Task)getM.Invoke(null, new object?[] { composite, ct })!; await task.ConfigureAwait(false);
                    var link = GetTaskResult(task);
                    if (link is not null)
                    {
                        var rulid = idType.GetProperty("ReferenceId")!.GetValue(link) as string;
                        if (!string.IsNullOrWhiteSpace(rulid)) return rulid!;
                    }
                    else
                    {
                        var ulid = Guid.CreateVersion7().ToString("n");
                        var provisional = Activator.CreateInstance(idType)!;
                        idType.GetProperty("Id")!.SetValue(provisional, composite);
                        idType.GetProperty("System")!.SetValue(provisional, sys);
                        idType.GetProperty("Adapter")!.SetValue(provisional, adp);
                        idType.GetProperty("ExternalId")!.SetValue(provisional, ext);
                        idType.GetProperty("ReferenceId")!.SetValue(provisional, ulid);
                        idType.GetProperty("Provisional")!.SetValue(provisional, true);
                        idType.GetProperty("ExpiresAt")!.SetValue(provisional, DateTimeOffset.UtcNow.AddDays(2));
                        var upsertM = idData.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { idType, typeof(CancellationToken) })!;
                        await (Task)upsertM.Invoke(null, new object?[] { provisional, ct })!;
                        return ulid;
                    }
                }
            }
            return null;
        }

        private async Task SaveRejectAndDrop(string code, object evidence, object rec, Type modelType, string intakeSet, CancellationToken ct)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(evidence);
            await new Koan.Canon.Diagnostics.RejectionReport { Id = Guid.CreateVersion7().ToString("n"), ReasonCode = code, EvidenceJson = json, PolicyVersion = (string?)rec.GetType().GetProperty("PolicyVersion")?.GetValue(rec) }.Save(ct);

            var opts = _opts.CurrentValue;
            if (opts.ParkAndSweepEnabled)
            {
                var modelTypeArg = modelType;
                var parkedType = typeof(ParkedRecord<>).MakeGenericType(modelTypeArg);
                var parked = Activator.CreateInstance(parkedType)!;
                // Id carry-over for traceability
                parkedType.GetProperty("Id")!.SetValue(parked, rec.GetType().GetProperty("Id")!.GetValue(rec));
                parkedType.GetProperty("SourceId")!.SetValue(parked, rec.GetType().GetProperty("SourceId")!.GetValue(rec));
                parkedType.GetProperty("OccurredAt")!.SetValue(parked, rec.GetType().GetProperty("OccurredAt")!.GetValue(rec));
                parkedType.GetProperty("PolicyVersion")!.SetValue(parked, rec.GetType().GetProperty("PolicyVersion")!.GetValue(rec));
                parkedType.GetProperty("CorrelationId")!.SetValue(parked, rec.GetType().GetProperty("CorrelationId")!.GetValue(rec));
                parkedType.GetProperty("Data")!.SetValue(parked, rec.GetType().GetProperty("Data")!.GetValue(rec));
                parkedType.GetProperty("Source")!.SetValue(parked, rec.GetType().GetProperty("Source")!.GetValue(rec));
                parkedType.GetProperty("ReasonCode")!.SetValue(parked, code);
                // Store original evidence object (also persisted in diagnostics as JSON)
                parkedType.GetProperty("Evidence")!.SetValue(parked, evidence);

                var dataType = typeof(Data<,>).MakeGenericType(parkedType, typeof(string));
                using (DataSetContext.With(CanonSets.StageShort(CanonSets.Parked)))
                {
                    await (Task)dataType.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { parkedType, typeof(CancellationToken) })!
                        .Invoke(null, new object?[] { parked, ct })!;
                }
            }

            // Drop from intake
            var recordType = rec.GetType();
            var delGeneric = typeof(Data<,>).MakeGenericType(recordType, typeof(string))
                .GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
            await (Task)delGeneric.Invoke(null, new object?[] { (string)recordType.GetProperty("Id")!.GetValue(rec)!, intakeSet, ct })!;
        }
    }

    /// <summary>
    /// Tries to resolve a parent entity's Canonical ULID by looking up its external ID.
    /// This enables cross-system parent-child relationships where child references parent by source-specific ID.
    /// </summary>
    private static async Task<string?> TryResolveParentViaExternalId(Type parentType, string sourceSystem, string parentExternalId, CancellationToken ct)
    {
        // Build composite key for IdentityLink lookup: system|adapter|externalId
        // Using sourceSystem for both system and adapter since they come from same source
        var composite = string.Join('|', sourceSystem, sourceSystem, parentExternalId);

        var idType = typeof(Koan.Canon.Model.IdentityLink<>).MakeGenericType(parentType);
        var idData = typeof(Data<,>).MakeGenericType(idType, typeof(string));
        var getM = idData.GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) });

        if (getM is null) return null;

        var task = (Task)getM.Invoke(null, new object?[] { composite, ct })!;
        await task.ConfigureAwait(false);
        var link = GetTaskResult(task);

        if (link is not null)
        {
            // Found parent via external ID - return its Canonical ULID
            var refUlid = idType.GetProperty("ReferenceId")!.GetValue(link) as string;
            return refUlid;
        }

        // Parent not found - will need to park until parent arrives
        return null;
    }

    private static List<Type> DiscoverModels()
    {
        var result = new List<Type>();
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                var def = bt.GetGenericTypeDefinition();
                // Discover both root CanonEntity<> models and CanonValueObject<> VOs
                if (def == typeof(CanonEntity<>) || def == typeof(CanonValueObject<>))
                    result.Add(t);
                // Also discover DynamicCanonEntity<> types  
                else if (def == typeof(DynamicCanonEntity<>))
                    result.Add(t);
            }
        }
        return result;
    }

    private static object? GetTaskResult(Task t)
    {
        var type = t.GetType();
        if (type.IsGenericType)
        {
            return type.GetProperty("Result")!.GetValue(t);
        }
        return null;
    }

    /// <summary>
    /// Extracts the system name from Source metadata dictionary.
    /// </summary>
    private static string? GetSourceSystem(IDictionary<string, object?> sourceDict)
    {

        // Try multiple possible keys for system name
        string[] systemKeys = { "envelope.system", "source.system", "system" };

        foreach (var key in systemKeys)
        {
            if (sourceDict.TryGetValue(key, out var systemValue) && systemValue != null)
            {
                var systemName = systemValue.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(systemName))
                {
                    return systemName;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the source entity ID from the data dictionary for external ID generation.
    /// For strong-typed models: uses [Key] property value (typically "Id" from Entity&lt;T&gt;).
    /// For dynamic models: uses an "id" property (case-insensitive).
    /// Returns the source-specific ID (e.g., D1, S1) that is stored in identifier.external.&lt;source&gt;.
    /// </summary>
    private static string? GetSourceEntityId(IDictionary<string, object?> dict, Type modelType)
    {
        // Check if it's a DynamicCanonEntity (has IDynamicCanonEntity interface)
        var isDynamic = typeof(IDynamicCanonEntity).IsAssignableFrom(modelType);

        if (isDynamic)
        {
            // For dynamic entities, look for "id" property (case-insensitive)
            var idKey = dict.Keys.FirstOrDefault(k => string.Equals(k, "id", StringComparison.OrdinalIgnoreCase));
            if (idKey != null && dict.TryGetValue(idKey, out var idValue))
            {
                return idValue?.ToString()?.Trim();
            }
        }
        else
        {
            // For strong-typed models, find the [Key] property
            var keyProperty = modelType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .FirstOrDefault(prop => prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>(inherit: true) != null);

            if (keyProperty != null)
            {
                // Check for explicit JsonProperty attribute first
                var jsonPropertyAttr = keyProperty.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>();
                var jsonPropertyName = jsonPropertyAttr?.PropertyName;

                // If no explicit name, use camelCase conversion
                if (string.IsNullOrEmpty(jsonPropertyName))
                {
                    jsonPropertyName = char.ToLowerInvariant(keyProperty.Name[0]) + keyProperty.Name[1..];
                }

                // Try to find the key value in the dictionary
                if (dict.TryGetValue(jsonPropertyName, out var keyValue) && keyValue != null)
                {
                    return keyValue.ToString()?.Trim();
                }

                // Fallback: try original property name
                if (dict.TryGetValue(keyProperty.Name, out keyValue) && keyValue != null)
                {
                    return keyValue.ToString()?.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Creates or updates IdentityLink entries for external IDs found in Canonical projection.
    /// This ensures proper indexing for efficient external ID → ReferenceId lookups.
    /// </summary>
    private static async Task CreateOrUpdateIdentityLinks(
        Type modelType,
        string? refUlid,
        Dictionary<string, List<string?>> Canonical,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(refUlid)) return;

        var idType = typeof(Koan.Canon.Model.IdentityLink<>).MakeGenericType(modelType);
        var idData = typeof(Data<,>).MakeGenericType(idType, typeof(string));
        var getAsync = idData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) });
        var upsertAsync = idData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { idType, typeof(CancellationToken) });

        if (getAsync == null || upsertAsync == null) return;

        // Process all identifier.external.* keys in the Canonical projection
        foreach (var kv in Canonical.Where(x => x.Key.StartsWith(Constants.Reserved.IdentifierExternalPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var systemName = kv.Key.Substring(Constants.Reserved.IdentifierExternalPrefix.Length);
            if (string.IsNullOrWhiteSpace(systemName)) continue;

            foreach (var externalId in kv.Value.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (externalId == null) continue;

                // Create composite key: system|adapter|externalId (using system for both system and adapter)
                var compositeId = string.Join('|', systemName, systemName, externalId);

                // Check if IdentityLink already exists
                var getTask = (Task)getAsync.Invoke(null, new object?[] { compositeId, stoppingToken })!;
                await getTask.ConfigureAwait(false);
                var existingLink = GetTaskResult(getTask);

                if (existingLink == null)
                {
                    // Create new IdentityLink
                    var newLink = Activator.CreateInstance(idType)!;
                    idType.GetProperty("Id")!.SetValue(newLink, compositeId);
                    idType.GetProperty("System")!.SetValue(newLink, systemName);
                    idType.GetProperty("Adapter")!.SetValue(newLink, systemName);
                    idType.GetProperty("ExternalId")!.SetValue(newLink, externalId);
                    idType.GetProperty("ReferenceId")!.SetValue(newLink, refUlid);
                    idType.GetProperty("Provisional")!.SetValue(newLink, false); // Non-provisional since it comes from Canonical projection
                    idType.GetProperty("CreatedAt")!.SetValue(newLink, DateTimeOffset.UtcNow);

                    // Set expiration for cleanup (optional - could be configurable)
                    idType.GetProperty("ExpiresAt")!.SetValue(newLink, DateTimeOffset.UtcNow.AddDays(365));

                    await (Task)upsertAsync.Invoke(null, new object?[] { newLink, stoppingToken })!;
                }
                else
                {
                    // Update existing link if ReferenceId has changed
                    var currentRefUlid = (string?)idType.GetProperty("ReferenceId")!.GetValue(existingLink);
                    if (!string.Equals(currentRefUlid, refUlid, StringComparison.Ordinal))
                    {
                        idType.GetProperty("ReferenceId")!.SetValue(existingLink, refUlid);
                        idType.GetProperty("Provisional")!.SetValue(existingLink, false);

                        await (Task)upsertAsync.Invoke(null, new object?[] { existingLink, stoppingToken })!;
                    }
                }
            }
        }
    }

    private static List<string> ToValuesFlexible(object? raw)
    {
        switch (raw)
        {
            case null: return new List<string>();
            case string s when !string.IsNullOrWhiteSpace(s): return new List<string> { s };
            case Newtonsoft.Json.Linq.JValue jv:
                var sv = jv.ToObject<object?>()?.ToString();
                return string.IsNullOrWhiteSpace(sv) ? new List<string>() : new List<string> { sv! };
            case Newtonsoft.Json.Linq.JArray ja:
                return ja.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
            case IEnumerable<object> arr:
                return arr.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
            default:
                return new List<string> { raw.ToString() ?? string.Empty };
        }
    }
    // Legacy workers removed

    /// <summary>
    /// Checks if there are any USER-DEFINED classes with the [CanonOrchestrator] attribute in the current application domain.
    /// Background workers should only run in orchestrator services, not in lightweight adapters.
    /// The DefaultCanonOrchestrator does not count as it's an internal fallback.
    /// </summary>
    private static bool HasCanonOrchestrators()
    {
        try
        {
            var orchestratorAttributeType = typeof(Koan.Canon.Attributes.CanonOrchestratorAttribute);
            var defaultOrchestratorType = typeof(Koan.Canon.Core.Orchestration.DefaultCanonOrchestrator);

            // Check all loaded assemblies for classes with [CanonOrchestrator]
            // Use cached assemblies instead of bespoke AppDomain scanning
            foreach (var assembly in AssemblyCache.Instance.GetAllAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsClass && !type.IsAbstract &&
                            type != defaultOrchestratorType &&  // Exclude the internal DefaultCanonOrchestrator
                            type.GetCustomAttributes(orchestratorAttributeType, inherit: true).Any())
                        {
                            return true;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                    continue;
                }
            }

            return false;
        }
        catch
        {
            // If detection fails, default to not registering workers (safer for adapters)
            return false;
        }
    }

    /// <summary>
    /// Registers optimized Canon services with batch processing and parallel execution.
    /// Use this instead of AddKoanCanon for high-performance scenarios.
    /// </summary>
    public static IServiceCollection AddKoanCanonOptimized(this IServiceCollection services)
    {
        // Add base Canon services first
        services.AddKoanCanon();

        // Register optimized services and infrastructure
        // BatchDataAccessHelper is static, no registration needed
        services.TryAddSingleton<Koan.Canon.Core.Services.AdaptiveBatchProcessor>();
        services.TryAddSingleton<Koan.Canon.Core.Monitoring.CanonPerformanceMonitor>();

        // Replace workers with optimized versions if orchestrators are present
        if (HasCanonOrchestrators())
        {
            // Remove original workers and add optimized versions
            var originalAssociationWorker = services.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "ModelAssociationWorkerHostedService");
            if (originalAssociationWorker != null)
            {
                services.Remove(originalAssociationWorker);
            }

            var originalProjectionWorker = services.FirstOrDefault(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "ModelProjectionWorkerHostedService");
            if (originalProjectionWorker != null)
            {
                services.Remove(originalProjectionWorker);
            }

            // Add optimized workers
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Koan.Canon.Core.Services.OptimizedModelAssociationWorker>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Koan.Canon.Core.Services.OptimizedModelProjectionWorker>());

            // Start performance monitoring
            services.AddSingleton<IHostedService>(provider =>
            {
                var monitor = provider.GetRequiredService<Koan.Canon.Core.Monitoring.CanonPerformanceMonitor>();
                monitor.StartPerformanceReporting(TimeSpan.FromMinutes(5));
                return new PerformanceMonitoringService(monitor);
            });
        }

        return services;
    }

    /// <summary>
    /// Extension method to add Canon optimization configuration with feature flags.
    /// </summary>
    public static IServiceCollection AddKoanCanonWithOptimizations(
        this IServiceCollection services,
        Action<CanonOptimizationOptions>? configure = null)
    {
        var options = new CanonOptimizationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Register base Canon services
        services.AddKoanCanon();

        // Register optimization infrastructure
        if (options.Features.EnableBatchOperations)
        {
            // BatchDataAccessHelper is static, no registration needed
        }

        if (options.Features.EnableAdaptiveBatching)
        {
            services.TryAddSingleton<Koan.Canon.Core.Services.AdaptiveBatchProcessor>();
        }

        if (options.Features.EnablePerformanceMonitoring)
        {
            services.TryAddSingleton<Koan.Canon.Core.Monitoring.CanonPerformanceMonitor>();
        }

        // Register optimized workers based on feature flags
        if (HasCanonOrchestrators() && options.Features.EnableParallelProcessing)
        {
            if (options.Features.UseOptimizedAssociationWorker)
            {
                // Remove original and add optimized
                var originalAssociationWorker = services.FirstOrDefault(s =>
                    s.ServiceType == typeof(IHostedService) &&
                    s.ImplementationType?.Name == "ModelAssociationWorkerHostedService");
                if (originalAssociationWorker != null)
                {
                    services.Remove(originalAssociationWorker);
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Koan.Canon.Core.Services.OptimizedModelAssociationWorker>());
                }
            }

            if (options.Features.UseOptimizedProjectionWorker)
            {
                // Remove original and add optimized
                var originalProjectionWorker = services.FirstOrDefault(s =>
                    s.ServiceType == typeof(IHostedService) &&
                    s.ImplementationType?.Name == "ModelProjectionWorkerHostedService");
                if (originalProjectionWorker != null)
                {
                    services.Remove(originalProjectionWorker);
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Koan.Canon.Core.Services.OptimizedModelProjectionWorker>());
                }
            }
        }

        return services;
    }
}

/// <summary>
/// Configuration options for Canon optimizations with feature flags.
/// </summary>
public class CanonOptimizationOptions
{
    public FeatureFlags Features { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();

    public class FeatureFlags
    {
        public bool EnableBatchOperations { get; set; } = true;
        public bool EnableParallelProcessing { get; set; } = true;
        public bool EnableAdaptiveBatching { get; set; } = true;
        public bool EnablePerformanceMonitoring { get; set; } = true;

        // Worker replacement flags
        public bool UseOptimizedAssociationWorker { get; set; } = true;
        public bool UseOptimizedProjectionWorker { get; set; } = true;
    }

    public class PerformanceSettings
    {
        public int DefaultBatchSize { get; set; } = 1000;
        public int MaxBatchSize { get; set; } = 5000;
        public int MinBatchSize { get; set; } = 50;
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromMinutes(5);
    }
}

/// <summary>
/// Hosted service wrapper for performance monitoring.
/// </summary>
internal class PerformanceMonitoringService : IHostedService
{
    private readonly Koan.Canon.Core.Monitoring.CanonPerformanceMonitor _monitor;

    public PerformanceMonitoringService(Koan.Canon.Core.Monitoring.CanonPerformanceMonitor monitor)
    {
        _monitor = monitor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _monitor.StartPerformanceReporting(TimeSpan.FromMinutes(5), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}




