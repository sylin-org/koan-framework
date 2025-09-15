# Docker/Testcontainers skip guards for tests

Short contract
- Input: None at runtime; tests probe the local Docker daemon via `Koan.Testing.DockerEnvironment`.
- Output: Tests execute when Docker is reachable; otherwise they are skipped deterministically with a clear reason.
- Error modes: No hard failures due to missing Docker; Testcontainers `Ryuk` is disabled to reduce friction.
- Success criteria: CI/dev without Docker stays green; Docker-enabled agents run full coverage.

Implementation
- Reusable probe lives in `tests/Koan.Testing/DockerEnvironment.cs` (Docker.DotNet ping + optional CLI fallback).
- Tests use `[SkippableFact]` and `Skip.IfNot(...)` to skip when the probe reports unavailable.
- For shared brokers (RabbitMQ), a collection fixture (`RabbitMqSharedContainer`) caches availability and exposes `Available`.

Applied changes
- RabbitMQ integration tests now use `[SkippableFact]` and `Skip.IfNot(_rmq.Available, ...)`.
- Mongo container smoke test probes Docker before starting the container and is marked `[SkippableFact]`.
- Test projects reference `Xunit.SkippableFact` and, where needed, `tests/Koan.Testing` for the probe.

Notes and edge cases
- If Docker is behind a non-default endpoint, set `DOCKER_HOST` (honored by the probe).
- `TESTCONTAINERS_RYUK_DISABLED` is set for reduced friction; enable Ryuk in CI if you need strict cleanup.
- Discovery E2E/caching tests keep their own guarded setup but follow the same pattern.

Related
- Engineering front door: `engineering/index.md`.
- Messaging/RabbitMQ tests: `tests/Koan.Mq.RabbitMq.IntegrationTests/**`.
- Probe utility: `tests/Koan.Testing/DockerEnvironment.cs`.