# ARCH-0068: Refactoring Strategy - Static Helpers vs DI vs Template Methods

**Status**: ✅ Accepted
**Date**: 2025-11-03
**Deciders**: Enterprise Architect + Senior Systems Architect Review
**Related**: REFACTORING-LEDGER.md, ARCHITECTURAL-ANALYSIS.md

---

## Context

Comprehensive codebase analysis identified 4,400-5,800 lines of duplication across the Koan Framework. Need to establish clear architectural patterns for eliminating this duplication while maintaining performance and clarity.

### Duplication Categories Identified

1. **Provenance Reporting**: 53 identical `Publish()` helper methods in KoanAutoRegistrar implementations
2. **Discovery Adapters**: 70-80 lines of identical container/local/Aspire logic in 12 adapters
3. **EntityController**: 730-line god class mixing query parsing, patch normalization, and HTTP orchestration
4. **Connection String Parsing**: Identical parsing logic duplicated in 4-5 connectors

---

## Decision

Apply **case-by-case architectural evaluation** using these criteria:

### Use STATIC Helpers When

✅ **Pure functions** - input → output transformation, no side effects
✅ **No injected dependencies** - parameters passed explicitly
✅ **Thread-safe by design** - no mutable state
✅ **Performance-sensitive** - hot path or startup path
✅ **Cross-context reusable** - discovery, compose, provenance, etc.

**Examples**: Query parsing, patch normalization, connection string parsing, provenance helper

### Use DI Services When

✅ **Requires injected dependencies** - ILogger, IOptions<T>, DbContext
✅ **Has state or lifecycle** - caching, connection management
✅ **Makes external calls** - HTTP, database, file system
✅ **Async operations with I/O** - discovery, health checks

**Examples**: Repository pattern, health contributors, discovery orchestration

### Use Template Method Pattern When

✅ **Multiple implementations share structure** - common algorithm, provider-specific steps
✅ **Base class already exists** - natural inheritance hierarchy
✅ **Async orchestration required** - complex multi-step process
✅ **Enforces consistent flow** - all implementations follow same pattern

**Examples**: ServiceDiscoveryAdapterBase enhancement

### Use Hybrid Approach When

✅ **Mix of pure and stateful operations** - extract pure parts as static, keep orchestration as DI
✅ **Controller decomposition** - parsing is pure, HTTP handling requires context

**Examples**: EntityController (static parsers + thin controller)

---

## Implementation Strategy

### Phase 1: Low-Hanging Fruits (Weeks 1-2)

#### P2.6: ConnectionStringParser (1-2 days)
- **Pattern**: Static helper
- **Rationale**: Pure parsing function, no state, multi-context reuse
- **Impact**: 160-250 lines eliminated from 4-5 files
- **Approach**:
  1. Create static `ConnectionStringParser` in `Koan.Core.Orchestration`
  2. Implement provider-specific parsers (Postgres, SQL Server, MongoDB, Redis, SQLite)
  3. Replace duplicated parsing in all connectors
  4. Add unit tests

#### P1.01: ProvenanceExtensions (1-2 weeks)
- **Pattern**: Static extension method
- **Rationale**: Pure function, no state, startup path
- **Impact**: 1,500-2,000 lines eliminated from 53 files
- **Approach**:
  1. Create static `ProvenanceExtensions.PublishConfigValue()` in `Koan.Core.Hosting.Bootstrap`
  2. Update all 53 KoanAutoRegistrar.Describe() methods
  3. Remove 53 duplicate `Publish()` methods
  4. Add unit tests

### Phase 2: Architectural Refactorings (Weeks 3-5)

#### P1.02: DiscoveryAdapter Template Method (1-2 weeks)
- **Pattern**: Template method (enhance existing base class)
- **Rationale**: Async orchestration, state management, base class exists
- **Impact**: 840-960 lines eliminated from 12 files
- **Approach**:
  1. Move container/local/Aspire logic into `ServiceDiscoveryAdapterBase`
  2. Create virtual methods for provider-specific behavior
  3. Update all 12 discovery adapters to use enhanced base
  4. Add integration tests

