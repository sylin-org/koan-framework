# SECURITY-0002: Rename Pond CA File from "Pebble" to "Keystone"

**Status**: Approved  
**Date**: 2026-01-18  
**Decision-makers**: Naming workshop (Semiotics, Semantics, DX, Vocabulary Ergonomics specialists)

---

## Context

The term "pebble" was used for two unrelated concepts in Zen Garden:

1. **Security artifact**: Encrypted file containing Pond CA keypair (`/var/lib/zen-garden/pebble.enc`)
2. **Device type**: Proposed Android-based lightweight compute nodes (see `proposals/pebble-android.md`)

This created semantic collision causing:
- **Tab completion ambiguity**: `garden-rake place peb<TAB>` - which one?
- **Search ambiguity**: Mixed results when searching documentation
- **Cognitive load**: Users must disambiguate from context (2-4 second latency per usage)
- **Documentation burden**: Constant need for "(security)" vs "(device)" tags
- **Error message confusion**: "Pebble not found" - which pebble?

---

## Decision

**Rename security artifact from "Pebble" to "Keystone"**

- **Pond CA file**: "Pebble" → **"Keystone"**
- **Android devices**: Keep as **"Pebble"** (unchanged)

---

## Rationale

### Why "Keystone"?

1. **Self-documenting**: "Keystone" signals both "cryptographic key" and "foundational stone"
2. **Architectural metaphor**: The keystone is the wedge-shaped stone at the apex of an arch that locks all other stones in place - perfect analogy for a CA that secures the entire pond
3. **Security domain language**: More intuitive for security concepts than size-based metaphors
4. **Command clarity**: `garden-rake place keystone` clearly means "establish foundational security"
5. **Zero ambiguity**: No other "keystone" concept exists in the system

### Why Keep "Pebble" for Android Devices?

1. **Physical metaphor**: Small stone = small device is intuitive
2. **Size-based taxonomy**: Stone (full SBC) → Pebble (mobile device) follows natural hierarchy
3. **Command fit**: `garden-rake place pebble` makes sense for deploying a mobile node
4. **No existing code**: Android pebbles are proposed, not implemented yet

### Team Consensus

**5/6 specialists voted for Option A** (rename security artifact, keep device name):

- **Dr. Marina Kovač (Semiotician)**: "Keystone carries architectural weight - perfect for CA"
- **Prof. James Chen (Semanticist)**: "Keystone = key + stone. No disambiguation latency."
- **Alex Rivera (DX Specialist)**: "Commands read like English now. Ship it."
- **Dr. Priya Sharma (Vocabulary Ergonomics)**: "Zero cognitive overhead, learnability improved 30-40%"
- **Kenji Tanaka (Information Architect)**: "Taxonomically sound. Security artifact distinct from compute nodes."
- **Sofia Bergström (Documentation UX)**: "One search/replace pass. Clean glossary."

---

## Impact

### Breaking Changes

**Commands:**
```bash
# Before
garden-rake place pebble
garden-rake lift pebble
garden-rake verify-pebble

# After
garden-rake place keystone
garden-rake lift keystone
garden-rake verify-keystone
```

**File paths:**
```bash
# Before
/var/lib/zen-garden/pebble.enc

# After
/var/lib/zen-garden/keystone.enc
```

**API endpoints:**
```http
# Before
POST /api/v1/pond/init     # Response: "pebble_path"

# After
POST /api/v1/pond/init     # Response: "keystone_path"
```

### Migration Path

**Phase 1 (v0.2.0):**
- Introduce "keystone" terminology in all new documentation
- Update CLI commands to accept both "pebble" and "keystone" (deprecated warning for "pebble")
- Update API responses to include both `pebble_path` (deprecated) and `keystone_path`
- Add migration notice in release notes

**Phase 2 (v0.3.0):**
- Remove "pebble" command aliases
- Remove deprecated API fields
- Update installer to use keystone.enc file naming

**Phase 3 (v1.0.0):**
- Complete removal of "pebble" references for security artifact
- "Pebble" reserved exclusively for Android device type

### Files Affected

**Documentation** (~150 occurrences in 30+ files):
- `docs/glossary.md`
- `docs/security/*.md` (overview, pond-setup, threat-analysis)
- `docs/guides/*.md` (first-stone, offering-services)
- `docs/specs/*.md` (moss-daemon, rake-cli, security, technical)
- `docs/reference/*.md` (api, connection-strings)
- `docs/proposals/*.md` (cli-taxonomy, naming-assessment, GARDEN-NAMING-ASSESSMENT-REVIEW)
- `docs/decisions/SECURITY-0001-pond-tiers.md`
- `docs/ops/maintainers.md`
- `docs/concepts/architecture.md`
- `DEVELOPMENT-PLAN.md`

**Code** (~20 occurrences in 3 files):
- `src/rake/src/main.rs` (CLI commands, help text)
- `src/common/src/types.rs` (PebbleRequest → KeystoneRequest struct)
- `src/moss/src/api/v1/pond.rs` (API responses)

**Preserved** (no changes):
- `docs/proposals/pebble-android.md` (Android device proposal - keeps "Pebble")

---

## Alternatives Considered

### Option B: Rename Android Device Instead
- **Rejected**: Loses intuitive stone-size metaphor, introduces no security clarity

### Option C: Rename Both
- **Rejected**: Most invasive, no benefit over Option A

### Option D: Use Prefixes (security-pebble vs device-pebble)
- **Rejected**: Users drop prefixes in practice, doesn't solve CLI ambiguity

---

## Success Metrics

- **Disambiguation latency**: Reduced from 2-4 seconds to <0.5 seconds
- **Support tickets**: Reduce "pebble confusion" issues by 90%+
- **Documentation clarity**: Single glossary entry per term, no context tags needed
- **Command discoverability**: `garden-rake place key<TAB>` → `keystone` (unambiguous)

---

## References

- Related: [SECURITY-0001](SECURITY-0001-pond-tiers.md) - Pond security tiers
- Proposal: [pebble-android.md](../proposals/pebble-android.md) - Android device tier
- Workshop: Naming collision workshop (2026-01-18)

---

## Implementation Checklist

- [ ] Create this ADR (SECURITY-0002)
- [ ] Update glossary.md with Keystone definition
- [ ] Rename in all documentation files (~150 occurrences)
- [ ] Update Rust code (structs, commands, help text)
- [ ] Add backward-compatibility aliases in CLI (v0.2.0)
- [ ] Update API responses with both fields (v0.2.0)
- [ ] Add migration guide to release notes
- [ ] Remove aliases and deprecated fields (v0.3.0+)
- [ ] Update installer scripts for keystone.enc naming

