---
id: ARCH-0136
slug: every-composition-entry-path-is-the-whole-composition
domain: Architecture
status: Accepted
date: 2026-10-21
title: Every composition entry path is the whole composition
related:
  - ARCH-0065
  - ARCH-0115
  - ARCH-0079
---

# ARCH-0136: Every composition entry path is the whole composition

## Context

A Koan host is composed in two steps that only make sense together. Module initialization activates the modules
the application declared and runs their `Register`. The outermost composition lease then compiles the typed
contributions those modules declare (hard segmentation, AI providers, discovery sources) into immutable plans, and
freezes the session. ARCH-0115 makes the second step the only producer of `SegmentationPlan`; until it runs, the
registered plan is `SegmentationPlan.Empty`.

Three public entry paths reached the first step without the second:

- `AddKoanDataCore()` called `AppBootstrapper.InitializeModules` directly, outside any lease. Every referenced
  module registered, Koan.Tenancy among them, and nothing compiled. The host ran with no tenant field, no read
  filter and no `SegmentationRequiredException`: cross-tenant reads and writes, with no error and no fact. The
  framework's own corrective messages recommended this entry point (ledger NK-034).
- `AppBootstrapper.InitializeModules` is public and had the same shape for any caller.
- `StartKoan()` copies the caller's descriptors, including an unfrozen composition session, into a new host
  builder. The session wrote everything it compiled into the collection it was created for, so the running host
  kept the empty plans while the abandoned collection received the compiled ones. `StartKoan()` also skipped
  composition whenever an `IAppRuntime` was registered, which `AddKoanCore()` alone does, so a collection prepared
  with `AddKoanCore()` started with no modules at all (ledger NK-035).
- `AddKoanCore()` followed by `AddKoan()` (or `AddKoanDataCore()`) threw "already has a compiler for this host",
  because `AddKoanCore()` scheduled Core's contribution target every time it was called.

## Decision

**Any path that activates modules compiles them, into the collection that is built.**

1. `AppBootstrapper.InitializeModules` called outside an open composition lease runs the complete composition
   (`AddKoan()`). Inside a lease it is unchanged.
2. `AddKoanDataCore()` is `AddKoan()` plus the Data core services it always guaranteed. It no longer has a
   composition of its own.
3. The session's outermost lease names the collection it composes (`Enter(services)`), and the constitution, the
   compiled plans and the compilation snapshot are written there. A nested lease for a different collection is
   refused.
4. `StartKoan()` always calls `AddKoan()` on the collection it builds. Over a frozen composition that is a no-op;
   over a partially composed one it finishes the composition into the new host. A caller-supplied `IAppRuntime` is
   kept, because `AddKoanCore()` only adds the default runtime when none is registered.
5. `AddKoanCore()` is idempotent per collection: an explicit call before `AddKoan()` is not a second Core.
6. A `StartKoan()` host whose start fails part-way is stopped before it is disposed. The hosted services that had
   already started, the ambient-host binder among them, release what they hold, so `AppHost.Current` never points at
   the disposed provider. Only the start failure reaches the caller.

No public signature changes. The corrective messages that recommended `AddKoanDataCore()` now recommend `AddKoan()`.

## Compatibility

- A host built with `AddKoanDataCore()` now compiles contributions. With Koan.Tenancy referenced, an unscoped
  tenant-scoped operation that silently succeeded now throws `SegmentationRequiredException`, as it does under
  `AddKoan()`; AI providers and discovery sources contributed by connectors appear where they were missing.
- `AddKoanDataCore()` now also registers what `AddKoan()` registers (Core hosted services, facts, health). A test or
  tool that built a bare provider from it gets the full Core substrate.
- `StartKoan()` over a collection that registered its own `IAppRuntime` without `AddKoan()` now composes Koan around
  that runtime instead of starting a host with no Koan modules.
- `AddKoanCore()` called twice, or before `AddKoan()`, no longer throws.
- A `StartKoan()` whose start throws now runs `StopAsync` on the partially started host before disposing it.

## Guarantee and correction

- Whatever public entry point composed the host, an active module's typed contributions are compiled into the
  provider the application resolves. There is no supported path to an active Tenancy on an empty plan.
- The correction for a data-only or console host is unchanged in spelling: `AddKoan()`, `AddKoanDataCore()` or
  `StartKoan()` are each the whole composition.

## Evidence

- `CompositionEntryPathSpec` (Koan.Tenancy.Tests): `AddKoanDataCore()` compiles the `tenant` dimension and refuses an
  unscoped upsert; `AddKoanCore()` then `AddKoanDataCore()` then `AddKoan()` composes one host; `StartKoan()` over
  `AddKoanDataCore()` and over `AddKoanCore()` runs with the compiled dimension, and the caller's collection holds no
  compiled plan; `StartKoan()` over `AddKoan()` keeps its composition. Four of the five failed before this change
  (empty plan, or the duplicate-compiler exception).
- `SemanticCompositionSessionSpec` (Koan.Core.Tests): a session copied before it freezes commits into the collection
  being composed and not into the original; a nested lease for another collection is refused; `AddKoanCore()` is
  one Core per collection.
- `StartKoanHostOwnershipSpec.Failed_runtime_start_releases_provider_and_owned_services` (Koan.Tests.Data.Core) failed
  once `StartKoan()` composed around a caller-supplied runtime: the binder had attached the ambient host and the
  failed start never stopped it. It passes with item 6.
