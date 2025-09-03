using System.Threading;
using System.Threading.Tasks;
using Sora.Messaging.Provisioning;

namespace Sora.Messaging.Core.Provisioning
{
    /// <summary>
    /// Default implementation of ITopologyPlanner. Discovers primitives and provisions topology using the registered ITopologyProvisioner.
    /// </summary>
    public sealed class DefaultTopologyPlanner : ITopologyPlanner
    {
        private readonly ITopologyProvisioner _provisioner;
        private readonly ITopologyNaming _naming;
        private readonly Sora.Messaging.ITypeAliasRegistry _aliasRegistry;
        private readonly Sora.Messaging.MessagingOptions _options;

        public DefaultTopologyPlanner(
            ITopologyProvisioner provisioner,
            ITopologyNaming naming,
            Sora.Messaging.ITypeAliasRegistry aliasRegistry,
            Microsoft.Extensions.Options.IOptions<Sora.Messaging.MessagingOptions> options)
        {
            _provisioner = provisioner;
            _naming = naming;
            _aliasRegistry = aliasRegistry;
            _options = options.Value;
        }

    public async Task PlanAndProvisionAsync(CancellationToken ct = default)
        {
            // Discover all primitives in loaded assemblies
            var primitives = DiscoverPrimitives();
            var exchanges = new List<Sora.Messaging.Provisioning.ExchangeSpec>();
            var queues = new List<Sora.Messaging.Provisioning.QueueSpec>();
            var bindings = new List<Sora.Messaging.Provisioning.BindingSpec>();

            var group = _options.DefaultGroup ?? "workers";
            var bus = _options.DefaultBus ?? "rabbit";

            foreach (var primitive in primitives)
            {
                var (type, kind, alias, version) = primitive;
                if (_options.EnableHandlerDiscovery)
                {
                    // Skip declaring queue/binding for primitives with no registered handler types
                    // Simple heuristic: look for any handler service registered for this primitive's interface(s)
                    try
                    {
                        if (System.AppDomain.CurrentDomain.GetData("IServiceProvider") is IServiceProvider root)
                        {
                            bool hasHandler = HasHandler(root, type, kind);
                            if (!hasHandler)
                                continue; // skip provisioning for unused primitive
                        }
                    }
                    catch { /* ignore discovery errors */ }
                }
                string exchangeName;
                string routingKey;
                string queueName;
                Sora.Messaging.Provisioning.DlqSpec? dlqSpec = null;
                Sora.Messaging.Provisioning.RetryBucketsSpec? retrySpec = null;
                switch (kind)
                {
                    case PrimitiveKind.Command:
                        exchangeName = _naming.CommandRouting(bus, alias, version, includeVersion: _options.IncludeVersionInAlias);
                        routingKey = alias;
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "direct", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                    case PrimitiveKind.Announcement:
                        exchangeName = _naming.AnnouncementRouting(bus, alias, version, includeVersion: _options.IncludeVersionInAlias);
                        routingKey = "";
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "fanout", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        // Typically announcements fanout; retries less common, keep disabled unless explicitly enabled
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                    case PrimitiveKind.FlowEvent:
                        exchangeName = _naming.FlowEventRouting(bus, alias);
                        if (_options.IncludeVersionInAlias && version.HasValue)
                            exchangeName += $".v{version.Value}";
                        routingKey = alias;
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "topic", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                }
            }

            // Provision all discovered topology
            foreach (var ex in exchanges)
                await _provisioner.DeclareExchangeAsync(ex.Name, ToExchangeType(ex.Type), ex.Durable, ex.AutoDelete, ct);
            if (_provisioner is Sora.Messaging.Provisioning.IAdvancedTopologyProvisioner adv)
            {
                foreach (var q in queues)
                    await adv.DeclareQueueAsync(q, ct);
            }
            else
            {
                foreach (var q in queues)
                    await _provisioner.DeclareQueueAsync(q.Name, q.Durable, q.Exclusive, q.AutoDelete, ct);
            }
            foreach (var b in bindings)
                await _provisioner.BindQueueAsync(b.To, b.FromExchange, b.RoutingKey, ct);

            // Attempt to publish diagnostics if service is available
            try
            {
                if (System.AppDomain.CurrentDomain.GetData("IServiceProvider") is IServiceProvider root)
                {
                    var diag = root.GetService(typeof(Sora.Messaging.IMessagingDiagnostics)) as Sora.Messaging.IMessagingDiagnostics;
                    if (diag != null)
                    {
                        var desired = new Sora.Messaging.Provisioning.DesiredTopology(exchanges, queues, bindings);
                        // Current & diff are not available in no-op planner; use empty placeholders
                        var current = new Sora.Messaging.Provisioning.CurrentTopology(Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(), Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(), Array.Empty<Sora.Messaging.Provisioning.BindingSpec>());
                        var diff = new Sora.Messaging.Provisioning.TopologyDiff(
                            Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(), // ExchangesToCreate
                            Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(),    // QueuesToCreate
                            Array.Empty<Sora.Messaging.Provisioning.BindingSpec>(),  // BindingsToCreate
                            Array.Empty<(Sora.Messaging.Provisioning.QueueSpec Existing, Sora.Messaging.Provisioning.QueueSpec Desired)>(), // QueueUpdates
                            Array.Empty<(Sora.Messaging.Provisioning.ExchangeSpec Existing, Sora.Messaging.Provisioning.ExchangeSpec Desired)>(), // ExchangeUpdates
                            Array.Empty<Sora.Messaging.Provisioning.ExchangeSpec>(), // ExchangesToRemove
                            Array.Empty<Sora.Messaging.Provisioning.QueueSpec>(),    // QueuesToRemove
                            Array.Empty<Sora.Messaging.Provisioning.BindingSpec>()   // BindingsToRemove
                        );
                        var pd = new Sora.Messaging.Provisioning.ProvisioningDiagnostics(
                            BusCode: _options.DefaultBus ?? "default",
                            Provider: "core-planner",
                            Mode: Sora.Messaging.Provisioning.ProvisioningMode.CreateIfMissing,
                            Desired: desired,
                            Current: current,
                            Diff: diff,
                            Timestamp: DateTimeOffset.UtcNow,
                            DesiredPlanHash: null,
                            PlanMs: 0,
                            InspectMs: 0,
                            DiffMs: 0,
                            ApplyMs: 0,
                            DesiredExchangeCount: desired.Exchanges.Count,
                            DesiredQueueCount: desired.Queues.Count,
                            DesiredBindingCount: desired.Bindings.Count
                        );
                        diag.SetProvisioning(_options.DefaultBus ?? "default", pd);
                    }
                }
            }
            catch { /* swallow diagnostics errors */ }
        }

