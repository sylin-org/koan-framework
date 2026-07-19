---
type: ARCH
domain: framework
title: "Adapter registration and external topology"
audience: [developers, architects, maintainers]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
---

# Adapter registration and external topology

> **Contract**
>
> - **Input:** a referenced functional provider package and host configuration.
> - **Result:** one discovered Koan module registers provider mechanics, discovery, health, and provenance.
> - **Topology boundary:** the application or its standard deployment tool creates infrastructure.
> - **Correction:** invalid provider configuration or an unreachable elected dependency fails through the owning
>   connector; Koan never silently starts a parallel container lifecycle.

## Provider module pattern

Every functional provider package supplies exactly one domain-named `KoanModule` that:

- registers its contracts with standard .NET DI;
- binds options without constructing a temporary `ServiceProvider`;
- appends an `IServiceDiscoveryAdapter` and health contributor with `TryAddEnumerable`;
- registers provider factories or clients at their narrow lifecycle owner;
- reports package configuration through `Report(...)` and resolved election/runtime evidence through
  `ReportComposition(...)`; and
- leaves the functional pillar responsible for election and policy.

```csharp
public sealed class AcmeDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AcmeOptions>();
        services.AddSingleton<IConfigureOptions<AcmeOptions>, AcmeOptionsConfigurator>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, AcmeDiscoveryAdapter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHealthContributor, AcmeHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, AcmeAdapterFactory>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Acme discovery is handled by AcmeDiscoveryAdapter");
    }
}
```

Identity comes from standard package/assembly metadata. Do not add a custom activation attribute, descriptor, module
ID, provider-specific registration helper, orchestration evaluator, or AppHost contributor interface.

## Aspire and other topology owners

Koan V1 publishes no bespoke CLI, hosting-provider SPI, manifest generator, Compose renderer, Aspire package, or
in-application self-container runtime. Applications use the standard tool that owns their topology.

An Aspire application authors ordinary AppHost code:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("postgres");
builder.AddProject<Projects.App>("app").WithReference(postgres);
await builder.Build().RunAsync();
```

The application references its Koan Postgres connector normally. Aspire injects `ConnectionStrings:postgres` and
service endpoints; Koan's existing connector discovery consumes them. Compose, Docker, Podman, Kubernetes, managed
services, and test harnesses follow the same ownership rule: they create topology, while Koan discovers and uses it.

This keeps topology readable where the application declares it and avoids assembly scans, contributor priorities,
temporary containers, process-environment mutation, and two lifecycle authorities.

## Checklist

- [ ] One constructible, domain-named `KoanModule` owns registration.
- [ ] Options bind through the host configuration pipeline.
- [ ] Provider candidates use standard `TryAddEnumerable` registration.
- [ ] Discovery understands explicit configuration and applicable external-tool endpoints.
- [ ] Health is critical only for elected or runtime-participating routes.
- [ ] Provenance explains configuration and resolved mechanics without secrets.
- [ ] A focused real-backend test proves the connector's guarantee.
- [ ] External topology is shown with standard tooling, not a Koan lifecycle abstraction.

This is the active registration standard. Historical orchestration proposals and shelved source are not V1 contracts.
