---
id: SEC-0020
slug: the-open-default-is-the-product-stance
domain: Security
status: Accepted
date: 2026-10-21
title: An entity or verb nothing declares is open; the open default is the product stance and its posture is stated in the facts
related:
  - SEC-0004
  - SEC-0012
  - WEB-0047
---

# SEC-0020: An entity or verb nothing declares is open; the open default is the product stance and its posture is stated in the facts

## Context

`Todo : Entity<Todo>` plus `TodosController : EntityController<Todo>` exposes the controller's full verb set to an
anonymous caller, including `DELETE .../all` (and, where MCP is enabled, `*.delete-all`). With no `[Access]` gate, no
`EntityAccess<T>` and no provider deciding, the authorization ladder falls through to
`AuthorizeOptions.DefaultDecision`, which is `Allow`. Ledger NK-055 asked whether that default should hold back the
mass-destructive verbs or report open entities as errors outside Development.

SEC-0004 chose allow-by-default deliberately and recorded its cost: an un-gated mutation is open, mitigated by the
per-row projection and by gating being one declaration away. WEB-0047 set the same posture for capability controllers.

## Decision

The open default stays. It is the product stance, not an oversight:

- **One mental model.** What you do not declare is open; what you declare is enforced. No verb is silently withheld,
  so a route that exists in the controller exists in the host, and a missing gate is never mistaken for a missing route.
- **Best for the common case.** A small application (the V0 of the product objective) works from a bare entity and a
  bare controller; each layer of protection is added when the application needs it, in the same grammar.
- **No hidden second rule.** Withholding `DELETE /all` or `*.delete-all` until a remove gate exists would make the open
  default open for everything except what the framework judged dangerous, which is a rule to learn and to explain on
  every platform that serves the entity.

Therefore, and with no change to enforcement:

- `AuthorizeOptions.DefaultDecision` remains `Allow`; the verb set of `EntityController<T>` and the MCP mutation tools
  are unchanged; the boot report does not turn open entities into warnings or errors.
- A developer narrows the default at the smallest scope that says what is meant:
  1. **One entity, per action**: `[Access(read: "anyone", write: "authenticated", remove: "is:admin")]`. An unspecified
     action stays open, so declaring `remove:` closes `DELETE /all` and the other remove verbs while reads stay open.
     `[Authorize]`, `[AllowAnonymous]` and `[RequireScope]` lower into the same gate (SEC-0012).
  2. **One entity, per row**: an `EntityAccess<T>` realization (`Owner`, `Constrain`), which also bounds mass deletes
     to the principal's rows.
  3. **MCP only**: `[McpEntity(AllowMutations = false)]` keeps the entity's mutation tools out of the registry.
  4. **The whole host**: `Koan:Web:Authorization:DefaultDecision=Forbid` makes every verb nothing declares denied,
     for REST and MCP alike (SEC-0011 keeps MCP listings consistent with it).
- The posture is stated, not inferred. The Web module adds one runtime fact,
  `koan.web.authorization.default` (subject `web:authorization`), to the host's facts
  (`/.well-known/Koan/facts`, the `koan://` facts resource and the composition snapshot). Its summary names the
  decision an undeclared entity or verb resolves to, `Allow` or `Forbid`, and the key that sets it. The per-entity
  posture stays visible through the access map and the `?access=true` projection (SEC-0004).

## Compatibility

No behavior, signature, configuration or stored data changes. One fact is added to the runtime facts of a host that
references `Koan.Web`; a consumer that enumerates facts by code sees one more entry.

## Evidence

`WebAuthorizationPostureFactsSpec` (Koan.Web.WellKnown.Tests) starts a host and asserts the fact states `Allow` by
default and `Forbid` when `Koan:Web:Authorization:DefaultDecision=Forbid`. It failed before the fact existed.
`AuthorizationSeamTests` and the entity-floor specs continue to cover both default decisions.
