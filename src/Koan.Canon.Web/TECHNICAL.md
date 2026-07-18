---
uid: reference.modules.Koan.canon.web
title: Koan.Canon.Web - Technical Reference
description: ASP.NET projection and inspection surfaces for the Canon host composition plan.
since: source-first
packages: [Sylin.Koan.Canon.Web]
source: src/Koan.Canon.Web/
last_updated: 2026-07-18
framework_version: pre-1.0
validation:
  date_last_tested: 2026-07-18
  status: tested
  scope: Canon integration 7/7 and CustomerCanon host 1/1
---

# Koan.Canon.Web technical reference

`CanonWebModule` owns only HTTP projection. Functional activation, model eligibility, pipeline
compilation, persistence, and commit semantics belong to `Sylin.Koan.Canon`.

## Composition contract

During host composition, the module reads the immutable `CanonCompositionPlan` already registered by
Canon. It projects each plan model into one catalog descriptor and one generic
`CanonEntitiesController<T>`. It does not perform a second registry or AppDomain scan. Duplicate route
slugs reject composition with a corrective exception naming every conflicting CLR type.

## Routes

- `/api/canon/{model}`: inherited Entity reads plus Canon-aware single and bulk writes.
- `/api/canon/models`: model, route, pipeline, aggregation, policy, and audit metadata.

Single writes map `Failed` to 422 and `Parked` to 202. Query and header options can supply origin,
correlation, stage behavior, rebuild/distribution flags, requested views, and tags. Bulk writes execute
sequentially and return one result per input.

## Boundaries

The module generates no admin, process-record, replay, value-object, or rebuild endpoint. Headless
rebuild remains available through `ICanonRuntime`. Routes participate in the host's ordinary ASP.NET
authentication and authorization setup; the package does not add a separate privileged policy.

Canon's canonical-to-index-to-audit commit remains non-atomic and fail-loud. HTTP projection does not
add transactions, rollback, retry, distributed delivery, or recovery.
