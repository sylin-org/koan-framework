# Sylin.Koan.Web.Auth.Abstractions — technical contract

## Ownership

This package is the inert cross-module boundary for Web authentication. It owns vocabulary consumed by modules other
than `Sylin.Koan.Web.Auth`; the functional Web Auth package owns composition, cookie behavior, controllers, scheme
seeding, and runtime policy.

## Activation

The assembly contains no `KoanModule`. Referencing it cannot activate Web hosting or authentication. A functional
package implements or consumes these contracts and owns its own ordinary module lifecycle.

## Runtime boundaries

- Auth lifecycle contexts carry one request/host operation; implementations are discovered and registered by Web Auth.
- Identity-store contracts let a durable identity module replace Web Auth's host-local defaults.
- Provider definitions describe availability; Web Auth alone compiles eligibility and election.
- Cross-module projections expose no credentials or token material.

Provider composition and error posture are documented by `Sylin.Koan.Web.Auth`.
