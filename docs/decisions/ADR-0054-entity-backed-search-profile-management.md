# ADR-0054: Entity-Backed Search Profile Management with Managed Taxonomies

**Status:** Implementing (Week 2 Complete, Week 3 In Progress)
**Date:** 2025-11-09
**Context:** KoanContext semantic search intelligence and RAG quality improvements
**Decision Makers:** Architecture review
**Affected Components:** Koan.Service.KoanContext, Entity<T>, EntityController<T>, MCP SDK
**Implementation Progress:** 🟡 Core complete (Weeks 1-2), MCP/Testing pending (Week 3)

---

## Context and Problem Statement

The KoanContext semantic search currently has **metadata poverty** issues that prevent effective content type discrimination:

**Current Problems:**
1. **No content type signals** - Documentation prose indistinguishable from implementation code
2. **Category field unused** - `Chunk.Category` exists but never populated during indexing
3. **Hard-coded logic** - Path patterns, audience mappings, and intent detection coded in C#
4. **Poor documentation retrieval** - Query "documentation about Entity<>" returns code snippets instead of guides
5. **No extensibility** - Users cannot define custom categories or audiences
6. **Missing query intent** - System doesn't adapt search parameters based on user intent

**Example Failure Case:**
```javascript
// User query: "documentation about how to use Entity<>"
// Expected: docs/guides/entity-first.md, docs/guides/data-modeling.md
// Actual: src/Data/Entity.cs, src/Data/Abstractions/IEntity.cs (code, not docs)
```

**Root Cause:** Indexing treats all content identically - no metadata for:
- Content type (guide vs. source code)
- Audience targeting (learner vs. architect)
- Document intent (tutorial vs. reference)
- Path-based categorization (docs/** vs. src/**)

**Architectural Opportunity:**

The framework already has **Entity<T>** + **EntityController<T>** patterns for zero-code CRUD. We can dogfood these to create **managed search profiles** - user-editable taxonomies with auto-seeded defaults.

---

## Decision Drivers

1. **Dogfooding** - Use framework's own Entity-first + auto-controller patterns
2. **Extensibility** - Users must be able to define custom categories/audiences
3. **RAG Quality** - Metadata enables content type discrimination and intent routing
4. **Great DX** - Auto-seeded defaults work out-of-box, admin UI for customization
5. **Zero-Code API** - EntityController<T> auto-generates REST endpoints
6. **Multi-Tenancy Ready** - Foundation for per-project profile overrides
7. **Documentation Gold** - Perfect example for samples/ and framework guides

---

## Considered Options

### Option 1: Keep Hard-Coded Path Patterns

**Approach:** Enhance existing `DetermineCategory()` with more path rules.

```csharp
private static string? DetermineCategory(string filePath)
{
    if (filePath.StartsWith("docs/guides/")) return "guide";
    if (filePath.StartsWith("docs/api/")) return "reference";
    if (filePath.StartsWith("src/")) return "source";
    // 20+ more rules...
}
```

**Pros:**
- ✅ Simple, no schema changes
- ✅ Fast (no DB lookups)

**Cons:**
- ❌ Not extensible (users can't add categories)
- ❌ Hard to test/maintain (logic scattered)
- ❌ Violates OCP (must modify code for new categories)
- ❌ No admin UI possible

**Verdict:** Technical debt. Doesn't solve extensibility.

---

### Option 2: JSON Configuration File

**Approach:** Store categories/audiences in `appsettings.json`.

```json
{
  "SearchProfiles": {
    "Categories": [
      { "name": "guide", "pathPatterns": ["docs/guides/**"], "priority": 10 },
      { "name": "adr", "pathPatterns": ["adrs/**"], "priority": 8 }
    ],
    "Audiences": [
      { "name": "learner", "categories": ["guide", "sample"], "alpha": 0.4 }
    ]
  }
}
```

**Pros:**
- ✅ Externalized configuration
- ✅ Easy to version control
- ✅ No schema changes

**Cons:**
- ❌ Requires app restart for changes
- ❌ No admin UI (direct file editing)
- ❌ No validation or audit trail
- ❌ Doesn't leverage Entity<T> patterns

**Verdict:** Better than hard-coding, but misses Entity-first benefits.

---

### Option 3: Entity<T>-Backed Managed Profiles ⭐ **SELECTED**

**Approach:** Create `SearchCategory` and `SearchAudience` entities with auto-controllers, auto-seeder, and admin UI.

```csharp
public class SearchCategory : Entity<SearchCategory>
{
    public string Name { get; set; }              // "guide", "adr", "sample"
    public string DisplayName { get; set; }       // "Developer Guides"
    public List<string> PathPatterns { get; set; } // ["docs/guides/**"]
    public int Priority { get; set; }             // For result boosting
}

public class SearchAudience : Entity<SearchAudience>
{
    public string Name { get; set; }              // "learner", "architect"
    public List<string> CategoryNames { get; set; } // ["guide", "sample"]
    public float DefaultAlpha { get; set; }       // 0.4f (semantic weight)
}

// Zero-code controllers
public class SearchCategoryController : EntityController<SearchCategory> { }
public class SearchAudienceController : EntityController<SearchAudience> { }
```

**Auto-Seeder:**
```csharp
public class SearchProfileSeeder : IKoanInitializer
{
    public async Task InitializeAsync(...)
    {
        await SeedCategoriesAsync();  // 7 default categories
        await SeedAudiencesAsync();   // 6 default audiences
    }
}
```

**Pros:**
- ✅ **Dogfooding** - Uses framework's own patterns
- ✅ **Extensibility** - Users create custom profiles via API/UI
- ✅ **Zero-Code API** - REST endpoints auto-generated
- ✅ **Admin UI** - CRUD interface for non-developers
- ✅ **Audit trail** - Created/Modified timestamps automatic
- ✅ **Validation** - Entity validation rules
- ✅ **Multi-tenancy ready** - Can partition profiles later
- ✅ **Great DX** - Auto-seeded, works out-of-box

**Cons:**
- ⚠️ Requires schema changes (new tables)
- ⚠️ DB lookups during indexing (mitigated by caching)

**Verdict:** Architecturally superior. Perfect framework showcase.

---

## Decision

**Adopt Option 3: Entity<T>-Backed Managed Search Profiles**

### Core Design Principles

1. **Entity-First Development** - `SearchCategory.Create()`, `category.Save()` patterns
2. **Auto-Generated API** - `EntityController<T>` provides REST CRUD
3. **Auto-Seeded Defaults** - Framework ships with 7 categories + 6 audiences
4. **User Extensibility** - Custom profiles via API or admin UI
5. **Performance via Caching** - IMemoryCache for profile lookups (30-min TTL)
6. **Global Profiles** - No partition (shared across projects) for MVP

---

## Implementation Details

### Phase 1: Entity Models

#### 1.1 SearchCategory Entity

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Models/SearchCategory.cs`

```csharp
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Defines a search content category with path-based auto-classification
/// </summary>
/// <remarks>
/// Categories enable content type discrimination (guides vs. source code).
/// Path patterns use glob syntax: "docs/guides/**", "src/**/*.cs"
/// Auto-seeded with defaults: guide, adr, sample, test, documentation, source, reference
/// </remarks>
public class SearchCategory : Entity<SearchCategory>
{
    /// <summary>
    /// Unique category identifier (e.g., "guide", "adr", "source")
    /// Used in API filters and audience mappings
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g., "Developer Guides", "Architecture Decisions")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this category represents
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Glob patterns for auto-classification during indexing
    /// Examples: ["docs/guides/**", "guides/*.md"], ["src/**/*.cs"]
    /// First matching pattern wins (ordered by Priority)
    /// </summary>
    public List<string> PathPatterns { get; set; } = new();

    /// <summary>
    /// Priority for pattern matching and result boosting (higher = first)
    /// Used when multiple patterns could match (e.g., "docs/api/guide.md")
    /// Also used for result re-ranking (boost documentation over code)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Default semantic vs keyword weight for this category
    /// 0.0 = keyword-only, 1.0 = semantic-only, 0.5 = balanced
    /// Example: ADRs might prefer 0.3 (keyword-heavy), code might prefer 0.7 (semantic-heavy)
    /// </summary>
    public float DefaultAlpha { get; set; } = 0.5f;

    /// <summary>
    /// Whether this category is active (allows soft-delete)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Icon name for UI display (optional)
    /// Examples: "book", "code", "lightbulb", "flask"
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Color hex code for UI display (optional)
    /// Examples: "#3B82F6" (blue), "#10B981" (green)
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Creates a new search category with validation
    /// </summary>
    public static SearchCategory Create(
        string name,
        string displayName,
        string description,
        List<string> pathPatterns,
        int priority = 5,
        float defaultAlpha = 0.5f)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty", nameof(displayName));

        if (pathPatterns == null || pathPatterns.Count == 0)
            throw new ArgumentException("At least one path pattern required", nameof(pathPatterns));

        if (defaultAlpha < 0f || defaultAlpha > 1f)
            throw new ArgumentException("DefaultAlpha must be between 0.0 and 1.0", nameof(defaultAlpha));

        return new SearchCategory
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            PathPatterns = pathPatterns,
            Priority = priority,
            DefaultAlpha = defaultAlpha,
            IsActive = true
        };
    }
}
```

#### 1.2 SearchAudience Entity

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Models/SearchAudience.cs`

