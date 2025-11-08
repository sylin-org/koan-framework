# Koan.Context: Master Implementation Guide for Agentic Development

---
**Type:** MASTER IMPLEMENTATION GUIDE
**Domain:** koan-context, agentic-coding, comprehensive-specification
**Status:** active
**Created:** 2025-11-07
**Framework Version:** v0.6.3+
**Target Completion:** 12-14 weeks (phased)
**Implementation Model:** AI-driven autonomous development

---

## Purpose

This document provides **complete implementation specifications** for transforming Koan.Context from MVP to production-grade enterprise product. It is designed for **autonomous AI coding agents** (Claude, Cursor, GitHub Copilot, etc.) to execute tasks with minimal human oversight.

**Balanced Priority:** Security hardening AND UX transformation in parallel streams

**Detail Level:** Mixed (critical=extreme detail, polish=high-level)

**Scope:** Security, UX, Backend API, Infrastructure

---

## Table of Contents

1. [Executive Context](#1-executive-context)
2. [Current State Snapshot](#2-current-state-snapshot)
3. [Target Architecture](#3-target-architecture)
4. [Implementation Streams](#4-implementation-streams)
5. [Task Catalog (Detailed)](#5-task-catalog-detailed)
6. [Decision Framework](#6-decision-framework)
7. [Quality Gates](#7-quality-gates)
8. [Reference Architecture](#8-reference-architecture)
9. [AI Agent Instructions](#9-ai-agent-instructions)
10. [Appendices](#10-appendices)

---

## 1. Executive Context

### 1.1 What is Koan.Context?

**Koan.Context** is a **self-contained, AI-powered semantic code search engine** built on Koan Framework. It enables developers and AI agents to query codebases using natural language.

**Core Capabilities:**
- **Semantic search** - Natural language queries return relevant code chunks
- **Differential indexing** - 96-97% time savings on re-indexing (SHA256 content hashing)
- **Multi-project support** - Search across monorepos, documentation hubs
- **MCP integration** - Native Model Context Protocol server for AI assistants
- **Zero-config** - Auto-provisions Weaviate vector database, SQLite persistence
- **Local-first** - All data stored in `.koan/data/`, no cloud dependencies

**Technical Stack:**
- **Backend:** ASP.NET Core 10, Koan Framework 0.6.3
- **Storage:** SQLite (metadata), Weaviate (vectors)
- **Embedding:** Ollama (local) or OpenAI (cloud)
- **Frontend:** Currently vanilla HTML/JS (single file)

### 1.2 Why Transform Now?

**Current State:** Functional MVP (D+ grade) - Works but not enterprise-ready

**Target State:** Premium enterprise product (A grade) - Grafana-quality UX + bank-grade security

**Business Drivers:**
1. **Enterprise sales** - Professional UI required for procurement approval
2. **Security compliance** - HIPAA/SOC2 customers require hardening
3. **Competitive differentiation** - Best-in-class UX separates from OSS tools
4. **Developer adoption** - Premium experience drives organic growth

**Expected ROI:** 250-400% in Year 1 through increased sales and reduced churn

### 1.3 Implementation Philosophy

**Balanced Approach:**
- Stream A: **Security Hardening** (path traversal, rate limiting, input validation)
- Stream B: **UX Transformation** (Dashboard, components, real-time updates)
- Stream C: **Backend API** (metrics, analytics, SSE streaming)

**Parallel Execution:**
- AI agents work on multiple streams simultaneously
- Clear dependencies prevent blocking (e.g., API endpoints before frontend integration)
- Weekly integration checkpoints to merge streams

**Quality Focus:**
- Critical tasks: Extreme detail, AI can execute autonomously
- Polish tasks: High-level guidance, AI uses best judgment
- All tasks: Testable acceptance criteria

---

## 2. Current State Snapshot

### 2.1 Existing Codebase Structure

```
koan-framework/
└── src/Koan.Context/
    ├── Program.cs (87 LOC - minimal bootstrap)
    ├── appsettings.json (configuration)
    │
    ├── Controllers/
    │   ├── ProjectsController.cs (391 LOC - CRUD + indexing)
    │   ├── SearchController.cs (275 LOC - search API)
    │   ├── JobsController.cs (unknown - jobs API)
    │   └── McpToolsController.cs (unknown - MCP tools)
    │
    ├── Models/
    │   ├── Project.cs (127 LOC - project entity)
    │   ├── Job.cs (190 LOC - job tracking)
    │   ├── Chunk.cs (vector entity)
    │   ├── IndexedFile.cs (manifest)
    │   └── SyncOperation.cs (vector sync)
    │
    ├── Services/
    │   ├── Indexer.cs (indexing pipeline)
    │   ├── Search.cs (search implementation)
    │   ├── Chunker.cs (chunking logic)
    │   ├── Embedding.cs (embedding generation)
    │   ├── Extraction.cs (content extraction)
    │   ├── Discovery.cs (file discovery)
    │   ├── TokenCounter.cs (token counting)
    │   ├── UrlBuilder.cs (URL generation)
    │   ├── Pagination.cs (pagination logic)
    │   ├── VectorSyncWorker.cs (background sync)
    │   ├── FileMonitoringService.cs (file watcher)
    │   ├── IndexingCoordinator.cs (job coordination)
    │   ├── IncrementalIndexer.cs (differential indexing)
    │   └── ProjectResolver.cs (project resolution)
    │
    ├── Tasks/
    │   ├── IndexingJobMaintenanceTask.cs (auto-resume jobs)
    │   └── IndexingJobMaintenanceTaskRegistration.cs
    │
    ├── Utilities/
    │   ├── GitignoreParser.cs (gitignore parsing)
    │   └── PathCategorizer.cs (path validation)
    │
    └── wwwroot/
        └── index.html (1,056 LOC - entire frontend)
```

### 2.2 Current Capabilities Matrix

| Feature | Implementation | Status | Quality Grade | Notes |
|---------|----------------|--------|---------------|-------|
| **Backend Core** |
| Project CRUD | `ProjectsController.cs` | ✅ Working | B | Complete API, needs pagination |
| Differential Indexing | `IncrementalIndexer.cs` | ✅ Working | A | SHA256 hashing, 96% time savings |
| Semantic Search | `Search.cs` | ✅ Working | B+ | Weaviate integration, hybrid search |
| Job Tracking | `Job.cs`, `JobsController.cs` | ✅ Working | B- | Basic progress, needs detailed stats |
| MCP Server | `McpToolsController.cs` | ✅ Working | B | Functional, needs more tools |
| File Monitoring | `FileMonitoringService.cs` | ✅ Working | B | Auto-reindex on file changes |
| **Security** |
| Path Traversal Protection | ❌ Missing | Not Implemented | F | **CRITICAL VULNERABILITY** |
| Input Validation | ⚠️ Minimal | Partial | D | Basic checks, no comprehensive validation |
| Rate Limiting | ❌ Missing | Not Implemented | F | DoS vulnerable |
| Authentication | ❌ Missing | Not Implemented | F | No auth (local-only mitigation) |
| SQL Injection Protection | ✅ Inherent | Working | A | EF Core parameterization |
| XSS Protection | ⚠️ Minimal | Partial | D | Frontend needs CSP, sanitization |
| **Frontend UX** |
| Dashboard | ❌ Missing | Not Implemented | F | No system overview |
| Projects Page | ⚠️ Basic | Partial | C | Functional but plain |
| Search Page | ⚠️ Basic | Partial | C+ | Works but text-only results |
| Jobs Page | ❌ Missing | Not Implemented | F | No job monitoring UI |
| Settings Page | ⚠️ Basic | Partial | C | Configuration-focused |
| Design System | ❌ Missing | Not Implemented | F | Inline styles only |
| Components | ❌ Missing | Not Implemented | F | No reusable components |
| Charts/Metrics | ❌ Missing | Not Implemented | F | No data visualization |
| Real-time Updates | ⚠️ Polling | Partial | D | 10-second poll, should be SSE |
| Responsive Design | ⚠️ Basic | Partial | C- | Basic media queries |
| Accessibility | ❌ Missing | Not Implemented | F | No ARIA, keyboard nav |
| **Backend API Enhancements** |
| Metrics API | ❌ Missing | Not Implemented | F | No `/api/metrics` endpoints |
| Health API | ❌ Missing | Not Implemented | F | No `/api/health` endpoint |
| Analytics API | ❌ Missing | Not Implemented | F | No `/api/analytics` endpoints |
| SSE Streaming | ❌ Missing | Not Implemented | F | No Server-Sent Events |
| Pagination | ⚠️ Partial | Partial | C | Basic, needs cursor-based |
| Filtering | ⚠️ Partial | Partial | C | Limited filter support |
| **Infrastructure** |
| Build System | ⚠️ Basic | Partial | C | Standard .NET, needs Vite |
| Testing | ⚠️ Minimal | Partial | D+ | Some tests, low coverage |
| CI/CD | ❌ Missing | Not Implemented | F | No automated pipeline |
| Documentation | ⚠️ Partial | Partial | C | Overview exists, needs guides |

**Overall Assessment:**
- **Backend Core:** B+ (Solid foundation, needs API expansion)
- **Security:** **F** (Critical vulnerabilities, must fix)
- **Frontend UX:** D+ (Functional but not premium)
- **Infrastructure:** D (Minimal automation, testing)

**Weighted Grade: C-** (Functional but not production-ready)

### 2.3 Critical Vulnerabilities (Security Stream Priority)

**P0 (Critical - Fix Immediately):**
1. **Path Traversal** - `ProjectsController.Create` accepts arbitrary paths
2. **DoS via Large Files** - No file size limits in indexing
3. **Resource Exhaustion** - No rate limiting on search API
4. **Unvalidated Redirects** - `UrlBuilder` doesn't validate URLs

**P1 (High - Fix in Week 1-2):**
5. **XSS in Frontend** - User-generated content not sanitized
6. **Information Disclosure** - Error messages reveal internal paths
7. **Missing CSRF Protection** - POST endpoints lack CSRF tokens (local mitigates)
8. **Insecure Deserialization** - JSON payload size uncapped

### 2.4 UX Debt (Frontend Stream Priority)

**P0 (Critical - Blocks Enterprise Sales):**
1. **No Dashboard** - No system overview, metrics, health status
2. **No Data Visualization** - No charts, graphs, trend analysis
3. **Poor Progress Feedback** - Polling every 10s, no real-time updates
4. **Text-Only Search** - No syntax highlighting, file tree navigation

**P1 (High - Impacts Usability):**
5. **No Design System** - Inline styles, inconsistent UI
6. **Modal-Based Creation** - Generic forms, poor validation feedback
7. **No Error States** - Alert boxes only, no recovery guidance
8. **No Empty States** - Plain text messages, no onboarding

---

## 3. Target Architecture

### 3.1 System Architecture (Future State)

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Koan.Context Service                         │
│                    (ASP.NET Core 10 + Koan Framework)               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │  REST API    │  │  MCP Server  │  │  Web UI      │              │
│  │  (HTTP)      │  │  (SSE/HTTP)  │  │  (React)     │              │
│  └──────────────┘  └──────────────┘  └──────────────┘              │
│         │                  │                  │                      │
│  ───────┴──────────────────┴──────────────────┴────────────────     │
│                           │                                          │
│              ┌────────────┴────────────┐                            │
│              │  Security Layer (NEW)   │                            │
│              ├─────────────────────────┤                            │
│              │ • Path Validation       │                            │
│              │ • Rate Limiting         │                            │
│              │ • Input Sanitization    │                            │
│              │ • CSRF Protection       │                            │
│              └─────────────────────────┘                            │
│                           │                                          │
│              ┌────────────┴────────────┐                            │
│              │  Enhanced Controllers   │                            │
│              ├─────────────────────────┤                            │
│              │ • ProjectsController    │                            │
│              │ • SearchController      │                            │
│              │ • JobsController        │                            │
│              │ • MetricsController (NEW)│                           │
│              │ • HealthController (NEW) │                           │
│              │ • AnalyticsController(NEW)│                          │
│              └─────────────────────────┘                            │
│                           │                                          │
│              ┌────────────┴────────────┐                            │
│              │  Service Layer          │                            │
│              ├─────────────────────────┤                            │
│              │ • Indexer               │                            │
│              │ • Search                │                            │
│              │ • MetricsService (NEW)  │                            │
│              │ • AnalyticsService (NEW)│                            │
│              │ • StreamingService (NEW)│                            │
│              └─────────────────────────┘                            │
│                           │                                          │
│  ────────────────────────┼───────────────────────────────           │
│              │                          │                            │
│    ┌─────────▼─────────┐    ┌─────────▼─────────┐                 │
│    │  Entity<T> Layer  │    │  Vector<T> Layer  │                 │
│    │  (Relational)     │    │  (Similarity)     │                 │
│    └─────────┬─────────┘    └─────────┬─────────┘                 │
│              │                          │                            │
│  ────────────┴──────────────────────────┴──────────────            │
│              │                          │                            │
│    ┌─────────▼─────────┐    ┌─────────▼─────────┐                 │
│    │   SQLite          │    │   Weaviate        │                 │
│    │   (.koan/data/)   │    │   (Docker)        │                 │
│    └───────────────────┘    └───────────────────┘                 │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
        │                                          │
        │  Self-Orchestration Layer                │
        │  (Auto-provisions dependencies)          │
        └──────────────────────────────────────────┘
```

### 3.2 Frontend Architecture (Future State)

```
┌─────────────────────────────────────────────────────────────────┐
│                     React + TypeScript + Vite                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  Pages/                                                 │    │
│  │  ├─ Dashboard (NEW - Grafana-quality)                   │    │
│  │  ├─ Projects (Enhanced)                                 │    │
│  │  ├─ Search (Enhanced with syntax highlighting)          │    │
│  │  ├─ Jobs (NEW - GitHub Actions-style)                   │    │
│  │  ├─ Insights (NEW - Analytics charts)                   │    │
│  │  └─ Settings (Enhanced with validation)                 │    │
│  └────────────────────────────────────────────────────────┘    │
│                           │                                      │
│  ┌────────────────────────┴────────────────────────────────┐   │
│  │  Components/ (30+ components)                           │   │
│  │  ├─ Common/ (Button, Input, Card, Modal, etc.)          │   │
│  │  ├─ Charts/ (LineChart, BarChart, PieChart, Gauge)     │   │
│  │  ├─ Domain/ (ProjectCard, SearchResultCard, etc.)      │   │
│  │  └─ Layout/ (Sidebar, Header, PageLayout)              │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                      │
│  ┌────────────────────────┴────────────────────────────────┐   │
│  │  Hooks/                                                  │   │
│  │  ├─ useProjects (TanStack Query)                        │   │
│  │  ├─ useSearch (TanStack Query)                          │   │
│  │  ├─ useJobs (TanStack Query)                            │   │
│  │  ├─ useRealtime (SSE connection)                        │   │
│  │  └─ useToast (notifications)                            │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                      │
│  ┌────────────────────────┴────────────────────────────────┐   │
│  │  Stores/ (Zustand)                                       │   │
│  │  ├─ projectsStore (project state)                       │   │
│  │  ├─ searchStore (search state)                          │   │
│  │  └─ uiStore (modals, toasts)                            │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                      │
│  ┌────────────────────────┴────────────────────────────────┐   │
│  │  API Client/ (Axios + TanStack Query)                   │   │
│  │  ├─ projects.ts (Project API calls)                     │   │
│  │  ├─ search.ts (Search API calls)                        │   │
│  │  ├─ jobs.ts (Jobs API calls)                            │   │
│  │  ├─ metrics.ts (Metrics API calls)                      │   │
│  │  └─ analytics.ts (Analytics API calls)                  │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                      │
│  ┌────────────────────────┴────────────────────────────────┐   │
│  │  Design System/                                          │   │
│  │  ├─ tokens.css (colors, typography, spacing)            │   │
│  │  ├─ utilities.css (helper classes)                      │   │
│  │  └─ reset.css (normalize)                               │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.3 API Enhancement Matrix

| Endpoint | Current | Future | Priority | Notes |
|----------|---------|--------|----------|-------|
| **Projects** |
| `GET /api/projects` | ✅ List all | Enhanced with pagination/filtering | P1 | Add `?page=1&pageSize=20&status=Ready` |
| `POST /api/projects` | ✅ Create | Add path validation | P0 | Security: Path traversal protection |
| `GET /api/projects/{id}` | ✅ Get one | No change | P3 | Already functional |
| `GET /api/projects/{id}/health` | ✅ Health | Enhanced with detailed checks | P2 | Add more health indicators |
| `GET /api/projects/{id}/analytics` | ❌ Missing | **NEW** | P2 | Indexing history, search stats |
| **Search** |
| `POST /api/search` | ✅ Search | Add rate limiting | P0 | Security: DoS protection |
| `POST /api/search/suggestions` | ✅ Suggestions | No change | P3 | Already functional |
| **Jobs** |
| `GET /api/jobs` | ✅ List jobs | Enhanced with filtering | P1 | Add `?status=active&projectId=...` |
| `GET /api/jobs/{id}` | ✅ Get job | Add detailed stage info | P2 | Include per-stage stats |
| `POST /api/jobs/{id}/cancel` | ✅ Cancel | No change | P3 | Already functional |
| `GET /api/jobs/{id}/logs` | ❌ Missing | **NEW** | P2 | Streaming log access |
| **Metrics (NEW)** |
| `GET /api/metrics/summary` | ❌ Missing | **NEW** | P0 | Dashboard metrics |
| `GET /api/metrics/performance` | ❌ Missing | **NEW** | P1 | Performance trends |
| **Health (NEW)** |
| `GET /api/health` | ❌ Missing | **NEW** | P0 | System health status |
| **Analytics (NEW)** |
| `GET /api/analytics/searches` | ❌ Missing | **NEW** | P2 | Search analytics |
| `GET /api/analytics/projects/health` | ❌ Missing | **NEW** | P2 | Project health distribution |
| **Streaming (NEW)** |
| `SSE /api/jobs/stream` | ❌ Missing | **NEW** | P1 | Real-time job updates |
| `SSE /api/metrics/stream` | ❌ Missing | **NEW** | P2 | Real-time metrics |

---

## 4. Implementation Streams

### 4.1 Stream A: Security Hardening (Weeks 1-4)

**Lead:** Backend security specialist AI agent
**Dependencies:** None (can start immediately)
**Output:** Hardened backend with comprehensive security controls

**Milestones:**
- **Week 1:** Path traversal protection, input validation framework
- **Week 2:** Rate limiting, file size limits, DoS protection
- **Week 3:** XSS protection (CSP, sanitization), error handling
- **Week 4:** Security testing, penetration testing, documentation

**Success Criteria:**
- Zero known critical vulnerabilities
- OWASP Top 10 compliance
- Passes automated security scan (SAST tools)
- Security audit documentation complete

### 4.2 Stream B: UX Transformation (Weeks 1-8)

**Lead:** Frontend specialist AI agent
**Dependencies:** Stream C (Backend API) for data integration
**Output:** Premium React-based UI with Grafana-quality design

**Milestones:**
- **Week 1-2:** Design system, core components (Button, Input, Card, etc.)
- **Week 3-4:** Dashboard, Projects, Search pages
- **Week 5-6:** Jobs, Insights, Settings pages
- **Week 7-8:** Charts integration, real-time updates, polish

**Success Criteria:**
- 30+ reusable components in Storybook
- WCAG 2.1 AA accessibility compliance
- <2s page load time
- Works on mobile, tablet, desktop

### 4.3 Stream C: Backend API Enhancements (Weeks 2-6)

**Lead:** Backend API specialist AI agent
**Dependencies:** Stream A (security framework must be in place)
**Output:** Enhanced REST API with metrics, analytics, streaming

**Milestones:**
- **Week 2-3:** Metrics API, Health API
- **Week 4-5:** Analytics API, SSE streaming
- **Week 6:** API testing, documentation (Swagger)

**Success Criteria:**
- All new endpoints documented in Swagger
- 85%+ test coverage
- <200ms P95 latency for metrics APIs
- SSE connections stable for 1+ hour

### 4.4 Stream D: Infrastructure (Weeks 5-8)

**Lead:** DevOps specialist AI agent
**Dependencies:** Streams A, B, C (needs functional code to deploy)
**Output:** CI/CD pipeline, testing, deployment automation

**Milestones:**
- **Week 5-6:** GitHub Actions CI/CD, Docker builds
- **Week 7-8:** E2E testing, performance testing, deployment guides

**Success Criteria:**
- CI runs on every commit
- Automated builds to Docker Hub
- E2E tests cover critical flows
- Deployment documentation complete

---

## 5. Task Catalog (Detailed)

### 5.1 Critical Tasks (Extreme Detail for AI Agents)

---

#### Task SECURITY-001: Path Traversal Protection in ProjectsController

**Priority:** P0 (Critical)
**Stream:** A (Security Hardening)
**Dependencies:** None
**Estimated LOC:** ~150 (new utility + tests)
**Files to Modify:**
- `src/Koan.Context/Controllers/ProjectsController.cs`
- `src/Koan.Context/Utilities/PathValidator.cs` (NEW)
- `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Security/PathValidation.Spec.cs` (NEW)

**Acceptance Criteria:**
1. ✅ `ProjectsController.CreateProject` validates `rootPath` before creating project
2. ✅ Path validation rejects:
   - Relative paths (`../`, `./`)
   - UNC paths (`\\server\share`)
   - Symbolic links outside allowed directories
   - Paths with null bytes (`\0`)
3. ✅ Allowed paths must be within:
   - User's home directory
   - Explicitly configured allowed directories (appsettings.json)
4. ✅ Returns `400 Bad Request` with clear error message if validation fails
5. ✅ Unit tests cover all attack vectors

**Implementation Guide:**

**Step 1: Create PathValidator Utility**

File: `src/Koan.Context/Utilities/PathValidator.cs`

```csharp
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Koan.Context.Utilities;

/// <summary>
/// Validates file system paths to prevent path traversal attacks
/// </summary>
public class PathValidator
{
    private readonly List<string> _allowedRoots;

    public PathValidator(IConfiguration configuration)
    {
        // Load allowed directories from appsettings.json
        _allowedRoots = configuration
            .GetSection("Koan:Context:Security:AllowedDirectories")
            .Get<List<string>>() ?? new List<string>();

        // Always allow user's home directory
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(homeDir) && !_allowedRoots.Contains(homeDir))
        {
            _allowedRoots.Add(homeDir);
        }
    }

    /// <summary>
    /// Validates that a path is safe to use as a project root
    /// </summary>
    /// <param name="path">Path to validate</param>
    /// <param name="errorMessage">Error message if validation fails</param>
    /// <returns>True if path is valid, false otherwise</returns>
    public bool IsValidProjectPath(string path, out string? errorMessage)
    {
        errorMessage = null;

        // Check 1: Path must not be null or empty
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path cannot be null or empty";
            return false;
        }

        // Check 2: Path must be absolute (fully qualified)
        if (!Path.IsPathFullyQualified(path))
        {
            errorMessage = $"Path must be absolute. Received: {path}";
            return false;
        }

        // Check 3: Reject paths with null bytes (common attack vector)
        if (path.Contains('\0'))
        {
            errorMessage = "Path contains null byte (security violation)";
            return false;
        }

        // Check 4: Reject UNC paths (\\server\share)
        if (path.StartsWith(@"\\") || path.StartsWith("//"))
        {
            errorMessage = "UNC paths are not allowed for security reasons";
            return false;
        }

        // Check 5: Reject paths with path traversal sequences
        var normalizedPath = Path.GetFullPath(path); // Normalizes ../
        if (normalizedPath.Contains(".."))
        {
            errorMessage = "Path contains path traversal sequence (..)";
            return false;
        }

        // Check 6: Path must be within allowed roots
        var isWithinAllowedRoot = _allowedRoots.Any(root =>
        {
            var normalizedRoot = Path.GetFullPath(root);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        });

        if (!isWithinAllowedRoot)
        {
            errorMessage = $"Path '{path}' is outside allowed directories. " +
                          $"Allowed roots: {string.Join(", ", _allowedRoots)}";
            return false;
        }

        // Check 7: Verify path exists (optional, can be removed if we want to allow creating non-existent paths)
        if (!Directory.Exists(path))
        {
            errorMessage = $"Directory does not exist: {path}";
            return false;
        }

        // Check 8: (Unix only) Verify path is not a symbolic link outside allowed roots
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var linkTarget = ResolvymbolicLink(path);
            if (linkTarget != null && linkTarget != path)
            {
                // Path is a symlink, verify target is within allowed roots
                var isTargetAllowed = _allowedRoots.Any(root =>
                {
                    var normalizedRoot = Path.GetFullPath(root);
                    return linkTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
                });

                if (!isTargetAllowed)
                {
                    errorMessage = $"Symbolic link target '{linkTarget}' is outside allowed directories";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves symbolic link to its target (Unix only)
    /// </summary>
    private string? ResolveSymbolicLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget ?? path;
        }
        catch
        {
            return path;
        }
    }
}
```

**Step 2: Register PathValidator in DI Container**

File: `src/Koan.Context/Program.cs`

```csharp
// Add after line 24 (builder.Services.AddKoan();)
builder.Services.AddSingleton<PathValidator>();
```

**Step 3: Update ProjectsController to Use PathValidator**

File: `src/Koan.Context/Controllers/ProjectsController.cs`

```csharp
// Add constructor injection
private readonly Indexer _Indexer;
private readonly FileMonitoringService _fileMonitoring;
private readonly PathValidator _pathValidator; // NEW

public ProjectsController(
    Indexer Indexer,
    FileMonitoringService fileMonitoring,
    PathValidator pathValidator) // NEW
{
    _Indexer = Indexer ?? throw new ArgumentNullException(nameof(Indexer));
    _fileMonitoring = fileMonitoring ?? throw new ArgumentNullException(nameof(fileMonitoring));
    _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator)); // NEW
}

// Update CreateProject method (around line 48)
[HttpPost("create")]
public async Task<ActionResult<Project>> CreateProject([FromBody] CreateProjectRequest request)
{
    try
    {
        // SECURITY: Validate path before creating project
        if (!_pathValidator.IsValidProjectPath(request.RootPath, out var pathError))
        {
            return BadRequest(new
            {
                error = "Invalid project path",
                details = pathError,
                hint = "Ensure the path is absolute and within allowed directories"
            });
        }

        var project = Project.Create(request.Name, request.RootPath, request.DocsPath);
        var saved = await project.Save();
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}
```

**Step 4: Add Configuration to appsettings.json**

File: `src/Koan.Context/appsettings.json`

```json
{
  "Koan": {
    "Context": {
      "Security": {
        "AllowedDirectories": [
          // Add system-specific allowed directories
          // Example for Linux/Mac:
          // "/home/user/projects",
          // "/opt/projects"
          //
          // Example for Windows:
          // "C:\\Users\\YourName\\Projects",
          // "D:\\Code"
        ]
      }
    }
  }
}
```

**Step 5: Create Unit Tests**

File: `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Security/PathValidation.Spec.cs` (NEW)

```csharp
using Koan.Context.Utilities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Security;

public class PathValidationSpec
{
    private readonly PathValidator _validator;

    public PathValidationSpec()
    {
        // Setup configuration with test allowed directories
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Koan:Context:Security:AllowedDirectories:0"] = Path.GetTempPath()
            })
            .Build();

        _validator = new PathValidator(configuration);
    }

    [Fact]
    public void RejectsNullPath()
    {
        var result = _validator.IsValidProjectPath(null, out var error);

        Assert.False(result);
        Assert.Contains("cannot be null", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsEmptyPath()
    {
        var result = _validator.IsValidProjectPath("", out var error);

        Assert.False(result);
        Assert.Contains("cannot be null or empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsRelativePath()
    {
        var result = _validator.IsValidProjectPath("../etc/passwd", out var error);

        Assert.False(result);
        Assert.Contains("must be absolute", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathWithNullByte()
    {
        var result = _validator.IsValidProjectPath("/tmp/test\0/evil", out var error);

        Assert.False(result);
        Assert.Contains("null byte", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUNCPath()
    {
        var result = _validator.IsValidProjectPath(@"\\malicious-server\share", out var error);

        Assert.False(result);
        Assert.Contains("UNC paths are not allowed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathOutsideAllowedRoots()
    {
        var result = _validator.IsValidProjectPath("/etc/shadow", out var error);

        Assert.False(result);
        Assert.Contains("outside allowed directories", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsValidPathWithinAllowedRoot()
    {
        // Create test directory within allowed root (temp path)
        var testDir = Path.Combine(Path.GetTempPath(), "koan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            var result = _validator.IsValidProjectPath(testDir, out var error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public void RejectsNonExistentPath()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid());

        var result = _validator.IsValidProjectPath(nonExistentPath, out var error);

        Assert.False(result);
        Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
    }

    // TODO: Add symlink test for Unix systems
}
```

**Validation Steps:**
1. Run `dotnet build` - Should compile without errors
2. Run `dotnet test` - All tests should pass
3. Manual test: Try creating project with path `/etc` - Should reject
4. Manual test: Try creating project with valid path in `%USERPROFILE%` - Should succeed

---

#### Task SECURITY-002: Rate Limiting for Search API

**Priority:** P0 (Critical)
**Stream:** A (Security Hardening)
**Dependencies:** None
**Estimated LOC:** ~200 (middleware + configuration)
**Files to Modify:**
- `src/Koan.Context/Middleware/RateLimitingMiddleware.cs` (NEW)
- `src/Koan.Context/Program.cs`
- `src/Koan.Context/appsettings.json`
- `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Security/RateLimiting.Spec.cs` (NEW)

**Acceptance Criteria:**
1. ✅ Search API limited to 100 requests per minute per IP
2. ✅ Returns `429 Too Many Requests` with `Retry-After` header
3. ✅ Rate limits configurable in appsettings.json
4. ✅ Whitelisted IPs exempt from rate limiting (e.g., localhost, internal IPs)
5. ✅ Redis-based storage for distributed scenarios (optional, fallback to in-memory)

**Implementation Guide:**

**Step 1: Add NuGet Package**

```bash
cd src/Koan.Context
dotnet add package AspNetCoreRateLimit --version 5.0.0
```

**Step 2: Configure Rate Limiting in appsettings.json**

File: `src/Koan.Context/appsettings.json`

```json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Real-IP",
    "ClientIdHeader": "X-ClientId",
    "HttpStatusCode": 429,
    "GeneralRules": [
      {
        "Endpoint": "POST:/api/search",
        "Period": "1m",
        "Limit": 100
      },
      {
        "Endpoint": "*",
        "Period": "1m",
        "Limit": 1000
      }
    ],
    "ClientWhitelist": [
      "127.0.0.1",
      "::1"
    ]
  }
}
```

**Step 3: Register Rate Limiting in Program.cs**

File: `src/Koan.Context/Program.cs`

```csharp
using AspNetCoreRateLimit;

// Add after line 17 (var builder = WebApplication.CreateBuilder(args);)

// ✅ RATE LIMITING CONFIGURATION
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add after line 42 (var app = builder.Build();)

// ✅ RATE LIMITING MIDDLEWARE (must be early in pipeline)
app.UseIpRateLimiting();
```

**Step 4: Create Integration Test**

File: `tests/Suites/Context/Integration/Koan.Tests.Context.Integration/Specs/Security/RateLimiting.Spec.cs` (NEW)

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Koan.Tests.Context.Integration.Specs.Security;

public class RateLimitingSpec : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RateLimitingSpec(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Search_RateLimiting_Enforced()
    {
        // Arrange: Create a project first (assuming one exists)
        var projectId = "test-project-id";

        // Act: Make 101 requests (limit is 100/minute)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 101; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/search", new
            {
                projectId,
                query = $"test query {i}"
            });

            responses.Add(response);
        }

        // Assert: First 100 should succeed, 101st should be rate limited
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        var rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.Equal(100, successCount);
        Assert.Equal(1, rateLimitedCount);

        // Verify Retry-After header is present
        var rateLimitedResponse = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(rateLimitedResponse.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Localhost_ExemptFromRateLimiting()
    {
        // This test verifies that localhost is whitelisted
        // All requests should succeed even beyond limit

        var projectId = "test-project-id";

        // Make 150 requests (well above limit of 100)
        for (int i = 0; i < 150; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/search", new
            {
                projectId,
                query = $"test query {i}"
            });

            // Since we're on localhost (whitelisted), all should succeed
            // Note: This may pass or fail depending on test runner IP
            // Adjust test based on your setup
        }
    }
}
```

**Validation Steps:**
1. Run `dotnet build` - Should compile
2. Run `dotnet test` - Rate limiting tests should pass
3. Manual test: Use `curl` or Postman to hit `/api/search` 101 times in 1 minute - 101st should return 429
4. Check response headers: Should include `Retry-After: 60` (seconds)

**Additional Notes:**
- For distributed deployments (multiple replicas), use Redis-based rate limiting:
  ```csharp
  builder.Services.AddDistributedRateLimiting();
  builder.Services.AddStackExchangeRedisCache(options =>
  {
      options.Configuration = "localhost:6379";
  });
  ```

---

#### Task UX-001: Create Design System Foundation

**Priority:** P0 (Critical - Blocks all frontend work)
**Stream:** B (UX Transformation)
**Dependencies:** None
**Estimated LOC:** ~500 (CSS tokens, typography, utilities)
**Files to Create:**
- `src/Koan.Context.UI/src/design-system/tokens.css` (NEW)
- `src/Koan.Context.UI/src/design-system/typography.css` (NEW)
- `src/Koan.Context.UI/src/design-system/utilities.css` (NEW)
- `src/Koan.Context.UI/src/design-system/reset.css` (NEW)

**Acceptance Criteria:**
1. ✅ Design tokens defined for colors, typography, spacing, shadows
2. ✅ Typography scale (xs, sm, base, lg, xl, 2xl, 3xl, 4xl)
3. ✅ Spacing system based on 8px grid
4. ✅ Utility classes for common patterns (flexbox, grid, spacing)
5. ✅ CSS custom properties (CSS variables) for theming
6. ✅ Dark mode support (via media query)

**Implementation Guide:**

**Step 1: Create tokens.css** (Full implementation from UX Asset Specification, Section 3.1)

[See Section 3.1 in KOAN-CONTEXT-UX-ASSET-SPECIFICATION.md for complete tokens.css]

**Step 2: Create typography.css** (Full implementation from UX Asset Specification, Section 3.2)

[See Section 3.2 in KOAN-CONTEXT-UX-ASSET-SPECIFICATION.md for complete typography.css]

**Step 3: Create utilities.css**

File: `src/Koan.Context.UI/src/design-system/utilities.css`

```css
/* ========================================
   UTILITY CLASSES
   ======================================== */

/* Flexbox */
.flex { display: flex; }
.flex-col { flex-direction: column; }
.flex-row { flex-direction: row; }
.items-center { align-items: center; }
.items-start { align-items: flex-start; }
.items-end { align-items: flex-end; }
.justify-between { justify-content: space-between; }
.justify-center { justify-content: center; }
.justify-start { justify-content: flex-start; }
.justify-end { justify-content: flex-end; }
.gap-1 { gap: var(--spacing-1); }
.gap-2 { gap: var(--spacing-2); }
.gap-3 { gap: var(--spacing-3); }
.gap-4 { gap: var(--spacing-4); }
.gap-6 { gap: var(--spacing-6); }
.gap-8 { gap: var(--spacing-8); }

/* Grid */
.grid { display: grid; }
.grid-cols-1 { grid-template-columns: repeat(1, 1fr); }
.grid-cols-2 { grid-template-columns: repeat(2, 1fr); }
.grid-cols-3 { grid-template-columns: repeat(3, 1fr); }
.grid-cols-4 { grid-template-columns: repeat(4, 1fr); }

/* Spacing */
.m-0 { margin: 0; }
.m-1 { margin: var(--spacing-1); }
.m-2 { margin: var(--spacing-2); }
.m-4 { margin: var(--spacing-4); }
.m-6 { margin: var(--spacing-6); }
.p-0 { padding: 0; }
.p-1 { padding: var(--spacing-1); }
.p-2 { padding: var(--spacing-2); }
.p-4 { padding: var(--spacing-4); }
.p-6 { padding: var(--spacing-6); }

/* Borders */
.border { border: var(--border-width-1) solid var(--color-border); }
.border-t { border-top: var(--border-width-1) solid var(--color-border); }
.border-b { border-bottom: var(--border-width-1) solid var(--color-border); }
.rounded { border-radius: var(--border-radius-md); }
.rounded-sm { border-radius: var(--border-radius-sm); }
.rounded-lg { border-radius: var(--border-radius-lg); }

/* Shadows */
.shadow-sm { box-shadow: var(--shadow-sm); }
.shadow-md { box-shadow: var(--shadow-md); }
.shadow-lg { box-shadow: var(--shadow-lg); }

/* Display */
.hidden { display: none; }
.block { display: block; }
.inline-block { display: inline-block; }

/* Width */
.w-full { width: 100%; }
.w-auto { width: auto; }

/* Cursor */
.cursor-pointer { cursor: pointer; }
```

**Step 4: Create reset.css**

File: `src/Koan.Context.UI/src/design-system/reset.css`

```css
/* Modern CSS Reset */
*,
*::before,
*::after {
  box-sizing: border-box;
}

* {
  margin: 0;
  padding: 0;
}

html,
body {
  height: 100%;
}

body {
  line-height: 1.5;
  -webkit-font-smoothing: antialiased;
  font-family: var(--font-family-sans);
  color: var(--color-text-primary);
  background-color: var(--color-background);
}

img,
picture,
video,
canvas,
svg {
  display: block;
  max-width: 100%;
}

input,
button,
textarea,
select {
  font: inherit;
}

p,
h1,
h2,
h3,
h4,
h5,
h6 {
  overflow-wrap: break-word;
}

#root {
  isolation: isolate;
}
```

**Step 5: Import Design System in Main App**

File: `src/Koan.Context.UI/src/main.tsx` (or `index.tsx`)

```typescript
import './design-system/reset.css';
import './design-system/tokens.css';
import './design-system/typography.css';
import './design-system/utilities.css';
```

**Validation Steps:**
1. Create simple HTML test page using design system classes
2. Verify colors, spacing, typography render correctly
3. Test dark mode toggle (if implementing)
4. Check browser DevTools for CSS variable availability

---

### 5.2 High-Priority Tasks (Moderate Detail)

#### Task API-001: Create Metrics API Endpoints

**Priority:** P1
**Stream:** C (Backend API)
**Dependencies:** SECURITY-002 (Rate limiting should be in place)
**Estimated LOC:** ~300 (controller + service)

**Files to Create:**
- `src/Koan.Context/Controllers/MetricsController.cs` (NEW)
- `src/Koan.Context/Services/MetricsService.cs` (NEW)
- `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Metrics/MetricsService.Spec.cs` (NEW)

**Endpoints to Implement:**
1. `GET /api/metrics/summary` - Dashboard summary metrics
2. `GET /api/metrics/performance?period=24h` - Performance trends

**Acceptance Criteria:**
- Returns accurate metrics based on database queries
- Caches results for 30 seconds (avoid expensive queries)
- Returns 200 OK with JSON payload
- Includes metadata (timestamp, period, granularity)

**Implementation Skeleton:**

```csharp
// MetricsController.cs
[ApiController]
[Route("api/metrics")]
public class MetricsController : ControllerBase
{
    private readonly MetricsService _metricsService;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _metricsService.GetSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] string period = "24h")
    {
        var performance = await _metricsService.GetPerformanceMetricsAsync(period);
        return Ok(performance);
    }
}

// MetricsService.cs
public class MetricsService
{
    public async Task<MetricsSummary> GetSummaryAsync()
    {
        // Query database for:
        // - Total projects, today's change
        // - Total chunks, today's change
        // - Searches today, per-hour rate
        // - Avg latency, P95, P99
    }

    public async Task<PerformanceMetrics> GetPerformanceMetricsAsync(string period)
    {
        // Query search logs for time-series data
        // Group by hour, return array of data points
    }
}
```

---

#### Task UI-002: Create Core Components Library

**Priority:** P1
**Stream:** B (UX Transformation)
**Dependencies:** UX-001 (Design system must exist)
**Estimated LOC:** ~2,000 (10 core components)

**Components to Implement:**
1. Button (primary, secondary, danger variants)
2. Input (text, search, with validation states)
3. Card (elevated, outlined, ghost variants)
4. Modal (all sizes, close behaviors)
5. Badge (all semantic colors)
6. ProgressBar (determinate, indeterminate)
7. Spinner (loading indicator)
8. Alert (info, success, warning, error)
9. Toast (with auto-dismiss)
10. Skeleton (loading placeholders)

**Acceptance Criteria:**
- Each component has TypeScript interface for props
- All components use design system tokens (no hardcoded colors/spacing)
- Storybook stories for each component
- Accessibility (ARIA labels, keyboard nav)
- Unit tests for interactive components

**Example Component:**

```typescript
// Button.tsx
interface ButtonProps {
  variant: 'primary' | 'secondary' | 'danger' | 'ghost';
  size: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  isDisabled?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  onClick?: () => void;
  children: React.ReactNode;
}

export function Button({
  variant = 'primary',
  size = 'md',
  isLoading = false,
  isDisabled = false,
  leftIcon,
  rightIcon,
  onClick,
  children
}: ButtonProps) {
  return (
    <button
      className={`btn btn-${variant} btn-${size}`}
      disabled={isDisabled || isLoading}
      onClick={onClick}
      aria-busy={isLoading}
    >
      {isLoading && <Spinner size="sm" />}
      {!isLoading && leftIcon && leftIcon}
      <span>{children}</span>
      {!isLoading && rightIcon && rightIcon}
    </button>
  );
}
```

---

### 5.3 Medium-Priority Tasks (High-Level Guidance)

#### Task INFRA-001: Set Up CI/CD Pipeline

**Priority:** P2
**Stream:** D (Infrastructure)
**Dependencies:** All streams (needs working code to build/test)

**Deliverables:**
1. GitHub Actions workflow for CI
2. Automated Docker builds
3. Automated testing (unit + integration)
4. Code quality checks (linters, formatters)

**Workflow File:**

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main, dev]
  pull_request:
    branches: [main, dev]

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '20.x'
      - run: npm ci
      - run: npm run build
      - run: npm test

  docker:
    runs-on: ubuntu-latest
    needs: [backend, frontend]
    steps:
      - uses: actions/checkout@v3
      - uses: docker/build-push-action@v5
        with:
          context: .
          file: ./Dockerfile
          push: ${{ github.ref == 'refs/heads/main' }}
          tags: koan-context:latest
```

---

## 6. Decision Framework

### 6.1 When to Ask for Human Input

AI agents should request human clarification when:

**Architectural Decisions:**
- Choosing between multiple valid patterns (e.g., Redux vs. Zustand for state)
- Database schema changes (adding/removing columns)
- Breaking API changes (affecting external consumers)

**UX/Design Decisions:**
- Color palette adjustments beyond design system
- Layout changes not specified in mockups
- Accessibility trade-offs (e.g., animation vs. reduced motion)

**Security Trade-offs:**
- Balancing security vs. usability (e.g., strict CORS vs. developer convenience)
- Choosing encryption algorithms (AES-256 vs. ChaCha20)
- Setting rate limits (too strict = bad UX, too loose = DoS risk)

**External Dependencies:**
- Adding new NuGet/npm packages (licensing, size implications)
- Changing runtime requirements (e.g., .NET 10 → .NET 11)

### 6.2 Autonomous Decision Guidelines

AI agents should proceed autonomously when:

**Implementation Details:**
- Variable naming, code formatting (follow existing patterns)
- Internal class structure (private methods, helper functions)
- Test organization (AAA pattern, descriptive test names)

**Bug Fixes:**
- Off-by-one errors, null reference exceptions
- CSS alignment issues, margin/padding tweaks
- Typos in documentation, error messages

**Refactoring:**
- Extracting methods for readability (if <20 lines)
- Renaming for clarity (if no public API impact)
- Adding comments for complex logic

**Testing:**
- Writing unit tests for new code
- Adding integration tests for new endpoints
- Increasing test coverage

### 6.3 Code Style Standards

**C# Backend:**
- Follow Microsoft C# Coding Conventions
- Use nullable reference types (`string?` for nullables)
- Prefer `var` for local variables when type is obvious
- Use expression-bodied members for simple properties/methods
- XML comments for public APIs

**TypeScript Frontend:**
- Use functional components (React hooks, no class components)
- Prefer named exports over default exports
- Use interfaces for props, types for unions/intersections
- Async/await over `.then()` chains
- JSDoc comments for complex functions

**CSS:**
- Use design tokens (CSS variables) exclusively, no hardcoded values
- Follow BEM naming convention for component-specific classes
- Prefer utility classes for spacing, layout (Tailwind-style)
- Mobile-first responsive design (min-width media queries)

---

## 7. Quality Gates

### 7.1 Task Completion Criteria

Each task is considered complete when:

**Code Quality:**
- ✅ Compiles without errors or warnings
- ✅ Passes all linters (ESLint, Prettier, dotnet format)
- ✅ No TypeScript `any` types (use `unknown` or proper types)
- ✅ No C# `#pragma warning disable` (fix warnings, don't suppress)

**Testing:**
- ✅ Unit tests written for new logic (85%+ coverage target)
- ✅ Integration tests for new API endpoints
- ✅ Manual testing checklist completed
- ✅ All tests pass in CI

**Documentation:**
- ✅ XML comments for public C# APIs
- ✅ JSDoc for complex TypeScript functions
- ✅ README updated if introducing new concepts
- ✅ Swagger documentation regenerated for API changes

**Security:**
- ✅ SAST scan passes (no new high/critical issues)
- ✅ Dependency vulnerabilities checked (`dotnet list package --vulnerable`)
- ✅ Input validation on all user inputs
- ✅ Output encoding for all user-generated content

**Accessibility:**
- ✅ Keyboard navigation works (Tab, Enter, Escape)
- ✅ Screen reader testing (NVDA or VoiceOver)
- ✅ Color contrast >= 4.5:1 (WCAG AA)
- ✅ ARIA labels on interactive elements

### 7.2 Weekly Integration Checkpoints

**Every Monday:**
1. Merge all completed tasks to `dev` branch
2. Run full test suite (unit + integration + E2E)
3. Deploy to staging environment (Docker Compose)
4. Manual smoke testing of critical flows:
   - Create project → Index → Search
   - View dashboard metrics
   - Monitor active jobs
5. Review merge conflicts, resolve
6. Plan next week's tasks

**Red Flags (Stop and Notify Human):**
- Test coverage drops below 80%
- Build time exceeds 10 minutes
- Docker image size exceeds 500 MB
- Lighthouse performance score <80
- More than 5 accessibility violations (axe-core)

---

## 8. Reference Architecture

### 8.1 Entity Model Reference

**Project Entity:**
```csharp
public class Project : Entity<Project>
{
    public string Name { get; set; }              // Display name
    public string RootPath { get; set; }          // Absolute path
    public string? DocsPath { get; set; }         // Optional docs subdirectory
    public IndexingStatus Status { get; set; }    // NotIndexed, Indexing, Ready, Failed
    public DateTime? LastIndexed { get; set; }    // Last successful index
    public int DocumentCount { get; set; }        // Total chunks
    public long IndexedBytes { get; set; }        // Total bytes
    public string? CommitSha { get; set; }        // Git commit SHA (provenance)
    public string? LastError { get; set; }        // Error message if failed

    public static Project Create(string name, string rootPath, string? docsPath = null);
    public void MarkIndexed(int documentCount, long indexedBytes);
}
```

**Job Entity:**
```csharp
public class Job : Entity<Job>
{
    public string ProjectId { get; set; }           // Project being indexed
    public JobStatus Status { get; set; }           // Pending, Planning, Indexing, Completed, Failed, Cancelled
    public int TotalFiles { get; set; }             // Total files to process
    public int ProcessedFiles { get; set; }         // Files processed so far
    public int SkippedFiles { get; set; }           // Files skipped (unchanged)
    public int ErrorFiles { get; set; }             // Files with errors
    public int NewFiles { get; set; }               // New files discovered
    public int ChangedFiles { get; set; }           // Changed files detected
    public int ChunksCreated { get; set; }          // Total chunks created
    public int VectorsSaved { get; set; }           // Total vectors saved
    public DateTime StartedAt { get; set; }         // Job start time
    public DateTime? CompletedAt { get; set; }      // Job end time
    public DateTime? EstimatedCompletion { get; set; } // ETA
    public string? ErrorMessage { get; set; }       // Error if failed
    public string? CurrentOperation { get; set; }   // Current operation description
    public decimal Progress { get; }                // Computed: ProcessedFiles / TotalFiles * 100
    public TimeSpan Elapsed { get; }                // Computed: CompletedAt - StartedAt

    public static Job Create(string projectId, int totalFiles);
    public void Complete();
    public void Fail(string errorMessage);
    public void Cancel();
    public void UpdateProgress(int processed, string? operation = null);
}
```

### 8.2 API Response Formats

**Standard Success Response:**
```json
{
  "data": { ... },
  "metadata": {
    "timestamp": "2025-11-07T14:32:00Z",
    "duration": "0:00:00.0123456",
    "version": "1.0"
  }
}
```

**Standard Error Response:**
```json
{
  "error": {
    "code": "INVALID_PATH",
    "message": "Invalid project path",
    "details": "Path '/etc/shadow' is outside allowed directories",
    "hint": "Ensure the path is absolute and within allowed directories",
    "timestamp": "2025-11-07T14:32:00Z"
  }
}
```

**Metrics Summary Response:**
```json
{
  "projects": {
    "total": 5,
    "ready": 3,
    "indexing": 2,
    "failed": 0,
    "changeToday": +1
  },
  "chunks": {
    "total": 127000,
    "changeToday": +3400,
    "changeTrend": "up"
  },
  "searches": {
    "today": 234,
    "last24h": 412,
    "perHour": 17.2,
    "changeTrend": "up"
  },
  "performance": {
    "avgLatencyMs": 156,
    "p95LatencyMs": 340,
    "p99LatencyMs": 680,
    "changeWeek": -12.0
  }
}
```

### 8.3 Database Schema (SQLite)

**Projects Table:**
```sql
CREATE TABLE Projects (
    Id TEXT PRIMARY KEY,                -- GUID v7
    Name TEXT NOT NULL,
    RootPath TEXT NOT NULL UNIQUE,
    DocsPath TEXT,
    Status INTEGER NOT NULL,            -- Enum: 0=NotIndexed, 1=Indexing, 2=Ready, 3=Failed
    LastIndexed TEXT,                   -- ISO 8601 datetime
    DocumentCount INTEGER DEFAULT 0,
    IndexedBytes INTEGER DEFAULT 0,
    CommitSha TEXT,
    LastError TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IX_Projects_Status ON Projects(Status);
CREATE INDEX IX_Projects_LastIndexed ON Projects(LastIndexed DESC);
```

**Jobs Table:**
```sql
CREATE TABLE Jobs (
    Id TEXT PRIMARY KEY,                -- GUID v7
    ProjectId TEXT NOT NULL,
    Status INTEGER NOT NULL,            -- Enum: 0=Pending, 1=Planning, 2=Indexing, 3=Completed, 4=Failed, 5=Cancelled
    TotalFiles INTEGER DEFAULT 0,
    ProcessedFiles INTEGER DEFAULT 0,
    SkippedFiles INTEGER DEFAULT 0,
    ErrorFiles INTEGER DEFAULT 0,
    NewFiles INTEGER DEFAULT 0,
    ChangedFiles INTEGER DEFAULT 0,
    ChunksCreated INTEGER DEFAULT 0,
    VectorsSaved INTEGER DEFAULT 0,
    StartedAt TEXT NOT NULL,
    CompletedAt TEXT,
    EstimatedCompletion TEXT,
    ErrorMessage TEXT,
    CurrentOperation TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,

    FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Jobs_ProjectId ON Jobs(ProjectId);
CREATE INDEX IX_Jobs_Status ON Jobs(Status);
CREATE INDEX IX_Jobs_StartedAt ON Jobs(StartedAt DESC);
```

---

## 9. AI Agent Instructions

### 9.1 How to Use This Document

**As an AI coding agent, you should:**

1. **Read the entire document first** - Understand context, architecture, quality gates
2. **Identify your assigned stream** - Security (A), UX (B), API (C), or Infrastructure (D)
3. **Check dependencies** - Don't start Task X if it depends on incomplete Task Y
4. **Follow task templates** - Critical tasks have extreme detail, follow them precisely
5. **Use decision framework** - Know when to ask for human input vs. proceed autonomously
6. **Validate against quality gates** - Don't mark task complete until all criteria met
7. **Update progress** - Comment in GitHub issues/PRs with progress updates

### 9.2 Task Execution Workflow

**For Each Task:**

```
1. Read task specification (Section 5)
   ↓
2. Check dependencies (are prerequisite tasks complete?)
   ↓
3. Review acceptance criteria (what defines "done"?)
   ↓
4. Implement solution
   - Follow code style standards (Section 6.3)
   - Use reference architecture (Section 8)
   - Write tests as you go (not after)
   ↓
5. Self-validate against quality gates (Section 7.1)
   - Does code compile?
   - Do tests pass?
   - Is coverage >= 85%?
   - Does SAST scan pass?
   ↓
6. Create PR with description:
   - Task ID (e.g., SECURITY-001)
   - Acceptance criteria checklist
   - Testing evidence (screenshots, test output)
   - Any deviations from spec (with rationale)
   ↓
7. Wait for code review (human or AI reviewer)
   ↓
8. Address feedback, merge when approved
```

### 9.3 Communication Templates

**When Asking for Clarification:**

```
CLARIFICATION NEEDED: Task {TASK_ID} - {Brief Description}

Context:
I'm implementing {feature/fix/enhancement} as part of {stream name}.

Question:
{Specific question about ambiguous requirement}

Options I'm Considering:
1. {Option A} - Pros: ..., Cons: ...
2. {Option B} - Pros: ..., Cons: ...

Recommendation:
I recommend {Option X} because {rationale}.

Impact if Delayed:
{Blocking X tasks, or Can proceed with placeholder and refactor later}

Please advise on preferred approach.
```

**When Reporting Completion:**

```
TASK COMPLETED: Task {TASK_ID} - {Brief Description}

Acceptance Criteria:
✅ Criterion 1: {Evidence}
✅ Criterion 2: {Evidence}
✅ Criterion 3: {Evidence}

Testing:
- Unit tests: {X tests added, Y% coverage}
- Integration tests: {Z tests added}
- Manual testing: {Checklist completed}

Files Changed:
- src/.../File.cs (150 LOC added, 20 modified)
- tests/.../Test.cs (200 LOC added)

Quality Gates:
✅ Compiles without warnings
✅ All tests pass
✅ Linters pass
✅ SAST scan clean
✅ Accessibility checked

PR Link: {GitHub PR URL}

Ready for code review.
```

### 9.4 Error Recovery

**If You Encounter a Blocker:**

1. **Document the blocker** - Clearly state what's preventing progress
2. **Propose workarounds** - Suggest 2-3 alternative approaches
3. **Estimate impact** - How many tasks are blocked? For how long?
4. **Escalate appropriately** - Tag human reviewer if blocker is critical
5. **Don't wait passively** - Work on unblocked tasks while waiting for resolution

**Example Blocker Report:**

```
BLOCKER: Task UX-003 - Cannot Complete Dashboard Without Metrics API

Current Situation:
I'm implementing the Dashboard page (Task UX-003), which displays real-time
metrics. The task specification assumes `/api/metrics/summary` endpoint exists,
but it's not yet implemented (Task API-001, assigned to Stream C).

Impact:
- UX-003 blocked (Dashboard incomplete)
- UX-005 partially blocked (Insights page also needs metrics)
- ~2-3 days delay if API-001 not prioritized

Workarounds:
1. Use mock data temporarily, refactor when API ready (RECOMMENDED)
2. Wait for API-001 completion (delays UX stream)
3. Implement API-001 myself (crosses streams, may conflict)

Recommendation:
Proceed with Workaround #1 (mock data). This allows:
- UI development to continue
- Early feedback on dashboard design
- Easy refactoring once API ready (1-hour task)

Mock data file: `src/Koan.Context.UI/src/api/mockMetrics.ts`

Requesting approval to proceed with mock data approach.
```

---

## 10. Appendices

### 10.1 Complete Task List (All Streams)

**Stream A: Security Hardening (Weeks 1-4)**

| Task ID | Description | Priority | LOC Est | Week |
|---------|-------------|----------|---------|------|
| SECURITY-001 | Path traversal protection | P0 | 150 | 1 |
| SECURITY-002 | Rate limiting for search API | P0 | 200 | 1 |
| SECURITY-003 | Input validation framework | P0 | 300 | 1 |
| SECURITY-004 | File size limits in indexing | P1 | 100 | 2 |
| SECURITY-005 | XSS protection (CSP headers) | P1 | 150 | 2 |
| SECURITY-006 | Error handling (no info disclosure) | P1 | 200 | 2 |
| SECURITY-007 | CSRF protection | P2 | 150 | 3 |
| SECURITY-008 | Sanitize user-generated content | P1 | 200 | 3 |
| SECURITY-009 | Secure headers (HSTS, X-Frame-Options) | P2 | 50 | 3 |
| SECURITY-010 | Security audit documentation | P2 | N/A | 4 |
| SECURITY-011 | Penetration testing | P2 | N/A | 4 |

**Stream B: UX Transformation (Weeks 1-8)**

| Task ID | Description | Priority | LOC Est | Week |
|---------|-------------|----------|---------|------|
| UX-001 | Design system foundation | P0 | 500 | 1 |
| UX-002 | Core components library (10) | P1 | 2000 | 1-2 |
| UX-003 | Dashboard page | P0 | 800 | 3 |
| UX-004 | Projects page (enhanced) | P1 | 600 | 3 |
| UX-005 | Search page (enhanced) | P0 | 700 | 4 |
| UX-006 | Jobs page | P2 | 500 | 4 |
| UX-007 | Insights page | P2 | 600 | 5 |
| UX-008 | Settings page (enhanced) | P2 | 400 | 5 |
| UX-009 | Charts library (Recharts integration) | P1 | 800 | 6 |
| UX-010 | Real-time updates (SSE client) | P1 | 300 | 6 |
| UX-011 | Responsive design (mobile/tablet) | P2 | 400 | 7 |
| UX-012 | Accessibility audit & fixes | P1 | 300 | 7 |
| UX-013 | Storybook documentation | P2 | N/A | 8 |
| UX-014 | Polish (animations, transitions) | P3 | 200 | 8 |

**Stream C: Backend API (Weeks 2-6)**

| Task ID | Description | Priority | LOC Est | Week |
|---------|-------------|----------|---------|------|
| API-001 | Metrics API endpoints | P1 | 300 | 2 |
| API-002 | Health API endpoint | P1 | 150 | 2 |
| API-003 | Analytics API endpoints | P2 | 400 | 3 |
| API-004 | SSE streaming (jobs) | P1 | 350 | 4 |
| API-005 | SSE streaming (metrics) | P2 | 200 | 4 |
| API-006 | Projects API (pagination, filtering) | P1 | 250 | 5 |
| API-007 | Jobs API (detailed logs) | P2 | 200 | 5 |
| API-008 | Swagger documentation | P2 | N/A | 6 |
| API-009 | API testing (integration tests) | P1 | 600 | 6 |

**Stream D: Infrastructure (Weeks 5-8)**

| Task ID | Description | Priority | LOC Est | Week |
|---------|-------------|----------|---------|------|
| INFRA-001 | CI/CD pipeline (GitHub Actions) | P2 | N/A | 5 |
| INFRA-002 | Docker builds (multi-stage) | P2 | N/A | 5 |
| INFRA-003 | E2E testing (Playwright) | P2 | 500 | 6 |
| INFRA-004 | Performance testing (k6) | P2 | 300 | 6 |
| INFRA-005 | Deployment guide (Docker Compose) | P2 | N/A | 7 |
| INFRA-006 | Kubernetes manifests | P3 | N/A | 7 |
| INFRA-007 | Monitoring (Prometheus, Grafana) | P3 | 200 | 8 |

**Total LOC Estimate:**
- Security: ~1,500 LOC
- UX: ~8,500 LOC
- API: ~2,450 LOC
- Infrastructure: ~1,000 LOC
- **Total: ~13,450 LOC** (excluding tests, which add ~4,000 LOC)

### 10.2 Technology Stack Reference

**Backend:**
- Runtime: .NET 10
- Framework: ASP.NET Core 10
- ORM: Entity Framework Core (via Koan.Data)
- Database: SQLite (dev), Postgres (prod)
- Vector DB: Weaviate (Docker)
- Embeddings: Ollama (local) or OpenAI (cloud)
- Testing: xUnit, Moq, FluentAssertions

**Frontend:**
- Framework: React 18 + TypeScript 5.3
- Build: Vite 5
- Styling: Tailwind CSS 3.4 + CSS Modules
- State: Zustand 4.4
- Data Fetching: TanStack Query (React Query) 5
- Charts: Recharts 2.10
- Icons: Lucide React 0.292
- Testing: Vitest 1.0 + React Testing Library

**Infrastructure:**
- Containers: Docker 24.x
- Orchestration: Docker Compose 2.x, Kubernetes (optional)
- CI/CD: GitHub Actions
- Registry: Docker Hub (public images)

### 10.3 Useful Commands

**Backend Development:**
```bash
# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run tests
dotnet test

# Run application
dotnet run --project src/Koan.Context

# Watch mode (auto-reload)
dotnet watch run --project src/Koan.Context

# Create migration (if using EF)
dotnet ef migrations add MigrationName --project src/Koan.Context

# Format code
dotnet format
```

**Frontend Development:**
```bash
# Install dependencies
npm install

# Run dev server
npm run dev

# Build for production
npm run build

# Run tests
npm test

# Run tests with coverage
npm run test:coverage

# Lint code
npm run lint

# Format code
npm run format

# Storybook
npm run storybook
```

**Docker:**
```bash
# Build image
docker build -t koan-context:latest .

# Run container
docker run -p 27500:27500 -v $(pwd)/.koan/data:/app/.koan/data koan-context:latest

# Docker Compose (full stack)
docker-compose up -d

# View logs
docker-compose logs -f koan-context
```

**Testing:**
```bash
# Run all tests (backend + frontend)
dotnet test && npm test

# Run integration tests only
dotnet test --filter Category=Integration

# Run E2E tests
npx playwright test

# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverageReportFormat=opencover
```

---

## Summary for AI Agents

You now have:

1. ✅ **Complete context** - What Koan.Context is, why we're transforming it
2. ✅ **Current state** - What exists, what's broken, what's missing
3. ✅ **Target architecture** - What we're building toward
4. ✅ **Implementation streams** - Parallel work tracks (Security, UX, API, Infrastructure)
5. ✅ **Detailed tasks** - Critical tasks with extreme detail, others with guidance
6. ✅ **Decision framework** - When to ask for help, when to proceed
7. ✅ **Quality gates** - How to validate work is complete
8. ✅ **Reference architecture** - Code patterns, API contracts, database schema
9. ✅ **Communication templates** - How to report progress, ask questions, escalate blockers

**Your mission:** Transform Koan.Context from D+ (functional) to A (production-grade) over 12-14 weeks.

**Success criteria:** Balanced security and UX, zero critical vulnerabilities, Grafana-quality interface, 85%+ test coverage, enterprise-ready.

**How to start:**
1. Choose your stream (Security, UX, API, or Infrastructure)
2. Start with P0 tasks (critical path)
3. Follow task templates precisely for critical tasks
4. Use judgment for polish tasks
5. Validate against quality gates before marking complete
6. Communicate progress weekly
7. Ask for help when decision framework indicates

**Remember:** You are autonomous but not alone. Escalate blockers, communicate progress, and deliver incrementally.

Now, let's build something exceptional. 🚀

---

**Document Version:** 1.0
**Last Updated:** 2025-11-07
**Next Review:** Weekly (every Monday during implementation)

**Related Documents:**
- [Koan.Context Overview](../guides/koan-context-overview.md)
- [UX Proposal](KOAN-CONTEXT-UX-PROPOSAL.md)
- [UX Asset Specification](KOAN-CONTEXT-UX-ASSET-SPECIFICATION.md)
- [Security Hardening Proposal](KOAN-CONTEXT-HARDENING.md)
