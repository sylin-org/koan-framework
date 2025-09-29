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
        var summary = await _ai.ChatAsync(new AiChatRequest
        {
            Model = "llama2",
            Messages = [
                new() { Role = AiMessageRole.System, Content = "Summarize this document concisely." },
                new() { Role = AiMessageRole.User, Content = request.Content }
            ]
        });

        var sentiment = await _ai.ChatAsync(new AiChatRequest
        {
            Model = "sentiment-analysis",
            Messages = [
                new() { Role = AiMessageRole.System, Content = "Analyze sentiment: positive, negative, or neutral." },
                new() { Role = AiMessageRole.User, Content = request.Content }
            ]
        });

        return Ok(new
        {
            Summary = summary.Choices?.FirstOrDefault()?.Message?.Content,
            Sentiment = sentiment.Choices?.FirstOrDefault()?.Message?.Content
        });
    }
}
```

Model selection per task.

## Budget Management

```json
{
  "Koan": {
    "AI": {
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

```csharp
public class BudgetMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Budget checks happen automatically
        // Requests are blocked when limits exceeded
        await next(context);
    }
}
```

Automatic cost protection.

## Production Configuration

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
          "ApiKey": "{AZURE_API_KEY}"
        }
    # AI Integration Playbook

    ## Contract

    - **Inputs**: Koan AI provider configured, entities ready to store embeddings or AI results, and familiarity with Flow/Data pillars.
    - **Outputs**: Chat endpoints, streaming responses, embedding pipelines, and RAG workflows that productionize AI without bespoke infrastructure.
    - **Error Modes**: Provider rate limits, token exhaustion, missing embeddings on legacy records, or chat history overflows.
    - **Success Criteria**: Deterministic chat responses, embeddings persisted with vector indices, RAG pipelines reuse Flow/Data helpers, and observability covers cost + latency.

    ### Edge Cases

    - **Offline vs cloud providers** – ensure local Ollama fallbacks mirror cloud model interfaces.
    - **Long prompts** – pre-trim or summarize conversation history to avoid truncation.
    - **Embeddings** – keep dimensionality consistent across models; reflow data after model swaps.
    - **Secrets management** – load provider credentials through options or secret stores, not code.

    ---

    ## How to Use This Playbook

    - 📌 Canonical reference: [AI Pillar Reference](../reference/ai/index.md)
    - 🧭 Flow integration: [Flow Pillar Reference](../reference/flow/index.md#semantic-pipelines)
    - 🗂️ Data storage: [Data Pillar Reference](../reference/data/index.md#vector-search--ai-integration)

    Follow the steps below each time you introduce AI functionality.

    ---

    ## 1. Pick a Provider Strategy

    - Start with Ollama locally for quick iteration; mirror settings in production with a hosted provider.
    - Record provider + model IDs in configuration—never hardcode them.
    - Capture rate limits and latency budgets before exposing endpoints.

    🧭 Reference: [Installation & configuration](../reference/ai/index.md#installation--configuration)

    ---

    ## 2. Stand Up a Chat Endpoint

    - Use `IAi.ChatAsync` for synchronous responses, `ChatStreamAsync` for SSE.
    - Introduce system prompts to enforce guardrails and persona behavior.
    - Measure token usage per request; log `AiChatResponse.Usage`.

    🧭 Reference: [Chat completion patterns](../reference/ai/index.md#chat-completion-patterns)

    ---

    ## 3. Add Real-Time Streaming (Optional)

    - Expose SSE endpoints only if the provider supports streaming.
    - Ensure clients gracefully handle partial chunks and reconnection.
    - Include `CancellationToken` to terminate long-running requests.

    🧭 Reference: [Streaming responses](../reference/ai/index.md#chat-completion-patterns)

    ---

    ## 4. Persist Embeddings

    - Annotate vector fields with `[VectorField]` on the entity.
    - Generate embeddings during writes or in background jobs; handle retries for provider hiccups.
    - Validate the returned dimension count before saving.

    🧭 Reference: [Embeddings & vector search](../reference/ai/index.md#embeddings--vector-search)

    ---

    ## 5. Build RAG Workflows

    - Retrieve similar documents via vector search and join them into prompts.
    - Track which sources contributed to each answer.
    - Move heavy retrieval/orchestration into Flow pipelines for retry + observability.

    🧭 Reference: [Retrieval-augmented generation](../reference/ai/index.md#retrieval-augmented-generation-rag)

    ---

    ## 6. Automate with Background Services

    - Subscribe to domain events (e.g., `DocumentUploaded`) and enrich with embeddings or summaries.
    - Use `BackgroundService` helpers like `.On<TEvent>` for succinct event handling.
    - Emit follow-up events when enrichment completes (ex: `DocumentIndexed`).

    🧭 Reference: [Background processing & messaging](../reference/ai/index.md#background-processing--messaging)

    ---

    ## 7. Route Across Multiple Models

    - Define routing logic (cost vs. fidelity) in a single place; decorate requests with `request.Provider`.
    - Collect latency and quality metrics per provider to feed future decisions.

    🧭 Reference: [Multi-model & routing strategies](../reference/ai/index.md#multi-model--routing-strategies)

    ---

    ## 8. Control Cost & Tokens

    - Run `IAi.TokenizeAsync` before sending large prompts.
    - Trim conversation history when crossing a threshold.
    - Persist usage metrics for chargeback or quota tracking.

    🧭 Reference: [Tokenization & cost control](../reference/ai/index.md#tokenization--cost-control)

    ---

    ## 9. Harden Observability

    - Wrap AI calls with retries tuned to provider guidance.
    - Attach structured logs including provider, model, prompt size, latency, and errors.
    - Provide actionable HTTP responses when providers return rate-limit or safety errors.

    🧭 Reference: [Error handling & observability](../reference/ai/index.md#error-handling--observability)

    ---

    ## Review Checklist

    - [ ] Provider configuration stored in environment-specific settings.
    - [ ] Chat endpoints expose both sync and streaming variants (if supported).
    - [ ] Embeddings persist reliably with validation & retries.
    - [ ] RAG flows cite sources and run inside Flow pipelines when complex.
    - [ ] Token usage & cost metrics are logged.
    - [ ] Error responses map provider failures to user-friendly messages.

    ---

    ## Next Steps

    - Integrate AI outputs with Messaging to notify downstream services.
    - Add moderation and safety filters before returning generated content.
    - Explore agentic workflows by orchestrating multiple prompts inside Flow pipelines.