        public Sora.Messaging.Provisioning.DesiredTopology Plan(string busCode, string? defaultGroup, object providerOptions, Sora.Messaging.IMessagingCapabilities caps, Sora.Messaging.ITypeAliasRegistry? aliases)
        {
            var primitives = DiscoverPrimitives();
            var exchanges = new List<Sora.Messaging.Provisioning.ExchangeSpec>();
            var queues = new List<Sora.Messaging.Provisioning.QueueSpec>();
            var bindings = new List<Sora.Messaging.Provisioning.BindingSpec>();

            var group = _options.DefaultGroup ?? "workers";
            var bus = _options.DefaultBus ?? "rabbit";

            foreach (var primitive in primitives)
            {
                var (type, kind, alias, version) = primitive;
                if (_options.EnableHandlerDiscovery)
                {
                    try
                    {
                        if (System.AppDomain.CurrentDomain.GetData("IServiceProvider") is IServiceProvider root)
                        {
                            bool hasHandler = HasHandler(root, type, kind);
                            if (!hasHandler) continue;
                        }
                    }
                    catch { }
                }
                string exchangeName;
                string routingKey;
                string queueName;
                Sora.Messaging.Provisioning.DlqSpec? dlqSpec = null;
                Sora.Messaging.Provisioning.RetryBucketsSpec? retrySpec = null;
                switch (kind)
                {
                    case PrimitiveKind.Command:
                        exchangeName = _naming.CommandRouting(bus, alias, version, includeVersion: _options.IncludeVersionInAlias);
                        routingKey = alias;
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "direct", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                    case PrimitiveKind.Announcement:
                        exchangeName = _naming.AnnouncementRouting(bus, alias, version, includeVersion: _options.IncludeVersionInAlias);
                        routingKey = "";
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "fanout", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                    case PrimitiveKind.FlowEvent:
                        exchangeName = _naming.FlowEventRouting(bus, alias);
                        routingKey = alias;
                        queueName = _naming.QueueFor(exchangeName, group);
                        exchanges.Add(new Sora.Messaging.Provisioning.ExchangeSpec(exchangeName, "topic", true, false));
                        if (_options.EnableDlq) dlqSpec = new();
                        if (_options.EnableRetry && _options.DefaultRetry.MaxAttempts > 1)
                            retrySpec = new Sora.Messaging.Provisioning.RetryBucketsSpec(Sora.Messaging.RetryMath.Sequence(_options.DefaultRetry).ToList());
                        queues.Add(new Sora.Messaging.Provisioning.QueueSpec(queueName, true, false, false, null, dlqSpec, retrySpec));
                        bindings.Add(new Sora.Messaging.Provisioning.BindingSpec(exchangeName, queueName, "queue", routingKey));
                        break;
                }
            }

            return new Sora.Messaging.Provisioning.DesiredTopology(exchanges, queues, bindings);
        }

