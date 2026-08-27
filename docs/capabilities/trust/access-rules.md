---
type: REFERENCE
domain: security
title: "Entity access rules"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the SQLite path against published packages (Web 1.0.20, Web.Auth 1.0.11, Connector.Test 1.0.10) — [Access(read,write,remove)] gate proven anonymous 401 / forbidden 403 / allowed 200 through the governed EntityController projection, row survives denied deletes, per-identity `can` projection verified
---

# Entity access rules

Declare who may read, write, or remove an Entity once, then let every governed projection reuse that
decision.

## You need

| Piece | Package | Note |
|---|---|---|
| Entity and HTTP policy pipeline | `Sylin.Koan.Web` | owns the governed Entity endpoint seam |
| Authenticated identities and authorization | `Sylin.Koan.Web.Auth` | supplies the subject and policy runtime |
| Durable rows the rules protect | a Koan Data connector, e.g. `Sylin.Koan.Data.Connector.Sqlite` | without one, Entity requests fail: `Koan Data has no provider candidates. Reference a Data connector and call AddKoan().` |
| Richer HTTP projections (optional) | `Sylin.Koan.Web.Extensions` | must remain on the same policy path |

## The constraint box

> **The constraint:** Protect the Entity or named operation, not only its controller. A route-only
> rule does not automatically govern MCP, Jobs, or another projection. A gate that refuses an action
> and a row constraint that removes invisible records are different decisions; many products need
> both.

## Choose the policy depth

| Need | Expression | Result |
|---|---|---|
| Coarse action gate | `[Access(...)]` on the Entity or action | allow, challenge, or forbid by operation |
| Per-row ownership and visibility | an `EntityAccess<T>` realization | one typed rule filters lists, protects one-row fetches, and stamps server truth |
| Custom non-Entity action | ordinary ASP.NET Core authorization | the application owns that route-specific action |

## Leaves

- **Build and negative-path proof:** [control who can do what](../../recipes/control-who-can-do-what.md)
- **Policy contract:** [authorization guide](../../guides/authorization-howto.md)
- **Identity contract:** [auth guide](../../guides/auth-howto.md)

Prove anonymous, allowed, and forbidden callers through every exposed surface. Passing HTTP tests do
not prove the same Entity is safe through MCP.
