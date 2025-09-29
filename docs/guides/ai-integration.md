---
type: GUIDE
domain: ai
title: "AI Integration Playbook"
audience: [developers, architects, ai-agents]
last_updated: 2025-09-28
framework_version: v0.6.2
status: current
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/guides/ai-integration.md
---

# AI Integration Playbook

## Contract

- **Inputs**: Koan AI provider configured, entities ready to store embeddings or AI outputs, and familiarity with Flow/Data pillars.
- **Outputs**: Chat endpoints, streaming responses, embedding pipelines, and RAG workflows that productionize AI without bespoke infrastructure.
- **Error Modes**: Provider rate limits, token exhaustion, missing embeddings on legacy records, or chat history overruns.
- **Success Criteria**: Deterministic chat behavior, embeddings persisted with vector indices, RAG pipelines reuse Flow/Data helpers, and observability covers cost plus latency.

### Edge Cases

- **Offline vs cloud providers** – ensure local Ollama fallbacks mirror cloud model interfaces.
- **Long prompts** – summarize conversation history to avoid truncation.
- **Embeddings** – keep dimensionality consistent across models; re-index when swapping providers.
- **Secrets management** – source credentials from options or secret stores rather than code.
- **Streaming gaps** – surface reconnection guidance to clients when SSE channels drop.

---

## How to Use This Playbook

- 📌 Canonical reference: [AI Pillar Reference](../reference/ai/index.md)
- 🌊 Flow automation: [Semantic Pipelines Playbook](./semantic-pipelines.md)
- 🗂️ Data storage: [Data Modeling Playbook](./data-modeling.md)
- 🔐 Access surfaces: [API Delivery Playbook](./building-apis.md)

Follow each step as you introduce or review AI surfaces in your service.

---

## 1. Choose a Provider Strategy

- Start with a local provider (Ollama) for fast iteration, then mirror configuration for hosted providers.
- Record provider, model, and region identifiers in configuration—never hardcode them.
- Capture currency and latency budgets before exposing endpoints.

```json
{
  "Koan": {
    "AI": {
      "Providers": {
        "Primary": {
          "Type": "OpenAI",
          "ApiKey": "{OPENAI_API_KEY}",
          "Model": "gpt-4"
        },
        "Fallback": {
          "Type": "Azure",
          "Endpoint": "{AZURE_ENDPOINT}",
          "ApiKey": "{AZURE_API_KEY}",
          "Model": "gpt-4o"
        }
      },
      "Budget": {
        "MaxTokensPerRequest": 2000,
        "MaxRequestsPerMinute": 60,
        "MaxCostPerDay": 50.0,
        "AlertThreshold": 0.8
      }
    }
  }
}
```

---

## 2. Build Chat Surfaces

- Use `IAi.ChatAsync` for synchronous responses.
- Introduce system prompts to anchor persona and guardrails.
- Log `AiChatResponse.Usage` for chargeback or quota tracking.

```csharp
[Route("api/[controller]")]
public class SummariesController : ControllerBase
{
    private readonly IAi _ai;

    public SummariesController(IAi ai) => _ai = ai;

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] SummaryRequest request, CancellationToken ct)
    {
        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Model = request.Model ?? "gpt-4",
            Messages =
            [
                new() { Role = AiMessageRole.System, Content = "Summarize the user content in three bullet points." },
                new() { Role = AiMessageRole.User, Content = request.Content }
            ]
        }, ct);

        return Ok(new
        {
            Summary = response.Choices?.FirstOrDefault()?.Message?.Content,
            response.Usage
        });
    }
}
```

---

## 3. Stream Responses (Optional)

- Expose Server-Sent Events when the provider supports streaming.
- Gracefully handle cancellations and client disconnects.
- Document retry/delay guidance for UI clients.

```csharp
[HttpGet("stream")]
public async Task Stream([FromQuery] string prompt, CancellationToken ct)
{
    Response.Headers.Append("Content-Type", "text/event-stream");

    await foreach (var chunk in _ai.ChatStreamAsync(new AiChatRequest
    {
        Model = "gpt-4o-mini",
        Messages =
        [
            new() { Role = AiMessageRole.System, Content = "Stream markdown back to the caller." },
            new() { Role = AiMessageRole.User, Content = prompt }
        ]
    }, ct))
    {
        await Response.WriteAsync($"data: {chunk.Delta}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

---

## 4. Persist Embeddings

- Annotate vector fields with `[VectorField]` to enable provider-backed search.
- Generate embeddings during writes or from Flow pipelines.
- Validate dimension counts before saving to avoid runtime errors.

```csharp
[DataAdapter("vector-store")]
public class DocumentIndex : Entity<DocumentIndex>
{
    public string DocumentId { get; set; } = "";
    public string Content { get; set; } = "";

    [VectorField]
    public float[] Embedding { get; set; } = [];
}
```

---

## 5. Build RAG Workflows

- Use vector queries to retrieve relevant documents, then assemble prompts with citations.
- Track source provenance for every generated answer.
- Run heavy orchestration inside Flow pipelines to gain retries, telemetry, and throttling.

---

## 6. Automate with Background Services

- Subscribe to domain events such as `DocumentUploaded` and enrich them asynchronously.
- Leverage `BackgroundServiceExtensions.On<TEvent>` for terse event handlers.
- Emit follow-up events (`DocumentIndexed`) so downstream services stay informed.

---

## 7. Route Across Multiple Models

- Centralize routing logic so cost vs fidelity decisions are transparent.
- Collect latency and quality metrics per provider/model pair.
- Expose configuration toggles to swap providers without redeploying code.

---

## 8. Control Cost & Tokens

- Run `IAi.TokenizeAsync` against large prompts to stay within provider limits.
- Trim or summarize conversation history once thresholds are exceeded.
- Surface alerts when the configured budget crosses the warning threshold.

---

## 9. Harden Observability

- Wrap AI calls with retries tuned to provider guidance.
- Emit structured logs including provider, model, prompt size, latency, and failure reason.
- Translate provider errors (429, safety breaks) into actionable HTTP responses.

---

## Review Checklist

- [ ] Provider configuration stored outside source control.
- [ ] Chat endpoints expose synchronous (and streaming where supported) variants.
- [ ] Embeddings persist reliably with dimension validation and retries.
- [ ] RAG flows cite sources and run inside Flow pipelines when orchestration grows complex.
- [ ] Token usage and cost metrics recorded for analysis.
- [ ] Error responses map provider failures to user-friendly payloads.

---

## Next Steps

- Combine AI outputs with Messaging to trigger downstream automations.
- Add moderation and safety filters before returning generated content.
- Explore agentic workflows by orchestrating multi-step prompts via the [Semantic Pipelines Playbook](./semantic-pipelines.md).

---

## Validation

- Last reviewed: 2025-09-28
- Verified across OpenAI, Azure, and Ollama provider adapters.