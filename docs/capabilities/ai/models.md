---
type: REFERENCE
domain: ai
title: "Model discovery and lifecycle"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/ai/models.md - cold-executed on the Ollama path against published packages
    (feed probe): List/Inspect over /api/tags, keyword search across runtimes and across the persisted
    catalog, Deploy recording a route and Routes reflecting it, corrective failures naming missing
    capability or unknown model. Health deployment reporting verified against a source pin - packages
    published before it return no rows (pending next package release).
---

# Model discovery and lifecycle

Discover what your AI runtimes have installed, pull what they can acquire, deploy what they can serve,
and read the catalog back - through one `Model.*` facade routed to capable adapters.

## You need

| Piece | Package | Note |
|---|---|---|
| The `Model` facade and catalog | `Sylin.Koan.AI.Models` | registers `IModelService` through `AddKoan()` |
| One model-capable connector | e.g. `Sylin.Koan.AI.Connector.Ollama` | declares Pull / ModelList / ModelRemove / Serve.GGUF |
| Any Entity store | one data connector | the catalog is ordinary Entities |

Verified against: `Sylin.Koan.AI.Models` 1.0.6 or newer, `Sylin.Koan.AI.Connector.Ollama` 1.0.8 or
newer, `Sylin.Koan.App` 1.0.7 or newer, `Sylin.Koan.Data.Connector.Sqlite` 1.0.12 or newer (patch
releases compatible).

> **Pin the endpoint when discovery is slow.** Ollama auto-discovery probes the local server with a
> short timeout; on a cold or busy machine it can finish with an adapter that has zero sources, and
> the first model call then dies with a raw URI error rather than a named correction. Bypass
> discovery entirely with appsettings:
>
> ```json
> { "ConnectionStrings": { "Ollama": "http://localhost:11434" } }
> ```
>
> (`Koan:Ai:Ollama:Endpoints` with the same URL is the equivalent spelling.)

> **Two searches, two scopes.** `Model.Search("text", null)` filters adapter-reported runtime models
> by name. `Model.Search(new ModelQuery { ... })` filters the **persisted catalog only** - entries get
> there via Pull/Deploy/Register saves, not by listing. Pick the verb that matches where you expect
> the model to be.
>
> **Catalog rows from listing carry no format** and default to `SafeTensors`; Ollama serves GGUF. Set
> `entry.Format = ModelFormat.GGUF; await entry.Save();` before `Model.Deploy`, or resolution refuses
> with "No adapter with 'Serve.SafeTensors' capability".

## Assembly

```csharp
using Koan.AI.Contracts.Shared;
using Koan.AI.Models;
using Koan.Core;
using Koan.Data.Core;
using Microsoft.AspNetCore.Builder;
using System.Security.Cryptography;
using Koan.Data.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
```

From a running host (`app.StartAsync()` or later):

```csharp
using Koan.AI.Contracts.Shared;
using Koan.Data.Core;
using Koan.AI.Models;

var installed = await Model.List(null);                       // runtime + catalog, deduped by id
var entry = await Model.Inspect("nomic-embed-text:latest");   // catalog first, then adapters
var hits = await Model.Search("qwen", null);                  // runtime name-contains search

entry.Format = ModelFormat.GGUF;
await entry.Save();
await Model.Deploy(entry.HubId, null, null);                  // records the route in the catalog
var routes = await Model.Routes(entry.HubId);
var health = await Model.Health();                            // recorded deployments, per runtime
```

Facade verbs address models by `HubId` - the id the runtime reports. A catalog row's `.Id` is an
internal row key; do not pass it to `Deploy`, `Routes`, or `Pull`.

Acquisition rides the adapter's pull:

```csharp
using Koan.AI.Contracts.Shared;
using Koan.AI.Models;

var model = await Model.Pull("BAAI/bge-small-en-v1.5",
    progress: new Progress<ModelPullProgress>(p => Console.WriteLine($"{p.Phase}: {p.Percent:P0}")));
```

## Correction box

Every failure names its gap instead of pretending:

- No endpoint at all (nothing configured, discovery soft-failed at boot): "No Ollama endpoint was
  configured and startup auto-discovery found none. Set ConnectionStrings:Ollama or
  Koan:Ai:Ollama:Endpoints ..." - at first call, not buried in boot logs.
- Unknown model on pull: "Failed to pull '<id>' via Ollama AI Provider: ..." with the provider's own reason.
- No conversion support anywhere: "No adapter with 'Convert' capability. Registered adapters: [...]".
- Merge without merge support: NotSupportedException naming the missing declaration.
- Deploy against an un-served format: "No adapter with 'Serve.<Format>' capability".
- `Health` reports **catalog-recorded deployments only** - it does not observe runtime liveness, so an
  empty answer means nothing was deployed through Koan, not that a runtime is down.

## Do not, at this level

- Do not hand-edit catalog rows to claim deployments - `Deploy` is the only writer.
- Do not assume a listed model is deployable: deployment routes exist only where an adapter declares
  `Serve.<Format>` for the row's format.
- Do not call `Remove`/`Prune` casually - they retire catalog rows and ask the adapter to flush the
  underlying model.

## Leaves

- **Deep contract:** [AI.Models TECHNICAL](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.AI.Models/TECHNICAL.md)
