---
type: REF
domain: ai
title: "AI Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/ai/index.md
---

# AI Pillar Reference

## Contract

- **Inputs**: Koan application configured with `services.AddKoan()`, at least one AI provider package (local Ollama, OpenAI, Azure OpenAI, etc.), and entities that may store embeddings or AI-generated data.
- **Outputs**: Reliable chat, embedding, and orchestration workflows that integrate with Data, Flow, and Messaging pillars.
- **Error Modes**: Provider limits (tokens, rate limiting), misaligned embedding dimensions, unsupported streaming in chosen provider, or missing capability flags when routing across multiple services.
- **Success Criteria**: Chat endpoints respond deterministically, embeddings persist with vector providers, RAG flows reuse Flow/Data surfaces, and observability captures token usage & latency.

### Edge Cases

- **Provider selection** – implement fallbacks when the default model is unavailable or rate-limited.
- **Token budgets** – guard `MaxTokens` and conversation history to avoid truncation or 413 responses.
- **Streaming** – check provider support before exposing SSE endpoints.
- **Embedding migrations** – when swapping models, regenerate embeddings to avoid cosine drift.
- **Security** – avoid storing sensitive prompts unencrypted; prefer secrets providers for API keys.

---

## Pillar Overview

Koan.AI unifies chat completion, embeddings, and vector-aware workflows:

- `IAi` facade for chat, streaming, embeddings, and tokenization
- Provider registry with capability discovery
- Integration with Data (`[VectorField]`), Flow pipelines, and Messaging events
- Configuration-first model selection (`DefaultProvider`, per-context overrides)

The AI pillar stays stateless; combine with Flow/Background services for long-running orchestration.

---

## Installation & Configuration

```bash
dotnet add package Koan.AI

// Optional providers
dotnet add package Koan.AI.Connector.Ollama
dotnet add package Koan.Ai.Provider.OpenAi
```

```csharp
// Program.cs
builder.Services.AddKoan();
```

