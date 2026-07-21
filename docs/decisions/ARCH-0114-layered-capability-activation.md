# ARCH-0114: Layered capability activation

**Status:** Accepted (2026-07-15)
**Scope:** Optional engines that augment an already usable Koan concern or adapter.
**Related:** ARCH-0044 · ARCH-0084 · ARCH-0087 · ARCH-0105 · ARCH-0111 · ARCH-0113

## Decision

Every layerable Koan capability follows one lifecycle:

1. The base concern provides a complete minimal implementation and remains useful by itself.
2. An adapter declares only concern-neutral identity and protocol behavior; optional engines remain unknown to it.
3. Referencing the engine and running `AddKoan()` activates the engine through Reference = Intent.
4. The engine contributes typed candidates, policies, or providers through the concern-owned seam.
5. The concern-owned coordinator applies precedence, health, context, and fallback policy.
6. The adapter interprets the elected value for its protocol and reports provider-specific health.

An adapter must not probe for optional assemblies, call an inactive engine, or duplicate the shared
election policy. An engine must not short-circuit the concern's coordinator. Package absence therefore
removes a layer; it does not create a partially configured runtime.

This is a framework invariant. Adapter-specific deviations are defects unless a later ADR identifies a
different concern boundary and proves why the same lifecycle cannot apply.

## Ownership

| Owner | Responsibility |
|---|---|
| Base concern | Minimal local provider, semantic contract, coordinator, precedence, context, fallback, facts |
| Adapter | Neutral identity/aliases, protocol conversion, normalization, health validation |
| Optional engine | Activation and typed contribution only |
| Application | Business intent and explicit overrides; no integration glue |

The standard is a lifecycle and separation-of-concerns rule, not a universal
`ILayeredCapability` abstraction. Pillars retain typed seams appropriate to their domain. A shared
mechanism is introduced only when multiple pillars exhibit the same executable contract.

## Configuration and precedence

Concrete application configuration is authoritative. Automatic layers may improve an unconfigured
experience but cannot override explicit intent.

For service discovery, the canonical order is:

1. concrete explicit configuration;
2. service-specific environment instruction not already represented by application configuration;
3. automatic candidates: Aspire, activated engine contributors, then adapter runtime topology;
4. host-gateway fallback;
5. loopback fallback.

Every candidate is interpreted and health-checked by the adapter. An unhealthy automatic contribution
falls through. A referenced distributed mechanism that was explicitly selected does not silently become
a weaker mechanism merely to keep startup green; that failure posture belongs to the concern's contract.

## Inspectability

The same decision must be legible from three viewpoints:

- the adapter's module report states its autonomous capability;
- the optional engine's retained module and compiled-plan facts state that contribution is active;
- the coordinator records the selected method or rejection as a credential-redacted runtime fact.

Reports distinguish `declared`, `active`, and `selected`; they do not infer activation from metadata or
selection from package presence.

## Current service-discovery realization

- a hidden invariant `IContributeTo<DiscoveryContributionTarget>` binds the retained Zen Garden module to the concern-owned target.
- the generated descriptor dispatches the exact retained module only when direct Reference = Intent activates it.
- adapter service names and aliases are the neutral selectors; there is no engine-specific binding map.
- Core compiles one immutable host plan, then `ServiceDiscoveryCoordinator` queries its retained live sources per operation.
- `ServiceDiscoveryAdapterBase` owns the non-replaceable candidate pipeline.
- adapters may customize only environment inputs, runtime topology, normalization, and health.
- `DiscoveryCandidatePriority` names and constrains the shared precedence slots.

The in-process or local baseline remains available when Zen Garden is absent. Adding Zen Garden changes
the candidate mesh without changing application code.

## Required conformance

Every layerable adapter proves:

- transitive contract presence without direct engine activation is inert;
- activation adds exactly the intended typed contribution;
- explicit configuration wins over every automatic layer;
- an unhealthy contribution falls through to the next eligible candidate;
- adapter-specific topology cannot replace the shared activation pipeline;
- the elected method is visible without exposing credentials or raw connection endpoints; and
- removing the engine restores the baseline without application changes.

## Consequences

Application code stays business-centric while infrastructure sophistication grows by reference. Adapter
authors get one predictable contract, coding agents can infer composition from types and facts, and
operators can distinguish what an app understands from what it activated and selected.

The cost is intentional pre-1.0 constraint: adapters can no longer replace the complete discovery
candidate builder. Specialized topology remains possible through the narrower runtime-candidate hook.
