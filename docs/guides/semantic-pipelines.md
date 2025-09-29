# Semantic Pipelines Guide

**Build sophisticated data processing workflows with AI integration through clean, readable patterns.**

## What Are Semantic Pipelines?

Semantic pipelines transform complex data processing workflows into natural, readable .NET code. Instead of writing error-prone orchestration logic, you express your intent through fluent, semantic operations that the framework executes efficiently.

### Before Pipelines: Complex and Fragile

```csharp
// Traditional approach - complex, error-prone
var documents = await Document.All();
var successCount = 0;
var failedCount = 0;

foreach (var doc in documents)
{
    try
    {
        // Manual orchestration
        doc.ProcessedAt = DateTime.UtcNow;
        var tokens = await tokenizer.TokenizeAsync(doc.Content);
        var embedding = await aiService.EmbedAsync(tokens);

        // Separate storage calls
        await doc.Save();
        await vectorDb.StoreAsync(doc.Id, embedding);
        await messageQueue.SendAsync(new DocumentProcessed { Id = doc.Id });

        successCount++;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process {DocId}", doc.Id);
        doc.Status = "failed";
        await doc.Save();
        failedCount++;
    }
}

logger.LogInformation("Processed {Success} documents, {Failed} failed", successCount, failedCount);
```

### After Pipelines: Clean and Semantic

```csharp
// Pipeline approach - clean, semantic, robust
await Document.AllStream()
    .Pipeline()
    .ForEach(doc => {
        doc.ProcessedAt = DateTime.UtcNow;
        doc.Status = "processing";
    })
    .Tokenize(doc => doc.Content)
    .Embed(new AiEmbedOptions { Model = "all-minilm" })
    .Branch(branch => branch
        .OnSuccess(success => success
            .Save()  // Stores both document and vector automatically
            .Notify(doc => new DocumentProcessed { Id = doc.Id }))
        .OnFailure(failure => failure
            .Trace(env => $"Failed: {env.Error?.Message}")
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

## Getting Started

### Step 1: Basic Entity Processing

```csharp
// Start with simple entity mutations
await Todo.AllStream()
    .Pipeline()
    .ForEach(todo => {
        todo.UpdatedAt = DateTime.UtcNow;
        todo.Status = "reviewed";
    })
    .Save()
    .ExecuteAsync();
```

### Step 2: Add AI Integration

```csharp
// Add AI tokenization and embedding
await Product.AllStream()
    .Pipeline()
    .Tokenize(product => $"{product.Name} {product.Description}")
    .Embed(new AiEmbedOptions { Model = "all-minilm" })
    .Save()  // Automatically stores both product data and embeddings
    .ExecuteAsync();
