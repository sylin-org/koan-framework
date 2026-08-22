# Runnable counterparts

The compositions themselves live in the [recipe index](https://github.com/sylin-org/koan-framework/blob/main/docs/recipes/index.md) — each recipe states what an outcome gets you, what must already be true, what it costs to operate, and which packages it needs. Read that first; this file exists only to point at the version that runs.

Prefer showing a developer a running shape over describing one. These are compiled applications, so they cannot drift from the framework:

| Outcome | Runs today |
|---|---|
| Store my things and expose them over HTTP · Let an agent use my application | [FirstUse](https://github.com/sylin-org/koan-framework/blob/main/samples/FirstUse/README.md) — one Entity becomes persisted data, an HTTP API, and a governed agent tool |
| Return quickly and finish the work reliably · a bounded model recommendation | [GoldenJourney](https://github.com/sylin-org/koan-framework/blob/main/samples/GoldenJourney/README.md) — a rule, durable background work, then an agent recommendation |
| Search by meaning | [GardenCoop: Local Discovery](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/README.md) — no Docker, API key, or vector server |
| Let people upload files and serve them back · Keep customers from seeing each other's data | [SnapVault](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/SnapVault/README.md) |
| Turn inconsistent arrivals into one trusted record | [CustomerCanon](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/CustomerCanon/README.md) |
| Publishing to a second named source | [DevPortal](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/DevPortal/README.md) |
| Know it works, and know why when it doesn't | [OrderIntake](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/OrderIntake/README.md) — a batch, verification, cleanup, and an honest receipt |
| Exploring the domain before choosing infrastructure | [LocalChecklist](https://github.com/sylin-org/koan-framework/blob/main/samples/fundamentals/LocalChecklist/README.md) |
| Relationships across Entity, set, and stream | [TaskGraph](https://github.com/sylin-org/koan-framework/blob/main/samples/fundamentals/TaskGraph/README.md) |
| A useful application learning something new without losing what worked | [GardenCoop journey](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/README.md) |

Control who can do what, tell another system when something happens, and make repeated reads fast have no single counterpart; compose them from the recipes and the samples above. The [full catalog](https://github.com/sylin-org/koan-framework/blob/main/samples/README.md) lists every runnable example.
