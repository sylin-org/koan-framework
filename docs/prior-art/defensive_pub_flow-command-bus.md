# Defensive Publication: Fire-and-Forget Flow Command Bus with Targetable Dispatch

## Header Block

- **Title:** Fire-and-Forget Command Bus with Targetable Dispatch, HTTP Facade, and Broadcast Semantics for Data Pipeline Frameworks
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Data pipeline command infrastructure, specifically methods for dispatching operational commands to adapters within an ETL/data integration framework using fire-and-forget semantics with optional targeting.
- **Keywords:** command bus, fire-and-forget, targetable dispatch, broadcast, ETL, data pipeline, adapter commands, HTTP facade, 202 accepted, operational commands

---

## 1. Problem Statement

Data integration pipelines involve multiple adapters (connectors to external systems like Shopify, Salesforce, databases). These adapters periodically need operational commands — cache refresh, reindex, pause/resume, configuration reload — that are not data events and don't belong in the event/message stream.

Existing solutions either overload the event stream (mixing operational commands with data events, complicating consumers), use heavyweight message brokers (MassTransit, RabbitMQ — excessive for simple operational signals), or require in-process mediator patterns (MediatR — no HTTP accessibility, requires handler registration, synchronous by default).

What is needed is a lightweight command dispatch mechanism that: (a) is fire-and-forget (no reply expected), (b) supports targeting specific adapters or broadcasting to all, (c) exposes an HTTP facade for external triggering, and (d) treats unknown commands as non-errors (logged but not failed).

---

## 2. Prior Art Summary

**MediatR:** In-process mediator requiring explicit handler registration per command type. Synchronous by default. No HTTP facade. Missing handler throws exception. No targeting mechanism.

**MassTransit / NServiceBus:** Full message broker integration (RabbitMQ, Azure Service Bus). Heavyweight for simple operational commands. Requires broker infrastructure. Expects reliable delivery and acknowledgement — not fire-and-forget.

**Kafka / Event Hubs:** Stream processing platforms. Commands mixed with data events create consumer complexity. No native targeting mechanism. Designed for ordered, durable delivery — opposite of fire-and-forget semantics.

**Custom HTTP endpoints:** Developers can build per-command endpoints, but this requires boilerplate per command and doesn't provide a unified dispatch mechanism with targeting.

**Specific gaps:**
1. No framework provides a fire-and-forget command bus specifically designed for data pipeline adapter operations.
2. No system combines broadcast + targeted dispatch with an HTTP facade that always returns 202.
3. No system treats unknown commands as non-errors by design.

---

## 3. Detailed Description of the Invention

### 3.1 Dispatch API

```
// Outbound (sending commands)
Flow.Outbound.SendCommand(
    name: "refresh-cache",
    args: { region: "APAC", force: true },
    target: "shopify:main"  // null = broadcast
);

// Inbound (receiving commands)
Flow.Inbound.On(
    name: "refresh-cache",
    handler: async (args, ct) => {
        await CacheService.Refresh(args["region"]?.ToString());
    },
    target: "shopify:main"  // null = receive all broadcasts
);
```

### 3.2 Targeting Semantics

- **target = null:** Broadcast — command delivered to all registered handlers for that command name.
- **target = "system:adapter":** Targeted — command delivered only to handlers registered with matching target identifier.
- Target matching is exact string comparison.
- A handler registered with target = null receives broadcasts but not targeted commands intended for other adapters.

### 3.3 HTTP Facade

```
POST /api/flow/commands/{command}?k=v&target=xyz
Content-Type: application/json (optional body)

Response: 202 Accepted (always)
```

- Args merged from query string parameters and JSON body
- Simple type coercion: strings from query string, typed values from JSON body
- Always returns 202 — the command is accepted for processing regardless of whether a handler exists
- Unknown commands: logged at Warning level, 202 returned
- Missing handlers: logged at Warning level, 202 returned

### 3.4 Execution Model

- All command handling is parallel and non-blocking by default
- Multiple handlers can register for the same command name (each receives a copy)
- Handler exceptions are caught, logged, and do not affect other handlers or the HTTP response
- No retry mechanism — fire-and-forget semantics mean the sender does not expect confirmation
- No ordering guarantees between commands or between handlers for the same command

### 3.5 Relationship to Flow Pipeline

The command bus operates alongside but independently from the Flow data pipeline:
- Data flows through: Adapter → Normalize → Associate → Key → Project → Store
- Commands flow through: Sender → CommandBus → Handler(s)
- Commands do not enter the data pipeline; they are operational signals
- Commands can trigger pipeline actions (e.g., "reprocess" command causes an adapter to re-emit data)

---

## 4. Claims-Style Disclosure

1. A fire-and-forget command bus for data pipeline frameworks wherein commands are dispatched with optional target identifiers, supporting both broadcast (null target) and targeted delivery (specific adapter identifier), distinct from message brokers in that no delivery confirmation, ordering, or durability is provided or expected.

2. An HTTP facade for pipeline command dispatch that always returns 202 Accepted regardless of handler existence, treating unknown commands and missing handlers as non-error conditions (logged at Warning level), distinct from REST APIs where missing endpoints return 404 and missing handlers return 500.

3. A command argument merging mechanism that combines HTTP query string parameters and JSON request body into a unified argument dictionary with simple type coercion, enabling command invocation via both programmatic API and HTTP without separate serialization contracts.

4. A parallel, non-blocking command execution model wherein multiple handlers registered for the same command name each receive an independent invocation, with handler exceptions isolated (caught and logged without affecting other handlers or the HTTP response).

5. An architectural separation between data pipeline events and operational commands wherein commands operate on a dedicated dispatch channel that does not enter the data pipeline's normalize/associate/key/project flow, preventing operational signals from polluting the data stream.

---

## 5. Implementation Evidence

- **ADR:** `docs/decisions/FLOW-0070-flow-commands-bus.md`
- **Framework Version:** Koan Framework v0.6.3 (proposed, pre-implementation)
- **Related:** Flow/Canon pillar architecture

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** This is just a pub/sub system with an HTTP endpoint. Every message broker provides this.

**Author revision:** The distinction is the deliberate absence of broker infrastructure. The command bus is in-process with an HTTP facade — no external dependencies (RabbitMQ, Kafka, etc.). The 202-always semantics and non-error treatment of unknown commands are architectural choices specific to operational pipeline commands, not general messaging. Added explicit comparison showing that message brokers provide delivery guarantees this system intentionally omits.

### Pass 2
**Antagonist:** The "always 202" pattern is just a webhook receiver. Many systems accept webhooks and return 202 regardless of processing outcome.

**Author revision:** Webhook receivers accept external events; this system dispatches internal operational commands. The targeting mechanism (broadcast vs. specific adapter) and the integration with the data pipeline framework's adapter identity system (`[FlowAdapter]` attribute) distinguish this from generic webhook receivers. The command bus knows about pipeline topology; a webhook receiver does not.

### Pass 3
**Antagonist:** No further objections. The combination of fire-and-forget semantics, pipeline-aware targeting, non-error unknown commands, and HTTP facade without broker infrastructure is sufficiently described.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
