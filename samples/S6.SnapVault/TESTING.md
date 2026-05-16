# S6.SnapVault Testing Guide

## Transaction Rollback Testing

### Overview
S6.SnapVault uses transaction coordination to ensure atomic commits across entity saves and vector store operations. This prevents orphaned vectors when embedding generation fails.

### Test Scenarios

#### 1. **Happy Path - Successful Transaction Commit**
**Test**: Upload a photo and verify both entity and vector are saved.

```bash
# Upload photo via API
curl -X POST http://localhost:5000/api/photos/upload \
  -F "file=@test-photo.jpg" \
  -F "eventId=test-event-123"

# Verify entity exists
curl http://localhost:5000/api/photos/{photoId}

# Verify vector exists (semantic search returns the photo)
curl http://localhost:5000/api/photos/search?q=test
```

**Expected**: Both entity and vector are present. Semantic search returns the uploaded photo.

---

#### 2. **AI Service Failure - Transaction Rollback**
**Test**: Simulate AI service unavailability during embedding generation.

**Setup**:
1. Stop Ollama service: `docker stop ollama` (or `podman stop ollama`)
2. Upload a photo via API

**Expected Behavior**:
- GenerateAIMetadataAsync() throws exception
- Transaction rollback executes
- Photo entity remains in database with `ProcessingStatus.Failed`
- **No orphaned vector in Weaviate** (critical!)
- Error logged: "Failed to generate AI metadata for photo {PhotoId}, rolling back transaction"

**Verification**:
```bash
# Check photo status
curl http://localhost:5000/api/photos/{photoId}
# Should show ProcessingStatus: "Failed"

# Verify no vector exists
curl http://localhost:5000/api/photos/search?q=test
# Should NOT return the failed photo
```

**Cleanup**:
```bash
# Restart AI service
docker start ollama
```

---

#### 3. **Vector Store Failure - Transaction Rollback**
**Test**: Simulate Weaviate unavailability during vector save.

**Setup**:
1. Stop Weaviate service: `docker stop weaviate`
2. Upload a photo via API

**Expected Behavior**:
- VectorData<PhotoAsset>.SaveWithVector() throws exception
- Transaction rollback executes
- Photo entity NOT saved (entire transaction rolled back)
- Error logged with rollback confirmation

**Verification**:
```bash
# Verify photo entity was NOT saved
curl http://localhost:5000/api/photos/{photoId}
# Should return 404

# Verify no orphaned entity in MongoDB
mongo snapvault --eval "db.PhotoAsset.find({_id: '{photoId}'})"
# Should return empty
```

**Cleanup**:
```bash
# Restart vector store
docker start weaviate
```

---

#### 4. **Vision Analysis Failure - Graceful Degradation**
**Test**: Upload a corrupted/invalid image file.

**Setup**:
1. Create invalid image: `echo "not an image" > corrupt.jpg`
2. Upload via API

**Expected Behavior**:
- GenerateDetailedDescriptionAsync() throws or returns error
- Transaction rolls back gracefully
- Photo entity saved with `ProcessingStatus.Failed`
- Error details logged

---

#### 5. **Regenerate Analysis with Transaction Protection**
**Test**: Verify RegenerateAIAnalysisAsync() maintains transactional safety.

```bash
# Initial upload (should succeed)
curl -X POST http://localhost:5000/api/photos/upload -F "file=@test.jpg"

# Stop AI service
docker stop ollama

# Attempt regeneration (should fail with rollback)
curl -X POST http://localhost:5000/api/photos/{photoId}/regenerate

# Expected: Original analysis preserved, no partial updates
```

---

## Load Testing - Async Embedding Queue

### Test: Concurrent Upload Burst
**Scenario**: Upload 100 photos simultaneously to test async queue behavior.

```bash
# Parallel upload script
for i in {1..100}; do
  curl -X POST http://localhost:5000/api/photos/upload \
    -F "file=@test-$i.jpg" \
    -F "eventId=load-test" &
done
wait

# Monitor embedding queue
curl http://localhost:5000/api/admin/embedding-queue/status
```

**Expected**:
- Photos saved immediately with `ProcessingStatus.InProgress`
- [Embedding] attribute queues work asynchronously
- Background worker processes queue in batches (BatchSize=10)
- Rate limiting prevents AI service overload

---

## Performance Benchmarks

