using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Flow.Options;
using Sora.Flow.Runtime;
using Microsoft.Extensions.Hosting;
using Sora.Data.Core;
using Sora.Data.Abstractions.Instructions;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Sora.Flow;

public static class ServiceCollectionExtensions
{
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

    // Background TTL purge
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowPurgeHostedService>());
        return services;
    }

    private sealed class FlowBootstrapInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Intentionally minimal; real providers can hook data initialization elsewhere.
        }
    }

    internal sealed class FlowSchemaEnsureHostedService : IHostedService
    {
        private readonly IServiceProvider _sp;
        public FlowSchemaEnsureHostedService(IServiceProvider sp) => _sp = sp;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _sp.CreateScope();
            var data = scope.ServiceProvider.GetService<IDataService>();
            if (data is null) return; // No data layer configured

            // Helper to ensure created for entity and optional set context
            static async Task EnsureAsync<TEntity>(IDataService d, string? set, CancellationToken ct) where TEntity : class
            {
                using var _set = Sora.Data.Core.DataSetContext.With(set);
                try { await d.Execute<TEntity, bool>(new Instruction(DataInstructions.EnsureCreated), ct); }
                catch { /* best effort; unsupported adapters may throw */ }
            }

            // Core stage sets
            await EnsureAsync<Record>(data, Constants.Sets.Intake, cancellationToken);
            await EnsureAsync<Record>(data, Constants.Sets.Standardized, cancellationToken);
            await EnsureAsync<Record>(data, Constants.Sets.Keyed, cancellationToken);

            // Associations and tasks (default set)
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

                    // Purge Records by OccurredAt per stage set
                    using (DataSetContext.With(Infrastructure.Constants.Sets.Intake))
                        await Data.Core.Data<Record, string>.Delete(r => r.OccurredAt < cutoffIntake, Infrastructure.Constants.Sets.Intake, stoppingToken);
                    using (DataSetContext.With(Infrastructure.Constants.Sets.Standardized))
                        await Data.Core.Data<Record, string>.Delete(r => r.OccurredAt < cutoffStd, Infrastructure.Constants.Sets.Standardized, stoppingToken);
                    using (DataSetContext.With(Infrastructure.Constants.Sets.Keyed))
                        await Data.Core.Data<Record, string>.Delete(r => r.OccurredAt < cutoffKeyed, Infrastructure.Constants.Sets.Keyed, stoppingToken);

                    // Purge ProjectionTask by CreatedAt
                    await Data.Core.Data<ProjectionTask, string>.Delete(t => t.CreatedAt < cutoffTask, set: null!, stoppingToken);

                    // Purge RejectionReport by CreatedAt
                    await Data.Core.Data<RejectionReport, string>.Delete(r => r.CreatedAt < cutoffReject, set: null!, stoppingToken);

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
            // Simple polling loop; provider-agnostic. No bespoke queues.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = Math.Max(1, _opts.CurrentValue.BatchSize);
                    var defaultView = _opts.CurrentValue.DefaultViewName;

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

                            var viewName = string.IsNullOrWhiteSpace(task.ViewName) ? defaultView : task.ViewName;
                            // Reduce all keyed records for this ReferenceId into canonical and lineage shapes
                            var keyed = await Record.Query($"CorrelationId == '{task.ReferenceId}'", Constants.Sets.Keyed, stoppingToken);
                            var canonical = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                            var lineage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

                            foreach (var r in keyed)
                            {
                                var src = r.SourceId ?? "unknown";
                                if (r.StagePayload is not IDictionary<string, object> dict) continue;
                                foreach (var (tag, raw) in dict)
                                {
                                    var values = ToValues(raw);
                                    if (values.Count == 0) continue;
                                    // canonical accumulate
                                    if (!canonical.TryGetValue(tag, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); canonical[tag] = set; }
                                    foreach (var v in values) set.Add(v);
                                    // lineage accumulate
                                    if (!lineage.TryGetValue(tag, out var m)) { m = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); lineage[tag] = m; }
                                    foreach (var v in values)
                                    {
                                        if (!m.TryGetValue(v, out var sources)) { sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase); m[v] = sources; }
                                        sources.Add(src);
                                    }
                                }
                            }

                            var canonicalView = new Dictionary<string, object>();
                            foreach (var (tag, set) in canonical)
                                canonicalView[tag] = set.ToArray();

                            var lineageView = new Dictionary<string, object>();
                            foreach (var (tag, m) in lineage)
                                lineageView[tag] = m.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

                            var canonicalDoc = new ProjectionView<object>
                            {
                                Id = $"{Infrastructure.Constants.Views.Canonical}::{task.ReferenceId}",
                                ReferenceId = task.ReferenceId,
                                ViewName = Infrastructure.Constants.Views.Canonical,
                                View = canonicalView
                            };
                            await canonicalDoc.Save(Infrastructure.Constants.Views.Canonical, stoppingToken);

                            var lineageDoc = new ProjectionView<object>
                            {
                                Id = $"{Infrastructure.Constants.Views.Lineage}::{task.ReferenceId}",
                                ReferenceId = task.ReferenceId,
                                ViewName = Infrastructure.Constants.Views.Lineage,
                                View = lineageView
                            };
                            await lineageDoc.Save(Infrastructure.Constants.Views.Lineage, stoppingToken);

                            if (refItem.RequiresProjection)
                            {
                                refItem.RequiresProjection = false;
                                await refItem.Save(stoppingToken);
                            }

                            await task.Delete(stoppingToken);
                            processed++;
                        }

                        pageNum++;
                    }

                    if (processed > 0)
                        _log.LogDebug("ProjectionWorker processed {Count} tasks", processed);
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

                    using (DataSetContext.With(Constants.Sets.Intake))
                    {
                        var page = await Record.Page(1, batch, stoppingToken);
                        if (page.Count == 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                            continue;
                        }

                        foreach (var rec in page)
                        {
                            // Expect normalized dictionary payload with tags; extract keyTag from policy or default tag
                            var referenceId = rec.CorrelationId ?? rec.RecordId; // placeholder keying; to be replaced by tag-based

                            // Update KeyIndex single-owner map (AggregationKey → ReferenceId)
                            // For now, use SourceId + RecordId as a stand-in aggregation key
                            var aggKey = $"{rec.SourceId}:{rec.RecordId}";
                            var ki = await KeyIndex.Get(aggKey, stoppingToken);
                            if (ki is null)
                            {
                                ki = new KeyIndex { AggregationKey = aggKey, ReferenceId = referenceId };
                                await ki.Save(stoppingToken);
                            }

                            // Update/insert ReferenceItem and mark projection needed
                            var ri = await ReferenceItem.Get(referenceId, stoppingToken) ?? new ReferenceItem { ReferenceId = referenceId };
                            ri.Version += 1; // simplistic version bump
                            ri.RequiresProjection = true;
                            await ri.Save(stoppingToken);

                            // Enqueue a projection task for canonical view
                            var task = new ProjectionTask
                            {
                                Id = $"{referenceId}::{ri.Version}::{Infrastructure.Constants.Views.Canonical}",
                                ReferenceId = referenceId,
                                Version = ri.Version,
                                ViewName = Infrastructure.Constants.Views.Canonical,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            await task.Save(stoppingToken);

                            // Move record to keyed set for traceability
                            await rec.Save(Constants.Sets.Keyed, stoppingToken);
                            await rec.Delete(stoppingToken); // delete from intake (current set)
                        }
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
    }
}
