# Test Strategy: ADR AI-0020 Entity-First AI Integration

**Document Version:** 1.0
**Date:** 2025-11-13
**Status:** Active

## Executive Summary

This document outlines the comprehensive test strategy for ADR AI-0020 implementation across 5 phases: Transaction Coordination, Pipeline API, Enhanced Embeddings, Production Guardrails, and Documentation/Samples.

**Risk Level:** HIGH - Core framework functionality affecting data consistency, cost tracking, and production operations.

**Test Coverage Target:** 95% code coverage, 100% critical path coverage

---

## 1. Test Pyramid Strategy

```
                    /\
                   /  \
                  / E2E \ (5%)
                 /______\
                /        \
               / Integr.  \ (25%)
              /____________\
             /              \
            /   Unit Tests   \ (70%)
           /__________________\
```

### Distribution Rationale
- **Unit Tests (70%)**: Fast feedback, isolated components, high coverage
- **Integration Tests (25%)**: Cross-component interactions, database/AI integration
- **E2E Tests (5%)**: Critical user flows, full stack validation

---

## 2. Phase 1: Transaction Coordination Tests

### 2.1 Unit Tests

#### TransactionCoordinator Tests
```
Suite: Koan.Tests.Data.Core.Unit.Transactions.TransactionCoordinatorSpec
Location: tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Transactions/
```

**Test Cases:**

1. **TrackVectorSave_WithActiveTransaction_DefersExecution**
   - **Given:** Active transaction with coordinator
   - **When:** `Vector<T>.Save()` called
   - **Then:** Operation added to tracked operations, not executed immediately
   - **Assertion:** `_operationsByAdapter` contains VectorSaveOperation

2. **TrackVectorDelete_WithActiveTransaction_DefersExecution**
   - **Given:** Active transaction
   - **When:** `Vector<T>.Delete()` called
   - **Then:** Operation deferred
   - **Assertion:** Delete operation tracked

3. **CommitAsync_WithVectorOperations_ExecutesAll**
   - **Given:** Transaction with 3 entity saves + 2 vector saves
   - **When:** `CommitAsync()` called
   - **Then:** All operations execute in order
   - **Assertion:** All repository methods called exactly once

4. **RollbackAsync_WithVectorOperations_DiscardsAll**
   - **Given:** Transaction with pending entity + vector operations
   - **When:** `RollbackAsync()` called
   - **Then:** Operations discarded, no execution
   - **Assertion:** Repository methods never called

5. **ExecuteAsync_VectorOperation_RestoresContext**
   - **Given:** VectorSaveOperation with source="vectordb-secondary"
   - **When:** `ExecuteAsync()` called
   - **Then:** EntityContext restored during execution
   - **Assertion:** Correct adapter/source used

#### VectorData Tests
```
Suite: Koan.Tests.Data.Vector.Unit.VectorDataSpec
Location: tests/Suites/Data/Vector/Unit/
```

6. **SaveWithVector_WithTransaction_DefersBoth**
   - **Given:** Active transaction
   - **When:** `SaveWithVector(entity, vector)` called
   - **Then:** Both entity and vector operations deferred
   - **Assertion:** Neither executed until commit

7. **SaveWithVector_WithoutTransaction_ExecutesSequentially**
   - **Given:** No active transaction
   - **When:** `SaveWithVector(entity, vector)` called
   - **Then:** Entity saves, then vector saves
   - **Assertion:** Execution order verified

8. **SaveWithVector_EntitySavedVectorFails_ThrowsCoordinationException**
   - **Given:** No transaction, vector repository throws exception
   - **When:** `SaveWithVector()` called
   - **Then:** Entity persisted, exception thrown with correct flags
   - **Assertion:**
     ```csharp
     exception.EntitySaved == true
     exception.VectorSaved == false
     exception.EntityId == entity.Id
     ```

9. **SaveWithVector_EntityFails_ThrowsOriginalException**
   - **Given:** Entity repository throws exception
   - **When:** `SaveWithVector()` called
   - **Then:** Original exception propagates, no coordination exception
   - **Assertion:** Exception type matches entity repository exception

