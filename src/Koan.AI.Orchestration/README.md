# Koan.AI.Orchestration

Typed, immutable chain composition for Koan: RAG pipelines, branching, parallel execution, structured output, compression, moderation, and streaming — all in a fluent builder.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Orchestration
```

## Quick Start

```csharp
// Simple RAG chain
var result = await Chain.Create()
    .System("Answer questions using only the provided context.")
    .Retrieve<KnowledgeArticle>()    // Semantic retrieval from entity store
    .Rerank()                        // Re-rank retrieved chunks by relevance
    .Compress()                      // Compress context to fit token budget
    .Chat("What is Koan Framework?")
    .Run(ct);

Console.WriteLine(result.Text);
```

## Fluent Builder API

```csharp
Chain.Create()
    .System(string systemPrompt)               // Persona / instructions
    .Chat(string message)                      // Add a chat turn
    .Retrieve<TEntity>()                       // Semantic retrieval step
    .Parse<TOutput>()                          // Structured output extraction
    .Classify(string[] labels)                 // Classification step
    .Branch(condition, thenChain, elseChain)   // Conditional branching
    .Parallel(Chain a, Chain b, ...)           // Parallel execution
    .Rerank()                                  // Re-rank retrieval results
    .Compress()                                // Compress context
    .Moderate()                                // Content moderation gate
    .WithTools(Tool[] tools)                   // Function calling
    .WithMemory()                              // Conversation history
    .Scope(IAiPipeline pipeline)               // Target specific pipeline
    .Run(CancellationToken ct)                 // → ChainResult
    .Stream(CancellationToken ct)              // → IAsyncEnumerable<ChainChunk>
```

## Structured Output

```csharp
public record ProductReview(string Sentiment, int Score, string Summary);

var result = await Chain.Create()
    .System("Extract structured review information.")
    .Chat(rawReviewText)
    .Parse<ProductReview>()
    .Run(ct);

var review = result.As<ProductReview>();
```

## Branching

```csharp
var chain = Chain.Create()
    .Chat(userMessage)
    .Classify(["technical", "billing", "general"])
    .Branch(
        condition: r => r.ClassifiedAs == "technical",
        then: Chain.Create().WithTools(technicalTools),
        @else: Chain.Create().System("Route to billing team")
    );
```

## Streaming

```csharp
await foreach (var chunk in Chain.Create()
    .Retrieve<Article>()
    .Chat(question)
    .Stream(ct))
{
    Console.Write(chunk.Content);
}
```

## Reference

- **ADR**: `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md`
- **Related**: `Koan.AI` (pipeline facade), `Koan.AI.Agents` (agentic patterns), `Koan.AI.Prompt`
