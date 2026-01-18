# Refactoring Execution Checklist

Quick reference for executing the source reorganization.

---

## Pre-Flight Checks

### ✅ Embedded Content Audit

| File | Embedded Content | Path Change Required |
|------|-----------------|---------------------|
| `src/linux/moss/src/templates.rs:10` | `include_dir!("$CARGO_MANIFEST_DIR/../../../manifests")` | ✅ YES: `../../manifests` |
| `manifests/` directory | Templates, compose files | ❌ NO: Stays at workspace root |

**Action:** Update `include_dir!` macro after moving moss → Phase 3.2

### ✅ Build Scripts Audit

| Script | References src/? | Change Required |
|--------|------------------|----------------|
| `installer/build-dist.ps1` | ❌ No | ✅ None - uses workspace-level cargo |
| `installer/build-linux.ps1` | ❌ No | ✅ None - uses workspace-level cargo |
| `installer/build-windows.ps1` | ❌ No | ✅ None - uses workspace-level cargo |
| `installer/push-moss-to-all-stones.ps1` | ❌ No | ✅ None - uses dist/ binaries |
| `installer/NewStone.ps1` | ❌ No | ✅ None - uses dist/ binaries |

**Conclusion:** ✅ Build scripts safe (no source path dependencies)

### ✅ Documentation Audit

Files requiring updates (via grep):

```
docs/STONE-INSTALLATION-FLOW.md:227
docs/CONTAINER-DIAGNOSTICS-PLAN.md:392,397,403
docs/MOSS-CROSS-PLATFORM-REFACTOR.md (multiple)
docs/MOSS-CONFIG.md:114,142
docs/PORT-ALLOCATION.md:35-142
docs/decisions/LANTERN-0001-service-registry-architecture.md
docs/proposals/LANTERN-SERVICE-PROPOSAL.md
```

**Regex replacements:**
```
src/linux/moss         → src/moss
src/linux/common       → src/common
src/windows/garden-rake → src/rake
```

---

## Phase Checklist

### Phase 0: Impact Assessment ✓

- [x] Audit embedded content (`include_dir!`, `include_str!`)
- [x] Test build scripts (verify no src/ paths)
- [x] Grep docs for hardcoded paths
- [x] Document findings

### Phase 1: Preparation

- [ ] Create directory structure
  ```powershell
  mkdir src/{moss,rake}/src/{api,domain,infra,commands}
  mkdir src/common/src/{types,net,errors,utils}
  mkdir src/build-utils/src
  ```
- [ ] Update workspace Cargo.toml members
- [ ] Create build-utils crate with build.rs template
- [ ] Set up Rust idioms (workspace deps, MSRV)
- [ ] Commit: "chore: prepare new source structure"

### Phase 2: Common Library

- [ ] `git mv src/linux/common src/common`
- [ ] Create module structure (types/, net/, errors/, utils/)
- [ ] Move existing types to modules
- [ ] Create lib.rs with re-exports
- [ ] Extract discovery protocol to net/
- [ ] Create error types with thiserror
- [ ] Test: `cargo test -p zen-common`
- [ ] Commit: "refactor: reorganize common library"

### Phase 2.5: Standardization Layer

- [ ] Create `src/common/src/constants/` directory
- [ ] Create `constants/mod.rs` (re-export module)
- [ ] Create `constants/timeouts.rs` with GARDEN_* env support
  ```rust
  // All timeouts: discovery, first-boot, cache, HTTP
  // Each with DEFAULT_* constant + function that reads env var
  ```
- [ ] Create `constants/paths.rs` with platform cfg
  ```rust
  // Templates dir, compose files, flag files
  // GARDEN_TEMPLATES_DIR override
  ```
- [ ] Create `constants/error_codes.rs`
  ```rust
  // Modules: service, template, docker, discovery
  // All error code string constants
  ```
- [ ] Create `constants/limits.rs`
  ```rust
  // Retry attempts, buffer sizes, etc.
  ```
- [ ] Create `src/common/src/responses.rs`
  ```rust
  // ApiResponse<T> with timestamp, request_id
  // IntoResponse for axum
  ```
- [ ] Create `src/common/src/jobs.rs`
  ```rust
  // RetryPolicy, BackoffStrategy
  // retry_with_policy helper
  ```
- [ ] Add chrono dependency to common/Cargo.toml
- [ ] Update common/src/lib.rs with new re-exports
- [ ] Test: `cargo test -p zen-common`
- [ ] Commit: "feat: add standardization layer (constants, responses, jobs)"

### Phase 3: Moss Refactor

- [ ] `git mv src/linux/moss src/moss`
- [ ] **CRITICAL:** Update templates.rs embedded path
  ```rust
  // Line 10
  static EMBEDDED_MANIFESTS: Dir = include_dir!("$CARGO_MANIFEST_DIR/../../manifests");
  ```
- [ ] Test template loading: `cargo test -p garden-moss`
- [ ] Create lib.rs and module structure
- [ ] Split main.rs into api/, domain/, infra/
- [ ] Replace error_response with ApiError types
- [ ] Add rustdoc to public items
- [ ] Test: `cargo test -p garden-moss`
- [ ] Manual test: Offer service (template loading)
- [ ] Commit: "refactor: reorganize moss with layered architecture"

### Phase 4: Rake Refactor

- [ ] `git mv src/windows/garden-rake src/rake`
- [ ] Create lib.rs and command modules
- [ ] Split main.rs into commands/
- [ ] Move discovery to common/net (remove local copy)
- [ ] Create client/ module for HTTP ops
- [ ] Test: `cargo test -p garden-rake`
- [ ] Manual test: `garden-rake status`, `garden-rake list`
- [ ] Commit: "refactor: reorganize rake CLI with command pattern"

