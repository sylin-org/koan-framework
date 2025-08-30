# Adapter Design and Developer Experience (DX)

Goal
- Thin adapters with minimal code; common behavior in shared libraries; great developer ergonomics (DX) with simple scaffolding and configuration.

Baseline
- .NET 9, Sora framework for service scaffolding
- RabbitMQ for data/control planes; REST IntakeGateway optional
- Mongo-backed PolicyBundle, OTEL instrumentation, workflow-lite orchestrator in the platform

## Design principles for thin adapters
- Single responsibility: acquire data from a source and emit IntakeRecord
- No business logic: parsing/healing/keys/invariants belong to the platform
- Minimal state: cursors handled by platform control plane; adapter caches are optional
- Contract-first: validate IntakeRecord schema before emit
- Observability built-in: OTEL traces and metrics; health endpoints by default

## Shared libraries (platform‑provided)
- Adapter SDK (NuGet):
  - Emission client: REST (IntakeGateway) and AMQP producer (intake exchange)
  - Control plane client: subscribe to Seed/Pull/Suspend/Resume/Throttle; publish Heartbeat/Announcement/SeedProgress
  - Cursor helper: fetch/update SourceCursor via platform API
  - Rate limiter and backoff policies (respect Throttle)
  - Schema validator for IntakeRecord
  - OTEL bootstrap and correlation headers propagation
  - Health/metrics middleware (/healthz, /readyz, /metrics)
- Templates:
  - sora-adapter-template: project skeleton with Program.cs, config, DI, handlers, and tests

## Minimal adapter code surface
- SourceClient: a single class implementing IDataSource with methods:
  - Task<IEnumerable<SourceItem>> PullAsync(Window window, int pageSize, CancellationToken ct)
  - IAsyncEnumerable<SourceItem> SeedAsync(SeedArgs args, CancellationToken ct)
- Mapper: SourceItem → IntakeRecord(payload + metadata)
- Handlers:
  - OnSeedCommand → SeedAsync → emit records → SeedProgress updates
  - OnPullWindowCommand → PullAsync → emit records
  - OnSuspend/Resume/Throttle → SDK handles defaults; override if needed

## Configuration conventions
- ADAPTER_ID, SOURCE_ID, SOURCE_API_URL, AUTH_* (token/keys), PAGE_SIZE_DEFAULT
- EMIT_MODE=rest|mq; INTAKE_GATEWAY_URL, RABBITMQ_URL
- OTEL_* envs; LOG_LEVEL

## Control plane alignment
- Startup self‑announcement with capabilities (supportsSeed, supportsIncremental, windowing modes, pageMax, rateLimit)
- Periodic heartbeat (status, metrics)
- Respect Throttle and Suspend commands; report progress via SeedProgress

## DX workflow
- dotnet new sora-adapter -n <Name>
- Implement SourceClient and a simple Mapper
- Configure env vars; run docker-compose local-full; adapter connects and announces
- Post a SeedCommand via platform API; observe records flow and progress in portal/Jaeger
- Write a couple mapping tests; verify IntakeRecord schema validation

## Pros/Cons and mitigations
Pros
- Rapid adapter delivery; consistent behavior via SDK
- Centralized control, policy, and governance
- Lower maintenance: platform owns retries, throttling, and instrumentation
Cons
- SDK becomes a dependency; must remain backward compatible → semantic versioning and deprecation policy
- Over‑abstraction risk → keep code surface small and extensible hooks

## Roadmap for the SDK
- v1: emission, control plane, schema validation, cursors, OTEL, health
- v1.1: generator for Mapper from JSON samples (assistive, not codegen-enforced)
- v1.2: source API connector helpers (REST paging, GraphQL cursors, file/dropbox polling)

By concentrating capabilities in a shared Adapter SDK and template, adapter authors write only source‑specific fetch logic and mapping, while the platform handles orchestration, governance, and reliability.
