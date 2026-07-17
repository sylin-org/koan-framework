# Koan samples

Samples are a public product surface and the Koan curriculum. Every active sample must be a golden,
executable example of current Koan use—not merely code that happens to compile. The assessment labels
below are temporary migration states; they must graduate or leave the active portfolio before V1.

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

## Portfolio under graduation

The remaining non-archived projects are not yet V1 curriculum claims. They stay visible so their disposition is honest:

| Disposition | Projects | Meaning |
|---|---|---|
| Assess capability/deployment | `S14.AdapterBench` | benchmark/job claims require focused execution |
| Assess dogfood | `S5.Recs`, `S6.SnapVault`, `S18.Prism`, `S8.Canon` / Api / Shared | valuable broad surfaces; prerequisites and business proofs must be explicit |
| Incubate | `S7.Meridian`, `S19.McpCatalogSample`, `S20.OpenGraph` | outside the maintained solution pending assessment |
| Archive or delete | `S3.Mq.Sample`, `S16.PantryPal` ghost directories | no executable project; not supported samples |
| Archived | [`archive/`](archive/ARCHIVED.md) | historical material; no modernization promise |

The exact inventory and queue live in [R10-02](../docs/initiatives/koan-v1/work-items/r10/R10-02-portfolio-inventory.md). Graduation criteria live in the [golden-sample contract](../docs/initiatives/koan-v1/work-items/r10/GOLDEN-SAMPLE-GRADUATION.md).

## What every graduated sample guarantees

1. Business intent is the dominant application code.
2. `AddKoan()` is the normal bootstrap; extra application code owns real business policy.
3. References state capability intent, and runtime decisions are inspectable.
4. One standard .NET command reaches a meaningful result.
5. A focused test proves the business result, HTTP or host surface, and composition facts.
6. Any claimed container, external service, package-only, or NativeAOT shape is actually exercised.
7. README, source, dashboard, requests, solution membership, and maturity status agree.

## Contributing or graduating a sample

Start with one business sentence and a strict baseline. Identify the smallest honest host, the deliberate capability references, and the defining business result. Repair framework defects at their owner, remove obsolete sample ceremony, and add sample-specific executable proof before promoting documentation.

Do not add launch helpers for ordinary `dotnet run`, private dogfood identities, generic test abstractions that hide the story, or deployment claims without evidence.
