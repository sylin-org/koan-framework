---
type: SPEC
domain: framework
title: "R07-10 - Communication Provider Election and RabbitMQ Transport"
audience: [architects, maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: provider-neutral Communication routing and real RabbitMQ Transport conformance
---

# R07-10 — Communication provider election and RabbitMQ Transport

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-09
- Unlocks: additional Communication adapters and internal Jobs/Cache convergence
- Owner: Communication route election, provider contract, host wire, and RabbitMQ Transport

## Meaningful outcome

An application can move Entity Transport from the local process onto RabbitMQ by directly
referencing one connector. Its Entity, receiver, and `AddKoan()` code do not change. Koan elects the
connector for Transport only, retains process-local Events, provisions or discovers the broker, and
reports the exact route and guarantee at boot.

Without any connector, the complete local semantic ring still works. A transitive connector cannot
change network reach. A directly intended external connector that is unavailable fails correctively
and never weakens into a local route.

## Architecture

- Core owns the general `[ProviderPriority]` declaration; Data, Vector, Cache, and Communication use
  the same priority concept instead of Communication creating a private copy.
- Communication owns one `ICommunicationAdapter` boundary with immutable declarations for lane,
  assurance, semantic capabilities, direct-reference identities, stable bindings, and publication
  acceptance.
- `CommunicationRouter` elects one provider per lane. Explicit binding wins, then R07-09 direct
  application intent; absent either, only the minimum-priority built-in floor participates.
- Hard semantic capabilities gate eligibility before assurance and priority. Direct or explicit
  external intent never falls back to a process-local route.
- The local runtime now implements the same adapter boundary as external connectors. One host-owned
  wire codec and ingress path preserve Entity snapshots, typed group identity, context carriage, and
  validation for every provider.
- RabbitMQ claims Transport only. It uses one durable direct exchange per application mesh, one
  durable queue per stable receiver group and contract, confirm-enabled mandatory persistent
  publication, bounded prefetch, and manual consumer acknowledgement.
- Communication owns context bytes; RabbitMQ authenticates the exact wire body with per-mesh HMAC
  before supplying authenticated ingress provenance. The connector never learns tenant semantics.
- Publisher confirmation is observable and reported as `durably-acknowledged`. Remote handler
  settlement is not observable and the receipt says so.

## Delight contract

- New application: `AddKoan()` plus business handlers gives complete local Events and Transport.
- Distributed application: add one package/project reference; the same `.Transport.Send()` reaches
  the RabbitMQ mesh without routing or queue code.
- Application intent is visible in the direct-reference manifest and the elected result is visible in
  startup/operator/agent facts.
- Health becomes critical only for the elected external route. An installed-but-unelected candidate
  does not make an otherwise healthy application unready.
- Failures name the selected provider and correction; the framework never hides a reach change behind
  fallback.

## Acceptance

- Existing local Communication behavior remains green after the in-process provider migration.
- A registered but transitively unintended provider cannot displace the local floor.
- Explicit unavailable provider intent fails host startup and states that no local fallback occurs.
- A real RabbitMQ container proves direct-reference election, confirmed publication, two-group
  fan-out, independent deserialized copies, and unavailable remote settlement.
- A real cross-adapter wire proves tenant context restoration only inside the receiver and absence
  after dispatch.
- RabbitMQ mandatory return proves zero receiver groups become a typed no-receiver failure without
  local delivery.
- Runtime facts prove RabbitMQ Transport plus local Events; connector health proves active, critical,
  and ready after election.
- Connector/source builds, packs, focused provider-priority consumers, docs, diff, and privacy gates
  pass without running release certification.

## Explicit non-claims

- RabbitMQ Events or configurable logical channel authoring;
- retries, deduplication, inbox/outbox, dead-letter policy, replay, or remote settlement;
- exactly-once handler effects or atomic coupling to Data;
- schema aliases, rolling contract migration, or bounded-context integration;
- adapter-defined Entity/context wire formats;
- automatic election from transitive module presence; or
- Jobs wake and Cache coherence migration in this child.

## Evidence

- `Koan.Communication.Tests` passes 31/31, including retained local Event/Transport behavior,
  unintended-candidate isolation, unavailable explicit intent, and known-zero external route
  rejection.
- `Koan.Communication.Connector.RabbitMq.Tests` passes 5/5 against RabbitMQ 3.13: direct election,
  durable confirmed fan-out, authenticated tenant carriage, mandatory no-route, runtime facts,
  elected/unelected health posture, and direct-intent orchestration.
- Communication and the RabbitMQ connector build warning-as-error. Web builds warning-clean after the
  shared provider-priority move; focused Data Vector 1/1, Cache topology 2/2, and Cache Messaging 1/1
  proofs retain their ranking behavior.
- The connector owns a 0.18 `version.json` and packs as
  `Sylin.Koan.Communication.Connector.RabbitMq` with DLL, XML documentation, README, symbols, and exact
  dependencies.
- Documentation lint reports 0 errors and 1,575 historical warnings. Diff check and the scoped
  privacy/stale-claim inventory pass. No release-certification suite ran.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-15
- Follow-up: choose the smallest internal Jobs/Cache convergence child rather than adding public
  routing grammar.
- Reviewer: Codex implementation and executable evidence under maintainer approval.
