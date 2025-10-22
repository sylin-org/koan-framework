# ARCH-0062 S8 legacy snapshot removal and sample streamline

Status: Accepted  
Date: 2025-10-08  
Authors: Koan Framework Team  
Supersedes: ARCH-0056 (scope: Canon naming) (clarifies separation)  
Relates-To: DX-0040, ARCH-0053, ARCH-0059  

## 1. Decision
Remove the archived `legacy/S8.Canon-legacy-20251005` snapshot (six projects) from the repository and solution to:
- Eliminate duplicate/obsolete Canon-era scaffolding that produced build noise
- Reduce maintenance overhead and symbol ambiguity around `AddKoan` bootstrap and Canon module usage
- Clarify the active S8 sample surface (`samples/S8.Canon/*`) as the only supported canonical demonstration going forward

## 2. Context / Problem Statement
A frozen snapshot of an older S8 Canon sample tree was preserved under `legacy/` for historical reference. As the framework evolved toward entity‑first patterns (ARCH-0053) and cache / observability refinements (ARCH-0059, ARCH-0060, ARCH-0061), the snapshot:
- Diverged from current minimal host + auto‑registration conventions
- Referenced removed or renamed testing helpers (e.g., Flow test constants)
- Caused build failures (missing modern `WebApplication` bootstrap or `AddKoan` resolution) when retained in the main solution
- Added cognitive overhead when searching for active Canon code, producing misleading hits

Retaining it violated current hygiene principles: no dead scaffolds, lean solution surface, and clear “reference = intent” signaling via project inclusion.

## 3. Options Considered
1. Keep snapshot in repo but exclude from `.sln` (still incurs grep noise, drift risk)  
2. Convert snapshot to a doc-only artifact (extract code into snippets)  
3. Tag historical commit, remove snapshot tree from `dev` (chosen)  
4. Attempt in-place modernization (cost > value; duplicates existing active sample)  

## 4. Rationale
Option (3) gives maximum clarity with minimal ongoing cost:
- Aligns with ARCH-0041 posture (instructional, not archival dump)  
- Strengthens single authoritative S8 sample path—improves discoverability & onboarding  
- Removes 5 obsolete project GUIDs + 1 solution folder reducing solution parse & CI graph  
- Unblocks clean build and reduces future refactor surface (fewer stale references during global renames)  

## 5. Consequences
Positive:
- Faster solution load & simpler grep/search results
- Elimination of build errors tied to outdated bootstrap patterns
- Reduced risk of contributors copying obsolete code

Neutral / Acceptable:
- Historical diff now accessed via git history (tag recommendation below)

Negative / Mitigations:
- Loss of side-by-side comparison: mitigate by adding a lightweight note (this ADR) and optionally tagging the last commit containing the snapshot (recommended tag: `archive/s8-legacy-20251005`).

## 6. Implementation Summary
- Removed 6 `Project` blocks & GUID config entries from `Koan.sln`
- Deleted directory `legacy/S8.Canon-legacy-20251005/`
- Clean build verified (no errors; only benign SDK warnings) post removal
- Updated RedisInbox sample to ensure modern Web SDK & `AddKoan` usage after legacy noise cleared

## 7. Edge Cases & Follow Ups
Edge considerations:
- CI referencing deleted project paths (none detected; build passed)
- Documentation links: no canonical docs pointed to the snapshot (confirmed by absence in toc entries)
- Tests: no test projects depended on the removed GUIDs

Follow ups (optional):
- Remove redundant `<FrameworkReference Include="Microsoft.AspNetCore.App" />` entries in Web SDK projects to silence NETSDK1086 warnings
- Tag last commit with legacy snapshot: `git tag archive/s8-legacy-20251005 <commit>`
- Add a brief note in `docs/decisions/index.md` linking to this ADR under “Recent archival decisions” (if an index section is curated for removals)

## 8. Measured Success Criteria
- Koan.sln builds without legacy S8 errors (achieved)  
- Developer search for `S8.Canon` returns only active sample paths (achieved)  
- No new PRs add references to removed GUIDs (to be monitored)  

## 9. References
- ARCH-0053 Entity-first & auto-registrar
- ARCH-0041 Documentation posture
- ARCH-0059 Cache module foundation
- ARCH-0061 JSON layer unification

---
Decision enforces repository clarity and reduces drag; future archival should prefer tags + ADR over in-tree code duplication.
