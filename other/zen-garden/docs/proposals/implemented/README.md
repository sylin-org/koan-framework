# Implemented Proposals

This directory contains proposals that have been fully implemented in the codebase.

## Offering Modes (2026-01-21)

The offering modes feature was fully implemented, enabling three deployment patterns:

### Proposals
1. **[offering-modes.md](offering-modes.md)** - Original specification
   - Status: ✅ Implemented with terminology change (Planted → Managed)
   - Date: January 2026
   - Note: Used "Planted" terminology; finalized as "Managed"

2. **[offering-modes-implementation.md](offering-modes-implementation.md)** - Intermediate implementation plan
   - Status: ✅ Superseded by refactoring plan
   - Provided design principles and data model foundation

3. **[offering-modes-refactoring-plan.md](offering-modes-refactoring-plan.md)** - Final implementation plan
   - Status: ✅ Fully implemented
   - Implementation Date: 2026-01-21
   - **This was the plan actually executed**

### Implementation Reports
- [OFFERING-MODES-IMPLEMENTATION-COMPLETE.md](../../OFFERING-MODES-IMPLEMENTATION-COMPLETE.md)
- [OFFERING-MODES-DATA-POPULATION.md](../../OFFERING-MODES-DATA-POPULATION.md)

### Key Deliverables

**Code Changes**:
- 18 new files (~1,800 lines of code)
- 7 modified files
- 99 tests (100% passing)

**Data**:
- 5 example offering manifests
- Complete manifest loader system
- 700+ lines of documentation

**Features**:
- ✅ Managed mode (container-based)
- ✅ Adopted mode (existing services)
- ✅ Borrowed mode (external network services)
- ✅ Auto-adoption with platform detection
- ✅ Detection orchestration (command, HTTP, container)
- ✅ Secrets management (encrypted file backend)
- ✅ REST API endpoints (5 endpoints)
- ✅ Minimal manifests (4-6 lines)

### Validation

**Architecture**:
- ✅ Zero hardcoded service names
- ✅ Optional fields completely omitted (not null/{}/[])
- ✅ 100% backwards compatible
- ✅ Clean domain/infra/API separation

**Testing**:
- ✅ 103 total tests passing
- ✅ 0 compilation errors
- ✅ 0 compilation warnings

---

## How to Use This Directory

When a proposal is implemented:

1. **Update the proposal status** at the top of the file
2. **Add implementation report link** to relevant documentation
3. **Move to implemented/** directory
4. **Update this README** with summary and links
5. **Create implementation report** in docs/ for reference

---

## Related Directories

### [ongoing/](../ongoing/)
Proposals that are substantially implemented (75-95%) with remaining work in progress.

**Current**: 1 proposal (rust-refactoring)

### [proposals/](../)
Active proposals awaiting implementation.

**Current**: 13 proposals

---

**Last Updated**: 2026-01-21
