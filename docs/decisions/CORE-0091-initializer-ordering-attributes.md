# CORE-0091: Initializer ordering via `[Before]` / `[After]` attributes

> **Superseded by [ARCH-0116](ARCH-0116-one-module-lifecycle.md) (2026-07-17).** `[Before]` and
> `[After]` remain, but they now order `KoanModule` types through `ModuleOrdering`; the initializer
> interfaces, registries, and registrar-specific language described below were removed.

Status: Accepted
Date: 2026-05-24

## Contract

- Inputs: concrete types that implement `IKoanInitializer` (including `IKoanAutoRegistrar`, which inherits from it), discovered via `KoanRegistry`.
- Outputs: the same set of types, returned from `KoanRegistry.GetInitializerTypes()` / `GetAutoRegistrarTypes()` in a deterministic order that satisfies all `[Before]` / `[After]` constraints.
- Error Modes: cycles throw at startup with the cycle path in the message; attribute targets that are not assignable to `IKoanInitializer` throw with the offending pair identified.
- Success Criteria: any `Initialize(IServiceCollection)` call that depends on another module having already registered services (e.g., an IStartupFilter that expects `UseRouting` to already be in the pipeline) can declare that dependency explicitly and have it honored on every build, machine, and runtime version.

## Context

CORE-0072 introduced source-generated registries to remove reflection-based assembly scans and stabilize NativeAOT trimming. The generated registrations land in `KoanRegistry`'s `ConcurrentDictionary<Type, byte>` (`_initializerTypes`, `_autoRegistrarTypes`). `GetInitializerTypes()` returns `_initializerTypes.Keys.ToArray()` — and `ConcurrentDictionary` key enumeration is **non-deterministic**: it follows the internal hash-table bucket layout, which depends on hash codes of `RuntimeType` instances and varies with module-load order, app size, and runtime version.

Downstream this matters because `AppBootstrapper.InitializeModules` iterates the returned array and invokes `Initialize(services)` on each type. The order of those calls transitively determines the registration order of `IStartupFilter` instances (since `TryAddEnumerable` preserves insertion order), which in turn determines pipeline composition order.

We hit a concrete instance of this in a downstream app (`the downstream consumer app`): `Koan.Web.Auth.Connector.Test.KoanTestProviderStartupFilter` invokes `app.UseEndpoints(MapKoanTestProviderEndpoints)` to register its mock OAuth routes. That call requires `app.UseRouting()` to have already been added to the pipeline. `KoanWebStartupFilter` is the one that calls `UseRouting()`. When the test connector's filter happens to register and run *before* `Koan.Web`'s filter in the IStartupFilter enumerable, `UseEndpoints` throws `InvalidOperationException: EndpointRoutingMiddleware must be added before EndpointMiddleware`; the test connector logs a warning, skips its route map, and the SPA fallback catches `/.testoauth/*` with a 404. The application worked fine in larger consumers (e.g., `S5.Recs`) because more loaded assemblies altered the hash distribution and pushed `Koan.Web`'s filter ahead. The behavior was thus correctness-by-luck.

Existing .NET ordering primitives don't cleanly solve this case. `IConfigureOptions<T>` / `IPostConfigureOptions<T>` order options configuration, not initializer execution. `IStartupFilter` itself has no priority surface; ASP.NET runs filters in DI registration order. We considered three alternatives:

1. **Integer `Order` property on `IKoanAutoRegistrar`.** Trivial to implement but reproduces the z-index problem: third-party modules have to guess numbers that fit between framework-assigned values, and the values shift every time the framework inserts a new internal module.
2. **Two-phase initialization (`Initialize` + `Finalize`) + per-module extension-point hooks (`AddBeforeAuthentication`, etc.).** Most expressive and aligns with `IConfigureOptions<T>` mental model, but requires a breaking change to `IKoanAutoRegistrar` and grows hook vocabulary per module.
3. **Declarative cross-type ordering via attributes.** Modules state what they need to run before / after by referring to the other module's registrar type. Topological sort produces a deterministic execution order. No magic numbers, no API breakage.

Option 3 wins on cost / value: a single attribute pair plus a topo-sort fixes the failure mode without changing any existing registrar's interface or semantics. The naming follows the existing convention (`[KoanBackgroundService]`, `[NotAutoRegistered]`) and reads naturally at the call site: `[After(typeof(Koan.Web.Initialization.KoanAutoRegistrar))]`.

## Decision

