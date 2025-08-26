namespace Sora.Messaging.Provisioning;

public sealed record DesiredTopology(IReadOnlyList<ExchangeSpec> Exchanges, IReadOnlyList<QueueSpec> Queues, IReadOnlyList<BindingSpec> Bindings);