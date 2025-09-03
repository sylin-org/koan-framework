using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sora.Messaging.Provisioning;

/// <summary>
/// Central orchestrator: evaluates provisioning mode, plans/inspects/diffs/applies topology once at startup.
/// Sets an AppDomain flag to let providers (e.g. RabbitMQ factory) know provisioning already occurred.
/// </summary>
internal sealed class TopologyOrchestratorHostedService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TopologyOrchestratorHostedService>? _logger;
    private readonly IOptions<Sora.Messaging.MessagingOptions> _msgOpts;

    private const string FlagKey = "Sora.Messaging.Provisioning.Orchestrated";

    public TopologyOrchestratorHostedService(IServiceProvider sp, IOptions<Sora.Messaging.MessagingOptions> msgOpts, ILogger<TopologyOrchestratorHostedService>? logger = null)
    {
        _sp = sp;
        _msgOpts = msgOpts;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // If already orchestrated (e.g. tests manually invoked), skip.
        if (AppDomain.CurrentDomain.GetData(FlagKey) is bool b && b)
            return Task.CompletedTask;

        try
        {
            Orchestrate();
            AppDomain.CurrentDomain.SetData(FlagKey, true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Messaging topology orchestration failed");
            // For production safety we swallow to not block app unless desired; could be configurable later.
        }
        return Task.CompletedTask;
    }

    private void Orchestrate()
    {
        var msgOpts = _msgOpts.Value;
        // Determine mode: env override > implicit (prod=Off else CreateIfMissing)
        var envOverride = Environment.GetEnvironmentVariable("SORA_MESSAGING_PROVISION");
        Sora.Messaging.Provisioning.ProvisioningMode mode;
        if (!string.IsNullOrWhiteSpace(envOverride) && Enum.TryParse(envOverride, true, out Sora.Messaging.Provisioning.ProvisioningMode parsed))
            mode = parsed;
        else
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
            var isProd = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
            mode = isProd ? Sora.Messaging.Provisioning.ProvisioningMode.Off : Sora.Messaging.Provisioning.ProvisioningMode.CreateIfMissing;
        }

    var diag = _sp.GetService(typeof(Sora.Messaging.IMessagingDiagnostics)) as Sora.Messaging.IMessagingDiagnostics;
    var planner = _sp.GetService(typeof(Sora.Messaging.Provisioning.ITopologyPlanner)) as Sora.Messaging.Provisioning.ITopologyPlanner;
        if (planner is null)
        {
            _logger?.LogDebug("No core topology planner registered; skipping orchestration");
            return;
        }

        var bus = msgOpts.DefaultBus ?? "default";
        // Acquire provider client (if any) before planning so we can supply provider options to planner
        var clientAccessor = _sp.GetService(typeof(Sora.Messaging.Provisioning.IProviderClientAccessor)) as Sora.Messaging.Provisioning.IProviderClientAccessor;
        object? providerClient = clientAccessor?.GetProviderClient(bus);
        object providerOptionsForPlan = new object();
        if (providerClient is not null)
        {
            // Attempt to extract Item3 (options) from known tuple shapes (e.g., (IConnection,IModel,RabbitMqOptions)) via reflection to keep orchestrator provider-agnostic
            try
            {
                var t = providerClient.GetType();
                var item3 = t.GetProperty("Item3");
                var val = item3?.GetValue(providerClient);
                if (val is not null) providerOptionsForPlan = val;
            }
            catch { /* ignore */ }
        }
        // If providerOptionsForPlan is just a plain object, log and skip planning for this bus
        if (planner.GetType().FullName?.Contains("RabbitMqProvisioner") == true && providerOptionsForPlan.GetType() == typeof(object))
        {
            _logger?.LogWarning("[messaging.provision] Skipping plan phase for bus={Bus} due to missing or invalid provider options (expected RabbitMqOptions)", bus);
            return; // skip planning for this bus
        }
    _logger?.LogDebug("[messaging.provision] starting plan phase bus={Bus} mode={Mode}", msgOpts.DefaultBus ?? "default", mode);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var desired = planner.Plan(bus, msgOpts.DefaultGroup, providerOptionsForPlan, new BasicCapabilities(), _sp.GetService(typeof(Sora.Messaging.ITypeAliasRegistry)) as Sora.Messaging.ITypeAliasRegistry);
    sw.Stop();
    long planMs = sw.ElapsedMilliseconds;

        Sora.Messaging.Provisioning.CurrentTopology current = new(Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(), Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(), Array.Empty<Sora.Messaging.Provisioning.BindingSpec>());
        Sora.Messaging.Provisioning.TopologyDiff diff = new(
            Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(),
            Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(),
            Array.Empty<Sora.Messaging.Provisioning.BindingSpec>(),
            Array.Empty<(Sora.Messaging.Provisioning.QueueSpec Existing, Sora.Messaging.Provisioning.QueueSpec Desired)>(),
            Array.Empty<(Sora.Messaging.Provisioning.ExchangeSpec Existing, Sora.Messaging.Provisioning.ExchangeSpec Desired)>(),
            Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(),
            Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(),
            Array.Empty<Sora.Messaging.Provisioning.BindingSpec>()
        );

        // If provider supplies inspector + differ + applier, use them via provider factory's services (Rabbit factory still does its own; this is additive for future providers).
        var inspector = _sp.GetService(typeof(Sora.Messaging.Provisioning.ITopologyInspector)) as Sora.Messaging.Provisioning.ITopologyInspector;
        var differ = _sp.GetService(typeof(Sora.Messaging.Provisioning.ITopologyDiffer)) as Sora.Messaging.Provisioning.ITopologyDiffer;
        var applier = _sp.GetService(typeof(Sora.Messaging.Provisioning.ITopologyApplier)) as Sora.Messaging.Provisioning.ITopologyApplier;
        long inspectMs = 0, diffMs = 0, applyMs = 0;
        if (inspector != null && differ != null && applier != null && providerClient != null)
        {
            var swInspect = System.Diagnostics.Stopwatch.StartNew();
            current = inspector.Inspect(bus, providerClient);
            swInspect.Stop();
            inspectMs = swInspect.ElapsedMilliseconds;
            var swDiff = System.Diagnostics.Stopwatch.StartNew();
            diff = differ.Diff(desired, current);
            swDiff.Stop();
            diffMs = swDiff.ElapsedMilliseconds;
            _logger?.LogDebug("[messaging.provision] diff computed bus={Bus} addsEx={AddsEx} addsQ={AddsQ} addsB={AddsB} updEx={UpdEx} updQ={UpdQ} rmEx={RmEx} rmQ={RmQ} rmB={RmB}", bus, diff.ExchangesToCreate.Count, diff.QueuesToCreate.Count, diff.BindingsToCreate.Count, diff.ExchangeUpdates.Count, diff.QueueUpdates.Count, diff.ExchangesToRemove.Count, diff.QueuesToRemove.Count, diff.BindingsToRemove.Count);
            if (mode != Sora.Messaging.Provisioning.ProvisioningMode.Off && mode != Sora.Messaging.Provisioning.ProvisioningMode.DryRun)
            {
                // Central ForceRecreate guard: require explicit env override SORA_MESSAGING_ALLOW_FORCE=1
                if (mode == Sora.Messaging.Provisioning.ProvisioningMode.ForceRecreate)
                {
                    var allowForce = string.Equals(Environment.GetEnvironmentVariable("SORA_MESSAGING_ALLOW_FORCE"), "1", StringComparison.Ordinal);
                    if (!allowForce)
                    {
                        _logger?.LogWarning("[messaging.provision] ForceRecreate requested but not allowed (set SORA_MESSAGING_ALLOW_FORCE=1). Skipping apply.");
                    }
                    else
                    {
                        var swApply = System.Diagnostics.Stopwatch.StartNew();
                        applier.Apply(bus, mode, diff, providerClient);
                        swApply.Stop();
                        applyMs = swApply.ElapsedMilliseconds;
                    }
                }
                else
                {
                var swApply = System.Diagnostics.Stopwatch.StartNew();
                applier.Apply(bus, mode, diff, providerClient);
                swApply.Stop();
                applyMs = swApply.ElapsedMilliseconds;
                }
                _logger?.LogInformation("[messaging.provision] apply completed bus={Bus} mode={Mode} applyMs={ApplyMs}", bus, mode, applyMs);
            }
        }
        else if (mode != Sora.Messaging.Provisioning.ProvisioningMode.Off && mode != Sora.Messaging.Provisioning.ProvisioningMode.DryRun)
        {
            // Fallback: directly provision via planner's provisioner (planner already calls provisioner internally in PlanAndProvisionAsync if invoked).
            // Invoke full provisioning path for default planner.
            if (planner is Sora.Messaging.Core.Provisioning.DefaultTopologyPlanner concrete)
            {
                concrete.PlanAndProvisionAsync().GetAwaiter().GetResult();
            }
        }

        // Compute stable hash of desired topology (exchange/queue/binding names + counts) for change detection
        string? desiredHash;
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ex in desired.Exchanges.OrderBy(e => e.Name)) sb.Append(ex.Name).Append('|').Append(ex.Type).Append(';');
            foreach (var q in desired.Queues.OrderBy(q => q.Name)) sb.Append(q.Name).Append('|').Append(q.Durable).Append(';');
            foreach (var b in desired.Bindings.OrderBy(b => b.FromExchange).ThenBy(b => b.To).ThenBy(b => b.RoutingKey)) sb.Append(b.FromExchange).Append('>').Append(b.To).Append('|').Append(b.RoutingKey).Append(';');
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            desiredHash = Convert.ToHexString(sha.ComputeHash(bytes));
        }
    catch { desiredHash = null; }

        // Fast no-op: check persisted hash (if enabled) before diff/apply (only if we actually diffed)
        if (msgOpts.PersistPlanHash && desiredHash is not null)
        {
            try
            {
                var dir = string.IsNullOrWhiteSpace(msgOpts.PlanHashDirectory) ? AppContext.BaseDirectory : System.IO.Path.GetFullPath(msgOpts.PlanHashDirectory);
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, $".sora-messaging-planhash-{bus}.txt");
                if (System.IO.File.Exists(file))
                {
                    var existing = (System.IO.File.ReadAllText(file) ?? string.Empty).Trim();
                    if (string.Equals(existing, desiredHash, StringComparison.OrdinalIgnoreCase) && (diff.ExchangesToCreate.Count + diff.QueuesToCreate.Count + diff.BindingsToCreate.Count + diff.ExchangeUpdates.Count + diff.QueueUpdates.Count + diff.ExchangesToRemove.Count + diff.QueuesToRemove.Count + diff.BindingsToRemove.Count) == 0)
                    {
                        _logger?.LogInformation("[messaging.provision] plan hash unchanged and no diff; skipping diagnostics update (bus={Bus})", bus);
                        return; // Short-circuit: nothing changed; avoid updating timestamp to signal true no-op
                    }
                }
                System.IO.File.WriteAllText(file, desiredHash);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[messaging.provision] failed persisting plan hash (ignored)");
            }
        }

        var pd = new Sora.Messaging.Provisioning.ProvisioningDiagnostics(
            BusCode: bus,
            Provider: "core-orchestrator",
            Mode: mode,
            Desired: desired,
            Current: current,
            Diff: diff,
            Timestamp: DateTimeOffset.UtcNow,
            DesiredPlanHash: desiredHash,
            PlanMs: planMs,
            InspectMs: inspectMs,
            DiffMs: diffMs,
            ApplyMs: applyMs,
            DesiredExchangeCount: desired.Exchanges.Count,
            DesiredQueueCount: desired.Queues.Count,
            DesiredBindingCount: desired.Bindings.Count,
            DiffCreateExchangeCount: diff.ExchangesToCreate.Count,
            DiffCreateQueueCount: diff.QueuesToCreate.Count,
            DiffCreateBindingCount: diff.BindingsToCreate.Count,
            DiffUpdateExchangeCount: diff.ExchangeUpdates.Count,
            DiffUpdateQueueCount: diff.QueueUpdates.Count,
            DiffRemoveExchangeCount: diff.ExchangesToRemove.Count,
            DiffRemoveQueueCount: diff.QueuesToRemove.Count,
            DiffRemoveBindingCount: diff.BindingsToRemove.Count,
            AppliedExchangeCount: diff.ExchangesToCreate.Count + diff.ExchangeUpdates.Count, // approximation (no removal apply count yet)
            AppliedQueueCount: diff.QueuesToCreate.Count + diff.QueueUpdates.Count,
            AppliedBindingCount: diff.BindingsToCreate.Count
        );
    diag?.SetProvisioning(bus, pd);
    _logger?.LogInformation("[messaging.provision] diagnostics recorded bus={Bus} hash={Hash} planMs={PlanMs} inspectMs={InspectMs} diffMs={DiffMs} applyMs={ApplyMs}", bus, desiredHash, planMs, inspectMs, diffMs, applyMs);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class BasicCapabilities : Sora.Messaging.IMessagingCapabilities
    {
        // Conservative baseline; adjust when provider capability discovery is wired.
        public bool DelayedDelivery => false; // Generic delay scheduling not assumed.
        public bool DeadLettering => true; // DLQ supported via core planning + provider args.
        public bool Transactions => false; // Not universally supported / enabled.
        public int MaxMessageSizeKB => 51200; // 50MB pragmatic upper bound placeholder.
        public string MessageOrdering => "None"; // No cross-partition ordering guarantee expressed.
        public bool ScheduledEnqueue => false; // Future enhancement.
        public bool PublisherConfirms => true; // Common for reliability (e.g., RabbitMQ confirms).
    }
}
