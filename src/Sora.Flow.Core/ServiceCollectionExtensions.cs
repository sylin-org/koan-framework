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
using Sora.Flow.Options;
using Sora.Flow.Runtime;

namespace Sora.Flow;

public static class ServiceCollectionExtensions
{
    // Serialize writes for JSON adapter to avoid file sharing collisions
    private static readonly SemaphoreSlim s_refItemLock = new(1, 1);
    private static readonly SemaphoreSlim s_projTaskLock = new(1, 1);
    private static readonly SemaphoreSlim s_keyIndexLock = new(1, 1);

    // Shared helper: normalize a dynamic payload (Dictionary or JObject) into a dictionary
    private static IDictionary<string, object>? ExtractDict(object? payload)
    {
        if (payload is null) return null;
        if (payload is IDictionary<string, object> d) return d;
        if (payload is Newtonsoft.Json.Linq.JObject jo)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in jo.Properties())
            {
                var val = prop.Value;
                if (val is Newtonsoft.Json.Linq.JArray ja)
                    dict[prop.Name] = ja.ToList<object>();
                else if (val is Newtonsoft.Json.Linq.JValue jv)
                    dict[prop.Name] = jv.ToObject<object?>() ?? string.Empty;
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
        services.TryAddSingleton<IFlowRuntime, InMemoryFlowRuntime>();

        // Bootstrap (indexes/TTLs) via initializer; no-ops by default; providers can extend.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(new FlowBootstrapInitializer()));

        // Ensure schemas exist at startup (idempotent). Providers that don't support instructions will no-op.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowSchemaEnsureHostedService>());

        // Background projection worker (materializes ProjectionView from ProjectionTask)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ProjectionWorkerHostedService>());

        // Background association/keying worker (derives ReferenceId, updates indexes)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AssociationWorkerHostedService>());

        // TTL purge
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowPurgeHostedService>());
        return services;
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

            static async Task EnsureAsync<TEntity>(IDataService d, string? set, CancellationToken ct) where TEntity : class
            {
                using var _set = DataSetContext.With(set);
                try { await d.Execute<TEntity, bool>(new Instruction(DataInstructions.EnsureCreated), ct); }
                catch { }
            }

