# Koan Context — Implementation Checklist

**Status:** In Progress
**Started:** 2025-11-05
**Target Completion:** TBD
**Specification:** [Koan-context.md](./Koan-context.md)

---

## Progress Summary

- [ ] **M0: Foundation & Partition Context** (0/12)
- [ ] **M1: Koan.Context App Skeleton** (0/8)
- [ ] **M2: Project Entity & CRUD** (0/9)
- [ ] **M3: Ingest Pipeline** (0/11)
- [ ] **M4: Weaviate Per-Class Provisioning** (0/7)
- [ ] **M5: Retrieval & MCP Tools** (0/10)
- [ ] **M6: Web UI** (0/9)
- [ ] **M7: Security & Polish** (0/8)

**Overall:** 0/74 tasks completed

---

## Milestone 0: Foundation & Partition Context ✅ COMPLETE

**Goal:** Build the ambient partition context mechanism that enables partition-aware Vector<T> operations.
**Status:** Completed 2025-11-05

### Partition Context (Reuse EntityContext)

- [x] **CANCELLED:** IPartitionContextProvider - EntityContext already provides this (DATA-0077)
- [x] **CANCELLED:** AsyncLocalPartitionContextProvider - EntityContext.Current.Partition exists
- [x] **CANCELLED:** Auto-registrar - EntityContext is static, no DI needed
- [x] Updated documentation to reference EntityContext instead of new provider

### Vector Partition Mapper

- [ ] Create `src/Koan.Data.Vector.Abstractions/Partition/IVectorPartitionMapper.cs`
  - [ ] Method `string MapStorageName<T>(string partitionId)`
  - [ ] Method `string SanitizePartitionId(string partitionId)`

- [ ] Create `src/Connectors/Data/Vector/Weaviate/WeaviatePartitionMapper.cs`
  - [ ] Implement class name mapping: `KoanDocument_{sanitizedId}`
  - [ ] Sanitization: lowercase, alphanumeric + hyphen/underscore only
  - [ ] Max length enforcement (Weaviate limit: 256 chars)
  - [ ] Register as singleton in Weaviate auto-registrar

### VectorCapabilities Enhancement

- [ ] Update `src/Koan.Data.Vector.Abstractions/VectorCapabilities.cs`
  - [ ] Add `DynamicCollections = 1 << 10` flag
  - [ ] Update documentation

- [ ] Update `WeaviateVectorRepository.Capabilities`
  - [ ] Include `VectorCapabilities.DynamicCollections`

### Vector<T> Partition Integration

- [ ] Update `src/Koan.Data.Vector/Vector.cs`
  - [ ] Inject `IPartitionContextProvider` via AppHost.Current
  - [ ] Update `Save()` to check partition context before calling repo
  - [ ] Update `Search()` to check partition context
  - [ ] Pass storage name to repo when partition is set

- [ ] Update `src/Koan.Data.Vector/VectorData.cs`
  - [ ] Modify `UpsertManyAsync` to support partition-aware storage name
  - [ ] Modify `SearchAsync` to support partition-aware storage name

### Testing

- [ ] Create `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Partition/PartitionContext.Spec.cs`
  - [ ] Test scope creation and disposal
  - [ ] Test nested scopes
  - [ ] Test async scope preservation

- [ ] Create `tests/Suites/Data/Vector/Koan.Tests.Data.Vector/Specs/PartitionAwareVector.Spec.cs`
  - [ ] Test `Vector.Save()` with partition context
  - [ ] Test `Vector.Search()` with partition context
  - [ ] Test partition isolation (save to A, search in B returns nothing)

---

## Milestone 1: Console App Skeleton

**Goal:** Create Koan.Context as a console app (g1c1.gardencoop pattern) with Web API, Web UI, and MCP endpoints.

### Project Structure

- [ ] Create `src/Koan.Context/Koan.Context.csproj`
  - [ ] OutputType: Exe (console app)
  - [ ] Target: net10.0
  - [ ] References:
    - Koan.Core (hosting & DI)
    - Koan.Web (REST API)
    - Koan.Mcp (MCP endpoints)
    - Koan.AI (embeddings)
    - Koan.Data.Vector (vector façade)
    - Koan.Data.Vector.Connector.Weaviate (adapter + orchestration)

