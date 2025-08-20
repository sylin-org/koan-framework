# Architecture Decision Records

Canonical ADRs live in this folder. They’re grouped by domain below for quick navigation; files keep their historical numeric IDs. New ADRs should prefer PREFIX-####-short-title.md for filenames while remaining in this folder.

## Architecture (ARCH)

- 0001 — Rename generic TAggregate → TEntity — ARCH-0001-rename-generic-to-tentity.md
- 0010 — Meta packages (Sora, Sora.App) — ARCH-0010-meta-packages.md
- 0011 — Logging (Core) and secure headers (Web) layering — ARCH-0011-logging-and-headers-layering.md
- 0013 — Health announcements and readiness — ARCH-0013-health-announcements-and-readiness.md
- 0033 — OpenTelemetry integration — ARCH-0033-opentelemetry-integration.md
- 0034 — DDD documentation and glossary — ARCH-0034-ddd-documentation-and-glossary.md
- 0039 — SoraEnv static runtime — ARCH-0039-soraenv-static-runtime.md
- 0040 — Config and constants naming — ARCH-0040-config-and-constants-naming.md

## Data (DATA)

- 0002 — QueryCapabilities flag — DATA-0002-query-capabilities-flag.md
- 0003 — WriteCapabilities and bulk markers — DATA-0003-write-capabilities-and-bulk-markers.md
- 0004 — Index anchoring and JSON mapping — DATA-0004-index-anchoring-and-json-mapping.md
- 0005 — Relational schema toolkit — DATA-0005-relational-schema-toolkit.md
- 0006 — Instruction execution API — DATA-0006-instruction-execution-api.md
- 0007 — Relational LINQ-to-SQL helper — DATA-0007-relational-linq-to-sql-helper.md
- 0007 — Transactional batch and concurrency — DATA-0007-transactional-batch-and-concurrency.md
- 0008 — Relational command caching — DATA-0008-relational-command-caching.md
- 0009 — Unify on IEntity — DATA-0009-unify-on-ientity.md
- 0016 — Entity extensions naming and parity — DATA-0016-entity-extensions-naming-and-parity.md
- 0017 — Storage naming conventions — DATA-0017-storage-naming-conventions.md
- 0018 — Centralized naming registry and DX — DATA-0018-centralized-naming-registry-and-dx.md
- 0019 — Outbox helper and defaults — DATA-0019-outbox-helper-and-defaults.md
- 0020 — Outbox provider discovery and priority — DATA-0020-outbox-provider-discovery-and-priority.md
- 0029 — JSON filter language and endpoint — DATA-0029-json-filter-language-and-endpoint.md
- 0030 — Entity sets routing and storage suffixing — DATA-0030-entity-sets-routing-and-storage-suffixing.md
- 0031 — Filter ignore-case option — DATA-0031-filter-ignore-case-option.md
- 0032 — Paging pushdown and in-memory fallback — DATA-0032-paging-pushdown-and-in-memory-fallback.md
- 0044 — Paging guardrails and tracing MUST — DATA-0044-paging-guardrails-and-tracing-must.md
- 0045 — Default projection policy and JSON pushdown — DATA-0045-default-projection-policy-and-json-pushdown.md
- 0046 — SQLite schema governance (DDL policy) — DATA-0046-sqlite-schema-governance-ddl-policy.md
- 0047 — Postgres adapter — DATA-0047-postgres-adapter.md
- 0049 — Direct commands API — DATA-0049-direct-commands-api.md
- 0050 — Instruction name constants and scoping — DATA-0050-instruction-name-constants-and-scoping.md
- 0051 — Direct routing via instruction executors — DATA-0051-direct-routing-via-instruction-executors.md
- 0052 — Relational Dapper boundary; Direct uses ADO.NET — DATA-0052-relational-dapper-boundary-and-direct-ado.md
- 0054 — Vector search capability and contracts — DATA-0054-vector-search-capability-and-contracts.md

## Web (WEB)

- 0012 — Web templates and rate limiters — WEB-0012-web-templates-and-rate-limiters.md
- 0035 — EntityController transformers — WEB-0035-entitycontroller-transformers.md
- 0041 — GraphQL module and controller — WEB-0041-graphql-module-and-controller.md
- 0042 — GraphQL naming and discovery — WEB-0042-graphql-naming-and-discovery.md

## Messaging (MESS)

- 0021 — Messaging capabilities and negotiation — MESS-0021-messaging-capabilities-and-negotiation.md
- 0022 — MQ provisioning, aliases, dispatcher — MESS-0022-mq-provisioning-aliases-and-dispatcher.md
- 0023 — Alias defaults, DefaultGroup, OnMessage — MESS-0023-alias-defaults-default-group-and-onmessage.md
- 0024 — Batch semantics and aliasing — MESS-0024-batch-semantics-and-aliasing.md
- 0025 — Inbox contract and client — MESS-0025-inbox-contract-and-client.md
- 0026 — Discovery over MQ policy — MESS-0026-discovery-over-mq-policy.md
- 0027 — Standalone MQ services and naming — MESS-0027-standalone-mq-services-and-naming.md

## Ops (OPS)

- 0014 — Samples port allocation — OPS-0014-samples-port-allocation.md
- 0015 — Default configuration fallback — OPS-0015-default-configuration-fallback.md
- 0043 — Mongo container default host — OPS-0043-mongo-container-default-host.md
- 0048 — Standardize Docker probing for tests — OPS-0048-standardize-docker-probing-for-tests.md

## Developer Experience (DX)

- 0028 — Service project naming and conventions — DX-0028-service-project-naming-and-conventions.md
- 0036 — Sylin prefix and package IDs — DX-0036-sylin-prefix-and-package-ids.md
- 0037 — Tiny templates family — DX-0037-tiny-templates-family.md
- 0038 — Auto-registration — DX-0038-auto-registration.md

Template: see 0000-template.md

## AI (AI)

- 0001 — Native AI baseline — AI-0001-ai-baseline.md
- 0002 — AI API contracts and SSE format — AI-0002-api-contracts-and-sse.md
- 0003 — Tokenization and cost strategy — AI-0003-tokenization-and-cost.md
- 0004 — Secrets provider and per-tenant key management — AI-0004-secrets-provider.md
- 0005 — Protocol surfaces (gRPC, OpenAI shim, MCP, AI-RPC) — AI-0005-protocol-surfaces.md
- 0006 — Data formats and grounding (Parquet, JSON-LD, Schema.org) — AI-0006-data-formats-and-grounding.md
- 0007 — Inference servers interop (KServe, vLLM, TGI) — AI-0007-inference-servers-interop.md
- 0010 — Prompt entrypoint and augmentation pipeline — AI-0010-entrypoint-and-augmentations.md
- 0008 — AI adapters and registry — AI-0008-adapters-and-registry.md
- 0009 — Multi-service routing, load balancing, and policies — AI-0009-multi-service-routing-and-policies.md