            await EnsureAsync<Record>(data, Constants.Sets.Intake, cancellationToken);
            await EnsureAsync<Record>(data, Constants.Sets.Standardized, cancellationToken);
            await EnsureAsync<Record>(data, Constants.Sets.Keyed, cancellationToken);
            await EnsureAsync<KeyIndex>(data, null, cancellationToken);
            await EnsureAsync<ReferenceItem>(data, null, cancellationToken);
            await EnsureAsync<ProjectionTask>(data, null, cancellationToken);
            await EnsureAsync<RejectionReport>(data, null, cancellationToken);
            await EnsureAsync<PolicyBundle>(data, null, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

                    using (DataSetContext.With(Constants.Sets.Intake))
                        await Data<Record, string>.Delete(r => r.OccurredAt < cutoffIntake, Constants.Sets.Intake, stoppingToken);
                    using (DataSetContext.With(Constants.Sets.Standardized))
                        await Data<Record, string>.Delete(r => r.OccurredAt < cutoffStd, Constants.Sets.Standardized, stoppingToken);
                    using (DataSetContext.With(Constants.Sets.Keyed))
                        await Data<Record, string>.Delete(r => r.OccurredAt < cutoffKeyed, Constants.Sets.Keyed, stoppingToken);

                    await Data<ProjectionTask, string>.Delete(t => t.CreatedAt < cutoffTask, set: null!, stoppingToken);
                    await Data<RejectionReport, string>.Delete(r => r.CreatedAt < cutoffReject, set: null!, stoppingToken);

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

    internal sealed class ProjectionWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<FlowOptions> _opts;
        private readonly ILogger<ProjectionWorkerHostedService> _log;

        public ProjectionWorkerHostedService(IServiceProvider sp, IOptionsMonitor<FlowOptions> opts, ILogger<ProjectionWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    int pageNum = 1;
                    int processed = 0;

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var tasks = await ProjectionTask.Page(pageNum, batch, stoppingToken);
                        if (tasks.Count == 0) break;

                        foreach (var task in tasks)
                        {
                            var refItem = await ReferenceItem.Get(task.ReferenceId, stoppingToken);
                            if (refItem is null) continue;

                            // JSON adapter doesn't support string queries; materialize and filter in-memory
                            var allKeyed = await Record.All(Constants.Sets.Keyed, stoppingToken);
                            var keyed = allKeyed.Where(r => string.Equals(r.CorrelationId, task.ReferenceId, StringComparison.Ordinal)).ToList();
                            var canonical = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                            var lineage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

                            foreach (var r in keyed)
                            {
                                var src = r.SourceId ?? "unknown";
                                var dict = ExtractDict(r.StagePayload);
                                if (dict is null) continue;
                                foreach (var kv in dict)
                                {
                                    var tag = kv.Key;
                                    var values = ToValues(kv.Value);
                                    if (values.Count == 0) continue;
                                    if (!canonical.TryGetValue(tag, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); canonical[tag] = set; }
                                    foreach (var v in values) set.Add(v);
                                    if (!lineage.TryGetValue(tag, out var m)) { m = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); lineage[tag] = m; }
                                    foreach (var v in values)
                                    {
                                        if (!m.TryGetValue(v, out var sources)) { sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase); m[v] = sources; }
                                        sources.Add(src);
                                    }
                                }
                            }

                            var canonicalView = canonical.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
                            var lineageView = lineage.ToDictionary(
                                kv => kv.Key,
                                kv => kv.Value.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                                StringComparer.OrdinalIgnoreCase);

                            var canonicalDoc = new CanonicalProjectionView { Id = $"{Infrastructure.Constants.Views.Canonical}::{task.ReferenceId}", ReferenceId = task.ReferenceId, ViewName = Infrastructure.Constants.Views.Canonical, View = canonicalView };
                            await canonicalDoc.Save(Infrastructure.Constants.Views.Canonical, stoppingToken);

                            var lineageDoc = new LineageProjectionView { Id = $"{Infrastructure.Constants.Views.Lineage}::{task.ReferenceId}", ReferenceId = task.ReferenceId, ViewName = Infrastructure.Constants.Views.Lineage, View = lineageView };
                            await lineageDoc.Save(Infrastructure.Constants.Views.Lineage, stoppingToken);

                            if (refItem.RequiresProjection)
                            {
                                refItem.RequiresProjection = false;
                                await s_refItemLock.WaitAsync(stoppingToken);
                                try { await refItem.Save(stoppingToken); }
                                finally { s_refItemLock.Release(); }
                            }

                            // Delete projection task with retry to avoid JSON file share collisions
                            await s_projTaskLock.WaitAsync(stoppingToken);
                            try
                            {
                                await ServiceCollectionExtensions.RetryIoAsync(() => task.Delete(stoppingToken), stoppingToken);
                            }
                            finally { s_projTaskLock.Release(); }
                            processed++;
                        }

