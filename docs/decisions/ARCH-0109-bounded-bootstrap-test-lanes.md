---
id: ARCH-0109
slug: bounded-bootstrap-test-lanes
domain: Architecture
status: Accepted
date: 2026-07-14
title: Bootstrap evidence is partitioned by composition cost
---

# ARCH-0109: Bootstrap evidence is partitioned by composition cost

## Context

Koan treats a referenced module as application intent. A bootstrap test assembly that referenced Core,
every offline pillar, Redis, ONNX, and sqlite-vec therefore loaded that entire composition for every
`AddKoan()` test. Test filtering could select a method, but it could not shrink the assembly's module
closure. A nominal Data smoke test consequently waited on unrelated Redis and AI initialization, and
the 38-test project could run for minutes without returning a useful result.

xUnit v3 projects are also self-executing applications. A successful process with no reported test
count is not sufficient evidence that a bootstrap contract ran.

## Decision

Bootstrap evidence is owned by three separate test assemblies:

- `Koan.Tests.Integration.Bootstrap` is the deterministic fast lane. It references only Core and the
  neutral test host and owns 16 discovery, manifest, fail-loud/lenient, startup-rendering, and test-host
  ownership proofs.
- `Koan.Tests.Integration.Bootstrap.Pillars` owns 16 real `AddKoan()` proofs for the in-process pillar
  composition. Its references are limited to modules that require no external service.
- `Koan.Tests.Integration.Bootstrap.Infrastructure` owns seven Redis, ONNX, and sqlite-vec proofs. All
  seven facts are explicit. The self-executing project also declares `IsTestProject=false` because
  VSTest can initialize a class fixture even when every fact is explicit; solution-wide `dotnet test`
  builds but does not execute this project.

`scripts/test-bootstrap.ps1` is the canonical lane runner. It builds and runs each selected project in
its own child process, applies separate overridable build and run deadlines, captures standard output
and error, and kills only the timed-out process tree. A lane passes only when the process exits zero
and xUnit reports a nonzero `TEST EXECUTION SUMMARY`. Timeout and process failures name the lane,
phase, project, command, and deadline while preserving captured diagnostics.

The projects remain serial internally because Koan hosts share a process-default host binding and some
bootstrap tests inspect process-wide registry snapshots.

## Consequences

- The default signal is deterministic and does not depend on Docker, a model download, or a native
  adapter.
- Reference = Intent remains honest: composition isolation is achieved with assembly boundaries, not
  filters that pretend references are absent.
- Infrastructure proof is deliberately opt-in and expensive. Its current assembly still composes all
  three infrastructure surfaces, so non-Redis tests can observe Redis startup work. Split it further
  only when a concrete reliability or deadline failure justifies more projects.
- Failed `KoanIntegrationHost.StartAsync` now disposes the host it built before rethrowing. The
  returned wrapper also uses true asynchronous host disposal when the underlying host supports it.
- This decision changes test topology and evidence, not application runtime behavior or capability
  maturity.

## Evidence

On the accepting development host, the self-executing lanes reported:

- fast: 16/16 in 0.417 seconds;
- pillars: 16/16 in 4.793 seconds;
- infrastructure: 7/7 in 115.178 seconds, explicitly selected with Docker available.

The bounded wrapper also validates the build phase and refuses a zero-test success. These are observed
results, not cross-machine performance guarantees; the conservative defaults are overrideable.
The failed-start proof was red because its async-owned service remained undisposed, then green after
the builder adopted the wrapper as its single cleanup boundary.

## References

- [ARCH-0079 — Integration tests are canon](ARCH-0079-integration-tests-as-canon.md)
- [ARCH-0091 — Koan testing platform](ARCH-0091-integration-test-harness-redesign.md)
- [R04-03 bounded bootstrap lanes](../initiatives/koan-v1/work-items/r04/R04-03-bounded-bootstrap-lanes.md)
