---
audience: [contributor, ai]
doc_type: notes
status: current
last_verified: 2026-01-18
canonical: true
---

# Architecture Decision Records (ADRs)

**Index of all architectural decisions**

---

## Active ADRs

### Build & Distribution
- **[BUILD-0001](BUILD-0001-natural-flow-versioning.md)**: Natural Flow Versioning
  - **Status**: Accepted (2026-01-15)
  - **Rationale**: major.minor.timestamp format, timestamp = truth
  - **Impact**: Predictable versioning, build-time revision injection

### Compatibility
- **[COMPAT-0001](COMPAT-0001-offering-compatibility-rules.md)**: Offering Compatibility Rules
  - **Status**: Accepted
  - **Rationale**: Version compatibility policies for service offerings
  - **Impact**: Clear upgrade/downgrade rules, semantic versioning for offerings

### Lantern (Registry)
- **[LANTERN-0001](LANTERN-0001-service-registry-architecture.md)**: Service Registry Architecture
  - **Status**: Accepted
  - **Rationale**: Optional central directory for service discovery
  - **Impact**: Faster discovery than mDNS, Windows compatibility

### Moss (Daemon)
- **[MOSS-0001](MOSS-0001-persistent-registry-and-adoption.md)**: Persistent Registry and Adoption
  - **Status**: Accepted
  - **Rationale**: Stone-local service registry with persistence
  - **Impact**: Survives reboots, enables offline operation

### Offerings (Services)
- **[OFFER-0001](OFFER-0001-offering-taxonomy-and-recommendations.md)**: Offering Taxonomy
  - **Status**: Accepted
  - **Rationale**: Categorization scheme for service offerings
  - **Impact**: Organized service catalog (data, cache, compute, ai, storage, messaging, web)

### Rake (CLI)
- **[RAKE-0010](RAKE-0010-tending-cached-endpoint-resolution.md)**: Cached Endpoint Resolution
  - **Status**: Accepted
  - **Rationale**: Tending command for resolution cache management
  - **Impact**: Performance optimization, stale endpoint cleanup

---

## Pending (Under Review)

_No pending ADRs at this time._

See [proposals/](../proposals/) for active proposals under discussion.

---

## Superseded / Deprecated

_No superseded ADRs at this time._

---

## ADR Process

1. **Proposal**: Create in `docs/proposals/` with `status: proposed`
2. **Review**: Discuss in GitHub Issues/Discussions
3. **Decision**: Accept → Create ADR | Reject → Archive with rationale
4. **Formalize**: Convert accepted proposal to ADR format
5. **Update Index**: Add to this README.md

### ADR Template

```markdown
---
audience: [contributor, maintainer, ai]
doc_type: adr
status: accepted|proposed|superseded
last_verified: YYYY-MM-DD
canonical: true
---

# ADR-XXXX: Title

**Status**: Accepted | Proposed | Deprecated | Superseded by ADR-YYYY  
**Date**: YYYY-MM-DD  
**Deciders**: [names/roles]  
**Tags**: [relevant, tags]

---

## Context

[Problem statement and constraints]

What situation led to this decision? What requirements must be met?

---

## Decision

[What was decided - clear, unambiguous statement]

We will [ACTION] by [METHOD].

---

## Rationale

[Why this decision was made]

- Reason 1: [explanation]
- Reason 2: [explanation]
- Reason 3: [explanation]

---

## Consequences

### Positive
- Benefit 1
- Benefit 2

### Negative
- Trade-off 1
- Trade-off 2

### Neutral
- Implication 1
- Implication 2

---

## Alternatives Considered

### Alternative 1: [Name]
- **Description**: [brief]
- **Pros**: [list]
- **Cons**: [list]
- **Rejected because**: [reason]

### Alternative 2: [Name]
- **Description**: [brief]
- **Pros**: [list]
- **Cons**: [list]
- **Rejected because**: [reason]

---

## References

- [Related ADRs]
- [Proposals]
- [External resources]
```

### ADR Naming Convention

**Format**: `<PREFIX>-<NUMBER>-<slug>.md`

**Prefixes**:
- `BUILD-` - Build system, versioning, distribution
- `COMPAT-` - Compatibility policies
- `LANTERN-` - Lantern registry decisions
- `MOSS-` - Moss daemon decisions
- `OFFER-` - Offering/service decisions
- `RAKE-` - Rake CLI decisions
- `POND-` - Security/Pond decisions
- `CLI-` - CLI design decisions
- `API-` - API design decisions

**Examples**:
- `BUILD-0001-natural-flow-versioning.md`
- `POND-0002-totp-stone-admission.md`
- `CLI-0001-dual-syntax-taxonomy.md`

### Numbering

- Numbers are unique within prefix (not globally unique)
- Use leading zeros: 0001, 0010, 0100
- Gaps are acceptable (e.g., 0001, 0010, 0015)

---

## Guidelines

### When to Create an ADR

**Do create ADR for**:
- Architectural choices with long-term impact
- Trade-offs between competing approaches
- Decisions that affect multiple components
- Changes to core abstractions or protocols
- Security model changes

**Don't create ADR for**:
- Implementation details (code comments suffice)
- Trivial choices with no significant trade-offs
- Reversible decisions with low cost
- Temporary workarounds

### ADR Immutability

Once an ADR is **Accepted**:
- Content should not change (preserve decision context)
- Status can change: Accepted → Superseded
- If decision changes, create new ADR and mark old as superseded

### Linking ADRs

- Reference related ADRs in "References" section
- Use relative links: `[BUILD-0001](BUILD-0001-natural-flow-versioning.md)`
- Link from specifications to ADRs for design rationale
- Link from proposals to related ADRs

---

## ADR Statistics

- **Total ADRs**: 6
- **By Status**: 
  - Accepted: 6
  - Proposed: 0
  - Superseded: 0
- **By Domain**:
  - Build: 1
  - Compatibility: 1
  - Lantern: 1
  - Moss: 1
  - Offerings: 1
  - Rake: 1

---

## Related Documentation

- [STRUCTURE.md](../STRUCTURE.md) - Documentation structure rules
- [Proposals](../proposals/) - Active proposals under review
- [Technical Specification](../specifications/technical.md) - Implementation details
- [Archive](../archive/) - Historical decisions and proposals

---

**Last Updated**: January 18, 2026  
**Maintained By**: Architecture Team  
**Review Cycle**: As needed (updated when ADRs added/changed)
