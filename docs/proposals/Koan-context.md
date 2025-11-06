# Koan Context — Implementation Specification (Weaviate-first)

**Status:** Implementation-ready specification for an agentic coding AI or developer to implement **Koan Context**, a Koan-hosted local-first Context7-style service that indexes local code + docs, exposes MCP tools, supports partition-aware vector storage using Koan `WithContext()`, and initially uses **Weaviate** as the vector provider with Aspire orchestration fallback.

---

> **⚠️ ARCHITECTURAL REVIEW (2025-11-05):**
> This specification has been reviewed and updated based on codebase analysis. Key corrections:
> - **EntityContext already exists** (DATA-0077) — reuse for partition routing instead of building new WithContext()
> - **Partition strategy:** Per-class-per-project ONLY (no shared-class filtering mode)
> - **Weaviate deployment:** Single shared instance (not per-project instances)
> - **Implementation checklist:** See [Koan-context-checklist.md](./Koan-context-checklist.md) for detailed task tracking
>
> These corrections ensure the spec aligns with Koan framework patterns and user requirements.

---

## Table of contents

1. [Goals & Scope](#1-goals--scope)
2. [High-level architecture](#2-high-level-architecture)
3. [Core components & responsibilities](#3-core-components--responsibilities)
4. [MCP surface specification](#4-mcp-surface-specification)
5. [Partition-aware Vector design (Weaviate-first)](#5-partition-aware-vector-design-weaviate-first)
6. [Weaviate connector & orchestration details](#6-weaviate-connector--orchestration-details)
7. [Ingest pipeline (discovery → chunk → embed → index)](#7-ingest-pipeline-discovery---chunk---embed---index)
8. [Retrieval & RAG flow](#8-retrieval--rag-flow)
9. [Web UI requirements & UX](#9-web-ui-requirements--ux)
10. [Security, privacy & operational policy](#10-security-privacy--operational-policy)
11. [Tests & acceptance criteria](#11-tests--acceptance-criteria)
12. [Implementation milestones & checklist](#12-implementation-milestones--checklist)
13. [Example code sketches & DI points](#13-example-code-sketches--di-points)
14. [Deliverables & documentation](#14-deliverables--documentation)

---

## 1. Goals & Scope

**Primary goal:** implement **Koan Context** so that local developers and agents can:

- Register local projects (folder + docs path), index their docs and code examples into a vector store, and query them using a Context7-like MCP surface (`resolve_library_id` + `get_library_docs`) and a local web UI.
- Achieve **partition-aware** vector storage automatically by reusing Koan’s ambient `WithContext()` for Entity<> queries — `Vector<>` must consult the same partition context (project/tenant) and map it to per-partition Weaviate storage artifacts (class or property filtering).
- Provide a **Weaviate-first** connector that:

  - Supports partition-aware behavior (shared-class + `projectId` property default; opt-in per-partition classes where needed),
  - Implements idempotent `EnsureCreated(storageName)` provisioning,
  - Can be auto-provisioned via Aspire using Koan orchestration patterns.

- Expose management actions (project list, reindex, indexing status) and a semantic search UI with provenance and hybrid tuning.

**Out-of-scope for initial work:** Add-on connectors (Pinecone/Qdrant/Chroma) beyond basic compatibility; full enterprise multi-instance orchestration policy (these are planned as future enhancements).

**Reference Koan pieces to reuse:**

- `Koan.Mcp` host & tools pattern.
- MCP HTTP+SSE transport examples.
- `IAi` embedding and tokenization patterns.
- `Vector<T>` façade and `VectorCapabilities` model.
- Koan auto-provisioning (`ExecuteWithSchemaProvisioningAsync` / `IInstructionExecutor`) pattern.
- Weaviate orchestration evaluator (pattern for Aspire provisioning).

---

## 2. High-level architecture

```
Koan Context (Koan app)
 ├─ Web UI (project manager, search, admin)
 ├─ Koan.Mcp Host (HTTP+SSE, stdio)
 ├─ Project Controller (REST endpoints)
 ├─ Ingest Service (Flow pipeline: discovery→tokenize→chunk→embed→save)
 ├─ Retrieval Service (Vector Query + RAG assembly)
 ├─ Partition Context (uses EntityContext from DATA-0077)
 ├─ Vector Partition Mapper (Weaviate mapper)
 └─ Orchestration Adapter (Weaviate Aspire evaluator)
Vector store: Weaviate (local container or remote)
```

- **Primary runtime**: Console app using `WebApplication.CreateBuilder` pattern (like [g1c1.gardencoop](../../samples/guides/g1c1.GardenCoop/Program.cs)). Exposes:
  - Web API (REST endpoints for project CRUD) on `http://localhost:27500`
  - Web Client (static HTML/JS UI for project management and search) on `http://localhost:27500`
  - MCP endpoints (HTTP+SSE and STDIO transports via Koan.Mcp) on `http://localhost:27500/mcp/sse`
  - Bound to `localhost:27500` by default for security
  - **Port allocation**: 27500-27510 range to avoid conflicts with other services
- **Vector provider**: Weaviate as the first-class connector. Uses existing `WeaviateOrchestrationEvaluator` to provision Weaviate container via Koan's Aspire integration.
  - Weaviate exposed on `http://localhost:27501` (mapped from container's internal 8080)
- **Hosting pattern**: `builder.Services.AddKoan()` auto-registers all entities, services, and MCP tools. Koan's hosting/DI patterns for auto-discovery and health checks.

---

## 3. Core components & responsibilities

### Koan.Mcp host & tools

- Register tools (see Section 4).
- Expose transports: STDIO and HTTP+SSE.
- Publish `/mcp/capabilities`.

### Project entity & API

- Persist `Project` as a Koan `Entity<>` with fields:

  - `Id` (GUID v7, auto-generated), `Name`, `Path`, `DocsPath`, `LastIndexedCommit`, `Status` (enum: NotStarted/InProgress/Completed/Failed), `LastIndexed` (DateTime?).
  - **REMOVED**: `PartitionMode` (always per-class), `InstanceEndpoint` (always shared instance), `VectorProviderConfig` (global config).

- CRUD and list endpoints + MCP `context.list_projects`.

### Partition Context (EntityContext)

- **EXISTING COMPONENT**: `EntityContext` already exists in the framework (DATA-0077) providing ambient partition routing.
- `EntityContext.Current.Partition` provides AsyncLocal-based partition context.
- `EntityContext.Partition(id)` creates scoped partition routing.
- `Vector<>` calls read `EntityContext.Current.Partition` — there is **no requirement** for callers to pass `projectId` explicitly.
- Consistent with `Entity<T>` partition routing (same system for both).

### Vector Partition Mapper

- `IVectorPartitionMapper` for Weaviate (maps `projectId` → storage semantics). Implement default mapping rules and sanitization.

### Weaviate connector

- Supports `VectorCapabilities` including `Knn`, `Filters`, `Hybrid`, `Partitioning` (advertised).
- Implements `Save`, `Search`, `EnsureCreated(storageName)`, `Stats`, and `Delete`.
- Default mapping: shared class `Document` + indexable property `projectId`. Optionally support per-class-per-project (`Document_{projectId}`) when `PartitionMode == per-class`.

### Ingest flow

- Discovery, extraction, tokenization, embedding, bulk upsert into vector store.
- Chunk size: 800–1,000 tokens with 50-token overlap.
- Use `IAi.TokenizeAsync` + `IAi.EmbedAsync` patterns and `Vector<Document>.Save` for upserts.

### Retrieval service & RAG

- Embedding query, vector search with hybrid mode (alpha selectable), assembly of context with provenance, `IAi.ChatAsync` with "Answer strictly from provided context".

### Orchestration (Aspire)

- `WeaviateOrchestrationEvaluator` template: create container image, env, volumes, health checks, named volume per `projectId` (e.g., `koan-weaviate-{projectId}`), and health timeout semantics. Use Koan’s orchestration patterns.

---

## 4. MCP surface specification

Publish the following MCP tools under `context.*` naming:

### `context.resolve_library_id`

- **Input**: `{ libraryName: string }`
- **Output**: `string[]` — canonical library IDs (e.g., `/vercel/next.js@14.0`)
- **Behavior**: fuzzy match to local indexed docs first, then fallback to global mapping logic (local-first).

### `context.get_library_docs`

- **Input**:

  ```json
  {
    "libraryId": "string",
    "topic": "string | null",
    "tokens": "int | null",
    "pathContext": "string | null",
    "continuationToken": "string | null"
  }
  ```

- **Output**:

  ```json
  {
    "chunks": [{ "text": "string", "source": { "file": "string", "commit": "string", "offset": [start,end] }, "score": "float" }],
    "continuationToken": "string | null"
  }
  ```

- **Behavior**:

  - If `pathContext` present, resolve to project/partition and bias results to that project.
  - Use partition-aware `Vector.Search` to return chunks; apply `tokens` cap when assembling.
  - Support paging via `continuationToken`.

### Admin / project tools

- `context.list_projects() -> Project[]`
- `context.project_status(projectId) -> { status, lastIndexed, stats }`
- `context.reindex_project(projectId, options) -> { started: bool }`

**Tool descriptors**: register via `McpToolDefinition` records and `McpEntityRegistration` per Koan.Mcp patterns. Publish capabilities via `/mcp/capabilities`.

---

## 5. Partition-aware Vector design (Weaviate-first)

### Objectives

- `Vector<T>` reads ambient partition id from the new `IPartitionContextProvider`.
- Map `partitionId` (projectId) to Weaviate query and storage semantics.
- **CORRECTED:** Use **per-class-per-partition ONLY**:
  - Each project gets its own Weaviate class: `KoanDocument_{sanitizedProjectId}`
  - Provides strong isolation between projects
  - Single shared Weaviate instance hosts all classes
  - Removed: shared-class with `projectId` filtering mode (not needed)

### VectorCapabilities

- Add `VectorCapabilities.DynamicCollections = 1 << 10` to `VectorCapabilities`. Connectors that can create collections/classes dynamically at runtime must advertise this capability. `Vector<T>.GetCapabilities()` remains the contract.
- Weaviate supports this via schema API; in-memory providers may not.

### `Vector` runtime algorithm (pseudo)

1. `partition = partitionProvider.CurrentPartitionId`
2. If `partition == null`: behave as today (global store).
3. Else:

   - If connector `Partitioning`:

     - `storageName = partitionMapper.MapStorageName<T>(partition)` (for per-class mode) OR
     - use shared class and always include `projectId` as a required metadata field.
     - Call connector `EnsureCreated(storageName)` if needed.
     - `Search` call: include `where` or `filter` matching `projectId` if using shared class; else search in `storageName` class.

   - Else:

     - Inject server-side search filter `{ projectId: partition }` and save metadata with `projectId`.
     - For strict isolation policy, orchestrate a per-project Weaviate instance and update project config to point to it.

### Provenance & metadata

- Save every vector with metadata:

  - `projectId`, `filePath`, `commitSha`, `chunkStart`, `chunkEnd`, `language`, `title`.

- Use these fields to implement `get_library_docs` results with precise sources.

---

## 6. Weaviate connector & orchestration details

### Weaviate mapping choices

**CORRECTED: Per-class-per-partition (only mode)**

- For each project with `projectId = "koan-framework"`, create class `KoanDocument_koan_framework`
- **Class name sanitization**: lowercase, alphanumeric + hyphens/underscores only, max 256 chars
- **Properties** (standardized schema for all project classes):
  - `docId` — text (entity ID for reverse lookup)
  - `searchText` — text with BM25 indexing (for hybrid search)
  - `projectId` — text (for cross-partition queries if needed in future)
  - `filePath` — text (relative path within project)
  - `commitSha` — text (git commit for provenance)
  - `chunkRange` — text (start:end byte offsets)
  - `title` — text (from markdown heading hierarchy)
  - `language` — text (programming language if from code file)

**Removed:** Shared-class mode is not implemented.

**Hybrid & BM25**

- Ensure `searchText` is indexed with tokenization suitable for BM25. Koan’s Vector doc gives specifics.

### EnsureCreated(className)

**CORRECTED:** Always creates per-class, never shared-class.

- Implementation responsibilities:

  - Create class `KoanDocument_{sanitizedProjectId}` with standardized DocumentChunk schema (see Properties section above).
  - Idempotent: if class exists, verify schema matches; if not, create it.
  - Use Weaviate schema API: POST `/v1/schema` with class definition.

- Follow Koan's auto-provisioning semantics: create resources with retries, wait until ready, log readiness. Use the existing `WeaviateVectorRepository.EnsureSchemaAsync` patterns as templates.

### Orchestration (Aspire)

**CORRECTED:** Single shared Weaviate instance, not per-project.

- `WeaviateOrchestrationEvaluator` **already exists** and works correctly for single-instance provisioning.
- **No changes needed** — evaluator provisions one Weaviate container with volume `koan-weaviate-data`.
- All projects share the same Weaviate endpoint, differentiated by class name.
- **Removed**: Per-project volume naming and per-project instance logic.

### Example Weaviate search & filter semantics (pseudocode)

- Shared-class search:

  ```json
  {
    "class": "KoanDocument",
    "nearText": { "concepts": ["routing middleware"] },
    "where": {
      "path": ["projectId"],
      "operator": "Equal",
      "valueText": "project-42"
    },
    "limit": 10
  }
  ```

- Per-class search:

  - Search in `KoanDocument_project-42` with no `where` filter.

---

## 7. Ingest pipeline (discovery → chunk → embed → index)

### Discovery

- Auto-detect:

  - `README*`, `docs/**`, `adrs/**`, `samples/**`, `*.md`, `CHANGELOG*`
  - Optionally `src/**` to extract code blocks.

- If `.git` present, record `HEAD` commit sha for provenance.

### Extraction & normalization

- From markdown:

  - Extract headings, paragraphs, code fences (tag language), blockquote notes.

- For code files:

  - Extract example blocks and inline comments as useful context.

### Tokenize & chunk

- Use `IAi.TokenizeAsync` to measure token length.
- Chunking policy:

  - Target chunk size 800–1,000 tokens.
  - Overlap 50 tokens for continuity.
  - For code, prefer logical boundaries (function/block) if token boundary occurs mid-block.

### Embedding & caching

- Use `IAi.EmbedAsync` with configured embedding model (default: `all-minilm` or configured).
- Cache by `hash(content + modelId)` to avoid redundant embed calls. Koan’s docs show embedding caching patterns.

### Save vectors

- Bulk upsert via `Vector<Document>.Save(batch)`, include metadata fields including `projectId` and provenance. Use streaming pipelines for large datasets.

---

## 8. Retrieval & RAG flow

### Query handling

- Accept query & optional `pathContext`. If `pathContext` present:

  - Resolve to project and set ambient partition via `WithContext(projectId)`.

- For text queries:

  1. `queryEmbedding = IAi.Embed(query)`
  2. Build `VectorQueryOptions`:

     - `vector = queryEmbedding`
     - `searchText = query` (enables hybrid)
     - `alpha` parameter adjustable via UI
     - `filter/where` if using shared-class mode
     - `storageName` if per-class/per-partition mapping is used

  3. Call `Vector<T>.Search(...)` and get matches.

### Context assembly & generation

- Build system prompt:

  - `System: "Answer strictly from provided context. Cite [1], [2] ..."`.

- Provide top-K chunks, include their metadata (file, commit, range).
- Call `IAi.ChatAsync` and return both answer and list of sources.

---

## 9. Web UI requirements & UX

**Binding & security**

- Default to `localhost:27500` only. Management endpoints require authentication via Koan Web auth middleware for production.
- **Port allocation**:
  - 27500: Koan.Context app (Web API, Web Client, MCP)
  - 27501: Weaviate (exposed from container)
  - 27502-27510: Reserved for future Koan.Context services

**Pages / features**

1. **Projects dashboard**

   - Projects list, indexing status, last indexed commit, provider, partition mode.

2. **Onboarding wizard**

   - Path pick, docs path auto-detect, vector provider config (Weaviate endpoint or "Start local Weaviate"), partition mode selection (`shared` / `per-class` / `per-instance`).

3. **Indexing panel**

   - Start / stop reindex, incremental index, indexing logs, chunking statistics, cache stats.

4. **Search interface**

   - Project-scoped search with alpha slider, topK selector, results with snippet + file path + commit + score + "open file" action.

5. **Provider admin**

   - Show `Vector.Stats()` and `EnsureCreated` status.

6. **Orchestration control**

   - "Start local Weaviate instance" button (calls Aspire evaluator), show instance endpoint & health, and allow "stop" or "recreate".

**Important UX constraints**

- Always show provenance for answers.
- Provide re-run/reindex and “explain why this snippet was found” controls for debugging.

---

## 10. Security, privacy & operational policy

- **Local-first**: default binding to `localhost`. Explicit opt-in needed to expose to other hosts.
- **Server-side scoping**: do not accept client-supplied partition; `EntityContext.Partition()` must be the authoritative source (server-injected).
- **Auth & RBAC**: admin for provider config, reindex, orchestration. Read-only role for searching.
- **External providers**: explicit consent before sending data to remote Weaviate or AI providers; show cost/time implications for embeddings.
- **Redaction**: pre-indexing rules and a secrets scrubber to avoid indexing credentials.
- **Auditing**: log index operations, provider interactions, orchestration events.

---

## 11. Tests & acceptance criteria

### Unit tests

- `Vector.Save/Search` when `partition = null` and `partition = "p1"` verifying connector called with `storageName` (when `Partitioning`) or with `filter` injection otherwise.
- `Vector.EnsureCreated(storageName)` calls connector provisioning logic.

### Integration tests

- Containerized Weaviate test:

  - Ensure `EnsureCreated` creates class/schema for shared-class and per-class modes.
  - Save two projects' documents and ensure search results are correctly scoped per `projectId`.

- Orchestration test:

  - Aspire evaluator spins Weaviate container for a given `projectId`, health check returns success, indexing uses the instance endpoint.

### E2E acceptance

- Register a sample project, run indexing, call `context.get_library_docs` via MCP HTTP+SSE, verify returned chunks and provenance.
- UI: onboard, index, run search, adjust alpha, reindex.

**Success criteria**

- MCP tools respond and return correct results.
- Partition-aware saves and searches behave correctly for shared-class and per-class modes.
- Aspiration provisioning (per-instance) works on-demand.

---

## 12. Implementation milestones & checklist

**Milestone 0 — Foundation & Partition Context** *(NEW — prerequisite for all other work)*

- Create `IPartitionContextProvider` interface and `AsyncLocalPartitionContextProvider` implementation.
- Create `IVectorPartitionMapper` interface and `WeaviatePartitionMapper` implementation.
- Add `VectorCapabilities.DynamicCollections` flag.
- Update `Vector<T>` to consult partition context provider.
- Unit tests for partition context and mapper.

**Milestone 1 — Console App Skeleton** *(formerly Milestone 0)*

- Create `src/Koan.Context/Koan.Context.csproj` as **console app** (OutputType: Exe)
- Follow [g1c1.gardencoop](../../samples/guides/g1c1.GardenCoop/Program.cs) pattern:
  - `WebApplication.CreateBuilder(args)`
  - `builder.Services.AddKoan()` for auto-registration
  - `app.MapMcpEndpoints()` for MCP HTTP+SSE transport
  - `app.MapControllers()` for REST API
  - `app.UseStaticFiles()` for web UI
  - Bind to `localhost` only by default
- Wire Koan.Mcp with HTTP+SSE and STDIO transports
- Publish basic `/mcp/capabilities` endpoint
- Add health check: `/health`

**Milestone 1 — Project & ingest**

- Implement `Project` entity, project CRUD, onboarding wizard UI.
- Implement ingest pipeline with chunking & embedding and save to `Vector<Document>` (without partition flow yet). Add caching & streaming.

**Milestone 2 — Partition plumbing**

- Discover `WithContext()` provider; implement `IPartitionContextProvider` if needed.
- Implement `IVectorPartitionMapper` (Weaviate mapper) and add `VectorCapabilities.Partitioning`.
- Update `Vector<T>` to consult partition provider and call connectors accordingly. Unit tests.

**Milestone 3 — Weaviate connector enhancements**

- Update `WeaviateVectorRepository.EnsureSchemaAsync` to accept optional `className` parameter.
- Implement dynamic per-class provisioning with standardized DocumentChunk schema.
- Add `DynamicCollections` capability flag.
- Integration tests: save to two partitions, verify class isolation.

**Milestone 4 — Weaviate Orchestration via Aspire** *(SIMPLIFIED — evaluator already exists)*

- **No new evaluator code required** — `WeaviateOrchestrationEvaluator` already exists in `src/Connectors/Data/Vector/Weaviate/Orchestration/`.
- Reference `Koan.Data.Vector.Connector.Weaviate` package to enable auto-provisioning.
- Weaviate container lifecycle:
  - Koan's Aspire integration auto-detects dependency via `WeaviateOrchestrationEvaluator`
  - Creates container with image `semitechnologies/weaviate:latest`
  - **Host port**: 27501 (mapped from container's internal 8080)
  - Health check endpoint: `/v1/.well-known/ready` on `http://localhost:27501`
  - Volume: `koan-weaviate-data` (persistent across restarts)
- Configuration: `appsettings.json` with `Koan:Data:Vector:Weaviate:Endpoint` set to `http://localhost:27501`
- Port allocation: 27500 (app), 27501 (Weaviate), 27502-27510 (reserved for future services)
- UI: Show Weaviate connection status (green if reachable at 27501, red if not)
- Tests: Verify container starts on port 27501 and vector operations succeed

**Milestone 5 — Retrieval & MCP**

- Implement `context.*` MCP tools (resolve + get_library_docs + admin tools). End-to-end tests using MCP SSE/RPC.

**Milestone 6 — Security & docs**

- Add auth, RBAC, audit logs, operator docs, migration notes, and release.

---

## 13. Example code sketches & DI points

> The code below is a concise sketch to guide the agent or developer. Each piece must be adapted to your repository style, logging, error handling, and DI conventions.

### Partition context provider (sketch)

```csharp
public interface IPartitionContextProvider
{
    string? CurrentPartitionId { get; }
}

public class AsyncLocalPartitionContextProvider : IPartitionContextProvider
{
    private static readonly AsyncLocal<string?> _current = new();
    public string? CurrentPartitionId => _current.Value;

    public static IDisposable BeginPartitionScope(string partitionId)
    {
        _current.Value = partitionId;
        return new DisposableAction(() => _current.Value = null);
    }
}
```

_(Prefer reusing `WithContext()`’s existing ambient context if present.)_

### IVectorPartitionMapper (Weaviate)

```csharp
public interface IVectorPartitionMapper
{
    string MapClassName<T>(string partitionId);
    // For shared-class mode return "KoanDocument"
    // For per-class mode return "KoanDocument_{partitionId}"
}
```

### Vector<T> pseudocode for Save/Search

```csharp
public static async Task<int> Save(IEnumerable<(string Id, float[] Emb, object? Meta)> items)
{
    var partition = _partitionProvider.CurrentPartitionId;
    if (partition != null && ConnectorSupportsPartitioning)
    {
        var className = _mapper.MapClassName<T>(partition);
        await _connector.EnsureCreated(className);
        return await _connector.Save(className, items);
    }
    else
    {
        // fallback: include projectId in metadata
        var enriched = items.Select(i => (i.Id, i.Emb, EnsureProjectMeta(i.Meta, partition)));
        return await _connector.Save(defaultClass, enriched);
    }
}
```

---

## 14. Deliverables & documentation

- **Source**: `src/Koan.Context` + updated `src/Koan.Data.Vector`, `src/Connectors/Data/Vector/Weaviate` connector updates, `src/Koan.Mcp` registrations.
- **UI**: static assets + backend endpoints in Koan web project.
- **Tests**: unit + integration (Weaviate containers) + E2E (MCP SSE).
- **Docs**: README, operator guide, API reference, migration guide for vector data, HowTo for partition modes.
- **PRs**: structured commits and release note describing changes to `VectorCapabilities` and new DI hooks.

---

## Appendix A — Quick references & useful Koan fragments

- Koan.Mcp architecture & quickstart (how to expose MCP tools).
- Expose MCP HTTP+SSE (guide showing `/mcp/sse` and `tools/list` usage).
- AI Pillar / embedding & tokenization patterns.
- Vector Search Design (Vector<T>, VectorCapabilities, hybrid BM25 guidance).
- Auto-provisioning system / `ExecuteWithSchemaProvisioningAsync` pattern for idempotent resource creation.
- Weaviate orchestration evaluator sample (how to create container + volumes).
- Vector embedding & streaming pipeline examples for ingestion.
