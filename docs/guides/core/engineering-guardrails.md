# Engineering Guardrails

Purpose: Keep Koan simple to read, easy to reason about, and pleasant to maintain. Favor SoC, KISS, YAGNI, DRY. The “magic” should be in the process flow (composability), not in clever code tricks.

## Architectural constraints
- No hidden magic by default
  - Explicit DI registration (AddXyz/UseXyz). Discovery is opt-in and Dev-only.
  - No service provider rebuilds. No runtime type-emission or deep reflection in hot paths.
- Separation of concerns
  - Small modules, small files, small public APIs. One responsibility per type.
  - Options types are small records with validation; bind once at startup.
- YAGNI first
  - Only implement what samples (S0–S7) need in each milestone. Defer features (e.g., versioning snapshots).
- Deterministic behavior
  - Explicit config wins over discovery. Fail fast on explicit misconfig; never silently override.

## Reliability and graceful degradation
- Start even when non-critical modules fail: cache, optional storage, outbound webhooks, messaging consumers may fail without preventing process start.
- Communicate immediately and loudly:
  - Startup logs at Warning/Error with clear remediation.
  - Health endpoints report Degraded with detailed reasons and affected modules.
  - Metrics counters (e.g., module.failures.startup, .retries) incremented.
  - Optional startup banner in console for local dev.
- Critical vs non-critical
  - Critical: primary data store for the app, HTTP listener, required auth mode (when configured), essential secrets provider.
  - Non-critical: cache, optional blob storage, background workers, outbound webhook sender, secondary buses.
  - Modules accept a Critical flag in options; defaults chosen conservatively (data adapters critical by default, cache not).
- Readiness/liveness
  - Liveness: stays Healthy to avoid restarts on transient non-critical failures.
  - Readiness: Degraded when non-critical failures occur; Unhealthy when a critical dependency is down (configurable policy).
  - Kubernetes: readiness failing prevents routing traffic but process keeps running to allow self-heal.
 - Self-healing data stores
  - On start, the platform attempts to ensure schemas exist for known entities when providers support it (e.g., SQLite). This is best-effort and no-ops for providers that do not implement schema ensure.

## Coding conventions
- C# latest, .NET 9, nullable enabled. Analyzer warnings as errors for new code.
- Naming: clear, descriptive, short. Prefer verbs for commands, nouns for data.
- Methods: prefer < 50 logical lines; extract helpers early. One exit path when reasonable.
- Async everywhere with CancellationToken first-class on I/O.
- No static global state. Prefer DI-singleton services when needed.
- Logging: structured logs (event id, key fields). No secrets or PII in logs.

## Composition patterns
- DI-first extension methods: 
  - services.AddData(...), services.AddMessaging(...), app.UseXyz(...)
  - Profiles (Lite/Standard/Extended/Distributed) are sugar and idempotent.
- Options + validation
  - ValidateOnStart; throw with actionable messages.
- Pipelines
  - Repository behaviors ordered by priority + constraints (Before/After). Keep behavior units tiny.

## Data and adapters
- Mid-abstractions only: Relational, Document, Vector (v1). Redis included under Document.
- Lean defaults: Dapper/ADO.NET for relational; EF optional and relational-only.
- Batch operations: IBatchSet Add/Update/Delete/Clear + SaveAsync. Atomic where supported; aggregate errors otherwise.
- Avoid leaky abstractions: use native SDK idioms.

## Messaging
- IBus with first-class IMessageBatch. Emulate batching if provider lacks it, keeping semantics predictable.
- Idempotency and retry/backoff are explicit options, not implicit.

## Web & Security
- Minimal API mappers; ProblemDetails for errors.
- Security via configuration: allow perimeter-auth mode; do not hard-code prod auth on.
- Dev-only conveniences gated behind env checks and feature flags.

Layering defaults (see ADR-0011)
- Core sets sane default logging (SimpleConsole and category filters); apps override via configuration.
- Koan.Web applies minimal secure response headers; CSP is opt-in via KoanWebOptions.
- Apps own policies like ProblemDetails and rate limiting.

## AI
- One-liner enablement: AddAiDefaults + MapAgentEndpoints.
- Local provider detection first; fall back to OpenAI/Azure with keys.
- Safety filters on in Dev by default; explicit opt-in for Prod.

## Testing & docs
- Each milestone ends with a runnable sample. CI runs unit + minimal integration (Testcontainers/Compose health).
- Tests prioritize clarity over cleverness. Keep setup small; prefer builders over mocking frameworks.
- Public APIs have concise XML docs or markdown snippets where helpful.

## Auto-registration (reference = intent)

