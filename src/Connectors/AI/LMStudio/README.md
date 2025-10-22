# Sylin.Koan.AI.Connector.LMStudio

> **Contract**  
> Inputs: Koan AI chat or embedding requests mapped to LM Studio's OpenAI-compatible API.  
> Outputs: Text completions, streaming deltas, embedding vectors, and model metadata.  
> Error Modes: HTTP failures, model not found, readiness timeout, serialization faults.  
> Criteria: Adapter registered via Koan auto-registrar, discovery resolved base URL, default model reachable or readiness marked degraded.

LM Studio adapter for Koan. Provides local OpenAI-compatible chat, streaming, and embeddings routed through LM Studio runtimes (desktop or headless) with Koan's autonomous discovery.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.LMStudio
```

## Minimal setup

Register Koan with the LM Studio provider (typical ASP.NET `Program.cs`):

```csharp
// using Koan.AI; using Koan.AI.Connector.LMStudio; using Koan.AI.Web;
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKoan()
    .AddAi()
    .AddLMStudioFromConfig() // options resolved via configuration + discovery
    .AddAiWeb(); // optional HTTP endpoints under /ai

var app = builder.Build();
app.MapControllers();
app.Run();
```

Then prompt LM Studio through the engine facade:

```csharp
using Koan.AI;

var response = await Engine.Prompt(
    "Summarize Koan adapter discovery in one sentence.",
    model: "phi3:mini"
);

Console.WriteLine(response.Text);
```

Enable HTTP access (when `AddAiWeb()` is registered):

```
POST /ai/chat
{
  "model": "phi3:mini",
  "messages": [
    { "role": "user", "content": "Summarize Koan adapter discovery in one sentence." }
  ]
}
```

## Discovery and configuration

- `ConnectionString` defaults to `auto`. The adapter probes LM Studio on host-first addresses (`http://localhost:1234`) and container fallbacks.
- Environment variables:
  - `LMSTUDIO_API_BASE_URL` single endpoint override.
  - `Koan_AI_LMSTUDIO_URLS` semicolon/comma separated list for multiple runtimes.
  - `LMSTUDIO_API_KEY` attaches a Bearer token when LM Studio enforces auth.
- Configuration keys (appsettings):
  - `Koan:Ai:Provider:LMStudio:BaseUrl`
  - `Koan:Ai:Provider:LMStudio:DefaultModel`
  - `Koan:Ai:Provider:LMStudio:ApiKey`

## Features

- Non-streaming chat completions via `/v1/chat/completions`.
- Streaming chat deltas (Server-Sent Events) surfaced through `Engine.PromptStream`.
- Embeddings via `/v1/embeddings` with multi-input batching.
- Model enumeration and readiness detection through `/v1/models`.
- Koan orchestration metadata describes required container image, ports, and health endpoints.

## Edge cases to watch

- Default model missing: readiness downgrades to `Degraded`, requests still work when explicit model supplied.
- Large payloads: LM Studio inherits OpenAI-compatible limits; monitor HTTP 413/429 responses.
- Streaming cancel: cancellation tokens immediately abort SSE consumption and close the HTTP stream.
- Auth failures: missing/invalid `LMSTUDIO_API_KEY` returns HTTP 401; surfaced as adapter exceptions.
- Multiple instances: when `Koan_AI_LMSTUDIO_URLS` enumerates several endpoints, all are registered with routing weights/labels from options.

## Links

- LM Studio: https://lmstudio.ai
- Koan AI adapters ADR: ../../../../docs/decisions/AI-0008-adapters-and-registry.md
- OpenAI compatibility (Koan): ../../../../docs/decisions/AI-0005-protocol-surfaces.md
