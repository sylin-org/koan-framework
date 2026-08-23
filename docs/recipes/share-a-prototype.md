---
type: RECIPE
recipe: share-a-prototype
title: "Let people outside your machine test it"
domain: web
status: current
last_updated: 2026-08-22
audience: [developers, ai-agents]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/share-a-prototype.md
gets_you: "Real sign-in, deliberate access rules, and a URL other people can click - without standing up production infrastructure."
works_if: "The idea survived contact: you are using it daily, and someone else wants in."
costs: "Authentication adds one sign-in decision and an access rule per exposed operation. Exposure adds a process the internet (or your circle) can reach - patch it and mind it like any server from then on."
ingredients:
  - "one | sign-in, starting development-friendly | Sylin.Koan.Web.Auth.Connector.Test"
  - "one | access rules on exposed entities | Sylin.Koan.Web"
  - "optional | real identity providers when testers arrive | Sylin.Koan.Web.Auth.Connector.Google, Sylin.Koan.Web.Auth.Connector.Microsoft"
  - "optional | tenant isolation when circles must not overlap | Sylin.Koan.Tenancy"
---

# Let people outside your machine test it

The second destination. Three decisions turn a private toy into something shareable: who signs
in, who may touch what, and where the URL points.

## Decide who signs in

Start with the deterministic provider while you are the only account:

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Test
```

When real humans arrive, swap to Google or Microsoft sign-in - same runtime, real credentials in
configuration. The moment more than one person can sign in, access declarations stop being
theoretical:

```csharp
[Access(Access.Read, Access.Write)]     // who may read and write PrepTask at all
public sealed class PrepTask : Entity<PrepTask>;
```

These are the same rules MCP tools and jobs will inherit later - declaring them now is the
cheapest governance you will ever buy.

## Pick an exposure path

| Path | When | Shape |
|---|---|---|
| LAN bind | testers on your network | `dotnet run -- --urls http://0.0.0.0:5000` plus a firewall allowance |
| Quick tunnel | send a link to anyone, zero infrastructure | standard tooling such as `cloudflared tunnel --url http://localhost:5000` (not part of Koan) |
| Private circle | testers you trust on their own devices | Tailscale-style funnel over the same local process |
| A real box | longer-lived testing | publish single-file (`dotnet publish -c Release -r linux-x64 --self-contained`) behind your reverse proxy |

Koan applies minimal security headers by default; behind a proxy, declare that posture so headers
are emitted once.

## Copy, do not invent

| Pattern | Where it already works / is taught |
|---|---|
| A multi-page static UI over the same-origin API | [SnapVault - `wwwroot/`](../../samples/applications/SnapVault/wwwroot/) |
| Sign-in wiring and provider swap | [authentication-setup guide](../guides/authentication-setup.md) |
| `[Access]` declarations and row rules | [authorization-howto](../guides/authorization-howto.md) - sample adoption is pending; treat the guide as canonical |

## Keep the data honest

SQLite keeps carrying a prototype comfortably until concurrent writes start hurting. If testers
report collisions, that is the store-selection table arguing for a networked relational engine -
and moving later is a cutover, not a rewrite.

Reset paths matter when seeds exist: wiping the SQLite file restores the demo state exactly.

## Boundaries

- This is exposure, not hardening: secrets belong in user-secrets rather than literals, but key
  rotation, backups, and audit trails are production destinations.
- Public tunnels make your dev logs semi-public adjacent - mind what the facts endpoint exposes
  and who holds the URL.
