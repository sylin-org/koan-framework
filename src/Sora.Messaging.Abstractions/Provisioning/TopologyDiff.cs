namespace Sora.Messaging.Provisioning;

public sealed record TopologyDiff(
    IReadOnlyList<ExchangeSpec> ExchangesToCreate,
    IReadOnlyList<QueueSpec> QueuesToCreate,
    IReadOnlyList<BindingSpec> BindingsToCreate,
    IReadOnlyList<(QueueSpec Existing, QueueSpec Desired)> QueueUpdates,
    IReadOnlyList<(ExchangeSpec Existing, ExchangeSpec Desired)> ExchangeUpdates,
    IReadOnlyList<ExchangeSpec> ExchangesToRemove,
    IReadOnlyList<QueueSpec> QueuesToRemove,
    IReadOnlyList<BindingSpec> BindingsToRemove);