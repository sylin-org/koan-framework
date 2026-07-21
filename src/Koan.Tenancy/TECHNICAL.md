# Sylin.Koan.Tenancy technical contract

## Composition owner

`TenancyModule` is the one functional module. Referencing the package makes it available; `AddKoan()` discovers it,
binds `TenancyOptions`, retains one `TenancyRuntime`, registers durable context carriage, and contributes one hard
segmentation dimension.

The module does not register Data-, Cache-, Storage-, or Communication-specific mechanics. Core's semantic
composition kernel distributes the contribution to active pillars; each pillar compiles its immutable realization
once and owns enforcement at its operation chokepoint.

## Hot path

`Tenant.Use(id)` and `Tenant.None()` push a `TenantContext` through `KoanContext`. The contribution reads the current
slice. When it is unset, `TenancyAmbient` reads the current host's retained `TenancyRuntime`: Open posture binds `dev`;
Closed posture returns a missing value. There is no assembly scan, contributor discovery, provider election, smart
tenant seed, signing-key generation, or registry lookup per operation.

`TenantContextCarrier` serializes only explicit tenant/host context for durable hops. The Development fallback is not
made portable; the receiving host applies its own posture when no explicit slice exists.

## Segmentation contract

The dimension id is `tenant`, its strength is hard, and its selector excludes `[HostScoped]` and the dependency-free
`IAmbientExempt` marker. A missing hard value throws `SegmentationRequiredException` before provider I/O. An active
pillar publishes one value-free runtime realization fact; tenant values never appear in composition facts.

An adapter must announce and implement the guarantee required by its pillar. The grammar does not imply false backend
parity: an incapable provider rejects the operation.

## Posture and startup

`TenancyOptions` binds from `Koan:Tenancy`. `Posture = null` derives Open only from the current host's Development
environment and Closed otherwise. A resolved Open posture outside Development throws `InvalidOperationException`
with the corrective configuration path during module start.

Core does not require `ITenantResolver`. Inbound request resolution is owned by `Sylin.Koan.Identity.Tenancy`; making
it a core prerequisite would incorrectly prevent closed production workers, tests, and other non-HTTP hosts.

Startup reporting states the resolved posture, local fallback or fail-closed behavior, segmentation strength, and
pillar-owned realization.

## Control-plane entities

- `TenantRecord`: stable id, mutable display name, optional routing code.
- `Membership`: deterministic seat id, subject id, tenant roles.
- `TenantAuditEntry`: host-scoped entry written by supported administration mutations.

All three are `[HostScoped]` so the registry that defines tenant scope is not itself tenant-segmented. Direct Entity
access remains available because they are ordinary Koan models. Higher-level guarantees—code uniqueness, reserved-role
rejection, and mutation audit—belong to the optional Web administration chokepoint.

## Dependencies and exclusions

This package depends on Core, Data abstractions, and Data Core because its minimal durable control plane is Entity-
backed. It has no ASP.NET dependency. Request carriers, authentication, operator UI/API, invitation ceremony,
tenant-status policy, cross-pillar data deletion, cryptographic erasure evidence, and verified domains are outside this
package's current contract.