                        pageNum++;
                    }

                    if (processed > 0) _log.LogDebug("ProjectionWorker processed {Count} tasks", processed);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "ProjectionWorker iteration failed (will retry)");
                }

                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        private static List<string> ToValues(object raw)
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

    }

    internal sealed class AssociationWorkerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IOptionsMonitor<FlowOptions> _opts;
        private readonly ILogger<AssociationWorkerHostedService> _log;

        public AssociationWorkerHostedService(IServiceProvider sp, IOptionsMonitor<FlowOptions> opts, ILogger<AssociationWorkerHostedService> log)
        { _sp = sp; _opts = opts; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    // Read a page from intake set only; avoid leaking the set into root-scoped entities
                    IReadOnlyList<Record> page;
                    using (DataSetContext.With(Constants.Sets.Intake))
                    {
                        page = await Record.Page(1, batch, stoppingToken);
                    }
                    if (page.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    foreach (var rec in page)
                    {
                        // Root scope for indexes, reference items, tasks, and rejections
                        using var _root = DataSetContext.With(null);

                        var dict = ExtractDict(rec.StagePayload);
                        var tags = _opts.CurrentValue.AggregationTags ?? Array.Empty<string>();
                        if (dict is null || tags.Length == 0)
                        {
                            await SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = dict is null ? "no-payload" : "no-config-tags", tags }, rec, stoppingToken);
                            continue;
                        }

                        var candidates = new List<(string tag, string value)>();
                        foreach (var tag in tags)
                        {
                            if (!dict.TryGetValue(tag, out var raw)) continue;
                            foreach (var v in ToValues(raw))
                            {
                                if (!string.IsNullOrWhiteSpace(v)) candidates.Add((tag, v));
                            }
                        }

                        if (candidates.Count == 0)
                        {
                            await SaveRejectAndDrop(Constants.Rejections.NoKeys, new { reason = "no-values", tags }, rec, stoppingToken);
                            continue;
                        }

                        var owners = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var candidate in candidates)
                        {
                            var val = candidate.value;
                            var ki = await KeyIndex.Get(val, stoppingToken);
                            if (ki != null && !string.IsNullOrWhiteSpace(ki.ReferenceId)) owners.Add(ki.ReferenceId);
                        }

                        string referenceId;
                        if (owners.Count > 1)
                        {
                            await SaveRejectAndDrop(Constants.Rejections.MultiOwnerCollision, new { owners = owners.ToArray(), keys = candidates }, rec, stoppingToken);
                            // Do not move to keyed on collision
                            goto NextRecord;
                        }
                        else if (owners.Count == 1)
                        {
                            referenceId = owners.First();
                        }
                        else
                        {
                            referenceId = rec.CorrelationId ?? candidates[0].value;
                        }

                        foreach (var candidate in candidates)
                        {
                            var val = candidate.value;
                            var ki = await KeyIndex.Get(val, stoppingToken);
                            if (ki is null)
                            {
                                await s_keyIndexLock.WaitAsync(stoppingToken);
                                try { await new KeyIndex { AggregationKey = val, ReferenceId = referenceId }.Save(stoppingToken); }
                                finally { s_keyIndexLock.Release(); }
                            }
                            else if (!string.Equals(ki.ReferenceId, referenceId, StringComparison.Ordinal))
                            {
                                await SaveRejectAndDrop(Constants.Rejections.KeyOwnerMismatch, new { key = val, existing = ki.ReferenceId, incoming = referenceId }, rec, stoppingToken);
                                goto NextRecord;
                            }
                        }

                        var ri = await ReferenceItem.Get(referenceId, stoppingToken) ?? new ReferenceItem { ReferenceId = referenceId };
                        ri.Version += 1;
                        ri.RequiresProjection = true;
                        await s_refItemLock.WaitAsync(stoppingToken);
                        try { await ri.Save(stoppingToken); }
                        finally { s_refItemLock.Release(); }

                        var t = new ProjectionTask { Id = $"{referenceId}::{ri.Version}::{Infrastructure.Constants.Views.Canonical}", ReferenceId = referenceId, Version = ri.Version, ViewName = Infrastructure.Constants.Views.Canonical, CreatedAt = DateTimeOffset.UtcNow };
                        await s_projTaskLock.WaitAsync(stoppingToken);
                        try { await t.Save(stoppingToken); }
                        finally { s_projTaskLock.Release(); }

                        rec.CorrelationId = referenceId;
                        await rec.Save(Constants.Sets.Keyed, stoppingToken);
                        // Explicitly delete from intake set
                        await Data<Record, string>.DeleteAsync(rec.Id, Constants.Sets.Intake, stoppingToken);

                    NextRecord:;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "AssociationWorker iteration failed");
                }

                try { await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        private static List<string> ToValues(object raw)
        {
            switch (raw)
            {
                case null: return new List<string>();
                case string s when !string.IsNullOrWhiteSpace(s): return new List<string> { s };
                case IEnumerable<object> arr:
                    return arr.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
                default:
                    return new List<string> { raw.ToString() ?? string.Empty };
            }
        }

        private static async Task SaveRejectAndDrop(string code, object evidence, Record rec, CancellationToken ct)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(evidence);
            await new RejectionReport { Id = Guid.NewGuid().ToString("n"), ReasonCode = code, EvidenceJson = json, PolicyVersion = rec.PolicyVersion }.Save(ct);
            // Delete the intake record explicitly from intake set
            await Data<Record, string>.DeleteAsync(rec.Id, Constants.Sets.Intake, ct);
        }

    }
}
