# AI-0018 - Chunk size hard cap enforcement

Status: Accepted
Date: 2025-11-10
Owners: Koan Context Guild

**Contract**
- Inputs: extracted document sections scheduled for semantic chunking
- Outputs: `ChunkedContent` segments reporting ≤1000 estimated tokens while preserving ordering and offsets
- Error Modes: section slicing occurs when no full section fits; emits telemetry on repeated trims; failure if content cannot be sliced without data loss
- Acceptance Criteria: chunk emission honours 800-1000 token target, never reports >1000 tokens, and documents the trim in debug logs for observability

## Context

Precision on chunk size is mandatory for downstream embedding cost controls and batched request limits. Earlier iterations allowed short-lived "drift" that frequently produced 1001-token chunks, breaking chunking specs and threatening hard caps enforced by external providers. Losing whole chunks to stay under a batch budget would wipe out up to 12% of planned payload.

## Decision

- Maintain a strict default ceiling of 1000 tokens per chunk; no tolerance margin.
- When a section would overflow the active chunk, split the section at whitespace boundaries to fit remaining capacity, pushing the remainder into the next chunk.
- Keep buffered overlap logic (50 tokens) untouched, but reflow tokens after every split so the estimator reflects the trimmed length.
- Surface the policy in `Chunker.cs` and tests, keeping hard caps the observable contract for batching and pricing engines.

## Consequences

- Chunk construction is deterministic: callers can budget `ceil(totalTokens / 1000)` without guarding for drift.
- Section splitting introduces more, smaller sections in rare cases; offsets are recalculated so similarity metadata stays correct.
- Tests enforce the ≤1000 rule; regressions immediately fail the suite.
- Future relaxations (if ever needed) must occur through configuration plus matching contract updates rather than ad-hoc drift.

## References

- `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Chunker.cs`
- `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Chunking/ChunkingService.Spec.cs`
- AI-0003 Tokenization and Cost Strategy
