---
name: koan-ai-integration
description: Chat endpoints, embeddings, RAG workflows, vector search
---

# Koan AI Integration

## Core Principle

**AI capabilities integrate seamlessly with entity patterns.** Store embeddings on entities, use vector repositories for search, and leverage standard Entity<T> patterns for AI-enriched data.

## Quick Reference

### Chat Endpoints

The AI facade is the **static `Client` class** — no DI injection needed. It resolves the configured
pipeline automatically. For controller use, call `Client.ChatAsync()` directly.

```csharp
public class ChatController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        var response = await Client.ChatAsync(request.Message, ct);
        return Ok(new { message = response.Content, usage = response.Usage });
    }

    // Streaming — use Server-Sent Events or chunked response
    [HttpPost("stream")]
    public async IAsyncEnumerable<string> Stream(
        [FromBody] ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in Client.StreamAsync(request.Message, ct))
            yield return chunk.Content;
    }
}
```

When you need to inject the pipeline (e.g., for testability), inject `IAiPipeline` and call
`PromptAsync` / `StreamAsync` directly:

```csharp
public class SummaryService(IAiPipeline ai)
{
    public Task<AiChatResponse> Summarise(string text, CancellationToken ct) =>
        ai.PromptAsync(new AiChatRequest
        {
            SystemPrompt = "Summarise the following text concisely.",
            Messages = [new AiMessage { Role = "user", Content = text }]
        }, ct);
}
```

> **There is no `IAi` interface and no `ChatAsync` method on any injectable type.**
> Use the static `Client` class for the default pipeline, or inject `IAiPipeline` when you need a testable seam.

### Entity with Embeddings

```csharp
[DataAdapter("weaviate")] // Force vector database
public class ProductSearch : Entity<ProductSearch>
{
    public string ProductId { get; set; } = "";
    public string Description { get; set; } = "";

    [VectorField]
    public float[] DescriptionEmbedding { get; set; } = Array.Empty<float>();

    // Semantic search
    public static async Task<List<ProductSearch>> SimilarTo(
        string query,
        CancellationToken ct = default)
    {
        return await Vector<ProductSearch>.SearchAsync(query, limit: 10, ct);
    }
}
```

### RAG Workflow

```csharp
public class KnowledgeBaseService
{
    public async Task<string> AnswerQuestion(string question, CancellationToken ct)
    {
        // 1. Find relevant documents via vector search
        var relevantDocs = await KnowledgeDocument.SimilarTo(question, ct);

        // 2. Build context from documents
        var context = string.Join("\n\n", relevantDocs.Select(d => d.Content));

        // 3. Query AI with context — use the fluent conversation builder
        var response = await Client.Converse()
            .WithSystem($"Answer based on this context:\n\n{context}")
            .WithUser(question)
            .PromptAsync(ct);

        return response.Content;
    }
}
```

### Configuration

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
          "Type": "Ollama",
          "BaseUrl": "http://localhost:11434",
          "Model": "llama2"
        }
      }
    },
    "Data": {
      "Sources": {
        "Vectors": {
          "Adapter": "weaviate",
          "ConnectionString": "http://localhost:8080"
        }
      }
    }
  }
}
```

## When This Skill Applies

- ✅ Integrating AI features
- ✅ Semantic search
- ✅ Chat interfaces
- ✅ Embeddings generation
- ✅ RAG workflows
- ✅ AI-enriched entities

## Reference Documentation

- **Full Guide:** `docs/guides/ai-integration.md`
- **Vector How-To:** `docs/guides/ai-vector-howto.md`
- **Sample:** `samples/S5.Recs/` (AI recommendation engine)
- **Sample:** `samples/S16.PantryPal/` (Vision AI integration)
