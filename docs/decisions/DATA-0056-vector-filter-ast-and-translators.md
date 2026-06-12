# DATA-0056 - Vector Filter AST and Translators

Status: Superseded by [DATA-0096](DATA-0096-unified-filter-pipeline.md) (unified Filter AST) and [DATA-0097](DATA-0097-vector-pathway-parity.md) (vector pathway parity)

Date: 2025-08-20

> **Superseded.** The unified `Filter` AST was harvested from this `VectorFilter` design — they share
> node shape and operator vocabulary — and the vector path has since collapsed onto it: every provider
> translator is repointed to the unified `Filter` nodes, `VectorFilter*` and `VectorFilterOperator`
> are retired, and each adapter is verified against a container-free translator conformance suite and
> live containers (DATA-0097). This document is retained for historical context; the unified pipeline
> ([DATA-0096](DATA-0096-unified-filter-pipeline.md)) and the vector pathway
> ([DATA-0097](DATA-0097-vector-pathway-parity.md)) are authoritative for the vector filter path.

Context

- We need a provider-agnostic way to push down filters in vector searches.
- Adapters were hand-parsing ad-hoc JSON; risk of divergence and bugs.

Decision

- Introduce a shared typed AST (VectorFilter) in Koan.Data.Abstractions covering a minimal set: And/Or/Not, Eq/Ne/Gt/Gte/Lt/Lte, Like, Contains.
- Provide helpers: VectorFilterJson (parse/write a small JSON DSL and equality-map shorthand) and VectorFilterExpression (subset of C# expression to AST).
- Each adapter implements a translator from AST to provider-native query (e.g., Weaviate GraphQL where).
- Documentation prefers typed filters; JSON-DSL is marked advanced and for interop.

Consequences

- Consistent behavior across adapters; easier conformance testing.
- Clients can choose typed AST or JSON wire format; both validate to the same semantics.
- Small surface now; new operators require ADR updates and translator changes.
