# ARCH-0086: KoanModule — the unified boot-time module primitive

**Status**: Accepted (2026-06-02) — additive-base approach + keep `[Before]/[After]` ordering, both signed off by the Enterprise Architect. Implementation gated on the Facet 0 green ratchet (standing).
**Date**: 2026-06-02
**Deciders**: Enterprise Architect
**Scope**: Facet 2 of the [foundation consolidation plan](../architecture/foundation-consolidation-plan.md): the **boot-time** registration + bootstrap + self-report story. Lands `KoanModule` in `Koan.Core` as the single primitive an assembly author writes, *over* the existing source-generated discovery (`KoanRegistry`) and topological ordering (`RegistrarOrdering`). Builds on the Facet 1 capability model (ARCH-0084) but — see the granularity finding below — does **not** host it.
**Related**: [foundation-consolidation-plan.md](../architecture/foundation-consolidation-plan.md) · folds in `IKoanInitializer`, the provenance half of `IKoanAutoRegistrar`, and the `IKoanBackgroundService` startup concept · fixes the off-registry `IKoanAuthEventContributor` scan (WEB-0065) · ARCH-0084 said Facet 2 would "host `Describe(ICapabilities)`" — **this ADR corrects that** (capabilities are a per-provider runtime concern, not a per-module boot concern).

---

## Context

The registration/bootstrap surface was the redesign's target #2 ("split-brain: 85 `KoanAutoRegistrar` + 7 interfaces + ~30 `Add*`"). A deep read of the *machinery* (not just the surface count) found the split-brain is **narrower than the first-pass audit framed it** — most of the apparatus is sound:

- **Discovery is already build-time-generated.** `Koan.Core.Registry.Generators.RegistrySourceGenerator` emits a `[ModuleInitializer]` per assembly that populates the static `KoanRegistry` with initializer / auto-registrar / background-service / discovery-adapter types; `RegistryManifestLoader` is a runtime-reflection *fallback* for assemblies the generator didn't process. Not a reflection mess.
- **Ordering is already declarative + topological.** `RegistrarOrdering.Sort` runs Kahn's algorithm over `[Before]`/`[After]` attributes with cycle detection and a stable `AssemblyQualifiedName` tie-break.
- **The `Add*` methods are mostly the registrar's own delegate.** The common shape is `KoanAutoRegistrar.Initialize() → services.AddKoanCache()` — one implementation, two entry points (auto + manual). That is the *good* pattern, not duplication.
- **The 7 interfaces are distinct concerns, not overlapping.** `IKoanAspireRegistrar` (distributed-app resources, 3 impls), `IKoanAdminManifestService` (an admin service, 1 impl), `IKoanManifest`/`[KoanApp]` (app-metadata anchor), and the `IKoanBackgroundService` family (runtime work) each solve a different problem from `IKoanAutoRegistrar`.

### The genuinely real smells (the narrow target)

1. **`IKoanAutoRegistrar` carries two jobs.** `Initialize(IServiceCollection)` (DI) **and** `Describe(ProvenanceModuleWriter, cfg, env)` (self-report) — fine to keep together, but the surface reads as two concerns and the verb `Describe` now collides conceptually with Facet 1's `IDescribesCapabilities.Describe`.
2. **No first-class `Start` lifecycle.** Startup work is scattered: registrars call `AddHostedService<T>()` inside `Initialize`, *and* there is a parallel `IKoanBackgroundService`/`IKoanStartupService` family run by `KoanBackgroundServiceOrchestrator`, *and* ad-hoc per-pillar `IHostedService`s (`AuthBootstrapHostedService`, `RoleBootstrapHostedService`, `CachePolicyBootstrapper`, …). A module has no single "run my startup work, in order" verb.
3. **`IKoanInitializer` (8 impls) duplicates `IKoanAutoRegistrar` minus the report.** Two interfaces for "register DI at boot."
4. **`IKoanAuthEventContributor` is the one true off-registry split-brain.** `Koan.Web.Auth` runs its *own* `AppDomain.CurrentDomain.GetAssemblies()` reflection scan (`ServiceCollectionExtensions.DiscoverAndRegisterAuthEventContributors`) instead of going through `KoanRegistry`, so those contributors are invisible to the central registry/provenance and fail silently if discovery misses them.

### The crucial granularity finding (corrects the original plan)

ARCH-0084 and the consolidation plan assumed `KoanModule` would host `Describe(ICapabilities)` — one unit declaring *both* DI and capabilities. **The research shows this conflates two granularities.** Facet 1's `IDescribesCapabilities` is implemented by the **per-type runtime provider** (`PostgresRepository<T>`, resolved per entity type, at request time). The registrar is **per-assembly, at boot, a singleton**. A boot-time module does not know — and should not know — the per-type capability set of every provider it registers. Capabilities are already correctly located on the provider (Facet 1, done). **`KoanModule` therefore does not host `Describe(ICapabilities)`.** This removes a large piece of the original KoanModule rationale and refocuses Facet 2 on the *boot* story only: identity, ordering, register, start, report.

