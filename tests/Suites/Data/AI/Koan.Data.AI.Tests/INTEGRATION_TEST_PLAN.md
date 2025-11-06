# Integration Test Plan: Koan.Data.AI

**Purpose**: Define comprehensive integration tests for attribute-driven AI embeddings
**Target**: Separate integration test project with real infrastructure
**Estimated Effort**: 12-16 hours

---

## Overview

Unit tests validate logic without dependencies. **Integration tests validate the complete workflow** with:
- ✅ Real AI embedding service (or test doubles)
- ✅ Real vector database (in-memory or containerized)
- ✅ Real entity repository
- ✅ Background worker processing
- ✅ End-to-end scenarios

---

## Test Categories

### 1. Phase 2: Semantic Search Integration Tests

**Infrastructure Required**: AI service + Vector DB + Entity repository

#### Test: `SemanticSearch_WithValidQuery_ReturnsRelevantResults`
```csharp
// Arrange
await SeedTestDocuments(new[] {
    new TestDocument { Id = "1", Title = "Machine Learning Basics" },
    new TestDocument { Id = "2", Title = "Deep Learning Advanced" },
    new TestDocument { Id = "3", Title = "Cooking Recipes" }
});

// Act
var results = await EntityEmbeddingExtensions.SemanticSearch<TestDocument>(
    query: "artificial intelligence",
    limit: 2);

// Assert
results.Should().HaveCount(2);
results[0].Id.Should().BeOneOf("1", "2"); // ML-related docs
results.Should().NotContain(r => r.Id == "3"); // Cooking excluded
```

#### Test: `SemanticSearch_WithThreshold_FiltersLowScores`
```csharp
var results = await EntityEmbeddingExtensions.SemanticSearch<TestDocument>(
    query: "machine learning",
    threshold: 0.8);

// All results should have similarity >= 0.8
results.Should().OnlyContain(r => GetSimilarityScore(r) >= 0.8);
```

#### Test: `SemanticSearch_WithPartition_ScopesToPartition`
```csharp
// Seed: Partition "tenant-1" and "tenant-2"
var results = await EntityEmbeddingExtensions.SemanticSearch<TestDocument>(
    query: "test",
    partition: "tenant-1");

results.Should().OnlyContain(r => r.Partition == "tenant-1");
```

#### Test: `SemanticSearch_WithZeroLimit_ReturnsEmptyList`
```csharp
var results = await EntityEmbeddingExtensions.SemanticSearch<TestDocument>(
    query: "test", limit: 0);

results.Should().BeEmpty();
```

#### Test: `SemanticSearch_WithNullQuery_ThrowsArgumentNullException`
```csharp
Func<Task> act = async () =>
    await EntityEmbeddingExtensions.SemanticSearch<TestDocument>(query: null!);

await act.Should().ThrowAsync<ArgumentNullException>();
```

#### Test: `FindSimilar_ExcludesSourceEntity_ByDefault`
```csharp
var source = new TestDocument { Id = "source", Title = "Original" };
await source.Save();

var similar = await source.FindSimilar(limit: 10);

similar.Should().NotContain(d => d.Id == "source");
```

#### Test: `FindSimilar_IncludesSourceEntity_WhenRequested`
```csharp
var source = new TestDocument { Id = "source", Title = "Original" };

var similar = await source.FindSimilar(limit: 10, includeSource: true);

similar.Should().Contain(d => d.Id == "source");
```

---

### 2. Phase 3: Background Worker Integration Tests

**Infrastructure Required**: Repository + Background service host

#### Test: `Worker_ProcessesPendingJobs_Automatically`
```csharp
// Arrange
var job = new EmbedJob<TestDocument>
{
    Id = EmbedJob<TestDocument>.MakeId("doc-1"),
    EntityId = "doc-1",
    Status = EmbedJobStatus.Pending,
    EmbeddingText = "test text"
};
await job.Save();

// Act - Start worker and wait
var worker = CreateWorkerWithRealDependencies();
await worker.StartAsync(CancellationToken.None);
await Task.Delay(TimeSpan.FromSeconds(5));

// Assert
var updatedJob = await EmbedJob<TestDocument>.Get(job.Id);
updatedJob.Status.Should().Be(EmbedJobStatus.Completed);
updatedJob.CompletedAt.Should().NotBeNull();
```

#### Test: `Worker_RespectsRateLimits_GlobalAndPerEntity`
```csharp
// Arrange - 100 jobs with 60/min global limit
var jobs = Enumerable.Range(1, 100)
    .Select(i => CreatePendingJob($"doc-{i}"))
    .ToList();

foreach (var job in jobs) await job.Save();

// Act
var startTime = DateTimeOffset.UtcNow;
await RunWorkerUntilComplete();
var endTime = DateTimeOffset.UtcNow;

// Assert - Should take ~2 minutes (100 jobs / 60 per minute)
var duration = endTime - startTime;
duration.TotalMinutes.Should().BeGreaterThanOrEqualTo(1.5);
```

