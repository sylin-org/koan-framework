# Sylin.Koan.Data.Connector.Redis

Use Redis through ordinary Koan Entity verbs for keyed persistence, native TTL, compact mapped JSON, and deliberately
bounded managed-set queries.

```powershell
dotnet add package Sylin.Koan.Data.Connector.Redis
```

```csharp
await new Cart { Id = cartId, Items = [] }.Save(ct);
var cart = await Cart.Get(cartId, ct);
```

`Sylin.Koan.Redis` owns endpoint discovery and one shared host-lifetime connection multiplexer per endpoint. The Data
adapter owns only routes, key/document plans, Entity execution, capabilities, Functions, and health.

## Existing JSON without a Redis-specific model

```csharp
services.AddKoan(koan => koan.Data.Source("Legacy").Map<Customer>(map => map
    .Container("legacy_customers")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name).Name("DISPLAY_NM")
    .Property(customer => customer.Profile).Object("PROFILE")));
```

Mapped updates change only declared values and preserve unknown JSON properties. Redis keys remain
application-assigned; `.Generated()` rejects correctively.

## Managed and external sources

A Managed source owns one membership set for each logical Entity container. `All`, `Query`, `Count`, and clear operate
only on that set and reject if its cardinality exceeds `MaxQueryEntries` (default 10,000). They never enumerate the
Redis server keyspace.

An External source creates no registry or metadata. Known-key `Get`, `Upsert`, and `Delete` remain available according
to its access policy; set enumeration and inspection reject. This keeps source-owned databases untouched.

## Named read-only Functions

Infrastructure loads Redis Functions. Koan invokes a registered read through `FCALL_RO`; it never deploys server code.

```csharp
services.AddKoan(koan => koan.Data.Source("Legacy")
    .Query("customers.active", query => query
        .Lane("Reports")
        .Function("active_customers")
        .Parameter<string>("region")));

RecordSet rows = await Data.Source("Legacy")
    .Query("customers.active", new { region = "west" }, ct);
```

Record Functions return an array of JSON object strings. Scalar Functions return one Redis scalar. Function keys are
declared after the function name; `@parameter` resolves a runtime parameter into `KEYS`, while operation parameters are
also delivered to `ARGV` in declaration order.

## Honest boundaries

- No `KEYS`/`SCAN` Entity path and no portable source inspection claim.
- No `ProviderBoundedPaging`; `AllStream` and `QueryStream` reject before yielding.
- No atomic Entity batch or generated-key claim.
- Native single-key TTL and optimistic conditional replacement are supported.
- Bulk and managed-query limits are correctness boundaries, not tuning hints.

See [TECHNICAL.md](TECHNICAL.md) for key layout, options, and operational constraints.

## What it adds

Redis keyed Entity persistence for Koan with native TTL, bounded managed sets, compact JSON mapping, read-only Functions, and shared connection ownership.