```csharp
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Defines a search audience profile with category filtering and search tuning
/// </summary>
/// <remarks>
/// Audiences enable intent-based search optimization (learner, architect, PM, executive).
/// Each audience has preferred categories and semantic tuning.
/// Auto-seeded with defaults: learner, developer, architect, pm, executive, contributor
/// </remarks>
public class SearchAudience : Entity<SearchAudience>
{
    /// <summary>
    /// Unique audience identifier (e.g., "learner", "architect", "pm")
    /// Used in API audience parameter
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g., "Developer Learning Koan", "Technical Architect")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of who this audience represents
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category names to filter (e.g., ["guide", "sample", "test"])
    /// Empty list = all categories
    /// </summary>
    public List<string> CategoryNames { get; set; } = new();

    /// <summary>
    /// Default semantic vs keyword weight for this audience
    /// 0.0 = keyword-only, 1.0 = semantic-only, 0.5 = balanced
    /// Example: Executives prefer 0.2 (keyword-heavy for precise terms),
    /// developers prefer 0.6 (semantic for concept understanding)
    /// </summary>
    public float DefaultAlpha { get; set; } = 0.5f;

    /// <summary>
    /// Maximum token budget for results (controls result verbosity)
    /// Example: Executives might prefer 2000 (summaries), developers prefer 8000 (details)
    /// </summary>
    public int MaxTokens { get; set; } = 5000;

    /// <summary>
    /// Whether to include reasoning metadata in results
    /// Example: Architects might want true (understand retrieval), PMs might want false (cleaner)
    /// </summary>
    public bool IncludeReasoning { get; set; } = true;

    /// <summary>
    /// Whether to include insights metadata in results
    /// </summary>
    public bool IncludeInsights { get; set; } = true;

    /// <summary>
    /// Whether this audience is active (allows soft-delete)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Icon name for UI display (optional)
    /// Examples: "user-graduate" (learner), "user-tie" (executive)
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Creates a new search audience with validation
    /// </summary>
    public static SearchAudience Create(
        string name,
        string displayName,
        string description,
        List<string> categoryNames,
        float defaultAlpha = 0.5f,
        int maxTokens = 5000)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty", nameof(displayName));

        if (defaultAlpha < 0f || defaultAlpha > 1f)
            throw new ArgumentException("DefaultAlpha must be between 0.0 and 1.0", nameof(defaultAlpha));

        if (maxTokens < 1000 || maxTokens > 20000)
            throw new ArgumentException("MaxTokens must be between 1000 and 20000", nameof(maxTokens));

        return new SearchAudience
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            CategoryNames = categoryNames ?? new List<string>(),
            DefaultAlpha = defaultAlpha,
            MaxTokens = maxTokens,
            IsActive = true
        };
    }
}
```

