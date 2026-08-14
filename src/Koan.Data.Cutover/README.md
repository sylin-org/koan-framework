# Sylin.Koan.Data.Cutover

Use this package for one deliberate operational move: copy the active default Entity database into a new, empty
configured source, verify exact logical readback, and atomically make that source the host's new default. SQLite,
MongoDB, and PostgreSQL are the first graduated providers, including moves between them. The package is discovered by
`AddKoan()`; there is no manual registrar.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Cutover
```

## Requirements

This supported envelope is intentionally narrow. It requires one host, no external writers during the run, graduated
SQLite, MongoDB, or PostgreSQL routes, string-keyed default-routed Entity roots, a Managed + ReadWrite empty target, and no
segmentation, managed fields, stored transforms, custom read filters, delete overrides, or compatibility mappings on
the included roots. `Plan()` reports a stable blocker and correction for anything outside that envelope.

Configure two physically distinct sources and keep the configured `Default` source unchanged:

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
          "Database": "app_next",
          "StorageLifecycle": "Managed",
          "Access": "ReadWrite"
        },
        "Postgres": {
          "Adapter": "postgres",
          "ConnectionString": "Host=localhost;Database=app_next;Username=app;Password=...",
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

## Usage

Assert writer ownership only when no process can write either database outside this host's Koan Data paths. Then plan
and run the transition:

```csharp
using Koan.Data.Core;
using Koan.Data.Cutover;

var transition = Data.Source("Mongo").PromoteToDefault();
var plan = await transition.Plan(ct);

if (!plan.CanRun)
{
    foreach (var blocker in plan.Blockers)
        logger.LogError("{Code}: {Reason} Correction: {Correction}",
            blocker.Code, blocker.Reason, blocker.Correction);
    return;
}

var receipt = await transition.Run(ct);

// A later verified move uses exactly the same application language.
var postgresReceipt = await Data.Source("Postgres").PromoteToDefault().Run(ct);
```

Use the [verified default-route cutover how-to](../../docs/guides/data/default-route-cutover.md) for the complete
maintenance-window checklist, blocker handling, durable-state preservation, and quarantine recovery procedure.

## Guarantees and boundaries

The active pointer is persisted outside both databases at `.Koan/data/active-route.json` beneath the host content root;
override it with `Koan:Data:Route:StatePath`. Preserve that file with the deployment. After activation, unqualified
Entity and Direct calls use the target, retained default-derived repositories fail as stale, and explicit
`EntityContext.Source("Default")` access still addresses the old configured source. Koan never performs an automatic
rollback.

A failed run leaves the old default active. Once target mutation could have begun, the target is durably quarantined
and normal Data access rejects it until an operator empties or reprovisions it and a later verified run succeeds.
See [TECHNICAL.md](TECHNICAL.md) and [DATA-0111](../../docs/decisions/DATA-0111-verified-default-data-cutover.md) for the
complete internal and architecture contracts.
