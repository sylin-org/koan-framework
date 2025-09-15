# Koan.Messaging & Koan.Flow Greenfield Refactor Plan

## Purpose
This document details the break-and-build refactoring plan for Koan.Messaging and Koan.Flow, aligning with ADRs MESS-0071 and MESS-0070. The goal is a pure greenfield implementation: all legacy code is deleted, and only the new, provider-agnostic, fluent, and testable messaging foundation remains.

---

## Principles
- **No legacy code or shims retained.**
- **Provider-agnostic, fluent, and intent-driven APIs.**
- **First-class support for topology provisioning and in-memory/mock testing.**
- **Documentation and samples reflect only the new patterns.**

---

## Refactor Steps

### 1. Remove All Old Code
- Delete all provider-specific topology logic, legacy APIs, and direct provider dependencies in Koan.Messaging.* and Koan.Flow.*.
- Remove all legacy message bus interfaces, registration patterns, and configuration paths.
- Purge all obsolete code, shims, and `[Obsolete]` attributes.

### 2. Implement New Core APIs
- Define and implement `ITopologyProvisioner` and supporting types in Koan.Messaging.Core.
- Expose only the new, provider-agnostic API for topology and messaging.

### 3. Provider Rebuild
- Rebuild each provider to implement only the new topology and messaging interfaces.
- No legacy extension points or configuration paths retained.

### 4. Fluent Messaging API
- Implement a fluent, intent-driven messaging surface (e.g., `.Publish<T>()`, `.Subscribe<T>()`, `.OnCommand<T>()`).
- No legacy registration or handler APIs remain.

### 5. In-Memory/Mock Bus
- Implement a new in-memory/mock bus with full API parity for testing.

### 6. Consumer Rebuild (Koan.Flow, etc.)
- Refactor Koan.Flow and all other consumers to use only the new APIs.
- Remove all direct provider dependencies and legacy code.

### 7. Documentation & Samples
- Rewrite all samples and documentation for the new APIs and patterns.
- Remove all legacy documentation.

### 8. Validation & Final Cleanup
- Run strict docs build and ensure all tests pass using only the new APIs.
- Confirm: no legacy code, shims, or references remain in the codebase.

---

## Deliverables
- Pure, provider-agnostic messaging and topology APIs.
- Fluent, discoverable, and testable developer experience.
- Clean, up-to-date documentation and samples.
- No legacy or obsolete code present.

---

**References:**
- MESS-0071: Messaging DX and Topology Provisioning
- MESS-0070: Messaging Topology System Primitives & Zero-Config
