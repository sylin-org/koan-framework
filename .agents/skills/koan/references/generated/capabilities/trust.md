---
type: REFERENCE
domain: security
title: "Trust and isolation"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/trust.md - route table verified against leaf targets
---

# Trust and isolation

Four decisions, kept separate: who is acting, what they may do, whose data exists in the current
scope, and which fields need protection at rest. Authentication is not authorization,
authorization is not tenancy, and tenancy is not encryption - each node owns exactly one.

The cheapest governance you will ever buy: declare an `[Access]` rule once and every doorway -
HTTP, MCP, jobs - inherits it.

## Route by need

| The request says | Fetch |
|---|---|
| "let people sign in" / "add Google login" | [sign-in](trust/sign-in.md) |
| "only admins may delete" / "who can touch what?" | [access rules](trust/access-rules.md) |
| "multiple customers, one app" - isolation | [tenant isolation](trust/tenant-isolation.md) |
| "encrypt these fields at rest" | [field protection](trust/field-protection.md) |

## Standing constraints

- An `[Access]` declaration binds every projection of the Entity - HTTP, MCP tools, and future
  surfaces compile the same rule. Declaring it late means re-deriving it per surface.
- Production key custody for field protection is the application's to supply; Development is
  zero-config on purpose.

## Do not, at this level

- Do not hand-roll authentication middleware or per-endpoint role checks beside the gate.
- Do not put tenant filters in business code - the ambient scope compiles them in.

For the one-screen maturity view, see
[Trust and isolation in the capability map](../reference/capability-map.md#trust-and-isolation).
