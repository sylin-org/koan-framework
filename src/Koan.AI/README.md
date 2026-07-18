# Sylin.Koan.AI

The AI capability ring for Koan: one business-facing client, capability-aware routing, provider composition, source
health, and startup inspection.

## Smallest meaningful use

Install this runtime and one provider, for example:

```powershell
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
```

```csharp
using Koan.AI;
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.MapGet("/summary", async () => await Client.Chat("Summarize today's orders."));
await app.RunAsync();
```

`AddKoan()` is the complete application bootstrap. Referenced provider modules describe their capabilities; Koan
compiles the host's provider topology, activates what is actually usable, and reports the result at startup. There is
no provider registration list for the application to maintain.

## The application surface

`Client` exposes semantic operations such as `Chat`, `Stream`, `Embed`, `Ocr`, `Imagine`, `Transcribe`, `Speak`,
`Rerank`, `Translate`, and `Moderate`. The application asks for an operation; the AI pipeline elects a source and
adapter that truthfully advertise that capability.

```csharp
string answer = await Client.Chat("Which orders need attention?");
float[] meaning = await Client.Embed("priority customer escalation");
```

Category configuration can constrain source or model without leaking provider mechanics into business code. Named
sources under `Koan:Ai:Sources` are the advanced routing surface; ordinary applications normally need only a provider
reference and, when conventions cannot locate it, that provider's exact endpoint configuration.

## Host and failure contract

- `Client.IsAvailable` and `Client.TryResolve()` are optional probes. They return absence for a missing or disposed
  host.
- Required operations throw `KoanHostContextException` when no live composed AI pipeline exists.
- Referencing the AI runtime without a usable provider is allowed; invoking an unsupported operation is not.
- Explicit provider configuration is intent and fails startup when invalid. Automatic candidates may be absent
  without making the application unhealthy.
- Provider HTTP/model errors and cancellation remain visible to the caller.

Provider packages document their exact discovery, configuration, and guarantee boundaries. See
[TECHNICAL.md](./TECHNICAL.md) for the compilation and routing model.
