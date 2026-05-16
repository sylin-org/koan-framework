# Koan.Context: Production Readiness & Simplification Proposal

---
**Type:** APPROVED PROPOSAL
**Domain:** koan-context, data, operations
**Status:** approved-for-implementation
**Created:** 2025-11-07
**Last Updated:** 2025-11-07
**Framework Version:** v0.6.3+
**Authors:** Architecture Team, Systems Engineering Consultancy

---

## Executive Summary

Koan.context is a **local-first semantic code search service** enabling AI agents to perform context-aware queries across project documentation and code. Built on a **dual-store architecture** (SQLite for metadata, vector provider for embeddings), it serves compliance-sensitive environments requiring PII-safe, air-gapped, or on-premises deployment.

**Current Assessment:** **C-** (critical issues present)
**Target Assessment:** **A-** (production-ready with quality guarantees)

**Critical Finding:**
While the architecture is fundamentally sound, the implementation suffers from:
- **8 critical security/data-loss bugs** (path traversal, batch failures, offset corruption)
- **0% test coverage** (no unit, integration, or E2E tests)
- **Scope creep** (1,767 LOC of unnecessary complexity: file monitoring, over-engineered features)
- **Vendor lock-in risk** (5 Weaviate mentions in comments, easy to fix)
- **Naming complexity** (9 interface files with verbose names like `IDocumentDiscoveryService`)

**This Proposal:**
A **4-week pragmatic rebuild** that preserves all valuable capabilities while eliminating technical debt, fixing critical bugs, and achieving 85%+ test coverage.

**Investment:** $52k-80k (external) or $40k-55k (internal)
**Timeline:** 4 weeks (2 FTE)
**Risk:** LOW (removes cruft, preserves core value)

---

## Table of Contents

