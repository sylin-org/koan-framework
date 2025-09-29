# Semantic Streaming Pipelines

**Transform complex AI and data workflows into readable, maintainable code with Koan's semantic streaming pipeline system.**

## Overview

Semantic streaming pipelines enable you to process large datasets with AI enrichment, multi-provider storage, and sophisticated error handling through natural, readable .NET patterns. What would traditionally require multiple services, complex orchestration, and error-prone coordination becomes a single, fluent pipeline expression.

## Key Features

- **Clean Semantic API**: `.Save()` instead of polluted generic types
- **Cross-Pillar Integration**: AI, Data, Messaging, and Observability work together seamlessly
- **Natural Async Patterns**: Task-based interface for intuitive async lambda expressions
- **Stream Processing**: Handle millions of items without memory issues
- **Branching Logic**: Success/failure paths with clean error handling
- **Framework Consistency**: One persistence verb across all storage types

## Basic Pattern

```csharp
await Entity.AllStream()
    .Pipeline()
    .ForEach(entity => {
        // Mutation logic
    })
    .Save()                    // Clean, semantic persistence
    .ExecuteAsync();
```

## Cross-Pillar Extensions

### AI Integration

```csharp
await Document.AllStream()
    .Pipeline()
    .Tokenize(doc => $"{doc.Title} {doc.Content}")
    .Embed(new AiEmbedOptions { Model = "all-minilm" })
    .Save()                    // Stores both document and embeddings
    .ExecuteAsync();
```

### Messaging Integration

```csharp
await Todo.AllStream()
    .Pipeline()
    .ForEach(todo => todo.Status = "processed")
    .Save()
    .Notify(todo => new TodoProcessed { TodoId = todo.Id })
    .ExecuteAsync();
```

### Observability Integration

```csharp
await Product.AllStream()
    .Pipeline()
    .Trace(env => $"Processing product {env.Entity.Id}")
    .ForEach(product => /* processing */)
    .Save()
    .Trace(env => $"Completed product {env.Entity.Id}")
    .ExecuteAsync();
```

## Advanced Branching

```csharp
await Media.AllStream()
    .Pipeline()
    .Tokenize(m => $"{m.Title} {m.Description}")
    .Embed(new AiEmbedOptions { Model = "all-minilm" })
    .Branch(branch => branch
        .OnSuccess(success => success
            .Save()
            .Notify(m => $"Media '{m.Title}' processed successfully"))
        .OnFailure(failure => failure
            .Trace(env => $"Failed: {env.Error?.Message}")
            .Notify(m => $"Processing failed for '{m.Title}'")))
    .ExecuteAsync();
```

## Real-World Examples

### Document Processing Pipeline

```csharp
// Process 100,000 documents with AI enrichment
await Document.AllStream()
    .Pipeline()
    .ForEach(doc => {
        doc.ProcessedAt = DateTime.UtcNow;
        doc.Status = "processing";
    })
    .Tokenize(doc => $"{doc.Title} {doc.Content}")
    .Embed(new AiEmbedOptions { Model = "all-minilm", Batch = 50 })
    .Branch(branch => branch
        .OnSuccess(success => success
            .Mutate(env => {
                env.Entity.Status = "completed";
                env.Features["vector:metadata"] = new {
                    title = env.Entity.Title,
                    category = env.Entity.Category
                };
            })
            .Save()
            .Notify(doc => new DocumentProcessed { DocumentId = doc.Id }))
        .OnFailure(failure => failure
            .Mutate(env => env.Entity.Status = "failed")
            .Save()
            .Notify(doc => new DocumentFailed {
                DocumentId = doc.Id,
                Error = env.Error?.Message
            })))
    .ExecuteAsync();
```

### Content Recommendation Pipeline

```csharp
// Update content recommendations based on user interaction
await UserInteraction.Where(i => i.CreatedAt > DateTime.UtcNow.AddHours(-1))
    .Pipeline()
    .Embed(interaction => $"{interaction.ContentTitle} {interaction.UserContext}")
    .ForEach(async interaction => {
        var similar = await Content.SemanticSearch(
            $"{interaction.ContentTitle} {interaction.Category}",
            limit: 10);
        interaction.RecommendedContent = similar.Select(c => c.Id).ToList();
    })
    .Save()
    .Notify(interaction => new RecommendationsUpdated {
        UserId = interaction.UserId,
        ContentIds = interaction.RecommendedContent
    })
    .ExecuteAsync();
```

## Performance Considerations

### Streaming vs Materialization

```csharp
// ❌ Materializes entire dataset in memory
var all = await Document.All();
foreach (var doc in all) { /* process */ }

// ✅ Streams with controlled memory usage
await Document.AllStream()
    .Pipeline()
    .ForEach(doc => /* process */)
    .Save()
    .ExecuteAsync();
```

### Batching

```csharp
// AI operations automatically batch for efficiency
await Document.AllStream()
    .Pipeline()
    .Embed(new AiEmbedOptions {
        Model = "all-minilm",
        Batch = 100  // Process 100 items per AI call
    })
    .Save()
    .ExecuteAsync();
```

