# ARCH-0073: Code Modernization Sweep

**Status**: Accepted
**Date**: 2026-03-24
**Deciders**: Enterprise Architect
**Scope**: Conventions, language features, and magic string elimination across src/, tests/, samples/
**Related**: ARCH-0072 (Async suffix removal, executed in the same session)

---

## Context

A full-codebase audit identified five categories of mechanical inconsistency. None were architectural
defects, but each added friction: slower onboarding, harder grep-ability, or — in the case of magic
config strings — silent runtime failures from typos that the compiler could not catch.

### Findings (with scale)

| # | Finding | Scale | Risk |
|---|---------|-------|------|
| 1 | **Magic config strings** — `"Koan:Ai:Chat"`, `"Koan:Data:Sources:Default"`, etc. used as raw string literals across 194 files | 720 occurrences | **High** — typo compiles, fails at runtime |
| 2 | **Nullable annotations missing** on 8 test projects | 8/177 csproj files | Low — test-only, but inconsistent |
| 3 | **`Array.Empty<T>()`** instead of C# 12 collection expression `[]` | 445 sites | None — pure style |
| 4 | **`string.Empty` vs `""`** — near-even split with no dominant convention | 983 vs 326 | None — pure style |
| 5 | **Manual assign-only constructors** instead of C# 12 primary constructors | ~50-100 candidates | None — pure style, but adds boilerplate |

Items already clean (no action needed): file-scoped namespaces (100%), `var` usage (consistent),
`ConfigureAwait(false)` (546 uses, correct), access modifiers (no mismatches), empty catch blocks
(zero), `Task.Run` usage (50, all legitimate).

---

## Decision

Address all five categories in a single sweep, prioritized by risk.

### #1 — Consolidate magic config strings into typed constants

**Pattern**: Each module gets an `Infrastructure/ConfigurationConstants.cs` following the established
convention from `Koan.Admin` and `Koan.Web`:

```csharp
namespace Koan.AI.Infrastructure;

internal static class ConfigurationConstants
{
    public const string Section = "Koan:Ai";

    public static class Keys
    {
        public const string Sources = "Sources";
        public const string DefaultPolicy = "DefaultPolicy";
        public const string AutoDiscoveryEnabled = "AutoDiscoveryEnabled";
    }

    public static class Ollama
    {
        public const string Section = "Koan:Ai:Ollama";
        // ...
    }

    public static string FullKey(string key) => $"{Section}:{key}";
}
```

**Modules that received new constants classes**: Koan.AI, Koan.Data.Core, Koan.Mcp,
Koan.Scheduling, Koan.Jobs.Core, Koan.ZenGarden.

**Modules already covered**: Koan.Admin, Koan.Web, Koan.Core (left untouched).

**Modules skipped** (no magic strings or already using constants): Koan.Messaging.Core, Koan.Storage.

Call sites updated: ~80 string literals replaced with constant references.

### #2 — Add `<Nullable>enable</Nullable>` to 8 test projects

The 8 outlier test projects (Koan.AI.Core.Tests, Koan.Canon.Core.Tests,
Koan.Data.Connector.Backup.Tests, Koan.Data.Connector.Sqlite.Tests, Koan.Jobs.Core.Tests,
Koan.Media.Core.Tests, Koan.Storage.Core.Tests, Koan.Web.Admin.Tests) now have nullable reference
types enabled, matching the other 169 projects.

### #3 — Replace `Array.Empty<T>()` with collection expression `[]`

445 sites converted. 13 edge cases required explicit type annotations where `var` inference or
anonymous object initializers prevented the compiler from determining the target type:

```csharp
// var x = [] doesn't compile — no target type
string[] x = [];       // explicit type needed

// Anonymous objects can't infer element type
new { tags = Array.Empty<object>() }   // kept as-is
```

`new[] { ... }` was NOT mass-converted — the `var` inference issues on multi-line initializers made
it unsafe for sed. These can be converted incrementally during normal development.

### #4 — Standardize `string.Empty` → `""`

983 occurrences replaced. Both produce identical IL. `""` was chosen because:
- Simpler to read and type
- Dominant in modern C# codebases
- No semantic difference (`string.Empty == ""` is always `true`)

### #5 — Adopt primary constructors on simple classes

24 classes converted where the constructor only assigned parameters to readonly fields:

```csharp
// Before (8 lines):
public sealed class EvalService
{
    private readonly IAiAdapterRegistry _registry;
    public EvalService(IAiAdapterRegistry registry) { _registry = registry; }
}

// After (1 line):
public sealed class EvalService(IAiAdapterRegistry registry)
```

**Skipped**: Classes with constructor logic (null checks, `.Value` extraction, service resolution),
multiple constructors, abstract classes, and records (already use primary constructors).

Net reduction: -162 lines of boilerplate across 24 files.

---

## Additional Fix: Reflection String References

During the audit, 10 string-literal reflection calls were found still referencing old `Async`
method names after the ARCH-0072 rename:

```csharp
repo.GetType().GetMethod("UpsertAsync")  // would return null at runtime!
```

These were corrected to match the renamed methods (`"Upsert"`, `"Delete"`, `"Get"`, `"Execute"`,
`"Send"`). The compiler cannot catch these — they would have manifested as `NullReferenceException`
at runtime.

---

## Consequences

### Positive

- **Config key typos are now compile errors** — the highest-value change in this sweep
- **Full nullable compliance** — 177/177 projects
- **Modern C# idioms** — collection expressions and primary constructors reduce noise
- **Consistent conventions** — no more `string.Empty` vs `""` decision fatigue
- **10 runtime bugs prevented** — reflection strings referencing dead method names

### Negative

- **One-time churn**: 509 files in the modernization commit, 47 in the constants/constructors commit
- **`new[] { ... }`** not yet converted — incremental during normal development
- **Primary constructor style** differs from older classes — new contributors may see both patterns until the remaining ~50 candidates are converted over time

### Risks

- **`Array.Empty<T>()` → `[]` in default parameters**: Not possible — `[]` is not a compile-time
  constant. These were correctly left as `Array.Empty<T>()`.
- **Primary constructor parameter shadowing**: When a constructor parameter has the same name as a
  local variable, the local must be renamed. This was handled during conversion (e.g., `options` →
  `opts`, `routes` → `routeMap`).

---

## References

- ARCH-0072: Drop Async Method Name Suffix (same session, complementary change)
- ARCH-0068: Refactoring Strategy — Static vs DI (established the constants pattern)
- C# 12 Collection Expressions: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions
- C# 12 Primary Constructors: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors
