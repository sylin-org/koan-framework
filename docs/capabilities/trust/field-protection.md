---
type: REFERENCE
domain: security
title: "Field-at-rest protection"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/trust/field-protection.md
---

# Field-at-rest protection

Mark supported Entity strings as sensitive and keep ordinary `Save` and materialization calls while
Koan encrypts the provider-bound copy.

## You need

| Piece | Package | Note |
|---|---|---|
| Field classification and transforms | `Sylin.Koan.Classification` | supports writable string properties |
| Local key custody | supplied automatically for Development | persists beside the application for restart continuity |
| Production key custody | application implementation | register the trusted key provider before `AddKoan()` |

## The constraint box

> **The constraint:** Local key custody is not production custody. A real deployment must retain
> durable keys across rotation; raw adapter or repository calls bypass the Data transforms, existing
> plaintext is not backfilled automatically, and field-at-rest encryption does not imply masking,
> searchable ciphertext, log redaction, or a complete compliance system.

## Choose the declaration by meaning

| Declaration | Meaning | Storage behavior today |
|---|---|---|
| `[Pii]` | personally identifiable information | authenticated AES-256-GCM envelope |
| `[Phi]` | protected health information | the same envelope behavior |
| `[Secret]` | application secret material | the same envelope behavior |
| `[Classified("category")]` | application-named sensitive category | the same envelope behavior |

## Leaves

- **Decision guide and receipt:** [harden for production](../../recipes/harden-for-production.md)
- **Package contract:** working Entity declaration, custody setup, and boundaries:
  [Classification README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Classification/README.md)

Tenancy automatically scopes key derivation when both capabilities are active; it does not remove
the need for durable production custody.
