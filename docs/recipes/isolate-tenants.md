---
type: RECIPE
recipe: isolate-tenants
title: "Keep customers from seeing each other's data"
domain: identity
status: current
last_updated: 2026-08-27
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the SQLite path against published packages (Tenancy 1.0.9, Data.Connector.Sqlite) — cross-tenant denial matrix and fail-closed negative path proven through a real AddKoan() host
gets_you: "Every read and write scoped to one customer, by construction rather than by remembering a filter."
works_if: "More than one customer will ever use this application. Decide before there is data."
costs: "Adds no service. Adds a boundary that every new surface must respect — retrofitting it later is the expensive path."
ingredients:
  - "one | tenant isolation | Sylin.Koan.Tenancy"
  - "one | authentication, to know whose request this is | Sylin.Koan.Web.Auth"
  - "one | resolve the tenant from an HTTP request - without this there is no way at all to scope HTTP callers to a tenant | Sylin.Koan.Identity.Tenancy, Sylin.Koan.Identity"
  - "optional | operator console for tenant lifecycle | Sylin.Koan.Tenancy.Web"
  - "optional | protect specific fields at rest | Sylin.Koan.Classification"
---

# Keep customers from seeing each other's data

The tenant is ambient and the data layer enforces it, so an ordinary `Entity<T>` query is already
scoped. That is the point: isolation you cannot forget beats isolation you must remember.

## When this is the answer

"Each customer sees only their own work." "We're going multi-tenant." Also — and this is worth raising
unprompted — **whenever the application will have more than one customer and does not have data yet.**
Adding isolation to a populated store means backfilling ownership onto existing rows and auditing
every existing query, which is far more expensive than starting with it.

The decisions worth settling, in this order:

1. **What is a tenant?** A company, a workspace, a region? Getting this wrong is the only truly
   expensive mistake here, because it is the one you cannot rename later.
2. **How does a request declare its tenant?** From the signed-in person, a host name, or a header. Host
   routing is friendly and must be validated; a header is convenient and must never be trusted from
   outside.
3. **Does one person belong to several tenants?** If yes, a durable person identity is worth adding at
   the same time — otherwise identity is pinned to a tenant and switching becomes a migration.
4. **Do specific fields need protection beyond isolation?** Isolation keeps customers apart;
   classification protects a field at rest even inside one tenant. Different problems.

## Assembly

```powershell
dotnet add package Sylin.Koan.Tenancy
```

Tenancy also needs an isolating backing store — reference one connector and configure it once (a
non-isolating store fails closed for a tenant-scoped operation rather than leak):

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

```json
"Koan": {
  "Data": {
    "Sources": {
      "Default": {
        "Adapter": "sqlite",
        "ConnectionString": "Data Source=app.db"
      }
    }
  }
}
```

Scope is deliberate and disposable where an operation must cross or pin it:

```csharp
using Koan.Tenancy;   // Tenant
using Koan.Data.Core; // FirstPage

using (Tenant.Use("acme"))
{
    var page = await Todo.FirstPage(25, ct);
}
```

Ordinary application code does not write that. It is for the exceptional case — an administrative
task, a migration, a background job acting for a known tenant — and its visibility is the feature.

Depth: [tenancy how-to](../guides/tenancy-howto.md).

## Prove it

1. **Behavior** — one tenant's request returns its own rows; the same request under another tenant
   returns none.
2. **Composition** — assert the isolation is enforced at the data layer, not by a controller filter you
   could remove.
3. **Correction** — a request with no resolvable tenant fails closed. If it returns everything, the
   feature is inverted.

Prove cross-tenant denial through **every** surface that reaches the same data — HTTP, agent tools,
jobs, events, storage, media, and vector search. One unscoped surface undoes all of them, and counts,
error messages, and search relevance all leak.

## Boundaries

- Tenancy is not authorization. It answers whose data this is, not whether this person may touch it.
- It does not encrypt anything or, on its own, delete a customer's data on request.
- Adding it does not partition data that already exists.

## Interacts with

**Background work.** Work that leaves the request thread must carry the tenant across the async hop,
or it reads nothing and silently does nothing — a failure that looks like success.

**AI and vector search.** Retrieval must be tenant-scoped at the vector query, not only at the Entity
query, or one customer's question is answered from another's documents.
