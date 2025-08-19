---
id: OPS-0048
slug: OPS-0048-standardize-docker-probing-for-tests
domain: OPS
status: Accepted
date: 2025-08-19
title: Standardize Docker environment discovery for integration tests
---

# ADR 0048: Standardize Docker environment discovery for integration tests

## Context

Several integration tests rely on Docker via Testcontainers. On Windows, some environments produced stream hijack errors when Ryuk is enabled or when the Docker endpoint was implicitly mis-detected (named pipe vs TCP). We also want consistent behavior in CI and local dev, including honoring DOCKER_HOST when provided.

## Decision

- Introduce a shared testing utility `Sora.Testing.DockerEnvironment` to probe Docker availability and produce a stable endpoint string.
- Probe priority:
  1) Use `DOCKER_HOST` if set and reachable.
  2) Platform default (Windows: `npipe://./pipe/docker_engine`; Linux/macOS: `unix:///var/run/docker.sock`).
  3) Fallback: `http://localhost:2375`.
- Use `WithDockerEndpoint(probe.Endpoint)` for all Testcontainers builder usages.
- Set `TESTCONTAINERS_RYUK_DISABLED=true` by default in tests to avoid Windows hijack issues.
- If Docker is not reachable and no env-provided connection string is present, affected tests no-op (or can be upgraded to true Skip later).

## Consequences

- More reliable startup of containers across platforms.
- Reduced flakiness on Windows due to Ryuk stream hijack.
- Clearer developer guidance via docs (Postgres adapter testing section updated).

## Alternatives considered

- Keep per-test ad hoc detection/code: rejected due to duplication and inconsistency.
- Require Docker only (no env override): rejected; local/CI may provide Postgres/RabbitMQ endpoints.

## Follow-ups

- Optionally introduce a skippable test attribute to mark tests as Skipped when Docker isn’t available.
- Extend docs for other adapters/services using Testcontainers to reference this standard.
