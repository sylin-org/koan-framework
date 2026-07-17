# ARCH-0116: One module lifecycle

**Status**: Accepted
**Date**: 2026-07-17
**Deciders**: Framework maintainer
**Scope**: Package activation, module authoring, startup ordering, provenance, composition evidence,
source generation, runtime fallback, and cross-module contract boundaries.
**Supersedes**: ARCH-0086's compatibility bridge, CORE-0003's initializer lifecycle,
CORE-0072's initializer/registrar catalogs, CORE-0091's initializer-specific ordering, and
DX-0038's registrar convention.
**Related**: ARCH-0001 · ARCH-0105 · ARCH-0114 · ARCH-0115

---

## Decision

Koan has one boot-time authoring and runtime primitive: `KoanModule`.

```csharp
public sealed class AcmeDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddAcmeData();
}
```

An implementation assembly has exactly one concrete, domain-named module. The module's identity is
derived from standard `PackageId`/assembly identity; authors do not declare a Koan ID or descriptor.
The source generator emits a construction-free descriptor, the host constitution decides whether it
is active, and one retained instance owns `Register`, `Start`, `Report`, and `ReportComposition`.

`AddKoan()` is the application entry point. Referencing a capability is intent; applications do not
invoke modules or repeat their service registration.

Contracts that another module may consume live in an isolated Core, Contracts, or Abstractions
assembly with no concrete module. The functional assembly owns activation. Ordinary project/package
references describe the graph; Koan does not admit `Inert`, `Required`, or equivalent reference
metadata to compensate for a misplaced contract.

`[Before]` and `[After]` order `KoanModule` types when exceptional type-safe sequencing is necessary.
Optional capabilities on the same assembly owner use typed .NET interfaces—for example
`IKoanAspireResources`—rather than a second lifecycle object or a class-name convention.

## Runtime contract

1. Source generation and the degraded reflection fallback discover concrete `KoanModule` types only.
2. The compiled host constitution filters and validates descriptors before constructing a module.
3. Registration and startup use the same deterministic module ordering.
4. Provenance and runtime composition evidence come from the retained module instance.
5. A module registration failure rejects the constitution. It cannot continue as a partial host.
6. Startup reporting names active modules, not internal registrars or initializer counts.

There is no compatibility interface, registry, generated table, reflection branch, provenance
reconstruction path, alias, or wrapper for the removed initializer/auto-registrar lifecycle.

## Why

The former lifecycle represented one concern through two interfaces, two generated catalogs, two
registry sets, separate reflection and bootstrap loops, reconstructed provenance objects, and a
compatibility bridge on `KoanModule`. That distributed the same intrinsic complexity across multiple
touchpoints and exposed accidental vocabulary to module authors and operators.

One retained module creates a direct intent-to-code mapping, makes startup inspectable, eliminates
class-name magic, and gives framework code one responsibility chokepoint to optimize and evolve.

## Consequences

- Module authors implement only the lifecycle verbs their concern needs.
- Applications retain the reference plus `AddKoan()` experience.
- Multiple concrete modules in one implementation assembly are a build error; coalesce the concern or
  split the package boundary.
- Contracts assemblies remain inert by structure, not metadata.
- Package identity changes are activation identity changes and follow ordinary packaging discipline.
- Historical documentation may describe the removed lifecycle only when clearly marked superseded.

## Verification

- Source search contains no removed lifecycle interface or registry API in production/test code.
- Generator proof emits the semantic descriptor and genuinely distinct catalogs only.
- Focused Core ordering, module lifecycle, fail-loud bootstrap, FirstUse, and representative pillar
  journeys remain green.
- `git diff --check` and active-documentation checks reject stale bootstrap guidance.
