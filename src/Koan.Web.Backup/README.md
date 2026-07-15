# Koan.Web.Backup

ASP.NET Core controllers and process-local polling endpoints over the experimental
`Koan.Data.Backup` services.

> **Maturity: experimental.** The current HTTP surface can initiate backup and restore operations,
> inspect their process-local status, browse the backup catalog, and request verification. It is not
> a certified production recovery system. Restore behavior and the wider adapter/storage matrix remain
> unverified, and this project currently has no dedicated automated test suite.

## Activate the current surface

Referencing the module is sufficient for registration through `AddKoan()`. The normal Koan web host
discovers and maps its attribute-routed controllers.

```csharp
using Koan.Core;
using Koan.Web.Backup.Initialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.UseKoanWebBackup(); // Applies the registered CORS policy; it does not add a hub.
app.Run();
```

The routes are currently unversioned and fixed under `/api`. The `basePath` argument on
`UseKoanWebBackup(...)` does not rewrite controller routes.

## Observe operations by polling

Starting an operation returns `202 Accepted` with an operation identifier. Poll the matching status
endpoint from the same running process:

```http
POST /api/backup/all
Content-Type: application/json

{
  "name": "orders-nightly"
}
```

```http
GET /api/backup/operations/{operationId}
```

Restore status is available at `GET /api/restore/operations/{operationId}`. There is no SignalR hub,
WebSocket transport, or server-push progress channel in this module. Polling cadence is controlled by
the client.

Operation tracking is in memory and process-local. It does not survive process restart and is not a
distributed coordination mechanism. The `AddKoanWebBackupWithPersistentTracking()` and
`AddKoanWebBackupWithEnhancedNotifications()` helpers currently delegate to the base registration;
they do not install persistent tracking or push notifications.

## Current controller surface

### Backup

- `POST /api/backup/all`
- `POST /api/backup/selective`
- `GET /api/backup/operations/{operationId}`
- `POST /api/backup/operations/{operationId}/cancel`
- `GET /api/backup/manifests`
- `GET /api/backup/manifests/{backupId}`
- `POST /api/backup/verify/{backupId}`
- `GET /api/backup/status`

### Restore

- `POST /api/restore/{backupName}`
- `POST /api/restore/{backupName}/test`
- `GET /api/restore/operations/{operationId}`
- `POST /api/restore/operations/{operationId}/cancel`
- `GET /api/restore/history`

### Entity-specific

- `GET /api/entities`
- `POST /api/entities/{entityType}/backup`
- `POST /api/entities/{entityType}/restore/{backupName}`
- `GET /api/entities/{entityType}/backups`

The cancel endpoints mark the process-local tracking record as `Cancelled`. They do **not** abort the
backup or restore work already running. Stopping active I/O requires a cancellation token connected to
the running service operation; the status endpoint does not provide that connection today.

## Data and memory boundaries

- Entity export requires `DataCaps.Query.ProviderBoundedPaging`. SQLite, PostgreSQL, SQL Server,
  CockroachDB, MongoDB, and Couchbase are qualified. InMemory, JSON, and Redis reject before export.
- `BatchSize` bounds the Koan-visible Entity candidate page. It does not bound opaque provider buffers
  or the completed archive.
- The complete compressed ZIP is assembled in a `MemoryStream` before upload. Peak memory therefore
  grows with archive size.
- One archive exports its entity types sequentially. `GlobalBackupOptions.MaxConcurrency` is not
  currently consumed by the backup writer.
- Verification endpoints inspect the implemented artifact contract; they do not certify application
  recovery, schema evolution, renamed types, or every adapter/storage combination.

See [`Koan.Data.Backup/README.md`](../Koan.Data.Backup/README.md) for the underlying service contract,
including encryption, retention, deletion, cancellation, and restore boundaries.

## OpenAPI and security

Call `AddKoanWebBackupSwagger()` when the host uses Swashbuckle and wants this controller surface in a
Swagger document. The generated description reports the same experimental, polling-only boundaries.

The module does not add authentication or authorization policies. Its default CORS registration is
permissive. Applications must apply their own authentication, authorization, origin policy, HTTPS,
rate limits, storage protection, and recovery drills before exposing these endpoints.

## Validation boundary

`Koan.Data.Backup.Tests` proves a real SQLite/local-storage export lane, caller cancellation, and
fail-closed resident adapters. `Koan.Web.Backup` itself currently has no dedicated automated controller
or integration test suite; a successful project build is not a recovery certification.

## License

Koan.Web.Backup is part of Koan Framework and is licensed under Apache License 2.0.
