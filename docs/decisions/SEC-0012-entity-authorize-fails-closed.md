---
id: SEC-0012
slug: entity-authorize-fails-closed
domain: Security
status: Accepted
date: 2026-10-21
title: An entity [Authorize] the gate cannot enforce stops boot
related:
  - ARCH-0092
  - SEC-0004
---

# SEC-0012: An entity [Authorize] the gate cannot enforce stops boot

## Context

ARCH-0092 lets an entity declare standard `[Authorize]` and lowers it into the entity gate, which REST and MCP both
enforce. `AccessGateCache.LowerLegacyFloor` read only `Roles` and `[RequireScope]`. An `[Authorize(Policy = "AdminOnly")]`
or `[Authorize(AuthenticationSchemes = "Bearer")]` produced a bag requiring authentication and nothing else, applied to
read, write and remove. An entity class attribute is not endpoint metadata, so ASP.NET never evaluated the policy either:
every authenticated caller could read, write and `DELETE /all` the entity on REST, and call `*.delete-all` on MCP
(ledger NK-027). ARCH-0092 also names `[Authorize(Policy = "scope:items:write")]` as equivalent to `[RequireScope]`,
which the lowering ignored.

Evaluating ASP.NET policies inside the gate would need DI in a gate that is compiled once and evaluated purely (by the
floor provider, the per-row projection and the Door), so a policy term could not be honored everywhere the gate is read.

## Decision

**On an entity, `[Authorize]` lowers what the gate can express and refuses the rest.**

1. `Roles` lower as before. `Policy = "scope:<scope>"` lowers to the scope grant `<scope>`, AND-ed with any
   `[RequireScope]`.
2. Any other `Policy`, and any `AuthenticationSchemes`, throws `AccessGateException` naming the entity and the
   declaration and pointing to `[Access(...)]`, `[RequireScope]` or an `EntityAccess<T>` realization.
3. `AccessGateRegistrar` compiles every Entity type that carries `[Authorize]` or `[RequireScope]` at boot, as it already
   compiled `[Access]`, so the refusal stops boot with the other malformed declarations. Controllers are not entities and
   keep their policies, which ASP.NET evaluates.
4. `[AllowAnonymous]` keeps its precedence.

This amends the ARCH-0092 sentence that presents `[Authorize]` as carrying "named policies" for entities.

## Compatibility

- An application whose entity declares a named policy or authentication schemes no longer boots; the message names the
  replacement. Before this change that entity was open to every authenticated caller.
- `[Authorize(Policy = "scope:x")]` on an entity now requires scope `x`, where it required only authentication.
- No public signature or stored format changes; `KoanWebConstants.Authorization.ScopePolicyPrefix` is added.

## Guarantee and correction

- A declared entity requirement is enforced on REST and MCP as declared, or the application does not start.

## Evidence

- `AccessGatePolicyLoweringSpec` (Koan.Web.WellKnown.Tests): a named policy and authentication schemes are refused; the
  `scope:` convention admits the scoped caller and forbids an authenticated caller without it; boot refuses an Entity
  whose `[Authorize]` names a policy, and leaves a controller's policy alone. The four refusal and lowering cells failed
  before this change.
