---
id: ARCH-0107
slug: host-scoped-koanlog
domain: Architecture
status: Accepted
date: 2026-07-14
title: KoanLog follows canonical host and flow ownership
supersedes:
  - ARCH-0057
---

# ARCH-0107: KoanLog follows canonical host and flow ownership

## Context

ARCH-0057 established a useful developer-facing façade: modules keep a reusable
`KoanLog.For<T>()` scope and emit structured stage/action/outcome facts without constructing service
providers. Its implementation introduced two additional lifetime mechanisms: a process-global
`ILoggerFactory` managed by `KoanLogFactoryBridge`, and an `ILogger` cached permanently inside each
scope.

That duplication became unsafe once Koan supported repeated and flow-scoped hosts. Thirteen static
scopes could retain the first host's logger graph after that host stopped. The bridge could select
only one process-wide factory and therefore could not follow `AppHost.PushScope` in concurrent flows.
Meanwhile, `AppHost` already owned the required host-default and flow-local selection semantics.

## Decision

Keep the `KoanLog` façade and remove its independent ownership system:

- `KoanLogScope` retains only its immutable category name.
- Every emission resolves `ILoggerFactory` from `AppHost.Current` and asks that factory for the
  category logger. The standard factory owns any category-level caching.
- `KoanLogFactoryBridge`, the process-global factory field, internal attach/detach methods, and the
  per-scope logger cache are removed.
- `AppHostBinderHostedService` is registered before other Koan hosted services so their startup
  emissions can resolve the current host.
- A hostless flow, a provider without `ILoggerFactory`, or a disposed provider produces no façade
  emission. Resolution remains best-effort and never falls back to another host.
- Overloads that receive an explicit `ILogger` and the internal deterministic test sink are
  unchanged.

The public call shape remains:

```csharp
private static readonly KoanLog.KoanLogScope Log = KoanLog.For<MyModule>();

Log.BootInfo("module.start", "ready");
```

## Ownership contract

| Situation | Selected logger factory |
|---|---|
| Generic host is active | The factory registered by the current binder-owned host. |
| A flow has entered `AppHost.PushScope` | The factory registered by that flow's provider. |
| A newer host replaced the process default | The newer host's factory. |
| An older host stops | The current newer owner remains selected. |
| No host/factory is available | None; the façade no-ops. |

Category scopes are therefore safe as process-static fields because they contain no service,
provider, configuration, or disposable runtime state.

## Consequences

- One ambient owner now governs Entity statics, application identity, and façade logging.
- Concurrent host-aware flows can emit to different logger factories without cross-routing.
- `StartKoan()` gains façade logging through its existing `AppHost` lease. ARCH-0119 later replaced
  the raw-provider bootstrap with the standard Generic Host lifecycle; no logging-specific bridge was
  introduced.
- Each façade call performs one ambient provider lookup. `ILoggerFactory` remains responsible for
  efficient category logger reuse.
- Registrar calls made before any host owns `AppHost` remain intentionally silent; this decision does
  not claim complete pre-host startup reporting.

## Evidence

The Core ownership surface uses one reusable scope across sequential attached hosts, teardown in the
presence of a newer owner, and simultaneous flow scopes. It also fixes the binder as the first Koan
hosted-service registration. Broader Core and Data.Core suites protect formatting and consumer
behavior.

## References

- [ARCH-0057 — KoanLog façade and stage logging centralization](ARCH-0057-koanlog-facade-and-stage-logging.md)
- [Koan product constitution](../architecture/product-constitution.md)
- [R04-02 host-scoped runtime](../initiatives/koan-v1/work-items/r04/R04-02-host-scoped-runtime.md)
- [ARCH-0119 — one console host lifecycle](ARCH-0119-one-console-host-lifecycle.md)
