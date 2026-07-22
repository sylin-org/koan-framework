---
type: SPEC
domain: framework
title: "R13-08 - Promote SQL Server Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: in-progress
  scope: focused SQL Server provider, pack, consumer, product, and API evidence
---

# R13-08 — Promote SQL Server Entity persistence

## Outcome

Promote `Sylin.Koan.Data.Connector.SqlServer` as a supported 0.20 networked Entity provider over the
already-supported relational foundation. The connector is the only Koan package owner added by this
slice; Microsoft.Data.SqlClient and Dapper remain ordinary external dependencies rather than new
framework concepts.

## Architecture checkpoint

**Application intent:** An application installs the SQL Server connector, calls `AddKoan()`, and uses
normal `Entity<T>` save, query, paging, and streaming operations against a reachable SQL Server.

**Public expression:** The complete common expression is one package reference, ordinary `AddKoan()`,
an `Entity<T>` model, and either local orchestration discovery or a standard connection string.

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order>;

var saved = await new Order().Save();
var same = await Order.Get(saved.Id);
```

**Guarantee/correction:** The connector owns provider election, discovery, configuration, source
routing, schema policy, health, and startup explanation. It preserves Entity CRUD, native filtering,
paging, provider-bounded streams, batch and conditional writes, and declared row/container/database
isolation. An unreachable selected server reports unhealthy readiness and provider errors instead of
falling back to another adapter. Unsupported stream ordering rejects before provider I/O.

**Complete intent surface:** Connector package reference; `AddKoan()`; Entity statics; an optional
existing connection key; a reachable SQL Server; startup facts and health; the existing provider
suite; one pack; and one clean external package consumer. There are no additional application actions.

**Public concepts:** Existing `SqlServerOptions` represents connection, schema, naming, JSON, and
readiness policy. Promotion adds no public type, registration method, runner, manifest, matrix, or
certificate.

**Coalescence:** R13-07 PostgreSQL is the closest promotion pattern, while the supported relational
foundation owns shared SQL semantics. Keep all provider-specific behavior in the existing connector
and promote that single owner. Do not duplicate shared relational law or introduce a provider-wide
admission subsystem.

**Ergonomics:** A user chooses one package and keeps the normal Entity grammar. IntelliSense remains
centered on `Entity<T>`; the only meaningful deployment branch is autonomous discovery versus an
explicit standard connection string.

## Evidence boundary

1. Run the complete existing SQL Server provider project against its real Testcontainers service.
2. Pack the connector with `PublicRelease=true`.
3. Restore, build, and run a clean external consumer using only the staged package plus NuGet.org;
   the consumer boots `AddKoan()` and persists/queries an Entity against SQL Server.
4. Compile generated product truth and run the API-floor guard.
5. Run cheap repository coherence; do not run sibling providers or framework-wide certification.

## Exit state

This card passes when the connector declares 0.20 intent, owns one honest supported claim, focused
native and external-consumer evidence passes, and the resulting public artifact is visible on
NuGet.org. Its exact first public 0.20 version becomes the immutable API floor in the following slice.

## Focused evidence — 2026-07-22

- architecture and package topology: one connector over the already-supported relational closure;
- real provider boundary: existing Testcontainers fixture uses the official SQL Server 2025 image;
- documentation audit: removed nonexistent retry/timeout promises and aligned configuration with the
  actual options/configurator;
- real SQL Server provider suite: 26/26 passed with zero skips against the official SQL Server 2025
  Testcontainers image;
- `PublicRelease=true` pack: connector artifact produced successfully at the local first-publication
  version `0.20.0`;
- genuinely external staged-package consumer: clean restore, zero-warning Release build, real SQL
  Server container boot, `AddKoan()` provider selection, and Entity save/get/query passed with
  `SQLSERVER|PACKAGE-CONSUMER|PASS`;
- generated product surface: 35 claims across 93 packages, current; and
- API posture: 44/45 configured, exactly the SQL Server connector pending first publication, and
  three content-only owners.

No sibling provider or framework-wide test ran. Cheap repository coherence, publication, public
indexing, final NuGet.org-only consumption, and immutable baseline capture remain.
