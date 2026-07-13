---
type: GUIDE
domain: core
title: "R04-02 - Make Runtime State Host-Scoped"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-02 — Make runtime state host-scoped

- Priority: P0
- Status: `ready`
- Depends on: R04-01
- Owner: Core hosting with Data.Core/AI consumers

## User-visible failure

Repeated integration hosts and some static Entity paths can retain a disposed service provider. The
current Data/AI lifecycle test fails with `ObjectDisposedException: IServiceProvider`; static lifecycle
registries and relationship metadata have the same risk shape.

## Personas

Developers see flaky tests or failures after restart; agents may diagnose the wrong host; operators
cannot trust lifecycle isolation in workers or reload scenarios.

## Current evidence

- `VectorModelGuardIntegrationSpecs.Registry_round_trips_the_full_model_lifecycle` fails through
  `Data<T>.Repo` after host disposal.
- `AppHost.Current`, static closed-generic registries, and cached relationship metadata are reachable
  from Entity paths.

## Smallest meaningful fix

Define one host/runtime lease and make service/configuration-backed registries resolve through it.
Immutable reflection/type metadata may remain static. First repair the failing lifecycle path, then add
repeat-create/dispose probes before broad migration.

## Failure behavior

A missing/disposed host throws one Koan host-context error naming the attempted operation and how to
establish a valid host. It never falls back to an earlier provider.

## Verification

- failing Data/AI lifecycle test passes repeatedly;
- N sequential and parallel host create/use/dispose cycles show no provider/registry residue;
- core Entity operations, lifecycle registration, relationship metadata, and AI registry have focused
  ownership tests;
- no new process-global mutable service/configuration cache is introduced.

## Compatibility and rollback

Preserve public call shapes while changing ownership underneath. If an API relied on cross-host static
registration, document and deprecate it rather than silently preserving leakage. Land migration in
small owner-specific commits behind the host lease.

## Stop condition

Split by owner if one host abstraction cannot be reviewed without changing unrelated module semantics.
