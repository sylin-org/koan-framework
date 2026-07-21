# Semantic Streaming Pipeline DX Pattern

## Purpose
This document turns the earlier conversational outline into a concrete developer experience (DX) proposal for building semantic data-processing pipelines inside the Koan framework. The aim is to give developers a fluent, LINQ-inspired workflow that:

- Filters and transforms data with familiar `IAsyncEnumerable` operators before calling AI services.
- Streams items through asynchronous stages such as tokenization and vector storage without buffering entire datasets.
- Handles backpressure automatically so upstream stages slow down when downstream stages are saturated or rate-limited.
- Exposes configuration for batching, parallelism, resilience, and observability without forcing developers to write boilerplate plumbing code.

## Core Concepts

### Fluent Pipeline Builder
Developers compose pipelines by chaining extension methods on `IAsyncEnumerable<T>` sequences. Each call translates into a stage within an underlying execution graph:

```csharp
await Articles
    .Where(a => a.Status == "new")              // Pure filter on the async stream
    .Batch(size: 16)                             // Optional grouping before async calls
    .Tokenize(llmClient, options => options      // AI-powered stage (adds tokens to the envelope)
        .WithMaxConcurrency(4)
        .WithTimeout(TimeSpan.FromSeconds(30)))
    .Branch(branch => branch                     // Declarative success/failure handling
        .OnSuccess(success => success
            .Store(vectorStore, storageOptions =>
                storageOptions.WithConsistency(Consistency.Quorum)))
        .OnFailure(failure => failure
            .Mutate(envelope => envelope.Entity.Status = "parked")
            .Save()                              // Persist mutated entity for reprocessing
            .Notify(alerts.Topic("tokenizer"))))
    .ExecuteAsync(cancellationToken);
```

Internally, the builder maps each fluent call to a stage node (`PipelineStage`) with well-defined inputs, outputs, and policies. The `ExecuteAsync` terminal operator materializes the pipeline and drives the asynchronous execution engine.

To keep enriched artifacts without losing the originating entity, every stage operates on a `PipelineEnvelope<TEntity>` rather than a bare `TEntity`. The envelope carries:

- `Entity` – the original model (`Article` in the sample) so downstream branches can still mutate and save it.
- `Features` – a typed dictionary of enrichment outputs (tokens, embeddings, summaries) appended by each stage.
- `Metadata` – execution diagnostics (timestamps, correlation IDs, retry counts) accumulated along the path.

Enrichment stages such as `.Tokenize(...)` add to the envelope instead of replacing it. Tokenization writes its payload into `Features[FeatureKeys.Tokens]` while forwarding the same `Entity`, which is why the failure branch can still call `.Mutate(...)` followed by `.Save()`.

Because `.Mutate(...)` receives the `PipelineEnvelope<TEntity>` for the current item, the lambda in the sample updates `envelope.Entity.Status`. This makes it explicit that the underlying `Article` entity—not the token collection—gets marked as parked before the subsequent `.Save()` persists the change.

Convenience extensions keep the fluent syntax terse:

```csharp
.Tokenize(llmClient)
.Tap(envelope =>
{
    var article = envelope.Entity;                // same instance filtered upstream
    var tokens = envelope.Features.GetTokens();   // enrichment emitted by Tokenize
    // ...additional logic...
});
```

Here `.Tap(...)` inspects the envelope (and its features) while leaving the entity available for downstream `.Save()` calls.

### Streaming Execution Engine

- **`IAsyncEnumerable` as the contract:** All stages operate on asynchronous streams to preserve laziness and prevent up-front materialization.
- **`Channel<T>`-backed queues:** Each stage boundary uses bounded channels to buffer items and enforce backpressure. When a downstream channel is full, upstream awaits free capacity.
- **Configurable concurrency:** Stages that invoke network-bound AI services use worker pools with configurable degrees of parallelism.
- **Cancellation-aware:** Cooperative cancellation flows through the pipeline via tokens passed to every stage.

### Backpressure and Flow Control

Backpressure is automatically handled by the bounded capacity of channels. Configuration controls include:

| Setting | Description |
| --- | --- |
| `ChannelCapacity` | Maximum buffered items for the stage. `0` can force synchronous handoff. |
| `MaxConcurrency` | Number of concurrent workers pulling from the input channel. |
| `BatchSize` | Number of items grouped per AI request, allowing upstream to pause when a batch is outstanding. |
| `Timeout`/`RetryPolicy` | Guard long-running AI calls and signal upstream when failures occur. |

By keeping the fluent API declarative, developers declare desired behavior while the engine handles the mechanics.