#### P1.10: EntityController Decomposition (2-3 weeks)
- **Pattern**: Hybrid (static helpers + thin controller)
- **Rationale**: Query/patch parsing are pure, orchestration needs HttpContext
- **Impact**: ~350 lines extracted, 730 → 200 lines in controller
- **Approach**:
  1. Create static `QueryOptionsParser` in `Koan.Web.Queries`
  2. Create static `PatchNormalizer` in `Koan.Web.PatchOps`
  3. Refactor EntityController to use static helpers
  4. Extract cross-cutting concerns to middleware (optional)
  5. Add unit tests for parsers, integration tests for controller

### Phase 3: Quick Wins (Week 6)

#### P1.03: ConfigureAwait(false) Removal (1 day)
- **Pattern**: Automated cleanup
- **Impact**: 800 lines of noise removed

#### P2.5, P2.7, P2.8: Utilities and Cleanup (1-2 weeks)

---

## Breaking Changes Policy

**Approach**: Break and fix - no backward compatibility required

**Rationale**:
- Framework is in greenfield/active development phase
- Clean refactoring more valuable than backward compatibility
- All changes internal to framework (no external API impact)

**Migration**: Not required - all changes applied atomically within framework

---

## Testing Strategy

**Approach**: Refactor first, then add tests

**Test Coverage**:
- **Unit tests** for all static helpers (pure functions, easy to test)
- **Integration tests** for template methods (discovery adapters)
- **Controller tests** for EntityController decomposition

**Quality Gates**:
- All existing tests must pass after refactoring
- New utilities require 90%+ code coverage
- No performance regressions (benchmark EntityController before/after)

---

## Documentation Updates

As each refactoring completes:
1. **ADR created** (this document + specific implementation ADRs)
2. **REFACTORING-LEDGER.md updated** - mark items as completed
3. **Code comments** - document architectural patterns in code
4. **CLAUDE.md updated** - add new patterns to framework guidelines if needed

---

## Success Metrics

**Code Quality**:
- 4,400-5,800 lines removed (~7-10% reduction)
- Zero critical bugs introduced
- Test coverage maintained or improved

**Developer Experience**:
- New connector: Describe() 80 → 50 lines (static helper)
- New discovery adapter: 150+ → 40-50 lines (template method)
- EntityController: 730 → 200 lines (hybrid approach)

**Performance**:
- Static helpers: Zero allocation overhead on hot paths
- No performance regressions in benchmarks

---

## Consequences

### Positive

✅ **Clear architectural patterns** - static vs DI vs template method explicitly defined
✅ **Significant duplication elimination** - 4,400-5,800 lines removed
✅ **Performance improvements** - zero allocation parsing on hot paths
✅ **Easier maintenance** - single source of truth for common patterns
✅ **Better testability** - pure functions easy to test without mocking

### Negative

❌ **Breaking changes required** - all 53 KoanAutoRegistrar files must be updated
❌ **Short-term churn** - 200+ files touched during refactoring
❌ **Learning curve** - developers must understand when to use each pattern

### Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Breaking existing functionality | Comprehensive test suite before refactoring |
| Performance regressions | Benchmark EntityController before/after |
| Inconsistent adoption | Clear guidelines in CLAUDE.md and ADRs |
| Over-application of static pattern | Case-by-case evaluation documented in ARCHITECTURAL-ANALYSIS.md |

---

## Implementation Phases

### Week 1-2: Foundation (Low-Hanging Fruits)
- ✅ P2.6: ConnectionStringParser
- ✅ P1.01: ProvenanceExtensions

### Week 3-5: Architectural Refactorings
- ✅ P1.02: DiscoveryAdapter template method
- ✅ P1.10: EntityController decomposition

### Week 6: Quick Wins
- ✅ P1.03: ConfigureAwait removal
- ✅ P2.5, P2.7, P2.8: Utilities

**Total Timeline**: 6 weeks for Phase 1 refactorings

---

## References

- `docs/refactoring/REFACTORING-LEDGER.md` - Complete issue inventory
- `docs/refactoring/ARCHITECTURAL-ANALYSIS.md` - Detailed case-by-case evaluation
- ARCH-0067: Service Organizational Structure (related to discovery adapters)

---

**Status**: ✅ Accepted - Ready for implementation
**Next Action**: Begin P2.6 implementation (ConnectionStringParser)
