---
type: REF
domain: ai
title: "AI"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-18
  status: demonstrated
  scope: AI runtime, prompt semantics, local provider composition, and HTTP projection
---

# AI

Koan AI lets application code state an operation—chat, embed, OCR, image generation, speech, reranking, or another
declared capability—while referenced providers own native protocol and model mechanics. `AddKoan()` compiles the
provider topology and reports which providers and sources became active.

## Shortest path

Install the runtime and one provider. Ollama is the smallest conventional local example:

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
app.MapGet("/summary", () => Client.Chat("Summarize today's orders."));
await app.RunAsync();
```

With Ollama listening on its conventional endpoint, no provider registration or configuration is required. If the
runtime is elsewhere, declare that exact placement:

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "Endpoints": ["http://ollama.internal:11434"],
        "DefaultModel": "llama3.2"
      }
    }
  }
}
```

`ConnectionStrings:Ollama` is the single-endpoint alternative. Configuring both forms is corrective startup failure,
not precedence guessing. See each provider package for its native runtime and model boundaries.

## Business-facing operations

`Client` is the application facade:

```csharp
string answer = await Client.Chat("Which orders need attention?");
float[] embedding = await Client.Embed("priority customer escalation");

await foreach (var delta in Client.Stream("Explain the fulfillment delay."))
    Console.Write(delta);
```

Other operations include OCR, image generation/editing, transcription, speech, description, classification,
structured extraction, reranking, translation, moderation, and video rendering. Availability depends on an active
provider that declares and implements the requested capability. Koan fails an unsupported operation; it does not
pretend every referenced provider supports every verb.

## Inspectable prompts

Prompt composition is an inert, immutable value operation. It does not activate AI, Data, or a provider:

```csharp
using Koan.AI.Prompt;

var prompt = Prompt.Create(p => p
    .System("You assist the order operations team.")
    .Instruct("Summarize order {orderId} for {audience}.")
    .Constrain("Use one sentence.")
    .Default("audience", "the morning reviewer"));

var summary = await Client.Chat(prompt, new { orderId = order.Id });
```

`Prompt.Parse`, `Prompt.Create`, `Resolve`, `UnresolvedVariables`, and `With` are in-memory semantics carried by
`Sylin.Koan.AI.Contracts` and brought transitively by the runtime.

When prompts must be ordinary editable application data, add `Sylin.Koan.AI.Prompt` plus a Data provider:

```csharp
await new PromptEntry
{
    Name = "order-summary",
    Version = 1,
    Status = PromptStatus.Active,
    Content = "Summarize order {orderId}."
}.Save();

var persisted = await PromptCatalog.Load("order-summary");
```

The catalog resolves latest-active or an exact version. It does not supply random A/B assignment, rollout stickiness,
approval workflows, or a management UI.

## Multiple providers and explicit routing

Providers contribute adapters and logical sources to one compiled runtime plan. When one eligible route is obvious,
Koan uses it. Applications can constrain a category through configuration or an explicit call scope when multiple
routes express materially different intent:

```csharp
using (Client.Scope(chat: "ollama"))
{
    var answer = await Client.Chat("Keep this request on the local chat source.");
}
```

Explicit source/model/endpoint intent wins. Koan does not promise automatic retry, provider fallback, budget
enforcement, rate limiting, or cost routing unless a separately documented capability implements it.

## HTTP projection

To expose the same runtime through HTTP, add the Web projection:

```powershell
dotnet add package Sylin.Koan.AI.Web
```

Keep the same `AddKoan()` bootstrap. The reference exposes `/ai/health`, `/ai/adapters`, `/ai/models`,
`/ai/capabilities`, `/ai/chat`, `/ai/chat/stream`, `/ai/embeddings`, `/ai/ocr`, and explicit provider model-management
routes. No `AddKoanAiWeb()` call exists.

The projection adds no authorization, quotas, retry policy, CORS policy, or universal error translation. Compose
those through their owning Koan/ASP.NET Core concerns before exposing AI routes outside a trusted boundary.

## Startup and failures

- A referenced but automatically undiscovered local provider is normal inactivity. Invalid explicit placement fails
  startup.
- `Client.IsAvailable` and `Client.TryResolve()` are optional host probes. Required operations without a live host fail
  correctively.
- Provider HTTP/model failures and cancellation remain visible to the caller.
- AI startup reporting projects the compiled provider/source decision. Framework health reports source-member state;
  `/ai/health` reports whether the HTTP projection has an active provider.
- Provider credentials, prompts, model output, and embeddings may be sensitive. Koan AI does not automatically redact,
  authorize, or encrypt application content.

## Related reading

- [Entity data](../data/index.md)
- [Vector providers](../cards/vector.md)
- [AnimeRecommendations complete local sample](../../../samples/applications/AnimeRecommendations/)
- [MCP and agent-facing surfaces](../cards/mcp.md)
- [Ollama package](../../../src/Connectors/AI/Ollama/README.md)
- [LM Studio package](../../../src/Connectors/AI/LMStudio/README.md)
- [ONNX package](../../../src/Connectors/AI/Onnx/README.md)