- [ ] Create `src/Koan.Context/Program.cs`
  - [ ] Follow [g1c1.gardencoop pattern](../../samples/guides/g1c1.GardenCoop/Program.cs)
  - [ ] `WebApplication.CreateBuilder(args)`
  - [ ] `builder.Services.AddKoan()` (auto-registration)
  - [ ] `app.MapMcpEndpoints()` (HTTP+SSE transport)
  - [ ] `app.MapControllers()` (REST API)
  - [ ] `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")` (Web UI)
  - [ ] `AppHost.Current = app.Services` (global service access)
  - [ ] Bind to `localhost:27500` by default (port range 27500-27510)
  - [ ] Configure logging (console + file)

- [ ] Create `src/Koan.Context/appsettings.json`
  - [ ] Urls: http://localhost:27500
  - [ ] Koan:Data:Vector:Weaviate:Endpoint: http://localhost:27501
  - [ ] Koan:Orchestration:Weaviate:HostPort: 27501
  - [ ] Reference: [Koan-context-appsettings.json](./Koan-context-appsettings.json)

- [ ] Reference sketch: [Koan-context-program-sketch.cs](./Koan-context-program-sketch.cs)

### MCP Integration

- [ ] Wire MCP HTTP+SSE transport
  - [ ] Map endpoints via `app.MapMcpEndpoints()`
  - [ ] Enable STDIO transport for local CLI usage

- [ ] Create `src/Koan.Context/Mcp/KoanContextMcpAutoRegistrar.cs`
  - [ ] Implement `IKoanInitializer`
  - [ ] Auto-register context.* tools
  - [ ] Add to boot report

- [ ] Test `/mcp/capabilities` endpoint
  - [ ] Returns valid MCP protocol version
  - [ ] Lists registered tools

### Health & Diagnostics

- [ ] Add health check endpoint `/health`
  - [ ] Check Weaviate connectivity
  - [ ] Check partition context provider availability

- [ ] Add `/diagnostics/boot-report` endpoint
  - [ ] Show MCP tools registered
  - [ ] Show vector adapter status
  - [ ] Show partition context status

---

## Milestone 2: Project Entity & CRUD

**Goal:** Implement Project entity with CRUD operations and persistence.

### Entity Definition

- [ ] Create `src/Koan.Context/Models/Project.cs`
  - [ ] Inherit from `Entity<Project, string>`
  - [ ] Properties: Name, Path, DocsPath, LastIndexedCommit, Status, LastIndexed
  - [ ] GUID v7 ID generation via framework default

- [ ] Create `src/Koan.Context/Models/IndexStatus.cs` enum
  - [ ] Values: NotStarted, InProgress, Completed, Failed

- [ ] Add storage adapter attribute (JSON, SQL, or Couchbase)
  - [ ] Use JSON adapter for initial development

### CRUD Operations

- [ ] Implement static methods on Project entity
  - [ ] `Project.Create(name, path, docsPath)` - with validation
  - [ ] `Project.Get(id)` - retrieve by ID
  - [ ] `Project.Query()` - list all projects
  - [ ] `project.Save()` - persist changes
  - [ ] `project.Delete()` - remove project and associated vectors

### REST API

- [ ] Create `src/Koan.Context/Controllers/ProjectsController.cs`
  - [ ] `GET /api/projects` - list all
  - [ ] `GET /api/projects/{id}` - get by ID
  - [ ] `POST /api/projects` - create new
  - [ ] `PUT /api/projects/{id}` - update
  - [ ] `DELETE /api/projects/{id}` - delete

- [ ] Add validation middleware
  - [ ] Path must exist and be readable
  - [ ] DocsPath auto-detection if not provided

### Testing

- [ ] Create integration tests
  - [ ] Test project CRUD lifecycle
  - [ ] Test path validation
  - [ ] Test duplicate project names

---

## Milestone 3: Ingest Pipeline

**Goal:** Build the document discovery, chunking, embedding, and indexing pipeline.

### Document Discovery