#### 1.3 QueryIntentPattern Entity (Phase 2)

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Models/QueryIntentPattern.cs`

```csharp
namespace Koan.Context.Models;

/// <summary>
/// Defines a query pattern for automatic intent detection
/// </summary>
/// <remarks>
/// Enables zero-configuration intent routing based on query text analysis.
/// Example: "how to" → howto intent → ["guide", "sample"] categories
/// Phase 2 feature - not required for MVP
/// </remarks>
public class QueryIntentPattern : Entity<QueryIntentPattern>
{
    /// <summary>
    /// Pattern to match (case-insensitive substring or regex)
    /// Examples: "how to", "documentation about", "why did we"
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Whether pattern is regex (default: false = substring match)
    /// </summary>
    public bool IsRegex { get; set; } = false;

    /// <summary>
    /// Intent type this pattern indicates
    /// Examples: "howto", "documentation", "decision", "example"
    /// </summary>
    public string IntentType { get; set; } = string.Empty;

    /// <summary>
    /// Suggested categories for this intent
    /// </summary>
    public List<string> SuggestedCategories { get; set; } = new();

    /// <summary>
    /// Suggested semantic weight for this intent
    /// </summary>
    public float SuggestedAlpha { get; set; } = 0.5f;

    /// <summary>
    /// Pattern priority (higher = first)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Whether this pattern is active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
```

---

### Phase 2: Auto-Controllers

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Controllers/SearchCategoryController.cs`

```csharp
using Koan.Context.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API for managing search categories
/// </summary>
/// <remarks>
/// Auto-generated endpoints:
/// - GET    /api/searchcategories
/// - POST   /api/searchcategories
/// - GET    /api/searchcategories/{id}
/// - PATCH  /api/searchcategories/{id}
/// - DELETE /api/searchcategories/{id}
/// </remarks>
[Route("api/searchcategories")]
public class SearchCategoryController : EntityController<SearchCategory>
{
    // EntityController<T> provides all CRUD operations
    // No additional code needed for MVP
}
```

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Controllers/SearchAudienceController.cs`

```csharp
using Koan.Context.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API for managing search audiences
/// </summary>
[Route("api/searchaudiences")]
public class SearchAudienceController : EntityController<SearchAudience>
{
    // EntityController<T> provides all CRUD operations
}
```

---

### Phase 3: Auto-Seeder

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Bootstrap/SearchProfileSeeder.cs`

```csharp
using Koan.Context.Models;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Bootstrap;

/// <summary>
/// Seeds default search categories and audiences on first run
/// </summary>
public class SearchProfileSeeder : IKoanInitializer
{
    private readonly ILogger<SearchProfileSeeder> _logger;

    public SearchProfileSeeder(ILogger<SearchProfileSeeder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Priority => 100;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        // Check if already seeded
        var existingCategories = await SearchCategory.All(ct);
        if (existingCategories.Any())
        {
            _logger.LogInformation("Search profiles already seeded, skipping");
            return;
        }

        _logger.LogInformation("Seeding search categories and audiences...");

        await SeedCategoriesAsync(ct);
        await SeedAudiencesAsync(ct);

        _logger.LogInformation("Search profiles seeded successfully");
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var categories = new[]
        {
            SearchCategory.Create(
                name: "guide",
                displayName: "Developer Guides",
                description: "Step-by-step developer guides and tutorials",
                pathPatterns: new() { "docs/guides/**", "guides/**" },
                priority: 10,
                defaultAlpha: 0.4f),

            SearchCategory.Create(
                name: "adr",
                displayName: "Architecture Decisions",
                description: "Architectural Decision Records explaining design choices",
                pathPatterns: new() { "docs/decisions/**", "adrs/**", "decisions/**" },
                priority: 9,
                defaultAlpha: 0.3f),

            SearchCategory.Create(
                name: "sample",
                displayName: "Code Samples",
                description: "Example implementations and sample applications",
                pathPatterns: new() { "samples/**", "examples/**" },
                priority: 8,
                defaultAlpha: 0.5f),

            SearchCategory.Create(
                name: "test",
                displayName: "Test Code",
                description: "Test code showing usage patterns",
                pathPatterns: new() { "**/tests/**", "**/*.test.cs", "**/*.spec.cs" },
                priority: 6,
                defaultAlpha: 0.6f),

            SearchCategory.Create(
                name: "documentation",
                displayName: "General Documentation",
                description: "General documentation, READMEs, and overviews",
                pathPatterns: new() { "docs/**", "**/readme.md", "**/README.md" },
                priority: 7,
                defaultAlpha: 0.4f),

            SearchCategory.Create(
                name: "source",
                displayName: "Source Code",
                description: "Implementation source code",
                pathPatterns: new() { "src/**" },
                priority: 4,
                defaultAlpha: 0.7f),

            SearchCategory.Create(
                name: "reference",
                displayName: "API Reference",
                description: "API documentation and technical references",
                pathPatterns: new() { "docs/api/**", "docs/reference/**" },
                priority: 8,
                defaultAlpha: 0.3f)
        };

        foreach (var category in categories)
        {
            await category.Save(ct);
            _logger.LogDebug("Seeded category: {Name}", category.Name);
        }
    }

    private async Task SeedAudiencesAsync(CancellationToken ct)
    {
        var audiences = new[]
        {
            SearchAudience.Create(
                name: "learner",
                displayName: "Developer Learning Koan",
                description: "New developers learning the framework",
                categoryNames: new() { "guide", "sample", "test" },
                defaultAlpha: 0.4f,
                maxTokens: 6000),

            SearchAudience.Create(
                name: "developer",
                displayName: "Active Developer",
                description: "Developers actively building with Koan",
                categoryNames: new() { "guide", "sample", "source", "test", "reference" },
                defaultAlpha: 0.5f,
                maxTokens: 8000),

            SearchAudience.Create(
                name: "architect",
                displayName: "Software Architect",
                description: "Technical leaders and architects",
                categoryNames: new() { "adr", "source", "documentation" },
                defaultAlpha: 0.3f,
                maxTokens: 5000),

            SearchAudience.Create(
                name: "pm",
                displayName: "Product Manager",
                description: "Product and project managers",
                categoryNames: new() { "adr", "guide", "documentation" },
                defaultAlpha: 0.3f,
                maxTokens: 4000),

            SearchAudience.Create(
                name: "executive",
                displayName: "Executive/Leadership",
                description: "Technical leadership and executives",
                categoryNames: new() { "adr", "documentation" },
                defaultAlpha: 0.2f,
                maxTokens: 3000),

            SearchAudience.Create(
                name: "contributor",
                displayName: "Framework Contributor",
                description: "Contributors to Koan Framework",
                categoryNames: new() { "source", "test", "adr" },
                defaultAlpha: 0.6f,
                maxTokens: 10000)
        };

        foreach (var audience in audiences)
        {
            await audience.Save(ct);
            _logger.LogDebug("Seeded audience: {Name}", audience.Name);
        }
    }
}
```

---

### Phase 4: Indexer Integration

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Indexer.cs`

```csharp
// Add to Indexer class

private readonly IMemoryCache _cache;

private async Task<string?> DetermineCategoryAsync(string filePath, CancellationToken ct)
{
    // Load categories from cache (30-min TTL)
    var cacheKey = "search-categories";
    var categories = await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

        var all = await SearchCategory.Query(c => c.IsActive, ct);
        return all.OrderByDescending(c => c.Priority).ToList();
    });

    if (categories == null || !categories.Any())
    {
        _logger.LogWarning("No search categories found, using fallback");
        return null;
    }

    // Match against path patterns (first match wins)
    var normalized = filePath.Replace('\\', '/').ToLowerInvariant();

    foreach (var category in categories)
    {
        if (MatchesAnyPattern(normalized, category.PathPatterns))
        {
            _logger.LogTrace("File {Path} matched category {Category}", filePath, category.Name);
            return category.Name;
        }
    }

    _logger.LogTrace("File {Path} did not match any category", filePath);
    return null;
}

