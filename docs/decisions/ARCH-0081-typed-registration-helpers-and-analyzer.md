# ARCH-0081: Typed Registration Helpers + Analyzer-Enforced Canon

**Status**: Accepted
**Date**: 2026-05-16
**Deciders**: Enterprise Architect
**Scope**: Service registration in `Koan.Cache.*` (extends to other pillars over time)
**Related**: ARCH-0079 (integration tests as canon), ARCH-0080 (shared transport ownership)

---

## Context

The `TryAddEnumerable(ServiceDescriptor.Singleton<I*>(factory))` descriptor bug (commit 14a5e8ce) was caught and fixed at 8 sites: 5 in the cache pillar + 3 latent (Recipe, Web.Extensions). The fix was mechanical — switch the single-generic factory overload to the two-generic `Singleton<TService, TImplementation>(factory)` form. Without enforcement, the next adapter author can reintroduce the bug.

Phase 3.4 of the cache-pillar work codified the fix as a typed helper:

```csharp
// Helper does the right thing.
services.AddCacheStore<RedisCacheStore>();

// Equivalent expanded form. Easy to get wrong.
services.TryAddSingleton<RedisCacheStore>();
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICacheStore, RedisCacheStore>(
        sp => sp.GetRequiredService<RedisCacheStore>()));
```

The helper is a behavior guard rail — adapter authors who use it can't get the descriptor shape wrong. But the bare-descriptor form is still legal C# and would compile silently in the next adapter.

### Forces

1. **Mechanical fix isn't enough.** A previously-broken pattern can be reintroduced verbatim by anyone unaware of the history. The framework's adapter ecosystem is open — third-party adapters won't have the same review pressure.
2. **Roslyn analyzers are the framework's existing tool of choice.** `Koan.Core.Registry.Generators` already ships source-gen infrastructure as a Roslyn component (`IsRoslynComponent = true`, netstandard2.0, packed as `analyzers/dotnet/cs`). Adding an analyzer follows the existing pattern.
3. **Diagnostic specificity matters.** A blanket "no `TryAddEnumerable` with factory" is too aggressive — there are legitimate use cases. The framework only cares about its own interfaces (`ICacheStore`, `ICacheCoherenceChannel`, etc.) where it ships typed helpers.
4. **Compile-time errors > runtime errors > integration-test catches.** The earlier the bug surfaces, the cheaper it is to fix. Analyzers give compile-time errors.

---

## Decision

The framework ships **typed registration helpers** for each shared-interface registration pattern, and a **Roslyn analyzer** that flags the bare-descriptor form for those interfaces.

### Part 1: Typed helpers (landed in commit c8a6c810)

In `Koan.Cache.Abstractions.Extensions.CacheRegistrationExtensions`:

| Helper | Replaces |
|---|---|
| `services.AddCacheStore<T>()` where `T : ICacheStore` | `TryAddSingleton<T>()` + `TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore, T>(...))` |
| `services.AddCoherenceChannel<T>()` where `T : ICacheCoherenceChannel` | Same pattern |

Future pillars can add their own helpers as they accumulate shared-interface registrations.

### Part 2: Analyzer

A new project `src/Koan.Cache.Analyzers/Koan.Cache.Analyzers.csproj` ships a `DiagnosticAnalyzer`:

- **Diagnostic ID**: `KOAN0001`
- **Title**: "Use typed cache registration helper"
- **Severity**: `Warning` (escalates to `Error` under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, which `tests/Directory.Build.props` already enables for test projects)
- **What it flags**:
  - `TryAddEnumerable(ServiceDescriptor.Singleton<I>(...))` where `I` is `Koan.Cache.Abstractions.Stores.ICacheStore` or `Koan.Cache.Abstractions.Coherence.ICacheCoherenceChannel`
  - The single-generic form, regardless of whether a factory is supplied
- **What it doesn't flag**:
  - The two-generic `Singleton<TService, TImplementation>(...)` form (correct shape)
  - `TryAddEnumerable` against framework-foreign interfaces (not the framework's concern)
- **Message**: "Use `services.AddCacheStore<{TypeName}>()` or `services.AddCoherenceChannel<{TypeName}>()` instead. See ARCH-0081 for the rationale."
- **Help link**: points at this ADR.

### Packaging

The analyzer ships as a Roslyn component (matching the existing `Koan.Core.Registry.Generators` pattern):
- `netstandard2.0` target
- `IsRoslynComponent = true`
- `IncludeBuildOutput = false`
- `Pack = true` with `PackagePath = analyzers/dotnet/cs`

Cache adapter packages reference the analyzer via `<ProjectReference Include="..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` so the analyzer fires at compile time without becoming a runtime dependency.

### Scope and roll-out

For this branch, the analyzer:
1. Ships as a project (compilable, distributable).
2. Is wired into at least one cache adapter project so the diagnostic actually fires during build.
3. Is verified by a synthetic violation (a test that asserts the analyzer's diagnostic is emitted).

Rolling the analyzer into every cache adapter happens iteratively. Adding it to future pillars (Data, Messaging) is the same pattern with their interface lists.

---

## Consequences

### Positive

- **Bug class eliminated at compile time.** Adapter authors get a clear diagnostic with a fix recommendation. Third-party authors get the same protection.
- **Helper + analyzer is a self-reinforcing pattern.** The helper is the recommended path; the analyzer enforces it. No drift between intent and reality.
- **Generalizes.** Future shared-interface registration patterns (data adapters, messaging consumers) follow the same shape.

### Negative

- **More packages to maintain.** `Koan.Cache.Analyzers` joins the framework's analyzer/generator footprint. Cost is small — analyzers are stable, low-churn code — but real.
- **Slightly slower compilation.** Analyzers add a small overhead per file. Negligible in practice but worth noting.

### Neutral

- **The two-generic helper form remains accessible.** The analyzer doesn't ban it; users with legitimate exotic patterns can still write the bare descriptor with `#pragma warning disable KOAN0001` (and a justification comment). Suppression with intent is canon; silent reintroduction is not.

---

## Notes for reviewers

- The analyzer is **narrow by design**: cache interfaces only, in this branch. Adding the data pillar's interfaces (`IDataAdapterFactory`, etc.) is a follow-up when those interfaces stabilize their typed-helper story.
- The diagnostic ID `KOAN0001` claims the first slot in the framework's diagnostic namespace. Future analyzers should consecutively number (`KOAN0002`, etc.) and live alongside or adjacent to this one.
- Synthetic-violation test lives in the analyzer project rather than a separate test project — keeps the analyzer self-contained for the v1 ship.
