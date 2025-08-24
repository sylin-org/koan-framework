Sora.AI.Contracts — Technical reference

Contract
- Inputs: Prompt/Message objects, Tool specifications, Embedding requests.
- Outputs: Model responses (text/JSON), Streaming tokens, Embedding vectors + metadata, Error/result envelopes.
- Options: Provider name/alias, Model id, Temperature/topP, MaxTokens, Timeouts, Retries, Telemetry correlation.
- Error modes: ProviderUnavailable, RateLimited, InvalidRequest (schema/size), Timeout, Canceled.

Scope and responsibilities
- Defines stable interfaces, records, and enums used by AI providers and application code.
- No network calls; no provider logic. Backwards-compatible by default; version only when required.
- Favor small, intent-revealing contracts with sealed records and explicit required members.

Design notes
- Separation of concern: providers implement these contracts; web/controllers orchestrate usage.
- Streaming first: expose both buffered and streaming shapes where applicable.
- Tool-call safety: explicit inputs/outputs, avoid magic strings; centralize literals in Constants.

Options and configuration
- Provider selection via option keys; callers should pass explicit provider and model identifiers.
- Tunables (temperature, topP, penalties) must have safe defaults; set via typed options not scattered literals (ARCH-0040).
- Timeouts and retry policies must be caller-controlled; contracts should carry correlation ids.

Edge cases
- Empty/oversized prompts; token budget exceeded; unsupported model capabilities.
- Provider transient failures; streaming aborted mid-flight; partial tool output.
- Concurrency: parallel tool calls; cancellation tokens honored throughout.

Security
- Treat prompts and outputs as sensitive. No PII logging by default; redact in diagnostics.
- Disallow arbitrary tool execution; require allow-lists and typed parameters.

References
- ./README.md
- /docs/architecture/principles.md
- /docs/engineering/index.md