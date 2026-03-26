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

## Category-Driven Routing (AI-0021)

The AI subsystem routes operations by category (Chat, Embed, Ocr) with independent source/model configuration:

```json
{
  "Koan": {
    "Ai": {
      "Chat": { "Source": "ollama-gpu", "Model": "llama3" },
      "Embed": { "Source": "openai-prod", "Model": "text-embedding-3-small" },
      "Ocr": { "Via": "Chat", "Model": "glm-ocr" }
    }
  }
}
```

### Scoped Routing

Override routing per-operation:
```csharp
using (Client.Scope(chat: "fast-local", embed: "cloud-prod"))
{
    var answer = await Client.Chat("Summarize this", ct);
    var vector = await Client.Embed("search text", ct);
}
```

## Entity-Aware AI (EntityAi)

`EntityAi` in `Koan.Data.AI` bridges entities with AI operations using convention inference:

```csharp
// Embed entity content (convention: all string properties)
float[] vector = await EntityAi.Embed(myNote, ct);

// Chat with entity as context (injected as system prompt)
string answer = await EntityAi.Chat("What are the key points?", myNote, ct);

// OCR from entity's byte[] property
string text = await EntityAi.Ocr(myDocument, ct);

// Extract text without AI call
string content = EntityAi.ExtractText(myNote);
```

Convention chain: `[Embedding]` attribute → AllStrings policy → JSON fallback.

## Recipe Bindings (AI-0032)

Named recipes bind capabilities to models. ML engineers author recipes, developers select them:

```json
{
  "Koan": {
    "Ai": {
      "ActiveRecipe": "fast-local",
      "Recipes": {
        "fast-local": { "Chat": "phi3:mini", "Embed": "nomic-embed-text" },
        "cloud-prod": { "Chat": "gpt-4o", "Embed": "text-embedding-3-large" }
      }
    }
  }
}
```

Recipes are sparse — missing keys mean "no opinion" (falls through to advisor/config).

## When This Skill Applies

- ✅ Integrating AI features
- ✅ Semantic search
- ✅ Chat interfaces
- ✅ Embeddings generation
- ✅ RAG workflows
- ✅ AI-enriched entities
- ✅ Per-category AI routing
- ✅ Entity-aware AI operations
- ✅ Recipe-based model selection

## Reference Documentation

- **Full Guide:** `docs/guides/ai-integration.md`
- **Vector How-To:** `docs/guides/ai-vector-howto.md`
- **ADR:** `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md` (Category-driven AI)
- **ADR:** `docs/decisions/AI-0032-intent-capability-resolution-with-recipes.md` (Recipes)
- **Sample:** `samples/S5.Recs/` (AI recommendation engine)
- **Sample:** `samples/S16.PantryPal/` (Vision AI integration)
