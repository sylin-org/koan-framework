# BUILD-0001: Natural Flow Versioning

**Status:** Accepted  
**Date:** 2026-01-18  
**Context:** Zen Garden versioning strategy

## Decision

Adopt **Natural Flow Versioning** where revision numbers are timestamps representing the moment of build creation.

### Format

```
{major}.{minor}.{timestamp}
```

Example: `0.1.202601181235`

### Semantics

- **Major (0)**: API generation - breaking changes, fundamental platform shifts
- **Minor (1)**: Phase/evolution - cohesive development periods with new capabilities
- **Revision**: **This moment** - timestamp (YYYYMMDDHHmm) when build was created

### Rationale

Traditional semantic versioning treats revision as "bug fix count," which is:
- Arbitrary (what counts as a bug vs feature?)
- Stateful (requires tracking)
- Pretends semantic meaning where there often is none

**Zen approach:** Accept that revision numbers don't carry semantic weight. Instead, let them represent **truth** - the actual moment a build exists.

### Benefits

1. **Truthful** - Timestamp is objective reality, not subjective interpretation
2. **Natural** - Time flows, builds flow with it - no artificial counting
3. **Simple** - No state management, no deciding "is this revision 847 or 848?"
4. **Traceable** - Know exactly when any artifact was built
5. **Monotonic** - Never decreases, never conflicts, naturally ordered
6. **Present-focused** - Each build identified by its moment of birth

### Philosophy Alignment

This reflects Zen Garden's core principles:
- **Simplicity over complexity** - No version bump debates
- **Truth over convention** - Timestamp is factual, not aspirational
- **Flow over rigidity** - Builds happen when they happen
- **Present-focused** - This build, this moment

### Usage

**version.json** (manual control):
```json
{
  "major": 0,
  "minor": 1,
  "description": "Initial Garden phase - stone coordination & offerings"
}
```

**Generated version** (automatic):
```
0.1.202601181235
```

**When to bump:**
- **Major**: Breaking API changes, fundamental platform shifts (rare)
- **Minor**: New cohesive phase (e.g., pond clustering complete, multi-stone tested)
- **Revision**: Automatic - every build gets current timestamp

### Implementation

- `version.json` stores major/minor at repo root
- `dist.ps1` reads version and appends timestamp
- All build scripts receive full version string
- Cargo.toml files updated before build

### Comparison to Traditional SemVer

| Aspect | Traditional | Natural Flow |
|--------|-------------|--------------|
| Revision meaning | "Bug fix #N" | "Built at time T" |
| State required | Patch counter | None (time) |
| Human decision | Every patch | Only major/minor |
| Truthfulness | Subjective | Objective |
| Build correlation | Indirect | Direct (timestamp) |

### Future Considerations

If production release versioning needs differ, add mode flag:
```json
{
  "major": 0,
  "minor": 1,
  "mode": "auto",
  "description": "..."
}
```

But start simple - timestamp revision for all builds.

## Consequences

### Positive
- Zero cognitive load for versioning during development
- Build artifacts self-document their creation time
- No merge conflicts on version files
- Natural alignment with CI/CD timestamp tracking

### Negative
- Deviates from strict semantic versioning convention
- Revision numbers are large (12 digits)
- Doesn't indicate "distance" between builds (but time does)

### Neutral
- Users must understand revision = timestamp, not semantic patch level
- Documentation should explain the philosophy

## Examples

```
0.1.202601181235  # First phase, built Jan 18 2026 12:35
0.2.202603151430  # Second phase, built Mar 15 2026 14:30
1.0.202608220900  # Production launch, built Aug 22 2026 09:00
```

Each tells you: what API generation, what phase, exactly when it was born.
