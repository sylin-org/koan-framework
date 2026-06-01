# DATA-0056 - Vector Filter AST and Translators

Status: Accepted (supersession pending — see DATA-0096)

Date: 2025-08-20

> **Convergence note ([DATA-0096](DATA-0096-unified-filter-pipeline.md)).** The unified `Filter`
> AST was harvested from this `VectorFilter` design — they share node shape and operator vocabulary.
> Collapsing the two (repoint the 5 provider translators onto the unified `Filter` nodes, retire
> `VectorFilter*`) is intended but **deferred**: vector metadata filtering is *schemaless* (arbitrary
> fields under a metadata blob, no CLR-type binding/coercion), genuinely distinct from entity
> filtering, and the provider translators are untested with no live vector store to verify against.
> The collapse is a scoped follow-up that must land a translator conformance suite first. Until then
> this ADR remains authoritative for the vector filter path.

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
