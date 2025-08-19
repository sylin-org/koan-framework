# Architecture Decision Records

Canonical ADRs live in this folder. They’re grouped by domain below for quick navigation; files keep their historical numeric IDs. New ADRs should prefer PREFIX-####-short-title.md for filenames while remaining in this folder.

## Architecture (ARCH)

- 0001 — Rename generic TAggregate → TEntity — 0001-rename-generic-to-tentity.md
- 0010 — Meta packages (Sora, Sora.App) — 0010-meta-packages.md
- 0011 — Logging (Core) and secure headers (Web) layering — 0011-logging-and-headers-layering.md
- 0013 — Health announcements and readiness — 0013-health-announcements-and-readiness.md
- 0033 — OpenTelemetry integration — 0033-opentelemetry-integration.md
- 0034 — DDD documentation and glossary — 0034-ddd-documentation-and-glossary.md
- 0039 — SoraEnv static runtime — 0039-soraenv-static-runtime.md
- 0040 — Config and constants naming — 0040-config-and-constants-naming.md

## Data (DATA)

- 0002 — QueryCapabilities flag — 0002-query-capabilities-flag.md
- 0003 — WriteCapabilities and bulk markers — 0003-write-capabilities-and-bulk-markers.md
- 0004 — Index anchoring and JSON mapping — 0004-index-anchoring-and-json-mapping.md
- 0005 — Relational schema toolkit — 0005-relational-schema-toolkit.md
- 0006 — Instruction execution API — 0006-instruction-execution-api.md
- 0007 — Relational LINQ-to-SQL helper — 0007-relational-linq-to-sql-helper.md
- 0007 — Transactional batch and concurrency — 0007-transactional-batch-and-concurrency.md
- 0008 — Relational command caching — 0008-relational-command-caching.md
- 0009 — Unify on IEntity — 0009-unify-on-ientity.md
- 0016 — Entity extensions naming and parity — 0016-entity-extensions-naming-and-parity.md
- 0017 — Storage naming conventions — 0017-storage-naming-conventions.md
- 0018 — Centralized naming registry and DX — 0018-centralized-naming-registry-and-dx.md
- 0019 — Outbox helper and defaults — 0019-outbox-helper-and-defaults.md
- 0020 — Outbox provider discovery and priority — 0020-outbox-provider-discovery-and-priority.md
- 0029 — JSON filter language and endpoint — 0029-json-filter-language-and-endpoint.md
- 0030 — Entity sets routing and storage suffixing — 0030-entity-sets-routing-and-storage-suffixing.md
- 0031 — Filter ignore-case option — 0031-filter-ignore-case-option.md
- 0032 — Paging pushdown and in-memory fallback — 0032-paging-pushdown-and-in-memory-fallback.md
- 0044 — Paging guardrails and tracing MUST — 0044-paging-guardrails-and-tracing-must.md
- 0045 — Default projection policy and JSON pushdown — 0045-default-projection-policy-and-json-pushdown.md
- 0046 — SQLite schema governance (DDL policy) — 0046-sqlite-schema-governance-ddl-policy.md
- 0047 — Postgres adapter — 0047-postgres-adapter.md
- 0049 — Direct commands API — 0049-direct-commands-api.md
- 0050 — Instruction name constants and scoping — 0050-instruction-name-constants-and-scoping.md
- 0051 — Direct routing via instruction executors — 0051-direct-routing-via-instruction-executors.md
- 0052 — Relational Dapper boundary; Direct uses ADO.NET — 0052-relational-dapper-boundary-and-direct-ado.md

## Web (WEB)

- 0012 — Web templates and rate limiters — 0012-web-templates-and-rate-limiters.md
- 0035 — EntityController transformers — 0035-entitycontroller-transformers.md
- 0041 — GraphQL module and controller — 0041-graphql-module-and-controller.md
- 0042 — GraphQL naming and discovery — 0042-graphql-naming-and-discovery.md

## Messaging (MESS)

- 0021 — Messaging capabilities and negotiation — 0021-messaging-capabilities-and-negotiation.md
- 0022 — MQ provisioning, aliases, dispatcher — 0022-mq-provisioning-aliases-and-dispatcher.md
- 0023 — Alias defaults, DefaultGroup, OnMessage — 0023-alias-defaults-default-group-and-onmessage.md
- 0024 — Batch semantics and aliasing — 0024-batch-semantics-and-aliasing.md
- 0025 — Inbox contract and client — 0025-inbox-contract-and-client.md
- 0026 — Discovery over MQ policy — 0026-discovery-over-mq-policy.md
- 0027 — Standalone MQ services and naming — 0027-standalone-mq-services-and-naming.md

## Ops (OPS)

- 0014 — Samples port allocation — 0014-samples-port-allocation.md
- 0015 — Default configuration fallback — 0015-default-configuration-fallback.md
- 0043 — Mongo container default host — 0043-mongo-container-default-host.md
- 0048 — Standardize Docker probing for tests — 0048-standardize-docker-probing-for-tests.md

## Developer Experience (DX)

- 0028 — Service project naming and conventions — 0028-service-project-naming-and-conventions.md
- 0036 — Sylin prefix and package IDs — 0036-sylin-prefix-and-package-ids.md
- 0037 — Tiny templates family — 0037-tiny-templates-family.md
- 0038 — Auto-registration — 0038-auto-registration.md

Template: see 0000-template.md
