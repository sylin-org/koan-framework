# Koan samples

Samples are a public product surface and the Koan curriculum. This index lists only applications with
a documented meaningful result and focused executable evidence. Other directories may be under
assessment; their presence is not a usage recommendation or support claim.

## Graduated examples

These samples currently have a documented meaningful path and focused executable evidence:

| Sample | Meaningful result | Concepts | Start |
|---|---|---|---|
| [S0.ConsoleJsonRepo](S0.ConsoleJsonRepo/README.md) | Save a local checklist, complete one item, and reload open work | console host, Entity statics, JSON, provider bounds | `cd samples/S0.ConsoleJsonRepo && dotnet run` |
| [FirstUse](FirstUse/README.md) | Persist and approve one request, inspect facts, expose governed MCP | bootstrap, Entity, SQLite, REST, facts, MCP | `dotnet run --project samples/FirstUse` |
| [GoldenJourney](GoldenJourney/README.md) | Grow the same request through a rule, durable job, and agent recommendation | cumulative composition, jobs, agent tools, recovery | `dotnet run --project samples/GoldenJourney` |
| [S1.Web](S1.Web/README.md) | Resolve one task graph over scalar, set, and stream cardinalities | EntityController, Parent, Relatives, Cache | `dotnet run --project samples/S1.Web` |
| [S10.DevPortal](S10.DevPortal/README.md) | Approve local articles and publish them idempotently through named provider channels | named sources, provider negotiation, Entity transfer | `cd samples/S10.DevPortal && dotnet run` |
| [g1c1.GardenCoop](guides/g1c1.GardenCoop/README.md) | Turn a dry sensor reading into a reminder, then observe recovery | lifecycle automation, SQLite, REST, facts, NativeAOT | `dotnet run --project samples/guides/g1c1.GardenCoop` |
| [g1c2.GardenCoopEmbedded](guides/g1c2.GardenCoopEmbedded/README.md) | Find local produce by meaning with no external service | Entity embeddings, ONNX, SQLite, sqlite-vec | `dotnet run --project samples/guides/g1c2.GardenCoopEmbedded` |

## What is not listed

An unlisted sample is not current curriculum. Maintainers track its graduation, archive, or deletion
outside the public learning path. Historical applications under [`archive/`](archive/ARCHIVED.md)
remain historical and carry no modernization or support promise.

## What every graduated sample guarantees

1. Business intent is the dominant application code.
2. `AddKoan()` is the normal bootstrap; extra application code owns real business policy.
3. References state capability intent, and runtime decisions are inspectable.
4. One standard .NET command reaches a meaningful result.
5. A focused test proves the business result, HTTP or host surface, and composition facts.
6. Any claimed container, external service, package-only, or NativeAOT shape is actually exercised.
7. README, source, dashboard, requests, solution membership, and maturity status agree.

## Contributing a sample

Start with one business sentence and a strict baseline. Identify the smallest honest host, the deliberate capability references, and the defining business result. Repair framework defects at their owner, remove obsolete sample ceremony, and add sample-specific executable proof before promoting documentation.

Do not add launch helpers for ordinary `dotnet run`, private dogfood identities, generic test
abstractions that hide the story, or deployment claims without evidence. A new sample joins this
index only after its business result, host lifecycle, projections, composition facts, and stated
deployment shape are exercised.
