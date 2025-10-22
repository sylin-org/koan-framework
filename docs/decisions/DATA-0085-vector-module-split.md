# DATA-0085: Isolate Vector Workflows into Koan.Data.Vector

**Status**: Proposed  
**Date**: 2025-10-20  
**Owner**: Platform/Data  

## Context

Vector workflow orchestration is currently bolted onto `Koan.Data.Core`. To enable optional adoption and avoid hot-path reflection, we need a standalone vector module analogous to `Koan.Data.Relational`. The repository already contains vector connectors (Weaviate, Pinecone, etc.) and a Weaviate test suite skeleton, but none of the core logic lives in a dedicated project. We also lack automated tests against a real vector store.

## Decision

Create a first-class `Koan.Data.Vector` project that owns all vector-specific abstractions, workflows, and entity extensions. Push vector awareness entirely out of `Koan.Data.Core`; the core package exposes no vector hooks, and vector entry points live behind optional extensions in the new module. Treat the module as optional—no code executes unless the host adds a vector connector (e.g., Weaviate). Build an end-to-end QA suite that runs against a live Weaviate container, using our existing TestPipeline container patterns.

## Consequences

- Vector functionality becomes an explicit dependency: `services.AddKoanDataVector()` wires profiles/workflows, and connectors such as Weaviate continue to load via existing auto-registrars.
- `Koan.Data.Core` no longer carries vector-specific helpers; save/query APIs delegate via a bridge only when the vector module is present. This eliminates per-call reflection and keeps the core package agnostic.
- Solution layout mirrors other optional data stacks (relational, cache). Vector-specific samples and docs move under the new module.
- Integration coverage leverages the Weaviate adapter running in a container, validating workflows, profile registration, and entity extension semantics end to end.
- Breaking change: consumers must reference the new project/package to keep vector workflows. As this is a greenfield framework, the impact is acceptable.
