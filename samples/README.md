# Koan samples

Samples are Koan's public curriculum. Names describe the business result; numbers appear only where
order has meaning inside a cumulative journey.

These applications teach the Koan 0.20 preview. A sample proves behavior, not package maturity; check
the [generated product surface](../docs/reference/product-surface.md) before treating a capability as
supported.

On Windows, every listed sample also starts through the `start.bat` in its own root. The launcher uses
the same standard `dotnet run` path, works from any caller directory, and forwards optional application
arguments. The tables retain the portable cross-platform command as the execution contract.

## Start here

| Sample | Meaningful result | Start |
|---|---|---|
| [FirstUse](FirstUse/README.md) | Persist and approve one request; inspect the resulting API, facts, and governed agent tool | `dotnet run --project samples/FirstUse` |
| [GoldenJourney](GoldenJourney/README.md) | Grow that same application through a rule, durable job, and agent recommendation | `dotnet run --project samples/GoldenJourney` |

## Fundamentals

Use these when you want one concern in isolation.

| Sample | Meaningful result | Concepts | Start |
|---|---|---|---|
| [LocalChecklist](fundamentals/LocalChecklist/README.md) | Save a checklist, complete one item, and reload open work | console host, Entity statics, JSON | `dotnet run --project samples/fundamentals/LocalChecklist` |
| [TaskGraph](fundamentals/TaskGraph/README.md) | Resolve one task graph over scalar, set, and stream cardinalities | EntityController, Parent, Relatives, Cache | `dotnet run --project samples/fundamentals/TaskGraph` |

## Capability journeys

[GardenCoop](journeys/GardenCoop/README.md) is one application that grows in meaningful small steps:

1. [Garden Journal](journeys/GardenCoop/01-GardenJournal/README.md) turns dry readings into watering reminders.
2. [Local Discovery](journeys/GardenCoop/02-LocalDiscovery/README.md) keeps that complete application and adds local semantic produce search.

Each chapter is independently runnable. Every later chapter must preserve the earlier business result,
then add one visible capability.

## Complete applications

| Sample | Meaningful result | Concepts | Start |
|---|---|---|---|
| [AnimeRecommendations](applications/AnimeRecommendations/README.md) | Rate familiar anime and receive local, explainable recommendations shaped by taste and mood | Entity ratings, embedding-on-save, local vector search, one multi-Entity workflow | `dotnet run --project samples/applications/AnimeRecommendations` |
| [DevPortal](applications/DevPortal/README.md) | Approve local articles, publish them through named provider channels, and render entity-backed share cards | named sources, Entity transfer, OpenGraph | `dotnet run --project samples/applications/DevPortal` |
| [OrderIntake](applications/OrderIntake/README.md) | Run bounded order intake through one named source and keep a verified durable receipt | named sources, Entity batch work, Jobs, readiness | `dotnet run --project samples/applications/OrderIntake` |
| [SnapVault](applications/SnapVault/README.md) | Upload a photo into a local studio, durably organize and serve it, then share its event without exposing the vault | Entity media, Jobs, tenancy, access, optional AI/vector | `dotnet run --project samples/applications/SnapVault` |
| [CustomerCanon](applications/CustomerCanon/README.md) | Turn messy customer arrivals into one durable trusted customer, or reject them with reasons | CanonEntity, discovered contributors, generated Canon API, JSON | `dotnet run --project samples/applications/CustomerCanon` |

Other application directories are active graduation work, not current curriculum. Presence in the tree
is not a support claim.

## Graduation contract

A public sample must have one business sentence, a standard .NET command to a meaningful result, focused
executable evidence, and documentation that agrees with its source, provider requirements, facts, and
deployment shape. Business intent must dominate the application code; mechanics belong at framework
chokepoints. Private dogfood identities and unsupported performance or deployment claims do not belong here.