### Branching and Alternate Paths

The execution graph is not constrained to a single linear chain. Each stage can expose routing predicates so items fan out to
alternate subgraphs when business logic or failures demand different handling:

- **Declarative routing:** A `.Branch(...)` stage wraps downstream stages in named branches (e.g., `OnSuccess`, `OnFailure`,
  `On(predicate)`), providing a fluent experience akin to `switch` expressions while compiling to dedicated channel networks.
- **Manual forks:** `.Fork()` duplicates the stream so one branch can persist to the system of record while another publishes
  a notification feed, without double-executing upstream work.
- **Parking for reprocessing:** Dedicated `.Save()`/`.Park(...)`/`.RetryLater(...)` verbs enqueue envelopes into holding stores
  (queues, blob storage, or Koan’s existing parking tables). Optional `.Mutate(...)` hooks let branches stamp status updates (for
  example, setting `Status = "parked"`) before the persistence call. Envelope metadata captures the originating stage and error,
  so future runs can resume via `.ResumeFromParked(...)` sources.
- **Dead-letter introspection:** Branches can emit diagnostics when they activate so observability surfaces include counts of
  routed items, retry latency, and ultimate disposition.

Branches are first-class nodes in the pipeline graph, meaning backpressure still applies per branch: if the park queue is slow
to accept new messages, upstream stages pause just as they would for the happy path.

## Configuration Model

Provide strongly-typed options objects with sensible defaults:

- `TokenizationOptions`
  - `MaxConcurrency`
  - `BatchSize`
  - `ChannelCapacity`
  - `Timeout`
  - `RetryPolicy`
- `StorageOptions`
  - `ChannelCapacity`
  - `MaxConcurrency`
  - `Consistency`
  - `OnConflict`

Options can be supplied via inline lambdas (as in the sample) or bound from configuration files using Koan's configuration system. This ensures consistency across environments and promotes reuse.

## Resilience

Implement resilience primitives using `Polly` or the Koan resilience abstractions:

- **Circuit breakers** to stop sending traffic to failing AI providers.
- **Bulkhead isolation** to limit concurrent requests.
- **Timeouts and retries** for transient failures.
- **Dead-letter queues** for items that exceed retry policies, preserving auditability.
- **Branch-aware recovery:** Retry policies cooperate with routing so a stage can attempt configured retries before emitting to a
  `.Save()` or `.Park(...)` branch, ensuring consistent handling for both transient and terminal failures.

Expose hooks so developers can register custom error handlers or telemetry enrichers.

## Observability

- **Metrics:** Emit stage-level metrics (throughput, latency, queue depth) via OpenTelemetry instruments.
- **Structured logging:** Include correlation IDs per item/batch to trace lifecycle across stages.
- **Tracing:** Wrap AI and storage calls in OpenTelemetry spans with semantic attributes (model, prompt size, token count, etc.).
- **Branch telemetry:** Attribute metrics/spans by branch name so parked/reprocessed workloads are visible alongside the primary
  flow.

A `PipelineDiagnostics` object can aggregate these signals and surface them in dashboards.

## Fit with Existing Koan Fluent Patterns

The proposal should layer on top of the verbs developers already know instead of inventing a parallel model:

- **Stream sources with `Entity<>`:** `Entity<TEntity, TKey>.AllStream()` and `QueryStream()` already expose `IAsyncEnumerable<T>` entry points, so a pipeline can start with `Articles.AllStream()` (or a LINQ filter over it) rather than materializing `Articles.All()` into memory.【F:src/Koan.Data.Core/Model/Entity.cs†L59-L100】 This keeps the streaming semantics consistent with the rest of the data stack.
- **Batch writes through the existing facade:** `Entity.Batch()` returns an `IBatchSet` that already knows how to persist groups of changes, so a `.Batch(size: 32)` stage can hand off to the same abstraction that powers today’s bulk saves instead of inventing a new buffer primitive.【F:src/Koan.Data.Core/Model/Entity.cs†L97-L100】【F:src/Koan.Data.Abstractions/IBatchSet.cs†L1-L11】
- **Reuse `.Save()` verbs:** The fluent `model.Save()` extensions in `AggregateExtensions` are thin aliases over `UpsertAsync`, so a terminal `.Save()` stage can simply call into the same helper regardless of whether items came from a stream or a traditional controller.【F:src/Koan.Data.Core/AggregateExtensions.cs†L18-L93】
- **Keep `.Send()` for fan-out:** Messaging already exposes the ergonomic `await message.Send()` extension, so a pipeline stage that sends downstream events can just invoke it per item/batch while still benefiting from backpressure (bounded channel capacity would naturally slow the stage if the transport stalls).【F:src/Koan.Messaging.Core/MessagingExtensions.cs†L72-L135】
- **Vector store compatibility:** Vector ingestion today flows through `Vector<TEntity>.Save(...)`, which accepts individual tuples or batches. The proposed `.Store()` stage can forward embeddings there, ensuring parity with existing AI indexing code paths.【F:src/Koan.Data.Vector/Vector.cs†L10-L76】
- **AI/token work is already centralized:** Koan’s AI facade exposes `Ai.Embed`/`Ai.EmbedAsync`, so a `.Tokenize()` stage can wrap those calls (or similar provider adapters) and inherit the same configuration, diagnostics, and retry policy that the rest of the platform uses—even though a first-class `.Tokenize()` verb does not ship today.【F:src/Koan.AI/Ai.cs†L9-L91】

