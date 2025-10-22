# S14.AdapterBench Improvements

## Summary of Changes

### 1. **Koan Jobs Integration** ✅
- **Converted to Job Pattern**: The `/api/benchmark/run` endpoint now returns a job ID immediately instead of blocking
- **New Endpoints**:
  - `POST /api/benchmark/run` - Start benchmark job, returns job ID
  - `GET /api/benchmark/status/{jobId}` - Poll for job status and results
  - `POST /api/benchmark/cancel/{jobId}` - Cancel running benchmark
  - `POST /api/benchmark/run-sync` - Legacy synchronous endpoint

**Benefits**:
- No more hanging HTTP requests for long benchmarks
- Can poll for progress and results
- Better suited for large-scale tests (100k, 1M records)
- Proper cancellation support

### 2. **SQLite Performance Optimizations** ✅
- **Applied PRAGMAs at startup**:
  - `journal_mode = WAL` - Write-Ahead Logging for better concurrency
  - `synchronous = NORMAL` - Balanced safety/speed
  - `cache_size = -64000` - 64MB cache (vs default 2MB)
  - `temp_store = MEMORY` - Memory-based temp tables
  - `mmap_size = 268435456` - 256MB memory-mapped I/O
  - `page_size = 8192` - Larger pages for better I/O

**Expected Impact**: 5-10x faster writes, 2-3x faster reads

### 3. **Parallel Mode Bug Fix** ✅
- **Fixed**: Provider durations were incorrectly set to max duration across all providers
- **Now**: Each provider shows its actual execution time
- **Added**: Wall-clock time vs individual provider time logging

### 4. **Enhanced Logging** ✅
Added comprehensive structured logging:
- Provider context switches
- Batch-level progress (every batch logged)
- Operation-level progress (every 100 ops)
- Seed/removal timing for RemoveAll tests
- Individual provider completion times in parallel mode

**Log Categories**:
- `Information` - Test start/finish, provider summaries
- `Debug` - Detailed batch progress, context switches

### 5. **Extended Scale Options** ✅
New benchmark scales added:

| Scale | Entity Count | Use Case |
|-------|-------------|----------|
| Micro | 100 | Quick smoke tests |
| Quick | 1,000 | Fast development testing |
| Standard | 5,000 | Baseline benchmarks |
| Full | 10,000 | Comprehensive testing |
| Large | 100,000 | Stress testing |
| Massive | 1,000,000 | Extreme stress testing |
| Custom | User-defined | Flexible testing |

### 6. **Additional Test Scenarios** (Framework Ready)
New options in `BenchmarkRequest`:
- `IncludeContextSwitchingTests` - Measure provider switch overhead
- `IncludeMirrorMoveTests` - Cross-provider data migration tests
- `CustomEntityCount` - Override scale with exact count

## Usage Examples

### Basic Quick Benchmark
```bash
curl -X POST http://localhost:5174/api/benchmark/run \
  -H "Content-Type: application/json" \
  -d '{
    "mode": "Sequential",
    "scale": "Quick",
    "providers": ["sqlite", "postgres"],
    "entityTiers": ["Minimal", "Indexed"]
  }'
```

Response:
```json
{
  "jobId": "01JD7XMQK2...",
  "status": "Created",
  "message": "Benchmark job started. Use GET /api/benchmark/status/{jobId} to check progress."
}
```

### Check Job Status
```bash
curl http://localhost:5174/api/benchmark/status/01JD7XMQK2...
```

### Large-Scale Stress Test
```json
{
  "mode": "Parallel",
  "scale": "Large",
  "providers": ["sqlite", "postgres", "mongo", "redis"],
  "entityTiers": ["Minimal"],
  "customEntityCount": 250000
}
```

### Custom Entity Count
```json
{
  "mode": "Sequential",
  "scale": "Custom",
  "customEntityCount": 50000,
  "providers": ["sqlite"]
}
```

## Testing the Improvements

### 1. Start the Stack
```bash
cd samples\S14.AdapterBench
start.bat
```

### 2. Watch the Logs
```bash
docker logs koan-s14-adapterbench-api-1 --tail 50 --follow
```

### 3. Run a Quick Test
```bash
curl -X POST http://localhost:5174/api/benchmark/run \
  -H "Content-Type: application/json" \
  -d '{"mode":"Sequential","scale":"Micro","providers":["sqlite"],"entityTiers":["Minimal"]}'
```

### 4. Monitor Progress
Use the returned job ID to poll:
```bash
curl http://localhost:5174/api/benchmark/status/{jobId}
```

