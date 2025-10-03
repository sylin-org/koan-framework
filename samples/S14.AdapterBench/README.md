# S14.AdapterBench - Koan Adapter Benchmark Suite

A comprehensive benchmarking tool for comparing performance across Koan Framework's data adapters: SQLite, PostgreSQL, MongoDB, and Redis.

## 🎯 Purpose

This sample demonstrates:
- **Multi-provider performance comparison** - Objective data for selecting the right storage provider
- **Entity<T> patterns** - Canonical Koan data access patterns with GUID v7 auto-generation
- **Provider transparency** - Same entity code works across all storage backends
- **Capability detection** - Framework fallbacks vs native provider execution
- **Real-time progress tracking** - SignalR-based live updates during benchmark execution
- **Interactive visualization** - Chart.js dashboards with actionable recommendations

## 🚀 Quick Start

### Prerequisites
- Docker Desktop (or Docker + Docker Compose)
- .NET 9.0 SDK (for local development)

### Running the Benchmark

**Windows:**
```bash
start.bat
```

**Linux/Mac:**
```bash
chmod +x start.sh
./start.sh
```

The application will:
1. Build the Docker image
2. Start all provider containers (PostgreSQL, MongoDB, Redis)
3. Launch the web UI at http://localhost:5174

## 📊 Benchmark Modes

### Sequential Mode (Default)
Tests each provider independently, one at a time. Shows pure provider performance without interference.

**Use when:** You want to see individual provider characteristics and capabilities.

### Parallel Mode
Simultaneously executes tests across all providers using `EntityContext.Adapter()`. Demonstrates Koan's multi-provider orchestration capability.

**Use when:** You want to understand bottlenecks in multi-provider scenarios or test real-world polyglot persistence patterns.

## 🔬 Test Categories

### Entity Tiers
1. **Minimal** - ID + timestamp (baseline framework overhead)
2. **Indexed** - Business entity with indexed properties (typical CRUD)
3. **Complex** - Document-style with nested objects (large payload handling)

### Performance Tests
- **Single Writes** - Individual `entity.Save()` calls (10k operations)
- **Batch Writes** - `List<entity>.Save()` with varying batch sizes
- **Read By ID** - Lookup performance with `Entity.Get(id)`

### Future Test Categories (Phase 2)
- Query performance (indexed vs non-indexed filters)
- Pagination and streaming
- Concurrent operations

## 📈 Understanding Results

### Provider Characteristics

**SQLite (In-Process)**
- ✅ Fastest for simple operations (no network overhead)
- ✅ Zero infrastructure setup
- ❌ No multi-process concurrency
- **Best for:** Development, single-user apps, embedded scenarios

**PostgreSQL (Containerized)**
- ✅ Best query optimizer and index support
- ✅ ACID compliance with excellent concurrency
- ✅ Rich feature set (JSON, full-text search, etc.)
- **Best for:** Complex queries, relational data, enterprise applications

**MongoDB (Containerized)**
- ✅ Excellent for document-oriented data
- ✅ Flexible schema evolution
- ✅ Horizontal scaling capabilities
- **Best for:** Document storage, rapidly evolving schemas

**Redis (Containerized)**
- ✅ Fastest in-memory operations
- ✅ Built-in caching and pub/sub
- ❌ Limited query capabilities (framework fallback)
- **Best for:** Caching, session storage, real-time features

### Interpreting Performance Differences

**Network vs In-Process:**
SQLite will appear fastest because it's in-process. Containerized providers incur network latency. This reflects real-world deployment scenarios.

**Native vs Framework Fallback:**
- ✓ Native: Provider executes operation using optimized database features
- ⚠ Fallback: Framework handles operation in-memory (slower but maintains transparency)

**Use Case Recommendations:**
The UI provides guidance based on test results:
- "Best for write-heavy workloads"
- "Best for read-heavy workloads"
- "Best for batch operations"
- "Most balanced"

## 🏗️ Architecture

### Backend (ASP.NET Core + Koan Framework)

```
S14.AdapterBench/
├── Controllers/
│   └── BenchmarkController.cs      # API endpoints
├── Hubs/
│   └── BenchmarkHub.cs             # SignalR real-time progress
├── Models/
│   ├── BenchmarkMinimal.cs         # Tier 1: Minimal entity
│   ├── BenchmarkIndexed.cs         # Tier 2: Indexed entity
│   ├── BenchmarkComplex.cs         # Tier 3: Complex entity
│   ├── BenchmarkRequest.cs         # Request DTOs
│   └── BenchmarkResult.cs          # Result DTOs
├── Services/
│   ├── IBenchmarkService.cs
│   └── BenchmarkService.cs         # Core benchmark logic
└── Program.cs                      # Koan bootstrap
```

### Frontend (Vanilla JS + Chart.js)

```
wwwroot/
├── index.html                      # Main UI
├── css/
│   └── benchmark.css               # Styles
└── js/
    ├── api.js                      # API communication
    ├── progress.js                 # SignalR progress handling
    ├── charts.js                   # Chart.js rendering
    ├── results.js                  # Results analysis & display
    └── app.js                      # Main application logic
```

## 🐳 Docker Infrastructure

**Ports (per OPS-0014):**
- `5174` - Web UI & API
- `5175` - PostgreSQL
- `5176` - MongoDB
- `5177` - Redis

**Data Persistence:**
All provider data is stored in `./data/` for inspection and persistence across runs.

## 🎓 Learning Outcomes

### For Framework Users
- Understand performance characteristics of different storage providers
- Learn when to use sequential vs parallel multi-provider patterns
- See real-world impact of capability detection and fallbacks
- Make informed architecture decisions based on objective data

### For Framework Contributors
- Validate Entity<T> abstraction overhead
- Identify optimization opportunities
- Test EntityContext.Adapter() performance for multi-provider routing
- Regression detection for framework changes

## 🔧 Development

### Running Locally (without Docker)

1. Start required providers manually (PostgreSQL, MongoDB, Redis)
2. Update connection strings in `appsettings.Development.json`
3. Run:
```bash
dotnet run
```

### Adding New Tests

1. Add test method to `BenchmarkService.cs`
2. Update `BenchmarkRequest` and `BenchmarkResult` models
3. Add UI visualization in `charts.js` and `results.js`

### Adding New Providers

1. Add NuGet package reference to `.csproj`
2. Add service to `docker/compose.yml`
3. Update `BenchmarkController.GetProviders()`
4. Add provider color to `charts.js`

## 📚 Related Documentation

- [ADR DX-0044](../../docs/decisions/DX-0044-adapter-benchmark-sample.md) - Architecture Decision Record
- [OPS-0014](../../docs/decisions/OPS-0014-samples-port-allocation.md) - Port allocation scheme
- [Entity<T> Patterns](../../docs/guides/entity-first-development.md)
- [Multi-Provider Patterns](../../docs/guides/multi-provider-patterns.md)

## 🐛 Troubleshooting

**Port conflicts:**
If ports 5174-5177 are in use, update `docker/compose.yml` and `start.bat`/`start.sh`.

**Slow container startup:**
First run downloads provider images. Subsequent runs are faster.

**Provider connection failures:**
Check `docker compose logs` for provider startup issues. MongoDB and PostgreSQL have health checks.

**Benchmark timeout:**
Full mode (10k entities) can take 8-10 minutes. Use Quick mode (1k entities) for faster results.

## 📄 License

Part of the Koan Framework samples. See repository root for license information.
