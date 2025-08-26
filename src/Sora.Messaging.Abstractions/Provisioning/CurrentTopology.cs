namespace Sora.Messaging.Provisioning;

public sealed record CurrentTopology(IReadOnlyList<ExchangeSpec> Exchanges, IReadOnlyList<QueueSpec> Queues, IReadOnlyList<BindingSpec> Bindings);