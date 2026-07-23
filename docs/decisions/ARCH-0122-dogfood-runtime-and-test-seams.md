---
id: ARCH-0122
slug: dogfood-runtime-and-test-seams
domain: Architecture
status: Accepted
date: 2026-07-23
title: Dogfood-derived runtime control and deterministic test seams
related:
  - ARCH-0105
  - ARCH-0115
  - ARCH-0121
  - AI-0015
  - JOBS-0005
---

# ARCH-0122: Dogfood-derived runtime control and deterministic test seams

## Outcome

Koan closes three application-facing gaps found through brownfield use:

1. SSE preserves the caller's protocol intent and composes with stronger HTTP cache policy.
2. AI sources can be inspected and changed at runtime through one source-lifecycle owner.
3. Jobs exposes an xUnit-free deterministic driver over the production execution engine.

A concise agent workflow teaches how to find and prove those expressions. It links to the canonical
capability guides rather than becoming another framework curriculum.

## Decision checkpoint

**Application intent:** An application can stream exact SSE protocol frames, change and inspect AI
sources at runtime, and drive Jobs deterministically in tests without copying framework internals.
An engineer or coding agent can discover those paths quickly for greenfield or brownfield work.

**Public expression:** An unnamed `SseEnvelope` remains unnamed, and an empty envelope carrying
comment, retry, or id fields is an exact control frame. AI exposes `IAiSourceControl` with inspect,
apply, enable, disable, and remove operations while ordinary routing remains automatic. Inspection
reports provider version, installed models, and resident models without exposing a provider client.
`JobsTestDriver.From(services)` drives the existing Jobs engine after the host opts out of background
execution. The agent workflow uses the normal package, configuration, Entity, context, and runtime
surfaces documented by each capability pillar.

**Guarantee/correction:** SSE control frames contain no synthetic event or data field, null data
still rejects, and stronger cache directives survive. Disabled or removed AI sources leave routing
immediately, replacement invalidates old health work, and endpoint inspection uses the selected
provider's protocol. Inspection distinguishes an unreachable endpoint, an available empty installed
model catalog, and unavailable residency information. Jobs tests execute the production orchestrator
deterministically. Unsupported inspection and incompatible Jobs test configuration fail with the
exact safe correction.

**Complete intent surface:** Reference the relevant package and use the API above. Deterministic Jobs
tests additionally set `JobsOptions.EnableWorker` to `false` and retain normal, non-inline execution.
No application integration, certification, manifest, provider lane, or assertion framework is added.

**Public concepts:** `IAiSourceControl` means runtime source lifecycle ownership. Source inspection
answers whether a provider endpoint can serve useful models before registration. `JobsTestDriver`
means deterministic production-engine execution, not access to orchestration internals. SSE envelope
shape remains the caller's protocol decision.

**Coalescence:** Existing registries, routers, adapters, endpoint pools, orchestrators, schedulers,
results, and formatters remain their single owners. Reusable Jobs TestKit mechanics move behind the
public driver; the private harness keeps only host and assertion conveniences. AI mutation is absorbed
by one source-control service rather than expanding the lookup registry. The agent workflow removes
duplication by routing to canonical capability owners.

**Ergonomics:** APIs use application verbs, are visible in IntelliSense, and expose no internal
orchestrator, scheduler, circuit, or provider-client type. Ordinary applications remain automatic;
advanced control appears only when requested. The public guide is short enough to retrieve as a whole
and its evaluation fixtures are small enough to run only when that guidance changes.

## Domain decisions

### SSE

An absent event name is meaningful. Fallback event names apply only when the caller explicitly asks
for that projection. Framework headers use set-if-absent or token composition: they never weaken a
stronger application or middleware cache directive.

An envelope with empty data and at least one comment, retry, or id field is a protocol control frame.
The formatter writes only those control fields and the terminating blank line. An empty envelope
without control fields remains an empty data event, and null data remains invalid.

### AI source lifecycle

The runtime registry owns a monotonically increasing revision for each applied source. Routing sees
only enabled sources. Health and inspection work may publish results only while its source revision is
current, so a late probe cannot resurrect removed, disabled, or replaced state.

Provider adapters own endpoint inspection because only they know the correct endpoint, request, and
response grammar. Source control coordinates inspection and lifecycle; it does not reimplement
provider protocols.

The provider-neutral inspection result carries optional provider version, installed models, and
resident models. Availability flags distinguish a successful empty collection from a provider facet
that could not be inspected. Overall availability means the endpoint answered at least one
provider-owned inspection request; partial failures remain visible in `Detail`.

Ollama owns `/api/version`, `/api/tags`, and `/api/ps`. The application neither calls nor parses those
endpoints. Other providers populate only the facets their protocol can truthfully inspect.

### Route-hint naming clarification

`WithRouteAdapter` currently carries a source or member hint; the router resolves the actual provider
adapter from that source. This decision does not add a source-named alias because two names for one
route field would make the public path less clear. A later semantic rename must change
`AiRouteHints.AdapterId` and its fluent method together, with one compatibility decision, rather than
layering another permanent synonym into this closure.

### Deterministic Jobs testing

`JobOrchestrator` and `JobScheduler` remain internal implementation owners. The packable
`Sylin.Koan.Jobs.Testing` package receives friend access and exposes a small driver over an existing
application service provider. It contains no assertion-library or xUnit dependency and creates no
second host.

### Agent workflow

The workflow asks for five statements before implementation: application intent, public expression,
guarantee and correction, complete action surface, and owner. Brownfield work additionally classifies
each bespoke mechanism as keep, absorb, rebuild, or delete. Proof remains three-layered: focused
behavior, composition, and corrective failure.

Two anonymous fixtures—one greenfield and one brownfield—plus one corrective prompt form the cheap
retrieval evaluation. They do not encode a private application, infrastructure topology, or client.

## Consequences

### Positive

- Applications stop working around missing lifecycle and testing seams.
- Provider integrations remain unchanged and continue to own their protocols.
- The public Jobs surface grows by a driver, not by exposing the engine's internals.
- Agent guidance becomes a routing and reasoning aid rather than duplicated documentation.
- Validation cost stays proportional to the changed guarantee.

### Tradeoffs

- AI source mutation requires revision-aware registry and health-monitor coordination.
- Provider-specific inspection is available only when an adapter implements the inspection contract;
  unsupported providers reject the request correctively.
- Deterministic Jobs tests must deliberately disable the background worker.

## Evidence boundary

Each slice owns focused behavioral tests. The new Jobs package is checked for an xUnit-free dependency
graph. Agent fixtures receive static structure/link validation; when the workflow itself changes
materially, each of the three anonymous prompts runs once with a cold agent and retains its raw output
and signal score. Successful prompts are not repeated and no model matrix is required. Repository-wide
certification remains outside this decision under ARCH-0121.

The material workflow change accepted by this decision has retained cold-agent evidence at
`tests/Fixtures/AgentApplicationEngineering/evidence/2026-07-23`: all three anonymous prompts passed
their declared signals on the first run.
