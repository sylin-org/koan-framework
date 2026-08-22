---
type: RECIPE
recipe: tell-another-system
title: "Tell another system when something happens"
domain: messaging
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/tell-another-system.md
gets_you: "A business event other code can react to, in this process or another one."
works_if: "Something happens to an Entity that a different part of the system should know about."
costs: "Nothing to operate in-process. A broker adds a service to run, secure, and monitor."
ingredients:
  - "one | Entity events and snapshot transport | Sylin.Koan.Communication"
  - "optional | carry events between processes | Sylin.Koan.Communication.Connector.RabbitMq"
---

# Tell another system when something happens

Two different things share one package, and confusing them is the usual mistake:

- **Events** mean *something happened to this Entity*. Other code reacts.
- **Transport** distributes *an isolated copy of current Entity state* to somewhere else.

Persistence knows about neither. Both are local-first and lift over collections and streams.

## When this is the answer

"Notify billing when an order is approved." "Push a copy to the reporting service." "Let the UI update
live."

Start in-process and stay there until something forces otherwise. Say plainly that a broker is a
service to run, secure, upgrade, and monitor — teams add one for a use case that never needed to leave
the process. The honest trigger for a broker is a **second deployable** that must react, not a wish for
decoupling inside one application.

Ask:

- **Is this an event or a copy?** "Tell them it happened" versus "give them the current state". The
  answer decides the shape, and the two are not interchangeable.
- **Does the receiver have to get it?** Acceptance and settlement are different promises; be explicit
  about which one is being made.
- **What happens on a duplicate?** Delivery retries. The receiver must tolerate it.

## Assembly

```powershell
dotnet add package Sylin.Koan.Communication
```

```csharp
await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

The event carries meaning; the transport carries state. Add a broker connector only when a second
process must receive it — the calls above do not change when you do.

Depth: [communication reference](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/communication/index.md).

## Prove it

1. **Behavior** — raising reaches the subscriber; sending delivers the expected snapshot.
2. **Composition** — assert whether the in-process floor or a broker actually carried it. These look
   identical from application code, which is the point and also the risk.
3. **Correction** — assert duplicate delivery, cancellation, and a broker outage behave as promised.
   Distinguish accepted from settled in the assertion, not just in the prose.

## Boundaries

- Event meaning is independent of transport; do not encode routing details into business events.
- Zero subscribers is not an error. Raising into a void succeeds.
- This is not a workflow engine. Ordering, compensation, and retries across steps belong to
  [background work](run-work-in-background.md).

## Interacts with

**Tenancy.** An event that crosses a process boundary must carry its tenant, or the receiver acts in
the wrong context — or, worse, in none.

**Jobs.** Events are the usual trigger for durable work; the job is what makes the reaction survive a
restart.
