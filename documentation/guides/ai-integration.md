---
type: GUIDE
domain: ai
title: "AI Integration with Koan"
audience: [developers, ai-engineers]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# AI Integration with Koan

**Document Type**: GUIDE
**Target Audience**: Developers, AI Engineers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## 3-Line Chat Integration

```bash
dotnet add package Koan.AI
```

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

That's it. You have working AI chat.

## Local Development Setup

```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a model
ollama pull llama2
```

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

No API keys. No cloud dependencies. Pure local development.

## Streaming Responses

```csharp
[HttpPost("stream")]
public async Task StreamChat([FromBody] string message)
{
    Response.Headers.Add("Content-Type", "text/event-stream");

    await foreach (var chunk in _ai.ChatStreamAsync(new AiChatRequest
    {
        Messages = [new() { Role = AiMessageRole.User, Content = message }],
        Stream = true
    }))
    {
        await Response.WriteAsync($"data: {chunk.Content}\n\n");
        await Response.Body.FlushAsync();
    }
}
```

Real-time streaming with server-sent events.

## Vector Search

```csharp
public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];

    // Semantic search
    public static Task<Document[]> SimilarTo(string query) =>
        Vector<Document>.SearchAsync(query);
}

// Usage
var documents = await Document.SimilarTo("machine learning concepts");
```

Automatic vectorization and semantic search.

## RAG (Retrieval-Augmented Generation)

```csharp
[Route("api/[controller]")]
public class KnowledgeController : ControllerBase
{
    private readonly IAi _ai;

    public KnowledgeController(IAi ai) => _ai = ai;

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] string question)
    {
        // Find relevant documents
        var docs = await Document.SimilarTo(question);
        var context = string.Join("\n\n", docs.Select(d => d.Content));

        // Generate answer with context
        var response = await _ai.ChatAsync(new AiChatRequest
        {
            Messages = [
                new() { Role = AiMessageRole.System, Content = $"Answer based on this context: {context}" },
                new() { Role = AiMessageRole.User, Content = question }
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

Knowledge base with citations.

## Document Processing

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

// Trigger processing
await new DocumentUploaded { DocumentId = doc.Id }.Send();
```

Automatic document vectorization pipeline.

## Multi-Model Setup

```csharp
public class SmartChatController : ControllerBase
{
    private readonly IAi _ai;

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeDocument([FromBody] AnalyzeRequest request)
    {
        // Use different models for different tasks
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
      },
      "FallbackStrategy": "Cascade",
      "Timeout": "00:00:30"
    }
  }
}
```

High availability with fallbacks.

## Error Handling

```csharp
[HttpPost("safe-chat")]
public async Task<IActionResult> SafeChat([FromBody] string message)
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
    catch (Exception ex)
    {
        return StatusCode(500, "AI processing failed");
    }
}
```

Graceful degradation.

## Content Moderation

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

[HttpPost("moderate")]
public async Task<IActionResult> PostWithModeration([FromBody] PostRequest request)
{
    if (!await _moderation.IsContentSafe(request.Content))
        return BadRequest("Content violates community guidelines");

    // Process safe content...
    return Ok();
}
```

Built-in safety checks.

## Testing

```csharp
[Test]
public async Task Should_Generate_Response()
{
    // Arrange
    var ai = new MockAi();
    var controller = new ChatController(ai);

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
}
```

Testable AI integration.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+