#### TrackedOperations Tests
```
Suite: Koan.Tests.Data.Core.Unit.Transactions.TrackedOperationsSpec
```

10. **VectorSaveOperation_ExecuteAsync_UsesReflection**
    - **Given:** VectorSaveOperation for Media entity
    - **When:** `ExecuteAsync()` called
    - **Then:** Reflection locates IVectorService and invokes UpsertAsync
    - **Assertion:** Vector stored in repository

11. **VectorSaveOperation_ServiceNotFound_ThrowsInformativeException**
    - **Given:** IVectorService not registered
    - **When:** `ExecuteAsync()` called
    - **Then:** Clear exception message guides remediation
    - **Assertion:** Exception message contains "IVectorService not found"

### 2.2 Integration Tests

```
Suite: Koan.Tests.Data.Integration.Transactions.VectorTransactionSpec
Location: tests/Suites/Data/Integration/
```

12. **Transaction_MixedEntityVectorOperations_CommitsAtomically**
    - **Setup:** Real SQL database + Qdrant vector DB
    - **Given:** Transaction with 2 entities + 3 vectors
    - **When:** Commit
    - **Then:** All persisted, queryable
    - **Assertion:** Database + vector counts match expected

13. **Transaction_MixedOperations_RollbackDiscardsAll**
    - **Setup:** Real databases
    - **Given:** Transaction with entities + vectors
    - **When:** Exception thrown, rollback triggered
    - **Then:** Nothing persisted
    - **Assertion:** Both stores empty

14. **Transaction_NestedTransactions_ThrowsNotSupported**
    - **Given:** Active transaction
    - **When:** `BeginTransaction()` called again
    - **Then:** NotSupportedException thrown
    - **Assertion:** Clear error message

---

## 3. Phase 2: Pipeline API Tests

### 3.1 Unit Tests

```
Suite: Koan.Tests.AI.Unit.Pipelines.TextPipelineSpec
Location: tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Specs/Pipelines/
```

15. **FromText_ToImage_DefersGeneration**
    - **Given:** `Ai.FromText("sunset")`
    - **When:** `.ToImage()` called
    - **Then:** No AI call made yet
    - **Assertion:** Lazy<Task<byte[]>> not evaluated

16. **FromText_ToImage_ToStorage_ExecutesOnce**
    - **Given:** Pipeline with ToImage
    - **When:** `.ToStorage()` called
    - **Then:** Image generated exactly once
    - **Assertion:** AI service called once, cached result used

17. **FromText_ToImage_MultipleSinks_CachesResult**
    - **Given:** `var pipeline = Ai.FromText("x").ToImage()`
    - **When:** `await pipeline.ToStorage()` then `await pipeline.ToBytes()`
    - **Then:** Image generated once, result reused
    - **Assertion:** AI call count == 1

18. **ToEmbedding_ImmediateExecution_ReturnsEmbedding**
    - **Given:** `Ai.FromText("machine learning")`
    - **When:** `.ToEmbedding()` awaited
    - **Then:** Embedding returned immediately
    - **Assertion:** float[] length matches model dimensions

```
Suite: Koan.Tests.AI.Unit.Pipelines.ImagePipelineSpec
```

19. **FromImage_ToText_CallsVisionModel**
    - **Given:** Image bytes + prompt
    - **When:** `.ToText("Describe this")` awaited
    - **Then:** Vision model invoked with image + prompt
    - **Assertion:** Response contains description

20. **ToStorage_SavesBlob_ReturnsStorageResult**
    - **Given:** ImagePipeline with bytes
    - **When:** `.ToStorage(container: "generated")` awaited
    - **Then:** Blob saved to specified container
    - **Assertion:** StorageResult.Id queryable from blob storage

```
Suite: Koan.Tests.AI.Unit.Pipelines.PipelineContextSpec
```