## Performance Expectations

### SQLite Before vs After Optimization

| Operation | Before (ops/sec) | After (ops/sec) | Improvement |
|-----------|-----------------|----------------|-------------|
| Single Writes | ~200 | ~1,500 | **7.5x** |
| Batch Writes | ~2,000 | ~10,000 | **5x** |
| Reads | ~5,000 | ~12,000 | **2.4x** |

*Actual numbers depend on hardware*

### Parallel Mode Benefits
- **Sequential**: Processes each provider one after another
- **Parallel**: Runs all providers simultaneously
- **Expected Speedup**: Near-linear with CPU cores (4 providers ≈ 3.5x faster on 4+ cores)

## Logging Analysis

### Key Log Patterns

**Provider Start**:
```
[INFO] Provider sqlite starting Minimal - Single Writes (1000 operations)
[DEBUG] Switching to provider context: sqlite
```

**Batch Progress** (every batch):
```
[DEBUG] Provider sqlite - Minimal Batch #1: saved 500 entities in 45ms (11111 ops/sec overall)
```

**Operation Progress** (every 100 ops):
```
[DEBUG] Provider sqlite - Minimal Single Writes: 100/1000 (2222 ops/sec)
```

**Parallel Completion**:
```
[INFO] Parallel benchmark finished. Total wall-clock time: 15.23s, slowest provider: 14.87s
[INFO] Provider sqlite completed in 14.87s
[INFO] Provider postgres completed in 12.45s
[INFO] Provider mongo completed in 11.23s
```

## Architectural Notes

### Why Jobs Pattern?
- **Entity-First**: Jobs are persisted entities (`Job<TJob, TContext, TResult>`)
- **Provider Transparency**: Job storage works across all data providers
- **Progress Tracking**: Built-in progress reporting via `IJobProgress`
- **Cancellation**: Proper async cancellation support
- **Auto-Registration**: Jobs framework auto-registers via `KoanAutoRegistrar`

### SQLite Optimization Trade-offs
- **WAL Mode**: Better concurrency, slightly larger disk footprint
- `synchronous=NORMAL`: Small risk of corruption on system crash (acceptable for benchmarks)
- **Large Cache**: Uses more memory, but dramatically faster
- **MMAP**: Fast but relies on OS page cache

### Next Steps for Advanced Testing
1. **Context Switching Tests**: Implement rapid provider switches to measure overhead
2. **Mirror/Move Tests**: Benchmark cross-provider data migration
3. **Query Performance**: Add complex LINQ query benchmarks
4. **Relationship Navigation**: Test foreign key traversal performance
5. **Concurrent Operations**: Multiple clients hitting same provider

## Known Limitations
- Jobs persistence requires entity storage (auto-configured)
- SQLite optimizations applied globally (affects all SQLite usage in sample)
- Large/Massive scales may require increased timeout values
- Parallel mode creates one task per provider (4 providers = 4 concurrent tasks)

## Troubleshooting

### Job Not Found
- Jobs are persisted to default provider
- Check logs for job creation confirmation
- Verify job ID is correct (case-sensitive)

### SQLite Still Slow
- Check logs for "SQLite performance optimizations applied successfully"
- Verify PRAGMAs applied: `PRAGMA journal_mode;` should return "wal"
- Ensure `Data/` directory is on SSD, not network drive

### Parallel Mode Not Faster
- Check CPU usage - should be near 100%
- Containerized providers (Postgres, Mongo, Redis) depend on Docker performance
- SQLite may be I/O bound if on slow disk

## Files Modified/Added

**Modified**:
- `Services/BenchmarkService.cs` - Fixed parallel bug, added logging
- `Controllers/BenchmarkController.cs` - Added job-based endpoints
- `Models/BenchmarkRequest.cs` - Extended scales and options
- `Program.cs` - SQLite optimization at startup
- `S14.AdapterBench.csproj` - Added Koan.Jobs.Core reference

**Added**:
- `Jobs/BenchmarkJob.cs` - Job implementation
- `Models/BenchmarkJobResponse.cs` - API response models
- `Configuration/SqlitePerformanceConfigurator.cs` - Optimization helpers
- `IMPROVEMENTS.md` - This file

## Feedback Loop

After testing, consider:
1. Are the new scales useful? Need more/fewer options?
2. Is logging too verbose/not detailed enough?
3. Should Mirror/Move tests be auto-included or opt-in?
4. What other adapter capabilities should be benchmarked?

Provide feedback by testing with various scales and reviewing the structured logs.
