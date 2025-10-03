---
type: DEV
domain: samples
title: "Adapter benchmark sample with multi-provider performance testing"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-30
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-30
  status: proposed
  scope: samples/SX.AdapterBench
---

# DX-0044: Adapter Benchmark Sample with Multi-Provider Performance Testing

Status: Accepted

## Context

Users need objective data to select appropriate storage providers for their workloads. While the framework provides transparent multi-provider support through Entity<T> patterns, performance characteristics vary significantly across SQLite, PostgreSQL, MongoDB, Redis, and Couchbase. Additionally, the framework's capability detection system means some operations are pushed down to providers natively while others fall back to in-memory processing.

Without benchmarking tools, users:
- Cannot compare provider performance for their specific workload patterns
- Don't understand the cost of framework fallbacks vs native operations
- Lack insight into how the multi-provider context-switching feature performs
- Miss guidance on provider selection for read-heavy vs write-heavy scenarios

## Decision

Create a new sample `SX.AdapterBench` (where X = next available sample number) that provides:

### 1. Benchmark Test Matrix

**Entity Complexity Tiers:**
- **Tier 1 - Minimal**: ID + timestamp (baseline overhead)
- **Tier 2 - Indexed**: Business entity with 3-5 indexed properties
- **Tier 3 - Complex**: Document-style with nested objects, collections, metadata

**Test Categories:**
- **Write Performance**: Single saves, batch saves (100/500/1000), bulk operations (10k)
- **Read Performance - By ID**: Single, batch, sequential vs random access
- **Read Performance - Queries**: Full materialization, pagination, streaming, indexed filters, non-indexed filters
- **Mixed Workloads** (optional): Shopping cart (read+update), feed generation (query+paginate), audit logging

**Provider Comparison:**
- SQLite (in-process) vs containerized providers (PostgreSQL, MongoDB, Redis, Couchbase)
- Clear UI indicators showing which providers are containerized vs in-process

### 2. Execution Modes

**Sequential Mode (default):**
- Test each provider independently, one at a time
- Shows pure provider performance
- Result: "Provider X completed in Y seconds"

**Parallel Mode:**
- Simultaneously write/read from all providers using `EntityContext.Adapter()`
- Demonstrates multi-provider adapter switching capability
- Shows bottleneck identification and framework coordination overhead
- Result: "All providers completed in Y seconds (bottleneck: Provider X)"

**Test Scale Options:**
- Quick Mode: 1k entities (~1-2 min)
- Standard Mode: 5k entities (~3-5 min)
- Full Mode: 10k entities (~8-10 min)

### 3. Technology Stack

**Backend:**
- ASP.NET Core Web API using canonical Koan patterns
- Entity<T> with GUID v7 for benchmark entities
- SignalR for real-time progress updates during test execution
- Result persistence in SQLite for historical comparison

**Frontend:**
- Single-page app (SPA) hosted in wwwroot (follows S5.Recs pattern)
- Vanilla JavaScript with modular structure
- Chart.js for comparative visualizations
- Real-time progress bars during test execution

**Infrastructure:**
- Docker Compose with all provider containers
- In-process SQLite for comparison
- Consistent resource limits across containers

### 4. Results Presentation

**Per-Category Rankings:**
- Write Speed (inserts/sec)
- Read Speed (reads/sec)
- Query Performance (queries/sec)
- Memory Efficiency (MB per 10k records)

**Use Case Guidance (not a single "winner"):**
- "Best for read-heavy workloads"
- "Best for write-heavy workloads"
- "Best for complex queries"
- "Best for simple key-value lookups"
- "Most balanced"

**Capability Transparency:**
- Visual indicators: ✓ Native execution vs ⚠ Framework fallback
- Performance impact explanation: "Redis in-memory fallback adds 18x overhead vs PostgreSQL native query"
- Educational tooltips explaining why differences exist

**Visualization:**
- Horizontal bar charts for cross-provider comparison
- Provider capability matrix showing what each supports natively
- Historical trend charts (if results are persisted)

### 5. UI/UX Features

**Test Configuration:**
- Toggle: Sequential vs Parallel mode
- Scale selector: Quick/Standard/Full
- Entity tier selector: Run all or specific tiers
- Provider selector: Run all or specific subset

**Live Progress:**
```
Running Tests: Sequential Mode
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ 45%

Current: PostgreSQL - Batch Write (500 records)
Completed: 4,230 / 10,000 writes  |  1,850 writes/sec

✓ SQLite - Single Writes (2.8s)
✓ SQLite - Batch Writes (0.9s)
✓ PostgreSQL - Single Writes (3.2s)
⏳ PostgreSQL - Batch Writes (in progress...)
⋯ MongoDB - Single Writes
⋯ Redis - Single Writes
```

**Results Display:**
- Tabbed interface: Overview / Write Performance / Read Performance / Queries / Analysis
- Downloadable JSON results for external analysis
- Share link for specific test runs

