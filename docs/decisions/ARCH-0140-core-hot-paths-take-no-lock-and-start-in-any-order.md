---
id: ARCH-0140
slug: core-hot-paths-take-no-lock-and-start-in-any-order
domain: Architecture
status: Accepted
date: 2026-10-30
title: Recording an unchanged fact takes no lock, and Koan starts before any hosted service whatever the registration order
related:
  - ARCH-0111
  - DATA-0162
  - ARCH-0079
  - ARCH-0139
---

# ARCH-0140: Recording an unchanged fact takes no lock, and Koan starts before any hosted service whatever the registration order

## Context

Measured at `51b2c33`. `KoanRuntimeFactStore.Record` (ARCH-0111) took the store's one lock and sorted every fact into a
new envelope on every call. Data records a fact on every query it finishes in memory and on every stream, and those
facts repeat the same decision each time, so under load every such read serialized on one lock and allocated and
sorted the whole fact set to say nothing new.

`AppHostBinderHostedService` attached `AppHost.Current`, and `KoanModuleHost` ran every module's `Start` (Data hydrates
its default route there, "before any hosted workload"), in their `StartAsync`. The host starts hosted services in
registration order, so an application hosted service registered before `AddKoan()` ran its `StartAsync` before the host
was attached and before the routes were hydrated, and an `Entity<T>` static in it threw `KoanHostContextException`.

## Decision

1. **An unchanged fact is not a write.** Recording a fact whose id the store already holds with the same kind, state,
   summary, reason, correction, source and correlation (everything but its observation time) reads one concurrent
   dictionary entry and returns: no lock, no allocation beyond the fact itself, no new envelope. The held fact keeps
   the time it was first observed in that form.
2. **The envelope is built when it is read.** A changed fact, or a recollection, is written under the store's gate and
   marks the envelope stale. `Current` returns the built envelope without a lock while nothing changed, and builds the
   ordered envelope once, under the gate, when it is stale. Its `Sequence` counts the changes, as it did when every
   change built an envelope, and is the version it was built at: the lock-free read compares the envelope's own
   `Sequence` with the store's version, so it can never pair one build's envelope with another build's version and
   miss a change that finished before the read began.

3. **Koan starts in the host's starting phase.** The binder, runtime discovery (`AppRuntimeHostedService`) and
   `KoanModuleHost` are `IHostedLifecycleService`s and do their work in `StartingAsync`, which the host completes for
   every lifecycle service before it calls any hosted service's `StartAsync`. Among themselves they keep the order
   `AddKoan()` registers them in: attach, discover, start modules. The binder releases the host in `StoppedAsync`, after
   every hosted service has stopped, so a hosted service's `StopAsync` still has its host. A host configured with
   `HostOptions.ServicesStartConcurrently` runs the starting phase concurrently as well and loses this order; Koan does
   not set it.

## Compatibility

- Behavioural: recording the same decision again no longer moves `Sequence` or the fact's `ObservedAtUtc`.
- Behavioural: module `Start` and runtime discovery run before every hosted service's `StartAsync`, including Koan's
  own background services, rather than in registration order among them.
- No public signature changes; the three hosted services are internal.

## Evidence

`KoanRuntimeFactsSpec.An_unchanged_fact_takes_no_lock_and_writes_nothing` (Koan.Core.Tests): with the store's gate held
by another thread, recording the held decision again completes, while a changed fact waits for the gate; afterwards
`Sequence` has moved by one. It failed before the change. `Recorder_updates_the_latest_stable_subject_without_reopening_collection`
still sees two changes move `Sequence` by two.

`SqliteEarlyHostedServiceSpec` (Connector.Sqlite): a hosted service registered before `AddKoan()` sees an attached host
and saves and reads an Entity in its `StartAsync`. Before the change it failed with `KoanHostContextException` ("no Koan
application host is active"). `AppHostBinderHostedServiceTests` (Koan.Core.Tests) prove the binder's lease through
`StartingAsync` and `StoppedAsync`.
