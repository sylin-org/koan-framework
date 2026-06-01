# Koan.Data.SearchEngine

Shared support for the search-engine vector connectors — `Koan.Data.Connector.ElasticSearch` and
`Koan.Data.Connector.OpenSearch`. Both engines are built on Apache Lucene and speak the same query
DSL, so the metadata-filter translation lives here once instead of being duplicated per adapter. The
package is named for the engine *category* (the same convention as `Koan.Data.Relational`), so the
dependency reads clearly without Lucene-internals knowledge.

- `SearchEngineFilterTranslator` — the single source of truth for translating the unified `Filter` AST
  into the Elasticsearch/OpenSearch query DSL, plus the one `VectorFilterCapabilities` constant both
  adapters expose.

## Capabilities
- Translate the unified `Filter` AST → the search-engine query DSL (`term`/`terms`/`range`/`wildcard`/
  `bool`/`exists`).
- One operator-aware `VectorFilterCapabilities` value shared by both adapters.
- Null-inclusive negation (`Ne`/`Nin`/`HasNone`) via Lucene `bool/must_not`.

## Usage

Each connector calls the shared translator, passing its own engine label so a not-supported error
names the right adapter:

```csharp
var query = SearchEngineFilterTranslator.TranslateWhereClause(
    options.Filter, metadataField, engine: "Elasticsearch");
```

Notes:
- The `engine` label only personalizes not-supported exception messages; the translation is identical.
- The repositories stay separate (their REST/transport wiring differs); only the translation and the
  capability constant are shared.

## References
- Vector pathway + filter model: `~/decisions/DATA-0097-vector-pathway-parity.md`
- Embedding↔vector seam (§10.4, the ES/OS shared base): `~/decisions/AI-0036-embedding-vector-seam.md`
