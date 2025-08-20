---
id: OPS-0043
slug: OPS-0043-mongo-container-default-host
domain: OPS
status: Accepted
date: 2025-08-19
title: Mongo default host when containerized
---
 
# 0043 – Mongo default host when containerized

## Context
The S4 sample uses the MongoDB adapter. In container-based setups (Docker Compose), the Mongo service is commonly reachable at the hostname `mongodb`. When no explicit Mongo connection string is configured, discovery previously defaulted to `mongodb://localhost:27017`, which fails inside containers or cross-container calls.

## Decision
When the process is containerized and no explicit Mongo connection string is provided, default to the Docker Compose host `mongodb`.

Detection: the environment variable `DOTNET_RUNNING_IN_CONTAINER=true` (or the same key via configuration) indicates a containerized execution context.

Resolution precedence:
1) `Sora:Data:Mongo` or `Sora:Data:Sources:Default:mongo` (options binding)
2) `ConnectionStrings:Default`
3) If still unset:
   - Containerized: `mongodb://mongodb:27017`
   - Non-container: `mongodb://localhost:27017`

The final value is normalized to ensure a `mongodb://` scheme is present when needed.

## Consequences
– Container-first DX: Sora samples work out-of-the-box with a standard `mongodb` service name in Compose.
– Local-first DX preserved: non-container runs still default to `localhost`.
– Behavior is transparent and shown in the bootstrap report (connection string redacted).

## Notes
Explicit configuration always wins. Setting any of the above keys overrides the default.