21. **Context_ModelInheritance_PropagatesThroughStages**
    - **Given:** `Ai.FromText("x")` with model set via `Client.Context(model: "gpt-4")`
    - **When:** Pipeline stages execute
    - **Then:** Model context inherited
    - **Assertion:** Correct model used in all stages

### 3.2 Integration Tests

```
Suite: Koan.Tests.AI.Integration.PipelineE2ESpec
```

22. **TextToImageToStorage_FullPipeline_E2E**
    - **Setup:** Real AI service (or mock with recording)
    - **Given:** Text prompt
    - **When:** Full pipeline executed
    - **Then:** Image stored, retrievable
    - **Assertion:** Blob exists, has correct MIME type

23. **Pipeline_AiServiceFailure_PropagatesError**
    - **Setup:** Mock AI service throws exception
    - **Given:** Pipeline with failing service
    - **When:** Terminal operation called
    - **Then:** Original exception propagates with context
    - **Assertion:** Exception contains pipeline stage info

---

## 4. Phase 3: Enhanced Embedding Attribute Tests

### 4.1 Unit Tests

```
Suite: Koan.Tests.Data.AI.Unit.EmbeddingMetadataSpec
Location: tests/Suites/Data/AI/Unit/
```

24. **BuildEmbeddingText_AllStrings_ConcatenatesProperties**
    - **Given:** Entity with 3 string properties
    - **When:** `BuildEmbeddingText()` with Policy=AllStrings
    - **Then:** All strings concatenated with spaces
    - **Assertion:** Result == "PropA PropB PropC"

25. **BuildEmbeddingText_Explicit_UsesSpecifiedProperties**
    - **Given:** Entity with Properties=["Title", "Description"]
    - **When:** `BuildEmbeddingText()`
    - **Then:** Only specified properties included
    - **Assertion:** Other properties excluded

26. **BuildEmbeddingText_Template_ReplacesPlaceholders**
    - **Given:** Template="{Title} by {Author}"
    - **When:** `BuildEmbeddingText()`
    - **Then:** Placeholders replaced with actual values
    - **Assertion:** Result == "Book Title by Author Name"

27. **BuildEmbeddingText_FullJson_SerializesEntity**
    - **Given:** Policy=FullJson, MaxDepth=2
    - **When:** `BuildEmbeddingText()`
    - **Then:** JSON string with depth limit
    - **Assertion:** JSON valid, depth ≤ 2

28. **BuildEmbeddingText_FullJson_WithExclusions_OmitsProperties**
    - **Given:** Exclude=["InternalNotes", "PasswordHash"]
    - **When:** FullJson serialization
    - **Then:** Excluded properties not in JSON
    - **Assertion:** JSON.parse().InternalNotes == undefined

29. **EstimateTokens_EnglishText_ReturnsReasonableEstimate**
    - **Given:** 400-character English text
    - **When:** `EstimateTokens()` called
    - **Then:** Estimate ~100 tokens (4 chars/token)
    - **Assertion:** Result between 80-120 (±20% tolerance)

30. **TruncateToTokenLimit_ExceedsLimit_TruncatesAtWordBoundary**
    - **Given:** 1000-character text, MaxTokens=100 (~400 chars)
    - **When:** Truncation applied
    - **Then:** Truncated at last space before 400 chars
    - **Assertion:** Result ends with "..." and complete word

31. **TruncateToTokenLimit_JsonPolicy_PreservesStructure**
    - **Given:** Large JSON exceeding MaxTokens
    - **When:** Truncation applied
    - **Then:** Valid JSON returned (removes verbose fields)
    - **Assertion:** JSON.parse() succeeds, key fields present

32. **ComputeSignature_IncludesVersion_VersionChange**
    - **Given:** Same entity, Version=1 then Version=2
    - **When:** Signatures computed
    - **Then:** Different signatures
    - **Assertion:** sig1 != sig2

33. **ComputeSignature_SameContent_SameVersion_Stable**
    - **Given:** Entity saved twice with same content
    - **When:** Signatures computed
    - **Then:** Identical signatures
    - **Assertion:** sig1 == sig2

