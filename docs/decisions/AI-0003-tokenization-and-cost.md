# AI-0003 - Tokenization and cost strategy

Status: Proposed
Date: 2025-08-19
Owners: Koan AI

## Context

Budgets, observability, and routing require reasonably accurate token counts and cost estimates across providers.

## Decision

- Prefer provider-native tokenizers; fall back to estimator with documented accuracy targets (±5%).
- Expose token usage (prompt/completion/total) in responses and metrics; cost estimators pluggable per model.

## Consequences

- Tests must validate tokenizer accuracy per provider and fallback bounds.
- Dashboards display token and cost metrics; privacy posture redacts content.