private bool MatchesAnyPattern(string normalizedPath, List<string> patterns)
{
    foreach (var pattern in patterns)
    {
        if (string.IsNullOrWhiteSpace(pattern)) continue;

        var normalizedPattern = pattern.ToLowerInvariant();

        // Simple glob matching (** = any subdirectories, * = any characters)
        if (normalizedPattern.Contains("**"))
        {
            var prefix = normalizedPattern.Split("**")[0];
            var suffix = normalizedPattern.Contains("**") && normalizedPattern.Split("**").Length > 1
                ? normalizedPattern.Split("**")[1]
                : "";

            if (normalizedPath.StartsWith(prefix) &&
                (string.IsNullOrEmpty(suffix) || normalizedPath.EndsWith(suffix)))
            {
                return true;
            }
        }
        else if (normalizedPattern.Contains("*"))
        {
            // Basic wildcard matching
            var parts = normalizedPattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
            var currentIndex = 0;

            foreach (var part in parts)
            {
                var index = normalizedPath.IndexOf(part, currentIndex);
                if (index < 0) return false;
                currentIndex = index + part.Length;
            }

            return true;
        }
        else
        {
            // Exact match
            if (normalizedPath == normalizedPattern)
                return true;
        }
    }

    return false;
}

// Update chunk creation to use category
var category = await DetermineCategoryAsync(chunk.FilePath, ct);
docChunk.Category = category;
```

---

### Phase 5: Search.cs Integration

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Search.cs`

