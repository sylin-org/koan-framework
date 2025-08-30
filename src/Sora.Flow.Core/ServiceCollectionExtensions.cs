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
}
