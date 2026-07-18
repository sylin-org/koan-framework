---
uid: reference.modules.Koan.canon
title: Koan.Canon - Technical Reference
description: Canon model composition, deterministic pipelines, persistence, and commit ownership.
since: source-first
packages: [Sylin.Koan.Canon]
source: src/Koan.Canon/
last_updated: 2026-07-18
framework_version: pre-1.0
validation:
  date_last_tested: 2026-07-18
  status: tested
  scope: Canon unit 35/35, integration 7/7, CustomerCanon host 1/1
---

# Koan.Canon technical reference

## Composition contract

`CanonModule` activates by package reference during `AddKoan()`. It reads Koan's generated registry once,
discovers concrete `CanonEntity<T>` models and `ICanonPipelineContributor<T>` implementations, validates
their bindings, and registers one immutable `CanonCompositionPlan` for the host. Every discovered model
receives a pipeline; custom contributors are optional.

The functional package owns the public Canon model, metadata, annotation, contributor, runtime,
persistence, and audit vocabulary. There is no separate Contracts package and no application-facing
runtime builder or configuration step.

## Runtime law

Phases run as `Intake`, `Validation`, `Aggregation`, `Policy`, `Projection`, and `Distribution`.
Contributors within a phase use `Order`, then type name. The first failed or parked contributor stops
that phase and the operation; later contributors and phases do not run. Failed and parked operations do
not enter the canonical commit.

Built-in aggregation and policy contributors are present even when the application provides no custom
contributor. One operation context carries the Entity, metadata, options, services, persistence, stage,
and operation-local items.

## Storage and commit ownership

The runtime, plan, pipeline catalog, persistence, and audit sink are host-owned singletons. Entity
terminal methods resolve the active Koan host; service-provider overloads establish and restore host
flow for the asynchronous operation.

`DefaultCanonPersistence` lowers all `ICanonPersistence` operations to Koan Entity/Data. Replacing that
contract replaces canonical reads/writes, stage writes, and aggregation-index operations together. The
default audit sink also uses Koan Data.

The successful default commit is ordered:

1. persist canonical Entity;
2. upsert aggregation indexes;
3. write audit entries.

This is deliberately fail-loud and not claimed as atomic. Exceptions identify the failed checkpoint
and retain the provider exception as their inner exception. Canon performs no rollback or automatic
retry. An index failure may leave a prefix of indexes durable, and an audit failure occurs after the
canonical Entity and indexes are durable.

## Operational limits

- Stage-only input is persisted and returned as parked. Failed and parked non-stage pipeline outcomes
  remain non-canonical.
- `RebuildViews<T>` is a headless application operation; Canon Web does not generate a rebuild route.
- Distributed locking, delivery, durable replay, retry policy, and recovery are outside this package.
- Concurrency, transaction, and durability guarantees come from the selected persistence provider.
- Authorization belongs at the application or optional Web projection boundary.

See the [public Canon reference](../../docs/reference/canon/index.md).
