---
type: GUIDE
domain: data
title: "Change the default Entity database safely"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-08-06
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-06
  status: verified
  scope: single-host verified default-route cutover across SQLite, MongoDB, and PostgreSQL
---

# Change the default Entity database safely

Use `Sylin.Koan.Data.Cutover` to copy the active default Entity database into a distinct empty
SQLite, MongoDB, or PostgreSQL source, verify logical equivalence, and durably make that source the
new default. Ordinary Entity calls do not change.

This is a maintenance operation for one active host. It is not rolling multi-host migration,
continuous replication, merge, schema transformation, or automatic rollback.

**Before anything else, you are asserting that nothing else is writing.** Every other host, and every
writer that does not go through this host's Koan Data path, must be stopped or externally quiesced.
Koan cannot verify that claim, and the copy is only as correct as it is.

Given that, the operation needs one active default route and one physically distinct target source
that is empty and configured `Managed + ReadWrite`. It copies every included Entity root in bounded
pages, rereads each by exact identity, verifies the result canonically, and activates the new route as
one durable revision.

It is built to refuse rather than improvise. `Plan()` reports blockers and their corrections instead
of treating an unsupported topology as migratable. Before the commit the old route is still the live
one, so failing there costs nothing. After a failure that may have left partial data the target is
quarantined until it is emptied or reprovisioned: there is no automatic rollback, and a half-copied
database is never silently reused.

## 1. Reference the operation and both providers

Keep every provider required by the current and target routes referenced by the application. For an
SQLite to MongoDB to PostgreSQL sequence:

```powershell
dotnet add package Sylin.Koan.Data.Cutover
dotnet add package Sylin.Koan.Data.Connector.Sqlite
dotnet add package Sylin.Koan.Data.Connector.Mongo
dotnet add package Sylin.Koan.Data.Connector.Postgres
```

The Cutover reference gives `AddKoan()` permission to compose the operational capability. No manual
service registration is required.

## 2. Configure distinct named sources

Leave the configured `Default` source unchanged. Add every destination under
`Koan:Data:Sources`, using a different physical database for each source:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=./data/current.db"
        },
        "Mongo": {
          "Adapter": "mongo",
          "ConnectionString": "mongodb://localhost:27017",
          "Database": "app_mongo",
          "StorageLifecycle": "Managed",
          "Access": "ReadWrite"
        },
        "Postgres": {
          "Adapter": "postgres",
          "ConnectionString": "Host=localhost;Database=app_postgres;Username=app;Password=...",
          "StorageLifecycle": "Managed",
          "Access": "ReadWrite"
        }
      },
      "Cutover": {
        "WriterOwnership": "HostExclusiveOrExternallyQuiesced"
      }
    }
  }
}
```

The writer-ownership value is an operational assertion, not a distributed lock. Do not set it while
another application instance, import process, database job, or operator can mutate either route.

The first graduated envelope accepts string-keyed Entity roots on the default route. It rejects
segmented roots, managed fields, stored transforms, compatibility mappings, custom read filters,
delete overrides, incomplete provider inventories, unexplained source containers, and nonempty
targets.

## 3. Plan and inspect before mutation

Run this from the single host that will own the maintenance operation:

<!-- validate -->
```csharp
using Koan.Data.Core;
using Koan.Data.Cutover;

CancellationToken ct = default;

var transition = Data.Source("Mongo").PromoteToDefault();
var plan = await transition.Plan(ct);

if (!plan.CanRun)
    throw new DefaultRouteTransitionRejectedException(plan);

var receipt = await transition.Run(ct);
```

Review every blocker rather than reducing the result to `CanRun`. Each blocker supplies a stable
code, subject, reason, and correction. Each Entity plan is classified as:

- `Included`: copied and verified by this operation;
- `OutsideDefault`: deliberately pinned to another route and unaffected; or
- `Rejected`: inside the default-route concern but outside the supported envelope.

`Run()` repeats preflight after acquiring the transition lease and again after closing mutation
admission. A plan is evidence for the operator; it is not permission to ignore a change that occurs
between planning and execution.

## 4. Preserve the durable route state

After success, unqualified Entity and Direct operations use the activated source. The active pointer
and route generations live outside both databases at:

```text
.Koan/data/active-route.json
```

The path is beneath the host content root and can be overridden with
`Koan:Data:Route:StatePath`. Preserve this file with the application deployment and its databases.
Do not rewrite source configuration to imitate the active pointer.

The receipt records only safe identities, counts, digests, timestamps, and durations. Runtime facts
also report planned, rejected, failed, pending, verified, and completed cutover decisions without
connection strings or Entity payloads.

## 5. Perform the next provider hop deliberately

After MongoDB is active, a later maintenance window can promote the still-empty PostgreSQL source:

```csharp
var postgres = Data.Source("Postgres").PromoteToDefault();
var plan = await postgres.Plan(ct);
if (!plan.CanRun) throw new DefaultRouteTransitionRejectedException(plan);
var receipt = await postgres.Run(ct);
```

Re-establish writer quiescence and inspect a fresh plan for every hop. Provider-specific string
collation does not determine correctness: Koan verifies target records by exact identity and proves
equal total cardinality.

## Failure and recovery actions

| Outcome | Operator action |
|---|---|
| `Plan().CanRun` is `false` | Apply every reported correction, then create a fresh plan. |
| `DefaultRouteTransitionRejectedException` | Read its `Plan`; the unsafe transition did not become active. |
| `DefaultRouteTransitionException` with `TargetMayContainData == false` | Correct the underlying failure and plan again; the old route remains active. |
| `DefaultRouteTransitionException` with `TargetMayContainData == true` | Empty or reprovision the target, keep other writers stopped, then plan and run again. Normal Data access to that target remains quarantined meanwhile. |
| Caller cancellation before durable commit | Treat the old route as active; inspect failure/quarantine state before retrying. |
| Successful activation | Keep the old database as a retained artifact or decommission it through an operator-owned process. Koan does not delete or automatically roll it back. |

Explicit `EntityContext.Source("Default")` access can still address the retained original configured
source after the first promotion. Writes through that explicit route diverge from the active database;
they are not reverse synchronization.

## Operational checklist

- Confirm the application is running as one host and all external writers are quiesced.
- Confirm the target is physically distinct, reachable, managed, writable, and empty.
- Preserve a recoverable copy of the active database and the route-state file.
- Inspect every plan blocker and Entity disposition.
- Run once; do not start competing transition commands.
- Retain the receipt and inspect runtime facts/health before restoring traffic.
- If the target is quarantined, empty or reprovision it before retrying.
- Treat rollback, multi-host fencing, transformations, partitions, and replication as different
  architecture requirements.

## Related

- [Data capability](../../reference/data/index.md)
- [Cutover package reference](../../../src/Koan.Data.Cutover/README.md)
- [Cutover technical contract](../../../src/Koan.Data.Cutover/TECHNICAL.md)
- [DATA-0111 verified default Data cutover](../../decisions/DATA-0111-verified-default-data-cutover.md)
