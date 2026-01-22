# Ongoing Proposals

This directory contains proposals that are **substantially implemented (75-95%)** with remaining work in progress.

## Status Criteria

A proposal moves to `ongoing/` when:
- ✅ Core features are implemented (75%+)
- ✅ Architecture and design principles followed
- ✅ Tests passing for implemented features
- 🔶 Optional polish/cleanup remains
- 🔶 Non-critical enhancements deferred

A proposal moves to `implemented/` when:
- ✅ 100% of proposed features complete
- ✅ All acceptance criteria met
- ✅ Documentation complete
- ✅ No remaining work

---

## Current Ongoing Proposals

### [rust-refactoring-proposal.md](rust-refactoring-proposal.md)
**Status**: ✅ Substantially Implemented (75-80%)
**Implementation Report**: [RUST-REFACTORING-STATUS.md](../../RUST-REFACTORING-STATUS.md)
**Date**: 2026-01-20

**Completed** (75-80%):
- ✅ domain/infra/API separation complete
- ✅ main.rs reduced 74% (3,976 → 1,014 lines)
- ✅ 50+ focused modules established
- ✅ Clean separation of concerns
- ✅ Testable architecture (103 tests passing)
- ✅ Common library for shared code
- ✅ Event system foundation
- ✅ Job pipeline system

**Remaining Work** (20-25%):
- 🔶 Further main.rs reduction (target: < 200 lines)
  - Extract route registration to bootstrap/router.rs
  - Extract state initialization to bootstrap/state.rs
- 🔶 Move legacy files to proper directories
  - docker.rs → infra/container/docker.rs
  - metrics.rs → infra/telemetry/metrics.rs
  - console.rs → infra/output/console.rs
  - discovery.rs/mdns.rs → infra/discovery/
- 🔶 Event system polish
  - Consistent event emission across all operations
  - Event handlers for cross-cutting concerns
- 🔶 Job pipeline enhancements
  - Better cancellation support
  - Job chaining/dependencies
  - Improved progress tracking

**Next Steps**:
1. Create Phase 2 proposal for remaining cleanup (optional)
2. Or close proposal as "substantially complete"
3. Remaining work can be done incrementally without formal proposal

**Why in ongoing/**: Core refactoring is complete and production-ready. Remaining work is optional polish that doesn't block usage or future development.

### [cli-taxonomy.md](cli-taxonomy.md)
**Status**: ✅ Partially Implemented (60-70%)
**Implementation**: garden-rake CLI (src/rake/)
**Date**: 2026-01-17

**Completed** (60-70%):
- ✅ Zen verbs implemented (offer, rest, wake, observe, watch, tend, place, invite, lift, make)
- ✅ Positional "at" syntax working
- ✅ Auto-discovery working
- ✅ Tending state (context management) implemented
- ✅ API versioning (/api/v1/)

**Remaining Work** (30-40%):
- ❌ Dual syntax NOT implemented (no normative "services create" style)
- ❌ Missing zen verbs: explore, nourish, release, touch, garden
- 🔶 API versionless redirect needs clarification
- 🔶 Self-teaching suggestions system needs implementation
- 🔶 Quiet mode needs verification

**Next Steps**:
1. Implement normative dual syntax (services create/stop/start/etc.)
2. Add missing zen verbs (explore, nourish, release, touch, garden)
3. Or close proposal as "zen-only implementation" and document divergence

**Why in ongoing/**: Zen command vocabulary is implemented and working in production. Dual syntax feature was not implemented, but system is functional without it.

---

## Lifecycle: Moving Between Directories

### proposals/ → ongoing/
When 75-95% complete, move from proposals/ to ongoing/

**Checklist**:
- [x] Update proposal status to "✅ Substantially Implemented (X%)"
- [x] Create implementation status report in docs/
- [x] Move to ongoing/ directory
- [x] Update ongoing/README.md with summary
- [x] Link to implementation status report

### ongoing/ → implemented/
When 100% complete, move from ongoing/ to implemented/

**Checklist**:
- [ ] Update proposal status to "✅ Implemented"
- [ ] Verify all acceptance criteria met
- [ ] Complete implementation report
- [ ] Move to implemented/ directory
- [ ] Update implemented/README.md
- [ ] Archive or remove from ongoing/

### ongoing/ → proposals/
If work stalls and proposal needs re-planning (rare)

**Checklist**:
- [ ] Update status to explain what stalled
- [ ] Move back to proposals/
- [ ] Create new proposal for remaining work

---

## Statistics

| Metric | Value |
|--------|-------|
| Total Ongoing | 2 |
| Average Completion | 65-75% |
| Total Files Created | 68+ |
| Total Lines Added | ~14,000+ |
| Total Tests | 103 |

---

## Related Documentation

- [../implemented/README.md](../implemented/README.md) - Fully implemented proposals
- [../PROPOSAL-VALIDATION-SUMMARY.md](../PROPOSAL-VALIDATION-SUMMARY.md) - Validation summary
- [../../RUST-REFACTORING-STATUS.md](../../RUST-REFACTORING-STATUS.md) - Rust refactoring status

---

**Last Updated**: 2026-01-21
