# AI-0004 - Secrets provider and per-tenant key management

Status: Proposed
Date: 2025-08-19
Owners: Koan Ops

## Context

Central proxy and multi-tenant scenarios need secure key resolution and rotation without code changes.

## Decision

- Abstraction: ISecretsProvider with adapters for Azure Key Vault and AWS Secrets Manager; cache with TTL and auto-refresh hooks.
- Tenancy: resolve keys by tenant/project; never log secrets; boot report only shows redacted status.

## Consequences

- Proxy depends on ISecretsProvider; rotation can be performed out-of-band; tests verify no secret leakage.