## Error Handling

### Pipeline-Level Errors

```csharp
try
{
    await Document.AllStream()
        .Pipeline()
        .ForEach(doc => /* might throw */)
        .Save()
        .ExecuteAsync();
}
catch (Exception ex)
{
    // Handle pipeline execution failures
}
```

### Item-Level Errors with Branching

```csharp
await Document.AllStream()
    .Pipeline()
    .Branch(branch => branch
        .OnSuccess(success => success
            .ForEach(doc => /* processing */)
            .Save())
        .OnFailure(failure => failure
            .Trace(env => $"Item failed: {env.Error?.Message}")
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

## Multi-Provider Transparency

The same pipeline code works across different storage providers:

```csharp
// Configuration determines actual providers
await Product.AllStream()
    .Pipeline()
    .Embed(p => p.Description)
    .Save()  // → PostgreSQL for data + Weaviate for vectors
    .ExecuteAsync();

// Same code, different config
await Product.AllStream()
    .Pipeline()
    .Embed(p => p.Description)
    .Save()  // → MongoDB for data + Redis for vectors
    .ExecuteAsync();
```

## Extension Points

### Custom Pipeline Extensions

```csharp
public static class CustomPipelineExtensions
{
    public static PipelineBuilder<TEntity> ValidateBusinessRules<TEntity>(
        this PipelineBuilder<TEntity> builder)
        where TEntity : class, IEntity<string>
    {
        return builder.AddStage(async (envelope, ct) => {
            if (envelope.IsFaulted) return;

            // Custom validation logic
            if (!IsValid(envelope.Entity))
            {
                envelope.RecordError(new ValidationException("Business rule failed"));
            }
        });
    }
}

// Usage
await Product.AllStream()
    .Pipeline()
    .ValidateBusinessRules()
    .Save()
    .ExecuteAsync();
```

## Best Practices

### 1. Use Streaming for Large Datasets

```csharp
// ✅ Good - streams data
await Document.AllStream().Pipeline()

// ❌ Avoid - loads everything in memory
await Document.All().Pipeline()
```

### 2. Leverage Branching for Error Handling

```csharp
// ✅ Good - handles success/failure paths
.Branch(branch => branch
    .OnSuccess(success => /* happy path */)
    .OnFailure(failure => /* error handling */))
```

### 3. Use Clean Semantic APIs

```csharp
// ✅ Good - clean, semantic
.Save()

// ❌ Avoid - type pollution
.Save<Entity, PipelineBuilder<Entity>>()
```

### 4. Add Observability

```csharp
// ✅ Good - includes tracing
await Document.AllStream()
    .Pipeline()
    .Trace(env => $"Processing {env.Entity.Id}")
    .ForEach(/* processing */)
    .Save()
    .ExecuteAsync();
```

## Framework Integration

Semantic streaming pipelines integrate seamlessly with:

- **Entity<> patterns**: Natural extension of your existing data models
- **Multi-provider storage**: Same pipeline, different backends
- **AI services**: Tokenization, embedding, and chat integration
- **Messaging systems**: Event publication and notification
- **Observability**: Tracing, logging, and monitoring
- **Error handling**: Structured error capture and branching logic

## Migration from Manual Patterns

### Before: Complex, Error-Prone

```csharp
var documents = await Document.All();
var failed = new List<Document>();

foreach (var doc in documents)
{
    try
    {
        doc.ProcessedAt = DateTime.UtcNow;

        var embedding = await aiService.EmbedAsync(doc.Content);
        await vectorDb.StoreAsync(doc.Id, embedding);

        await doc.Save();
        await messagingService.SendAsync(new DocumentProcessed { DocumentId = doc.Id });
    }
    catch (Exception ex)
    {
        doc.Status = "failed";
        failed.Add(doc);
        logger.LogError(ex, "Failed to process document {DocumentId}", doc.Id);
    }
}

// Handle failed documents separately...
```

### After: Clean, Semantic

```csharp
await Document.AllStream()
    .Pipeline()
    .ForEach(doc => {
        doc.ProcessedAt = DateTime.UtcNow;
        doc.Status = "processing";
    })
    .Embed(doc => doc.Content)
    .Branch(branch => branch
        .OnSuccess(success => success
            .Save()
            .Notify(doc => new DocumentProcessed { DocumentId = doc.Id }))
        .OnFailure(failure => failure
            .Trace(env => $"Failed: {env.Error?.Message}")
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

## Next Steps

- **[AI Integration Guide](../ai/index.md)** - Deep dive into AI pipeline extensions
- **[Data Provider Guide](../data/index.md)** - Multi-provider storage patterns
- **[Messaging Integration](../messaging/index.md)** - Event-driven pipeline patterns
- **[Performance Optimization](performance-optimization.md)** - Scaling pipeline workloads

---

*Semantic streaming pipelines represent a fundamental shift toward readable, maintainable code for complex data workflows. They embody Koan's principle of making sophisticated solutions accessible through simple, natural patterns.*