```
Suite: Koan.Tests.Data.AI.Unit.EntityEmbeddingExtensionsSpec
```

34. **ApplyEmbedding_WithSource_UsesClientContext**
    - **Given:** [Embedding(Source="openai-prod")]
    - **When:** Entity saved
    - **Then:** `Client.Context(source: "openai-prod")` applied
    - **Assertion:** Correct AI service invoked

35. **ApplyEmbedding_ContentUnchanged_SkipsRegeneration**
    - **Given:** Entity with existing embedding, no changes
    - **When:** Entity saved again
    - **Then:** Embedding generation skipped
    - **Assertion:** AI service not called

36. **ApplyEmbedding_VersionIncremented_RegeneratesEmbedding**
    - **Given:** Entity with Version=1 embedding, attribute now Version=2
    - **When:** Entity saved
    - **Then:** New embedding generated
    - **Assertion:** EmbeddingState.Version == 2

### 4.2 Integration Tests

```
Suite: Koan.Tests.Data.AI.Integration.EmbeddingLifecycleSpec
```

37. **Entity_WithEmbeddingAttribute_AutoGeneratesOnSave**
    - **Setup:** Real database + mock AI service
    - **Given:** [Embedding] entity
    - **When:** `entity.Save()` called
    - **Then:** Embedding generated and stored
    - **Assertion:** Vector exists in vector DB

38. **Entity_AsyncEmbedding_QueuesJob**
    - **Given:** [Embedding(Async=true)]
    - **When:** Entity saved
    - **Then:** EmbedJob created, entity saves immediately
    - **Assertion:** Job status == Pending

39. **Entity_MaxTokensExceeded_LogsWarning**
    - **Setup:** Development environment
    - **Given:** [Embedding(MaxTokens=100)], large entity
    - **When:** Entity saved
    - **Then:** Warning logged with truncation preview
    - **Assertion:** Log contains "exceeds 100 tokens"

---

## 5. Phase 4: Production Guardrails Tests

### 5.1 Unit Tests

```
Suite: Koan.Tests.Data.AI.Unit.Telemetry.EmbeddingTelemetrySpec
Location: tests/Suites/Data/AI/Unit/Specs/Telemetry/
```

40. **RecordEmbeddingGeneration_Success_IncrementsCounters**
    - **Given:** Telemetry instance
    - **When:** `RecordEmbeddingGeneration()` called with success=true
    - **Then:** Counters incremented, histogram recorded
    - **Assertion:**
      ```csharp
      GetMetric("koan.embeddings.generated.total") == 1
      GetMetric("koan.embeddings.errors.total") == 0
      ```

41. **RecordEmbeddingGeneration_Failure_IncrementsErrorCounter**
    - **When:** Recorded with success=false
    - **Then:** Error counter incremented
    - **Assertion:** Error counter == 1, generated counter == 1

42. **CalculateStats_MultipleEntries_ComputesPercentiles**
    - **Given:** 100 embedding operations with varying latencies
    - **When:** `CalculateStats(period: 1h)` called
    - **Then:** P50, P95, P99 computed correctly
    - **Assertion:** P50 < P95 < P99, values reasonable

43. **GetEmbeddingMetrics_OldEntries_Excluded**
    - **Given:** Metrics from 25 hours ago + 1 hour ago
    - **When:** `GetEmbeddingMetrics(since: now - 24h)`
    - **Then:** Only recent entries returned
    - **Assertion:** Count == 1 (old entry excluded)

44. **UpdateQueueState_UpdatesGauges**
    - **Given:** Queue with 10 pending, 2 failed
    - **When:** `UpdateQueueState()` called
    - **Then:** Observable gauges updated
    - **Assertion:** Pending gauge reads 10, failed gauge reads 2

```
Suite: Koan.Tests.Data.AI.Unit.Telemetry.EmbeddingCostEstimatorSpec
```

45. **EstimateCost_KnownModel_ReturnsAccurateCost**
    - **Given:** model="text-embedding-3-small", tokens=1_000_000
    - **When:** `EstimateCost()` called
    - **Then:** Cost == $0.02 (correct pricing)
    - **Assertion:** Math.Abs(result - 0.02) < 0.001