```csharp
// Add audience resolution

private readonly IMemoryCache _cache;

private async Task<(List<string> Categories, float Alpha, int MaxTokens)> ResolveAudienceAsync(
    string? audienceName,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(audienceName))
        return (new List<string>(), 0.5f, 5000);

    // Check cache first (30-min TTL)
    var cacheKey = $"audience:{audienceName}";
    var audience = await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

        var result = await SearchAudience.Query(
            a => a.Name == audienceName && a.IsActive,
            ct);

        return result.FirstOrDefault();
    });

    if (audience == null)
    {
        _logger.LogWarning("Audience '{Audience}' not found, using defaults", audienceName);
        return (new List<string>(), 0.5f, 5000);
    }

    return (audience.CategoryNames, audience.DefaultAlpha, audience.MaxTokens);
}

// Add automatic query intent detection (optional)
private (List<string> Categories, float Alpha) InferSearchIntent(string query)
{
    var lower = query.ToLowerInvariant();

    // Documentation-seeking queries
    if (lower.Contains("documentation") || lower.Contains("guide") ||
        lower.Contains("learn") || lower.Contains("tutorial"))
    {
        return (new() { "guide", "documentation" }, 0.4f);
    }

    // Decision/rationale queries
    if (lower.Contains("why") || lower.Contains("decision") ||
        lower.Contains("rationale") || lower.Contains("adr"))
    {
        return (new() { "adr" }, 0.3f);
    }

    // Example/sample queries
    if (lower.Contains("example") || lower.Contains("sample") ||
        lower.Contains("demo") || lower.Contains("show me"))
    {
        return (new() { "sample", "test" }, 0.5f);
    }

    // Implementation queries
    if (lower.Contains("implement") || lower.Contains("code") ||
        lower.Contains("class") || lower.Contains("method"))
    {
        return (new() { "source" }, 0.7f);
    }

    // How-to queries
    if (lower.Contains("how to") || lower.Contains("how do i"))
    {
        return (new() { "guide", "sample" }, 0.4f);
    }

    // Architecture/overview queries
    if (lower.Contains("architecture") || lower.Contains("overview") ||
        lower.Contains("design"))
    {
        return (new() { "adr", "documentation" }, 0.3f);
    }

    // Default: balanced search
    return (new List<string>(), 0.5f);
}

// Update SearchAsync to use audience and intent
public async Task<SearchResult> SearchAsync(
    string projectId,
    string query,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
{
    var normalizedOptions = NormalizeOptions(options ?? new SearchOptions());

    // 1. Resolve audience if provided
    if (!string.IsNullOrWhiteSpace(normalizedOptions.Audience))
    {
        var (audienceCategories, audienceAlpha, audienceMaxTokens) =
            await ResolveAudienceAsync(normalizedOptions.Audience, cancellationToken);

        normalizedOptions = normalizedOptions with
        {
            Categories = audienceCategories,
            Alpha = audienceAlpha,
            MaxTokens = audienceMaxTokens
        };
    }

    // 2. Auto-detect intent if no explicit categories
    if (normalizedOptions.Categories == null || normalizedOptions.Categories.Count == 0)
    {
        var (inferredCategories, inferredAlpha) = InferSearchIntent(query);

        if (inferredCategories.Any())
        {
            _logger.LogInformation(
                "Inferred search intent: categories={Categories}, alpha={Alpha}",
                string.Join(", ", inferredCategories),
                inferredAlpha);

            normalizedOptions = normalizedOptions with
            {
                Categories = inferredCategories,
                Alpha = normalizedOptions.Alpha == 0.7f ? inferredAlpha : normalizedOptions.Alpha
            };
        }
    }

    // Continue with existing search logic...
    // Apply category filter during chunk hydration
}
```

---

### Phase 6: SearchOptions Extension

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Search.cs`

```csharp
public record SearchOptions(
    int MaxTokens = 5000,
    float Alpha = 0.7f,
    string? ContinuationToken = null,
    bool IncludeInsights = true,
    bool IncludeReasoning = true,
    List<string>? Languages = null,
    List<string>? Categories = null,  // NEW: Filter by categories
    string? Audience = null            // NEW: Apply audience profile
);
```

---

### Phase 7: MCP SDK Update

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/mcp-sdk/koan-code-mode.d.ts`

```typescript
declare namespace Koan {
  namespace Entities {
    /** Search indexed codebase with semantic + keyword hybrid search */
    function search(params: {
      /** Search query text */
      query: string;

      /** Maximum tokens to return (default: 5000) */
      maxTokens?: number;

      /** Semantic vs keyword weight (0.0=keyword, 1.0=semantic, default: 0.7) */
      alpha?: number;

      /** Continuation token from previous search */
      continuationToken?: string;

      /** Filter by language (e.g., ["csharp", "markdown"]) */
      languages?: string[];

      /** Filter by categories (e.g., ["guide", "documentation"]) */
      categories?: string[];

      /** Apply audience profile (e.g., "learner", "architect", "pm") */
      audience?: "learner" | "developer" | "architect" | "pm" | "executive" | "contributor";

      /** Include reasoning metadata */
      includeReasoning?: boolean;

      /** Include insights metadata */
      includeInsights?: boolean;
    }): SearchResult;

    // Category management
    function getAllCategories(): Promise<SearchCategory[]>;
    function getCategoryByName(name: string): Promise<SearchCategory | null>;

    // Audience management
    function getAllAudiences(): Promise<SearchAudience[]>;
    function getAudienceByName(name: string): Promise<SearchAudience | null>;
  }
}

interface SearchCategory {
  id: string;
  name: string;
  displayName: string;
  description: string;
  pathPatterns: string[];
  priority: number;
  defaultAlpha: number;
  isActive: boolean;
}

interface SearchAudience {
  id: string;
  name: string;
  displayName: string;
  description: string;
  categoryNames: string[];
  defaultAlpha: number;
  maxTokens: number;
  includeReasoning: boolean;
  includeInsights: boolean;
  isActive: boolean;
}
```

---

## UI/UX Design

### Master/Detail Layout

**Page:** `/admin/search-profiles`

