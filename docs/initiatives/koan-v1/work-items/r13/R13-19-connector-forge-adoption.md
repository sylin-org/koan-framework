---
type: SPEC
domain: framework
title: "R13-19 - Adopt the connector-forge adapters (Firebird, CouchDB, Chroma, llama.cpp)"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-08-30
framework_version: v1.0
validation:
  status: passed
  scope: four provider adapters, their family oracles, NativeAOT machine cells, staged-package consumers, and product truth
---

# R13-19 — Adopt the connector-forge adapters (Firebird, CouchDB, Chroma, llama.cpp)

## Outcome

The maintainer adopts the four adapters authored through the `koan-create-adapter` program as
supported providers, per the ARCH-0120 promotion contract. `Sylin.Koan.Data.Connector.Firebird`
and `Sylin.Koan.Data.Connector.CouchDb` become their own supported-extension claims;
`Sylin.Koan.Data.Vector.Connector.Chroma` joins the `external-vector-providers` family;
`Sylin.Koan.AI.Connector.LlamaCpp` joins the `local-ai-provider-composition` family. Every adapter
keeps its provider-specific limits as part of the claim — adoption does not widen what the
evidence proved.

## Architecture checkpoint

**Application intent:** an application references one adapter package, keeps ordinary `AddKoan()`,
and reaches its capability through the standard facade (`Entity<T>` for Firebird and CouchDB,
`Vector<T>` for Chroma, `Client`/AI topology for llama.cpp) with configuration only — no provider
client construction, no repository code, no manual registration.

**Guarantee/correction:** each adapter preserves the framework-owned semantics its family oracle
proves (isolation modes, filter convergence, honest outcome receipts, readiness) and rejects what
its provider cannot honor with a corrective before provider I/O. The claim boundaries are exactly
each package README's "what it adds / limits" contract.

## Evidence boundary

Per ARCH-0120 §3, each adoption satisfies the five conditions:

1. **Public guarantee** — package READMEs state outcome, limits, and corrective behavior; claims
   entries reference them.
2. **Family behavior** — the shared family oracle passes, twice, against real providers:
   - Firebird: record-plane AODB conformance + filter convergence + sort/paging/capability truth —
     **14/14 green ×2** (`tests/Suites/Data/Connector.Firebird`, `firebirdsql/firebird:5.0.4`).
   - CouchDB: record-plane AODB conformance + full filter corpus with the strict pushdown guard +
     paging + capability truth — **12/12 green ×2** (`tests/Suites/Data/Connector.CouchDb`,
     `couchdb:3.5`).
   - Chroma: vector-plane AODB isolation cells + V-01..V-24 annex (18 proven, 6 declined with
     reasons) + G-09 — **28/28 green ×2**
     (`tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.Chroma.Tests`,
     `chromadb/chroma:1.5.9`).
   - llama.cpp: wire-contract suite (chat, SSE streaming, embeddings, readiness ladder, error
     mapping, cancellation) — **13/13 green ×2** (`tests/Suites/AI/Connector.LlamaCpp/
     Koan.AI.Connector.LlamaCpp.Tests`, deterministic Kestrel llama-server wire-contract service
     per ARCH-0120 §"wire-contract service"; HuggingFace is download-gated in the proof
     environment, so model-inference behavior is out of scope by nature and is excluded from the
     claim).
3. **Real boundary** — real containers for the three stores; a deterministic wire-contract service
   for llama-server, which is the sanctioned posture for the AI seam (ARCH-0127: no shared AI kit
   exists; ARCH-0120 admits a wire-contract service appropriate to the guarantee).
4. **Consumer use** — clean external projects restored each package from a staged local feed of
   `PublicRelease=true` packs (no repository project references), composed `AddKoan()`, and
   reached one meaningful result against the same real boundaries. Recorded 2026-08-30:
   - `FIREBIRD|PACKAGE-CONSUMER|PASS` — Entity save/get round-trip (`firebirdsql/firebird:5.0.4`).
   - `COUCHDB|PACKAGE-CONSUMER|PASS` — Entity save/get round-trip (`couchdb:3.5`).
   - `CHROMA|PACKAGE-CONSUMER|PASS` — Vector save/get/search/delete round-trip
     (`chromadb/chroma:1.5.9`).
   - `LLAMACPP|PACKAGE-CONSUMER|PASS` — `Client.Chat` through a llama-server wire stub.
   - Observed during evidence and fixed in the same change: discovery used to debug-log a
     health-validation refusal for the `couchdb://` URI scheme the connection path itself accepted.
     The endpoint grammar now lives in one shared reader (`CouchDbEndpoint.Parse`) used by the
     factory, the client, and discovery alike — discovery health-checks a `couchdb://` source with
     its credentials, pinned by `CouchDbEndpointSpec` (5 cells) and
     `CouchDbDiscoveryHealthSpec` (2 live cells); suite 19/19.