Standard
- Intent to use a Koan module is expressed by adding a reference. Every package must self-register when referenced.
- Each assembly exposes a single registrar at `/Initialization/KoanAutoRegistrar.cs` implementing `Koan.Core.IKoanAutoRegistrar`.
- Registrar contract:
  - Initialize(IServiceCollection): wire services and options. Keep idempotent; avoid provider rebuilds.
  - Describe(BootReport, IConfiguration, IHostEnvironment): add a module header and a few key settings to the startup report.

Describe expectations
- Include stable, high-signal settings (flags, route prefixes, provider names). Avoid volatile values.
- Redact secrets by marking values with `isSecret: true`; the runtime will de-identify values.
- Keep output short: tokens like key=value; and optional brief notes.

Hygiene
- Do not keep placeholder initializers. Remove empty/non-functional files.
- If an assembly already contains internal `IKoanInitializer` helpers for discovery, ensure the registrar doesn’t duplicate the same work.

## PR checklist (short)
- Readability: can a new contributor follow the flow without prior context?
- Simplicity: is there a less clever, more obvious way?
- Scope: does it serve the current milestone/sample?
- Observability: logs/metrics for important operations.
- Safety: cancellation, error handling, and no secrets/PII in logs.

## Service lifetimes & scope (minimize static and scoped)

Policy
- Avoid static mutable state entirely. Static is allowed for pure helpers, constants, and precomputed readonly tables only.
- Prefer Singleton for thread-safe, stateless services and client factories. This reduces allocations and avoids per-request scopes.
- Use Transient for small, stateless components (mappers, validators, formatters) that are cheap to create and may carry ephemeral method-level state.
- Scoped is a last resort: use only when a true request/transaction boundary is required (e.g., EF DbContext in the EF optional adapter). For Dapper/SDK-first adapters, avoid scoped services.

Context over scope
- Pass explicit context objects (RepoOperationContext, MessageContext, ToolContext) as method parameters rather than relying on scoped services or ambient state.
- Do not depend on IHttpContextAccessor in deep layers; accept needed values (user, correlation id) via parameters.

Module-specific guidelines
- Data
  - IDbConnectionFactory: Singleton; creates/opens short-lived connections per operation (using blocks). No scoped connections.
  - MongoDB: Singleton MongoClient; get DB/collection on demand. Repositories are Singleton if stateless.
  - Redis: Singleton multiplexer; repositories/caches are Singleton.
  - EF (optional): Scoped DbContext as required by EF; keep mapping logic thin to minimize scoped usage.
  - Batch builders (IBatchSet): Transient per batch.
- Messaging
  - Bus client: Singleton. Producer/consumer channels managed internally; consumers run as background singletons.
  - Handlers: Transient per message. IMessageBatch: Transient per batch.
- Web/Webhooks
  - Minimal API mappers: static routing methods calling into Singleton services; avoid per-request service construction.
  - Webhook sender/verification services: Singleton; per-delivery state is method-local.
- AI
  - LLM/Vector clients: Singleton; runtime orchestrator: Singleton; tools: Singleton if stateless.

Options & configuration
- Bind options once at startup with validation; inject as IOptions<T> (Singleton semantics) or IOptionsMonitor<T> when hot-reload is required.
- Avoid IOptionsSnapshot (Scoped) unless absolutely necessary.

### Naming: configuration helper and constants
- Use `Koan.Core.Configuration.Read[...]` and `ReadFirst[...]` for config access. Avoid ad-hoc `cfg["..."]` and direct `Environment.GetEnvironmentVariable` reads.
- Keep constant keys in canonical `:` form; the helper translates env/provider shapes internally.
- Name the per-assembly constants class `Constants` and rely on namespaces for clarity (e.g., `Koan.Web.Swagger.Infrastructure.Constants`). Use using-aliases when multiple `Constants` are required in the same file.
 - When an `IConfiguration cfg` is in scope, prefer the extension methods `cfg.Read(...)` and `cfg.ReadFirst(...)` for brevity; use `Koan.Core.Configuration.Read(...)` when no `cfg` is readily available.

Heuristics (choose lifetime)
1) Does it hold mutable state across calls? If yes, can it be internal and thread-safe? → Singleton; otherwise Transient.
2) Does it need request-specific ambient data? Prefer parameter passing; only use Scoped if unavoidable.
3) Does it wrap a pooled client (HTTP, DB, Redis, Mongo)? → Singleton factory/client.
4) Is construction expensive? Prefer Singleton; ensure thread-safety.
5) Is disposal required per operation? Keep service Singleton, return disposable per-call handles.
