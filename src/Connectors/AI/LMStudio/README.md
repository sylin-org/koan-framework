# Sylin.Koan.AI.Connector.LMStudio

LM Studio provider for Koan AI: OpenAI-compatible chat, streaming, embeddings, model listing, and readiness.

The generated [product surface](../../../../docs/reference/product-surface.md) owns support maturity;
this page owns LM Studio setup and limits.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.LMStudio
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

In Development, Koan discovers a healthy LM Studio server at its conventional local address. LM Studio is modeled
honestly as an external runtime: Koan does not claim to install or launch the desktop application.

## Exact configuration

```json
{
  "Koan": {
    "Ai": {
      "LMStudio": {
        "Endpoints": ["http://localhost:1234"],
        "DefaultModel": "qwen3-4b",
        "ApiKey": "optional-token",
        "RequestTimeoutSeconds": 120
      }
    }
  }
}
```

For one endpoint, `ConnectionStrings:LMStudio` is supported instead of `Endpoints`. Do not configure both.
Environment configuration uses ordinary .NET key mapping, for example
`Koan__Ai__LMStudio__ApiKey` and `ConnectionStrings__LMStudio`.

Explicit placement works in every environment. Automatic discovery follows `Koan:Ai:AutoDiscoveryEnabled` and,
outside Development, `Koan:Ai:AllowDiscoveryInNonDev`. Discovery validates `/v1/models` and, when a default model is
declared, requires that model to appear in the catalog.

## Runtime contract

- A ready provider publishes one source named `lmstudio`; ordered endpoint meshes are supported.
- When `DefaultModel` is absent, requests must supply a model.
- A configured default model that is unavailable produces degraded readiness; an explicit per-request model may
  still work.
- Authentication, serialization, HTTP, and cancellation failures surface to the caller.
- No reachable automatic candidate is normal inactivity, allowing another AI provider to serve the application.
- Conflicting explicit placement and unresolved explicit Zen Garden intent fail startup correctively.

## Boundaries

The provider requires a separately installed and running LM Studio server. Koan does not launch the desktop app,
load a model, manufacture credentials, retry inference, or guarantee OpenAI compatibility beyond the operations
implemented by this adapter.

See [TECHNICAL.md](./TECHNICAL.md) for discovery, ownership, and readiness details.