### Forces

1. **The author writes one unit today** (`KoanAutoRegistrar`) and should still write one — the win is lifecycle clarity (a real `Start`) and losing the `Initializer`/`AutoRegistrar` duplication, **not** a different count.
2. **The machinery (source-gen discovery, topological ordering) works** and is the framework's fast-boot backbone — replacing it is pure risk. Build *over* it.
3. **85 registrars across ~85 assemblies.** A big-bang rename is an enormous, low-reward mechanical change for a system that already functions. The green ratchet + dogfood discipline favor an additive, opportunistic migration.
4. **Reference = Intent must keep working** — discovery is by assembly reference; the module primitive must be discovered by the *same* `KoanRegistry` path with zero per-app wiring.

---

## Decision

Introduce **`KoanModule`** as an `abstract class` in `Koan.Core` that **implements `IKoanAutoRegistrar`** — an *additive* clean surface over the unchanged discovery + ordering machinery. Keep `[Before]`/`[After]` ordering.

```csharp
public abstract class KoanModule : IKoanAutoRegistrar
{
    /// <summary>Canonical module id, e.g. "data.postgres". (Surfaces as IKoanAutoRegistrar.ModuleName.)</summary>
    public abstract string Id { get; }

    /// <summary>Module version; defaults to the declaring assembly version.</summary>
    public virtual string? Version => GetType().Assembly.GetName().Version?.ToString();

    /// <summary>Register this module's services. (Replaces IKoanInitializer.Initialize.)</summary>
    public virtual void Register(IServiceCollection services) { }

    /// <summary>Run one-time startup work, in DependsOn order, with DI available. First-class lifecycle
    /// — folds the "register a bootstrap IHostedService in Initialize" pattern into one verb.</summary>
    public virtual Task Start(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Publish the module's provenance self-report. (Renamed from the provenance Describe to
    /// disambiguate from the capability IDescribesCapabilities.Describe — they are different objects.)</summary>
    public virtual void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);

    // --- IKoanAutoRegistrar bridge: existing source-gen discovery + RegistrarOrdering work UNCHANGED ---
    string IKoanAutoRegistrar.ModuleName => Id;
    string? IKoanAutoRegistrar.ModuleVersion => Version;
    void IKoanInitializer.Initialize(IServiceCollection services)
    {
        Register(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(KoanModule), GetType())); // for Start
    }
    void IKoanAutoRegistrar.Describe(ProvenanceModuleWriter m, IConfiguration c, IHostEnvironment e) => Report(m, c, e);
}
```

### How `Start` runs

`Koan.Core`'s own module registers a single `KoanModuleHost : IHostedService`. On `StartAsync` it resolves `IEnumerable<KoanModule>` from DI, orders them with the **same `RegistrarOrdering`** (so `[Before]`/`[After]` govern start order too — today's hosted services have no shared ordering), and `await`s each `module.Start(sp, ct)`. The per-pillar bootstrap hosted services (`AuthBootstrapHostedService`, `RoleBootstrapHostedService`, `CachePolicyBootstrapper`, …) collapse into their module's `Start` override as they migrate. The `IKoanBackgroundService` family stays as-is for **periodic/pokable** work — `Start` is for one-time ordered startup, which that family does not model well.

### The four targeted fixes (the real content of Facet 2)

1. **`KoanModule` subsumes `IKoanAutoRegistrar` + `IKoanInitializer`** via the bridge — authors write `KoanModule`; the two interfaces become internal plumbing (kept, since 85 registrars still implement them, but no longer the authoring surface).
2. **First-class `Start`** + `KoanModuleHost` (new lifecycle; folds scattered bootstrap).
3. **Route off-registry discovery through `KoanRegistry`** — generalized to a `[KoanDiscoverable]` interface marker rather than special-casing one interface in the generator. Any interface marked `[KoanDiscoverable]` has its implementers auto-registered (build-time generator + runtime `RegistryManifestLoader` fallback), keyed by the interface `Type`, and queried with `KoanRegistry.GetDiscoveredImplementors(typeof(T))`. `Koan.Web.Auth` marks **both** `IKoanAuthEventContributor` and `IKoanAuthFlowHandler` and deletes **both** bespoke `AppDomain` scans. Kills the off-registry split-brain class — not just the one instance — and fixes the latent "misses lazily-loaded assemblies" bug (the bootstrap force-loads the closure before any registrar reads the registry).
4. **`Report` rename** disambiguates the two `Describe`s.

### Out of scope (stays as-is — distinct concerns)

`IKoanAspireRegistrar` (optional Aspire-only mixin), `IKoanAdminManifestService` (a service), `IKoanManifest`/`[KoanApp]` (app-metadata anchor, declares no DI/Start), and `IDescribesCapabilities` (per-provider runtime capability — the granularity finding). None fit `Id/DependsOn/Register/Start` and none should be forced to.