## Rationale

### Educational Value
- Demonstrates framework's multi-provider transparency in action
- Shows real cost of capability fallbacks vs native operations
- Teaches users about provider selection trade-offs

### Framework Validation
- Proves Entity<T> abstraction doesn't add prohibitive overhead
- Validates EntityContext.Adapter() performance for multi-provider routing
- Identifies framework optimization opportunities

### User Decision Support
- Provides objective data for architecture decisions
- Reduces trial-and-error during provider selection
- Shows realistic performance expectations for containerized vs in-process providers

### Follows Framework Patterns
- Uses Entity<T> exclusively (no manual repository pattern)
- Leverages auto-registration via KoanAutoRegistrar
- Demonstrates canonical controller patterns
- Shows proper use of EntityContext.Adapter() for provider switching

## Scope

### In Scope
- Benchmark entities for three complexity tiers
- Sequential and parallel test execution modes
- Write, read, and query performance tests
- Real-time progress via SignalR
- Interactive web UI with Chart.js visualizations
- Docker Compose with all major providers
- Result persistence and historical comparison

### Out of Scope (Phase 1)
- Stress testing with concurrent threads (defer to Phase 2)
- Memory profiling and GC pressure analysis (defer to Phase 2)
- Network latency simulation
- Provider-specific optimization recommendations
- Custom user-defined benchmarks

### Future Enhancements
- Concurrency testing (10 threads × 100 ops each)
- Memory allocation tracking per operation
- Failure resilience testing (what happens when one provider fails)
- Custom benchmark definitions via configuration
- CI/CD integration for regression detection

## Consequences

### Positive
- Users can make informed provider decisions based on real data
- Framework transparency features are showcased effectively
- Demonstrates proper use of Entity<T> and DataSetContext patterns
- Provides regression testing capability for framework performance
- Reduces "which provider should I use?" support questions

### Negative
- Adds ~10 minute test execution time (Full Mode)
- Requires maintaining 5+ provider configurations in Docker Compose
- Results depend heavily on host machine specs (not directly comparable across environments)
- Container networking overhead skews results vs in-process SQLite

### Risks
- Users may misinterpret results (e.g., "Redis is fastest so I'll use it for everything")
- Need clear disclaimers about test environment (containers, local network, etc.)
- Provider configurations must be fair (no artificial throttling or resource starvation)
- Chart.js complexity might require framework upgrades over time

### Mitigations
- Prominent UI disclaimer about test environment and containerization
- Educational tooltips explaining provider strengths/weaknesses
- "Use Case Fit" scoring instead of single "winner"
- Provider capability matrix to set proper expectations
- Documentation emphasizing that benchmarks are relative, not absolute

## Implementation Notes

### Project Structure
```
samples/SX.AdapterBench/
├── Controllers/
│   ├── BenchmarkController.cs       # Test orchestration endpoints
│   └── BenchmarkHub.cs              # SignalR progress hub
├── Models/
│   ├── BenchmarkMinimal.cs          # Tier 1 entity
│   ├── BenchmarkIndexed.cs          # Tier 2 entity
│   └── BenchmarkComplex.cs          # Tier 3 entity
├── Services/
│   ├── IBenchmarkService.cs
│   └── BenchmarkService.cs          # Test execution logic
├── wwwroot/
│   ├── index.html                   # SPA entry point
│   ├── /js/
│   │   ├── benchmark.js             # Test orchestration
│   │   ├── charts.js                # Chart.js rendering
│   │   ├── results.js               # Result display
│   │   └── progress.js              # SignalR progress handling
│   ├── /css/
│   │   └── benchmark.css
│   └── /lib/                        # Chart.js, SignalR client libs
├── docker-compose.yml               # All provider containers
├── start.bat / start.sh
└── Program.cs                       # Standard Koan bootstrap
```

### Sample Numbering
- Assign next available sample number (currently S6-S13 are in use)
- Update samples inventory and port allocation registry

### Port Allocation
- Reserve ports per OPS-0014 standards
- Web UI: Check next available port (likely 7014+)
- Provider ports: Use standard defaults where possible

## Follow-ups

1. Create sample project structure following Koan conventions
2. Implement benchmark entities (three tiers)
3. Build test harness with sequential and parallel modes
4. Create SignalR hub for progress updates
5. Develop frontend with Chart.js visualizations
6. Write Docker Compose configuration with all providers
7. Add educational documentation explaining results
8. Update samples inventory and README

## References

- DX-0042: Narrative samples entity naming (precedent for sample design)
- OPS-0014: Sample port allocation standards
- ARCH-0039: KoanEnv static runtime (used for environment detection)
- DATA-0059: Entity-first facade (canonical pattern to benchmark)
- S5.Recs sample: Reference for wwwroot-hosted SPA pattern