```
┌─────────────────────────────────────────────────────────────────┐
│ Search Profile Management                                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Tabs: [Categories] [Audiences] [Intent Patterns]              │
│                                                                  │
│  ┌──────────────────────┐  ┌─────────────────────────────────┐ │
│  │ Master List          │  │ Detail Panel                     │ │
│  ├──────────────────────┤  ├─────────────────────────────────┤ │
│  │ ┌──────────────────┐ │  │ ┌─────────────────────────────┐ │ │
│  │ │ 🔍 Search...     │ │  │ │ Category: guide             │ │ │
│  │ └──────────────────┘ │  │ │                             │ │ │
│  │ ┌──────────────────┐ │  │ │ Display Name:               │ │ │
│  │ │ + New Category   │ │  │ │ [Developer Guides______]    │ │ │
│  │ └──────────────────┘ │  │ │                             │ │ │
│  │                      │  │ │ Description:                │ │ │
│  │ ✓ Developer Guides   │  │ │ [Step-by-step guides...]   │ │ │
│  │   Priority: 10       │  │ │                             │ │ │
│  │                      │  │ │ Path Patterns:              │ │ │
│  │ ✓ Architecture ADRs  │  │ │ ┌─────────────────────────┐ │ │ │
│  │   Priority: 9        │  │ │ │ docs/guides/**          │ │ │ │
│  │                      │  │ │ │ guides/**               │ │ │ │
│  │ ✓ Code Samples       │  │ │ │ + Add pattern           │ │ │ │
│  │   Priority: 8        │  │ │ └─────────────────────────┘ │ │ │
│  │                      │  │ │                             │ │ │
│  │ ✓ General Docs       │  │ │ Priority: [10____]          │ │ │
│  │   Priority: 7        │  │ │ Default Alpha: [0.4___]     │ │ │
│  │                      │  │ │                             │ │ │
│  │ ✓ Test Code          │  │ │ Active: [✓]                 │ │ │
│  │   Priority: 6        │  │ │                             │ │ │
│  │                      │  │ │ [Save] [Delete] [Cancel]    │ │ │
│  │ ✓ Source Code        │  │ └─────────────────────────────┘ │ │
│  │   Priority: 4        │  │                                 │ │
│  │                      │  │                                 │ │
│  └──────────────────────┘  └─────────────────────────────────┘ │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Key UX Features

1. **Master/Detail Split** - List on left, form on right
2. **Inline Search** - Filter categories/audiences by name
3. **Drag-to-Reorder** - Change priority via drag-and-drop
4. **Live Preview** - Show example file paths that would match
5. **Validation** - Inline error messages for invalid patterns
6. **Bulk Actions** - Enable/disable multiple categories
7. **Import/Export** - Download/upload profiles as JSON

### Audience Tab

```
┌──────────────────────┐  ┌─────────────────────────────────┐
│ Master List          │  │ Detail Panel                     │
├──────────────────────┤  ├─────────────────────────────────┤
│ ✓ Developer Learner  │  │ Audience: learner               │
│   Categories: 3      │  │                                 │
│                      │  │ Display Name:                   │
│ ✓ Active Developer   │  │ [Developer Learning Koan___]    │
│   Categories: 5      │  │                                 │
│                      │  │ Description:                    │
│ ✓ Technical Architect│  │ [New developers learning...]   │
│   Categories: 3      │  │                                 │
│                      │  │ Categories:                     │
│ ✓ Product Manager    │  │ ┌─────────────────────────────┐ │
│   Categories: 3      │  │ │ ☑ Developer Guides          │ │
│                      │  │ │ ☑ Code Samples              │ │
│ ✓ Executive          │  │ │ ☑ Test Code                 │ │
│   Categories: 2      │  │ │ ☐ Architecture ADRs         │ │
│                      │  │ │ ☐ Source Code               │ │
│ ✓ Framework Contrib  │  │ └─────────────────────────────┘ │
│   Categories: 3      │  │                                 │
│                      │  │ Default Alpha: [0.4___]         │
│                      │  │ Max Tokens: [6000_]             │
│                      │  │                                 │
│                      │  │ [Save] [Delete] [Cancel]        │
└──────────────────────┘  └─────────────────────────────────┘
```

---

## Consequences

### Positive

✅ **Dogfooding** - Uses framework's own Entity<T> + EntityController<T> patterns
✅ **Extensibility** - Users create custom categories/audiences via API or UI
✅ **Zero-Code API** - REST endpoints auto-generated by EntityController<T>
✅ **Auto-Seeded** - Works out-of-box with 7 categories + 6 audiences
✅ **RAG Quality** - Metadata enables content type discrimination
✅ **Intent Routing** - Automatic query intent detection
✅ **Great DX** - Query "documentation about Entity<>" auto-routes to guides
✅ **Audit Trail** - Entity timestamps track Created/Modified
✅ **Validation** - Entity validation rules enforce data integrity
✅ **Multi-Tenancy Ready** - Can partition profiles per-project later
✅ **Documentation Gold** - Perfect example for samples/ and guides

### Negative

⚠️ **Schema Changes** - Requires new tables (SearchCategory, SearchAudience)
⚠️ **DB Lookups** - Profile resolution requires queries (mitigated by caching)
⚠️ **UI Development** - Admin UI requires frontend work
⚠️ **Migration Complexity** - Existing indexed content needs re-indexing for categories

### Neutral

➡️ **Global Profiles** - MVP uses global (non-partitioned) profiles
➡️ **Fallback Logic** - Graceful degradation if no profiles found
➡️ **Cache Strategy** - 30-minute TTL balances freshness vs. performance

---

## Migration Path

### Week 1: Core Entities + Seeder ✅ COMPLETE
- [x] Create `SearchCategory` entity
- [x] Create `SearchAudience` entity
- [x] Create `SearchProfileSeeder` with 7 categories + 6 audiences
- [x] Add `SearchCategoryController` (inherits EntityController<T>)
- [x] Add `SearchAudienceController` (inherits EntityController<T>)
- [x] Test via Swagger UI

### Week 2: Indexer + Search Integration ✅ COMPLETE
- [x] Update `Indexer.cs` to use categories for classification
- [x] Add glob pattern matching logic
- [x] Add category caching (IMemoryCache)
- [x] Update `Search.cs` to resolve audiences
- [x] Add automatic query intent detection
- [x] Add category filtering during hydration
- [x] Update `SearchOptions` with Categories and Audience parameters

### Week 3: MCP SDK + Testing ⚠️ PARTIAL
- [ ] Update MCP TypeScript definitions ❌ NOT DONE
- [ ] Re-index existing content with categories
- [ ] Test category filtering: `search("Entity<>", categories=["guide"])`
- [ ] Test audience profiles: `search("Entity<>", audience="learner")`
- [ ] Test intent detection: `search("documentation about Entity<>")`
- [ ] Write integration tests

### Week 4: Admin UI 🔜 NOT STARTED
- [ ] Design master/detail layout
- [ ] Implement category CRUD UI
- [ ] Implement audience CRUD UI
- [ ] Add drag-to-reorder for priorities
- [ ] Add live preview for path patterns
- [ ] Add import/export functionality

### Phase 2 (Future) 🔮 DEFERRED
- [ ] Create `QueryIntentPattern` entity
- [ ] Add custom intent pattern UI
- [ ] Per-project profile overrides (partition support)
- [ ] Profile versioning/history
- [ ] A/B testing of audiences
- [ ] Analytics dashboard

---

## Implementation Status

**Last Updated:** 2025-11-09 (Post-Crash Analysis)
**Current Phase:** Week 2 Complete, Week 3 Partial
**Overall Status:** 🟡 Core implementation complete, MCP integration and testing pending

### ✅ Completed Components

#### Phase 1: Entity Models (100%)
- ✅ `SearchCategory` entity (`Models/SearchCategory.cs`)
  - All properties match ADR specification
  - Static `Create()` factory with validation
  - `[McpEntity]` attribute for MCP exposure
- ✅ `SearchAudience` entity (`Models/SearchAudience.cs`)
  - All properties match ADR specification
  - Static `Create()` factory with validation
  - `[McpEntity]` attribute for MCP exposure
- ✅ `Chunk.Category` field exists

#### Phase 2: Auto-Controllers (100%)
- ✅ `SearchCategoryController` inherits `EntityController<SearchCategory>`
  - REST endpoints auto-generated: GET, POST, PATCH, DELETE, Query
  - Route: `/api/searchcategories`
- ✅ `SearchAudienceController` inherits `EntityController<SearchAudience>`
  - REST endpoints auto-generated: GET, POST, PATCH, DELETE, Query
  - Route: `/api/searchaudiences`

#### Phase 3: Auto-Seeder (100% with deviation)
- ✅ `SearchProfileSeeder` implemented (`Bootstrap/SearchProfileSeeder.cs`)
  - **DEVIATION**: Uses `IHostedService` instead of `IKoanInitializer` as specified in ADR
  - Registered in `Program.cs` via `AddHostedService<SearchProfileSeeder>()`
  - Seeds 7 categories: guide, adr, sample, test, documentation, source, reference
  - Seeds 6 audiences: learner, developer, architect, pm, executive, contributor
  - Idempotent: checks if categories exist before seeding
  - Error handling: logs failures but doesn't crash app

#### Phase 4: Indexer Integration (100%)
- ✅ `DetermineCategoryAsync()` method in `Indexer.cs` (lines 941-990)
  - Loads categories from IMemoryCache (30-min TTL)
  - Falls back to database if cache miss
  - Orders by Priority (descending) for pattern matching
  - Returns first matching category or null
- ✅ `MatchesAnyPattern()` glob matching (lines 992-1056)
  - Supports `**` (any subdirectories)
  - Supports `*` (any characters)
  - Supports exact match
  - Case-insensitive matching
- ✅ Category assignment during indexing (line 398)
  - `docChunk.Category = await DetermineCategoryAsync(chunk.FilePath, ct)`
  - Runs for every chunk during indexing

#### Phase 5: Search Integration (100%)
- ✅ `ResolveAudienceAsync()` method in `Search.cs` (lines 436-481)
  - Uses IMemoryCache with 30-min TTL
  - Returns (Categories, Alpha, MaxTokens) tuple
  - Graceful fallback to defaults if audience not found
- ✅ `InferSearchIntent()` method (lines 483-541)
  - Heuristic-based intent detection
  - 7 intent patterns: documentation, decision, example, implementation, how-to, architecture
  - Returns suggested categories and alpha values
- ✅ Audience resolution in `SearchAsync()` (lines 49-68)
  - Resolves audience profile first (highest priority)
  - Overrides categories, alpha, maxTokens from audience
  - Logs applied profile for debugging
- ✅ Auto-intent detection (lines 71-89)
  - Runs if no explicit categories or audience
  - Logs inferred intent for transparency
- ✅ Category filtering during hydration (lines 171-222)
  - **OPTIMIZATION**: Batch fetch all chunks (was individual Get calls)
  - In-memory category filter after batch fetch
  - Language filter also uses batch approach
  - Logs filter results (before/after counts)

#### Phase 6: SearchOptions Extension (100%)
- ✅ `SearchOptions` record updated (lines 582-587)
  - Added `Categories` parameter (List<string>?)
  - Added `Audience` parameter (string?)
- ✅ `SearchController` updated to pass new parameters
  - Single-project search (lines 136-138)
  - Multi-project search (lines 204-206)
- ✅ `SearchRequest` record updated (lines 493-498)
  - Added `Categories` parameter
  - Added `Audience` parameter

#### Bonus Enhancements (100%)
- ✅ Markdown language fallback in `Chunker.cs` (lines 197-201)
  - Sets `language = "markdown"` for .md files with no detected language

### ⚠️ Partial/Incomplete Components

#### Phase 7: MCP SDK Update (0%)
- ❌ **TypeScript definitions NOT updated** (`mcp-sdk/koan-code-mode.d.ts`)
  - File shows only timestamp/hash changes in git diff
  - `Koan.Entities` namespace is empty
  - Missing `search()` function with new parameters
  - Missing `SearchCategory` interface
  - Missing `SearchAudience` interface
  - Missing `getAllCategories()` function
  - Missing `getCategoryByName()` function
  - Missing `getAllAudiences()` function
  - Missing `getAudienceByName()` function
  - **ROOT CAUSE**: File appears to be auto-generated, may require regeneration

#### Testing (0%)
- ❌ No integration tests visible in uncommitted code
- ❌ No evidence of re-indexing with categories
- ❌ No test coverage for:
  - Category filtering
  - Audience profiles
  - Intent detection
  - Glob pattern matching edge cases

### 🔜 Not Started

#### Admin UI (Phase 2)
- Master/detail layout design
- Category CRUD UI
- Audience CRUD UI
- Priority drag-to-reorder
- Path pattern live preview
- Import/export functionality

#### QueryIntentPattern Entity (Phase 2)
- Deferred to future phase as planned

### 📋 Implementation Deviations from ADR

#### 1. SearchProfileSeeder Pattern ⚠️ DEVIATION
**ADR Specification:**
```csharp
public class SearchProfileSeeder : IKoanInitializer
{
    public int Priority => 100;
    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct) { ... }
}
```

**Actual Implementation:**
```csharp
public class SearchProfileSeeder : IHostedService
{
    public async Task StartAsync(CancellationToken ct) { ... }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Rationale for Deviation:**
- Koan Framework supports both `IKoanInitializer` and `IHostedService` for initialization
- `IHostedService` is a standard ASP.NET Core pattern (better IDE support)
- `IHostedService` runs during app startup (same timing as `IKoanInitializer`)
- No functional difference for this use case (one-time seeding)

**Recommendation:** Update ADR to reflect actual implementation (IHostedService is acceptable)

#### 2. MCP SDK Not Updated ❌ INCOMPLETE
**ADR Specification:** Extensive TypeScript interface definitions for search(), categories, audiences

**Actual Implementation:** File only shows timestamp changes, no new interfaces

**Impact:** MCP tools/clients cannot use new search features via type-safe SDK

**Required Action:**
1. Determine if SDK is auto-generated (check for SDK generator)
2. If auto-generated, trigger regeneration
3. If manual, implement TypeScript definitions per ADR spec

### 🎯 Next Steps to Complete ADR-0054

#### Immediate (Week 3 Completion)
1. **MCP SDK Update** (Priority: HIGH)
   - [ ] Investigate SDK generation process
   - [ ] Add search() function with Categories and Audience parameters
   - [ ] Add SearchCategory and SearchAudience TypeScript interfaces
   - [ ] Add category/audience management functions

2. **Re-index Existing Content** (Priority: HIGH)
   - [ ] Trigger full re-index to populate Category field on existing chunks
   - [ ] Verify categories are assigned correctly via logs
   - [ ] Spot-check database to confirm Category field populated

3. **Testing** (Priority: HIGH)
   - [ ] Test category filtering: `POST /api/search { "query": "Entity<>", "categories": ["guide"] }`
   - [ ] Test audience profiles: `POST /api/search { "query": "Entity<>", "audience": "learner" }`
   - [ ] Test intent detection: `POST /api/search { "query": "documentation about Entity<>" }`
   - [ ] Verify glob patterns match expected files
   - [ ] Write integration tests for critical paths

#### Short-term (Week 4+)
4. **Admin UI** (Priority: MEDIUM - can use REST API directly for MVP)
5. **Performance Validation** (Priority: MEDIUM)
   - Measure indexing overhead with category resolution
   - Validate cache hit rates (should be >95% after warmup)
   - Measure search latency with category filtering

#### Future (Phase 2)
6. **QueryIntentPattern Entity** (Deferred as planned)
7. **Per-project Profile Overrides** (Deferred as planned)

---

---

## Example Usage

### API Examples

**Get all categories:**
```bash
GET /api/searchcategories
```

**Create custom category:**
```bash
POST /api/searchcategories
{
  "name": "troubleshooting",
  "displayName": "Troubleshooting Guides",
  "description": "Problem-solving guides and FAQs",
  "pathPatterns": ["docs/troubleshooting/**", "docs/faq/**"],
  "priority": 9,
  "defaultAlpha": 0.3
}
```

**Search with audience:**
```javascript
// MCP tool usage
Koan.search({
  query: "how to use Entity<T>",
  audience: "learner"  // Auto-applies ["guide", "sample", "test"] + alpha=0.4
})
```

**Search with explicit categories:**
```javascript
Koan.search({
  query: "why Entity<T> instead of repositories",
  categories: ["adr"]  // Only architectural decisions
})
```

**Auto-intent detection:**
```javascript
Koan.search({
  query: "documentation about Entity<>"
  // Auto-detects intent → categories=["guide", "documentation"], alpha=0.4
})
```

---

## Performance Characteristics

### Profile Resolution

| Operation | Cold Cache | Hot Cache | Notes |
|-----------|------------|-----------|-------|
| Load Categories | 5-10ms | <1ms | 30-min TTL |
| Load Audience | 3-5ms | <1ms | 30-min TTL |
| Pattern Matching | 1-2ms | 1-2ms | Per file (indexing) |
| Intent Detection | <1ms | <1ms | String matching |

### Expected Impact

**Before (Hard-Coded):**
- Query: "documentation about Entity<>"
- Categories matched: All (no filtering)
- Results: 60% code, 40% docs
- Alpha: 0.7 (semantic-heavy)

**After (Managed Profiles):**
- Query: "documentation about Entity<>"
- Categories matched: ["guide", "documentation"] (auto-detected)
- Results: 95% docs, 5% code
- Alpha: 0.4 (keyword-heavy for "documentation" queries)

---

## References

- ADR-0053: Vector Search Native Continuation
- Koan Framework: Entity-First Development
- Koan Framework: EntityController<T> Auto-Generated APIs
- Koan Framework: IKoanInitializer Auto-Registration
- Diataxis Framework: https://diataxis.fr/
- Glob Pattern Specification: https://en.wikipedia.org/wiki/Glob_(programming)

---

## Decision Log

**2025-11-09 (Morning):** Initial proposal - Entity-backed managed profiles
**2025-11-09 (Morning):** Accepted - PathPattern renamed to PathPatterns (plural)
**2025-11-09 (Morning):** Architecture finalized - Master/detail UI design approved
**2025-11-09 (Afternoon):** Implementation started - Weeks 1-2 completed
**2025-11-09 (Evening):** Post-crash analysis - Core implementation complete, documented deviations

---

**Last Updated:** 2025-11-09 (Post-Crash Analysis)
**Implementation Target:** Sprint 2025-Q4
**Status:** 🟡 Week 2 complete, Week 3 in progress (MCP SDK + testing pending)
