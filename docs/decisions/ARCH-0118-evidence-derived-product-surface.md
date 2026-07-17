# ARCH-0118: Evidence-derived product surface

**Status**: Accepted
**Date**: 2026-07-17
**Deciders**: Framework maintainer
**Scope**: Public package, capability, platform, evidence, and maturity truth.
**Related**: ARCH-0085 · ARCH-0105 · ARCH-0110 · ARCH-0116 · ARCH-0117

---

## Decision

Koan compiles one product surface from two differently owned forms of truth:

1. `RepositoryInspector` and `PackageGraph` derive mechanical facts from evaluated standard .NET/NuGet projects.
2. `product/claims.json` records only irreducible product judgment: a stable claim, its maturity, packages, evidence,
   and documentation.

`ProductSurfaceCompiler` joins and validates them, then produces every machine and human projection. Package
existence is availability, not support. Packages without a valid claim remain explicitly `unassessed`. Promoted
claims fail closed when packages, owned documentation, evidence, maturity, or graph facts are absent or
contradictory.

`KoanPackageKind` is deleted. Package shape derives from `PackageType`, `PackAsTool`, `IsRoslynComponent`,
`IncludeBuildOutput`, and dependency shape. The old Bundle/Kernel/Periphery taxonomy neither controlled behavior
nor expressed a useful user decision.

Release planning invokes the compiler automatically. Operators never select maturity, reconcile a catalog, or
supply another release input.

## Ownership boundary

Standard project files own package identity, description, tags, target frameworks, package type, documentation
payload, and project dependencies. The claims file must not repeat those facts. It exists only because support is a
human promise that cannot be derived honestly from compilation success.

The packaging compiler is the narrowest correct owner: it already evaluates the complete package graph and owns
release evidence. Runtime Core must not carry repository policy, and individual modules cannot validate product-wide
claims.

## Consequences

- Human docs, agent JSON, and release validation cannot disagree without the compiler failing.
- Root repository README fallback no longer counts as package-owned documentation for a promoted claim.
- New packages are visible but make no support claim until deliberately assessed.
- Module authors use standard .NET metadata and repository-owned evidence; there is no Koan module/package
  descriptor ceremony.
- The deprecated manual module catalog is replaced by a generated projection rather than repaired by hand.

## Verification

- Focused compiler tests cover deterministic ordering and every fail-closed boundary.
- The real repository surface compiles and its Markdown projection matches the checked-in reference.
- Release-planner tests prove surface validation is automatic and operator-free.
- No broad release certification or publication is required for this structural slice.