### Before (Manual Orchestration)
```
GenerateAIMetadataAsync():
- Lines of code: ~50
- Manual embedding generation
- Manual VectorData<T>.SaveWithVector()
- No automatic retry on transient failures
```

### After (Attribute-Driven)
```
GenerateAIMetadataAsync():
- Lines of code: ~25 (50% reduction)
- Automatic embedding via [Embedding] attribute
- Lifecycle hook handles vectorization
- Transaction coordination prevents orphaned data
- Async queue supports high-volume scenarios
```

---

## Monitoring & Observability

### Key Metrics to Track

1. **Transaction Success Rate**
   - Metric: `koan.transactions.committed` vs `koan.transactions.rolled_back`
   - Alert: Rollback rate > 5%

2. **Embedding Generation Latency**
   - Metric: `koan.embeddings.generation_time_ms`
   - Alert: P95 > 5000ms

3. **Orphaned Vector Detection**
   - Query: Find vectors without corresponding entities
   - Schedule: Daily reconciliation job
   - Alert: Count > 0

4. **Queue Depth**
   - Metric: `koan.embeddings.queue_depth`
   - Alert: Depth > 1000 (indicates backlog)

### Log Patterns to Monitor

```
✅ Success: "AI metadata generated for photo {PhotoId} (attribute-driven, transactional)"
❌ Failure: "Failed to generate AI metadata for photo {PhotoId}, rolling back transaction"
⚠️  Warning: "Embedding text truncated due to MaxTokens limit (8191)"
```

---

## Manual Testing Checklist

- [ ] Upload photo with valid EXIF metadata
- [ ] Upload photo without EXIF metadata
- [ ] Upload non-image file (should fail gracefully)
- [ ] Upload during AI service outage (transaction rollback)
- [ ] Upload during vector store outage (transaction rollback)
- [ ] Regenerate analysis with locked facts
- [ ] Regenerate analysis during service outage
- [ ] Semantic search returns correct results
- [ ] Hybrid search (alpha=0.5) balances keyword + semantic
- [ ] Queue processing under high load (100+ photos)
- [ ] Transaction logs show commit/rollback correlation IDs

---

## Integration Test Examples

### Pseudo-Code Test Cases

```csharp
[Fact]
public async Task GenerateAIMetadata_WhenAIServiceFails_ShouldRollbackTransaction()
{
    // Arrange
    var photo = CreateTestPhoto();
    var mockAiClient = new Mock<IAiPipeline>();
    mockAiClient.Setup(x => x.Embed(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("AI service unavailable"));

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(() =>
        _service.GenerateAIMetadataAsync(photo));

    // Verify rollback
    var savedPhoto = await PhotoAsset.Get(photo.Id);
    Assert.Equal(ProcessingStatus.Failed, savedPhoto.ProcessingStatus);

    // Verify no orphaned vector
    var vectorExists = await Vector<PhotoAsset>.Exists(photo.Id);
    Assert.False(vectorExists);
}

[Fact]
public async Task GenerateAIMetadata_WhenVectorStoreFails_ShouldRollbackEntity()
{
    // Arrange
    var photo = CreateTestPhoto();
    var mockVectorStore = new Mock<IVectorRepository>();
    mockVectorStore.Setup(x => x.Save(It.IsAny<VectorData>()))
        .ThrowsAsync(new Exception("Weaviate connection timeout"));

    // Act & Assert
    await Assert.ThrowsAsync<Exception>(() =>
        _service.GenerateAIMetadataAsync(photo));

    // Verify entity was NOT saved (full rollback)
    var entityExists = await PhotoAsset.Exists(photo.Id);
    Assert.False(entityExists);
}
```

---

## Continuous Monitoring

### Health Check Endpoint

```bash
curl http://localhost:5000/health

# Expected response:
{
  "status": "Healthy",
  "checks": {
    "mongodb": "Healthy",
    "weaviate": "Healthy",
    "ollama": "Healthy",
    "embedding_queue": {
      "status": "Healthy",
      "depth": 23,
      "processed_today": 1547
    }
  }
}
```

### Daily Reconciliation

```sql
-- Find orphaned vectors (vector exists but entity doesn't)
-- Run daily as maintenance job
SELECT v.id FROM vectors v
LEFT JOIN photo_assets p ON v.id = p.id
WHERE p.id IS NULL;

-- Expected: 0 rows (transaction coordination prevents orphans)
```
