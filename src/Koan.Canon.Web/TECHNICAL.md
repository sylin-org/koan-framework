---
uid: reference.modules.Koan.canon.web
title: Koan.Canon.Web - Technical Reference
description: Generated HTTP and inspection surfaces for discovered Canon models.
since: source-first
packages: [Sylin.Koan.Canon.Web]
source: src/Koan.Canon.Web/
last_updated: 2026-07-17
framework_version: pre-1.0
---

# Koan.Canon.Web technical reference

`CanonWebModule` owns only Web projection. Functional activation and pipeline compilation belong to
`Sylin.Koan.Canon`; shared vocabulary belongs to `Sylin.Koan.Canon.Contracts`.

The module reads Koan's generated model registry once during host composition, builds a host-owned model
catalog, and registers generic controllers for each concrete Canon entity or value object. It performs
no AppDomain scan and does not activate the runtime itself.

## Routes

- `/api/canon/{model}`: Canon-aware entity endpoints; single and bulk writes enter `ICanonRuntime`.
- `/api/canon/value-objects/{type}`: generic Entity endpoints for Canon value objects.
- `/api/canon/models`: model, route, pipeline, aggregation, policy, and audit metadata.
- `/api/canon/admin/records`: bounded process-local runtime records.
- `/api/canon/admin/{slug}/rebuild`: rebuild a discovered canonical model.

Single writes map `Failed` to 422 and `Parked` to 202. Query/header options can supply origin,
correlation, stage behavior, rebuild/distribution flags, requested views, and tags.

Apply authentication and authorization at the host boundary, especially for admin routes.