These hooks mean the fluent pipeline can mostly compose existing verbs. The sample `Articles.All().Batch(size: 32).Send().Each(i => i.Sent = true).Save();` would need two tweaks to align with today’s API surface:

1. Swap `.All()` for `.AllStream()` (or another `IAsyncEnumerable` source) to avoid eager loading.
2. Implement an `.Each()`/`.ForEachAsync()` pipeline stage that applies mutations before delegating to the existing `.Save()` helper.

Everything else—batch persistence, messaging fan-out, and vector storage—can lean on the currently shipping abstractions once the streaming builder wires them together.

## Integration Points

- **LLM Clients:** Provide adapters for OpenAI, Azure OpenAI, and other providers. The `Tokenize` stage accepts an abstraction (e.g., `ITokenizationClient`).
- **Vector Stores:** Support pluggable backends (Pinecone, Qdrant, pgvector) through a `IVectorStore` interface.
- **Koan Templates:** Offer a project template that scaffolds the pipeline with recommended defaults and wiring.
- **Developer Tooling:** Integrate with IDE analyzers to suggest pipeline configurations and highlight missing resilience policies.

## Extension Hooks

Developers can register custom stages:

```csharp
public static class PipelineExtensions
{
    public static IPipelineBuilder<Article> Summarize(
        this IPipelineBuilder<Article> builder,
        ISummarizationClient client,
        Action<SummarizationOptions>? configure = null)
    {
        // Stage registration exposing options and diagnostics
        return builder.AddStage("Summarize", configureOptions =>
        {
            var options = configureOptions(configure);
            return new SummarizationStage(client, options);
        });
    }
}
```

A stage simply implements an interface such as:

```csharp
public interface IPipelineStage<TIn, TOut>
{
    ValueTask ExecuteAsync(ChannelReader<TIn> input, ChannelWriter<TOut> output, CancellationToken cancellationToken);
}
```

This ensures compatibility with the execution engine while giving developers flexibility to plug in additional AI-enabled behaviors.

## Next Steps

1. **Prototype:** Build a spike implementation using Channels to validate throughput, backpressure, and diagnostics instrumentation.
2. **Define Contracts:** Finalize interfaces for `IPipelineBuilder`, stage options, and diagnostics hooks.
3. **Template & Samples:** Provide sample projects and documentation illustrating common scenarios (content ingestion, customer support transcripts, etc.).
4. **Tooling:** Explore Roslyn analyzers and editor tooling to assist in configuring pipelines and highlight missing resilience policies.

By following this blueprint, Koan can offer a first-class semantic streaming pipeline experience that blends the elegance of LINQ with the robustness required for production-grade AI integrations.

## Refactoring Opportunities Across Samples

Scanning the flagship samples reveals multiple hot spots where today’s imperative loops can collapse into declarative pipelines once the fluent builder ships:

- **S5.Recs ingestion worker:** `SeedService` already streams provider batches, then performs sequential import, embedding, and catalog fan-out inside the `await foreach` loop. Replacing the inner section with `.Batch(size).Import().Tokenize().StoreVectors().PublishDiagnostics()` would encapsulate the progress tracking, throttle embedding concurrency, and allow vector failures to dead-letter instead of halting the whole import.【F:samples/S5.Recs/Services/SeedService.cs†L132-L181】
- **S9.Location harmonization:** `LocationFlowProcessor` polls parked records, iterates each item, and invokes the harmonization pipeline one by one. A streaming pipeline could subscribe to `ParkedRecord<RawLocation>.AllStream()` and compose `.Where(...)`, `.Tokenize()` for normalization, and `.Send()` into the healing actions, letting bounded channels replace the manual batching/polling loop and exposing observability automatically.【F:samples/S9.Location/Core/Processing/LocationFlowProcessor.cs†L33-L124】
- **S12.MedTrials document ingestion:** `ProtocolDocumentService` orchestrates embedding, vector storage, diagnostics, and persistence inside a single method. The semantic pipeline can convert this into `.Tokenize()` with retry policies, `.StoreVectors()` honoring vector availability, and `.Save()` to persist documents, enabling backpressure when embedding throughput is lower than document ingestion and eliminating ad-hoc warning aggregation.【F:samples/S12.MedTrials.Core/Services/ProtocolDocumentService.cs†L24-L133】
- **Telemetry-heavy flows:** Seeders such as `FlowSeeder` execute adapter-specific catalog writes sequentially and handle readiness timeouts manually. Future pipelines could express `.Send()` to adapters with `.OnFailure(...)` hooks, while the execution engine centralizes timeout/retry patterns and emits consistent metrics for seeding workloads.【F:samples/S8.Canon/S8.Canon.Api/FlowSeeder.cs†L20-L79】

These candidates provide an incremental adoption path: wrap existing logic in pipeline stages, then replace bespoke concurrency, retry, and logging blocks with reusable options surfaced by the fluent DSL.

## Top-Level Pipeline Extensions per Koan Pillar

To make the DSL feel native, each pillar module should expose a top-level `PipelineExtensions` class that lights up the domain-specific verbs developers already use today:

- **Data (`Koan.Data.Core`):**
  - `Stream<TEntity>(this Entity<TEntity, TKey> source)` returns an `IPipelineBuilder<TEntity>` seeded from `AllStream()`/`QueryStream()`.
  - `Save<TEntity>(this IPipelineBuilder<TEntity> builder, Action<SaveOptions>? configure = null)` drains the pipeline into the existing `AggregateExtensions.UpsertAsync` helper with bulk/batch awareness.
  - `Batch<TEntity>(this IPipelineBuilder<TEntity> builder, int size, Action<BatchOptions>? configure = null)` bridges to the `IBatchSet` infrastructure for write coalescing.
- **Messaging (`Koan.Messaging.Core`):**
  - `Send<TMessage>(this IPipelineBuilder<TMessage> builder, Func<TMessage, ValueTask> dispatcher, Action<SendOptions>? configure = null)` wraps the `MessagingExtensions.Send` helpers and exposes channel capacity/concurrency knobs aligned with transport throughput.
  - `Publish<TMessage>(this IPipelineBuilder<TMessage> builder, IMessagePublisher publisher, Action<PublishOptions>? configure = null)` fans out to topic-based pipelines while providing retry/backoff defaults consistent with messaging policies.
- **AI (`Koan.AI`):**
  - `Tokenize<T>(this IPipelineBuilder<T> builder, Func<T, AiEmbeddingsRequest> requestFactory, Action<TokenizationOptions>? configure = null)` orchestrates embedding calls through `Ai.EmbedAsync`, capturing model metadata and surfacing vector counts for diagnostics.
  - `Classify<T>(this IPipelineBuilder<T> builder, Func<T, AiCompletionRequest> promptFactory, Action<ClassificationOptions>? configure = null)` enables streaming enrichment scenarios with natural throttling when provider latency spikes.
- **Vector/Data Products (`Koan.Data.Vector`):**
  - `StoreVectors<TEntity>(this IPipelineBuilder<(TEntity Entity, ReadOnlyMemory<float> Vector, object Metadata)> builder, Action<VectorStoreOptions>? configure = null)` forwards batches into `Vector<TEntity>.Save` while supporting fan-out to alternate sinks when the primary store is degraded.
  - `Search<TEntity>(this IPipelineBuilder<ReadOnlyMemory<float>> builder, int topK, Action<VectorSearchOptions>? configure = null)` emits query matches as an async stream for downstream filtering/ranking stages.
- **Observability (`Koan.Diagnostics` or equivalent):**
  - `WithDiagnostics<T>(this IPipelineBuilder<T> builder, Action<PipelineDiagnosticsOptions>? configure = null)` auto-wires metrics, logs, and traces, ensuring every pipeline stage contributes to OpenTelemetry dashboards without bespoke wiring.

By surfacing these entry points alongside existing verbs, developers can adopt the semantic pipeline incrementally—start with `Entity<Article>.Stream().Where(...)` and sprinkle in `.Tokenize()` or `.Send()` exactly where AI and messaging cross-cut the workflow.