1. [Value Proposition & Strategic Context](#1-value-proposition--strategic-context)
2. [Corrected Assessment: C- Grade Reality](#2-corrected-assessment-c--grade-reality)
3. [Preserved Capabilities Matrix](#3-preserved-capabilities-matrix)
4. [MANDATORY Simplifications](#4-mandatory-simplifications)
5. [Scope Rationalization](#5-scope-rationalization)
6. [Implementation Roadmap (4 Weeks)](#6-implementation-roadmap-4-weeks)
7. [Success Metrics & Quality Gates](#7-success-metrics--quality-gates)
8. [Budget & Resource Requirements](#8-budget--resource-requirements)
9. [Risk Assessment](#9-risk-assessment)
10. [Appendices](#10-appendices)

---

## 1. Value Proposition & Strategic Context

### 1.1 Core Value Proposition

**For:** Developers and AI agents needing semantic code search
**Who:** Require local/on-prem deployment for compliance or PII concerns
**Koan.context is:** A local-first, vendor-agnostic semantic search service
**That:** Indexes project documentation and enables natural language queries via MCP protocol
**Unlike:** GitHub Copilot (cloud-only), Cursor (vendor lock-in)
**Koan.context:** Works with any MCP-compatible agent (Claude, Gemini, Continue.dev) and runs 100% locally

### 1.2 Target Market

**Primary:** Compliance-sensitive organizations
- Healthcare (HIPAA)
- Financial Services (SOC2, PCI-DSS)
- Government/Defense (FedRAMP, IL5)
- Legal/Enterprise (GDPR, data sovereignty)

**Secondary:** Privacy-conscious developers
- Individual developers wanting local AI tools
- Teams with proprietary codebases
- Organizations with air-gapped networks

**Estimated TAM:** $70B+ (10-15% AI adoption across healthcare $250B, financial $500B, government $150B)

### 1.3 Strategic Positioning

**Competitive Advantages:**

| Feature | Koan.context | GitHub Copilot | Cursor | Continue.dev |
|---------|--------------|---------------|--------|--------------|
| **Local-First** | ✅ | ❌ (cloud-only) | ❌ (cloud) | ✅ |
| **MCP Protocol** | ✅ | ❌ (proprietary) | ❌ | ⚠️ (partial) |
| **Multi-Agent** | ✅ | ❌ (GitHub only) | ❌ | ✅ |
| **Vendor Agnostic** | ✅ | ❌ | ❌ | ✅ |
| **Air-Gapped** | ✅ | ❌ | ❌ | ⚠️ |
| **Open Source** | ✅ | ❌ | ❌ | ✅ |

**Key Differentiator:** "Reference = Intent" pattern enables **true vendor independence** - swap vector providers by changing a single project reference, zero code changes.

---

## 2. Corrected Assessment: C- Grade Reality

### 2.1 Grade Correction

**Previous Claim:** "B+ → A- with hardening"
**Reality:** **C- → A- requires fundamental fixes**

**Justification:**

| Dimension | Previous Assessment | Actual Grade | Evidence |
|-----------|-------------------|--------------|----------|
| **Security** | B+ | **D** | Path traversal vulnerability, no input validation, symlink loops |
| **Data Integrity** | B+ | **D+** | Batch failures lose 100+ chunks, offset corruption, unclosed code blocks |
| **Reliability** | B | **C** | No retry logic (Polly added but unused), synchronous I/O blocks async |
| **Testing** | N/A | **F** | 0% coverage - no unit, integration, or E2E tests |
| **Observability** | C | **F** | Zero metrics, zero traces, zero health checks |
| **Vendor Independence** | A | **A-** | 95% agnostic (5 Weaviate comments to fix) |

**Composite:** **C-** (not B+)

### 2.2 Critical Issues from QA Report

**Security (3 Critical)**:
1. Path traversal vulnerability (DocumentDiscoveryService.cs:40) - arbitrary file read
2. Symlink cycle detection missing - infinite loops
3. Substring out-of-bounds (DocumentDiscoveryService.cs:126) - application crash

**Data Loss (3 Critical)**:
1. Batch vector failures discard 100+ chunks (IndexingService.cs:147) - no retry
2. Unclosed code blocks silently dropped (ContentExtractionService.cs:47-84)
3. Byte offset double-increment (ContentExtractionService.cs:149) - provenance corruption

**Reliability (2 High)**:
1. Silent exception swallowing (6 locations) - impossible to debug
2. No dual-store transaction coordination - orphaned metadata/vectors

### 2.3 Why Previous Assessment Was Wrong

**Incorrect Assumptions:**

1. ❌ **"Indexing takes 30-60 seconds"** → **Reality: 30-60 MINUTES**
   - 10,000 chunks × 200ms embedding API call = 33 minutes minimum
   - Job tracking is ESSENTIAL, not "over-engineering"

2. ❌ **"B+ grade"** → **Reality: C- grade**
   - 8 critical bugs (not "minor gaps")
   - 0% test coverage (not "adequate testing")
   - Manual QA found 45 issues in first review

3. ❌ **"EntityContext.Transaction() solves dual-store sync"** → **Reality: Best-effort only**
   - Framework transactions are NOT distributed transactions
   - Failure window exists between adapter commits
   - Need outbox pattern for true reliability

---

## 3. Preserved Capabilities Matrix

### 3.1 Core Capabilities (100% Preserved)

| Capability | Status | Implementation | Value | Notes |
|------------|--------|----------------|-------|-------|
| **Semantic search** | ✅ KEEP | `Vector<Chunk>.Search()` with hybrid (BM25 + vector) | HIGH | Core value proposition |
| **MCP integration** | ✅ KEEP | Single `/api/search` endpoint, agent-agnostic | HIGH | Strategic differentiator |
| **Job tracking** | ✅ **KEEP** | `IndexingJob` entity with progress/ETA | **CRITICAL** | **30-60 min operations require visibility** |
| **Partition isolation** | ✅ KEEP | `EntityContext.Partition(projectId)` | HIGH | Multi-tenant ready |
| **Dual-store sync** | ✅ KEEP + FIX | SQLite (metadata) + Vector (embeddings) | HIGH | **Needs outbox pattern** |
| **Chunking** | ✅ KEEP | 800-1000 token chunks with 50-token overlap | HIGH | Semantic quality |
| **Provenance** | ✅ KEEP | File path, line numbers, commit SHA | HIGH | Traceability for users |
| **Auto-indexing** | ✅ KEEP | Index on first query if not indexed | HIGH | DX feature |
| **Embedding caching** | ✅ KEEP | SHA256-based cache to avoid redundant API calls | MEDIUM | Cost savings (embeddings expensive) |
| **Hybrid search** | ✅ KEEP | Alpha parameter (0.0=keyword, 1.0=semantic) | MEDIUM | User flexibility |
| **Vendor independence** | ✅ FIX | Remove 5 comments, keep project reference | HIGH | Strategic |

### 3.2 Features to Simplify/Remove (Scope Rationalization)

| Feature | LOC Change | Action | Rationale | Risk |
|---------|-----------|--------|-----------|------|
| **File monitoring** | -450 | REMOVE | Separate product feature, adds complexity without value | LOW - users trigger manual re-index |
| **IncrementalIndexingService** | -280 | REMOVE | Depends on file monitoring, adds bugs | LOW - full re-index is fast enough |
| **Differential scanning** | 287→100 | SIMPLIFY | Critical for re-indexing (96% time savings), but over-engineered | LOW - simplified version preserves benefit |
| **IndexedFile entity** | 87→40 | SIMPLIFY | Needed for differential scanning, simplify schema | LOW - reduced complexity |
| **ConfigurationController** | -80 | REMOVE | Runtime config anti-pattern | NONE - use appsettings.json |
| **REST API** | +120 | **ENHANCE** | **Add cross-project search, health endpoints for web UI** | **POSITIVE - enables web interface** |
| **9 interface files** | -200 | REMOVE | Single implementations, unnecessary abstraction | LOW - DI works with concrete classes |
| **Monitoring options** | -100 | REMOVE | Config for removed file monitoring feature | NONE |
| **GitignoreParser** | -150 | REMOVE | Only needed for file monitoring | LOW - discovery handles basics |
| **DebouncingQueue** | -100 | REMOVE | Only needed for file monitoring | NONE |

**Total LOC Removed:** ~1,600 LOC
**Total LOC Simplified:** ~187 LOC (287→100)
**Total LOC Added:** ~200 LOC (REST API enhancements)
**Net Change:** ~1,587 LOC reduction (39% of codebase)

### 3.3 What Users Actually Need

**User Story:** As a developer, I want to semantically search my codebase from any AI agent in 5 minutes.

**Essential Flow:**
1. ✅ **Index**: `koan-context index ./my-project` (30-60 min for large projects)
2. ✅ **Search**: Agent calls `/api/search?q=authentication` via MCP
3. ✅ **Results**: Get chunks with file paths, line numbers, scores

**Non-Essential (Currently Built):**
- ❌ File monitoring with gitignore parsing (450 LOC) - users can re-index manually
- ⚠️ Differential scanning - **ESSENTIAL but over-engineered** (287 LOC → 100 LOC simplified)
- ❌ Progress bars with ETA for file discovery (100 LOC) - indexing **embedding generation** is the bottleneck, not file I/O

---

## 4. MANDATORY Simplifications

### 4.1 MANDATORY: Naming Simplification (Week 1, Days 1-2, 8 hours)

**Status:** NON-NEGOTIABLE, blocks all other work
**Deadline:** End of Day 2, Week 1

#### Why Mandatory

**Problem:** Verbose enterprise naming adds cognitive load without value
- `IDocumentDiscoveryService` (26 chars) vs `Discovery` (9 chars) - 65% shorter
- 9 interface files with single implementations - pure ceremony
- "Service" suffix on every class - redundant in context

**Impact:**
- Average name length: 22 chars → 9 chars (59% reduction)
- File count: 39 → 27 (31% reduction)
- Better readability, easier navigation, faster onboarding

#### Changes

**Entity Renames:**
```csharp
DocumentChunk → Chunk          // "Document" implied in Koan.Context namespace
IndexingJob → Job              // Keep "Job" (only one kind of job in context)
VectorOperation → SyncOperation // Generic name for outbox pattern
```

**Service Renames (Delete Interfaces):**
```csharp
// BEFORE (18 files: 9 interfaces + 9 implementations)
IDocumentDiscoveryService + DocumentDiscoveryService
IContentExtractionService + ContentExtractionService
IChunkingService + ChunkingService
IEmbeddingService + EmbeddingService
IIndexingService + IndexingService
IRetrievalService + RetrievalService
ITokenCountingService + TokenCountingService
IContinuationTokenService + ContinuationTokenService
ISourceUrlGenerator + SourceUrlGenerator

// AFTER (9 files: concrete classes only)
Discovery      // File discovery
Extraction     // Content extraction
Chunker        // Text chunking
Embedding      // Vector generation (singular, follows Koan convention)
Indexer        // Orchestrates indexing
Search         // Semantic search
TokenCounter   // Token counting (explicit purpose)
Pagination     // Continuation tokens
UrlBuilder     // Source URL generation
```

**DI Registration (Simplified):**
```csharp
// BEFORE
builder.Services.AddSingleton<IChunkingService, ChunkingService>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
// ... 9 registrations

// AFTER (Koan auto-registers)
builder.Services.AddKoan();  // Auto-discovers all services
```

#### Verification Checklist (End of Day 2)

```bash
# 1. Verify no interfaces remain
find src/Koan.Context/Services -name "I*.cs" -type f
# Expected: 0 files

# 2. Verify old names gone
grep -r "DocumentDiscoveryService\|ChunkingService" --include="*.cs" src/Koan.Context/
# Expected: 0 matches

# 3. Verify new names used
grep -r "class Discovery\|class Chunker" --include="*.cs" src/Koan.Context/Services/
# Expected: 9 matches

# 4. Compilation
dotnet build src/Koan.Context/Koan.Context.csproj
# Expected: 0 errors, 0 warnings

# 5. Tests pass
dotnet test src/Koan.Context.Tests/
# Expected: All green
```

### 4.2 MANDATORY: Project Entity Simplification (Week 1, Days 1-2, 4 hours)

**Status:** NON-NEGOTIABLE, blocks schema migrations
**Deadline:** End of Day 2, Week 1

#### Why Mandatory

**Problem:** Project entity has 23 properties, 13 are unused or misplaced
- Audit creep: `CreatedAt`, `UpdatedAt` (not needed for core functionality)
- File monitoring: `MonitorCodeChanges`, `MonitorDocChanges` (feature removed)
- Job tracking: `IndexingStartedAt`, `ActiveJobId` (belongs in Job entity)
- Unused detection: `ProjectType` (9-value enum, never used)

**Impact:**
- Property count: 23 → 10 (57% reduction)
- LOC: ~200 → ~100 (50% reduction)
- Better separation of concerns (job tracking in Job, not Project)

#### Simplified Project Entity

```csharp
public class Project : Entity<Project>
{
    /// <summary>
    /// Unique identifier (GUID v7, auto-generated)
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name (auto-derived from folder name)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to project root directory
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional subdirectory for documentation (e.g., "docs")
    /// </summary>
    public string? DocsPath { get; set; }

    /// <summary>
    /// Last successful indexing timestamp
    /// </summary>
    public DateTime? LastIndexed { get; set; }

    /// <summary>
    /// Current indexing status
    /// </summary>
    public IndexingStatus Status { get; set; } = IndexingStatus.NotIndexed;

    /// <summary>
    /// Total chunks indexed
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// Total bytes of indexed content
    /// </summary>
    public long IndexedBytes { get; set; }

    /// <summary>
    /// Git commit SHA at time of indexing (provenance)
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// Last error message if indexing failed
    /// </summary>
    public string? LastError { get; set; }
}
```

#### Properties Removed (13 total)

| Property | Reason |
|----------|--------|
| `ProjectType` | Unused 9-value enum, adds no value |
| `GitRemote` | Unused field |
| `IsActive` | Unnecessary flag, just query all projects |
| `CreatedAt` | Audit creep |
| `UpdatedAt` | Audit creep |
| `MonitorCodeChanges` | File monitoring feature removed |
| `MonitorDocChanges` | File monitoring feature removed |
| `IndexingStartedAt` | Moved to Job entity |
| `IndexingCompletedAt` | Moved to Job entity |
| `IndexingError` | Replaced by `LastError` |
| `ActiveJobId` | Query `Job.Query(j => j.Status == Indexing)` instead |
| `IsMonitoringEnabled` | Derived property, feature removed |
| `FolderName` | Derived property, compute when needed |

#### Updated IndexingStatus Enum (4 states)

```csharp
public enum IndexingStatus
{
    NotIndexed = 0,  // Project created but never indexed
    Indexing = 1,    // Initial indexing in progress
    Ready = 2,       // Indexed and queryable
    Failed = 3       // Last indexing failed
}
```

**Removed:** `Updating` state (only needed for incremental indexing, which is removed)

---

## 5. Scope Rationalization

### 5.1 Features Removed (1,967 LOC)

#### 5.1.1 File Monitoring System (880 LOC)

**Components:**
- `FileMonitoringService.cs` (~350 LOC) - FileSystemWatcher with 4 event types
- `IncrementalIndexingService.cs` (~280 LOC) - Process file changes
- `DebouncingQueue.cs` (~100 LOC) - Batch change events
- `GitignoreParser.cs` (~150 LOC) - Parse .gitignore rules

**Why Remove:**
- **Separate product feature** - should be optional add-on, not core
- **Adds 450 LOC complexity** with limited value
- **Users can trigger manual re-index** - 30-60 min operation anyway
- **Introduces bugs** - FileSystemWatcher edge cases, symlink handling, gitignore parsing

**User Impact:** NONE - users call `koan-context index` when they want to re-index

#### 5.1.2 Differential Scanning (287 LOC → 100 LOC) ✅ SIMPLIFY & KEEP

**Why Keep Differential Scanning:**

**Scenario:** Re-indexing 10,000-file codebase with 1% change rate (100 files modified)

| Approach | File Scan | Embedding Gen | Total Time | Savings |
|----------|-----------|---------------|------------|---------|
| **Full re-index** | 3-5 min | 30-60 min | **35-68 min** | Baseline |
| **Differential** | 1-2 min (SHA256) | 0.3-0.5 min (100 files only) | **1.5-2.5 min** | **96-97%** |

**Critical Use Cases:**
1. **Provider migration** (Weaviate → Qdrant): Re-index needed, but only metadata changed
2. **Incremental updates** (daily commits): Typical 0.5-2% file change rate
3. **CI/CD integration** (future): 10-50 files per commit, not 10,000

**Conclusion:** Differential scanning provides **50-100× speedup** for re-indexing workflows.

---

**Simplification Strategy:**

**BEFORE (287 LOC - Over-Engineered):**
```csharp
// Complex IndexedFile entity (87 LOC)
public class IndexedFile : Entity<IndexedFile>
{
    public string ProjectId { get; set; }
    public string RelativePath { get; set; }
    public string ContentHash { get; set; }
    public DateTime LastIndexedAt { get; set; }
    public long FileSize { get; set; }
    public DateTime FileModifiedAt { get; set; }  // ❌ Unused, OS metadata unreliable
    public int ChunkCount { get; set; }            // ❌ Denormalized, can query Chunk table
    public string? Category { get; set; }          // ❌ Derived from path
    public bool IsDeleted { get; set; }            // ❌ Soft-delete complexity
    // ... 87 LOC total with validation, indexes, etc.
}

// Complex change detection (200 LOC)
- Parallel SHA256 computation with batching
- Differential deletion (mark IsDeleted=true, cleanup later)
- Category cache invalidation
- Transaction coordination across 3 tables
```

**AFTER (100 LOC - Simplified):**
```csharp
// Simplified IndexedFile entity (40 LOC)
public class IndexedFile : Entity<IndexedFile>
{
    public string ProjectId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;  // SHA256
    public DateTime LastIndexedAt { get; set; }
    public long FileSize { get; set; }
}

// Simplified change detection (60 LOC)
public async Task<List<string>> GetChangedFilesAsync(
    string projectId,
    List<string> currentFiles,
    CancellationToken ct)
{
    var changed = new List<string>();

    foreach (var filePath in currentFiles)
    {
        var currentHash = await ComputeSHA256Async(filePath, ct);
        var indexed = await IndexedFile.Query(
            f => f.ProjectId == projectId && f.RelativePath == filePath,
            ct).FirstOrDefaultAsync();

        if (indexed == null || indexed.ContentHash != currentHash)
        {
            changed.Add(filePath);
        }
    }

    return changed;
}
```

**Complexity Reduction:**
- ❌ Remove soft-delete (IsDeleted flag) → Hard delete orphaned IndexedFile records
- ❌ Remove FileModifiedAt (unreliable, OS-dependent)
- ❌ Remove ChunkCount (denormalized, query instead)
- ❌ Remove Category (derived from path, compute when needed)
- ❌ Remove parallel batching (premature optimization, 1-2 min is acceptable)
- ✅ Keep SHA256-based change detection (core value)
- ✅ Keep simple sequential processing (easier to debug)

**LOC Breakdown:**
- IndexedFile entity: 87 → 40 LOC (47 LOC saved)
- Change detection logic: 200 → 60 LOC (140 LOC saved)
- **Total: 287 → 100 LOC (187 LOC saved, 65% reduction)**

**User Impact:**
- ✅ **Benefit preserved**: 96-97% time savings on re-indexing
- ✅ **Complexity reduced**: 187 LOC removed, simpler maintenance
- ✅ **Bug surface reduced**: No soft-deletes, no parallel coordination bugs

#### 5.1.3 REST API Enhancement (Week 2 Task 3) ✅ ENHANCE & KEEP

**Status:** ENHANCED (not deleted) - Required for web UI and multi-channel access

**Rationale:** On-premise multi-project documentation hub requires web UI for:
- Project management (non-technical users)
- Cross-project semantic search
- Integration with Slack/mobile/CI-CD
- Universal browser access (no CLI barriers)

**Enhanced Controllers:**
- `ProjectsController.cs` - CRUD + health endpoints + bulk indexing
- `SearchController.cs` - Cross-project search + suggestions API
- `McpToolsController.cs` - MCP endpoint (AI agent primary interface)
- `JobsController.cs` - Job status queries for Web UI

**Deleted:**
- `ConfigurationController.cs` (~80 LOC) - Runtime config anti-pattern

**Implementation:**
- ProjectsController uses `EntityController<Project>` base class (Koan pattern)
- SearchController supports cross-project search (ProjectIds array)
- Swagger auto-activated via `Koan.Web.Swagger` project reference
- Rate limiting + request logging for production

**User Impact:** POSITIVE - Dual-channel API (REST for web, MCP for AI agents)

#### 5.1.4 Interface Files (200 LOC) ❌ REMOVE

**Removed:** All 9 interface files (covered in MANDATORY Simplifications)

---

**Total Scope Changes Summary:**
- **LOC Removed:** ~1,600 lines (file monitoring, incremental indexing, ConfigController, interfaces)
- **LOC Simplified:** ~187 lines (differential scanning 287→100)
- **LOC Added:** ~200 lines (REST API enhancements: cross-project search, health endpoints)
- **Net Change:** ~1,587 LOC reduction (39% of codebase)
- **Files Deleted:** 14 files (monitoring, incremental, ConfigController, interfaces)
- **Files Enhanced:** 4 files (ProjectsController, SearchController, IndexedFile, change detection)

### 5.2 Capabilities Preserved

**Critical Point:** ALL user-facing capabilities are preserved:
- ✅ Semantic search with hybrid mode
- ✅ MCP protocol integration
- ✅ Job tracking with progress/ETA (**ESSENTIAL for 30-60 min operations**)
- ✅ Partition isolation
- ✅ Auto-indexing on first query
- ✅ Provenance tracking
- ✅ Vendor independence

**What users lose:** NOTHING - removed features are internal complexity, not user-facing

---

## 6. Implementation Roadmap (4 Weeks)

### 6.1 Week 1: MANDATORY Simplifications + Critical Fixes (40 hours)

**Days 1-2: MANDATORY SIMPLIFICATIONS (12 hours)**

**Priority:** P0 - BLOCKS ALL OTHER WORK

**Tasks:**
1. **Naming Simplification** (8 hours)
   - Delete 9 interface files
   - Rename 9 service classes
   - Update all references (IDE refactoring)
   - Update DI registrations
   - Verify compilation + tests pass

2. **Project Entity Simplification** (4 hours)
   - Remove 13 properties
   - Update all references
   - Test migration script
   - Verify schema updates

**Deliverable (EOD Day 2):**
- ✅ All 9 interface files deleted
- ✅ All services renamed (shorter names, no interfaces)
- ✅ Project entity reduced from 23 → 10 properties
- ✅ Code compiles successfully
- ✅ All tests updated and passing
- ✅ Verification checklist complete

**Days 3-5: CRITICAL FIXES (28 hours)**

**Priority:** P0 - Production blockers

**Task 1: Security Hardening** (8 hours)
- Fix path traversal vulnerability (DocumentDiscoveryService.cs:40)
- Add symlink detection and protection
- Implement input validation for all user-supplied paths
- Fix substring out-of-bounds crash (DocumentDiscoveryService.cs:126)
- **Tests:** 15 test cases covering path traversal attacks

**Task 2: Data Loss Prevention** (12 hours)
- Implement **Transactional Outbox Pattern** for dual-store coordination
- Fix batch vector failure recovery (no more silent data loss)
- Fix unclosed code block handling (emit with warning)
- Fix byte offset double-increment bug (ContentExtractionService.cs:149)
- **Tests:** 25 test cases covering failure scenarios, retry logic

**Task 3: Vendor Independence** (2 hours)
- Remove 5 Weaviate mentions in comments:
  1. Program.cs:69-76 (auto-provisioning comments)
  2. Program.cs:114-118 (service listing)
  3. DocumentChunk.cs:15 (storage model comment)
  4. IndexingService.cs:250 (clear operation comment)
  5. IRetrievalService.cs:79 (metadata example)
- Genericize all references to "vector provider" or "vector store"
- **Keep project reference** (correct "Reference = Intent" pattern)
- **Verification:** Grep scan shows zero Weaviate mentions

**Task 4: Logging & Error Handling** (6 hours)
- Add logging to 6 locations with silent exception swallowing
- Implement proper error messages (user-friendly + technical details)
- Add retry budgets and backoff strategies
- **Tests:** 10 test cases

**Week 1 Quality Gate:**
- [ ] All MANDATORY simplifications complete and verified
- [ ] All 8 critical security/data-loss bugs fixed
- [ ] 100% vendor independence (grep verification passes)
- [ ] 60% test coverage
- [ ] Code compiles with zero warnings

### 6.2 Week 2: Scope Rationalization (40 hours)

**Goal:** Remove unnecessary features, simplify architecture

**Task 1: Remove File Monitoring** (6 hours)
- Delete FileMonitoringService.cs (~350 LOC)
- Delete IncrementalIndexingService.cs (~280 LOC)
- Delete DebouncingQueue.cs (~100 LOC)
- Delete GitignoreParser.cs (~150 LOC)
- Remove `MonitorCodeChanges`, `MonitorDocChanges` from Project entity
- Update tests
- **Deliverable:** 880 LOC removed

**Task 2: Simplify Differential Scanning** (6 hours)
- Simplify IndexedFile entity from 87 → 40 LOC (remove soft-delete, denormalized fields)
- Simplify change detection from 200 → 60 LOC (sequential processing, no batching)
- Update indexing pipeline to use simplified differential logic
- Update tests for simplified schema
- **Deliverable:** 187 LOC removed (287→100), benefit preserved

**Task 3: Enhance REST API for Web UI** (12 hours)
- **Enhance ProjectsController**: Add health endpoint, bulk indexing, use `EntityController<Project>` base
- **Enhance SearchController**: Cross-project search (ProjectIds array), search suggestions API
- **Delete ConfigurationController.cs** (~80 LOC) - Runtime config anti-pattern
- **Verify Swagger** auto-activation via `Koan.Web.Swagger` reference
- Add rate limiting + request logging (production-ready)
- **Deliverable:** -80 LOC (ConfigController), +200 LOC (enhancements), net +120 LOC

**Task 4: Simplify ProjectResolver** (4 hours)
- Reduce from 10 options to 3 (AutoCreate, AutoIndex, MaxSizeGB)
- Simplify resolution logic (256 → 100 LOC)
- Remove ancestor matching, symlink following complexity
- Keep git root detection (essential)
- **Deliverable:** 156 LOC removed

**Task 5: Update All Tests** (23 hours)
- Remove tests for deleted features
- Update tests for renamed entities/services
- Add tests for simplified logic
- Ensure all tests pass
- **Target:** Maintain 60% coverage

**Week 2 Quality Gate:**
- [ ] 1,767 LOC net reduction (43% reduction: 1,680 removed + 187 simplified)
- [ ] 15 files deleted, 2 files simplified
- [ ] Differential scanning simplified and working (96% time savings preserved)
- [ ] All tests passing
- [ ] Architecture simplified
- [ ] 60%+ test coverage maintained

### 6.3 Week 3: Architectural Hardening (40 hours)

**Goal:** Production-grade reliability and observability

**Task 1: Outbox Background Worker** (8 hours)
```csharp
public class SyncOperation : Entity<SyncOperation>
{
    public string ChunkId { get; set; }
    public string EmbeddingJson { get; set; }  // Serialized float[]
    public OperationStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VectorSyncWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pending = await SyncOperation.Query(
                op => op.Status == OperationStatus.Pending && op.RetryCount < 5,
                ct);

            foreach (var operation in pending)
            {
                await ProcessWithRetryAsync(operation, ct);
            }

            await Task.Delay(5000, ct);  // Poll every 5 seconds
        }
    }
}
```
**Tests:** 15 test cases (success, retry, dead-letter queue)

**Task 2: Observability (OpenTelemetry)** (10 hours)
```csharp
// Metrics
public static Counter<long> ChunksIndexed = _meter.CreateCounter<long>(
    "koan.context.chunks.indexed",
    description: "Total chunks indexed");

public static Histogram<double> SearchLatency = _meter.CreateHistogram<double>(
    "koan.context.search.latency",
    unit: "ms",
    description: "Search query latency");

public static ObservableGauge<long> OutboxPending = _meter.CreateObservableGauge(
    "koan.context.outbox.pending",
    () => SyncOperation.Count(op => op.Status == OperationStatus.Pending),
    description: "Pending outbox operations");

// Traces
using var activity = _activitySource.StartActivity("IndexProject");
activity?.SetTag("project.id", projectId);

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite")
    .AddCheck<VectorStoreHealthCheck>("vector-store")
    .AddCheck<OutboxHealthCheck>("outbox");
```
**Endpoints:** `/health`, `/health/ready`, `/metrics`
**Tests:** 10 test cases

**Task 3: Error Handling & Resilience (Polly)** (8 hours)
```csharp
public static readonly IAsyncPolicy EmbeddingRetry = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

public static readonly IAsyncPolicy CircuitBreaker = Policy
    .Handle<Exception>()
    .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromMinutes(1));
```
**Tests:** 20 test cases (retry scenarios, circuit breaker trips)

**Task 4: Backup & Restore** (6 hours)
```bash
# CLI commands
koan-context backup ./backup-dir --project <id>
koan-context restore ./backup-dir --project <id>
```
```csharp
public async Task BackupAsync(string projectId, string outputDir, CancellationToken ct)
{
    // 1. Backup SQLite chunks (VACUUM INTO)
    await session.Execute($"VACUUM INTO '{outputPath}'", ct);

    // 2. Export vector store (provider-agnostic)
    await Vector<Chunk>.ExportAsync(Path.Combine(outputDir, "vectors.bin"), ct);

    // 3. Write manifest
    var manifest = new { Version = "1.0", BackupDate = DateTime.UtcNow, ... };
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest), ct);
}
```
**Tests:** 8 test cases (backup/restore round-trip)

**Task 5: Reconciliation UI** (4 hours)
- Web UI page to show:
  - Chunks in SQLite without vectors (orphaned metadata)
  - Vectors without chunks (orphaned embeddings)
  - Button: "Sync Now" - triggers reconciliation
- **Tests:** 6 test cases

**Task 6: Integration Testing** (4 hours)
- End-to-end indexing flow
- Dual-store coordinator
- Outbox worker retry logic
- MCP endpoint

**Week 3 Quality Gate:**
- [ ] Outbox pattern working
- [ ] Observability instrumented (metrics, traces, health checks)
- [ ] Error handling hardened (Polly policies, circuit breakers)
- [ ] Backup/restore working
- [ ] 70% test coverage

### 6.4 Week 4: Polish & Documentation (40 hours)

**Goal:** Production-ready documentation and final testing

**Task 1: Comprehensive Testing** (16 hours)
- **Unit tests:** All service classes (target 85% coverage)
- **Integration tests:** SQLite + Vector dual-store, outbox worker
- **E2E tests:** CLI index command, MCP search endpoint, Web UI job status
- **Performance tests:** Index 10,000 files (target <60 minutes with local embeddings)
- **Load tests:** 100 concurrent search queries (target P95 <500ms)
- **Security tests:** Path traversal scenarios, input validation

**Task 2: Documentation** (12 hours)

**README.md** (5-minute quick start):
```markdown
# Koan.context - Semantic Code Search for AI Agents

## Quick Start

### 1. Install
```bash
dotnet tool install -g koan.context
```

### 2. Choose Vector Provider (Reference = Intent)
```xml
<!-- Weaviate (containerized, auto-provisioned) -->
<ProjectReference Include="Koan.Data.Vector.Connector.Weaviate" />

<!-- OR Qdrant (containerized, auto-provisioned) -->
<ProjectReference Include="Koan.Data.Vector.Connector.Qdrant" />

<!-- OR Pinecone (cloud, requires API key) -->
<ProjectReference Include="Koan.Data.Vector.Connector.Pinecone" />
```

### 3. Index Your Project
```bash
cd /path/to/your/project
koan-context index .
# This will take 30-60 minutes for large projects (embedding generation is slow)
```

### 4. Search from AI Agent
```bash
claude --mcp http://localhost:27500
> search "authentication middleware"
```
```

**ARCHITECTURE.md** (1-page diagram + explanation):
- Component diagram
- Data flow (indexing + search)
- Dual-store coordination (outbox pattern)
- Partition isolation

**OPERATORS.md** (runbook):
- Installation
- Configuration
- Monitoring (metrics/traces)
- Backup/restore
- Troubleshooting
- Performance tuning

**API.md** (MCP endpoint specification):
- `/api/search` request/response format
- Authentication (localhost-only by default)
- Error codes

**Task 3: Compliance Readiness** (8 hours)
- Security audit prep (document all data flows)
- HIPAA/SOC2 readiness guide (map controls to implementation)
- Access control matrix
- Audit logging design
- **Deliverable:** Compliance guide for Phase 2 certification

**Task 4: Final Polish** (4 hours)
- Fix any remaining test failures
- Address code review feedback
- Update copyright headers
- Verify zero vendor mentions (grep scan)
- Run static analysis (CodeQL, SonarQube)

**Week 4 Quality Gate:**
- [ ] 85%+ test coverage
- [ ] All quality gates passed
- [ ] Documentation complete
- [ ] Zero critical bugs
- [ ] Production-ready

---

## 7. Success Metrics & Quality Gates

### 7.1 Technical Metrics

| Metric | Current | Week 1 Target | Week 4 Target | Measurement |
|--------|---------|---------------|---------------|-------------|
| **Critical Bugs** | 8 | 0 | 0 | QA audit |
| **Test Coverage** | 0% | 60% | 85%+ | CodeCov |
| **Vendor Mentions** | 8 | 0 | 0 | Grep scan |
| **LOC** | ~4,100 | ~3,700 | ~2,333 | Cloc (1,767 net reduction) |
| **Interface Files** | 9 | 0 | 0 | File count |
| **Entity Properties (Project)** | 23 | 10 | 10 | Manual count |
| **Entity Properties (IndexedFile)** | 9 | 5 | 5 | Simplified for differential scanning |
| **Data Consistency** | Unknown | 99.9% | 99.9% | Outbox replay tests |
| **Re-index Time (10k, 1% change)** | 35-68 min | 1.5-2.5 min | 1.5-2.5 min | Differential scanning benchmark |
| **Search Latency P95** | Unknown | <500ms | <200ms | Load test |
| **Outbox Lag P99** | N/A | <5 min | <5 min | Production telemetry |

### 7.2 Quality Gates (Must Pass)

**Week 1 Gate:**
- [ ] All MANDATORY simplifications complete (naming + entity)
- [ ] All 8 critical bugs fixed
- [ ] 100% vendor independence (grep: zero "Weaviate" in code)
- [ ] 60% test coverage
- [ ] Code compiles with zero warnings

**Week 2 Gate:**
- [ ] 1,767 LOC net reduction (1,680 removed + 187 simplified)
- [ ] 15 files deleted, 2 files simplified
- [ ] Differential scanning working (benchmark: <2.5 min for 1% change rate)
- [ ] All tests passing
- [ ] 60%+ test coverage maintained

**Week 3 Gate:**
- [ ] Outbox pattern working
- [ ] Observability implemented (metrics, traces, health checks)
- [ ] 70% test coverage

**Week 4 Gate (Production Ready):**
- [ ] 85%+ test coverage
- [ ] Zero critical bugs
- [ ] Zero vendor mentions (verified)
- [ ] Dual-store consistency 99.9%+
- [ ] Security scan passes (no critical/high findings)
- [ ] Performance benchmarks meet targets
- [ ] Documentation complete
- [ ] Manual testing of all user journeys complete

### 7.3 Business Metrics (Post-Launch)

| Metric | 3 Months | 6 Months | 12 Months |
|--------|----------|----------|-----------|
| **Developer Adoptions** | 50+ | 200+ | 500+ |
| **Enterprise Trials** | 2+ | 5+ | 10+ |
| **Compliance Certifications** | 0 (roadmap) | 1 (SOC2 Type I) | 2 (+ HIPAA) |
| **Vector Provider Support** | 3 (Weaviate, Qdrant, Pinecone) | 5 (+ Chroma, Milvus) | 7+ |
| **GitHub Stars** | 50+ | 200+ | 1000+ |

---

## 8. Budget & Resource Requirements

### 8.1 Team Structure

**Core Team (4 weeks):**
- 1 Senior Engineer (40 hrs/week × 4 weeks = 160 hrs)
- 1 Mid-Level Engineer (40 hrs/week × 4 weeks = 160 hrs)
- 0.5 QA Engineer (20 hrs/week × 4 weeks = 40 hrs)

**Total Effort:** 360 hours

### 8.2 Budget

**External Consultancy:**
| Role | Hours | Rate | Cost |
|------|-------|------|------|
| Senior Engineer | 160 | $175/hr (avg) | $28,000 |
| Mid-Level Engineer | 160 | $125/hr (avg) | $20,000 |
| QA Engineer | 40 | $100/hr (avg) | $4,000 |
| **Total Labor** | 360 | | **$52,000** |

**Additional Costs:**
- External security audit (pentest): $15,000-25,000
- CI/CD infrastructure: $500/month
- Vector store hosting (dev): $200/month

**Total External Budget:** $70,000-$80,000

**Internal Team:**
- 2 FTE × 4 weeks = 0.5 FTE-years
- Loaded cost: $80k-$110k per FTE-year
- **Internal Budget:** $40,000-$55,000

### 8.3 Infrastructure

**Development:**
- Vector store (containerized): Local Docker
- SQLite: Local file
- Embedding API: Local Ollama or cloud (OpenAI $50/month)

**Production (per deployment):**
- Vector store: $100-500/month (depends on data volume)
- SQLite: Included (file-based)
- Embedding API: Variable ($0-1000/month based on usage)

---

## 9. Risk Assessment

### 9.1 Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Outbox pattern complexity** | MEDIUM | MEDIUM | Thorough testing, phased rollout |
| **Performance degradation from simplifications** | LOW | MEDIUM | Benchmarks before/after, load testing |
| **Breaking changes for users** | NONE | N/A | Zero users (greenfield) |
| **Timeline slippage** | MEDIUM | LOW | 4-week estimate has 20% buffer built in |
| **Incomplete testing** | LOW | HIGH | Dedicated QA engineer, 85% coverage mandatory |
| **Vendor mentions missed** | LOW | LOW | Automated grep verification in CI/CD |

### 9.2 Business Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **GitHub Copilot adds on-prem** | MEDIUM | HIGH | Emphasize multi-agent positioning, open source moat |
| **Low adoption (<50 in 3 months)** | MEDIUM | HIGH | DX showcase, docs, community engagement |
| **Compliance costs exceed budget** | HIGH | MEDIUM | Defer certifications to Phase 2, focus on Koan users first |
| **Vector provider lock-in perception** | LOW | MEDIUM | Marketing emphasizes "Reference = Intent" pattern |

### 9.3 Execution Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **MANDATORY simplifications delayed** | LOW | CRITICAL | Days 1-2 are sacred, no other work starts until complete |
| **Team bandwidth insufficient** | LOW | MEDIUM | 2 FTE committed for 4 weeks, no distractions allowed |
| **Quality gate failures** | MEDIUM | HIGH | Weekly review, adjust scope if needed (Week 4 polish is flex) |

---

## 10. Appendices

### Appendix A: Code Structure (After Refactoring)

```
src/Koan.Context/
├─ Models/
│  ├─ Chunk.cs (renamed from DocumentChunk)
│  ├─ Project.cs (simplified: 10 properties)
│  ├─ Job.cs (kept as "Job" - only one kind of job in context)
│  └─ SyncOperation.cs (new: outbox pattern, renamed from VectorOperation)
│
├─ Services/
│  ├─ Discovery.cs (renamed, no interface)
│  ├─ Extraction.cs (renamed, no interface)
│  ├─ Chunker.cs (renamed, no interface)
│  ├─ Embedding.cs (singular, renamed, no interface)
│  ├─ Indexer.cs (renamed, no interface)
│  ├─ Search.cs (renamed, no interface)
│  ├─ TokenCounter.cs (explicit name, renamed, no interface)
│  ├─ Pagination.cs (renamed, no interface)
│  ├─ UrlBuilder.cs (renamed, no interface)
│  ├─ ProjectResolver.cs (simplified: 100 LOC)
│  ├─ DualStoreCoordinator.cs (new: outbox pattern)
│  ├─ VectorSyncWorker.cs (new: background worker)
│  ├─ ReconciliationService.cs (new: consistency checks)
│  └─ BackupService.cs (new: data export/import)
│
├─ Controllers/
│  ├─ McpToolsController.cs (MCP endpoint - AI agent primary interface)
│  ├─ ProjectsController.cs (REST API - web UI + health + bulk index)
│  ├─ SearchController.cs (REST API - cross-project search)
│  └─ JobsController.cs (job status queries for Web UI)
│
├─ Utilities/
│  ├─ PathValidator.cs (new: security)
│  └─ Metrics.cs (new: observability)
│
└─ Program.cs (zero vendor mentions in comments)

Total Files: 29 (vs. 39 before) - kept REST controllers for web UI
Total LOC: ~2,300 (vs. ~4,100 before) - includes REST API enhancements
```

### Appendix B: Vendor Independence Verification

**✅ CONFIRMED:** Vector provider abstraction exists in Koan Framework

**Evidence from S5.Recs and S6.SnapVault samples:**
```csharp
// S5.Recs sample (lines 112, 171, 279)
if (Vector<Media>.IsAvailable) {
    var vectorResults = await Vector<Media>.Search(
        vector: queryVector,
        text: query,  // Hybrid search
        alpha: effectiveAlpha,
        topK: topK,
        filter: vectorFilter,
        ct: ct);
}

// S6.SnapVault sample (line 246)
await VectorData<PhotoAsset>.SaveWithVector(photo, embedding, vectorMetadata, ct);

// Embedding API (line 235)
var embedding = await Koan.AI.Ai.Embed(embeddingText, ct);
```

**Project References from Samples:**
```xml
<!-- Both S5 and S6 reference: -->
<ProjectReference Include="..\Koan.Data.Vector.Abstractions\..." />
<ProjectReference Include="..\Koan.Data.Vector\..." />
<ProjectReference Include="..\Koan.Data.Vector.Connector.Weaviate\..." />
```

**Grep Audit (post-refactoring):**
```bash
# Search for vendor mentions in business logic
grep -r -i "weaviate\|qdrant\|pinecone\|chroma\|milvus" \
  --include="*.cs" \
  src/Koan.Context/ | \
  grep -v "\.csproj"

# Expected result: 0 matches (except project references)
```

**Project Reference (CORRECT - Keep This):**
```xml
<!-- This is the "Reference = Intent" pattern - DO NOT REMOVE -->
<ProjectReference Include="..\Connectors\Data\Vector\Weaviate\Koan.Data.Vector.Connector.Weaviate.csproj" />
```

**How Provider Selection Works:**
1. Reference Weaviate project → KoanAutoRegistrar detects → Weaviate adapter loads
2. Reference Qdrant project instead → Qdrant adapter loads
3. **Zero code changes needed** - just swap the project reference

**Koan Framework APIs Used:**
- `Vector<T>.Search()` - Provider-agnostic vector search
- `Vector<T>.IsAvailable` - Capability detection
- `VectorData<T>.SaveWithVector()` - Dual-store pattern helper
- `Koan.AI.Ai.Embed()` - Provider-agnostic embedding generation

### Appendix C: Testing Matrix

| Test Type | Target Coverage | Test Count | Focus Areas |
|-----------|----------------|------------|-------------|
| **Unit** | 85%+ | 150+ cases | All services, path validation, chunking, offset calculation |
| **Integration** | Key flows | 40+ cases | SQLite + Vector dual-store, outbox worker, partition isolation |
| **E2E** | User journeys | 10+ cases | CLI commands, MCP endpoint, Web UI, agent integration |
| **Performance** | Benchmarks | 5+ cases | 10k chunks indexing, 100 concurrent searches, outbox lag |
| **Security** | Attack vectors | 15+ cases | Path traversal, input validation, symlink attacks |

### Appendix D: Comparison with Previous Proposal

| Aspect | Previous (Hardening) | This Proposal (Approved) | Difference |
|--------|---------------------|-------------------------|------------|
| **Timeline** | 6-8 weeks | 4 weeks | 40% faster |
| **Budget** | $120k-160k | $52k-80k | 50% cheaper |
| **LOC** | ~4,100 | ~2,333 | 43% reduction (1,767 LOC) |
| **Grade Assessment** | B+ → A- | **C- → A-** | Honest assessment |
| **Test Coverage** | 0% → 60% | 0% → 85% | Higher quality |
| **Vendor Independence** | 95% | 100% | Fully agnostic |
| **Job Tracking** | ✅ Kept | ✅ **Kept (CRITICAL)** | Correctly preserved |
| **Differential Scanning** | Removed (287 LOC) | **Simplified (287→100 LOC)** | Benefit preserved, complexity reduced |
| **Scope Creep** | Preserved | **Reduced (1,680 LOC removed)** | Leaner |
| **Dual-Store Sync** | Best-effort transactions | **Outbox pattern** | More reliable |
| **MANDATORY Simplifications** | Not specified | **Week 1, Days 1-2** | Clear priorities |

---

## Approval & Next Steps

**Document Status:** ✅ **APPROVED FOR IMPLEMENTATION**
**Approval Date:** 2025-11-07
**Review Cadence:** Weekly during execution, monthly post-launch
**Next Review:** 2025-11-14 (end of Week 1)

**Immediate Actions (Next 48 Hours):**

**Day 1:**
- ✅ Budget approved: $52k-80k (external) or $40k-55k (internal)
- ✅ Team allocated: 2 FTE for 4 weeks (1 senior, 1 mid-level + 0.5 QA)
- ✅ Sprint planning: Week 1 tasks assigned
- ✅ Environment setup: Dev machines, CI/CD pipeline

**Day 2 (Week 1, Day 1):**
- ✅ **BEGIN MANDATORY SIMPLIFICATIONS**
- Hour 1: Delete 9 interface files
- Hour 2-8: Rename services, update references
- Hours 1-4: Simplify Project entity
- EOD: Verification checklist complete, all tests passing

**Next Milestone:** End of Week 1, Day 2 - MANDATORY simplifications complete and verified

**Success Criteria for Week 1:**
- All MANDATORY simplifications complete
- All 8 critical bugs fixed
- 100% vendor independence
- 60% test coverage
- Ready to proceed to Week 2

---

**Related Documentation:**
- [Transaction Support ADR](../decisions/DATA-0078-ambient-transaction-coordination.md)
- [Transaction Usage Guide](../guides/transactions-usage-guide.md)
- [QA Report 2025-11-05](../qa/Koan-Context-QA-Report-2025-11-05.md)
- [MCP Protocol Specification](https://modelcontextprotocol.io/)

---

## 11. Agent Implementation Guide

**For AI Agents Implementing This Proposal**

This section provides practical guidance for agentic code sessions executing the 4-week implementation plan. Follow these patterns to maintain consistency and quality.

### 11.1 Context Preservation Between Sessions

**Critical Files to Reference:**
```
ALWAYS READ FIRST (establish context):
1. docs/proposals/KOAN-CONTEXT-HARDENING.md (this file)
2. docs/qa/Koan-Context-QA-Report-2025-11-05.md (bug list)
3. src/Koan.Context/Models/DocumentChunk.cs (current entity structure)
4. src/Koan.Context/Services/IndexingService.cs (main pipeline)

REFERENCE WHEN NEEDED:
- docs/decisions/DATA-0078-ambient-transaction-coordination.md (transaction patterns)
- docs/guides/transactions-usage-guide.md (transaction examples)
- .claude/CLAUDE.md (framework principles, anti-patterns)
```

**State Tracking Pattern:**
```markdown
At start of each session, create checklist:
- [ ] Week X, Task Y in progress
- [ ] Files modified: [list]
- [ ] Tests added: [count]
- [ ] Quality gates passed: [list]
```

### 11.2 Week 1: MANDATORY Simplifications

#### Session 1: Naming Simplification (4 hours)

**Objective:** Delete 9 interface files, rename services

**Step-by-Step:**
```bash
# 1. Verify current state
ls -1 src/Koan.Context/Services/I*.cs
# Expected: 9 interface files

# 2. Delete interface files (ONE AT A TIME, verify compilation after each)
rm src/Koan.Context/Services/IDocumentDiscoveryService.cs
dotnet build src/Koan.Context/Koan.Context.csproj
# If build fails, fix references before proceeding

# 3. Rename implementation files
mv src/Koan.Context/Services/DocumentDiscoveryService.cs \
   src/Koan.Context/Services/Discovery.cs
```

**Critical Pattern:**
```csharp
// BEFORE (in DocumentDiscoveryService.cs)
public class DocumentDiscoveryService : IDocumentDiscoveryService
{
    // implementation
}

// AFTER (in Discovery.cs)
public class Discovery
{
    // implementation - NO INTERFACE
}
```

**Verification Script:**
```bash
# Run after EACH service rename
./verify-naming-simplification.sh

# Contents:
#!/bin/bash
echo "Checking for remaining interfaces..."
find src/Koan.Context/Services -name "I*.cs" -type f
echo "Expected: 0 files"

echo "Checking for old service names..."
grep -r "DocumentDiscoveryService\|ChunkingService\|EmbeddingService" \
  --include="*.cs" src/Koan.Context/ | wc -l
echo "Expected: 0 matches"

echo "Compiling..."
dotnet build src/Koan.Context/Koan.Context.csproj
```

**Common Pitfalls:**
1. ❌ **Renaming all files at once** → Compile errors cascade, hard to debug
   - ✅ **Do:** Rename one service, fix all references, verify compilation, THEN next service

2. ❌ **Forgetting DI registrations** → Runtime errors
   - ✅ **Do:** Update `Program.cs` DI registrations immediately after rename

3. ❌ **Missing test updates** → Test failures
   - ✅ **Do:** Search for old class name in test projects, update mocks

**Decision Tree:**
```
Q: Does IDE refactoring fail?
├─ Yes → Manual find/replace in specific files
└─ No → Use IDE refactoring (faster)

Q: Compilation fails after rename?
├─ Error: "Type not found" → Check DI registration in Program.cs
├─ Error: "Namespace conflict" → Check using statements
└─ Error: "Circular dependency" → Check service constructor parameters
```

#### Session 2: Entity Simplification (4 hours)

**Objective:** Reduce Project entity from 23 → 10 properties

**File Location:** `src/Koan.Context/Models/Project.cs`

**Step-by-Step:**
```csharp
// 1. Comment out properties first (don't delete yet)
public class Project : Entity<Project>
{
    // KEEP
    public string Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? DocsPath { get; set; }
    public DateTime? LastIndexed { get; set; }
    public IndexingStatus Status { get; set; } = IndexingStatus.NotIndexed;
    public int DocumentCount { get; set; }
    public long IndexedBytes { get; set; }
    public string? CommitSha { get; set; }
    public string? LastError { get; set; }

    // REMOVE (comment out first, delete after verification)
    // public ProjectType ProjectType { get; set; }
    // public string? GitRemote { get; set; }
    // public bool IsActive { get; set; }
    // public DateTime CreatedAt { get; set; }
    // public DateTime UpdatedAt { get; set; }
    // public bool MonitorCodeChanges { get; set; }
    // public bool MonitorDocChanges { get; set; }
    // public DateTime? IndexingStartedAt { get; set; }
    // public DateTime? IndexingCompletedAt { get; set; }
    // public string? IndexingError { get; set; }
    // public string? ActiveJobId { get; set; }
}

// 2. Compile and fix all reference errors
dotnet build src/Koan.Context/Koan.Context.csproj

// 3. Once compilation succeeds, delete commented properties
```

**Property Migration Map:**
```csharp
// IndexingError → LastError (rename, keep concept)
if (!string.IsNullOrEmpty(project.IndexingError))
{
    project.LastError = project.IndexingError;
}

// IndexingStartedAt, IndexingCompletedAt → Move to Job entity
// Query Job entity instead: Job.Query(j => j.ProjectId == projectId && j.Status == Indexing)

// MonitorCodeChanges, MonitorDocChanges → Feature removed, delete outright

// ProjectType → Unused enum, delete outright
```

**Verification Checklist:**
```bash
# 1. Property count
grep -c "public.*{.*get.*set.*}" src/Koan.Context/Models/Project.cs
# Expected: 10

# 2. No removed properties referenced
grep -r "MonitorCodeChanges\|IndexingStartedAt\|ProjectType" \
  --include="*.cs" src/Koan.Context/
# Expected: 0 matches

# 3. Compilation
dotnet build src/Koan.Context/Koan.Context.csproj
# Expected: 0 errors, 0 warnings
```

### 11.3 Week 1: Critical Fixes

#### Security: Path Traversal Fix

**File Location:** `src/Koan.Context/Services/DocumentDiscoveryService.cs:40` (or `Discovery.cs` after rename)

**Vulnerable Code Pattern:**
```csharp
// BEFORE (VULNERABLE)
public async Task<List<string>> DiscoverFilesAsync(string basePath, string? relativePath)
{
    var targetPath = string.IsNullOrEmpty(relativePath)
        ? basePath
        : Path.Combine(basePath, relativePath);  // ❌ NO VALIDATION

    return Directory.GetFiles(targetPath, "*.md", SearchOption.AllDirectories).ToList();
}
```

**Fixed Pattern:**
```csharp
// AFTER (SECURE)
public async Task<List<string>> DiscoverFilesAsync(string basePath, string? relativePath)
{
    // 1. Normalize base path
    var normalizedBase = Path.GetFullPath(basePath);

    // 2. Normalize target path
    var targetPath = string.IsNullOrEmpty(relativePath)
        ? normalizedBase
        : Path.GetFullPath(Path.Combine(normalizedBase, relativePath));

    // 3. Verify target is within base (CRITICAL SECURITY CHECK)
    if (!targetPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
    {
        throw new SecurityException(
            $"Path traversal detected: '{relativePath}' escapes base directory");
    }

    // 4. Check for symlinks (prevent infinite loops)
    var dirInfo = new DirectoryInfo(targetPath);
    if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
    {
        _logger.LogWarning("Skipping symlink: {Path}", targetPath);
        return new List<string>();
    }

    return Directory.GetFiles(targetPath, "*.md", SearchOption.AllDirectories).ToList();
}
```

**Test Cases Required:**
```csharp
[Fact]
public async Task DiscoverFiles_PathTraversal_ThrowsSecurityException()
{
    var basePath = "/project/root";
    var attackPath = "../../etc/passwd";  // Attempt to escape

    var ex = await Assert.ThrowsAsync<SecurityException>(
        () => _discovery.DiscoverFilesAsync(basePath, attackPath));

    ex.Message.Should().Contain("Path traversal detected");
}

[Fact]
public async Task DiscoverFiles_Symlink_SkipsDirectory()
{
    // Create symlink: /project/root/link -> /outside/directory
    var basePath = CreateTestDirectory();
    var symlinkPath = CreateSymlink(basePath, "/outside/directory");

    var files = await _discovery.DiscoverFilesAsync(basePath, null);

    files.Should().NotContain(f => f.Contains("link"));
}
```

#### Data Loss: Outbox Pattern

**New Files to Create:**
```
src/Koan.Context/Models/SyncOperation.cs
src/Koan.Context/Services/VectorSyncWorker.cs
src/Koan.Context/Services/DualStoreCoordinator.cs
```

**SyncOperation Entity (renamed from VectorOperation):**
```csharp
// src/Koan.Context/Models/SyncOperation.cs
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Outbox pattern entity for dual-store coordination (SQLite + Vector)
/// </summary>
public class SyncOperation : Entity<SyncOperation>
{
    public string ChunkId { get; set; } = string.Empty;
    public string EmbeddingJson { get; set; } = string.Empty;  // Serialized float[]
    public OperationStatus Status { get; set; } = OperationStatus.Pending;
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum OperationStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
```

**Critical Pattern - Dual-Store Save:**
```csharp
// src/Koan.Context/Services/DualStoreCoordinator.cs
public async Task SaveChunkAsync(Chunk chunk, float[] embedding, CancellationToken ct)
{
    // Phase 1: Atomic SQLite transaction (chunk + outbox entry)
    using var tx = await BeginTransactionAsync(ct);
    try
    {
        // Save chunk metadata to SQLite
        await chunk.Save(ct);

        // Save outbox entry (ensures at-least-once delivery)
        var operation = new SyncOperation
        {
            ChunkId = chunk.Id,
            EmbeddingJson = JsonSerializer.Serialize(embedding),
            Status = OperationStatus.Pending
        };
        await operation.Save(ct);

        await tx.CommitAsync(ct);
    }
    catch
    {
        await tx.RollbackAsync(ct);
        throw;
    }

    // Phase 2: Immediate attempt to sync to vector store (best-effort)
    try
    {
        await Vector<Chunk>.Save(chunk.Id, embedding, ct);

        // Mark outbox entry as completed
        operation.Status = OperationStatus.Completed;
        operation.ProcessedAt = DateTime.UtcNow;
        await operation.Save(ct);
    }
    catch (Exception ex)
    {
        // Log but don't fail - background worker will retry
        _logger.LogWarning(ex, "Vector save deferred to background worker for chunk {ChunkId}", chunk.Id);
    }
}
```

**Background Worker:**
```csharp
// src/Koan.Context/Services/VectorSyncWorker.cs
public class VectorSyncWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pending = await SyncOperation.Query(
                    op => op.Status == OperationStatus.Pending && op.RetryCount < 5,
                    ct);

                foreach (var operation in pending)
                {
                    await ProcessOperationAsync(operation, ct);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox worker error");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);  // Back off on error
            }
        }
    }

    private async Task ProcessOperationAsync(SyncOperation op, CancellationToken ct)
    {
        try
        {
            // Deserialize embedding
            var embedding = JsonSerializer.Deserialize<float[]>(op.EmbeddingJson);

            // Attempt to save to vector store
            await Vector<Chunk>.Save(op.ChunkId, embedding, ct);

            // Mark as completed
            op.Status = OperationStatus.Completed;
            op.ProcessedAt = DateTime.UtcNow;
            await op.Save(ct);

            _logger.LogInformation("Successfully synced chunk {ChunkId} to vector store", op.ChunkId);
        }
        catch (Exception ex)
        {
            op.RetryCount++;
            op.ErrorMessage = ex.Message;

            if (op.RetryCount >= 5)
            {
                op.Status = OperationStatus.Failed;
                _logger.LogError(ex, "Failed to sync chunk {ChunkId} after 5 retries", op.ChunkId);
            }

            await op.Save(ct);
        }
    }
}
```

### 11.4 Week 2: Simplified Differential Scanning

**Objective:** Reduce IndexedFile complexity while preserving 96% time savings on re-indexing

#### Simplified IndexedFile Entity

**File Location:** `src/Koan.Context/Models/IndexedFile.cs`

**Implementation:**
```csharp
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Tracks file content hashes for differential re-indexing.
/// Enables 96-97% time savings on re-index by processing only changed files.
/// </summary>
public class IndexedFile : Entity<IndexedFile>
{
    /// <summary>
    /// Project ID (partition key)
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Relative path from project root (e.g., "docs/guide.md")
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of file contents (hex string, 64 chars)
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Last time this file was successfully indexed
    /// </summary>
    public DateTime LastIndexedAt { get; set; }

    /// <summary>
    /// File size in bytes (used for progress estimation)
    /// </summary>
    public long FileSize { get; set; }
}
```

**Total:** 40 LOC (vs. 87 LOC original)

#### Simplified Change Detection

**File Location:** `src/Koan.Context/Services/Indexer.cs` (or `IndexingService.cs` before rename)

**Implementation:**
```csharp
/// <summary>
/// Detects which files have changed since last indexing.
/// Returns list of files requiring re-indexing.
/// </summary>
private async Task<List<string>> GetChangedFilesAsync(
    string projectId,
    List<string> discoveredFiles,
    CancellationToken ct)
{
    var changedFiles = new List<string>();
    var rootPath = _project.RootPath;

    foreach (var filePath in discoveredFiles)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);

        // Compute current hash
        var currentHash = await ComputeSHA256Async(filePath, ct);

        // Check if file exists in index
        var indexed = await IndexedFile.Query(
            f => f.ProjectId == projectId && f.RelativePath == relativePath,
            ct).FirstOrDefaultAsync();

        // File is new or content changed
        if (indexed == null || indexed.ContentHash != currentHash)
        {
            changedFiles.Add(filePath);
        }
    }

    return changedFiles;
}

/// <summary>
/// Computes SHA256 hash of file contents
/// </summary>
private async Task<string> ComputeSHA256Async(string filePath, CancellationToken ct)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var hashBytes = await sha256.ComputeHashAsync(stream, ct);
    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
}

/// <summary>
/// Updates IndexedFile manifest after successful indexing
/// </summary>
private async Task UpdateIndexedFileAsync(
    string projectId,
    string filePath,
    string contentHash,
    long fileSize,
    CancellationToken ct)
{
    var relativePath = Path.GetRelativePath(_project.RootPath, filePath);

    var indexed = await IndexedFile.Query(
        f => f.ProjectId == projectId && f.RelativePath == relativePath,
        ct).FirstOrDefaultAsync();

    if (indexed == null)
    {
        indexed = new IndexedFile
        {
            ProjectId = projectId,
            RelativePath = relativePath
        };
    }

    indexed.ContentHash = contentHash;
    indexed.FileSize = fileSize;
    indexed.LastIndexedAt = DateTime.UtcNow;

    await indexed.Save(ct);
}
```

**Total:** 60 LOC (vs. 200 LOC original)

#### Integration with Indexing Pipeline

**Modified:** `IndexProjectAsync` method in `Indexer.cs`

```csharp
public async Task IndexProjectAsync(string projectId, CancellationToken ct)
{
    var project = await Project.Get(projectId, ct);

    // 1. Discover all files
    var allFiles = await _discovery.DiscoverFilesAsync(project.RootPath, null, ct);

    // 2. Differential: Get only changed files
    var filesToIndex = await GetChangedFilesAsync(projectId, allFiles, ct);

    _logger.LogInformation(
        "Differential scan: {Changed}/{Total} files changed ({Percent:F1}%)",
        filesToIndex.Count,
        allFiles.Count,
        (filesToIndex.Count * 100.0) / allFiles.Count);

    // 3. Delete chunks for changed files only (not full partition clear)
    foreach (var filePath in filesToIndex)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, filePath);
        await Chunk.Query(
            c => c.ProjectId == projectId && c.FilePath == relativePath,
            ct).DeleteAsync();
    }

    // 4. Index changed files
    foreach (var filePath in filesToIndex)
    {
        var document = await _extraction.ExtractAsync(filePath, ct);
        var chunks = await _chunker.ChunkAsync(document, projectId, ct);

        foreach (var chunk in chunks)
        {
            var embedding = await _embeddings.GenerateAsync(chunk.Text, ct);
            await _coordinator.SaveChunkAsync(chunk, embedding, ct);
        }

        // 5. Update IndexedFile manifest
        var contentHash = await ComputeSHA256Async(filePath, ct);
        var fileSize = new FileInfo(filePath).Length;
        await UpdateIndexedFileAsync(projectId, filePath, contentHash, fileSize, ct);
    }

    // 6. Cleanup: Remove IndexedFile entries for deleted files
    var currentRelativePaths = allFiles
        .Select(f => Path.GetRelativePath(project.RootPath, f))
        .ToHashSet();

    var indexedFiles = await IndexedFile.Query(
        f => f.ProjectId == projectId,
        ct).ToListAsync();

    foreach (var indexed in indexedFiles)
    {
        if (!currentRelativePaths.Contains(indexed.RelativePath))
        {
            await indexed.Delete(ct);
        }
    }
}
```

#### Performance Benchmark Test

**File Location:** `src/Koan.Context.Tests/Performance/DifferentialScanningTests.cs`

```csharp
[Fact]
public async Task DifferentialScanning_1PercentChange_Completes_Under3Minutes()
{
    // Arrange: Create 10,000 file index
    var projectId = await CreateTestProjectAsync(fileCount: 10_000);

    // Act 1: Initial full index (establish baseline)
    var fullIndexTime = await MeasureAsync(() => _indexer.IndexProjectAsync(projectId, ct));

    // Modify 100 files (1% change rate)
    await ModifyRandomFilesAsync(projectId, count: 100);

    // Act 2: Differential re-index
    var differentialTime = await MeasureAsync(() => _indexer.IndexProjectAsync(projectId, ct));

    // Assert: Differential should be 96%+ faster
    var speedup = fullIndexTime / differentialTime;
    speedup.Should().BeGreaterThan(25);  // At least 96% reduction (25× faster)

    differentialTime.Should().BeLessThan(TimeSpan.FromMinutes(3));  // <3 min target

    _output.WriteLine($"Full index: {fullIndexTime.TotalMinutes:F1} min");
    _output.WriteLine($"Differential: {differentialTime.TotalSeconds:F1} sec");
    _output.WriteLine($"Speedup: {speedup:F1}×");
}
```

#### Verification Checklist

```bash
# 1. Entity simplified
grep -c "public.*{.*get.*set.*}" src/Koan.Context/Models/IndexedFile.cs
# Expected: 5 properties (vs. 9 original)

# 2. Change detection method exists
grep -A 30 "GetChangedFilesAsync" src/Koan.Context/Services/Indexer.cs
# Expected: ~30 LOC method

# 3. Integration test passes
dotnet test --filter "DifferentialScanning_1PercentChange_Completes_Under3Minutes"
# Expected: PASS, <3 min

# 4. Differential speedup benchmark
# Expected log output: "Differential scan: 100/10000 files changed (1.0%)"
```

---

### 11.5 Vendor Independence Cleanup

**Search and Replace Map:**
```bash
# Location 1: Program.cs:69-76
OLD: "// ✅ WEAVIATE AUTO-PROVISIONING"
NEW: "// ✅ VECTOR PROVIDER AUTO-PROVISIONING"

OLD: "If Koan.Data.Vector.Connector.Weaviate is referenced:"
NEW: "If a containerized vector connector is referenced (e.g., Weaviate, Qdrant):"

OLD: "- WeaviateOrchestrationEvaluator auto-registers"
NEW: "- OrchestrationEvaluator auto-registers (via Reference = Intent)"

OLD: "- Volume: koan-weaviate-data (persistent)"
NEW: "- Volume: koan-vector-data (persistent, provider-specific naming)"

# Location 2: Program.cs:114-118
OLD: "- WeaviateVectorRepository<TEntity, TKey>"
NEW: "- VectorRepository<TEntity, TKey> (provider-specific implementation)"

OLD: "- WeaviatePartitionMapper"
NEW: "- VectorPartitionMapper (provider-specific implementation)"

OLD: "- WeaviateOrchestrationEvaluator (auto-provisions Weaviate)"
NEW: "- OrchestrationEvaluator (auto-provisions containerized providers)"

# Location 3: DocumentChunk.cs:15 (or Chunk.cs after rename)
OLD: "/// - Vector (Weaviate): Embeddings via Vector<DocumentChunk>.Save(...)"
NEW: "/// - Vector (Provider): Embeddings via Vector<Chunk>.Save(...)"

# Location 4: IndexingService.cs:250 (or Indexer.cs after rename)
OLD: "// Clear vectors from Weaviate first"
NEW: "// Clear vectors from vector store first"

# Location 5: IRetrievalService.cs:79 (or Search.cs after interface deletion)
OLD: 'string VectorProvider,  // "weaviate"'
NEW: 'string VectorProvider,  // e.g. "weaviate", "qdrant", "pinecone"'
```

**Verification Command:**
```bash
# Run after all replacements
grep -r -i "weaviate" --include="*.cs" src/Koan.Context/ | \
  grep -v "Koan.Data.Vector.Connector.Weaviate.csproj" | \
  wc -l
# Expected: 0 (no matches except project reference)
```

### 11.6 Testing Requirements Per Task

**Week 1 Test Coverage Target: 60%**

**Test File Naming Convention:**
```
src/Koan.Context.Tests/
├─ Services/
│  ├─ DiscoveryTests.cs (renamed from DocumentDiscoveryServiceTests.cs)
│  ├─ ChunkerTests.cs (renamed from ChunkingServiceTests.cs)
│  └─ DualStoreCoordinatorTests.cs (NEW)
├─ Models/
│  ├─ ChunkTests.cs (renamed from DocumentChunkTests.cs)
│  └─ VectorOperationTests.cs (NEW)
└─ Security/
   └─ PathTraversalTests.cs (NEW)
```

**Required Test Patterns:**

**1. Security Tests (Path Traversal):**
```csharp
public class PathTraversalTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\System32")]
    [InlineData("/absolute/path/outside")]
    public async Task PathTraversal_Attempts_ThrowSecurityException(string attackPath)
    {
        // Test implementation
    }

    [Fact]
    public async Task Symlink_IsDetected_AndSkipped()
    {
        // Test implementation
    }
}
```

**2. Dual-Store Coordinator Tests:**
```csharp
public class DualStoreCoordinatorTests
{
    [Fact]
    public async Task SaveChunk_Success_CompletesImmediately()
    {
        // Both SQLite and Vector store succeed
    }

    [Fact]
    public async Task SaveChunk_VectorStoreFails_CreatesOutboxEntry()
    {
        // SQLite succeeds, Vector fails → outbox entry created
    }

    [Fact]
    public async Task SaveChunk_SqliteFails_NothingPersisted()
    {
        // SQLite fails → transaction rolls back, no outbox entry
    }
}
```

**3. Outbox Worker Tests:**
```csharp
public class VectorSyncWorkerTests
{
    [Fact]
    public async Task Worker_ProcessesPendingOperations()
    {
        // Create pending outbox entries, verify worker processes them
    }

    [Fact]
    public async Task Worker_RetriesFailedOperations()
    {
        // Simulate transient failure, verify retry with backoff
    }

    [Fact]
    public async Task Worker_MovesToDeadLetterAfter5Retries()
    {
        // Simulate permanent failure, verify dead-letter after 5 retries
    }
}
```

### 11.7 Common Mistakes to Avoid

**❌ ANTI-PATTERNS:**

1. **Working without reading this proposal first**
   - Always read sections 4-6 before starting any task

2. **Renaming multiple files simultaneously**
   - Rename ONE service, verify compilation, THEN proceed

3. **Deleting code before commenting it out**
   - Comment first, verify compilation, THEN delete

4. **Forgetting to update tests**
   - Update tests IMMEDIATELY after code changes

5. **Not verifying vendor independence**
   - Run grep scan after EVERY comment change

6. **Using EntityContext.Transaction() for dual-store**
   - Use DualStoreCoordinator with outbox pattern instead

7. **Hardcoding "Weaviate" in new code**
   - Use "vector store" or "vector provider" generically

8. **Adding features not in this proposal**
   - Stick to the plan, no scope creep

9. **Skipping quality gates**
   - Quality gates are MANDATORY, not optional

10. **Not tracking progress**
    - Update checklist after each completed task

### 11.8 Decision Authority Matrix

**When You Can Proceed Independently:**
- ✅ Renaming variables/methods (follow naming conventions)
- ✅ Adding tests (more coverage is always good)
- ✅ Fixing obvious bugs (security, data loss, crashes)
- ✅ Improving error messages (user-friendly + technical details)
- ✅ Adding logging (use structured logging with context)

**When You Must Ask User:**
- ⚠️ Changing entity schemas (beyond approved Project simplification)
- ⚠️ Adding new dependencies (NuGet packages)
- ⚠️ Modifying API contracts (MCP endpoint parameters)
- ⚠️ Removing features not listed in section 5.1
- ⚠️ Extending timeline beyond 4 weeks
- ⚠️ Changing quality gate thresholds (85% coverage is mandatory)

**When You Must STOP and Escalate:**
- 🛑 Discovery of security vulnerability not in QA report
- 🛑 Data loss scenario not covered by outbox pattern
- 🛑 Breaking changes to Koan framework patterns
- 🛑 Quality gate failure that cannot be resolved

### 11.9 Session Handoff Template

**At END of each session, document:**
```markdown
## Session Handoff Report

**Date:** YYYY-MM-DD
**Week/Task:** Week X, Task Y
**Hours Spent:** N hours
**Status:** [In Progress / Completed / Blocked]

### Work Completed
- [ ] Task 1 description (file:line references)
- [ ] Task 2 description (file:line references)

### Files Modified
- src/Koan.Context/Services/Discovery.cs (renamed, 200 LOC)
- src/Koan.Context/Models/Project.cs (simplified, removed 13 properties)

### Tests Added/Updated
- src/Koan.Context.Tests/Services/DiscoveryTests.cs (15 new tests)
- Coverage: 45% → 55% (+10%)

### Quality Gates Status
- [ ] Compilation: ✅ PASS (0 errors, 0 warnings)
- [ ] Tests: ✅ PASS (all green)
- [ ] Vendor Independence: ✅ PASS (grep scan clean)
- [ ] Coverage: ⚠️ IN PROGRESS (55%, target 60%)

### Next Session TODO
- [ ] Complete remaining service renames (3 services left)
- [ ] Update DI registrations in Program.cs
- [ ] Achieve 60% coverage (add 5% more tests)

### Blockers/Questions
- None / [list blockers]

### Verification Commands
```bash
# Run these to verify session work
dotnet build src/Koan.Context/Koan.Context.csproj
dotnet test src/Koan.Context.Tests/
grep -r -i "weaviate" --include="*.cs" src/Koan.Context/ | wc -l
```
```

### 11.10 Quick Reference: File Locations

**Core Implementation Files:**
```
src/Koan.Context/
├─ Models/
│  ├─ Chunk.cs (Entity<Chunk>, 10 properties)
│  ├─ Project.cs (Entity<Project>, 10 properties after simplification)
│  ├─ Job.cs (Entity<Job>, progress tracking - KEEP)
│  └─ VectorOperation.cs (Entity<VectorOperation>, outbox pattern - NEW)
│
├─ Services/
│  ├─ Discovery.cs (file discovery, security-hardened)
│  ├─ Extraction.cs (content extraction, fixed offset bugs)
│  ├─ Chunker.cs (800-1000 token chunks, 50-token overlap)
│  ├─ Embeddings.cs (vector generation, retry logic)
│  ├─ Indexer.cs (main pipeline, uses DualStoreCoordinator)
│  ├─ Search.cs (semantic search, hybrid alpha parameter)
│  ├─ DualStoreCoordinator.cs (outbox pattern - NEW)
│  └─ VectorSyncWorker.cs (background worker - NEW)
│
├─ Controllers/
│  ├─ McpToolsController.cs (PRIMARY API - /api/search)
│  └─ JobsController.cs (job status for Web UI)
│
└─ Program.cs (zero vendor mentions in comments)
```

**Test Files:**
```
src/Koan.Context.Tests/
├─ Services/
│  ├─ DiscoveryTests.cs (security tests)
│  ├─ DualStoreCoordinatorTests.cs (outbox tests)
│  └─ VectorSyncWorkerTests.cs (background worker tests)
├─ Models/
│  ├─ ChunkTests.cs
│  └─ VectorOperationTests.cs
└─ Security/
   └─ PathTraversalTests.cs (15+ test cases)
```

**Documentation to Update:**
```
docs/
├─ proposals/
│  └─ KOAN-CONTEXT-HARDENING.md (this file)
├─ qa/
│  └─ Koan-Context-QA-Report-2025-11-05.md (reference for bugs)
└─ guides/
   └─ transactions-usage-guide.md (reference for patterns)
```

### 11.11 Success Criteria Checklist

**Week 1 Complete When:**
- [ ] All 9 interface files deleted
- [ ] All 9 services renamed (shorter names)
- [ ] Project entity has exactly 10 properties
- [ ] Zero compilation errors/warnings
- [ ] Zero vendor mentions in code (grep verified)
- [ ] 8 critical bugs fixed (QA report)
- [ ] 60% test coverage achieved
- [ ] All tests passing (green)
- [ ] Handoff report documented

**Week 2 Complete When:**
- [ ] 1,767 LOC net reduction verified (1,680 removed + 187 simplified)
- [ ] 15 files deleted, 2 files simplified
- [ ] File monitoring removed (450 LOC)
- [ ] Differential scanning simplified (287 → 100 LOC)
- [ ] IndexedFile entity simplified (9 → 5 properties)
- [ ] Performance benchmark passes (<2.5 min for 1% change rate)
- [ ] 4 REST controllers removed (400 LOC)
- [ ] ProjectResolver simplified (256 → 100 LOC)
- [ ] 60%+ test coverage maintained
- [ ] All tests passing (green)

**Week 3 Complete When:**
- [ ] Outbox pattern implemented and tested
- [ ] VectorSyncWorker running successfully
- [ ] OpenTelemetry metrics/traces working
- [ ] Health checks responding (3 endpoints)
- [ ] Polly retry policies configured
- [ ] Backup/restore CLI commands working
- [ ] 70% test coverage achieved
- [ ] All tests passing (green)

**Week 4 Complete When:**
- [ ] 85%+ test coverage (MANDATORY)
- [ ] Zero critical bugs
- [ ] Zero vendor mentions (verified)
- [ ] Performance benchmarks met
- [ ] Documentation complete (4 files)
- [ ] Security scan passes
- [ ] Manual testing complete
- [ ] PRODUCTION READY ✅

---

**APPROVED BY:** Architecture Team, Engineering Leadership
**IMPLEMENTATION START:** 2025-11-08 (Week 1, Day 1)
**EXPECTED COMPLETION:** 2025-12-06 (4 weeks)
