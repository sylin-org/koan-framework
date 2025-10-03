---
type: GUIDE
domain: flow
title: "Semantic Pipelines Playbook"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/guides/semantic-pipelines.md
---

# Semantic Pipelines Playbook

## Contract

- **Inputs**: Koan Flow pillar configured, required data/vector adapters installed, and entities prepared for streaming via `AllStream` or `QueryStream`.
- **Outputs**: Durable pipelines that enrich data, persist vectors, and trigger downstream events without bespoke orchestration code.
- **Error Modes**: Ignoring provider rate limits, omitting cancellation tokens, persisting vectors with mismatched dimensions, or branching logic that never calls `.Save()`.
- **Success Criteria**: Pipelines execute idempotently, AI work is batched and observable, failures requeue or dead-letter correctly, and telemetry shows throughput + costs.

### Edge Cases

- **Rate limits** – coordinate `AiEmbedOptions.Batch` with provider quotas to avoid throttling.
- **Mixed content** – branch on MIME type or payload size before running expensive AI steps.
- **Legacy records** – backfill embeddings via Flow events or background jobs so search stays consistent.
- **Cancellation** – always thread the `CancellationToken` through `.ExecuteAsync(ct)` for long-running jobs.
- **Replays** – guard replay pipelines with idempotency checks to prevent duplicate notifications.

---

## How to Use This Playbook

Use this checklist while building or refactoring a pipeline. Deep dives live in the Flow reference.

- 📌 Canonical reference: [Flow Pillar Reference](../reference/flow/index.md#semantic-pipelines)
- 🧭 Intake & adapters: [Adapters & ingestion](../reference/flow/index.md#adapters--ingestion)
- 🧮 AI integration: [Embedding guidance](../reference/flow/index.md#semantic-pipelines)
- 🚨 Error handling: [Error handling & retries](../reference/flow/index.md#error-handling--retries)
- 📊 Observability: [Monitoring & diagnostics](../reference/flow/index.md#monitoring--diagnostics)

---

## 1. Choose the Right Stream Source

- Prefer `Entity.AllStream()` or `Entity.QueryStream()` for large workloads; fall back to paged queries only when the dataset is intrinsically small.
- Define clear filters before starting to avoid fan-out of ineligible records.
- Record assumptions (date windows, status flags) inside the pipeline via `.Trace(...)` so observability captures scope.

🧭 Reference: [Flow entities & stages](../reference/flow/index.md#flow-entities--stages)

---

## 2. Set Up Baseline Mutations

- Start every pipeline with `.ForEach(...)` to establish invariant state (status flags, timestamps).
- If external services contribute metadata, wrap calls in `try/catch` and surface failures via `.RecordError` or branch clauses.
- Keep IO-bound work async to avoid blocking pipeline threads.

🧭 Reference: [Core operations](../reference/flow/index.md#core-operations)

---

## 3. Layer in AI Steps

- Call `.Tokenize(...)` before `.Embed(...)` when prompts require normalization or truncation.
- Set `AiEmbedOptions.Batch` to balance throughput and rate limits; monitor provider quotas.
- Persist embeddings with `.Save()` so entity + vector writes remain atomic.

🧭 Reference: [Pipeline quick start](../reference/flow/index.md#pipeline-quick-start)

---

## 4. Design Branching & Recovery

- Use `.Branch(...)` with `.OnSuccess`, `.OnFailure`, and `.When` to encode business outcomes.
- Ensure each branch persists or forwards data (`.Save()`, `.Notify(...)`, event dispatch).
- Capture rich diagnostics with `.Trace(...)` or custom logging inside each branch.

🧭 Reference: [Branching & error capture](../reference/flow/index.md#branching--error-capture)

---

## 5. Notify Downstream Systems

- Prefer `.Notify(...)` for lightweight messaging; switch to `FlowEvent` or Messaging pillar publishers when payloads need richer contracts.
- Tag notifications with correlation IDs (entity IDs, batch IDs) so replays remain idempotent.
- Document retry expectations—consumers should treat notifications as at-least-once.

🧭 Reference: [Events & messaging](../reference/flow/index.md#events--messaging)

---

## 6. Harden for Production

- Wrap AI and network calls with retry policies or provider-specific backoff logic.
- Enforce guardrails via `FlowInterceptors` to reject bad payloads as early as possible.
- Add health contributors that check queue depth, stage counts, and latency thresholds.

🧭 Reference: [Interceptors & lifecycle](../reference/flow/index.md#interceptors--lifecycle) · [Monitoring & diagnostics](../reference/flow/index.md#monitoring--diagnostics)

---

## 7. Operate & Iterate

- Schedule recurring replays to backfill new stages or regenerate embeddings when models change.
- Monitor cost and token metrics; trim prompts or summaries proactively.
- Feed telemetry into dashboards that highlight throughput, error rate, and provider spend.

🧭 Reference: [Error handling & retries](../reference/flow/index.md#error-handling--retries)

---

## Review Checklist

- [ ] Pipeline streams data and threads cancellation tokens end-to-end.
- [ ] AI steps batch work and validate embedding dimensions before saving.
- [ ] Success and failure branches both persist state or emit compensating events.
- [ ] Notifications or Flow events include idempotent identifiers.
- [ ] Health checks and telemetry cover backlog, latency, and provider usage.
- [ ] Replays guarded to avoid duplicate side effects.

---

## Next Steps

- Orchestrate enrichment through Flow events to support multi-stage processing.
- Pair pipelines with [AI Integration Playbook](ai-integration.md) for composite workflows.
- Combine Flow controllers with Web payload transformers to expose curated APIs.
