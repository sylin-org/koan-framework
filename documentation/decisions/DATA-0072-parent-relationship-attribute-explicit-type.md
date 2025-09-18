---
id: DATA-0072
slug: DATA-0072-parent-relationship-attribute-explicit-type
status: Accepted
date: 2025-09-12
domain: DATA
---

# DATA-0072: Parent relationship attribute must specify explicit parent type

## Context

Previous parent relationship attributes (e.g., `[ParentKey]`) allowed implicit or string-based parent references, which led to ambiguity and unreliable parent resolution. The new `[Parent]` attribute, if used without a type argument, cannot reliably indicate the parent entity type, breaking controller aggregation and relationship metadata discovery.

## Decision

- The `[Parent]` attribute must always be declared with an explicit parent entity type, e.g. `[Parent(typeof(Device))]`.
- The framework's relationship metadata service and controller logic depend on this explicit type for correct parent resolution and aggregation.
- All documentation, guides, and code samples must show `[Parent(typeof(ParentEntity))]` usage.
- Legacy usages of `[ParentKey]` or `[Parent]` without a type argument must be migrated.

## Consequences

- Parent relationships are always discoverable and unambiguous.
- Controller logic and metadata services can reliably aggregate parent entities.
- Migration required for any code or documentation using `[Parent]` without a type argument.

## Alternatives considered

- Allowing `[Parent]` without a type argument: rejected due to ambiguity and unreliable resolution.
- Inferring parent type from property name or context: rejected as brittle and error-prone.

## Migration notes

- Update all entity models to use `[Parent(typeof(ParentEntity))]`.
- Update documentation and guides to reflect this requirement.
- Remove or refactor any usages of `[ParentKey]` or `[Parent]` without a type argument.

---
