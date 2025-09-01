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
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Flow.Diagnostics;
using Sora.Flow.Options;
using Sora.Flow.Runtime;
using Sora.Data.Core.Naming;
using System.Reflection;
using Sora.Flow.Materialization;
using Sora.Core.Json;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using Sora.Core.Utilities.Ids;
using Sora.Flow.Monitoring;
using Sora.Messaging;
using Sora.Flow.Actions;

namespace Sora.Flow;

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
        if (payload is Newtonsoft.Json.Linq.JObject jo)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in jo.Properties())
            {
                var val = prop.Value;
                if (val is Newtonsoft.Json.Linq.JArray ja)
                    dict[prop.Name] = ja.ToList<object?>();
                else if (val is Newtonsoft.Json.Linq.JValue jv)
                    dict[prop.Name] = jv.ToObject<object?>();
                else
                    dict[prop.Name] = val.ToString();
            }
            return dict;
        }
        return null;
    }

    // Shared tiny IO retry helper for transient file sharing errors (JSON dev adapter)
    internal static async Task RetryIoAsync(Func<Task> action, CancellationToken ct)
    {
        var attempts = 0;
        while (true)
        {
            try { await action(); return; }
            catch (IOException) when (attempts < 3)
            {
                attempts++;
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempts), ct);
            }
        }
    }
    public static IServiceCollection AddSoraFlow(this IServiceCollection services)
    {
        services.AddSoraOptions<FlowOptions>("Sora:Flow");
    services.AddSoraOptions<FlowMaterializationOptions>("Sora:Flow:Materialization");
        services.TryAddSingleton<IFlowRuntime, InMemoryFlowRuntime>();
    services.TryAddSingleton<IFlowMaterializer, FlowMaterializer>();

    // Naming: prefer model-qualified base names for typed Flow collections (e.g., "S8.Flow.Shared.Device#flow.intake").
    // This avoids unwieldy generic type names like "Sora.Flow.Model.StageRecord`1[[...]]#...".
    services.OverrideStorageNaming((entityType, defaults) => TryResolveFlowModelQualifiedName(entityType));

        // Bootstrap (indexes/TTLs) via initializer; no-ops by default; providers can extend.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(new FlowBootstrapInitializer()));

        // Ensure schemas exist at startup (idempotent). Providers that don't support instructions will no-op.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowSchemaEnsureHostedService>());

    // Legacy workers removed in greenfield runtime

    // vNext: per-model runtime (typed) — processes flow.{model}.** sets
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModelProjectionWorkerHostedService>());
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModelAssociationWorkerHostedService>());

        // TTL purge
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowPurgeHostedService>());

    // DX: default action sender and responder
    // Sender (IFlowActions) for apps to publish actions easily
    services.AddFlowActions();
    // Responder to handle FlowAction (seed/report/ping) with generic defaults
    services.TryAddSingleton<Sora.Messaging.IMessageHandler<Sora.Flow.Actions.FlowAction>, Sora.Flow.Actions.FlowActionHandler>();
        return services;
    }

    // Registration helpers for monitor hooks
    public static IServiceCollection AddFlowMonitor<TMonitor>(this IServiceCollection services)
        where TMonitor : class, IFlowMonitor
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowMonitor, TMonitor>());
        return services;
    }

    public static IServiceCollection AddFlowMonitor<TModel, TMonitor>(this IServiceCollection services)
        where TMonitor : class, IFlowMonitor<TModel>
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowMonitor<TModel>, TMonitor>());
        return services;
    }

    private static string? TryResolveFlowModelQualifiedName(Type entityType)
    {
        if (!entityType.IsGenericType) return null;
        var def = entityType.GetGenericTypeDefinition();
        // Resolve the model type's full name (normalized) once
        string ModelBaseName()
        {
            var model = entityType.GetGenericArguments()[0];
            var full = model.FullName ?? model.Name;
            return full.Replace('+', '.');
        }

    // Stage/parked records and views: keep base as the model name, sets supply the suffix
    if (def == typeof(StageRecord<>) || def == typeof(ParkedRecord<>) || def == typeof(CanonicalProjection<>) || def == typeof(LineageProjection<>))
            return ModelBaseName();

        // Root-scoped typed entities (no set provided): bake a type-specific suffix into the base
    if (def == typeof(KeyIndex<>)) return ModelBaseName() + "#flow.index";
        if (def == typeof(ReferenceItem<>)) return ModelBaseName() + "#flow.refs";
    if (def == typeof(ProjectionTask<>)) return ModelBaseName() + "#flow.tasks";
    if (def == typeof(PolicyState<>)) return ModelBaseName() + "#flow.policies";
    if (def == typeof(Sora.Flow.Model.IdentityLink<>)) return ModelBaseName() + "#flow.identity";
        // Dynamic root entity should use the pure model name as the base (no suffix)
        if (def == typeof(DynamicFlowEntity<>)) return ModelBaseName();

        return null;
    }

    private sealed class FlowBootstrapInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services) { }
    }

    internal sealed class FlowSchemaEnsureHostedService : IHostedService
    {
        private readonly IServiceProvider _sp;
        public FlowSchemaEnsureHostedService(IServiceProvider sp) => _sp = sp;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _sp.CreateScope();
            var data = scope.ServiceProvider.GetService<IDataService>();
            if (data is null) return;

            // no legacy ensures — typed ensures are handled below

            // Greenfield: no legacy ensures

            // vNext per-model sets and entities (discover models dynamically)
            // ensure assembly is loaded (no-op) and discover models via reflection
            var byName = new Dictionary<string, Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
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
                    if (bt.GetGenericTypeDefinition() != typeof(FlowEntity<>)) continue;
                    var modelName = Infrastructure.FlowRegistry.GetModelName(t);
                    byName[modelName] = t;
                }
            }

            foreach (var kv in byName)
            {
                    var modelType = kv.Value;
                    // Ensure stage sets
                    foreach (var stage in new[] { FlowSets.Intake, FlowSets.Standardized, FlowSets.Keyed })
                    {
                        var set = FlowSets.StageShort(stage);
                        var generic = typeof(StageRecord<>).MakeGenericType(modelType);
                        var method = typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                            .MakeGenericMethod(generic);
                        await (Task)method.Invoke(null, new object?[] { data, set, cancellationToken })!;
                    }
                    // Ensure typed indexes/entities
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(KeyIndex<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(ReferenceItem<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(ProjectionTask<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(CanonicalProjection<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, FlowSets.ViewShort(Constants.Views.Canonical), cancellationToken })!;
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(LineageProjection<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, FlowSets.ViewShort(Constants.Views.Lineage), cancellationToken })!;

                    // Root entities: dynamic materialized snapshot and per-reference policy state
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(DynamicFlowEntity<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(PolicyState<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;
                    // Identity links
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(Sora.Flow.Model.IdentityLink<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, null, cancellationToken })!;

                    // Parked records set
                    await (Task)typeof(FlowSchemaEnsureHostedService).GetMethod(nameof(EnsureCreatedFor), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(ParkedRecord<>).MakeGenericType(modelType))
                        .Invoke(null, new object?[] { data, FlowSets.StageShort(FlowSets.Parked), cancellationToken })!;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static async Task EnsureCreatedFor<TEntity>(IDataService d, string? set, CancellationToken ct) where TEntity : class
        {
            using var _set = DataSetContext.With(set);
            try { await d.Execute<TEntity, bool>(new Instruction(DataInstructions.EnsureCreated), ct); }
            catch { }
        }
    }

    

    internal sealed class FlowPurgeHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<FlowOptions> _opts;
        private readonly ILogger<FlowPurgeHostedService> _log;
        public FlowPurgeHostedService(IServiceProvider sp, IOptionsMonitor<FlowOptions> opts, ILogger<FlowPurgeHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
            while (!stoppingToken.IsCancellationRequested)
            {
                var opts = _opts.CurrentValue;
                if (!opts.PurgeEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }
                try
                {
                    using var scope = _sp.CreateScope();
                    var now = DateTimeOffset.UtcNow;
                    var cutoffIntake = now - opts.IntakeTtl;
                    var cutoffStd = now - opts.StandardizedTtl;
                    var cutoffKeyed = now - opts.KeyedTtl;
                    var cutoffTask = now - opts.ProjectionTaskTtl;
                    var cutoffReject = now - opts.RejectionReportTtl;
                    var cutoffParked = now - opts.ParkedTtl;

                    // Purge typed records and tasks for each discovered model
                    foreach (var modelType in DiscoverModels())
                    {
                        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
                        var projTaskType = typeof(ProjectionTask<>).MakeGenericType(modelType);

                        // intake/standardized/keyed
                        // Page through sets and delete older than TTLs (provider-agnostic)
                        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
                        {
                            var pageM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                            var delM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                            var pageTask = (Task)pageM.Invoke(null, new object?[] { 500, stoppingToken })!; await pageTask.ConfigureAwait(false);
                            var items = (System.Collections.IEnumerable)GetTaskResult(pageTask)!;
                            foreach (var it in items)
                            {
                                var ts = (DateTimeOffset)it.GetType().GetProperty("OccurredAt")!.GetValue(it)!;
                                if (ts < cutoffIntake)
                                {
                                    await (Task)delM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, FlowSets.StageShort(FlowSets.Intake), stoppingToken })!;
                                }
                            }
                        }
                        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Standardized)))
                        {
                            var pageM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                            var delM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                            var pageTask = (Task)pageM.Invoke(null, new object?[] { 500, stoppingToken })!; await pageTask.ConfigureAwait(false);
                            var items = (System.Collections.IEnumerable)GetTaskResult(pageTask)!;
                            foreach (var it in items)
                            {
                                var ts = (DateTimeOffset)it.GetType().GetProperty("OccurredAt")!.GetValue(it)!;
                                if (ts < cutoffStd)
                                {
                                    await (Task)delM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, FlowSets.StageShort(FlowSets.Standardized), stoppingToken })!;
                                }
                            }
                        }
                        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
                        {
                            var pageM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                            var delM = typeof(Data<,>).MakeGenericType(recordType, typeof(string)).GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                            var pageTask = (Task)pageM.Invoke(null, new object?[] { 500, stoppingToken })!; await pageTask.ConfigureAwait(false);
                            var items = (System.Collections.IEnumerable)GetTaskResult(pageTask)!;
                            foreach (var it in items)
                            {
                                var ts = (DateTimeOffset)it.GetType().GetProperty("OccurredAt")!.GetValue(it)!;
                                if (ts < cutoffKeyed)
                                {
                                    await (Task)delM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, FlowSets.StageShort(FlowSets.Keyed), stoppingToken })!;
                                }
                            }
                        }

                        // projection tasks TTL
                        // Fallback: page then delete by CreatedAt
                        var pageTasksM = typeof(Data<,>).MakeGenericType(projTaskType, typeof(string)).GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
                        var delTaskM = typeof(Data<,>).MakeGenericType(projTaskType, typeof(string)).GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                        using (DataSetContext.With(null))
                        {
                            var pageTask2 = (Task)pageTasksM.Invoke(null, new object?[] { 500, stoppingToken })!; await pageTask2.ConfigureAwait(false);
                            var titems = (System.Collections.IEnumerable)GetTaskResult(pageTask2)!;
                            foreach (var it in titems)
                            {
                                var ts = (DateTimeOffset)it.GetType().GetProperty("CreatedAt")!.GetValue(it)!;
                                if (ts < cutoffTask)
                                {
                                    await (Task)delTaskM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, null!, stoppingToken })!;
                                }
                            }
                        }
                    }

                    // Purge diagnostics
                    await Data<RejectionReport, string>.Delete(r => r.CreatedAt < cutoffReject, set: null!, stoppingToken);

                    // Purge parked records for each model
                    foreach (var modelType in DiscoverModels())
                    {
                        var parkedType = typeof(ParkedRecord<>).MakeGenericType(modelType);
                        var parkedData = typeof(Data<,>).MakeGenericType(parkedType, typeof(string));
                        var pageM = parkedData.GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) });
                        var delM = parkedData.GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                        if (pageM is not null && delM is not null)
                        {
                            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
                            {
                                var t = (Task)pageM.Invoke(null, new object?[] { 500, stoppingToken })!; await t.ConfigureAwait(false);
                                var items = ((System.Collections.IEnumerable)GetTaskResult(t)!).Cast<object>().ToList();
                                foreach (var it in items)
                                {
                                    var ts = (DateTimeOffset)it.GetType().GetProperty("OccurredAt")!.GetValue(it)!;
                                    if (ts < cutoffParked)
                                    {
                                        await (Task)delM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, FlowSets.StageShort(FlowSets.Parked), stoppingToken })!;
                                    }
                                }
                            }
                        }
                    }

                    // Purge expired provisional identity links for all models
                    foreach (var modelType in DiscoverModels())
                    {
                        var idType = typeof(Sora.Flow.Model.IdentityLink<>).MakeGenericType(modelType);
                        var idData = typeof(Data<,>).MakeGenericType(idType, typeof(string));
                        var queryDel = idData.GetMethod("Delete", BindingFlags.Public | BindingFlags.Static, new[] { typeof(Func<,>).MakeGenericType(idType, typeof(bool)), typeof(string), typeof(CancellationToken) });
                        if (queryDel is not null)
                        {
                            // Fallback: page and delete since static Delete with predicate may not be available for all adapters
                        }
                        // Safer generic: FirstPage then filter
                        var pageM = idData.GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) });
                        if (pageM is not null)
                        {
                            int fetched;
                            do
                            {
                                var t = (Task)pageM.Invoke(null, new object?[] { 500, stoppingToken })!; await t.ConfigureAwait(false);
                                var items = ((System.Collections.IEnumerable)GetTaskResult(t)!).Cast<object>().ToList();
                                fetched = items.Count;
                                foreach (var it in items)
                                {
                                    var prov = (bool)(it.GetType().GetProperty("Provisional")?.GetValue(it) ?? false);
                                    var exp = (DateTimeOffset?)it.GetType().GetProperty("ExpiresAt")?.GetValue(it);
                                    if (prov && exp is DateTimeOffset e && e < now)
                                    {
                                        var delM = idData.GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                                        await (Task)delM.Invoke(null, new object?[] { (string)it.GetType().GetProperty("Id")!.GetValue(it)!, stoppingToken })!;
                                    }
                                }
                            } while (fetched == 500);
                        }
                    }

                    _log.LogDebug("Sora.Flow purge completed");
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Sora.Flow purge run failed (will retry later)");
                }

                try { await Task.Delay(_opts.CurrentValue.PurgeInterval, stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }
    }

    // vNext model-aware projection worker
    internal sealed class ModelProjectionWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<FlowOptions> _opts;
        private readonly ILogger<ModelProjectionWorkerHostedService> _log;

        public ModelProjectionWorkerHostedService(IServiceProvider sp, IOptionsMonitor<FlowOptions> opts, ILogger<ModelProjectionWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    var models = DiscoverModels();
                    foreach (var modelType in models)
                    {
                        var taskType = typeof(ProjectionTask<>).MakeGenericType(modelType);
                        // Static generics on Entity<T> are not discoverable via derived type; call through Data<,>
                        var pageMethod = typeof(Data<,>).MakeGenericType(taskType, typeof(string))
                            .GetMethod("Page", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(int), typeof(CancellationToken) });
                        if (pageMethod is null) continue;
                        int pageNum = 1;
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            var task = (Task)pageMethod.Invoke(null, new object?[] { pageNum, batch, stoppingToken })!;
                            await task.ConfigureAwait(false);
                            var enumerable = (System.Collections.IEnumerable)GetTaskResult(task)!;
                            var tasks = enumerable.Cast<object>().ToList();
                            if (tasks.Count == 0) break;

                            foreach (var t in tasks)
                            {
                                var refUlid = t.GetType().GetProperty("ReferenceUlid")?.GetValue(t) as string;
                                // ULID is the authoritative key for view/doc ids
                                var refId = refUlid ?? string.Empty;
                                var canonicalId = t.GetType().GetProperty("CanonicalId")?.GetValue(t) as string;
                                // keyed stage for this model
                                var keyedSet = FlowSets.StageShort(FlowSets.Keyed);
                                var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
                                using (DataSetContext.With(keyedSet))
                                {
                                    var allMethod = typeof(Data<,>).MakeGenericType(recordType, typeof(string))
                                        .GetMethod("All", BindingFlags.Public | BindingFlags.Static, new[] { typeof(CancellationToken) });
                                    var allTask = (Task)allMethod!.Invoke(null, new object?[] { stoppingToken })!;
                                    await allTask.ConfigureAwait(false);
                                    var allEnum = (System.Collections.IEnumerable)GetTaskResult(allTask)!;
                                    var all = new List<object>();
                                    foreach (var r in allEnum.Cast<object>())
                                    {
                                        var rulid = r.GetType().GetProperty("ReferenceUlid")?.GetValue(r) as string;
                                        bool match = !string.IsNullOrWhiteSpace(refUlid)
                                            ? string.Equals(rulid, refUlid, StringComparison.Ordinal)
                                            : false;
                                        if (match) all.Add(r);
                                    }

                                    // canonical (range structure) and lineage
                                    var canonical = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
                                    var lineage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
                                    // Optional: exclude tag prefixes from canonical/lineage (e.g., "reading.")
                                    var exclude = (_opts.CurrentValue.CanonicalExcludeTagPrefixes ?? Array.Empty<string>())
                                        .Where(p => !string.IsNullOrWhiteSpace(p))
                                        .Select(p => p.Trim())
                                        .ToArray();

                                    foreach (var r in all)
                                    {
                                        var src = (string)(r.GetType().GetProperty("SourceId")!.GetValue(r) ?? "unknown");
                                        var payload = r.GetType().GetProperty("StagePayload")!.GetValue(r);
                                        var dict = ExtractDict(payload);
                                        if (dict is null) continue;
                                        foreach (var kv in dict)
                                        {
                                            var tag = kv.Key;
                                            if (exclude.Length > 0 && exclude.Any(p => tag.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                                                continue;
                                            var values = ToValuesFlexible(kv.Value);
                                            if (values.Count == 0) continue;
                                            if (!canonical.TryGetValue(tag, out var list)) { list = new List<string?>(); canonical[tag] = list; }
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

                                    // Build nested canonical object with range arrays per dotted path
                                    var ranges = new Dictionary<string, Newtonsoft.Json.Linq.JToken?>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var kv in canonical)
                                    {
                                        var dedup = kv.Value.Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                                        ranges[kv.Key] = new JArray(dedup);
                                    }
                                    var canonicalExpanded = JsonPathMapper.Expand(ranges);
                                    // Convert to provider-safe nested object (Expando/primitives/arrays)
                                    static object? Plainify(object? token)
                                    {
                                        if (token is null) return null;
                                        if (token is Newtonsoft.Json.Linq.JValue jv) return jv.Value;
                                        if (token is Newtonsoft.Json.Linq.JArray ja) return ja.Select(t => Plainify(t)).ToList();
                                        if (token is Newtonsoft.Json.Linq.JObject jo)
                                        {
                                            IDictionary<string, object?> exp = new ExpandoObject();
                                            foreach (var p in jo.Properties()) exp[p.Name] = Plainify(p.Value);
                                            return (ExpandoObject)exp;
                                        }
                                        return token;
                                    }
                                    var canonicalView = Plainify(canonicalExpanded);
                                    var lineageView = lineage.ToDictionary(
                                        kv => kv.Key,
                                        kv => kv.Value.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                                        StringComparer.OrdinalIgnoreCase);

                                    var canType = typeof(CanonicalProjection<>).MakeGenericType(modelType);
                                    var canDoc = Activator.CreateInstance(canType)!;
                                    canType.GetProperty("Id")!.SetValue(canDoc, $"{Constants.Views.Canonical}::{refId}");
                                    // legacy ReferenceId removed; identifiers are CanonicalId and ReferenceUlid
                                    // Populate identifier fields when available
                                    if (!string.IsNullOrWhiteSpace(canonicalId)) canType.GetProperty("CanonicalId")?.SetValue(canDoc, canonicalId);
                                    if (!string.IsNullOrWhiteSpace(refUlid)) canType.GetProperty("ReferenceUlid")?.SetValue(canDoc, refUlid);
                                    canType.GetProperty("ViewName")!.SetValue(canDoc, Constants.Views.Canonical);
                                    // Canonical now publishes nested ranges under Model
                                    var modelProp = canType.GetProperty("Model") ?? canType.GetProperty("View");
                                    modelProp!.SetValue(canDoc, canonicalView);
                                    var canData = typeof(Data<,>).MakeGenericType(canType, typeof(string));
                                    var upsertSet = canData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { canType, typeof(string), typeof(CancellationToken) })!;
                                    await (Task)upsertSet.Invoke(null, new object?[] { canDoc, FlowSets.ViewShort(Constants.Views.Canonical), stoppingToken })!;

                                    var linType = typeof(LineageProjection<>).MakeGenericType(modelType);
                                    var linDoc = Activator.CreateInstance(linType)!;
                                    linType.GetProperty("Id")!.SetValue(linDoc, $"{Constants.Views.Lineage}::{refId}");
                                    // legacy ReferenceId removed; identifiers are CanonicalId and ReferenceUlid
                                    if (!string.IsNullOrWhiteSpace(canonicalId)) linType.GetProperty("CanonicalId")?.SetValue(linDoc, canonicalId);
                                    if (!string.IsNullOrWhiteSpace(refUlid)) linType.GetProperty("ReferenceUlid")?.SetValue(linDoc, refUlid);
                                    linType.GetProperty("ViewName")!.SetValue(linDoc, Constants.Views.Lineage);
                                    linType.GetProperty("View")!.SetValue(linDoc, lineageView);
                                    var linData = typeof(Data<,>).MakeGenericType(linType, typeof(string));
                                    await (Task)linData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { linType, typeof(string), typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { linDoc, FlowSets.ViewShort(Constants.Views.Lineage), stoppingToken })!;

                                    // Materialized snapshot via policy engine → give monitors a chance to adjust before commit
                                    var materializer = _sp.GetRequiredService<IFlowMaterializer>();
                                    // preserve insertion order for paths
                                    var ordered = new Dictionary<string, IReadOnlyCollection<string?>>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var kv in canonical)
                                    {
                                        ordered[kv.Key] = kv.Value.AsReadOnly();
                                    }
                                    var modelName = Infrastructure.FlowRegistry.GetModelName(modelType);
                                    var (materializedValues, materializedPolicies) = await materializer.MaterializeAsync(modelName, ordered, stoppingToken);
                                    // Build a mutable model dictionary from materialized dotted values (flat) → expanded below to nested
                                    var mutableModel = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var kvp in materializedValues) mutableModel[kvp.Key] = kvp.Value;
                                    var mutablePolicies = new Dictionary<string, string>(materializedPolicies, StringComparer.OrdinalIgnoreCase);
                                    // Invoke typed monitors first, then untyped
                                    var monitorsObj = _sp.GetServices(typeof(IFlowMonitor));
                                    var typedMonitorType = typeof(IFlowMonitor<>).MakeGenericType(modelType);
                                    var typedMonitors = _sp.GetServices(typedMonitorType);
                                    var ctx = new Sora.Flow.Monitoring.FlowMonitorContext(modelName, refId, mutableModel, mutablePolicies);
                                    foreach (var tm in typedMonitors)
                                    {
                                        var m = typedMonitorType.GetMethod("OnProjectedAsync")!;
                                        var monitorTask = (Task)m.Invoke(tm, new object?[] { ctx, stoppingToken })!; await monitorTask.ConfigureAwait(false);
                                    }
                                    foreach (var um in monitorsObj)
                                    {
                                        var m = typeof(IFlowMonitor).GetMethod("OnProjectedAsync")!;
                                        var monitorTask2 = (Task)m.Invoke(um, new object?[] { modelType, ctx, stoppingToken })!; await monitorTask2.ConfigureAwait(false);
                                    }
                                    // Build nested JSON object from dotted paths
                                    var pathMap = new Dictionary<string, JToken?>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var kvp in mutableModel)
                                    {
                                        pathMap[kvp.Key] = kvp.Value is null ? JValue.CreateNull() : new JValue(kvp.Value);
                                    }
                                    var nested = JsonPathMapper.Expand(pathMap);
                                    // Convert JObject to plain dictionary recursively for provider-safe serialization
                                    static object? ToPlain(object? token)
                                    {
                                        if (token is null) return null;
                                        if (token is JValue jv) return jv.Value;
                                        if (token is JArray ja) return ja.Select(t => ToPlain(t)).ToList();
                                        if (token is JObject jo)
                                        {
                                            IDictionary<string, object?> exp = new ExpandoObject();
                                            foreach (var p in jo.Properties()) exp[p.Name] = ToPlain(p.Value);
                                            return (ExpandoObject)exp;
                                        }
                                        return token;
                                    }
                                    var plain = ToPlain(nested) as ExpandoObject;
                                    // Upsert dynamic root entity and policy state in ROOT scope (no set)
                                    using (DataSetContext.With(null))
                                    {
                                        var dynType = typeof(DynamicFlowEntity<>).MakeGenericType(modelType);
                                        var dyn = Activator.CreateInstance(dynType)!;
                                        dynType.GetProperty("Id")!.SetValue(dyn, refId);
                                        // no legacy ReferenceId on root; Id carries ULID. CanonicalId populated below.
                                        if (!string.IsNullOrWhiteSpace(canonicalId)) dynType.GetProperty("CanonicalId")?.SetValue(dyn, canonicalId);
                                        if (!string.IsNullOrWhiteSpace(refUlid)) dynType.GetProperty("ReferenceUlid")?.SetValue(dyn, refUlid);
                                        // Root materialized snapshot stored under Model (renamed from Data)
                                        var dynModelProp = dynType.GetProperty("Model") ?? dynType.GetProperty("Data");
                                        dynModelProp!.SetValue(dyn, plain);
                                        var dynData = typeof(Data<,>).MakeGenericType(dynType, typeof(string));
                                        await (Task)dynData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { dynType, typeof(CancellationToken) })!
                                            .Invoke(null, new object?[] { dyn, stoppingToken })!;
                                        // Upsert policy state
                                        var polType = typeof(PolicyState<>).MakeGenericType(modelType);
                                        var pol = Activator.CreateInstance(polType)!;
                                        polType.GetProperty("Id")!.SetValue(pol, refId);
                                        // Policy state now stores ReferenceUlid instead of legacy ReferenceId
                                        polType.GetProperty("ReferenceUlid")!.SetValue(pol, refId);
                                        polType.GetProperty("Policies")!.SetValue(pol, new Dictionary<string, string>(mutablePolicies, StringComparer.OrdinalIgnoreCase));
                                        var polData = typeof(Data<,>).MakeGenericType(polType, typeof(string));
                                        await (Task)polData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { polType, typeof(CancellationToken) })!
                                            .Invoke(null, new object?[] { pol, stoppingToken })!;
                                    }
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
                { _log.LogDebug(ex, "ModelProjectionWorker iteration failed (will retry)"); }

                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }
    }

    // vNext model-aware association worker
    internal sealed class ModelAssociationWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<FlowOptions> _opts;
        private readonly ILogger<ModelAssociationWorkerHostedService> _log;

        public ModelAssociationWorkerHostedService(IServiceProvider sp, IOptionsMonitor<FlowOptions> opts, ILogger<ModelAssociationWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Greenfield: no legacy flags; always active
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    var models = DiscoverModels();
                    foreach (var modelType in models)
                    {
                        var intakeSet = FlowSets.StageShort(FlowSets.Intake);
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
                        if (page.Count == 0) continue;

                        var tags = Infrastructure.FlowRegistry.GetAggregationTags(modelType);
                        if (tags.Length == 0) tags = _opts.CurrentValue.AggregationTags ?? Array.Empty<string>();
                        foreach (var rec in page)
                        {
                            using var _root = DataSetContext.With(null);
                            var dict = ExtractDict(rec.GetType().GetProperty("StagePayload")!.GetValue(rec));
                            if (dict is null || tags.Length == 0)
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = dict is null ? "no-payload" : "no-config-tags", tags }, rec, modelType, intakeSet, stoppingToken);
                                continue;
                            }

                            var candidates = new List<(string tag, string value)>();
                            string? canonicalId = null;
                            foreach (var tag in tags)
                            {
                                if (!dict.TryGetValue(tag, out var raw)) continue;
                                foreach (var v in ToValuesFlexible(raw))
                                {
                                    if (!string.IsNullOrWhiteSpace(v))
                                    {
                                        candidates.Add((tag, v));
                                        // Heuristic: prefer the first aggregation key as CanonicalId if none explicit
                                        canonicalId ??= v;
                                    }
                                }
                            }
                            // Optional composite candidate: system|adapter|externalId for safer ownership, when present
                            if (dict.TryGetValue(Constants.Envelope.System, out var sys) &&
                                dict.TryGetValue(Constants.Envelope.Adapter, out var adp))
                            {
                                // Discover external-id field names from [EntityLink] metadata
                                var extKeys = Infrastructure.FlowRegistry.GetExternalIdKeys(modelType);
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
                            }
                            if (candidates.Count == 0)
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = "no-values", tags }, rec, modelType, intakeSet, stoppingToken);
                                continue;
                            }

                            var kiType = typeof(KeyIndex<>).MakeGenericType(modelType);
                            var kiData = typeof(Data<,>).MakeGenericType(kiType, typeof(string));
                            var getKi = kiData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                            var owners = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var c in candidates)
                            {
                                var kiTask = (Task)getKi.Invoke(null, new object?[] { c.value, stoppingToken })!;
                                await kiTask.ConfigureAwait(false);
                                var ki = GetTaskResult(kiTask);
                                var rid = (string?)ki?.GetType().GetProperty("ReferenceUlid")?.GetValue(ki);
                                if (!string.IsNullOrWhiteSpace(rid)) owners.Add(rid!);
                            }

                            string referenceUlid;
                            if (owners.Count > 1)
                            {
                                await this.SaveRejectAndDrop(Constants.Rejections.MultiOwnerCollision, new { owners = owners.ToArray(), keys = candidates }, rec, modelType, intakeSet, stoppingToken);
                                goto NextRecord;
                            }
                            else if (owners.Count == 1)
                            { referenceUlid = owners.First(); }
                            else
                            {
                                // Try identity map first using envelope fields
                                var refFromIdentity = await TryResolveIdentityAsync(modelType, dict, stoppingToken);
                                referenceUlid = refFromIdentity ?? UlidId.New();
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
                                    kiType.GetProperty("ReferenceUlid")!.SetValue(newKi, referenceUlid);
                                    // also record CanonicalId if known
                                    if (!string.IsNullOrWhiteSpace(canonicalId))
                                        kiType.GetProperty("CanonicalId")?.SetValue(newKi, canonicalId);
                                    await (Task)kiData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { kiType, typeof(CancellationToken) })!
                                        .Invoke(null, new object?[] { newKi, stoppingToken })!;
                                }
                                else if (!string.Equals((string)kiType.GetProperty("ReferenceUlid")!.GetValue(ki)!, referenceUlid, StringComparison.Ordinal))
                                {
                                    await this.SaveRejectAndDrop(Constants.Rejections.KeyOwnerMismatch, new { key = c.value, existing = kiType.GetProperty("ReferenceUlid")!.GetValue(ki), incoming = referenceUlid }, rec, modelType, intakeSet, stoppingToken);
                                    goto NextRecord;
                                }
                            }

                            var refType = typeof(ReferenceItem<>).MakeGenericType(modelType);
                            var refData = typeof(Data<,>).MakeGenericType(refType, typeof(string));
                            var getRef = refData.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                            var refTask = (Task)getRef.Invoke(null, new object?[] { referenceUlid, stoppingToken })!;
                            await refTask.ConfigureAwait(false);
                            var ri = GetTaskResult(refTask) ?? Activator.CreateInstance(refType)!;
                            // Mint a ULID for the reference if it's a new item (Id empty)
                            var currId = (string?)refType.GetProperty("Id")!.GetValue(ri);
                            if (string.IsNullOrWhiteSpace(currId))
                            {
                                refType.GetProperty("Id")!.SetValue(ri, referenceUlid);
                            }
                            // Set CanonicalId (business key)
                            if (!string.IsNullOrWhiteSpace(canonicalId))
                                refType.GetProperty("CanonicalId")?.SetValue(ri, canonicalId);
                            var nextVersion = (ulong)((refType.GetProperty("Version")!.GetValue(ri) as ulong?) ?? 0) + 1UL;
                            refType.GetProperty("Version")!.SetValue(ri, nextVersion);
                            refType.GetProperty("RequiresProjection")!.SetValue(ri, true);
                            await (Task)refData.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { refType, typeof(CancellationToken) })!
                                .Invoke(null, new object?[] { ri, stoppingToken })!;

                            var taskType = typeof(ProjectionTask<>).MakeGenericType(modelType);
                            var newTask = Activator.CreateInstance(taskType)!;
                            var refUlid = (string)refType.GetProperty("Id")!.GetValue(ri)!;
                            taskType.GetProperty("Id")!.SetValue(newTask, $"{refUlid}::{nextVersion}::{Constants.Views.Canonical}");
                            taskType.GetProperty("ReferenceUlid")?.SetValue(newTask, refUlid);
                            if (!string.IsNullOrWhiteSpace(canonicalId)) taskType.GetProperty("CanonicalId")?.SetValue(newTask, canonicalId);
                            taskType.GetProperty("Version")!.SetValue(newTask, nextVersion);
                            taskType.GetProperty("ViewName")!.SetValue(newTask, Constants.Views.Canonical);
                            taskType.GetProperty("CreatedAt")!.SetValue(newTask, DateTimeOffset.UtcNow);
                            var taskDataType = typeof(Data<,>).MakeGenericType(taskType, typeof(string));
                            await (Task)taskDataType.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { taskType, typeof(CancellationToken) })!
                                .Invoke(null, new object?[] { newTask, stoppingToken })!;

                            // Move record to keyed set and drop from intake
                            var keyedSet = FlowSets.StageShort(FlowSets.Keyed);
                            // CorrelationId can carry business key (optional) for diagnostics; leave as-is if already present
                            if (!string.IsNullOrWhiteSpace(canonicalId))
                                rec.GetType().GetProperty("CorrelationId")?.SetValue(rec, canonicalId);
                            // also propagate ULID on stage record for downstream consumers
                            rec.GetType().GetProperty("ReferenceUlid")?.SetValue(rec, refUlid);
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
                { _log.LogDebug(ex, "ModelAssociationWorker iteration failed"); }

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
            var idType = typeof(Sora.Flow.Model.IdentityLink<>).MakeGenericType(modelType);
            var idData = typeof(Data<,>).MakeGenericType(idType, typeof(string));
            // Probe for externalId fields discovered via [EntityLink]
            foreach (var extKey in Infrastructure.FlowRegistry.GetExternalIdKeys(modelType))
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
                        var rulid = idType.GetProperty("ReferenceUlid")!.GetValue(link) as string;
                        if (!string.IsNullOrWhiteSpace(rulid)) return rulid!;
                    }
                    else
                    {
            // Issue a canonical ULID immediately and create a provisional identity link to it
            var ulid = UlidId.New();
                        var provisional = Activator.CreateInstance(idType)!;
                        idType.GetProperty("Id")!.SetValue(provisional, composite);
                        idType.GetProperty("System")!.SetValue(provisional, sys);
                        idType.GetProperty("Adapter")!.SetValue(provisional, adp);
                        idType.GetProperty("ExternalId")!.SetValue(provisional, ext);
            idType.GetProperty("ReferenceUlid")!.SetValue(provisional, ulid);
                        idType.GetProperty("Provisional")!.SetValue(provisional, true);
                        // TTL hint via ExpiresAt; reuse intake TTL window as soft expiry for provisional
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
            await new Sora.Flow.Diagnostics.RejectionReport { Id = Guid.NewGuid().ToString("n"), ReasonCode = code, EvidenceJson = json, PolicyVersion = (string?)rec.GetType().GetProperty("PolicyVersion")?.GetValue(rec) }.Save(ct);

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
                parkedType.GetProperty("StagePayload")!.SetValue(parked, rec.GetType().GetProperty("StagePayload")!.GetValue(rec));
                parkedType.GetProperty("ReasonCode")!.SetValue(parked, code);
                // Store original evidence object (also persisted in diagnostics as JSON)
                parkedType.GetProperty("Evidence")!.SetValue(parked, evidence);

                var dataType = typeof(Data<,>).MakeGenericType(parkedType, typeof(string));
                using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
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

    private static List<Type> DiscoverModels()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
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
                if (bt.GetGenericTypeDefinition() != typeof(FlowEntity<>)) continue;
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
}
