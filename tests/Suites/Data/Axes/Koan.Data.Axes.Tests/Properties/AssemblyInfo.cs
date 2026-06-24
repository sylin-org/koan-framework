using Xunit;

// The [DataAxis] expander writes into process-global static registries (ManagedFieldRegistry,
// StorageNameParticleRegistry, OperationOverrideRegistry). Serialize the whole assembly so concurrent specs never
// race that shared state; each spec resets the registries it touches in its ctor/dispose.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
