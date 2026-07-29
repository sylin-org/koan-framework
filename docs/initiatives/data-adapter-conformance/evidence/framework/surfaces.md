---
type: REFERENCE
domain: data
title: "Koan.Data Framework Surface Inventory"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: DAC-01 clean-baseline public and alternate execution surface inventory
---

# Koan.Data Framework surfaces

Status: complete audit inventory. Source commit `working-tree`; no production source was read from the dirty worktree.

The syntax inventory contains 637 public types and 2516 public members across 27 Data projects. Every one of those 3153 declarations is assigned exactly once below. Ten internal chokepoints cover alternate paths that public declarations alone cannot expose.

| Surface | Concern | Owner | Public types | Public members | Internal anchors | Cells | Disposition |
|---|---|---|---:|---:|---:|---|---|
| SUR-ADAPTER-COCKROACH | Koan.Data.Connector.Cockroach public provider surface | Adapter | 8 | 40 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-COUCHBASE | Koan.Data.Connector.Couchbase public provider surface | Adapter | 3 | 19 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-ELASTICSEARCH | Koan.Data.Connector.ElasticSearch public provider surface | Adapter | 3 | 2 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-INMEMORY | Koan.Data.Connector.InMemory public provider surface | Adapter | 2 | 6 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-JSON | Koan.Data.Connector.Json public provider surface | Adapter | 8 | 12 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-MONGO | Koan.Data.Connector.Mongo public provider surface | Adapter | 3 | 14 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-OPENSEARCH | Koan.Data.Connector.OpenSearch public provider surface | Adapter | 3 | 2 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-POSTGRES | Koan.Data.Connector.Postgres public provider surface | Adapter | 7 | 38 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-REDIS | Koan.Data.Connector.Redis public provider surface | Adapter | 7 | 14 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-SQLITE | Koan.Data.Connector.Sqlite public provider surface | Adapter | 8 | 32 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-SQLSERVER | Koan.Data.Connector.SqlServer public provider surface | Adapter | 7 | 36 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-VECTOR-INMEMORY | Koan.Data.Vector.Connector.InMemory public provider surface | Adapter | 2 | 7 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-VECTOR-MILVUS | Koan.Data.Vector.Connector.Milvus public provider surface | Adapter | 4 | 21 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-VECTOR-QDRANT | Koan.Data.Vector.Connector.Qdrant public provider surface | Adapter | 5 | 27 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-VECTOR-SQLITEVEC | Koan.Data.Vector.Connector.SqliteVec public provider surface | Adapter | 3 | 9 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADAPTER-VECTOR-WEAVIATE | Koan.Data.Vector.Connector.Weaviate public provider surface | Adapter | 4 | 11 | 0 | A-01, A-02, A-03, A-04, A-05, C-04, C-06, G-02, G-03, G-04, G-08, H-01, H-04, H-05, H-06, P-01, P-03, P-05, P-06 | inventory only; provider verdict belongs to its fleet card |
| SUR-ADJ-DATA-AI | Data.AI Entity-adjacent embedding and vector-model surface | Adjacent pillar | 40 | 233 | 0 | C-04, H-04, P-06 | out of record-adapter scope; preserve source-policy participation where it performs Data I/O |
| SUR-EXT-BACKUP | Backup/restore alternate Data execution surface | Framework extension | 6 | 10 | 0 | B-02, B-03, B-04, C-01, C-04, G-04, H-02, P-04 | policy-bind source and target before enumeration or mutation |
| SUR-EXT-SOFTDELETE | Soft-delete axis and write override surface | Framework extension | 4 | 2 | 0 | B-03, B-04, C-01, C-04, E-11, H-04 | keep as pipeline contribution; never bypass source access policy |
| SUR-FAM-DOCUMENT | Core document-store family mechanics | Family | 2 | 16 | 1 | B-01, B-02, B-03, B-04, B-06, B-07, C-01, G-02, P-04 | rebuild against shared plans; retain only document translation mechanics |
| SUR-FAM-KEYVALUE | Core key/value family mechanics | Family | 2 | 14 | 1 | B-01, B-02, B-03, B-04, B-05, B-06, B-07, B-08, B-09, G-05, G-09, P-04 | rebuild handled claims and batch atomicity semantics |
| SUR-FAM-RELATIONAL-CONTRACT | Relational mapping, schema, DDL, and store contracts | Relational Family | 17 | 70 | 0 | A-07, A-08, A-09, C-02, E-01, E-02, E-03, E-04, E-08, E-09, E-11, E-12, E-13, E-14, E-15, G-01 | rebuild behind shared mapping and lifecycle plans |
| SUR-FAM-RELATIONAL-EXECUTION | Relational ADO, filter lowering, scalar encoding, and query mechanics | Relational Family | 14 | 67 | 0 | B-01, B-02, B-06, B-07, D-05, E-07, E-08, E-11, F-04, G-04, H-06, P-02, P-03, P-04 | keep parameterized native mechanics; remove static unbounded plan caches |
| SUR-FAM-RELATIONAL-NPGSQL | Npgsql relational repository family implementation | Relational Npgsql Family | 2 | 28 | 0 | B-01, B-02, B-03, B-04, B-06, B-07, C-01, E-11, G-06, H-06, P-04 | evaluate as family seam; do not use as gold-author input |
| SUR-FAM-SEARCHENGINE | Search-engine family translation and vector repository mechanics | SearchEngine Family | 9 | 56 | 0 | A-01, A-02, A-04, C-01, C-04, G-02, G-04, H-01, H-04, P-01, P-03, P-06 | share source-core policy; defer similarity annex semantics to DAC-49 |
| SUR-FAM-VECTOR-CONTRACT | Vector repository, schema, claims, and query contracts | Vector Family | 21 | 78 | 0 | A-01, A-02, A-04, C-01, C-04, G-02, G-04, H-01, H-04, P-01, P-03, P-06, V-01, V-02, V-03, V-04, V-05, V-06, V-07, V-08, V-09, V-10, V-11, V-12, V-13, V-14, V-15, V-16, V-17, V-18, V-19, V-20, V-21, V-22, V-23, V-24 | own the ratified provider-neutral Vector contract |
| SUR-FAM-VECTOR-RUNTIME | Vector public terminals, provider election, coordination, and filter gate | Vector Family | 14 | 63 | 0 | A-01, A-02, A-04, C-01, C-04, G-02, G-04, H-01, H-02, H-04, P-01, P-03, P-06, V-01, V-02, V-03, V-04, V-05, V-06, V-07, V-08, V-09, V-10, V-11, V-12, V-13, V-14, V-15, V-16, V-17, V-18, V-19, V-20, V-21, V-22, V-23, V-24 | realize the ratified Vector contract through one runtime plan |
| SUR-FWK-CAPS-RECEIPTS | Capabilities, query results, counts, and execution receipts | Framework | 12 | 51 | 2 | B-07, B-09, D-08, E-08, E-12, E-15, G-05, G-06, G-07, G-09, H-01, H-02, H-04, P-04 | rebuild claim vocabulary and receipts around primer profiles |
| SUR-FWK-DIRECT | Direct session, connection override, raw command, scalar, query, and transaction | Framework | 3 | 16 | 1 | C-01, C-02, C-04, C-05, D-05, D-06, D-08, F-05, F-06, F-11, G-04, H-05, H-06, P-02 | narrow to explicit provider-native escape hatch behind source policy |
| SUR-FWK-ENTITY-BULK | Entity/Data bulk and batch mutation | Framework | 0 | 11 | 0 | B-03, B-04, B-05, C-01, C-04, G-05, P-05 | rebuild around explicit outcome and atomicity claims |
| SUR-FWK-ENTITY-QUERY | Entity/Data query, count, page, raw query, and result shaping | Framework | 0 | 40 | 0 | B-06, B-07, B-08, B-09, C-04, H-02, P-04 | keep compact Entity grammar; rebuild execution receipts and bounded fallback law |
| SUR-FWK-ENTITY-READ | Entity/Data keyed and finite reads | Framework | 7 | 71 | 0 | B-01, B-02, B-08, C-04, P-01, P-02 | keep Entity grammar; route through compiled operation plans |
| SUR-FWK-ENTITY-STREAM | Entity/Data streaming and provider-bounded paging | Framework | 0 | 28 | 0 | B-08, B-09, G-04, G-08, P-04, P-05 | keep only capability-qualified incremental paths |
| SUR-FWK-ENTITY-WRITE | Entity/Data scalar writes, patch, remove, and delete-all | Framework | 0 | 49 | 0 | B-01, B-03, B-04, C-01, C-02, C-03, C-04, G-06, H-06 | keep Entity grammar; move access and effect gate before callbacks/readiness/I/O |
| SUR-FWK-INSPECTION-RECORDS | Provider-neutral inspection, storage descriptors, and RecordSet materialization | Framework | 0 | 0 | 0 | D-01, D-02, D-03, D-04, D-05, D-06, D-07, D-08, D-09, P-02, P-05, P-06 | create as a shared target contract; current Direct dictionaries do not qualify |
| SUR-FWK-INSTRUCTION | Instruction, raw-query, patch, and native-operation dispatch | Framework | 16 | 30 | 0 | B-04, C-01, C-02, C-04, C-05, F-05, F-06, F-11, H-05, H-06 | rebuild effect typing and prohibit message/prefix inference and replay |
| SUR-FWK-LIFECYCLE | Entity persistence lifecycle registration and dispatch | Framework | 6 | 34 | 0 | B-03, B-04, C-01, C-04, H-04 | keep lifecycle; guarantee policy rejection before callbacks |
| SUR-FWK-MAPPING | Projection, index, optimization, and logical-to-physical mapping metadata | Framework | 11 | 30 | 1 | A-07, E-01, E-02, E-03, E-04, E-05, E-06, E-07, E-08, E-09, E-10, E-11, E-12, E-13, E-14, E-15, P-02, P-03 | replace scalar-only process cache with host-owned compiled mapping plans |
| SUR-FWK-NAMING-SEGMENTATION | Storage naming, partitions, axes, monikers, and segmentation | Framework | 44 | 104 | 0 | A-02, A-04, C-04, E-05, E-06, E-11, G-03, G-09, H-01, P-01, P-03 | keep semantic routing; compile into source-bound plans and close alternate bypasses |
| SUR-FWK-PIPELINE | Managed fields, guards, transforms, stamps, and operation overrides | Framework | 19 | 46 | 0 | B-01, B-03, B-04, C-01, C-04, E-11, H-04, P-02, P-03 | keep one canonical pipeline; bind source policy before side effects |
| SUR-FWK-POLYMORPHISM | Entity family descriptors, type catalog, and document codecs | Framework | 7 | 26 | 0 | B-01, B-02, E-06, E-07, E-11, P-02, P-03 | keep shared codecs; consume the same compiled map on every path |
| SUR-FWK-QUERY-PLAN | Filter, sort, projection, pagination, count, and pushdown planning | Framework | 45 | 140 | 0 | B-06, B-07, B-08, B-09, E-08, E-11, H-02, P-02, P-04 | absorb into one immutable execution plan and receipt |
| SUR-FWK-READINESS-DIAGNOSTICS | Readiness, health, boot reports, facts, and failure classification | Framework | 21 | 89 | 1 | A-03, A-05, A-07, A-08, A-09, C-02, C-06, G-01, G-02, G-04, G-08, H-01, H-02, H-03, H-04, H-05, H-06, P-01, P-03, P-05 | rebuild route/shape state; remove operation probe/replay and message classification |
| SUR-FWK-REGISTERED-OPERATIONS | Immutable named Query/Scalar catalog, effect gate, lanes, parameters, and bounds | Framework | 0 | 0 | 0 | C-05, F-01, F-02, F-03, F-04, F-05, F-06, F-07, F-08, F-09, F-10, F-11, F-12, P-01, P-02, P-03, P-05, P-06 | create as a shared target contract; current raw instructions do not qualify |
| SUR-FWK-RELATIONSHIP | Relationship metadata and bounded relationship loading | Framework | 10 | 40 | 0 | B-06, B-08, B-09, H-02, P-04 | keep outside adapter mapping; preserve explicit bounded fallback |
| SUR-FWK-REPOSITORY-CHOKEPOINT | RepositoryFacade canonical execution chokepoint | Framework | 0 | 0 | 1 | A-08, A-09, B-01, B-03, B-04, C-01, C-02, C-03, C-04, G-02, H-06 | rebuild ordering around compiled source policy and readiness stages |
| SUR-FWK-REPOSITORY-CONTRACT | Repository, query, bounded-query, batch, conditional, and instruction contracts | Framework | 7 | 23 | 0 | B-01, B-02, B-03, B-04, B-05, B-06, B-07, B-08, B-09, C-01, C-04, G-05, G-06, P-04 | keep minimal mechanics; add truthful receipts and fail-closed policy contracts |
| SUR-FWK-ROUTING-CONTEXT | Ambient source, adapter, partition, cache, and transaction context | Framework | 2 | 20 | 0 | A-02, A-04, C-01, C-04, G-03, G-09, H-01, P-01, P-03 | absorb into policy-bound operation context |
| SUR-FWK-SOURCE-CATALOG | Source declaration, catalog, provider election, and route resolution | Framework | 18 | 49 | 1 | A-01, A-02, A-03, A-04, A-06, C-04, C-06, H-01, H-04, P-01, P-03, P-06 | keep route mechanics; rebuild under frozen source plans |
| SUR-FWK-SUPPORT | Remaining public Data support, annotations, metadata, configuration, and utilities | Framework | 183 | 629 | 0 | A-01, H-04, P-02, P-03, P-06 | keep only concepts required by a business decision or shared guarantee |
| SUR-FWK-TRANSACTION | Deferred transaction coordination and transaction context | Framework | 6 | 30 | 1 | B-05, C-01, C-04, G-03, G-04, G-05, G-09, H-02, H-06 | rename/describe as local best-effort unless native atomicity is proved |
| SUR-FWK-TRANSFER | Copy, move, mirror, and partition transfer builders | Framework | 12 | 33 | 0 | B-02, B-03, B-04, C-01, C-04, G-04, H-02, P-04 | keep bounded workflow; bind both endpoints to explicit source policy |

## Mechanical coverage

- `public-api.json` is the restore-free Roslyn syntax inventory of the frozen production source.
- `surface-map.json` contains every API-to-SUR assignment and the ten internal-anchor records.
- `vocabulary.json` compares the compact primer vocabulary with exact public declarations.
- Adapter rows are inventory boundaries, not adapter verdicts; provider certification remains with the fleet cards.
