# Sylin.Koan.Orchestration.Aspire.Abstractions technical notes

`IKoanAspireResources` is the isolated boundary between resource-contributing modules and the functional
`Sylin.Koan.Orchestration.Aspire` projection that discovers and invokes them inside an AppHost.

The interface carries only the Aspire builder, standard configuration/environment inputs, ordering, and a
participation predicate. It deliberately owns no module activation, orchestration mode, container lifecycle,
discovery, or application services.

The package pins the patched MessagePack dependency floor required by the Aspire.Hosting 13.0 graph so every
contributor receives the same safe transitive minimum.
