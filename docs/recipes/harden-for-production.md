---
type: RECIPE
recipe: harden-for-production
title: "Harden for production and scale"
domain: operations
status: current
last_updated: 2026-08-22
audience: [developers, ai-agents]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/harden-for-production.md
gets_you: "Graduated providers with verified migration, a declared secrets posture, observability export, protection for sensitive fields - and a hardening receipt that names what is proved and what is not."
works_if: "The prototype earned trust: real usage, data worth keeping, and someone accountable when it is down."
costs: "Production asks for evidence at every step: container-backed conformance runs, a maintenance window for cutover, credentials for telemetry endpoints. Time shifts from writing code to proving it."
ingredients:
  - "one | a networked database once SQLite stops fitting | Sylin.Koan.Data.Connector.Postgres"
  - "one | the verified move itself | Sylin.Koan.Data.Cutover"
  - "one | observability export | Sylin.Koan.Observability"
  - "optional | field-at-rest protection for sensitive strings | Sylin.Koan.Classification"
  - "optional | recoverable deletion | Sylin.Koan.Data.SoftDelete"
---

# Harden for production and scale

The third destination. Nothing here is new architecture - it is evidence gathered around the
application you already have, in an explicit order.

## Copy, do not invent

| Pattern | Where it already works / is taught |
|---|---|
| The cutover envelope, blockers and recovery | [default-route-cutover how-to](../guides/data/default-route-cutover.md) · cross-provider proof in [Koan.Data.Cutover.CrossProvider.Tests](../../tests/Suites/Data/Cutover/Koan.Data.Cutover.CrossProvider.Tests/) |
| NativeAOT publish properties that survive | [AotRelational - `AotRelational.csproj`](../../samples/fundamentals/AotRelational/AotRelational.csproj) |
| Conformance specs against a real engine | [Entity conformance kit](../../src/Koan.Testing/EntityConformanceSpecs.cs) subclassed per connector under `tests/Suites/Data/` |

## Graduate the store deliberately

Cutover copies the active default database into an empty configured target, verifies exact
logical readback, and promotes it:

```csharp
var plan = await Data.Source("Postgres").PromoteToDefault().Plan(ct);
if (!plan.CanRun) { /* log blockers + corrections; widen the envelope */ return; }
var receipt = await Data.Source("Postgres").PromoteToDefault().Run(ct);
```

The envelope is intentionally narrow: one host, no external writers during the window, graduated
routes. A failed run leaves the old default active; a half-written target is quarantined rather
than trusted. Decide before running whether testers' prototype data comes along or production
starts clean - cutover will not decide for you.

## Prove against the real engine

Suites built on SQLite prove SQLite. Before calling it production-ready, run the inherited
conformance specs against a container-backed instance of the real engine - round trips, paging,
pushdown versus the reference oracle, partition isolation. The testing kit boots those hosts so
this is configuration, not construction.

## Declare the posture

- Secrets move to environment or a secret provider; literals and user-secrets do not ship.
- The public URL is declared once: `Koan:Web:ApplicationUrl` (scheme, host, and the path when the
  application lives under one), or accepted by the application through its `IApplicationUrlStore` (WEB-0081). Koan
  then serves only under the configured path, and the issuer, MCP resource, card URLs and atproto client metadata all
  derive from it instead of from the request `Host` header.
- Behind a reverse proxy, name it: `Koan:Web:TrustedProxies` lists every proxy in front of the application (IP or
  CIDR, a CDN's published ranges included) whose `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-For` are
  believed. Nothing declared, nothing
  believed.
- The key ring persists: `Koan:Web:KeyRingDirectory` is a folder on the persistent volume, so sign-in cookies and
  protected sessions outlive a rebuilt container and every instance shares them. The folder remembers the application
  name its keys belong to, so a rename keeps them; `Koan:Web:KeyRingApplicationName` pins it when instances must agree
  before the first start. Keys are written in clear: the folder's permissions protect them (owner-only when Koan
  creates it on Unix), or add an encryptor with ASP.NET's own `ProtectKeysWith…`.
- A failed sign-in lands on the application's own page: `Koan:Web:Auth:ReturnUrl:FailurePath` (for example
  `/sign-in`) receives one sanitized code in `error`, never the provider's text.
- Security headers are on by default; when the proxy emits them instead, say so (`IsProxiedApi`) so
  they are emitted exactly once.
- Tenancy posture closes: unscoped operations fail instead of resolving a development tenant.
- Sensitive string fields take classification attributes - encrypted at rest, keys scoped by
  segmentation, Development zero-config, production backed by your key provider.
- Observability exports traces and meters over OTLP; health endpoints describe external topology.

## Publish shape

Single-file publish works, NativeAOT works where packages allow, and embedded static assets ride
along. The repository's scheduled AOT lane proves win-x64 and linux-x64 binaries start and map
entities - reuse that lane rather than trusting a fresh idea.

## The hardening receipt

End the way every Koan claim ends - graded, with the gaps named:

```text
proved:   cutover receipt (sqlite -> postgres) · conformance on pg · headers via proxy
          OTLP flowing · secrets from environment · [Access] rules on all exposed entities
unproved: load beyond prototype traffic · disaster recovery (platform's job)
next:     schedule the AOT lane if single-binary deployment is chosen
```

## Boundaries

- Koan does not provision infrastructure, own backups/DR, or provide platform failover - those
  remain yours, stated here so nobody discovers them in an incident.
- Capabilities marked unassessed on the product surface carry no guarantees even when installed;
  the receipt passes that honesty through.