### Staged migration ledger (green at every step; gated by the Facet 0 ratchet)

- **(a) Additive foundation. ✅ DONE (29b27e3c).** Landed `KoanModule` + `KoanModuleHost`. Nothing migrated; everything compiles and runs unchanged. Conformance: a tiny `KoanModule` discovers, registers, reports, and starts through the existing pipeline (`KoanModuleTests`, Core 179/179). _(The generator teaching moved to stage (b), where it landed as the generic `[KoanDiscoverable]` mechanism.)_
- **(b) Auth de-split. ✅ DONE.** Added the generic `[KoanDiscoverable]` marker + `KoanRegistry.Register/GetDiscoveredImplementors` (Type-keyed) + generator detection/emission + `RegistryManifestLoader` runtime fallback. Marked `IKoanAuthEventContributor` **and** `IKoanAuthFlowHandler`; deleted both `AppDomain` scans in `Koan.Web.Auth`. Canon: `AuthDiscoverableContributorSpec` proves end-to-end through real `AddKoan()` that the built-in contributor is discovered via the registry, wired into scoped DI, and that the legacy wrapper stays out of the registration (ARCH-0079). Build green; Core 179/179, Web.Auth 9/9, Bootstrap auth specs 3/3.
- **(c) Opportunistic migration.** Convert registrars to `KoanModule` when a pillar is already being touched — collapse its bootstrap hosted service into `Start`, its `Initialize` into `Register`, its `Describe` into `Report`. Drive from dogfood; never a big-bang. Fold the 8 `IKoanInitializer`-only types first (smallest, clearest win).
- **(d) Settle.** When the dogfood apps' pillars are migrated and a new pillar can be authored as one `KoanModule` with an ordered `Start`, Facet 2 is "settled." `IKoanInitializer`/`IKoanAutoRegistrar` may remain as the internal discovered interfaces indefinitely (invisible plumbing) — deleting them is **not** a goal.

---

## Consequences

### Positive
- **One authoring surface** (`KoanModule`) with a clear five-part shape (Id, DependsOn via attributes, Register, Start, Report) — replacing the `Initializer`/`AutoRegistrar` pair + the implicit "register a hosted service for startup" idiom.
- **A real, ordered `Start` lifecycle** — startup work is declarative and topologically ordered, not scattered across hosted services with no shared ordering.
- **The one true split-brain (auth off-registry scan) is fixed**, and the registry becomes the single discovery authority.
- **Zero discovery/ordering churn** — the source generator + `KoanRegistry` + `RegistrarOrdering` are reused exactly; Reference = Intent is untouched.
- **Low risk, no big-bang** — 85 registrars keep working through the bridge; migration is opportunistic and green-at-each.

### Negative
- **Two surfaces coexist during migration** — `KoanModule` (new authoring) and raw `IKoanAutoRegistrar` (85 unmigrated). Acceptable and intended: the bridge makes them behave identically; the ratchet keeps both green.
- **`KoanModule` is a base class, not an interface** — a module can't also extend another base. In practice registrars extend nothing, so this is a non-issue; the rare exception keeps implementing `IKoanAutoRegistrar` directly.

### Neutral
- **Capabilities stay on the provider** (`IDescribesCapabilities`, ARCH-0084) — this ADR explicitly does not move them onto the module, correcting the original plan. Self-report therefore has two honest sources: per-module provenance (`Report`) and per-provider capabilities (`caps.All`), rendered together by the boot report.
- **`IKoanAspireRegistrar` and the admin/manifest services are left alone** by design.

---

## Alternatives considered

- **Full big-bang migration (rename all 85 registrars to `KoanModule`, delete `IKoanAutoRegistrar`/`IKoanInitializer`).** Rejected: an ~85-assembly mechanical change for largely a rename + lifecycle reshape, since discovery/ordering/`Add*` already work. The author still writes *one* unit either way, so the concept-count win is the same as the additive approach at a fraction of the risk. (Architect-confirmed.)
- **String-id `DependsOn` instead of `[Before]`/`[After]`.** Rejected: the existing attribute mechanism is already declarative, expressive, and cycle-detecting, and type references give compile-time safety. String ids would only add decoupling at the cost of a new id-resolution + cycle-check pass replacing a working one. (Architect-confirmed.)
- **Host `Describe(ICapabilities)` on the module (the original plan).** Rejected on the granularity finding: capabilities are per-type/runtime (the provider), not per-assembly/boot (the module). Forcing them onto the module would be incorrect; Facet 1 already solved them in the right place.
- **Unify the `IKoanBackgroundService` family into `Start`.** Rejected: `Start` models one-time ordered startup; periodic/pokable recurring work is a genuinely different lifecycle the family models well. They coexist.
