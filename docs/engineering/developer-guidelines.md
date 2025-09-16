# Developer Guidelines

Concise conventions for contributors building within Koan. These complement the Engineering front door and Contributor workflow.

Core defaults (follow unless an ADR says otherwise)
- Controllers only for HTTP routes; no inline endpoints. Prefer transformers for payload shaping.
- Data access via first‑class model statics (All/Query/Page/Stream). Avoid generic facades unless needed.
- Centralize constants and config keys. Use typed Options for tunables; constants for stable literals.
- Keep project roots clean; one public class per file; co‑locate satellites via nested types when it helps clarity.
- No stubs; delete placeholders. Prefer small, finished increments.

Coding patterns
- Async first: observe CancellationToken; avoid fire‑and‑forget; prefer TryXYZ for recoverable flows.
- Paging guardrails: enforce DefaultPageSize/MaxPageSize; prefer server pushdown with bounded fallbacks.
- Pushdown‑first: attempt provider pushdown for filters, counts, and ordering; surface capability flags honestly.
- Options/config: adopt “first‑win” key resolution (Koan:Data:<Adapter>:..., Koan:Data:Sources:Default:<name>:..., ConnectionStrings:...).
- Logging/tracing: structured logs (LoggerMessage) and ActivitySource spans with db.*, messaging.* tags.

Testing
- Unit tests for options and guardrails; integration with Testcontainers where applicable.
- Prefer tiny, fast tests; skip when infra is unavailable but allow opt‑in via env variables.

Docs and ADRs
- Update docs with new behaviors; cite ADRs. For exceptions, add a short decision note under decisions/.

References
- Engineering front door: engineering/index.md
- Contributor workflow: engineering/contributor-workflow.md
- Architecture principles: architecture/principles.md
- Data reference and adapter matrix: reference/data-access.md, reference/adapter-matrix.md
