# Sylin.Koan.AI.Connector.Ollama

Local and remote Ollama provider for Koan AI: chat, streaming, embeddings, vision, tools, and model operations.

The generated [product surface](../../../../docs/reference/product-surface.md) owns support maturity;
this page owns Ollama setup and limits.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.Ollama
```

The package reference is the provider declaration. There is no Ollama-specific registration call:

```csharp
using Koan.AI;
using Koan.Core;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();
using var app = builder.Build();
await app.StartAsync();

Console.WriteLine(await Client.Chat("What makes a good domain model?"));
```

In Development, Koan looks for a healthy Ollama runtime at the conventional local or container address. A ready
runtime becomes an `ollama` AI source automatically. Startup reports whether the provider is ready, inactive, or
explicitly configured.

## Exact configuration

Use one of these placement forms, not both:

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "Endpoints": ["http://localhost:11434"],
        "DefaultModel": "qwen3:4b",
        "MaxConcurrentRequests": 3
      }
    }
  }
}
```

For one endpoint, standard .NET connection-string configuration is also supported:

```json
{ "ConnectionStrings": { "Ollama": "http://localhost:11434" } }
```

Explicit placement is honored in every environment. Automatic discovery follows `Koan:Ai:AutoDiscoveryEnabled`
and, outside Development, `Koan:Ai:AllowDiscoveryInNonDev`. A `zen-garden://` connection string is resolved only
when the functional Zen Garden engine is present and can satisfy the intent.

## Runtime contract

- Endpoint order is preserved and duplicate endpoints are removed.
- Provider configuration never creates a second legacy or `Default` source.
- Explicit configuration is intent: conflicting placement or an unresolved explicit Zen Garden URI fails startup
  with a corrective error.
- No reachable auto-discovery candidate is normal inactivity; the application can still start and another provider
  may be elected.
- Requests can select a model explicitly; otherwise `DefaultModel` is used.
- Cancellation and provider HTTP failures propagate to the caller.

Koan does not install Ollama or pull the default model merely because this package is referenced. Model pull and
removal remain explicit operations.

## Boundaries

The provider requires a separately installed, reachable Ollama runtime. It does not promise model availability,
authentication, TLS termination, retries, or compatibility between every model and every advertised protocol
operation.

See [TECHNICAL.md](./TECHNICAL.md) for provider ownership and routing details.