        private enum PrimitiveKind { Command, Announcement, FlowEvent }

        private static Sora.Messaging.Provisioning.ExchangeType ToExchangeType(string type)
            => type.ToLowerInvariant() switch
            {
                "direct" => Sora.Messaging.Provisioning.ExchangeType.Direct,
                "fanout" => Sora.Messaging.Provisioning.ExchangeType.Fanout,
                "topic" => Sora.Messaging.Provisioning.ExchangeType.Topic,
                "headers" => Sora.Messaging.Provisioning.ExchangeType.Headers,
                _ => Sora.Messaging.Provisioning.ExchangeType.Topic
            };

        private List<(Type type, PrimitiveKind kind, string alias, int? version)> DiscoverPrimitives()
        {
            var result = new List<(Type, PrimitiveKind, string, int?)>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (typeof(Sora.Messaging.Primitives.ICommandPrimitive).IsAssignableFrom(t))
                        result.Add((t, PrimitiveKind.Command, _aliasRegistry.GetAlias(t), null));
                    else if (typeof(Sora.Messaging.Primitives.IAnnouncementPrimitive).IsAssignableFrom(t))
                        result.Add((t, PrimitiveKind.Announcement, _aliasRegistry.GetAlias(t), null));
                    else if (typeof(Sora.Messaging.Primitives.IFlowEventPrimitive).IsAssignableFrom(t))
                        result.Add((t, PrimitiveKind.FlowEvent, _aliasRegistry.GetAlias(t), null));
                }
            }
            return result;
        }

        private static bool HasHandler(IServiceProvider sp, Type primitiveType, PrimitiveKind kind)
        {
            // Handler type conventions (lightweight): ICommandHandler<T>, IAnnouncementHandler<T>, IFlowEventHandler<T>
            Type? contract = kind switch
            {
                PrimitiveKind.Command => FindGenericHandlerInterface(primitiveType, "ICommandHandler`1"),
                PrimitiveKind.Announcement => FindGenericHandlerInterface(primitiveType, "IAnnouncementHandler`1"),
                PrimitiveKind.FlowEvent => FindGenericHandlerInterface(primitiveType, "IFlowEventHandler`1"),
                _ => null
            };
            if (contract is null) return true; // if we can't resolve the handler contract, don't exclude
            try
            {
                var service = sp.GetService(contract);
                if (service != null) return true;
                // Fallback: check IEnumerable<contract>
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(contract);
                var collection = sp.GetService(enumerableType) as System.Collections.IEnumerable;
                if (collection != null)
                {
                    foreach (var _ in collection) return true;
                }
            }
            catch { }
            return false;
        }

        private static Type? FindGenericHandlerInterface(Type primitiveType, string ifaceSimpleName)
        {
            // Search loaded assemblies for interface with given name and 1 generic parameter, then construct closed generic
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.IsInterface && t.IsGenericTypeDefinition && t.Name == ifaceSimpleName)
                        {
                            return t.MakeGenericType(primitiveType);
                        }
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
