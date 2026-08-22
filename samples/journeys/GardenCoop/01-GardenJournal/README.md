# GardenCoop 01 — Garden Journal

The first chapter receives sensor readings, binds them to garden beds, and keeps one watering reminder
active while recent soil humidity is dry. SQLite, REST, OpenAPI, admin, development authentication,
startup facts, and the dashboard arrive through referenced Koan capabilities.

## Run the meaningful result

From the repository root:

```pwsh
dotnet run --project samples/journeys/GardenCoop/01-GardenJournal -- --urls http://localhost:5000
```

Open <http://localhost:5000>. A fresh database starts with three plots and three readings. Bed 3 is dry,
so Entity lifecycle policy creates one active reminder. Post a recovery reading and that reminder becomes
acknowledged. No external service or configuration is required.

The same story is inspectable through HTTP:

```pwsh
Invoke-RestMethod http://localhost:5000/api/garden/plots
Invoke-RestMethod http://localhost:5000/api/garden/reminders
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

## Read the application

`Program.cs` is the complete four-line host. Entities name members, plots, sensors, readings, and reminders.
`EntityController<T>` declarations expose the conventional HTTP surface. `GardenAutomation` owns sensor
binding and watering policy at the write boundary. `GardenCoopModule` composes that policy, seeds the first
useful state, and explains it at startup.

This chapter also carries Koan's win-x64 NativeAOT configuration. On the pinned .NET 10.0.302 SDK with
10.0.10 runtime packs it publishes to a native executable and serves its reminders endpoint with
`/health/ready` green. NativeAOT sits outside the 1.x guarantee; the
[NativeAOT guide](../../../../docs/guides/nativeaot-howto.md) carries the recipe and its limits.

Continue to [Chapter 2 — Local Discovery](../02-LocalDiscovery/README.md) to add local semantic search without
losing any of this application.

---

**Working with a coding agent?** [AGENTS.md](../../../../AGENTS.md) at the repository root orients any
agent to Koan's application grammar, the evidence an application produces about itself, and the
capability map. If you lifted this sample out of the repository, start from the
[agent retrieval map](https://github.com/sylin-org/koan-framework/blob/main/llms.txt).
