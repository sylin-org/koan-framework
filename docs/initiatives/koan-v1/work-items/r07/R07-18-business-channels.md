---
type: SPEC
domain: framework
title: "R07-18 - Business-Named Communication Channels"
audience: [architects, maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: startup-declared logical channels, per-channel election, binding, facts, and RabbitMQ carriage
---

# R07-18 — Business-named Communication channels

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-17
- Unlocks: R07 parent closure and T7 readiness assessment
- Owner: Communication route planning and adapter binding

## Meaningful outcome

The default `AddKoan()` ring stays configuration-free. When a workflow needs an explicit reach or
guarantee policy, application code names that business channel at the existing terminal while the host
declares its provider binding:

```csharp
await urgentOrders.Transport.Send(ct, channel: "priority");
await order.Events.Raise<OrderEscalated>(ct, channel: "priority");
```

The channel is not a provider, authorization rule, or receiver predicate. It is a stable logical route
policy whose selected adapter and assurance are visible at startup.

## Architecture

- `default` remains inferred and uses the existing top-level provider pins.
- Named channels are declared under `Koan:Communication:Channels:{name}`. Each may pin Transport and
  Events independently; absent pins use the same direct-reference/built-in election as `default`.
- One router plan owns normalized channel identity, per-lane election, local group binding, publication,
  and facts. Unknown channels reject before source enumeration.
- Every typed public handler group binds once on every declared public channel. Each adapter receives
  only the bindings for routes elected to that adapter.
- RabbitMQ includes channel identity in routing keys and queues. The connector still owns physical
  topology and does not learn business meaning.
- Internal Jobs and Cache routes remain on their inferred framework-owned channels.

## Non-claims

- dynamic channels created after host startup;
- channel-based authorization, confidentiality, or receiver filtering;
- automatic sender branching, mirroring, failover, or topology DSL;
- RabbitMQ Events, retries, deduplication, remote settlement, schema aliases, or cross-application
  integration; or
- provider types or `Via<TProvider>()` in domain code.

## Acceptance

- the unchanged default local and RabbitMQ contracts remain green;
- scalar, finite, and async Entity syntax accepts an optional business channel;
- a declared local channel carries Transport and Events with ordinary handlers and settlement;
- invalid or undeclared channels reject correctively before source enumeration;
- election is independent per lane/channel and adapters receive only their elected bindings;
- a real RabbitMQ named-channel route preserves isolated group fan-out and reports durable acceptance;
- startup/operator/agent facts report every declared public route and group binding from the runtime
  plan; and
- strict builds, focused packages, docs/examples/diff/privacy gates pass without release certification.

## Evidence

- `Koan.Communication.Tests` passes 37/37, including configuration-bound local Transport/Events,
  pre-enumeration unknown-channel rejection, invalid startup declarations, independent election, and
  adapter-scoped bindings.
- `Koan.Communication.Connector.RabbitMq.Tests` passes 9/9 against the real broker, including a named
  RabbitMQ Transport channel beside a process-local default, isolated two-group fan-out, facts, and a
  channel-qualified topology identity cell.
- Entity Language passes 25/25. Its consumer fixture compiles named scalar, finite-set, and async-stream
  Transport and Events terminals, including explicit Event details.
- Communication and RabbitMQ build warning-as-error with zero warnings/errors. FirstUse and
  GoldenJourney build clean and their checked-in locks now record Communication 0.20; the RabbitMQ
  suite lock records both independently versioned 0.20 owners.
- Both packages pack at 0.20.0 with DLL, XML documentation, README, symbols, and exact source-graph
  dependencies. Package inventory remains 112 independently versioned owners.
- Docs lint reports 0 errors / 1584 historical or version-front-matter warnings; changed marked
  examples pass 2/2. Diff, stale-public-claim, and privacy checks pass.
- No full-solution or public-release certification suite ran. No package, release, tag, push, or
  remote mutation occurred.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: stable distributed contract/group aliases and heterogeneous evolution remain a deliberate
  post-cycle decision in PMC-023; they are not hidden inside business channels.
- Reviewer: Codex implementation under maintainer standing approval.
