---
type: SPEC
domain: framework
title: "R13-14 - Promote external Vector and Search providers"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: passed
  scope: Qdrant, Milvus, Weaviate, Elasticsearch, OpenSearch, shared SearchEngine, package, consumer, product, and API evidence
---

# R13-14 — Promote external Vector and Search providers

## Architecture checkpoint

**Task:** Promote Koan's five intended external vector/search providers to the supported 0.20 surface
through their existing family semantics, native provider deltas, and real service boundaries.

**Application intent:** An application installs one external vector-provider package, keeps ordinary
`AddKoan()`, and saves and searches Entity embeddings through `Vector<TEntity>` without constructing a
provider client or repository.

**Public expression:** The normal path is one provider package plus the existing Vector facade:

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Qdrant
# or Milvus / Weaviate / Sylin.Koan.Data.Connector.ElasticSearch / OpenSearch
```

```csharp
builder.Services.AddKoan();

public sealed class Article : Entity<Article>;

await Vector<Article>.Save("koan", embedding, new { category = "docs" });
var nearest = await Vector<Article>.Search(embedding, topK: 10);
```

The complete expression adds an endpoint and credentials only when zero-configuration local discovery
does not describe the deployment. `[VectorAdapter("...")]` or the existing Vector default-provider
option is required only when more than one eligible provider is referenced and placement must be
explicit. The selected provider service must be reachable at runtime.

**Guarantee/correction:** The Vector runtime elects exactly one provider for an Entity/source route and
the adapter preserves Koan naming, partition/tenant folds, caller-owned positive `topK`, honest
capabilities, and selection-aware readiness through its native protocol. An explicitly selected but
unavailable provider fails through its operation/readiness boundary; Koan does not silently fall back
to another referenced provider. Unsupported filters, reads, hybrid search, export, continuation, or
statistics fail or remain capability-gated rather than being emulated dishonestly.

**Complete intent surface:** One provider package; `AddKoan()`; a string-keyed Entity; application- or
model-produced embeddings; `Vector<TEntity>.Save/Search`; optional existing provider routing;
provider-specific endpoint/authentication when not local; and a reachable Qdrant, Milvus, Weaviate,
Elasticsearch, or OpenSearch deployment. No manual factory, repository, HTTP client, schema, index, or
health registration is required.

**Public concepts:** Existing `Vector<TEntity>` is the application language; `VectorQueryOptions`,
`VectorCaps`, `[VectorAdapter]`, and Vector defaults express optional guarantees/routing. Provider
options express deployment and native tuning. Elasticsearch/OpenSearch intentionally expose their
general connector package names while contributing only vector-search behavior here. No new public
concept is required.

**Docs read:**

- `docs/engineering/index.md` — requires focused owner evidence, package hygiene, centralized stable
  identifiers, and proportionate validation; governing.
- `docs/architecture/principles.md` — makes package reference availability, Vector-owned election,
  adapter-owned mechanics, and corrective failure the public model; governing.
- `docs/decisions/ARCH-0120-terminal-package-maturity.md` — requires real-boundary provider evidence,
  a clean package consumer, and claim splitting when grouped providers are not equally ready; governing.
- `docs/reference/cards/vector.md` — freezes `Vector<TEntity>.Save/Search`, capability gating,
  provider selection, and participation-aware readiness; directly applicable.
- `docs/initiatives/koan-v1/work-items/R13-terminal-package-maturity.md` — identifies these five
  providers as the next value-led family after the local Vector floor; directly applicable.
- Provider and `Koan.Data.SearchEngine` companions — state native capabilities, non-claims,
  configuration, naming, authentication, and health behavior; directly applicable.

**Code read:**

- `SearchEngineVectorRepository.cs` — owns the common Elasticsearch/OpenSearch REST, naming, filter,
  export, statistics, and lifecycle mechanics behind a narrow native dialect seam; keep.
- `ElasticSearchVectorAdapterFactory.cs` and `OpenSearchVectorAdapterFactory.cs` — retain only native
  identity, service metadata, and dialect descriptors over the shared mechanism; keep.
- Qdrant, Milvus, and Weaviate adapter factories/repositories — own materially different protocols,
  key shapes, native capabilities, consistency, discovery, and lifecycle; keep adapter-specific.
- Five `VectorAdapterSurface` factories — already own real container lifecycle and truthful capability
  matrices; reuse as the evidence owner.
- `IVectorAdapterTestFactory` and shared matrix bases — already express the common CRUD/search,
  isolation, capability, and semantic laws; reuse without another admission layer.

**Reusing:** The supported Vector runtime/facade, provider catalog and naming law, participation health,
typed provider options, existing constants, SearchEngine shared mechanism/dialects, five real-container
matrix cells, package compiler, API guard, lean PR gate, and main publisher already exist.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Provider identity and priority members | Existing Qdrant/Milvus/Weaviate `Infrastructure/Constants.cs` files | Remove duplicated stable adapter identifiers without changing provider behavior or creating a shared abstraction. |
| R13-14 evidence card | This file | Freeze the two honest claims, real boundaries, and consumer/public results at their existing family owner. |

No new runtime type, option, contract, DTO, repository abstraction, endpoint, or test framework is
planned. Ordinary provider tests may expose a focused correction at their owning adapter.

**Coalescence:** Closest common pattern: `Koan.Data.SearchEngine`, which already absorbs mechanics whose
meaning and lifecycle are identical for Elasticsearch and OpenSearch while retaining three dialect
deltas. Disposition: keep that shared capability-family owner; keep Qdrant, Milvus, and Weaviate as
adapter-specific implementations; absorb only duplicated stable provider literals into each existing
constants owner. A wider universal vector repository is wrong because protocols, consistency,
authentication, filter languages, schema, and capability sets differ. A narrower duplication of
SearchEngine mechanics is also wrong. No current public path is superseded or deleted.

**Ergonomics:** Installation and IntelliSense remain centered on provider package names and
`Vector<TEntity>`. Provider options appear only for deployment/authentication/native tuning; routing
appears only when multiple providers create a genuine placement decision. Application code never sees
factories, dialects, HTTP clients, repositories, health contributors, or discovery candidates.

**Constraints satisfied:**

- Entity-centered `Vector<TEntity>` facade; repository use remains an advanced escape hatch.
- No HTTP endpoint is added.
- Stable provider identity/priority literals move to adapter-scoped constants; tunables remain typed
  options.
- Search remains caller-bounded by positive `topK`; export is explicit and capability-qualified.
- Existing README/TECHNICAL companions and generated product truth receive the support claim/limits.
- Existing family cells and real containers provide evidence; no generic certification or admission
  infrastructure is added.

**Risks:** Five native services have different resource profiles. Milvus uses a real standalone stack
with etcd and MinIO and may exceed this workstation's Docker memory; if it cannot run reliably, the
external-vector claim must split and Milvus must remain demonstrated. Exact first 0.20 patch versions
are determined only by the merged main commit and must become API floors in the following slice.

## Evidence boundary

1. Run each existing provider cell against its real container with no infrastructure skip accepted;
   record its explicit unsupported-capability skips.
2. Run the smallest provider-specific deltas already owned by each test project; repair only failures
   that contradict the documented guarantee.
3. Pack the six package owners (`SearchEngine` plus five providers) with `PublicRelease=true` and
   inspect their supported Koan dependency bands.
4. Restore/build/run one clean external staged-package consumer containing all six packages and prove
   ordinary `AddKoan()` activation of every provider. Do not repeat the five service-backed data paths:
   the focused provider cells already own that behavioral evidence.
5. Compile product truth, run API posture and lean no-tests coherence, publish through `main`, then
   rerun the same consumer from NuGet.org-only packages in a fresh cache.
6. Do not run unrelated providers, AI, or whole-framework certification.

## Focused evidence — 2026-07-22

- Qdrant real container: 39 passed, with only hybrid search and index statistics skipped as declared;
- Weaviate real container: 34/34 passed with zero skips, including provider-specific isolation and
  AODB coverage;
- Elasticsearch 9.4.3 real container: 29 passed with four declared skips for embedding retrieval and
  hybrid search;
- OpenSearch 3.7.0 real container: 29 passed with the same four declared skips, proving the shared
  SearchEngine mechanism through the native OpenSearch dialect;
- Milvus 2.6.20 plus real etcd/MinIO dependencies: 25 passed with eight declared skips for immediate
  delete visibility, embedding retrieval, export, statistics, and hybrid search;
- no infrastructure skip, false pass, unrelated provider suite, AI suite, or whole-framework
  certification ran;
- six `PublicRelease=true` packs produced exact staged `0.20.0` package and symbol artifacts; every
  Koan dependency is bounded to the supported `[0.20.x, 0.21.0)` family, including shared
  `SearchEngine` and the already-public Vector foundation;
- one clean external consumer restored all six staged packages into a fresh cache, built with zero
  warnings/errors, and proved normal `AddKoan()` activation of all five providers with
  `EXTERNAL-VECTOR-SEARCH|PACKAGE-CONSUMER|ADDKOAN|ELASTICSEARCH|OPENSEARCH|QDRANT|MILVUS|WEAVIATE|PASS`;
- no service stack was restarted for the consumer because the five focused real-container cells
  already provide the provider-specific data proof; the consumer owns only package graph and normal
  activation proof;
- generated product truth is current at 41 claims / 93 packages; API posture is 56/62 configured,
  with exactly these six allowed first-publication floors pending and three content-only owners;
- lean no-tests coherence passed release build, composition lockfile, documentation truth/lint,
  diff-scoped code validation, skills lint, and blueprint lint; no certification suite ran.
- PR `#104` passed lean gate `29908221940` and squash-merged to `main` as
  `663b947f783ff0d9a445cce6c45b0330684e59d3`; release run `29908506818` published the six exact
  `0.20.0` package and symbol artifacts;
- NuGet.org indexed all six artifacts. The unchanged consumer restored from NuGet.org only into a
  second fresh cache, built with zero warnings/errors, and emitted the same
  `EXTERNAL-VECTOR-SEARCH|PACKAGE-CONSUMER|ADDKOAN|ELASTICSEARCH|OPENSEARCH|QDRANT|MILVUS|WEAVIATE|PASS`
  result;
- the six immutable first-publication API floors are captured in their owning projects by R13-15.