46. **EstimateCost_LocalProvider_ReturnsZero**
    - **Given:** provider="ollama"
    - **When:** `EstimateCost()` called
    - **Then:** Cost == 0.0
    - **Assertion:** Result == 0.0

47. **EstimateCost_UnknownModel_ReturnsZero**
    - **Given:** model="unknown-model-xyz"
    - **When:** `EstimateCost()` called
    - **Then:** Cost == 0.0 (unknown models default to 0)
    - **Assertion:** Result == 0.0

```
Suite: Koan.Tests.Data.AI.Unit.Health.EmbeddingHealthCheckSpec
```

48. **CheckHealthAsync_HealthySystem_ReturnsHealthy**
    - **Given:** Error rate < 5%, queue age < 5 min
    - **When:** Health check executed
    - **Then:** Status == Healthy
    - **Assertion:** `result.Status == HealthStatus.Healthy`

49. **CheckHealthAsync_HighErrorRate_ReturnsDegraded**
    - **Given:** Error rate 8% (between 5-20%)
    - **When:** Health check executed
    - **Then:** Status == Degraded
    - **Assertion:** Status == Degraded, data contains error rate

50. **CheckHealthAsync_VeryHighErrorRate_ReturnsUnhealthy**
    - **Given:** Error rate 25% (>20%)
    - **When:** Health check executed
    - **Then:** Status == Unhealthy
    - **Assertion:** Status == Unhealthy

51. **CheckHealthAsync_OldQueue_ReturnsDegraded**
    - **Given:** Oldest pending job 10 minutes old
    - **When:** Health check executed
    - **Then:** Status == Degraded
    - **Assertion:** Data contains queue age warning

```
Suite: Koan.Tests.Data.AI.Unit.Migration.EmbeddingMigratorSpec
```

52. **ReEmbedAll_SmallBatch_ProcessesAllEntities**
    - **Given:** 5 entities, batchSize=2
    - **When:** `ReEmbedAll<T>()` called
    - **Then:** All 5 entities re-embedded
    - **Assertion:** result.TotalEntities == 5, SuccessfulEntities == 5

53. **ReEmbedAll_WithFailures_TracksPartialSuccess**
    - **Given:** 10 entities, 3 fail during re-embedding
    - **When:** Migration executed
    - **Then:** 7 successful, 3 failed tracked
    - **Assertion:** SuccessfulEntities == 7, FailedEntities == 3

54. **ReEmbedAll_TargetModel_UpdatesEmbeddingState**
    - **Given:** targetModel="text-embedding-3-large"
    - **When:** Entity re-embedded
    - **Then:** EmbeddingState.Model updated
    - **Assertion:** state.Model == "text-embedding-3-large"

55. **ExportEmbeddings_ValidPath_CreatesJsonFile**
    - **Given:** 100 embedding states
    - **When:** `ExportEmbeddings()` called
    - **Then:** JSON file created with all states
    - **Assertion:** File.Exists(path), JSON array length == 100

56. **CleanupOrphanedStates_NoOrphans_ReturnsZero**
    - **Given:** All embedding states have valid entities
    - **When:** Cleanup executed
    - **Then:** No states removed
    - **Assertion:** result == 0

57. **CleanupOrphanedStates_WithOrphans_RemovesStates**
    - **Given:** 3 embedding states for deleted entities
    - **When:** Cleanup executed
    - **Then:** 3 states removed
    - **Assertion:** result == 3

### 5.2 Integration Tests

```
Suite: Koan.Tests.Data.AI.Integration.TelemetrySpec
```

58. **EmbeddingWorker_ProcessesBatch_RecordsTelemetry**
    - **Setup:** Real telemetry service + in-memory DB
    - **Given:** 10 pending embedding jobs
    - **When:** Worker processes batch
    - **Then:** Telemetry metrics recorded
    - **Assertion:** GetMetric("koan.embeddings.generated.total") == 10

