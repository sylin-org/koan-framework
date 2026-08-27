---
type: REFERENCE
domain: security
title: "Tenant isolation"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the SQLite path against published packages (Tenancy 1.0.9, Data.Connector.Sqlite) — cross-tenant read/write denial matrix, fail-closed unscoped query with the verbatim SegmentationRequiredException, koan.lock.json composition proof
---

# Tenant isolation

Make ordinary Entity reads and writes belong to one customer by construction, then carry that same
scope through every active capability.

## You need

| Piece | Package | Note |
|---|---|---|
| Ambient tenant isolation | `Sylin.Koan.Tenancy` | contributes segmentation to active pillars |
| Authentication | `Sylin.Koan.Web.Auth` | establishes the trusted caller from which tenancy may resolve |
| Durable person-to-tenant membership (optional) | `Sylin.Koan.Identity.Tenancy` · `Sylin.Koan.Identity` | supports people who belong to several tenants |
| Operator lifecycle surface (optional) | `Sylin.Koan.Tenancy.Web` | separates administration exposure from authority |

Verified against: `Sylin.Koan.Tenancy` 1.0.8 or newer, `Sylin.Koan.Identity.Tenancy` 1.0.6 or newer, `Sylin.Koan.Web.Auth.Connector.Test` 1.0.7 or newer (patch releases compatible).

## The constraint box

> **The constraint:** Tenant scope must be established before tenant-owned work and must survive
> every HTTP, MCP, Job, event, Data, Storage, Media, Cache, and Vector boundary. Adding tenancy does
> not partition rows that already exist; production work with no resolvable tenant must fail closed.

## Settle these decisions before data exists

| Decision | Why it changes the application |
|---|---|
| What a tenant is | company, workspace, or region becomes the durable ownership boundary |
| How a request resolves it | identity, validated host, or trusted internal header establishes scope |
| Whether one person belongs to several | determines whether durable Identity membership belongs now |
| Which data is truly global | only explicit host-scoped state opts out; it is not a cross-tenant bypass |

## Leaves

- **Build and cross-surface proof:** [isolate tenants](../../recipes/isolate-tenants.md)
- **Isolation contract:** [tenancy guide](../../guides/tenancy-howto.md)
- **Package contract:**
  [Tenancy README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Tenancy/README.md)

Authorization answers whether a caller may act; tenancy answers whose data exists in the operation.
Keep both.
