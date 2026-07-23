# Sylin.Koan.Data.SearchEngine

Shared runtime mechanics for Koan's Elasticsearch and OpenSearch vector connectors. Application
developers reference one of those connector packages; it brings this implementation dependency
transitively. This package is not a third provider and requires no direct application registration.

- Target framework: net10.0
- License: Apache-2.0

## Install

Do not install this package directly in an application. Choose the provider that owns your data:

```powershell
dotnet add package Sylin.Koan.Data.Connector.ElasticSearch
# or
dotnet add package Sylin.Koan.Data.Connector.OpenSearch
```

Provider authors extending the shared mechanism can reference it directly:

```powershell
dotnet add package Sylin.Koan.Data.SearchEngine
```

The generated [product surface](../../docs/reference/product-surface.md) owns support maturity.
Applications install one of the provider packages above rather than this mechanism directly.

## What it adds

- one REST repository for index ensure, upsert, bulk operations, deletion, count, scroll export, and kNN search;
- one filter translator for the unified Koan `Filter` AST and Lucene query DSL;
- one configuration, discovery, authentication, health-participation, naming, and startup-reporting path;
- a three-member dialect seam for the Elasticsearch and OpenSearch request shapes that genuinely differ.

Provider packages keep only their identity, orchestration metadata, discovery vocabulary, and native dialect.
Applications continue to use the `Vector<TEntity>` facade and do not depend on these mechanics.

## Boundaries

- This is a vector-search implementation, not a general Elasticsearch/OpenSearch document repository.
- One configured cluster endpoint serves all sources; source and partition isolation are expressed through
  generated index names. Per-source cluster endpoints are not supported.
- An explicit `IndexName` pins every context to one index and can defeat source, tenant, or partition isolation;
  Koan reports that conflict rather than silently rewriting the user's name.
- Automatic discovery probes unauthenticated endpoints. Secured clusters should use exact configuration.
- Provider-native dialect differences remain in their connector assemblies; they are not hidden behind a claim
  that both products accept identical kNN or mapping requests.

## References

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.SearchEngine/TECHNICAL.md)
- [Vector runtime](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.Vector/README.md)