59. **HealthCheck_RealWorker_ReturnsAccurateStatus**
    - **Setup:** EmbeddingWorker running with jobs
    - **Given:** System under normal load
    - **When:** Health check invoked
    - **Then:** Status reflects actual system state
    - **Assertion:** Status matches queue/error metrics

```
Suite: Koan.Tests.Data.AI.Integration.MigrationSpec
```

60. **ReEmbedAll_RealDatabase_MigratesSuccessfully**
    - **Setup:** SQL + Qdrant with 100 entities
    - **Given:** Entities with Ollama embeddings
    - **When:** Migrate to OpenAI
    - **Then:** All re-embedded, vector DB updated
    - **Assertion:** Vector similarity search works with new embeddings

---

## 6. Phase 5: Documentation & Samples Tests

### 6.1 Documentation Tests

```
Suite: Koan.Tests.Docs.EmbeddingDocumentationSpec
Location: tests/Suites/Docs/
```

61. **HowToGuide_CodeSamples_Compile**
    - **Given:** Code blocks from docs/how-to/embeddings.md
    - **When:** Extracted and compiled
    - **Then:** All samples compile successfully
    - **Assertion:** 0 compilation errors

62. **HowToGuide_Links_Valid**
    - **Given:** All markdown links in guide
    - **When:** Links validated
    - **Then:** All targets exist
    - **Assertion:** No 404 links

### 6.2 Sample Application Tests

```
Suite: Koan.Tests.Samples.S5Recs.EmbeddingEndpointsSpec
Location: tests/Samples/S5.Recs/
```

63. **AdminEndpoint_GetMetrics_ReturnsValidJson**
    - **Given:** S5.Recs running with telemetry
    - **When:** GET /admin/embeddings/metrics
    - **Then:** Valid JSON with expected structure
    - **Assertion:** Response contains performance, cost, queue sections

64. **AdminEndpoint_TriggerMigration_StartsJob**
    - **Given:** Media entities exist
    - **When:** POST /admin/embeddings/migrate
    - **Then:** Migration executes, returns result
    - **Assertion:** Response.success == true, totalEntities > 0

65. **EmbeddingMonitoringService_Startup_LogsMetrics**
    - **Given:** Service registered in DI
    - **When:** Application starts, waits 1 second
    - **Then:** Initial metrics logged
    - **Assertion:** Log contains "EmbeddingMonitoringService started"

---

## 7. Cross-Cutting Concerns Tests

### 7.1 Concurrency Tests

```
Suite: Koan.Tests.Data.AI.Stress.ConcurrencySpec
```

66. **ParallelTransactions_MultipleThreads_NoDeadlocks**
    - **Given:** 10 threads each starting transaction with vector ops
    - **When:** All execute concurrently
    - **Then:** All complete successfully without deadlock
    - **Assertion:** No timeout exceptions, all operations committed

67. **EmbeddingWorker_ConcurrentBatches_NoRaceConditions**
    - **Given:** Multiple worker instances processing same queue
    - **When:** Jobs processed in parallel
    - **Then:** No duplicate processing
    - **Assertion:** Each job processed exactly once

### 7.2 Performance Tests

```
Suite: Koan.Tests.Data.AI.Performance.PerformanceSpec
```

68. **Transaction_1000Vectors_CompletesUnder5Seconds**
    - **Given:** Transaction with 1000 vector save operations
    - **When:** Commit executed
    - **Then:** Completes within 5 seconds
    - **Assertion:** Elapsed time < 5000ms

69. **EmbeddingGeneration_100Entities_BatchedEfficiently**
    - **Given:** 100 entities to embed, batchSize=20
    - **When:** Worker processes queue
    - **Then:** Completes in reasonable time (< 30s with mock)
    - **Assertion:** Batches processed sequentially, no blocking

### 7.3 Memory Leak Tests

```
Suite: Koan.Tests.Data.AI.Stress.MemoryLeakSpec
```

