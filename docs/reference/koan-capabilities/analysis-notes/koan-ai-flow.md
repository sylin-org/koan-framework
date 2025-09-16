# Koan AI and Event-Driven Flow Architecture Analysis

## Executive Summary

Koan's AI and Event-Driven Flow architecture provides event-driven AI application capabilities, combining dynamic schema flexibility with AI provider abstraction and event sourcing patterns in a distributed system architecture.

## AI Infrastructure Architecture

### Core AI Abstraction Strategy

**Adapter-Router Pattern** with complete provider abstraction:
- **IAiAdapter**: Stateless, thread-safe provider implementations with capability negotiation
- **IAiRouter**: Intelligent routing layer with round-robin load balancing
- **IAiAdapterRegistry**: Dynamic provider registry with runtime adapter addition/removal
- **EngineSelector**: Fluent API for provider/model selection with route hints

**Provider Pattern Design:**
```csharp
public interface IAiAdapter {
    bool CanServe(AiChatRequest request);  // Dynamic capability matching
    string Id { get; }  // Stable provider identifier (e.g., "ollama:local:11434")
    string Type { get; }  // Provider type classification
}
```

**Sophisticated Routing Strategy:**
1. **Explicit Routing**: Honor `AiRouteHints.AdapterId` for targeted execution
2. **Capability-Based Round-Robin**: Auto-select providers that `CanServe()` the request
3. **Graceful Fallback**: Default to any available provider when capability matching fails

### Framework Integration Patterns

**Service Locator with Ambient Context:**
```csharp
public static class Ai {
    private static readonly AsyncLocal<IAi?> _override = new();
    // Greenfield boot integration with AppHost.Current
    // Scoped vs singleton resolution strategy
}
```

**Key Integration Points:**
- **AppHost Integration**: Leverages `Koan.Core.Hosting.App.AppHost.Current` for service resolution
- **Scoped Context Override**: `AsyncLocal<T>` pattern for request-scoped AI provider overrides
- **DI-Friendly**: Full `IServiceProvider` integration with scope factory fallbacks

### Extension Points and Contracts

**Multi-Modal AI Contracts:**
- **Chat**: Synchronous and streaming conversation interfaces
- **Embeddings**: Vector generation for RAG/semantic search scenarios
- **Model Management**: Dynamic model discovery and capability reporting
- **Vendor Options**: Flexible parameter passing via `JObject` VendorOptions

**Extensibility Architecture:**
- **Auto-Registration**: `KoanAutoRegistrar` pattern for provider discovery
- **Health Monitoring**: `AiHealthSubscriber` for provider availability tracking
- **Configuration-Driven**: Dynamic provider configuration via discovery initializers

## Flow and Event Sourcing Architecture

### Flow Architecture and Event Sourcing Implementation

**Sophisticated Event Sourcing Pattern** with materialization engines:

**Core Flow Concepts:**
1. **Stage-Based Processing**: `Intake → Association → Keying → Projection → Materialization`
2. **Dynamic Entity Support**: Both strongly-typed `FlowEntity<T>` and flexible `DynamicFlowEntity<T>`
3. **Projection-Driven Views**: Canonical (current state) vs Lineage (full history) projections
4. **External ID Correlation**: Advanced entity association across system boundaries

**Event Sourcing Architecture:**
```csharp
public sealed class StageRecord<TModel> : Entity<StageRecord<TModel>> {
    public TModel? Data { get; set; }  // Clean business data
    public Dictionary<string, object?>? Source { get; set; }  // System metadata
    public string? ReferenceId { get; set; }  // Post-association entity correlation
}
```

### Flow Orchestration and Dynamic Entities

**FlowOrchestratorBase** - Core Flow processing:

**Sophisticated Entity Processing:**
- **Type-Safe Deserialization**: Handles `FlowEntity<T>`, `DynamicFlowEntity<T>`, and `TransportEnvelope<T>`
- **Clean Metadata Separation**: Business data vs system metadata isolation
- **Interceptor Pipeline**: Fluent `BeforeIntake` interceptors with Drop/Park/Continue actions
- **Dynamic Handler Integration**: `Flow.OnUpdate<T>` handlers with conflict resolution

**Dynamic Entity Architecture:**
```csharp
public interface IDynamicFlowEntity {
    JObject? Model { get; set; }  // Flexible schema using JObject
}

// Extension methods for path-based access
entity.WithPath("identifier.username", "jdoe")
      .WithPath("attributes.location.city", "Seattle")
```

**Key Design Patterns:**
- **Path-Based Property Access**: JSON path notation for nested property access
- **Aggregation Key Resolution**: Dynamic key extraction for entity correlation
- **MongoDB-Safe Serialization**: Recursive primitive type conversion

### Materialization Engines and Event Processing

**FlowMaterializer** - Advanced conflict resolution engine:

**Materialization Strategies:**
- **Policy-Driven**: `Last`, `First`, `Max`, `Min`, `Coalesce` built-in policies
- **Custom Transformers**: Extensible `IPropertyMaterializationTransformer` and `IRecordMaterializationTransformer`
- **Per-Path Configuration**: Fine-grained policy control at individual property paths
- **Canonical Ordering**: Time-ordered value resolution with policy-based conflict resolution

**Performance Optimizations:**
- **Concurrent Policy Cache**: `ConcurrentDictionary` caching for transformer instances
- **Lazy Type Resolution**: Reflection-based type loading with assembly scanning
- **Warn-Once Pattern**: Reduces log noise for configuration issues

### External ID Correlation and Entity Association

**Sophisticated Association Patterns:**
```csharp
public sealed class KeyIndex<TModel> : Entity<KeyIndex<TModel>> {
    public string AggregationKey { get => Id; set => Id = value; }
    public string ReferenceId { get; set; }  // UUID correlation
}
```

**Association Architecture:**
- **Multi-Key Correlation**: Support for composite aggregation keys
- **UUID-Based References**: `ReferenceItem<T>` provides stable entity correlation
- **Versioned Projections**: `ulong Version` tracking for projection state management
- **Parent-Child Hierarchies**: `KeyedRecord<T>.Owners` for hierarchical entity relationships

## Event-Driven Integration Patterns

### RabbitMQ and Dapr Runtime Integration

**Dapr Flow Runtime** - Cloud-native distributed flow capabilities:
```csharp
public async Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
{
    // Enqueue projection tasks for references marked RequiresProjection
    // Time-window aware replay using lineage timestamps
}
```

**Event Distribution Mechanisms:**
- **ProjectionTask<T>**: Distributed task queuing for projection operations
- **Reference-Based Correlation**: Entity-centric replay and reprocessing
- **Multi-Model Discovery**: Automatic model type discovery via reflection

**Messaging Infrastructure:**
- **Message Bus Integration**: `Koan.Messaging` abstractions for transport-agnostic messaging
- **Flow Action Pattern**: Structured command messaging with `FlowAction` records
- **Idempotency Keys**: Built-in duplicate message handling

### Event Ordering, Delivery, and Failure Recovery

**Reliability Patterns:**
- **Parking Pattern**: `ParkedRecord<T>` for failed/delayed messages with reason codes
- **Stage Transitions**: Atomic progression through processing stages
- **Retry Logic**: Background service integration with exponential backoff
- **Dead Letter Handling**: TTL-based parking for manual inspection

**Event Processing Guarantees:**
- **At-Least-Once Delivery**: Idempotent processing with correlation IDs
- **Order Preservation**: Timestamp-based ordering within aggregation boundaries
- **Fault Isolation**: Per-entity failure boundaries to prevent cascade failures

## Web Integration and API Patterns

### AI Services Web API Integration