### Phase 5: Lantern Update

- [ ] Already at `src/lantern` (no move)
- [ ] Apply layered structure (api/, domain/, infra/)
- [ ] Update deps to `zen-common = { path = "../common" }`
- [ ] Test: `cargo test -p garden-lantern`
- [ ] Commit: "refactor: apply layered architecture to lantern"

### Phase 6: Clean Up

- [ ] Delete old folders
  ```powershell
  Remove-Item src/linux -Recurse -Force
  Remove-Item src/windows -Recurse -Force
  ```
- [ ] Verify no broken imports
- [ ] Test full workspace build
  ```powershell
  cargo clean
  cargo build --workspace --release
  cargo test --workspace
  cargo clippy --workspace -- -D warnings
  cargo fmt --all -- --check
  ```
- [ ] Test installer scripts
  ```powershell
  .\installer\build-dist.ps1 -Release
  ```
- [ ] Verify binaries in dist/ work
- [ ] Commit: "refactor: remove old platform folders"

### Phase 7: Documentation

- [ ] Update docs with regex replacements
- [ ] Manual review of critical docs
- [ ] Generate rustdoc: `cargo doc --workspace --no-deps --open`
- [ ] Create ARCHITECTURE.md
- [ ] Update CONTRIBUTING.md (Rust guidelines)
- [ ] Update BUILD.md (or create)
- [ ] Commit: "docs: update for new source structure"

---

## Validation Tests

### Compilation
```powershell
cargo clean
cargo build --workspace --release
cargo test --workspace --release
```

### Linting
```powershell
cargo clippy --workspace --all-targets -- -D warnings
cargo fmt --all -- --check
```

### Build Scripts
```powershell
.\installer\build-dist.ps1 -Release -SkipTests
# Verify: dist/linux/garden-moss, garden-rake
# Verify: dist/windows/garden-moss.exe, garden-rake.exe
```

### Manual Smoke Tests
```powershell
# Windows
.\dist\windows\garden-rake.exe --version
.\dist\windows\garden-rake.exe status --at http://192.168.1.X:7185

# Linux (via SSH to stone)
./garden-moss --version
./garden-rake list
```

### Template Loading Test
```powershell
# On stone or local moss
curl http://localhost:7185/templates
# Should return list of offerings (manifests loaded)
```

---

## Rollback Strategy

### Per-Phase Rollback
```powershell
# If Phase N fails
git log --oneline  # Find commit before Phase N
git reset --hard <commit-hash>
```

### Tagged Checkpoints
```powershell
# After each successful phase
git tag refactor-phase-N-complete
git push origin refactor-phase-N-complete

# Rollback to checkpoint
git reset --hard refactor-phase-N-complete
```

### Nuclear Option
```powershell
# Abort entire refactor
git checkout main
git branch -D refactor/src-reorganization
```

---

## Common Issues & Solutions

### Issue: Template not found after moving moss

**Symptom:** Runtime error loading offerings
**Cause:** Embedded manifest path still uses old relative path
**Solution:** 
```rust
// src/moss/src/templates.rs:10
static EMBEDDED_MANIFESTS: Dir = include_dir!("$CARGO_MANIFEST_DIR/../../manifests");
```

### Issue: Build scripts fail to find binaries

**Symptom:** dist/ directory empty or missing binaries
**Cause:** Workspace members not updated in Cargo.toml
**Solution:** Verify workspace Cargo.toml members list
```toml
[workspace]
members = ["src/common", "src/moss", "src/rake", "src/lantern"]
```

### Issue: Import errors after moving modules

**Symptom:** Compiler errors about missing modules
**Cause:** Stale build cache or incorrect module paths
**Solution:**
```powershell
cargo clean
cargo build --workspace
```

### Issue: Tests fail with path errors

**Symptom:** Integration tests can't find modules
**Cause:** Missing lib.rs in binary crates
**Solution:** Add lib.rs that re-exports modules
```rust
// src/moss/src/lib.rs
pub mod api;
pub mod domain;
pub mod infra;
```

### Issue: Documentation links broken

**Symptom:** Dead links in rustdoc
**Cause:** Outdated module references
**Solution:** Use `cargo doc` warnings to find broken links
```powershell
cargo doc --workspace 2>&1 | Select-String "warning"
```

---

## Success Verification

### Must Pass
- ✅ All tests pass: `cargo test --workspace`
- ✅ Clippy clean: `cargo clippy --workspace -- -D warnings`
- ✅ Formatted: `cargo fmt --all -- --check`
- ✅ Build scripts work: `build-dist.ps1 -Release` succeeds
- ✅ Binaries functional: Manual smoke tests pass

### Should Pass
- ✅ No files > 500 lines (target < 300)
- ✅ Rustdoc builds: `cargo doc --workspace --no-deps`
- ✅ Discovery protocol unified (single module)
- ✅ Error handling uses thiserror/anyhow
- ✅ Documentation updated (no src/linux references)

### Nice to Have
- ✅ Code coverage maintained or improved
- ✅ No new clippy warnings
- ✅ Faster compile times (fewer cross-crate deps)
- ✅ Integration tests use lib.rs exports

---

## Timeline Checkpoints

| Day | Phase | Deliverable |
|-----|-------|-------------|
| 1 | 0-1 | Impact audit + structure created |
| 2 | 2 | Common library reorganized |
| 3-4 | 3 | Moss refactored + tested |
| 5 | 4 | Rake refactored + tested |
| 6 | 5-6 | Lantern updated, old folders removed |
| 7 | 7 | Documentation updated |
| 8+ | Testing | Full validation + fixes |

**Total: ~8-10 days with buffer**

---

**Last Updated:** January 17, 2026  
**Status:** Ready for execution  
**Owner:** [Assign implementer]
