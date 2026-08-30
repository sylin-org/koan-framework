# Sylin.Koan.AI.Connector.LlamaCpp

llama.cpp (`llama-server`) provider for Koan AI: OpenAI-compatible chat, streaming, embeddings, model
listing, and readiness against a locally served GGUF model.

> **Supported provider** (member of `local-ai-provider-composition`): the wire contract is proven
> against a deterministic llama-server wire-contract service — real sockets, SSE, status codes, recorded
> requests. Model-inference behavior (generation quality, tokenizer drift) belongs to the model and the
> server build, not to this claim.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.LlamaCpp
```

The reference activates the provider through the normal Koan boot path; no provider-specific setup method exists:

```csharp
using Koan.AI;
using Koan.Core;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();
using var app = builder.Build();
await app.StartAsync();

Console.WriteLine(await Client.Chat("Summarize this release in one sentence."));
```

In Development, Koan discovers a healthy llama-server at its conventional local address. llama.cpp is modeled
honestly as an external runtime: Koan does not claim to build llama.cpp, download GGUF weights, or start the server.

## Exact configuration

```json
{
  "Koan": {
    "Ai": {
      "LlamaCpp": {
        "Endpoints": ["http://localhost:8080"],
        "DefaultModel": "qwen2.5-0.5b-instruct",
        "ApiKey": "optional-token",
        "RequestTimeoutSeconds": 120
      }
    }
  }
}
```

For one endpoint, `ConnectionStrings:LlamaCpp` is supported instead of `Endpoints`. Do not configure both.
Environment configuration uses ordinary .NET key mapping, for example
`Koan__Ai__LlamaCpp__ApiKey` and `ConnectionStrings__LlamaCpp`.

Explicit placement works in every environment. Automatic discovery follows `Koan:Ai:AutoDiscoveryEnabled` and,
outside Development, `Koan:Ai:AllowDiscoveryInNonDev`. Discovery validates `/v1/models` and, when a default model is
declared, requires that model to appear in the catalog.

## What it adds

| Capability | State |
|---|---|
| Chat and streaming over `POST /v1/chat/completions` (SSE, `data:`/`[DONE]` framing) | declared |
| Embeddings over `POST /v1/embeddings` (requires llama-server started with `--embedding`) | declared |
| Readiness from `GET /health` (503 while the model loads → not ready), degraded when the default model is absent from `/v1/models` | declared |
| Model listing | declared |
| Tools, vision, model pull/remove | **not declared** — llama-server has no model manager, and tool calling is template-dependent; the adapter never claims it |

## Limits

- llama-server serves the ONE model it was started with; a request naming another model is refused
  by the server and the adapter passes the refusal through — no silent model substitution.
- Embeddings fail loudly when the server was started without `--embedding`, because `/health` stays
  green without the flag.
- Endpoints normalize by stripping a trailing `/v1`; configure either the server root or root+v1.
- A request without a model when no `DefaultModel` is configured throws correctively.

See [TECHNICAL.md](./TECHNICAL.md) for discovery, ownership, and readiness details.