**AiController** - RESTful AI service exposition:
```csharp
[HttpPost(Constants.Routes.ChatStream)]
public async Task Stream([FromBody] AiChatRequest request, CancellationToken ct) {
    Response.ContentType = "text/event-stream";  // Server-Sent Events
    await foreach (var chunk in _ai.StreamAsync(request, ct)) {
        await Response.WriteAsync($"data: {chunk.DeltaText}\n\n", ct);
    }
}
```

**Web Integration Features:**
- **Streaming Chat**: Server-Sent Events for real-time AI responses
- **Health Endpoints**: Provider availability and capability reporting
- **Model Discovery**: Dynamic model listing across all registered providers
- **Authentication Ready**: Framework integration points for auth middleware

### Flow Processing Web Integration

**IntakeController** - Direct Flow entity ingestion:
- **Clean DTO Patterns**: `IntakeRecordDto` for structured data intake
- **Validation Pipeline**: Schema validation before Flow processing
- **Async Processing**: Accepted (202) response pattern for long-running operations

**Real-Time Integration Patterns:**
- **Background Services**: `KoanFluentServiceBase` integration for Flow orchestrators
- **Service Actions**: Attribute-driven action handling for Flow operations
- **Event Emission**: Structured event publishing for observability

## Dynamic Entity and Projection System

### Dynamic Entity System Architecture

**Flexible Schema Management:**
```csharp
// Path-based property manipulation
entity.SetPathValue("customer.billing.address.street", "123 Main St");
var city = entity.GetPathValue<string>("customer.billing.address.city");

// Aggregation key extraction
var keys = entity.ExtractAggregationValues(new[] { "identifier.code", "location.facility" });
```

**Key Design Elements:**
- **JObject-Based Storage**: JSON-native property storage with path-based access
- **Type-Safe Wrappers**: Strongly-typed access patterns over flexible schema
- **Flattening Utilities**: Bidirectional conversion between nested/flattened representations
- **MongoDB Optimization**: BSON-safe serialization with null value elimination

### Projection Patterns and Performance

**Projection Architecture:**
```csharp
public sealed class CanonicalProjection<TModel> : Entity<CanonicalProjection<TModel>> {
    public string ViewName { get; set; }  // Multi-view support
    public string? ReferenceId { get; set; }  // Entity correlation
    public object? Model { get; set; }  // Materialized snapshot
}
```

**High-Performance Patterns:**
- **Batch Processing**: `OptimizedModelProjectionWorker` with adaptive batching
- **Parallel Projection**: Concurrent processing with semaphore-based throttling
- **Memory Management**: GC-aware batch sizing with performance metrics
- **Bulk Operations**: MongoDB bulk upsert/delete operations

## AI-Flow Integration Patterns

### AI Operations in Flow Orchestrations

**Flow.OnUpdate Pattern with AI:**
```csharp
Flow.OnUpdate<Customer>(async (ref customer, current, metadata) => {
    if (Ai.IsAvailable) {
        var enrichment = await Ai.Prompt($"Enrich customer: {customer.Name}");
        customer.AiEnrichment = enrichment;
    }
    return UpdateResult.Continue("AI enrichment applied");
});
```

**Event-Driven AI Processing:**
- **Flow Interceptors**: `BeforeIntake` interceptors can trigger AI processing
- **Materialization Transformers**: Custom transformers can incorporate AI-generated content
- **Projection-Based AI**: Post-projection AI processing for derived intelligence

**Vector Database Integration:**
```csharp
// Embedding generation during Flow processing
var embeddingResponse = await Ai.Embed(new AiEmbeddingsRequest {
    Input = new List<string> { entity.Content },
    Model = "text-embedding-model"
});
entity.Vector = embeddingResponse.Vectors.First();
```

## Distributed System and Scalability Patterns

### Distributed Event Processing

