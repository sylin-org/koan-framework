# AI Pillar Reference

**Document Type**: Reference Documentation  
**Target Audience**: Developers, AI Engineers  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## Overview

The Koan AI pillar provides chat, embeddings, vector search, and RAG building blocks with local-first architecture and production-ready patterns.

## Core Features

### Chat & Streaming
- **Streaming SSE**: Server-sent events for real-time chat responses
- **OpenAI-Compatible API**: Standard chat completions interface
- **Local LLMs**: Ollama provider for privacy and control
- **Fallback Support**: OpenAI/Azure when configured

### Embeddings & Vector Search
- **Text Embeddings**: Convert text to vectors for semantic search
- **Multi-Provider**: Redis, Weaviate vector database support
- **Similarity Search**: Find semantically similar content
- **RAG Patterns**: Retrieval-augmented generation with citations

### Budget & Governance
- **Token Limits**: Per-request and per-tenant token budgets
- **Cost Tracking**: Usage monitoring and alerts
- **Rate Limiting**: Request throttling and queuing
- **Observability**: Comprehensive telemetry and logging

## Quick Start

### 1. Installation
```bash
dotnet add package Koan.AI
dotnet add package Koan.Data.Weaviate  # For vector search
```

### 2. Configuration
```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434"
      },
      "Budget": {
        "MaxTokensPerRequest": 1000,
        "MaxTokensPerDay": 10000
      }
    }
  }
}
```

### 3. Chat Usage
```csharp
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var response = await _chatService.ChatAsync(request.Message);
        return Ok(response);
    }

    [HttpPost("chat/stream")]
    public async Task StreamChat([FromBody] ChatRequest request)
    {
        await foreach (var chunk in _chatService.ChatStreamAsync(request.Message))
        {
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
        }
    }
}
```

### 4. Vector Search
```csharp
public class SearchController : ControllerBase
{
    private readonly IVectorService _vectorService;

    [HttpPost("search")]
    public async Task<IActionResult> SemanticSearch([FromBody] SearchRequest request)
    {
        var results = await _vectorService.SearchAsync(request.Query, limit: 10);
        return Ok(results);
    }
}
```

## Providers

### Ollama (Local)
- **Purpose**: Local LLM execution for privacy and control
- **Models**: Llama 2, Code Llama, Mistral, and more
- **Benefits**: No API costs, full data control, offline capability
- **Setup**: Install Ollama locally, configure base URL

### OpenAI/Azure OpenAI
- **Purpose**: Cloud-based AI with advanced models
- **Models**: GPT-4, GPT-3.5, text-embedding-ada-002
- **Benefits**: Latest models, high performance, no local setup
- **Setup**: Configure API keys via secrets provider

## Vector Databases

### Weaviate
- **Purpose**: Production vector database with hybrid search
- **Features**: Vector + keyword search, automatic vectorization
- **Scaling**: Distributed, CRUD operations, real-time updates

### Redis
- **Purpose**: In-memory vector search for fast retrieval
- **Features**: Redis Stack with vector similarity search
- **Use Cases**: Caching, session data, real-time recommendations

## RAG Patterns

### Basic RAG
```csharp
public class RAGService
{
    private readonly IVectorService _vectorService;
    private readonly IChatService _chatService;

    public async Task<RAGResponse> QueryAsync(string question)
    {
        // 1. Search for relevant documents
        var documents = await _vectorService.SearchAsync(question, limit: 5);
        
        // 2. Build context prompt
        var context = string.Join("\n", documents.Select(d => d.Content));
        var prompt = $"Context:\n{context}\n\nQuestion: {question}";
        
        // 3. Generate response with citations
        var response = await _chatService.ChatAsync(prompt);
        
        return new RAGResponse
        {
            Answer = response.Content,
            Sources = documents.Select(d => d.Source).ToArray()
        };
    }
}
```

### Advanced RAG with Citations
- **Source Tracking**: Maintain document sources through the pipeline
- **Citation Formatting**: Include source references in responses
- **Relevance Scoring**: Filter results by similarity threshold
- **Context Windowing**: Manage token limits with smart truncation

## Security & Production

### Authentication
- **API Keys**: Secure AI endpoint access
- **Per-Tenant Keys**: Separate credentials per tenant
- **Rate Limiting**: Prevent abuse and manage costs

### Monitoring
- **Token Usage**: Track consumption per user/tenant
- **Response Times**: Monitor AI service performance
- **Error Rates**: Alert on API failures or quota limits
- **Cost Tracking**: Budget management and alerts

### Data Privacy
- **Local Processing**: Ollama for sensitive data
- **Data Residency**: Control where data is processed
- **Audit Logging**: Track AI interactions for compliance

## API Reference

### Chat Service
```csharp
public interface IChatService
{
    Task<ChatResponse> ChatAsync(string message, CancellationToken ct = default);
    IAsyncEnumerable<ChatChunk> ChatStreamAsync(string message, CancellationToken ct = default);
}
```

### Vector Service
```csharp
public interface IVectorService
{
    Task<VectorResult[]> SearchAsync(string query, int limit = 10, double threshold = 0.7);
    Task<string> EmbedAsync(string text);
    Task IndexAsync(string id, string content, Dictionary<string, object> metadata);
}
```

## Configuration Reference

### AI Options
```csharp
public class KoanAIOptions
{
    public string DefaultProvider { get; set; } = "Ollama";
    public OllamaOptions Ollama { get; set; } = new();
    public OpenAIOptions OpenAI { get; set; } = new();
    public BudgetOptions Budget { get; set; } = new();
}
```

### Budget Options
```csharp
public class BudgetOptions
{
    public int MaxTokensPerRequest { get; set; } = 1000;
    public int MaxTokensPerDay { get; set; } = 10000;
    public bool EnableCostTracking { get; set; } = true;
}
```

## Best Practices

1. **Start Local**: Use Ollama for development and testing
2. **Budget Controls**: Always set token limits and monitoring
3. **Error Handling**: Implement fallbacks for AI service failures  
4. **Caching**: Cache embeddings and frequent queries
5. **Security**: Never log user inputs or AI responses in production
6. **Performance**: Use streaming for long responses
7. **Testing**: Mock AI services for unit tests

## Troubleshooting

### Common Issues
- **Ollama Connection**: Verify service is running on correct port
- **Token Limits**: Check budget configuration and usage
- **Vector Search**: Ensure embeddings are generated and indexed
- **Performance**: Consider caching and async patterns

### Debugging
- Enable detailed logging for AI pillar
- Check health endpoints for service status
- Monitor token usage and rate limits
- Validate vector database connections

---

## Next Steps

- **[AI Guides](../../guides/ai/)** - Detailed how-to guides
- **[Vector Search Guide](../../guides/adapters/vector-search.md)** - Vector database setup
- **[Architecture Decisions](../../architecture/decisions/)** - AI-related ADRs (AI-0001 through AI-0011)
- **[Samples](../../../samples/)** - Working AI examples