#### Test: `Worker_RetriesFailedJobs_WithExponentialBackoff`
```csharp
// Arrange - Job that fails first 2 times
var job = CreateJobThatFailsTwice();
await job.Save();

// Act
await RunWorkerWithRetries();

// Assert
var finalJob = await EmbedJob<TestDocument>.Get(job.Id);
finalJob.Status.Should().Be(EmbedJobStatus.Completed);
finalJob.RetryCount.Should().Be(2);
```

#### Test: `Worker_MarksAsPermanentFailed_AfterMaxRetries`
```csharp
var job = CreateJobThatAlwaysFails();
await job.Save();

await RunWorkerWithRetries(maxRetries: 3);

var finalJob = await EmbedJob<TestDocument>.Get(job.Id);
finalJob.Status.Should().Be(EmbedJobStatus.FailedPermanent);
finalJob.RetryCount.Should().Be(3);
```

#### Test: `Worker_HandlesCancellation_Gracefully`
```csharp
var cts = new CancellationTokenSource();
var worker = CreateWorker();

await worker.StartAsync(cts.Token);
await Task.Delay(100);
cts.Cancel();

// Should not throw, should stop cleanly
Func<Task> act = async () => await worker.StopAsync(CancellationToken.None);
await act.Should().NotThrowAsync();
```

#### Test: `Worker_CleansUpCompletedJobs_WhenEnabled`
```csharp
// Arrange - Completed job older than retention period
var oldJob = new EmbedJob<TestDocument>
{
    Status = EmbedJobStatus.Completed,
    CompletedAt = DateTimeOffset.UtcNow.AddHours(-25) // 25 hours old
};
await oldJob.Save();

// Act
var options = new EmbeddingWorkerOptions
{
    AutoCleanupCompleted = true,
    CompletedJobRetention = TimeSpan.FromHours(24)
};
await RunWorkerCleanupCycle(options);

// Assert
var deletedJob = await EmbedJob<TestDocument>.Get(oldJob.Id);
deletedJob.Should().BeNull();
```

---

### 3. Admin Commands Integration Tests

**Infrastructure Required**: Repository with test data

#### Test: `RetryFailed_ResetsStatusAndRetryCount`
```csharp
// Arrange
var failedJob = new EmbedJob<TestDocument>
{
    Status = EmbedJobStatus.Failed,
    RetryCount = 2,
    Error = "Previous error"
};
await failedJob.Save();

// Act
var retriedCount = await EmbedJobExtensions.RetryFailed<TestDocument>();

// Assert
retriedCount.Should().Be(1);
var updatedJob = await EmbedJob<TestDocument>.Get(failedJob.Id);
updatedJob.Status.Should().Be(EmbedJobStatus.Pending);
updatedJob.RetryCount.Should().Be(0);
updatedJob.Error.Should().BeNull();
```

#### Test: `PurgeCompleted_RemovesOnlyOldJobs`
```csharp
// Arrange
await CreateCompletedJob(completedAt: DateTimeOffset.UtcNow.AddHours(-25)); // Old
await CreateCompletedJob(completedAt: DateTimeOffset.UtcNow.AddHours(-1));  // Recent

// Act
var purgedCount = await EmbedJobExtensions.PurgeCompleted<TestDocument>(
    olderThan: TimeSpan.FromHours(24));

// Assert
purgedCount.Should().Be(1);
var remaining = await EmbedJob<TestDocument>.All();
remaining.Should().HaveCount(1);
```

#### Test: `GetStats_CalculatesAccurateMetrics`
```csharp
// Arrange - Known distribution
await CreateJobs(
    pending: 10,
    processing: 5,
    completed: 80,
    failed: 3,
    failedPermanent: 2);

// Act
var stats = await EmbedJobExtensions.GetStats<TestDocument>();

// Assert
stats.TotalJobs.Should().Be(100);
stats.CompletedCount.Should().Be(80);
stats.PendingCount.Should().Be(10);
// Verify percentage calculations
var successRate = (double)stats.CompletedCount / stats.TotalJobs * 100;
successRate.Should().Be(80.0);
```

#### Test: `GetFailedJobs_ReturnsDetailsWithErrors`
```csharp
await CreateFailedJob("job-1", error: "Timeout");
await CreateFailedJob("job-2", error: "Rate limit");

var failedJobs = await EmbedJobExtensions.GetFailedJobs<TestDocument>(limit: 10);

failedJobs.Should().HaveCount(2);
failedJobs.Should().OnlyContain(j => !string.IsNullOrEmpty(j.Error));
failedJobs.Should().Contain(j => j.Error.Contains("Timeout"));
```

---

### 4. End-to-End Workflow Tests