70. **Telemetry_LongRunning_NoMemoryLeak**
    - **Given:** Telemetry running for 1 hour (simulated)
    - **When:** 100K metrics recorded
    - **Then:** Memory usage stable (old metrics evicted)
    - **Assertion:** Memory growth < 100MB

71. **Transaction_RepeatedRollbacks_ReleasesMemory**
    - **Given:** 1000 transactions, each rolled back
    - **When:** All executed
    - **Then:** Memory released after each rollback
    - **Assertion:** GC collections reclaim memory

---

## 8. Test Data & Fixtures

### 8.1 Entity Fixtures

```csharp
// tests/Shared/Koan.Testing/Fixtures/AI/EmbeddingTestEntities.cs

[Embedding(Policy = EmbeddingPolicy.AllStrings)]
public class SimpleArticle : Entity<SimpleArticle>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

[Embedding(
    Template = "{Title} by {Author}: {Abstract}",
    MaxTokens = 100,
    Version = 1)]
public class ResearchPaper : Entity<ResearchPaper>
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Abstract { get; set; } = "";
}

[Embedding(
    Policy = EmbeddingPolicy.FullJson,
    MaxTokens = 500,
    MaxDepth = 2,
    Exclude = new[] { "InternalNotes" })]
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public ProductCategory Category { get; set; } = null!;
    public string InternalNotes { get; set; } = "";
}

[Embedding(
    Source = "openai-test",
    Model = "text-embedding-3-small",
    Async = true)]
public class AsyncEntity : Entity<AsyncEntity>
{
    public string Text { get; set; } = "";
}
```

### 8.2 Mock AI Services

```csharp
// tests/Shared/Koan.Testing/Mocks/MockAiService.cs

public class MockAiService : IAi
{
    public List<(string Text, string Model)> EmbedCalls { get; } = new();
    public Dictionary<string, float[]> EmbeddingResponses { get; } = new();

    public async Task<float[]> Embed(string text, string? model, CancellationToken ct)
    {
        EmbedCalls.Add((text, model ?? "default"));

        if (EmbeddingResponses.TryGetValue(text, out var response))
            return response;

        // Default: Generate deterministic embedding from text hash
        var hash = text.GetHashCode();
        var embedding = new float[1536]; // Standard dimension
        for (int i = 0; i < embedding.Length; i++)
            embedding[i] = (hash + i) % 100 / 100f;

        return embedding;
    }
}
```

### 8.3 Test Database Helpers

```csharp
// tests/Shared/Koan.Testing/Helpers/TestDatabaseHelper.cs

public static class TestDatabaseHelper
{
    public static async Task<IDisposable> CreateTestDatabaseAsync()
    {
        // Creates isolated SQL + Vector DB for test
        var dbName = $"test_{Guid.NewGuid():N}";
        // ... setup logic
        return new DatabaseCleanup(dbName);
    }

    public static async Task SeedEmbeddingTestData<TEntity>(int count)
        where TEntity : IEntity<string>, new()
    {
        for (int i = 0; i < count; i++)
        {
            var entity = new TEntity
            {
                Id = Guid.CreateVersion7().ToString()
                // ... set properties
            };
            await entity.Save();
        }
    }
}
```

---

## 9. Test Execution Strategy

### 9.1 CI/CD Pipeline

```yaml
# .github/workflows/test-adr-ai-0020.yml

name: ADR AI-0020 Test Suite

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Run Unit Tests
        run: dotnet test --filter "Category=Unit&ADR=AI-0020"
      - name: Upload Coverage
        run: dotnet test --collect:"XPlat Code Coverage"

  integration-tests:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
      qdrant:
        image: qdrant/qdrant:latest
    steps:
      - name: Run Integration Tests
        run: dotnet test --filter "Category=Integration&ADR=AI-0020"

  e2e-tests:
    runs-on: ubuntu-latest
    steps:
      - name: Start S5.Recs
        run: cd samples/S5.Recs && dotnet run &
      - name: Run E2E Tests
        run: dotnet test --filter "Category=E2E&ADR=AI-0020"
```

### 9.2 Local Development

