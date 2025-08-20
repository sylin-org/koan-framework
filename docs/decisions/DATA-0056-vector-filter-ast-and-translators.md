# DATA-0056 — Vector Filter AST and Translators

Status: Accepted

Date: 2025-08-20

Context
- We need a provider-agnostic way to push down filters in vector searches.
- Adapters were hand-parsing ad-hoc JSON; risk of divergence and bugs.

Decision
- Introduce a shared typed AST (VectorFilter) in Sora.Data.Abstractions covering a minimal set: And/Or/Not, Eq/Ne/Gt/Gte/Lt/Lte, Like, Contains.
- Provide helpers: VectorFilterJson (parse/write a small JSON DSL and equality-map shorthand) and VectorFilterExpression (subset of C# expression to AST).
- Each adapter implements a translator from AST to provider-native query (e.g., Weaviate GraphQL where).
- Documentation prefers typed filters; JSON-DSL is marked advanced and for interop.

Consequences
- Consistent behavior across adapters; easier conformance testing.
- Clients can choose typed AST or JSON wire format; both validate to the same semantics.
- Small surface now; new operators require ADR updates and translator changes.
