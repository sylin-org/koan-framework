---
type: RECIPE
recipe: control-who-can-do-what
title: "Control who can do what"
domain: identity
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/control-who-can-do-what.md
gets_you: "One rule about who may read or change a thing, enforced everywhere that thing is exposed."
works_if: "People can already sign in, or will be able to before this ships."
costs: "Adds no service. Adds a decision you must make per operation, which is the work."
ingredients:
  - "one | HTTP conventions and EntityController | Sylin.Koan.Web"
  - "one | authentication and authorization | Sylin.Koan.Web.Auth"
  - "optional | shaping and projection add-ons | Sylin.Koan.Web.Extensions"
---

# Control who can do what

Authentication says who someone is. This says what they may do — and the point is that one rule
governs every surface, not that HTTP has a filter on it.

## When this is the answer

"Only the author can edit their post." "Support can read, admins can delete." "This endpoint is
internal."

The mistake to head off is **protecting the pipeline instead of the thing**. A rule attached to a
controller is not enforced when the same Entity is reached through an agent tool, a background job, or
a projection. Ask early which surfaces exist now and which are planned: if MCP or Jobs are on the
table, the rule belongs on the Entity or the operation.

Ask two things and then stop:

1. **What is the unit of protection?** Usually the Entity or a named operation on it — rarely a route.
2. **What happens to a denied request?** Refused, or filtered out of the collection so it was never
   visible? Those are different products, and the second is usually what people mean by "they
   shouldn't see other people's things".

If the answer to the second is "filtered", say plainly that a filter is not a per-row check and both
may be needed.

## Assembly

```powershell
dotnet add package Sylin.Koan.Web.Auth
```

Authorization rides on the same package as sign-in; there is no separate one to add. Express the rule
at the operation boundary rather than inside business code, and let every projection inherit it.

Depth: [authorization how-to](../guides/authorization-howto.md) ·
[auth how-to](../guides/auth-howto.md).

## Prove it

1. **Behavior** — anonymous is refused, an allowed identity succeeds, a forbidden identity is refused.
   All three, every time; the third is the one people skip.
2. **Composition** — assert the policy is actually registered and applies, rather than inferring it
   from a passing happy path.
3. **Correction** — a denied request explains itself at the owning boundary instead of failing as a
   generic 500 or, worse, succeeding with filtered-looking data.

Add the negative paths for **every** surface that reaches the same data, not only HTTP.

## Boundaries

- This does not authenticate anyone. Without [sign-in](let-people-sign-in.md) there is no subject.
- It does not isolate customers from one another. That is [tenancy](isolate-tenants.md), and a
  per-user rule is not a substitute.
- Never weaken a policy to make a demonstration or a test pass.

## Interacts with

**Agent surfaces.** An MCP tool over a protected Entity must reach the same rule, or the agent surface
becomes the way around it.

**Tenancy.** Authorization answers "may this person do this". Tenancy answers "whose data is this at
all". Applications that conflate them leak across customers while every permission check passes.