Introduce two opt-in attributes in `Koan.Core/Ordering/`:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class BeforeAttribute : Attribute
{
    public Type[] Targets { get; }
    public BeforeAttribute(params Type[] targets) => Targets = targets ?? [];
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AfterAttribute : Attribute
{
    public Type[] Targets { get; }
    public AfterAttribute(params Type[] targets) => Targets = targets ?? [];
}
```

Add `RegistrarOrdering.Sort(IEnumerable<Type> types)` (Kahn's algorithm) in `Koan.Core/Ordering/`:

- Build a directed graph where `A -> B` means "A must run before B". `[Before(typeof(B))]` on `A` adds edge `A -> B`. `[After(typeof(B))]` on `A` adds edge `B -> A`. The two forms are equivalent and may be combined.
- Stable tie-break for unconstrained nodes by `Type.AssemblyQualifiedName` ordinal comparison, so unrelated modules sort deterministically across machines and runs.
- Cycle detection: when the algorithm cannot find any zero-in-degree node and unprocessed nodes remain, throw `InvalidOperationException` with the residual subgraph rendered as a readable cycle path. The host fails fast — silent fallback hides the design bug.
- Validation: when a target type is not assignable to `IKoanInitializer`, throw at sort time with the offending `(source, target)` pair named. Optional: a `Type` target that doesn't appear in the input set is treated as an "ignored constraint" (warn-on-debug, no error) — this lets a module declare ordering against an optional dependency it doesn't force-reference.

Wire the sort into both `KoanRegistry.GetInitializerTypes()` and `KoanRegistry.GetAutoRegistrarTypes()` so initialization order and provenance order share the same deterministic shape.

Dogfood the new capability by annotating the Koan-shipped auth registrars:

- `Koan.Web.Auth.Initialization.KoanAutoRegistrar` → `[After(typeof(Koan.Web.Initialization.KoanAutoRegistrar))]`
- `Koan.Web.Auth.Connector.{Discord,Google,Microsoft,Oidc,Test}.Initialization.KoanAutoRegistrar` → `[After(typeof(Koan.Web.Auth.Initialization.KoanAutoRegistrar))]`

The Test connector's annotation is the load-bearing one — it fixes the observed `/.testoauth/*` 404 — but annotating the whole auth lineage establishes the canonical dependency chain and prevents the same class of bug from showing up in any of the other connectors when they grow new responsibilities.

### Edge Cases

- A registrar declares both `[Before(X)]` and `[After(X)]` for the same X: cycle detection catches this — the resulting graph has a 1-cycle X ↔ self-declared. Throw with the conflict named.
- An attribute target type is referenced but the assembly defining it is not loaded: the target won't be in the input set, the constraint becomes a no-op (warn-on-debug). Reference = intent is preserved — if you want the ordering enforced, reference the module.
- Two registrars in the same assembly with mutual `[Before]` constraints: throws (real cycle).
- A registrar with `[After(typeof(self))]`: trivially a 1-cycle; throws.
- All targets unreferenced (no constraints anywhere): topological sort degenerates to stable sort by `AssemblyQualifiedName`. Strictly more deterministic than today's `ConcurrentDictionary.Keys` order.
- A registrar implements `IKoanAutoRegistrar` but isn't in the registry (e.g., generator skipped it): not a concern of this ADR.

## Consequences

- The auto-registrar ordering "luck" failure mode disappears. Framework consumers no longer need workarounds like manually calling `app.MapKoanTestProviderEndpoints()` after `builder.Build()`.
- Boot output and provenance descriptions become deterministic across machines and builds.
- No breaking change. Existing registrars without attributes get a strictly more deterministic ordering (AQN-sorted) than today's behavior.
- The source generator is unchanged. Ordering is a runtime concern; the generator continues to emit the type list. Keeps the generator simple and AOT-friendly.
- A small additional reflection cost at startup (reading attributes off each registrar type). Bounded by the registrar count, run once per registry call. Negligible vs. the cost of the `Initialize` calls themselves.
- Establishes a pattern other Koan registries (e.g., `IServiceDiscoveryAdapter`, background services if they grow startup ordering needs) can adopt by sharing the same sort utility.

## Follow-ups

1. If a real "register early, run late" case ever emerges (a registrar wants its `Initialize` to run first but its produced IStartupFilter to run last), add sibling `[AddBefore]` / `[AddAfter]` attributes targeting service-registration phase only, leaving `[Before]` / `[After]` as the runtime / Initialize-call ordering. Until then, the single pair covers the common case.
- Consider extending the source generator (CORE-0072 follow-up #4) to emit a static ordering manifest, avoiding the runtime reflection pass entirely. Optimization, not correctness.
- Add a Roslyn analyzer that detects obvious mistakes (`[Before(typeof(self))]`, targets that aren't `IKoanInitializer`) at compile time.

## References

- `src/Koan.Core/Ordering/BeforeAttribute.cs` (new)
- `src/Koan.Core/Ordering/AfterAttribute.cs` (new)
- `src/Koan.Core/Ordering/RegistrarOrdering.cs` (new)
- `src/Koan.Core/Hosting/Registry/KoanRegistry.cs` (modified — sort applied)
- `src/Koan.Web.Auth/Initialization/KoanAutoRegistrar.cs` (annotated)
- `src/Connectors/Web/Auth/{Discord,Google,Microsoft,Oidc,Test}/Initialization/KoanAutoRegistrar.cs` (annotated)
- CORE-0072 (source-generated registries) — prior decision; this ADR adds runtime ordering on top of the generated registries
- Failure observed in `the downstream consumer app` downstream consumer (`/.testoauth/authorize` returning SPA 404)