**Scalability Architecture:**
- **Dapr Integration**: Cloud-native service mesh integration for distributed flows
- **Partition-Based Processing**: Entity-based partitioning for horizontal scaling
- **Background Services**: `KoanBackgroundService` pattern for scalable worker processes
- **Adaptive Batching**: Dynamic batch sizing based on system performance metrics

**High-Throughput Patterns:**
- **Concurrent Processing**: Semaphore-controlled parallel processing
- **Bulk Database Operations**: MongoDB bulk operations for reduced network overhead
- **Memory-Aware Processing**: GC pressure monitoring for optimal batch sizing
- **Stage-Based Scaling**: Independent scaling of Intake, Association, Projection workers

### Cloud-Native Distributed Flows

**Dapr Integration Benefits:**
- **Service Discovery**: Automatic service mesh integration
- **State Management**: Distributed state stores for Flow metadata
- **Pub/Sub Integration**: Message broker abstraction for event distribution
- **Observability**: Distributed tracing and metrics collection

## Configuration and Operational Patterns

### AI Provider and Flow System Configuration

**Configuration Architecture:**
```csharp
// Dynamic AI provider discovery
public class OllamaDiscoveryInitializer {
    // Auto-discovery of AI providers with health checking
    // Configuration-driven provider registration
    // Environment-specific provider routing
}
```

**Operational Features:**
- **Health Monitoring**: `AiHealthSubscriber` for provider availability tracking
- **Dynamic Configuration**: Hot-reload support for AI provider configuration
- **Auto-Discovery**: Environment-based provider discovery (Docker, K8s service discovery)

### Observability and Error Handling

**Monitoring Capabilities:**
- **Structured Logging**: Comprehensive Flow processing logging with correlation IDs
- **Performance Metrics**: `AdaptiveBatchProcessor` with processing time/throughput metrics
- **Health Endpoints**: Provider health and capability reporting
- **Event Emission**: Structured event publishing for external monitoring systems

**Fault Tolerance Design:**
- **Parking Pattern**: Non-blocking failure handling with manual resolution paths
- **Idempotent Processing**: Correlation ID-based duplicate detection
- **Stage Isolation**: Failure boundaries prevent cascade failures
- **Retry Strategies**: Exponential backoff with circuit breaker patterns

## Module Breakdown

### AI and Machine Learning (4 modules)
- **Koan.AI** - Core AI infrastructure and abstractions
- **Koan.AI.Contracts** - AI service contracts and DTOs
- **Koan.AI.Web** - Web integration for AI services
- **Koan.Ai.Provider.Ollama** - Ollama AI provider integration

### Event Streaming and Flow (4 modules)
- **Koan.Flow.Core** - Event sourcing and flow orchestration core
- **Koan.Flow.Web** - Web integration for flow operations
- **Koan.Flow.RabbitMq** - RabbitMQ integration for flow events
- **Koan.Flow.Runtime.Dapr** - Dapr runtime integration for distributed flows

## Distinctive Framework Characteristics

### What Makes Koan's AI-Flow Integration Unique

1. **Hybrid Entity Model**: Seamless integration of strongly-typed and dynamic entities within the same processing pipeline

2. **Clean Metadata Separation**: Architectural principle of separating business data from system metadata throughout the entire pipeline

3. **Policy-Driven Materialization**: Sophisticated conflict resolution with pluggable materialization strategies at the property level

4. **AI-Native Design**: Built-in AI provider abstraction with capability-based routing and ambient context overrides

5. **Projection-Centric Architecture**: Event sourcing through projection lenses rather than traditional aggregate roots

6. **Stage-Based Processing**: Explicit pipeline stages with independent scaling and failure isolation

7. **Cloud-Native Distributed Design**: Dapr integration for true distributed flow processing across service boundaries

## Conclusion

This architecture provides capabilities for event-driven AI applications, combining the flexibility of dynamic schemas with AI processing and the reliability of event sourcing in a cloud-native, distributed system architecture. The framework enables complex AI-driven workflows while maintaining enterprise-grade reliability and scalability patterns.