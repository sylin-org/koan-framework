namespace Sora.Messaging;

public sealed record ExchangeSpec(string Name, string Type, bool Durable = true, bool AutoDelete = false, IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record QueueSpec(string Name, bool Durable = true, bool Exclusive = false, bool AutoDelete = false,
    IReadOnlyDictionary<string, object?>? Arguments = null,
    DlqSpec? Dlq = null,
    RetryBucketsSpec? Retry = null,
    int? MessageTtlMs = null);

public sealed record BindingSpec(string FromExchange, string To, string ToType, string RoutingKey = "", IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record DlqSpec(string? ExchangeName = null, string? RoutingKey = null);

public sealed record RetryBucketsSpec(IReadOnlyList<int> DelaysSeconds);

public sealed record DesiredTopology(IReadOnlyList<ExchangeSpec> Exchanges, IReadOnlyList<QueueSpec> Queues, IReadOnlyList<BindingSpec> Bindings);

public sealed record CurrentTopology(IReadOnlyList<ExchangeSpec> Exchanges, IReadOnlyList<QueueSpec> Queues, IReadOnlyList<BindingSpec> Bindings);

public sealed record TopologyDiff(
    IReadOnlyList<ExchangeSpec> ExchangesToCreate,
    IReadOnlyList<QueueSpec> QueuesToCreate,
    IReadOnlyList<BindingSpec> BindingsToCreate,
    IReadOnlyList<(QueueSpec Existing, QueueSpec Desired)> QueueUpdates,
    IReadOnlyList<(ExchangeSpec Existing, ExchangeSpec Desired)> ExchangeUpdates,
    IReadOnlyList<ExchangeSpec> ExchangesToRemove,
    IReadOnlyList<QueueSpec> QueuesToRemove,
    IReadOnlyList<BindingSpec> BindingsToRemove);

public interface ITopologyPlanner
{
    /// <param name="busCode">Logical bus code (used in naming).</param>
    /// <param name="defaultGroup">Default consumer group name to use when provider options do not specify subscriptions.</param>
    /// <param name="providerOptions">Provider-specific options object.</param>
    /// <param name="caps">Provider capabilities.</param>
    /// <param name="aliases">Type alias registry for name resolution.</param>
    DesiredTopology Plan(string busCode, string? defaultGroup, object providerOptions, IMessagingCapabilities caps, ITypeAliasRegistry? aliases);
}

public interface ITopologyInspector
{
    CurrentTopology Inspect(string busCode, object providerClient);
}

public interface ITopologyDiffer
{
    TopologyDiff Diff(DesiredTopology desired, CurrentTopology current);
}

public interface ITopologyApplier
{
    void Apply(string busCode, ProvisioningMode mode, TopologyDiff diff, object providerClient);
}

public sealed record ProvisioningDiagnostics(
    string BusCode,
    string Provider,
    ProvisioningMode Mode,
    DesiredTopology Desired,
    CurrentTopology Current,
    TopologyDiff Diff,
    DateTimeOffset Timestamp);
