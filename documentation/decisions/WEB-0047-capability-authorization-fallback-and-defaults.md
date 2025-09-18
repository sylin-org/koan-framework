---
id: WEB-0047
slug: capability-authorization-fallback-and-defaults
domain: Web
status: accepted
date: 2025-08-28
title: Capability authorization - global allow/deny fallback and per-entity defaults
---

## Context

Koan exposes generic capability controllers for moderation, soft-delete, and audit. These endpoints must enforce authorization consistently without forcing bespoke controllers. Teams need a clear policy for:

- How permissions are resolved per capability action.
- What happens when no explicit mapping is configured for an entity/action.
- A simple global switch to adopt either a permissive or strict posture.

## Decision

Adopt a layered authorization resolution policy for capability actions with a global fallback:

Fallback order (highest to lowest)

1. Entity-specific mapping for the action
2. Global Defaults mapping for the action
3. Global DefaultBehavior (Allow or Deny)

Global posture

- Standard posture: Allow-by-default
- Supported alternative: Deny-by-default (only mapped actions are allowed)

Scope

- Applies to capability controllers: Moderation, SoftDelete, Audit.
- Does not define identity/claims issuance. It only specifies resolution and fallback.

## Consequences

- Predictable behavior for unmapped actions (no surprises): permissive by default unless teams opt into deny-by-default.
- Minimal configuration for common cases (inherit from Defaults; override only where needed).
- Clear 403 responses when denied by explicit mapping or deny-by-default, surfaced in Swagger (ProducesResponseType 403).

## Implementation notes

Options (conceptual)

- CapabilityAuthorizationOptions
  - DefaultBehavior: Allow | Deny
  - Defaults: CapabilityPolicy (Moderation, SoftDelete, Audit)
  - Entities: Dictionary<string, CapabilityPolicy>

Resolution

- Input: entityType, capabilityAction, user principal
- Resolve mapping via Entity → Defaults → DefaultBehavior
- If mapping exists, evaluate user permission per app’s auth provider
- If denied or missing and DefaultBehavior == Deny, return 403 ProblemDetails

Examples

Allow-by-default with targeted overrides
// Program.cs
opts.Authorization = new CapabilityAuthorizationOptions
{
DefaultBehavior = CapabilityDefaultBehavior.Allow,
Defaults = new CapabilityPolicy { /_ map common permissions _/ },
Entities =
{
["Article"] = new CapabilityPolicy { /_ stricter Approve mapping _/ },
["Author"] = new CapabilityPolicy { /_ soft-delete overrides _/ }
}
};

Deny-by-default (strict)
// Program.cs
opts.Authorization.DefaultBehavior = CapabilityDefaultBehavior.Deny; // unmapped actions → 403

Error/edge cases

- Missing entity mapping and no default: allow or deny based on DefaultBehavior
- Unknown capability action: treat as unmapped → DefaultBehavior
- Bulk operations: evaluate action per endpoint (e.g., DeleteMany)

## Follow-ups

- Add reference samples under S7 to demonstrate policy wiring and 403 ProblemDetails.
- Consider a central permission service and attribute to DRY controller checks.

## References

- WEB-0046 - Entity capabilities - short endpoints and set routing
- Web capability controllers reference: docs/reference/web-capabilities.md
