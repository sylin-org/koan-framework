---
uid: reference.modules.Koan.data.searchengine
title: Koan.Data.SearchEngine - Technical Reference
description: Shared Filter → query-DSL translation for the Elasticsearch/OpenSearch (Lucene-family) vector connectors.
packages: [Sylin.Koan.Data.SearchEngine]
source: src/Koan.Data.SearchEngine/
---

## Contract

- **Input:** the unified `Filter` AST (`Koan.Data.Abstractions.Filtering`), the configured metadata
  field, and an `engine` label.
- **Output:** a Newtonsoft `JObject` query-DSL clause, or `null` for a null filter.
- **Error modes:** `NotSupportedException` for an operator or node the engine cannot render — the
  message names the engine (e.g. "Elasticsearch") so a failure points at the actual adapter.

## Usage guidance

- Used by the Elasticsearch and OpenSearch connectors; not intended for direct use in apps.
- `SearchEngineFilterTranslator.Caps` is the one `VectorFilterCapabilities` both adapters expose in
  their `FilterCapabilities`.
- Only the translation and the capability constant are shared; each connector keeps its own repository
  (REST/transport wiring differs between the two engines).

## Supported operators

`Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `StartsWith`, `EndsWith`, `Contains`, `Has`,
`HasAny`, `HasAll`, `HasNone`, `Exists`. Nested metadata paths are supported; case-insensitive
matching is not (declared via `VectorFilterCapabilities`).

## Field-targeting rules

- String exact-match (`term`/`terms`) and wildcard target the dynamic `.keyword` sub-field — Lucene
  maps strings as analyzed text plus a `.keyword` sub-field, and `term`/`wildcard` only match the
  keyword form.
- Numeric range and `exists` target the bare field.
- `Ne`/`Nin`/`HasNone` use Lucene's null-inclusive `bool/must_not`, so rows missing the key are
  included — matching the `DictionaryFilterEvaluator` convergence oracle.

## References

- DATA-0097 Vector Pathway Parity: `~/decisions/DATA-0097-vector-pathway-parity.md`
- AI-0036 §9.4 — the ES/OS shared base: `~/decisions/AI-0036-embedding-vector-seam.md`
