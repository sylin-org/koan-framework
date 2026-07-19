---
type: SUPPORT
domain: troubleshooting
title: "Koan troubleshooting"
audience: [developers, support-engineers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: reviewed
  scope: public diagnostics and current extension seams
---

# Koan troubleshooting

Start with the decision Koan made. Most failures are a missing package reference, rejected explicit
intent, or unavailable infrastructure—not a registration problem to solve with more application
code.

## Start here

1. Read the startup report. It names activated modules, elections, rejected intent, and corrections.
2. Call `/health/live` to confirm the process is running and `/health/ready` to check selected
   dependencies. Use the exact base URL printed by ASP.NET Core.
3. In a Web host, read `/.well-known/Koan/facts` for the same redacted runtime decisions. MCP hosts
   expose that envelope as `koan://facts`.
4. Compare `koan.lock.json` with the references you intended to ship. The runtime boot line reports
   lockfile drift when the loaded composition differs.
5. Turn on `Debug` logging only for the affected `Koan.*` namespace, then reproduce once.

The facts endpoint is gated by the host's access policy. Do not weaken a production boundary merely
to make diagnostics public.

## Boot and module activation

A Koan application needs the host bootstrap and the package that owns the capability:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
```

Do not manually register Koan controllers, adapters, jobs, or contributors. Confirm the relevant
`Sylin.Koan.*` package is a direct or transitive reference, then inspect startup facts for whether it
was activated, inactive by design, or rejected. Package proximity alone does not promise that a
surface exists.

For HTTP 404s, also verify the route on the application controller and use the URL printed by the
host. Entity controllers normally derive from `EntityController<T>` and declare their application
route. `/.well-known/auth/providers` exists only when the Auth Web capability is referenced and
active; it is not a general Web-health probe.

## Adapter & Data Connectivity

Read the data decision in `/.well-known/Koan/facts` first. A referenced adapter may be available but
unelected, which is normal and must not make readiness fail. An explicitly selected adapter that is
missing or unavailable is different: Koan rejects the intent instead of silently changing storage.

The application-wide default setting is:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "postgres"
        }
      }
    }
  }
}
```

Reference the package that owns the named adapter, verify its external service independently, and
restart. Entity-level `[SourceAdapter]` or `[DataAdapter]` declarations take precedence where the
domain deliberately requires stable placement. Do not add arbitrary startup delays or raw SDK
warm-up code to the application; adapter readiness belongs to the adapter.

For capability and placement details, use the
[data reference](../reference/data/index.md) and
[adapter diagnostics](../reference/data/adapter-diagnostics.md).

## Health does not match the runtime

- `/health/live` answers whether the process can serve.
- `/health/ready` answers whether the capabilities Koan actually selected are ready.
- Runtime facts explain why a provider was selected, left inactive, or rejected.

An installed but unelected provider should remain visible evidence, not a readiness failure. Add an
application `IHealthContributor` only for a real business-critical dependency that Koan cannot own;
do not duplicate adapter checks already supplied by a capability package.

## A job is not running

Application code implements `IKoanJob<T>` and its static `Execute` handler. The interface carries
Koan's discovery marker, so the concrete job does not need a discovery attribute, worker
registration, repository, or startup task. Submit through the Entity's `.Job` or `.Jobs` accessor,
then inspect the job status and Jobs health contribution.

If a durable ledger or cross-node wake-up was intended, verify that the elected data adapter and
Communication connector support that tier. The same handler remains valid when those packages are
added. See the [Jobs pillar map](../reference/cards/jobs.md) and
[Jobs how-to](../guides/jobs-howto.md).

## Authentication is unavailable

`GET /.well-known/auth/providers` lists eligible providers when the Auth Web capability is active.
An absent provider usually means its required configuration is incomplete; inspect startup facts and
the provider's documented keys. Keep client secrets outside source control and verify configured
redirect URIs against the external provider.

Koan can supply supported identity, authorization, token, trust, and tenancy primitives. The
application owns policy declarations, exposed operations, credentials, HTTPS and network boundary,
input validation, backups, and deployment controls. See
[authentication setup](../guides/authentication-setup.md).

## AI or vector work is unavailable

The startup report distinguishes a referenced provider, an inactive automatic candidate, and
rejected explicit configuration. Verify the selected provider at its own health endpoint. Configure
one supported placement form; do not set both an endpoint list and its connection-string
alternative. Koan AI does not claim a universal application budget, retry, rate-limit, or
provider-fallback policy—those belong to the selected provider or the application's resilience
boundary.

For Entity indexing, declare `[Embedding]` and save normally. With asynchronous embedding,
persistence completion and vector completion are separate facts. Inspect the embedding ledger and
`Koan.Data.AI` logs before treating a vector as available. For a backfill, inspect the
`EmbeddingMigrator.ReEmbed` result; partial failure does not roll back successful vector writes.
See the [AI reference](../reference/ai/index.md).

## Escalate with evidence

Include:

- the Koan version and direct `Sylin.Koan.*` package references;
- startup logs from process start through the failure;
- `/health/live`, `/health/ready`, and redacted runtime facts;
- the relevant `koan.lock.json` drift line;
- configuration with secrets removed; and
- a minimal reproduction or failing request.

That evidence describes composition, intent, and observed behavior without requiring private
application access.
