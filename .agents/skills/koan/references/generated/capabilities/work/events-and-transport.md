---
type: REFERENCE
domain: jobs
title: "Entity events and transport"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/work/events-and-transport.md
---

# Entity events and transport

Let an Entity say that something happened, or distribute an isolated copy of its current state,
without teaching persistence about either concern.

## You need

| Piece | Package | Note |
|---|---|---|
| Local Events and snapshot Transport | `Sylin.Koan.Communication` | adds `entity.Events` and `entity.Transport` terminals |
| Cross-process snapshot carriage (optional) | `Sylin.Koan.Communication.Connector.RabbitMq` | RabbitMQ implements Transport, not Entity Events |

## The constraint box

> **The constraint:** An Event carries business meaning; Transport carries current Entity state.
> Transport acceptance is not remote-handler settlement. RabbitMQ confirms durable publication and
> a receiver route, but does not promise exactly-once effects, inbox/outbox, replay, or remote
> completion; intended broker failure never silently falls back to local reach.

## Choose the promise before the provider

| They said | Use | First reach |
|---|---|---|
| “Tell billing the order was approved” | `order.Events.Raise<OrderApproved>()` | in-process unless a second deployable must react |
| “Give reporting the order as it is now” | `order.Transport.Send()` | in-process unless a second deployable must receive it |
| “Finish this reliably after the request” | a [background Job](background-jobs.md) | the Job owns retries and visible execution state |
| “A second service must receive snapshots” | Transport plus RabbitMQ | operate, secure, and monitor the broker |

## Leaves

- **Build and delivery proof:** [tell another system](../../recipes/tell-another-system.md)
- **Runtime contract:** collection, stream, handler, acceptance, and channel grammar:
  [Communication reference](../../reference/communication/index.md)
- **Connector contract:** cross-process guarantees and limits:
  [RabbitMQ README](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/Communication/RabbitMq/README.md)

Events commonly trigger Jobs; the Event names what happened and the Job makes the reaction survive a
restart.