- [ ] Create `src/Koan.Context/Services/IDocumentDiscoveryService.cs`
  - [ ] Method `IAsyncEnumerable<DiscoveredFile> DiscoverAsync(string path, string? docsPath)`

- [ ] Implement `DocumentDiscoveryService`
  - [ ] Scan for: README*, docs/**, adrs/**, *.md, CHANGELOG*
  - [ ] Exclude: node_modules, bin, obj, .git (except for commit SHA)
  - [ ] Return file path, relative path, size, last modified

- [ ] Git integration
  - [ ] Detect .git presence
  - [ ] Read HEAD commit SHA for provenance
  - [ ] Store in metadata

### Content Extraction

- [ ] Create `src/Koan.Context/Services/IContentExtractionService.cs`
  - [ ] Method `Task<ExtractedDocument> ExtractAsync(string filePath)`

- [ ] Implement markdown extraction
  - [ ] Parse headings (h1-h6) for title hierarchy
  - [ ] Extract paragraphs
  - [ ] Extract code fences with language tags
  - [ ] Normalize whitespace and line endings

### Tokenization & Chunking

- [ ] Create `src/Koan.Context/Services/IChunkingService.cs`
  - [ ] Method `IAsyncEnumerable<DocumentChunk> ChunkAsync(ExtractedDocument doc, string projectId)`

- [ ] Implement semantic chunking
  - [ ] Use `IAi.TokenizeAsync` to measure token count
  - [ ] Target: 800-1000 tokens per chunk
  - [ ] Overlap: 50 tokens for continuity
  - [ ] Respect heading boundaries
  - [ ] Metadata: projectId, filePath, commitSha, chunkStart, chunkEnd, title

### Embedding

- [ ] Create `src/Koan.Context/Services/IEmbeddingService.cs`
  - [ ] Method `Task<float[]> EmbedAsync(string text)`
  - [ ] Method `Task<Dictionary<string, float[]>> EmbedBatchAsync(Dictionary<string, string> textBatch)`

- [ ] Implement with caching
  - [ ] Use `IAi.EmbedAsync` from configured provider
  - [ ] Cache by SHA256(text + modelId)
  - [ ] Store cache in JSON or in-memory (configurable)

### Vector Indexing

- [ ] Create `src/Koan.Context/Services/IIndexingService.cs`
  - [ ] Method `Task<IndexResult> IndexProjectAsync(string projectId, IProgress<IndexProgress>? progress = null)`

- [ ] Implement indexing orchestrator
  - [ ] Set partition context via `BeginScope(projectId)`
  - [ ] Stream: discover → extract → chunk → embed → save
  - [ ] Bulk save batches (100 chunks at a time)
  - [ ] Progress reporting (files processed, chunks created, vectors saved)
  - [ ] Error handling and partial failure recovery

### Testing

- [ ] Create test fixtures
  - [ ] Sample markdown files with headings, code blocks
  - [ ] Mock IAi embedding service
  - [ ] Containerized Weaviate instance

- [ ] Integration tests
  - [ ] Test discovery finds expected files
  - [ ] Test chunking produces correct token counts
  - [ ] Test embedding caching prevents duplicate API calls
  - [ ] Test vector save creates correct Weaviate class

---

## Milestone 4: Weaviate Per-Class Provisioning

**Goal:** Update Weaviate connector to support dynamic per-project class creation.

### Weaviate Repository Enhancement

- [ ] Update `WeaviateVectorRepository.EnsureSchemaAsync`
  - [ ] Accept optional `className` parameter
  - [ ] Fall back to current naming logic if not provided
  - [ ] Create class with standardized schema for DocumentChunk metadata

- [ ] Update schema definition for Koan Context
  - [ ] Properties: `docId`, `searchText`, `projectId`, `filePath`, `commitSha`, `chunkRange`, `title`, `language`
  - [ ] Enable BM25 on `searchText` for hybrid search
  - [ ] Index `projectId` and `filePath` for filtering

### Partition Mapper Integration

- [ ] Update `Vector<T>` save path
  - [ ] When partition context is set, get storage name from mapper
  - [ ] Call `repo.EnsureCreatedAsync(storageName)` before save
  - [ ] Pass storage name to `UpsertAsync` methods

- [ ] Update `Vector<T>` search path
  - [ ] When partition context is set, constrain search to mapped storage name
  - [ ] Verify results only come from correct class

### Schema Validation

- [ ] Implement schema validation logic
  - [ ] Before indexing, check if class schema matches expected structure
  - [ ] Warn if schema drift detected
  - [ ] Option to auto-migrate or fail safely

### Testing

- [ ] Create multi-partition test
  - [ ] Index project A with 100 documents
  - [ ] Index project B with 100 documents
  - [ ] Verify Weaviate has `KoanDocument_a` and `KoanDocument_b` classes
  - [ ] Search in partition A context returns only A's documents
  - [ ] Search in partition B context returns only B's documents

---

## Milestone 5: Retrieval & MCP Tools

**Goal:** Implement MCP tools for library resolution, document retrieval, and project management.

### Library Resolution Tool

- [ ] Create `src/Koan.Context/Mcp/Tools/ResolveLibraryIdTool.cs`
  - [ ] Tool name: `context.resolve_library_id`
  - [ ] Input: `{ libraryName: string }`
  - [ ] Output: `string[]` (list of matched project IDs)

- [ ] Implement fuzzy matching
  - [ ] Exact match on project name (case-insensitive)
  - [ ] Prefix match on project name
  - [ ] Substring match on project path
  - [ ] Return top 5 matches sorted by relevance

### Document Retrieval Tool

- [ ] Create `src/Koan.Context/Mcp/Tools/GetLibraryDocsTool.cs`
  - [ ] Tool name: `context.get_library_docs`
  - [ ] Input: `{ libraryId, topic?, tokens?, pathContext?, continuationToken? }`
  - [ ] Output: `{ chunks: [...], continuationToken? }`

- [ ] Implement retrieval service
  - [ ] Resolve libraryId to project
  - [ ] Set partition context
  - [ ] Embed `topic` query
  - [ ] Perform hybrid search (vector + BM25) with configurable alpha
  - [ ] Respect `tokens` limit when assembling response
  - [ ] Implement cursor-based pagination for continuation

- [ ] Add provenance to results
  - [ ] Each chunk includes: text, filePath, commitSha, chunkRange, score
  - [ ] Include "open file" URL for local file system

### Admin Tools

- [ ] Create `context.list_projects` tool
  - [ ] Returns all projects with ID, name, status, lastIndexed

- [ ] Create `context.project_status` tool
  - [ ] Input: `{ projectId }`
  - [ ] Output: `{ status, lastIndexed, stats: { files, chunks, vectors } }`
  - [ ] Query Weaviate for vector count via stats API

- [ ] Create `context.reindex_project` tool
  - [ ] Input: `{ projectId, options: { full?, incrementalFrom? } }`
  - [ ] Output: `{ started: bool, jobId? }`
  - [ ] Trigger background indexing job
  - [ ] Support incremental reindex (only changed files since commit)

### Testing

- [ ] E2E MCP test
  - [ ] Call `resolve_library_id("koan")` → returns koan-framework project ID
  - [ ] Call `get_library_docs` with returned ID → returns relevant chunks
  - [ ] Verify provenance includes correct file paths
  - [ ] Test pagination with large result sets

---

## Milestone 6: Web UI

**Goal:** Build local web UI for project management, search, and administration.

### Projects Dashboard

- [ ] Create `src/Koan.Context/wwwroot/index.html`
  - [ ] List all projects with cards
  - [ ] Show: name, path, status badge, last indexed timestamp
  - [ ] Actions: reindex, view details, delete

- [ ] Add project detail page
  - [ ] Show full project metadata
  - [ ] Display indexing logs
  - [ ] Show vector stats (count, last sync)

### Onboarding Wizard

- [ ] Create multi-step project creation flow
  - [ ] Step 1: Enter project name
  - [ ] Step 2: Browse/select project path
  - [ ] Step 3: Auto-detect docs path (or manual entry)
  - [ ] Step 4: Preview files to be indexed
  - [ ] Step 5: Confirm and start indexing

- [ ] Add path validation
  - [ ] Check path exists and is readable
  - [ ] Warn if no .md files found
  - [ ] Suggest docs/ or documentation/ if found

### Search Interface

- [ ] Create semantic search page
  - [ ] Project selector dropdown (or "All projects")
  - [ ] Query input with autocomplete
  - [ ] Alpha slider (0.0 = keyword, 1.0 = semantic)
  - [ ] TopK selector (5, 10, 20, 50)

- [ ] Display search results
  - [ ] Chunk text with highlighting
  - [ ] File path breadcrumb
  - [ ] Commit SHA badge
  - [ ] Similarity score
  - [ ] "Open file" button (opens in default editor)

### Provider Admin

- [ ] Create Weaviate status page
  - [ ] Show endpoint URL
  - [ ] Connection status (green/red)
  - [ ] List all classes managed by Koan Context
  - [ ] Show vector counts per class
  - [ ] "Clear all vectors" button (with confirmation)

### Indexing Monitor

- [ ] Create real-time indexing monitor
  - [ ] Progress bar per project
  - [ ] Live log stream (files processed, chunks created, errors)
  - [ ] Pause/resume controls
  - [ ] Cancel button

### Testing

- [ ] UI integration tests
  - [ ] Playwright or Selenium tests
  - [ ] Test project creation flow
  - [ ] Test search interaction
  - [ ] Test reindex trigger

---

## Milestone 7: Security & Polish

**Goal:** Production-ready security, audit logging, and documentation.

### Security

- [ ] Localhost binding enforcement
  - [ ] Default to `http://localhost:5432`
  - [ ] Require explicit opt-in to bind to 0.0.0.0
  - [ ] Warning log when exposing to network

- [ ] Authentication middleware
  - [ ] Use Koan.Web authentication patterns
  - [ ] Admin role required for: reindex, delete, provider admin
  - [ ] Read-only role for: search, view projects

- [ ] Secrets redaction
  - [ ] Pre-indexing scan for patterns: API keys, tokens, passwords
  - [ ] Redact or skip files containing secrets
  - [ ] Configurable regex patterns

### Audit Logging

- [ ] Log all admin operations
  - [ ] Project create/update/delete
  - [ ] Reindex triggers (user, timestamp, options)
  - [ ] Vector flush operations

- [ ] Log external provider interactions
  - [ ] Embedding API calls (model, token count, cost estimate)
  - [ ] Weaviate operations (class create, bulk upsert, search)

### Documentation

- [ ] Create `docs/guides/koan-context-setup.md`
  - [ ] Installation instructions
  - [ ] First project walkthrough
  - [ ] Configuration reference

- [ ] Create `docs/guides/koan-context-mcp-integration.md`
  - [ ] How to configure Claude Desktop / Cline
  - [ ] Example MCP tool usage
  - [ ] Troubleshooting common issues

- [ ] Update `README.md` in Koan.Context project
  - [ ] Quick start
  - [ ] Architecture diagram
  - [ ] Feature list

### Release Preparation

- [ ] Version bump and changelog
  - [ ] Add CHANGELOG entry for new Koan.Context package
  - [ ] Note VectorCapabilities.DynamicCollections addition
  - [ ] Note IPartitionContextProvider framework enhancement

- [ ] Performance benchmarks
  - [ ] Index 1000 markdown files, measure time and memory
  - [ ] Search benchmark: 100 queries, measure p50/p95/p99 latency
  - [ ] Document results in performance guide

---

## Future Enhancements (Out of Scope for v1)

- [ ] Multi-provider support (Pinecone, Qdrant, Chroma)
- [ ] Code file indexing (extract function signatures, comments)
- [ ] Incremental indexing based on git diff
- [ ] RAG answer generation with citations
- [ ] Slack/Discord bot integration
- [ ] Multi-tenant SaaS mode with auth

---

## Session Log

### 2025-11-05
- Completed architectural review
- Created comprehensive checklist
- Identified critical gap: WithContext() doesn't exist
- Awaiting user confirmation on design questions

---

## Notes & Decisions

- **Storage strategy:** Per-class-per-project (no shared-class filtering)
- **Weaviate instance:** Single shared instance (no per-project containers)
- **Partition context:** New framework capability, not Vector-specific
- **Class naming:** `KoanDocument_{projectId}` with sanitization