#### Test: `E2E_SaveEntity_AutoGeneratesEmbedding_SearchFinds`
```csharp
// Arrange
var doc = new TestAsyncDocument
{
    Id = "e2e-test",
    Title = "Machine Learning Tutorial",
    Content = "Learn about neural networks"
};

// Act - Save triggers embedding generation
await doc.Save();

// Wait for async processing
await WaitForJobCompletion("e2e-test", timeout: TimeSpan.FromSeconds(30));

// Search for similar content
var results = await EntityEmbeddingExtensions.SemanticSearch<TestAsyncDocument>(
    query: "AI and deep learning",
    limit: 5);

// Assert
results.Should().Contain(d => d.Id == "e2e-test");
```

#### Test: `E2E_UpdateEntity_RegeneratesEmbedding_OnlyWhenContentChanges`
```csharp
// Arrange
var doc = new TestDocument { Id = "update-test", Title = "Original" };
await doc.Save();
var originalJobId = EmbedJob<TestDocument>.MakeId("update-test");
var originalJob = await EmbedJob<TestDocument>.Get(originalJobId);
var originalSignature = originalJob.ContentSignature;

// Act 1 - Update with same content signature
doc.Title = "Original"; // No change
await doc.Save();
await Task.Delay(1000);

// Assert 1 - No new job created
var job1 = await EmbedJob<TestDocument>.Get(originalJobId);
job1.ContentSignature.Should().Be(originalSignature);

// Act 2 - Update with different content
doc.Title = "Modified Content";
await doc.Save();
await WaitForJobCompletion("update-test");

// Assert 2 - New embedding generated
var job2 = await EmbedJob<TestDocument>.Get(originalJobId);
job2.ContentSignature.Should().NotBe(originalSignature);
```

---

### 5. Performance Tests

#### Test: `Performance_EmbedLargeDataset_CompletesInReasonableTime`
```csharp
// Arrange - 10,000 entities
var entities = Enumerable.Range(1, 10000)
    .Select(i => new TestDocument
    {
        Id = $"perf-{i}",
        Title = $"Document {i}",
        Content = GenerateRandomContent()
    })
    .ToList();

// Act
var startTime = DateTimeOffset.UtcNow;

foreach (var entity in entities)
{
    await entity.Save();
}

await WaitForAllJobsComplete(timeout: TimeSpan.FromMinutes(30));

var endTime = DateTimeOffset.UtcNow;
var duration = endTime - startTime;

// Assert - Should complete within reasonable time
duration.TotalMinutes.Should().BeLessThan(30);

// Verify all embeddings generated
var stats = await EmbedJobExtensions.GetStats<TestDocument>();
stats.CompletedCount.Should().Be(10000);
```

#### Test: `Performance_ConcurrentMetadataAccess_NoDeadlocks`
```csharp
// Simulate 1000 concurrent requests
var tasks = Enumerable.Range(0, 1000)
    .Select(_ => Task.Run(() =>
    {
        var metadata = EmbeddingMetadata.Get<TestDocument>();
        return metadata.BuildEmbeddingText(CreateTestDoc());
    }))
    .ToArray();

// Should complete without deadlocks
Func<Task> act = async () => await Task.WhenAll(tasks);
await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
```

---

## Test Infrastructure Setup

### Required Test Doubles

1. **Mock AI Embedding Service**
```csharp
public class MockAiEmbeddingProvider : IAiEmbeddingProvider
{
    public Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        // Generate deterministic embedding from text hash
        var hash = text.GetHashCode();
        return Task.FromResult(GenerateEmbeddingVector(hash));
    }
}
```

2. **In-Memory Vector Database**
```csharp
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorEntry> _vectors = new();

    public Task<VectorSearchResult> Search(
        float[] query,
        int topK,
        CancellationToken ct = default)
    {
        // Cosine similarity search in memory
        var results = _vectors.Values
            .Select(v => new { v.Id, Score = CosineSimilarity(query, v.Vector) })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(new VectorSearchResult { Matches = results });
    }
}
```

3. **Test Container Setup** (using Testcontainers)
```csharp
[Collection("Integration Tests")]
public class IntegrationTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder().Build();
        await _postgresContainer.StartAsync();

        _redisContainer = new RedisBuilder().Build();
        await _redisContainer.StartAsync();

        // Initialize repositories with test containers
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer != null)
            await _postgresContainer.DisposeAsync();
        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
    }
}
```

---

## Test Execution Strategy

### Local Development
```bash
# Unit tests only (fast, no infrastructure)
dotnet test --filter "FullyQualifiedName~.Tests.Phase1|Phase2|Phase3"

# Integration tests with test containers
dotnet test --filter "Category=Integration"
```

### CI/CD Pipeline
```yaml
- name: Unit Tests
  run: dotnet test --filter "Category!=Integration"

- name: Integration Tests
  run: |
    docker-compose -f docker-compose.test.yml up -d
    dotnet test --filter "Category=Integration"
    docker-compose -f docker-compose.test.yml down
```

---

## Success Criteria

✅ **All integration tests pass** with real infrastructure
✅ **E2E workflow test** demonstrates full functionality
✅ **Performance test** completes 10K entities < 30 minutes
✅ **No flaky tests** (all pass consistently)
✅ **Clear test output** with timing and resource usage

**When complete**: Production-ready with confidence in real-world behavior.
