title: Sylin.Koan.Data.Vector.Connector.InMemory - Technical Reference
description: Managed automatic-floor vector provider for Koan.
packages: [Sylin.Koan.Data.Vector.Connector.InMemory]
source: src/Connectors/Data/Vector/InMemory/

## Composition and election

The package references `Sylin.Koan.Data.Vector`, so one connector reference supplies both provider and functional
runtime. `InMemoryVectorModule` registers one singleton factory. The factory declares provider `inmemory`, aliases
`memory` and `inproc`, priority `-100`, and `IsAutomaticFloor = true`.

Direct provider references participate in normal Vector election. When no direct candidate owns the decision, the
catalog may elect this provider as its automatic floor. Explicit `[VectorAdapter]` and configured Vector defaults remain
exact.

## Lifetime and naming

One factory owns concurrent dictionaries for the application service-provider lifetime. Repositories are memoized by
Vector Core per entity and routed source. Each operation asks `VectorAdapterNaming` for the selected provider's current
entity/partition/source name; the adapter does not repeat election or maintain a second isolation naming rule.

There is no public reset API. Tests and isolated applications obtain a fresh store by creating a fresh application
service provider.

## Capability behavior

The provider declares kNN, full unified metadata filters, hybrid scoring, native continuation, streaming export, bulk
upsert/delete, normalized scores, and dynamic collections. It does not declare atomic batch or multiple vectors per
entity. Ranking is exact brute-force cosine similarity; memory and CPU therefore grow with the active dataset.

Vector Core applies segmentation before repository calls. Tenant metadata is stamped and filtered because this
provider declares filter support. Partition and Database-source isolation are physical dictionary-name folds.

## Health and failure boundary

There is no connector health contributor because there is no external or persistent dependency to probe. Invalid
vector/filter operations fail through the shared Vector contracts. Process exit is data loss by design.

