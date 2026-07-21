---
type: SPEC
domain: framework
title: "R07-11 - Jobs Wake Convergence"
audience: [architects, maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: Communication internal framework-signal lane and Jobs wake over local/RabbitMQ providers
---

# R07-11 — Jobs wake convergence

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-10
- Unlocks: bounded Cache coherence inventory without preserving arbitrary-object Messaging
- Owner: Communication signal carriage; Jobs wake meaning and ledger fallback

## Meaningful outcome

Jobs no longer owns a transport provider or asks developers to reference a Jobs Messaging package.
Every non-transactional submit emits one bounded internal wake hint. With no connector, the built-in
Communication provider wakes the local worker. A directly referenced Communication connector such as
RabbitMQ transparently carries the same stable worker-group signal across nodes.

The signal contains no work or business context. The ledger remains the queue, and polling remains the
correctness fallback, so loss or duplication changes latency only.

## Architecture

- `CommunicationLane.FrameworkSignals` is infrastructure-facing and has no Entity facet, application
  publisher, subscription API, or handler discovery convention.
- Friend assemblies register closed, typed value signals with stable contract/group identities. One
  bounded, non-blocking host egress prevents an optional hint from delaying the business operation.
- The router elects the lane with the same explicit/direct/built-in precedence and hard semantic
  eligibility as other Communication lanes.
- InProcess now owns a lane-indexed channel/worker set rather than hard-coding two lanes.
- The shared wire advances to schema 2 and uses a neutral payload field. Providers still see only
  authenticated host bytes and routing identities.
- RabbitMQ claims Transport plus FrameworkSignals, uses lane-qualified routes and queues on one schema-2
  mesh exchange, and retains confirmed mandatory persistent publication.
- `JobWakeCoordinator` owns only coalescing and the meaning of work readiness. Jobs facts point to the
  Communication election rather than conducting a second provider election.

## Principal deletion

- public `IJobTransport`;
- `InProcessJobTransport`;
- direct construction of the now-internal `JobCoordinator`; applications compose `IJobCoordinator`;
- `Koan.Jobs.Transport.Messaging` and its package identity;
- `MessagingJobTransport`, `IMessageProxy`, `services.On<T>`, `AppHost.Current` service location, and
  unmanaged fire-and-forget publication from the Jobs wake path; and
- the separate Jobs Messaging test project and solution folder.

At the time of this slice, legacy Messaging remained for Cache coherence and other
previous-generation consumers. R07-12 subsequently removed Cache's legacy channel packages and
moved its key-invalidation contract onto Communication's internal every-node broadcast route.

## Delight contract

- Application developers write only job business code; `AddKoan()` supplies local wake automatically.
- Adding a Communication connector changes physical reach without Jobs registration or configuration.
- Coding agents see no alternate bus, queue, or provider vocabulary in the Jobs authoring surface.
- Operators see `jobs:wake` and `communication:framework-signals:default` facts backed by the same
  elected provider and health decision.
- Provider loss cannot make a durable submit false; the next poll recovers naturally.

## Acceptance

- Local signals wake, coalesce, time out, and are emitted by real job submission.
- Existing Jobs behavior remains green after removing the transport seam.
- Existing Entity Events/Transport behavior remains green after adding the internal lane and neutral wire.
- A real RabbitMQ container carries a typed internal signal through the direct-reference election.
- Transport fan-out, authenticated tenant carriage, mandatory no-route behavior, orchestration, and
  elected/unelected health remain green on schema 2.
- Direct intent selects RabbitMQ for FrameworkSignals; explicit in-process binding leaves the connector
  unelected and non-critical.

## Explicit non-claims

- an application-visible arbitrary-object bus;
- durable Jobs correctness from the signal;
- broadcast-to-every-node semantics—replicas compete in the stable Jobs worker group;
- retries, dedupe, dead letters, replay, or remote signal settlement;
- Cache coherence compatibility; or
- logical channel/routing grammar.

## Evidence

- `Koan.Jobs.Tests`: 77/77.
- `Koan.Communication.Tests`: 31/31.
- `Koan.Communication.Connector.RabbitMq.Tests`: 6/6 against RabbitMQ 3.13.
- Communication, Jobs, and the RabbitMQ connector build warning-as-error; the connector test graph
  retains two unrelated pre-existing Tenancy XML-documentation warnings when warnings are not promoted.
- Focused packaging, documentation, lock, stale-surface, diff, and privacy checks complete in this child.
- No release-certification suite runs.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-15
- Follow-up: inventory Cache coherence separately; do not force its node-broadcast/layered semantics
  through the Jobs competing-group contract.
- Reviewer: Codex implementation and executable evidence under maintainer approval.
