---
id: ARCH-0138
slug: module-discovery-failures-are-boot-failures
domain: Architecture
status: Accepted
date: 2026-10-21
title: Module discovery failures are boot failures, and only active modules contribute
related:
  - ARCH-0086
  - ARCH-0114
  - ARCH-0116
  - ARCH-0101
  - CORE-0091
---

# ARCH-0138: Module discovery failures are boot failures, and only active modules contribute

## Context

`AppBootstrapper` reaches module assemblies through four prongs: the assemblies already loaded, a reference
fixpoint, the build-embedded module manifest, and a `Koan.*.dll` directory scan. Each new assembly is handed to the
registry manifest loader. An exception escaping that loader is a framework defect, so `RunManifestLoader` records a
`koan.bootstrap.module.rejected` fact and throws `KoanBootException` unless `KOAN_BOOT_LENIENT=1`.

Three defects broke that promise.

- **Scan failures were swallowed.** The last three prongs call the loader inside a bare `catch` meant for an
  unloadable reference, so the `KoanBootException` became a counted "lenient skip" whatever `KOAN_BOOT_LENIENT` said,
  and boot continued with whatever the failing scan had registered before it threw. At `AddKoan()` time a typical
  application has loaded only its entry assembly and Koan.Core, so almost every module assembly is reached through a
  swallowing prong. The policy decided only for the assemblies that needed it least.
- **Ordering failures escaped the boot contract.** `[Before]`/`[After]` validation (a target that is not a
  `KoanModule`, a self-reference, a cycle) throws a plain `InvalidOperationException` from `ModuleOrdering.Sort`.
  `AppBootstrapper` catches only the runtime's own registration exception, so these failures produced no fact, no
  `MODULES-FAILED` entry and no `KoanBootException`. Composition still faulted, but nothing named the module.
- **Inactive modules contributed data axes.** `DataAxisExpander.ExpandDiscovered` instantiated and expanded every
  discovered `IDataAxis`, including those declared in an assembly whose module the constitution had left inactive
  (present but not referenced, or side-loaded and found by the directory scan). Such an axis changed read filtering,
  stamped managed fields and overrode deletes in a host that never declared it.

## Decision

1. **A boot failure is not a lenient skip.** The prongs keep counting what they were written to tolerate (an
   unresolvable reference, an unreadable file or manifest) and let `KoanBootException` through. `KOAN_BOOT_LENIENT=1`
   is the only switch that turns a manifest-loader failure into a recorded skip, for every prong.
2. **An ordering failure is a module registration failure.** `ModuleOrdering` reports the module whose declaration
   is invalid, and the semantic runtime turns it into a `module-ordering-invalid` problem owned by that module. The
   existing registration path then records the failure, publishes the fact and throws `KoanBootException` with phase
   `register`. The public `ModuleOrdering.Sort` still throws `InvalidOperationException`.
3. **Only active modules contribute discovered axes.** `KoanRegistry.GetDiscoveredImplementors(contract, services)`
   returns the discovered implementors less those declared in an assembly whose Koan modules are all inactive in the
   composing host's constitution. An assembly without a module (the application's own libraries) and every assembly
   of a degraded constitution (no reference manifest, every loaded module active) are kept. `DataAxisExpander` uses it.

## Compatibility

- A module assembly whose manifest scan throws now stops boot with `KoanBootException`, or is recorded as a failure
  under `KOAN_BOOT_LENIENT=1`, wherever it was found. It used to boot silently with a partial registry.
- An invalid `[Before]`/`[After]` declaration now surfaces as `KoanBootException` naming the module, with a
  `koan.bootstrap.module.rejected` fact, instead of a bare `InvalidOperationException`.
- A data axis declared in an inactive or side-loaded module assembly no longer applies. To use it, reference the
  package that declares it, which activates its module.
- No public signature changes. The `GetDiscoveredImplementors` overload is additive.

## Not decided here

`[KoanDiscoverable]` contracts other than `IDataAxis` still read the ungated process-wide registry: jobs
(`JobTypeRegistry`), Canon models and pipeline contributors, Identity access and erasure contributors, Web access
realizations, Web.Auth contracts and Communication handlers. Each consumer should move to the host-scoped overload
in its own change, after deciding what an implementor in an inactive assembly means for that pillar.

## Evidence

- `ModuleScanFailLoudSpec` (Koan.Tests.Integration.Bootstrap): a `KoanBootException` raised while the embedded
  manifest prong hands an assembly to the loader escapes `LoadIntentModulesFromManifest`; before this change it was
  counted as a skip and boot continued. The shared scan step (`TryScan`) that the reference-fixpoint, manifest and
  directory prongs now all use lets the same exception through, counts an unloadable reference as a skip, and does
  nothing when there is no new assembly to scan.
- `ModuleOrderingFailureSpec` (Koan.Core.Tests): an invalid `[Before]` target makes registration fail with a
  `module-ordering-invalid` problem owned by the declaring module, and a cycle is owned by a module on it; before,
  both escaped as a bare `InvalidOperationException`.
- `DiscoveredImplementorActivationSpec` (Koan.Core.Tests): an implementor whose assembly holds only an inactive module
  is excluded for that host, kept for a degraded constitution, kept for an assembly with an active module, and kept
  before module initialization. `DataAxisExpander.ExpandDiscovered` reads through it.