Example configuration:

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2"
      }
    }
  }
}
```

Environment overrides:

```bash
export Koan__AI__DefaultProvider=OpenAI
export Koan__AI__OpenAI__ApiKey="***"
export Koan__AI__OpenAI__DefaultModel="gpt-4o"
```

---

## Chat Completion Patterns

### Minimal Chat Endpoint

```csharp
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IAi _ai;

    public ChatController(IAi ai) => _ai = ai;

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] string message)
    {
        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Messages = [new() { Role = AiMessageRole.User, Content = message }]
        });

        return Ok(response.Choices?.FirstOrDefault()?.Message?.Content);
    }
}
```

### Streaming Responses

```csharp
[HttpPost("stream")]
public async Task StreamChat([FromBody] string message, CancellationToken ct)
{
    Response.Headers.Append("Content-Type", "text/event-stream");

    await foreach (var chunk in _ai.ChatStreamAsync(new AiChatRequest
    {
        Messages = [new() { Role = AiMessageRole.User, Content = message }],
        Stream = true
    }, ct))
    {
        await Response.WriteAsync($"data: {chunk.Content}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

### Multi-Turn Conversations

```csharp
public record ConversationTurn(AiMessageRole Role, string Content);

[HttpPost("conversation")]
public async Task<IActionResult> Conversation([FromBody] ConversationTurn[] history, [FromQuery] string input)
{
    var messages = history.Select(turn => new AiMessage { Role = turn.Role, Content = turn.Content }).ToList();
    messages.Add(new AiMessage { Role = AiMessageRole.User, Content = input });

    var response = await _ai.ChatAsync(new AiChatRequest
    {
        Messages = messages,
        MaxTokens = 500
    });

    return Ok(response.Choices?.FirstOrDefault()?.Message?.Content);
}
```

Guard memory growth by trimming history when token usage exceeds provider limits.

---

## Embeddings & Vector Search

Annotate entity properties and generate embeddings on write.

```csharp
public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];

    public static Task<Document[]> SimilarTo(string query, string? category = null) =>
        Vector<Document>.SearchAsync(query, filter: category is null ? null : d => d.Category == category, limit: 20);
}

public class DocumentIngestor
{
    private readonly IAi _ai;

    public DocumentIngestor(IAi ai) => _ai = ai;

    public async Task<Document> CreateAsync(string title, string content)
    {
        var embedding = await _ai.EmbedAsync(new AiEmbeddingRequest { Input = content });
        var vector = embedding.Embeddings.FirstOrDefault()?.Vector ?? [];

        var document = new Document
        {
            Title = title,
            Content = content,
            ContentEmbedding = vector
        };

        await document.Save();
        return document;
    }
}
```

Vector providers advertise dimensionality via capabilities—validate before persisting historical records.

---

## Retrieval-Augmented Generation (RAG)

```csharp
[Route("api/[controller]")]
public class KnowledgeController : ControllerBase
{
    private readonly IAi _ai;

    public KnowledgeController(IAi ai) => _ai = ai;

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] string question)
    {
        var docs = await Document.SimilarTo(question, limit: 5);
        var context = string.Join("\n\n", docs.Select(d => d.Content));

        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Messages =
            [
                new() { Role = AiMessageRole.System, Content = "Answer strictly from the provided context." },
                new() { Role = AiMessageRole.User, Content = $"Context:\n{context}\n\nQuestion: {question}" }
            ]
        });

        return Ok(new
        {
            Answer = response.Choices?.FirstOrDefault()?.Message?.Content,
            Sources = docs.Select(d => d.Title)
        });
    }
}
```

For production flows, move retrieval and prompt assembly into Flow pipelines or background jobs.

---

## Background Processing & Messaging

```csharp
public class DocumentProcessor : BackgroundService
{
    private readonly IAi _ai;

    public DocumentProcessor(IAi ai) => _ai = ai;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<DocumentUploaded>(async evt =>
        {
            var document = await Document.ById(evt.DocumentId);
            if (document is null) return;

            var embedding = await _ai.EmbedAsync(new AiEmbeddingRequest { Input = document.Content });
            document.ContentEmbedding = embedding.Embeddings.FirstOrDefault()?.Vector ?? [];
            await document.Save();

            await new DocumentIndexed { DocumentId = document.Id }.Send();
        }, ct);
    }
}
```

Combine with Flow pipelines for large datasets and retry semantics.

---

## Multi-Model & Routing Strategies

Use policy-based routing when multiple providers are available.

```csharp
public class SmartAi : IAi
{
    private readonly IAi _ai;

    public SmartAi(IAi ai) => _ai = ai;

    public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var provider = request.Metadata.TryGetValue("provider", out var value)
            ? value?.ToString()
            : ChooseProvider(request);

        request.Provider = provider;
        return _ai.ChatAsync(request, ct);
    }

    // Other IAi members proxy similarly...
}
```

Track latency, token cost, and success rate per provider to inform routing decisions.

---

## Tokenization & Cost Control

- Use `IAi.TokenizeAsync` to estimate prompt size before sending expensive requests.
- Truncate history or chunk documents based on token counts.
- Record token usage from `AiChatResponse.Usage` for observability dashboards.

---

## Error Handling & Observability

- Wrap AI calls in retry policies mindful of provider rate limits.
- Emit structured logs containing provider, model, latency, and token usage.
- Surface user-friendly error messages; keep raw provider responses for diagnostics only.

```csharp
try
{
    var result = await _ai.ChatAsync(request, ct);
    return Ok(result);
}
catch (AiProviderException ex) when (ex.IsRateLimited)
{
    return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate_limited" });
}
```

---

## Related Reading

- [Flow Pillar Reference](../flow/index.md) – semantic pipelines, enrichment, batching
- [Data Pillar Reference](../data/index.md) – storing embeddings & entity patterns
- [AI Provider ADRs](../../decisions/AI-0001-ai-baseline.md) for policy and governance context

```csharp
public class AdvancedRAGService
{
    private readonly IAi _ai;

    public async Task<RAGResponse> QueryWithCitations(string question)
    {
        // Search with metadata
        var results = await Document.Vector()
            .SearchAsync(question, limit: 5, threshold: 0.7);

        var contextParts = results.Select((doc, index) =>
            $"[{index + 1}] {doc.Content}").ToArray();
        var context = string.Join("\n\n", contextParts);

        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Messages = [
                new() { Role = AiMessageRole.System, Content =
                    "Answer the question using the provided context. " +
                    "Include citation numbers [1], [2], etc. when referencing sources." },
                new() { Role = AiMessageRole.System, Content = context },
                new() { Role = AiMessageRole.User, Content = question }
            ]
        });

        return new RAGResponse
        {
            Answer = response.Choices?.FirstOrDefault()?.Message?.Content ?? "",
            Sources = results.Select((doc, index) => new CitationSource
            {
                Index = index + 1,
                Title = doc.Title,
                Url = $"/documents/{doc.Id}"
            }).ToArray()
        };
    }
}
```

## Provider Configuration

### Local Development (Ollama)

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2"
      }
    }
  }
}
```

### Production (OpenAI)

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "OpenAI",
      "OpenAI": {
        "ApiKey": "{OPENAI_API_KEY}",
        "DefaultModel": "gpt-4"
      }
    }
  }
}
```

### Multi-Provider with Fallback

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
      },
      "FallbackStrategy": "Cascade"
    }
  }
}
```

## Budget Management

### Token Limits

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

### Budget Enforcement

```csharp
[HttpPost("protected-chat")]
public async Task<IActionResult> ProtectedChat([FromBody] string message)
{
    try
    {
        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Messages = [new() { Role = AiMessageRole.User, Content = message }],
            MaxTokens = 500
        });

        return Ok(response.Choices?.FirstOrDefault()?.Message?.Content);
    }
    catch (AiBudgetExceededException)
    {
        return StatusCode(429, "Daily budget exceeded");
    }
    catch (AiModelUnavailableException)
    {
        return StatusCode(503, "AI service temporarily unavailable");
    }
}
```

## Background Processing

### Document Processing Pipeline

```csharp
public class DocumentProcessor : BackgroundService
{
    private readonly IAi _ai;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<DocumentUploaded>(async evt =>
        {
            var document = await Document.ById(evt.DocumentId);
            if (document == null) return;

            // Generate embedding
            var embedding = await _ai.EmbedAsync(new AiEmbeddingRequest
            {
                Input = document.Content
            });

            document.ContentEmbedding = embedding.Embeddings.FirstOrDefault()?.Vector ?? [];
            await document.Save();
        });
    }
}
```

### Batch Processing

```csharp
public class BatchEmbeddingService
{
    private readonly IAi _ai;

