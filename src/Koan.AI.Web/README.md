# Sylin.Koan.AI.Web

Provider-neutral HTTP projection for Koan AI. A package reference exposes chat, streaming, embeddings, OCR, provider
inspection, model inventory, and model-management operations through the same compiled AI runtime used in-process.

## Install and use

```powershell
dotnet add package Sylin.Koan.AI.Web
dotnet add package Sylin.Koan.AI.Connector.Ollama
```

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

No `AddKoanAiWeb()` call is required. The referenced module registers its controllers with Koan Web during
`AddKoan()`.

## HTTP surface

| Method | Route | Meaning |
|---|---|---|
| `GET` | `/ai/health` | Projection/provider availability; `Inactive` is valid when no provider is active. |
| `GET` | `/ai/adapters` | Active provider identities and declared capabilities. |
| `GET` | `/ai/models` | Model inventory plus provider-specific failures that prevented a complete result. |
| `GET` | `/ai/capabilities` | Capabilities declared by each active provider. |
| `POST` | `/ai/chat` | Provider-neutral `AiChatRequest` to `AiChatResponse`. |
| `POST` | `/ai/chat/stream` | Text deltas over server-sent events. |
| `POST` | `/ai/embeddings` | Provider-neutral embedding request/response. |
| `POST` | `/ai/ocr` | Multipart image OCR through an OCR-capable provider. |
| `POST` | `/ai/adapters/{adapterId}/models/{install|refresh|flush}` | Explicit provider model management. |

## Guarantees and boundaries

- The projection does not select, wrap, or own providers; it uses the AI runtime's compiled registry and pipeline.
- Cancellation reaches AI operations. Provider errors remain visible; model inventory returns explicit per-provider
  failures instead of silently dropping them.
- This package does not add authentication, authorization, quotas, retries, CORS policy, request-size policy, or a
  universal provider-error mapping. Compose those concerns through their owning ASP.NET Core/Koan modules.
- `/ai/health` describes projection/provider availability, not an external provider reachability SLA. Framework health
  and readiness remain owned by Koan AI's health contributor.

See [TECHNICAL.md](./TECHNICAL.md) for composition and response boundaries.
