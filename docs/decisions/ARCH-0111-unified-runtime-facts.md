# ARCH-0111 — One host-owned runtime fact envelope

**Status**: Accepted
**Date**: 2026-07-14
**Deciders**: Framework maintainer
**Scope**: Runtime composition explanation, failure facts, health projection, and machine inspection
**Related**: ARCH-0105, ARCH-0108, ARCH-0109, R04-05

---

## Context

Koan already had useful diagnostics, but not one account of what happened. Bootstrap failures lived in
a process-static registry summary and exception prose. Data adapter elections were recomputed by a
lockfile contributor instead of reusing runtime selection. Health, startup text, Web diagnostics, and
MCP resources projected different sources. Broad best-effort catches could remove a view without
leaving a machine-readable reason.

The product constitution requires startup, operators, tests, and agents to receive projections of the
same facts. It does not require one universal provider payload or exact human formatting.

## Decision

### 1. `KoanFactEnvelope` is the shared machine contract

`IKoanRuntimeFacts.Current` exposes an immutable envelope containing:

- integer schema version;
- host session and monotonic snapshot sequence;
- collection timestamp and explicit completion state;
- deterministically ordered `KoanFact` entries.

Each fact contains only stable shared fields: code, kind, state, subject, safe summary, reason code,
optional correction, source, correlation ID, and observation time. Schema 1 defines discovery,
dependency, election, capability, default, degradation, rejection, health, and correction kinds.

The envelope has no arbitrary payload dictionary. Raw exception messages, stack traces, configuration
values, connection material, and provider-specific objects are not accepted. Framework-owned fact
creation normalizes length and applies common de-identification. Provider detail remains in its owning
diagnostic surface and may be referenced by a stable reason code.

### 2. Facts are host-owned

`KoanRuntimeFactStore` is a singleton inside one service provider, not a process-global registry.
Pre-container bootstrap produces an immutable `KoanBootstrapSnapshot` registered into that host's
service collection. `AppRuntimeHostedService` performs discovery for every generic host; `AppRuntime`
keeps Web and synchronous compatibility calls idempotent.

Before collection completes, health and machine consumers see `complete: false`, not an empty success.
Reporter failures produce `collectionFailed` facts and a completed degraded snapshot.

Configuration provenance remains a detailed input during migration. It is not the runtime-decision
authority and is not copied wholesale into the envelope.

### 3. Producers calculate once

The accepting vertical slice owns two decisions:

- module activation creates one redacted fact. `KoanBootException.Fact`, lenient startup rendering,
  health, Web, and MCP use that same fact;
- `AdapterResolver.ResolveDefault` returns one `AdapterResolutionDecision`. Actual Entity data
  resolution and `DataCompositionContributor` use it. The composition builder derives both the
  `data:default` lockfile election and runtime election fact from that decision.

A contributor that cannot report records rejection or collection failure. It cannot disappear and
leave the envelope looking healthy.

### 4. Projections do not become sources

- startup blocks render decisions, failures, and corrections from the current envelope;
- `koan-runtime-facts` health is unknown before collection, healthy with no issue facts, and degraded
  for rejection/degradation fact kinds or collection failures. A capability fact whose operation
  state is `rejected` remains inspectable without declaring the host unhealthy;
- `GET /.well-known/Koan/facts` emits the canonical JSON in Development or when
  `Koan:Web:ExposeObservabilitySnapshot=true`;
- MCP exposes the same JSON as `koan://facts` through its existing authorization boundary;
- the resolved lockfile retains its existing schema, with tests proving its election matches the fact.

Human formatting is not a compatibility contract. `KoanFactEnvelope.Schema` is.

## Consequences

### Positive

- A developer can follow one stable code and correction instead of correlating prose.
- An agent consumes structured facts without scraping startup logs.
- Operators can compare HTTP, MCP, health, exceptions, and lockfiles against one host snapshot.
- Multiple hosts cannot overwrite one another's runtime explanation.
- Missing diagnostics are distinguishable from healthy diagnostics.

### Boundaries

- Schema 1 covers module activation and default data-adapter election as the proved vertical slice; it
  does not claim every provider negotiation or runtime Entity override has migrated.
- Existing configuration provenance and provider health details remain separate owned inputs.
- The Web projection is intentionally gated because even redacted module and decision names disclose
  application topology.
- New fact kinds or fields require additive compatibility review; changing existing meaning requires a
  schema increment.
- The universal schema must not absorb provider-specific payloads. Add a stable reason/correction and
  keep the detailed object with its owner.

## Verification

- Core schema, ordering, JSON round-trip, redaction, and health-state tests;
- bootstrap fail-fast and lenient tests proving exception/store/startup agreement;
- Data composition test proving resolver decision, fact, and lockfile election agreement;
- Web and MCP tests proving exact canonical JSON projection.