    public async Task ProcessDocumentBatch(Document[] documents)
    {
        var batchRequest = new AiEmbeddingRequest
        {
            Input = documents.Select(d => d.Content).ToArray()
        };

        var response = await _ai.EmbedAsync(batchRequest);

        for (int i = 0; i < documents.Length; i++)
        {
            if (i < response.Embeddings.Length)
            {
                documents[i].ContentEmbedding = response.Embeddings[i].Vector;
            }
        }

        await Document.SaveBatch(documents);
    }
}
```

## Content Moderation

### Safety Checks

```csharp
public class ModerationService
{
    private readonly IAi _ai;

    public async Task<bool> IsContentSafe(string content)
    {
        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Model = "content-moderator",
            Messages = [
                new() { Role = AiMessageRole.System, Content = "Rate this content as 'safe' or 'unsafe'" },
                new() { Role = AiMessageRole.User, Content = content }
            ]
        });

        return response.Choices?.FirstOrDefault()?.Message?.Content?.Contains("safe") == true;
    }
}
```

### Moderated Chat

```csharp
[HttpPost("safe-chat")]
public async Task<IActionResult> SafeChat([FromBody] string message)
{
    if (!await _moderation.IsContentSafe(message))
        return BadRequest("Content violates community guidelines");

    var response = await _ai.ChatAsync(new AiChatRequest
    {
        Messages = [new() { Role = AiMessageRole.User, Content = message }]
    });

    var answer = response.Choices?.FirstOrDefault()?.Message?.Content ?? "";

    if (!await _moderation.IsContentSafe(answer))
        return BadRequest("Generated content requires review");

    return Ok(answer);
}
```

## Error Handling

### Retry and Fallback

```csharp
public class RobustChatService
{
    private readonly IAi _ai;

    public async Task<string> ChatWithRetry(string message, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await _ai.ChatAsync(new AiChatRequest
                {
                    Messages = [new() { Role = AiMessageRole.User, Content = message }]
                });

                return response.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            }
            catch (AiModelUnavailableException) when (attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
            }
        }

        throw new InvalidOperationException("AI service unavailable after retries");
    }
}
```

## Testing

### Mock AI Provider

```csharp
[Test]
public async Task Should_Generate_Response()
{
    // Arrange
    var mockAi = new MockAi();
    var controller = new ChatController(mockAi);

    // Act
    var result = await controller.Chat("Hello");

    // Assert
    var response = result as OkObjectResult;
    Assert.IsNotNull(response);
    Assert.IsNotEmpty(response.Value?.ToString());
}

public class MockAi : IAi
{
    public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new AiChatResponse
        {
            Choices = [new() { Message = new() { Content = "Mock response" } }]
        });
    }

    public Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new AiEmbeddingResponse
        {
            Embeddings = [new() { Vector = new float[1536] }] // Standard OpenAI embedding size
        });
    }
}
```

## API Reference

### Core Interfaces

```csharp
public interface IAi
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatStreamChunk> ChatStreamAsync(AiChatRequest request, CancellationToken ct = default);
}

public interface IVector<T> where T : IEntity
{
    Task<T[]> SearchAsync(string query, int limit = 10, double threshold = 0.7);
    Task<T[]> SearchAsync(float[] embedding, int limit = 10, double threshold = 0.7);
    Task IndexAsync(T entity, CancellationToken ct = default);
}
```

### Request/Response Models

```csharp
public class AiChatRequest
{
    public AiMessage[] Messages { get; set; } = [];
    public string? Model { get; set; }
    public int? MaxTokens { get; set; }
    public bool Stream { get; set; } = false;
}

public class AiChatResponse
{
    public AiChoice[] Choices { get; set; } = [];
    public AiUsage? Usage { get; set; }
}

public class AiEmbeddingRequest
{
    public string[] Input { get; set; } = [];
    public string? Model { get; set; }
}

public class AiEmbeddingResponse
{
    public AiEmbedding[] Embeddings { get; set; } = [];
    public AiUsage? Usage { get; set; }
}
```

## Agentic Code Generation

Looking for end-to-end guidance on agent-facing code generation workflows? See the
[Agentic AI Code Generation Reference](agentic-code-generation.md) for structured prompting patterns, MCP tool wiring, and
validation guardrails that align with the new S12.MedTrials sample.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+