```

### Step 3: Add Error Handling

```csharp
// Add branching for success/failure paths
await Document.AllStream()
    .Pipeline()
    .Tokenize(doc => doc.Content)
    .Branch(branch => branch
        .OnSuccess(success => success
            .ForEach(doc => doc.Status = "completed")
            .Save())
        .OnFailure(failure => failure
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

## Core Pipeline Operations

### ForEach - Entity Mutation

```csharp
.ForEach(entity => {
    entity.ProcessedAt = DateTime.UtcNow;
    entity.Version += 1;
})

// Or async operations
.ForEach(async entity => {
    entity.ExternalData = await externalService.GetDataAsync(entity.Id);
})
```

### Save - Unified Persistence

```csharp
.Save()  // Clean, semantic - no type pollution

// Works across all providers:
// - Entities → PostgreSQL/MongoDB/etc
// - Vectors → Weaviate/Redis/etc
// - Both stored atomically
```

### Tokenize - AI Text Processing

```csharp
.Tokenize(entity => $"{entity.Title} {entity.Content}")
.Tokenize(entity => entity.Description, new AiTokenizeOptions {
    MaxTokens = 1000,
    Model = "gpt-3.5-turbo"
})
```

### Embed - Vector Generation

```csharp
.Embed(new AiEmbedOptions {
    Model = "all-minilm",
    Batch = 50  // Process 50 items per API call
})
```

### Notify - Messaging Integration

```csharp
.Notify(entity => new EntityProcessed { Id = entity.Id })
.Notify(entity => $"Processed: {entity.Title}")  // Simple string messages
```

### Trace - Observability

```csharp
.Trace(env => $"Processing {env.Entity.Id}")
.Trace(env => $"Completed {env.Entity.Id} in {env.Duration}")
```

### Branch - Success/Failure Paths

```csharp
.Branch(branch => branch
    .OnSuccess(success => success
        .ForEach(entity => entity.Status = "completed")
        .Save()
        .Notify(entity => new ProcessingCompleted { Id = entity.Id }))
    .OnFailure(failure => failure
        .ForEach(entity => entity.Status = "failed")
        .Save()
        .Notify(entity => new ProcessingFailed {
            Id = entity.Id,
            Error = env.Error?.Message
        })))
```

## Real-World Examples

### Content Recommendation System

```csharp
// Update user recommendations based on recent interactions
await UserInteraction.Where(i => i.CreatedAt > DateTime.UtcNow.AddHours(-24))
    .Pipeline()
    .ForEach(interaction => interaction.Status = "analyzing")
    .Embed(interaction => $"user:{interaction.UserId} item:{interaction.ItemId} rating:{interaction.Rating}")
    .ForEach(async interaction => {
        // Find similar items using the generated embedding
        var similar = await Item.SemanticSearch(
            $"category:{interaction.Category} rating:>=4",
            limit: 10,
            embedding: interaction.GetEmbedding());

        interaction.RecommendedItems = similar.Select(s => s.Id).ToList();
        interaction.Status = "completed";
    })
    .Save()
    .Notify(interaction => new RecommendationsUpdated {
        UserId = interaction.UserId,
        ItemIds = interaction.RecommendedItems
    })
    .ExecuteAsync();
```

### Document Processing with Validation

```csharp
// Process uploaded documents with content validation
await Document.Where(d => d.Status == "uploaded")
    .Pipeline()
    .ForEach(doc => {
        doc.ProcessedAt = DateTime.UtcNow;
        doc.Status = "processing";
    })
    .Branch(branch => branch
        .When(doc => doc.ContentLength > 50,
              validDocs => validDocs
                  .Tokenize(doc => doc.Content)
                  .Embed(new AiEmbedOptions { Model = "all-minilm" })
                  .ForEach(doc => doc.Status = "completed")
                  .Save()
                  .Notify(doc => new DocumentReady { Id = doc.Id }))
        .When(doc => doc.ContentLength <= 50,
              invalidDocs => invalidDocs
                  .ForEach(doc => {
                      doc.Status = "rejected";
                      doc.RejectionReason = "Content too short";
                  })
                  .Save()
                  .Notify(doc => new DocumentRejected {
                      Id = doc.Id,
                      Reason = doc.RejectionReason
                  })))
    .ExecuteAsync();
```

### E-commerce Product Enrichment

```csharp
// Enrich product data with AI-generated descriptions and categories
await Product.Where(p => p.EnrichmentStatus == "pending")
    .Pipeline()
    .ForEach(product => product.EnrichmentStatus = "processing")
    .Branch(branch => branch
        .OnSuccess(success => success
            .ForEach(async product => {
                // Generate enhanced description using AI
                var prompt = $"Create a compelling product description for: {product.Name}. Specifications: {product.Specs}";
                var enhancedDescription = await Ai.Chat(prompt);
                product.EnhancedDescription = enhancedDescription.Content;

                // Auto-categorize
                var categoryPrompt = $"Categorize this product: {product.Name} {product.Description}";
                var category = await Ai.Chat(categoryPrompt);
                product.AiSuggestedCategory = category.Content;

                product.EnrichmentStatus = "completed";
            })
            .Tokenize(product => $"{product.Name} {product.EnhancedDescription}")
            .Embed(new AiEmbedOptions { Model = "all-minilm" })
            .Save()  // Stores product data + embeddings for semantic search
            .Notify(product => new ProductEnriched {
                ProductId = product.Id,
                HasEnhancedDescription = !string.IsNullOrEmpty(product.EnhancedDescription),
                SuggestedCategory = product.AiSuggestedCategory
            }))
        .OnFailure(failure => failure
            .Trace(env => $"Failed to enrich product {env.Entity.Id}: {env.Error?.Message}")
            .ForEach(product => {
                product.EnrichmentStatus = "failed";
                product.FailureReason = env.Error?.Message;
            })
            .Save()))
    .ExecuteAsync();
```

## Performance Best Practices

### 1. Use Streaming for Large Datasets

```csharp
// ✅ Good - streams data in batches
await Document.AllStream()
    .Pipeline()
    .ForEach(doc => /* process */)
    .Save()
    .ExecuteAsync();

// ❌ Avoid - loads entire dataset in memory
var allDocs = await Document.All();
await allDocs.Pipeline()...
```

### 2. Configure AI Batching

```csharp
// ✅ Good - batch AI calls for efficiency
.Embed(new AiEmbedOptions {
    Model = "all-minilm",
    Batch = 100  // Process 100 items per API call
})

// ❌ Inefficient - one call per item
.Embed(new AiEmbedOptions { Batch = 1 })
```

### 3. Use Appropriate Models

```csharp
// ✅ Good - fast model for large datasets
.Embed(new AiEmbedOptions { Model = "all-minilm" })  // Fast, good quality

// ❌ Slow - expensive model for bulk processing
.Embed(new AiEmbedOptions { Model = "text-embedding-ada-002" })  // High quality, but slower/more expensive
```

### 4. Leverage Multi-Provider Efficiency

```csharp
// The framework automatically optimizes storage:
.Save()  // → Bulk operations for entities + vectors
         //   PostgreSQL bulk insert for entities
         //   Weaviate batch insert for vectors
```

## Error Handling Patterns

### Simple Error Logging

```csharp
await Document.AllStream()
    .Pipeline()
    .Trace(env => $"Processing {env.Entity.Id}")
    .ForEach(doc => /* might fail */)
    .Branch(branch => branch
        .OnFailure(failure => failure
            .Trace(env => $"Error: {env.Error?.Message}")
            .ForEach(doc => doc.Status = "failed")))
    .Save()
    .ExecuteAsync();
```

### Detailed Error Capture

```csharp
await Document.AllStream()
    .Pipeline()
    .Branch(branch => branch
        .OnSuccess(success => success
            .ForEach(doc => /* processing */)
            .Save())
        .OnFailure(failure => failure
            .ForEach(doc => {
                doc.Status = "failed";
                doc.ErrorMessage = env.Error?.Message;
                doc.ErrorDetails = env.Error?.ToString();
                doc.FailedAt = DateTime.UtcNow;
            })
            .Save()
            .Notify(doc => new ProcessingFailed {
                DocumentId = doc.Id,
                Error = doc.ErrorMessage,
                Timestamp = doc.FailedAt.Value
            })))
    .ExecuteAsync();
```

### Retry Logic

```csharp
await Document.Where(d => d.Status == "failed" && d.RetryCount < 3)
    .Pipeline()
    .ForEach(doc => {
        doc.RetryCount++;
        doc.Status = "retrying";
        doc.LastRetryAt = DateTime.UtcNow;
    })
    .Branch(branch => branch
        .OnSuccess(success => success
            .ForEach(doc => doc.Status = "completed")
            .Save())
        .OnFailure(failure => failure
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

## Testing Pipeline Code

### Unit Testing Individual Stages

```csharp
[Test]
public async Task ForEach_ShouldUpdateProcessedAt()
{
    // Arrange
    var todos = new[] {
        new Todo { Title = "Test", ProcessedAt = null }
    };

    // Act
    await todos.AsAsyncEnumerable()
        .Pipeline()
        .ForEach(todo => todo.ProcessedAt = DateTime.UtcNow)
        .ExecuteAsync();

    // Assert
    Assert.That(todos[0].ProcessedAt, Is.Not.Null);
}
```

### Integration Testing Full Pipelines

```csharp
[Test]
public async Task DocumentProcessing_ShouldEmbedAndStore()
{
    // Arrange
    var testDoc = new Document {
        Id = Guid.NewGuid().ToString(),
        Content = "Test document content",
        Status = "pending"
    };
    await testDoc.Save();

    // Act
    await Document.Where(d => d.Id == testDoc.Id)
        .Pipeline()
        .Tokenize(doc => doc.Content)
        .Embed(new AiEmbedOptions { Model = "all-minilm" })
        .ForEach(doc => doc.Status = "completed")
        .Save()
        .ExecuteAsync();

    // Assert
    var processed = await Document.Get(testDoc.Id);
    Assert.That(processed.Status, Is.EqualTo("completed"));

    // Verify vector was stored
    var vectors = await Vector<Document>.Query(testDoc.Id);
    Assert.That(vectors, Is.Not.Empty);
}
```

## Advanced Patterns

### Custom Pipeline Extensions

```csharp
public static class CustomPipelineExtensions
{
    public static PipelineBuilder<TEntity> ValidateBusinessRules<TEntity>(
        this PipelineBuilder<TEntity> builder,
        Func<TEntity, ValidationResult> validator)
        where TEntity : class, IEntity<string>
    {
        return builder.AddStage(async (envelope, ct) => {
            if (envelope.IsFaulted) return;

            var result = validator(envelope.Entity);
            if (!result.IsValid)
            {
                envelope.RecordError(new ValidationException(result.ErrorMessage));
            }
        });
    }
}

// Usage
await Product.AllStream()
    .Pipeline()
    .ValidateBusinessRules(product => new ValidationResult
    {
        IsValid = product.Price > 0,
        ErrorMessage = "Price must be positive"
    })
    .Save()
    .ExecuteAsync();
```

### Conditional Processing

```csharp
await Document.AllStream()
    .Pipeline()
    .Branch(branch => branch
        .When(doc => doc.ContentType == "text/plain",
              textDocs => textDocs
                  .Tokenize(doc => doc.Content)
                  .Embed(new AiEmbedOptions { Model = "text-model" }))
        .When(doc => doc.ContentType.StartsWith("image/"),
              imageDocs => imageDocs
                  .ForEach(async doc => {
                      doc.ImageDescription = await Ai.DescribeImage(doc.Content);
                  })
                  .Embed(new AiEmbedOptions { Model = "vision-model" }))
        .Otherwise(otherDocs => otherDocs
              .ForEach(doc => doc.Status = "unsupported-format")))
    .Save()
    .ExecuteAsync();
```

### Pipeline Composition

```csharp
// Define reusable pipeline segments
public static PipelineBuilder<Document> AddStandardDocumentProcessing(
    this PipelineBuilder<Document> pipeline)
{
    return pipeline
        .ForEach(doc => {
            doc.ProcessedAt = DateTime.UtcNow;
            doc.Status = "processing";
        })
        .Tokenize(doc => doc.Content)
        .Embed(new AiEmbedOptions { Model = "all-minilm" });
}

public static PipelineBuilder<Document> AddQualityGating(
    this PipelineBuilder<Document> pipeline)
{
    return pipeline
        .Branch(branch => branch
            .When(doc => doc.Content.Length > 100,
                  validDocs => validDocs.ForEach(doc => doc.Status = "approved"))
            .Otherwise(invalidDocs => invalidDocs
                  .ForEach(doc => {
                      doc.Status = "rejected";
                      doc.RejectionReason = "Content too short";
                  })));
}

// Compose pipelines
await Document.AllStream()
    .Pipeline()
    .AddStandardDocumentProcessing()
    .AddQualityGating()
    .Save()
    .ExecuteAsync();
```

## Migration Strategies

### From Manual Processing

If you have existing manual processing code:

1. **Start with simple ForEach replacement**:
   ```csharp
   // Old
   foreach (var item in items) { item.Status = "processed"; }

   // New
   await items.AsAsyncEnumerable()
       .Pipeline()
       .ForEach(item => item.Status = "processed")
       .ExecuteAsync();
   ```

2. **Add streaming for large datasets**:
   ```csharp
   // Replace .All() with .AllStream()
   await Entity.AllStream().Pipeline()...
   ```

3. **Replace manual AI calls**:
   ```csharp
   // Old
   foreach (var doc in docs) {
       var embedding = await aiService.EmbedAsync(doc.Content);
       await vectorDb.StoreAsync(doc.Id, embedding);
   }

   // New
   await docs.Pipeline()
       .Embed(new AiEmbedOptions { Model = "all-minilm" })
       .Save()  // Handles both document and vector storage
       .ExecuteAsync();
   ```

4. **Add error handling**:
   ```csharp
   await Entity.AllStream()
       .Pipeline()
       .ForEach(/* processing */)
       .Branch(branch => branch
           .OnSuccess(success => success.Save())
           .OnFailure(failure => failure
               .Trace(env => $"Error: {env.Error?.Message}")
               .Save()))
       .ExecuteAsync();
   ```

## Next Steps

- **[AI Integration Guide](ai-integration.md)** - Deep dive into AI-powered workflows
- **[Performance Optimization](performance.md)** - Scaling pipeline workloads
- **[Multi-Provider Data](multi-provider-data.md)** - Storage backend strategies
- **[Core Pipeline Reference](../reference/core/semantic-streaming-pipelines.md)** - Complete API reference

---

*Semantic pipelines represent the future of data processing in .NET - complex workflows expressed through simple, natural patterns that scale effortlessly from prototype to production.*