5. **Package integrity** — the fleet quality gate reports 0 findings; generated product truth
   (product surface, package quality, connector matrix) is regenerated in the adoption change; the
   release pipeline's own API/coherence checks run at publication.

## Zero-configuration audit (2026-08-30)

Adoption is developer-facing, so each adapter was proven with ZERO application configuration —
package reference + `AddKoan()` only, server on its conventional address:

| Adapter | Conventional start | Default credentials | Zero-config proof |
|---|---|---|---|
| Chroma | `docker run -p 8000:8000 chromadb/chroma:1.5.9` | none (server unauthenticated) | save/get/search/delete round-trip PASS |
| CouchDB | `docker run -p 5984:5984 -e COUCHDB_USER=admin -e COUCHDB_PASSWORD=password couchdb:3.5` | `admin`/`password`, then the image's own `COUCHDB_USER`/`COUCHDB_PASSWORD` env, then config keys | Entity save/get round-trip PASS |
| Firebird | `docker run -p 3050:3050 -e FIREBIRD_ROOT_PASSWORD=masterkey firebirdsql/firebird:5.0.4` | engine-shipped `SYSDBA`/`masterkey`; `koan.fdb` created by managed lifecycle | Entity save/get round-trip PASS |
| llama.cpp (AI) | start `llama-server` on localhost:8080 | none by default (`--api-key` optional) | composes and boots; wire suite proves the contract |

Two defects surfaced and were fixed in this change:

1. **Firebird discovery refused a fresh container.** Health validation attached `koan.fdb`, which
   does not exist until managed lifecycle creates it — so the conventional candidate failed its
   health probe and `auto` refused to resolve. Health now treats isc_io_error (database absent) as
   healthy: the server answered and the credentials work. Pinned by `FirebirdDiscoveryHealthSpec`
   (absent-database and existing-database cells against the live container); suite 16/16.
2. **CouchDB had no viable credential default.** CouchDB 3.x refuses to start without an admin
   user, so an unset default is a guaranteed 401. Prior art: Testcontainers CouchDB modules ship
   `admin`/`password`, Aspire generates and injects (it owns the container), and the official
   image documentation's own examples use `admin`/`password` with `COUCHDB_USER`/`COUCHDB_PASSWORD`.
   Credentials now layer most-specific-first: configuration keys, then the image's environment
   convention (the operator typed them for `docker run` already), then `admin`/`password`. Pinned
   by `CouchDbDefaultsSpec`; suite 22/22.

Chroma needed nothing (unauthenticated server, conventional port). The no-server boot posture is
fail-closed by design: `auto` with no discoverable deployment refuses at startup with a corrective
naming the remedy, while a concrete-default adapter (Chroma) composes and fails correctively at
the first operation.

## NativeAOT machine evidence

`scripts/aot-verify.ps1` extended from six to eight cells and run green with adapter receipts on
win-x64: Firebird `FirebirdRepository` (28.6 MB) and CouchDb `CouchDbRepository` (26.9 MB) publish
**and run** write-read proof against their containers; Chroma is carried by
`samples/fundamentals/AotVector`, whose single-file binary (29.5 MB) performs a full vector
save/get/search/delete round-trip with `adapter=ChromaVectorAdapterFactory`, and which also proves
the llama.cpp connector composes and boots under ILC (inactive without an endpoint — the correct
posture). The AOT adoption also fixed `AddKoanOptions` options-ctor rooting in the framework
(options-twin of the KoanRegistry descriptor fix) and the assembly-scan verbose payload on a
source-generated `JsonSerializerContext` — the last reflection-JSON call site in Koan.Core. The
remaining IL2091s from the first AotVector publish are since annotated away at their flow
sources: the other `AddKoanOptions` overloads (options + configurator), `FixedOptionsMonitor<T>`,
and `BoundedSingleFlightCache` — whose `Lazy<TValue>` field became a per-entry exactly-once
holder because Lazy's metadata demands a public parameterless ctor its values never use. AotVector
now publishes with zero IL2091s; Core 119/119 and Data.Core 492/492 confirm behavior unchanged.

## Exit state

All four packages ship as supported with claims in `product/claims.json`, capability-map rows, and
family-reference entries. Publication happened through the shared release pipeline: `dev` pushed, `main` fast-forwarded
(f371f848f → 76e7a5c83), release run `33293020568` green (plan/pack/prove 12m59s, publish 2m54s).
First publication versions, indexed on NuGet.org 2026-08-30 and immutable API floors from here:

- `Sylin.Koan.Data.Connector.Firebird` **1.0.349**
- `Sylin.Koan.Data.Connector.CouchDb` **1.0.350**
- `Sylin.Koan.Data.Vector.Connector.Chroma` **1.0.357**
- `Sylin.Koan.AI.Connector.LlamaCpp` **1.0.358**