```bash
# Run all ADR AI-0020 tests
dotnet test --filter "ADR=AI-0020"

# Run by phase
dotnet test --filter "ADR=AI-0020&Phase=1"  # Transaction tests
dotnet test --filter "ADR=AI-0020&Phase=2"  # Pipeline tests
dotnet test --filter "ADR=AI-0020&Phase=3"  # Embedding tests
dotnet test --filter "ADR=AI-0020&Phase=4"  # Guardrails tests

# Run by type
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=Performance"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
reportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage/report
```

---

## 10. Coverage Targets

### 10.1 Code Coverage by Component

| Component | Target | Rationale |
|-----------|--------|-----------|
| TransactionCoordinator | 100% | Critical path, data consistency risk |
| TrackedOperations | 100% | Reflection logic, high complexity |
| VectorData | 95% | Core coordination logic |
| EmbeddingMetadata | 90% | Business logic, edge cases |
| EmbeddingWorker | 85% | Background service, complex flow |
| Pipeline API | 90% | User-facing API, type safety |
| Telemetry | 80% | Observability, non-critical failures acceptable |
| Migration Tools | 85% | Operational tools, error handling important |
| Health Checks | 90% | Production monitoring |

### 10.2 Mutation Testing

Run Stryker.NET to validate test effectiveness:

```bash
dotnet stryker --project "Koan.Data.Core" --test-projects "Koan.Tests.Data.Core"
dotnet stryker --project "Koan.AI" --test-projects "Koan.Tests.AI.Unit"
```

**Target:** 80% mutation score (80% of code mutations caught by tests)

---

## 11. Test Maintenance

### 11.1 Test Review Cadence

- **Weekly:** Review failing tests, update fixtures
- **Per-Sprint:** Add tests for new features
- **Quarterly:** Performance regression testing
- **Annually:** Full test suite audit, refactor duplicates

### 11.2 Test Ownership

| Phase | Owner | Backup |
|-------|-------|--------|
| Phase 1 (Transactions) | Data Team | QA Lead |
| Phase 2 (Pipeline API) | AI Team | QA Lead |
| Phase 3 (Embeddings) | AI Team | Data Team |
| Phase 4 (Guardrails) | Platform Team | QA Lead |
| Phase 5 (Docs/Samples) | DevRel Team | QA Lead |

---

## 12. Acceptance Criteria

### 12.1 Definition of Done

Tests are complete when:

- [ ] All 71 test cases implemented and passing
- [ ] Code coverage ≥95% for critical paths
- [ ] Integration tests pass with real databases
- [ ] Performance tests meet SLAs (see section 7.2)
- [ ] No memory leaks detected in stress tests
- [ ] Documentation code samples verified
- [ ] CI/CD pipeline green
- [ ] Mutation score ≥80%

### 12.2 Sign-Off

- [ ] QA Senior Analyst: _______________________
- [ ] Framework Architect: _____________________
- [ ] Team Lead: ______________________________
- [ ] Date: ___________________________________

---

## Appendix A: Risk Matrix

| Risk | Likelihood | Impact | Mitigation | Test IDs |
|------|------------|--------|------------|----------|
| Transaction partial commit | Medium | Critical | Extensive integration tests | 12-14 |
| Memory leak in telemetry | Low | High | Long-running stress tests | 70 |
| Cost tracking inaccuracy | Medium | Medium | Unit tests with known pricing | 45-47 |
| Reflection failure (circular dep) | Low | Critical | Unit + integration tests | 10-11 |
| Race condition in worker | Low | High | Concurrency tests | 66-67 |
| Token estimation inaccuracy | High | Low | Boundary tests, multiple languages | 29 |

## Appendix B: Test Doubles Strategy

- **Mocks:** AI services, external APIs (full control)
- **Stubs:** Simple data providers (minimal behavior)
- **Fakes:** In-memory databases for fast unit tests
- **Spies:** Telemetry verification (call tracking)
- **Real:** SQL/Vector databases in integration tests

---

**End of Test Strategy Document**
