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
   - Observed, not blocking: CouchDB's discovery adapter debug-logs a health-validation refusal for
     the `couchdb://` URI scheme that the connection path itself accepts; the operation completes.
     Cosmetic noise for explicitly-configured sources, filed here for a later pass.
5. **Package integrity** — the fleet quality gate reports 0 findings; generated product truth
   (product surface, package quality, connector matrix) is regenerated in the adoption change; the
   release pipeline's own API/coherence checks run at publication.

## NativeAOT machine evidence

`scripts/aot-verify.ps1` extended from six to eight cells and run green with adapter receipts on
win-x64: Firebird `FirebirdRepository` (28.6 MB) and CouchDb `CouchDbRepository` (26.9 MB) publish
**and run** write-read proof against their containers; Chroma is carried by
`samples/fundamentals/AotVector`, whose single-file binary (29.5 MB) performs a full vector
save/get/search/delete round-trip with `adapter=ChromaVectorAdapterFactory`, and which also proves
the llama.cpp connector composes and boots under ILC (inactive without an endpoint — the correct
posture). The AOT adoption also fixed `AddKoanOptions` options-ctor rooting in the framework
(options-twin of the KoanRegistry descriptor fix) and the assembly-scan verbose payload on a
source-generated `JsonSerializerContext` — the last reflection-JSON call site in Koan.Core.

## Exit state

All four packages ship as supported with claims in `product/claims.json`, capability-map rows, and
family-reference entries. Their exact first publication versions become immutable API floors in
the following slice, same as every train release. Merging `dev` into `main` (fast-forward)
triggers the shared release pipeline, which plans, packs, proves, and publishes the changed
packages — including the four adapters and the framework fixes this program landed.
