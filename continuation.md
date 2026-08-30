# continuation.md — `koan-create-adapter` skill + four proven adapters — SESSION COMPLETE

The task brief (`agent-prompts/koan-create-adapter-skill.md`) is fully executed: the skill exists,
all four seams are proven through it, and every obligation landed. Nothing is left to resume; this
file is now the completion record. The analytics pillar's handoff remains preserved, unchanged, as
`continuation-analytics-2026-08-28.md` (superseded per its own header).

## What shipped, phase by phase (all committed on `dev`)

| Phase | Commit(s) | What |
|---|---|---|
| 1 — skill | `650c67581` | `.agents/skills/koan-create-adapter/` (SKILL.md, references/data.md, agents/openai.yaml) |
| 2 — relational | `650c67581` | **Firebird** (`Sylin.Koan.Data.Connector.Firebird`), AODB record-plane oracle 14/14 green ×2 |
| 3 — document | `67c5518ef` | **CouchDB** (`Sylin.Koan.Data.Connector.CouchDb`), record-plane oracle 12/12 green ×2, `references/document.md` |
| 3 — vector | this session | **Chroma** (`Sylin.Koan.Data.Vector.Connector.Chroma`), vector AODB oracle 28/28 green ×2, `references/vector.md` |
| 3 — AI | this session | **llama.cpp** (`Sylin.Koan.AI.Connector.LlamaCpp`), wire-contract suite 13/13 green ×2, `references/ai.md` |
| framework | this session | `AddKoanOptions` options-ctor rooting for NativeAOT + MEMORY.md lessons |

Framework closures this session inherited from the prior one: AOT single-binary end-to-end,
fleet quality 0 findings, lockfile source-gen.

## Oracle numbers

- **Chroma** — `Koan.Data.VectorAdapterSurface.Chroma.Tests` vs real `chromadb/chroma:1.5.9`:
  28/28 = the shared `VectorAodbConformanceSpecsBase` (G-09 declaration + Shared/Container/Database
  isolation) + 24 annex cells of which 18 proven live and 6 declined with reasons (V-12 Eventual,
  V-14 Hybrid, V-15 named spaces, V-16 continuation, V-18 atomic batch, V-19 export — declining is
  conformant). Filter pushdown proven for Eq/Ne/ranges/In/Nin/AllOf/AnyOf with absent-key semantics
  agreeing with the neutral evaluator; Not/nested paths/Exists/Has*/Size/ignore-case/Eq(null)/
  non-numeric ranges fail closed before provider I/O.
- **llama.cpp** — `Koan.AI.Connector.LlamaCpp.Tests` vs a deterministic Kestrel llama-server
  wire-contract service (ARCH-0120 posture; HuggingFace is download-gated in this environment, so a
  real-weights run was impossible and model-inference behavior is out of scope by nature — stated
  in README/TECHNICAL/report): 13/13 covering chat payload+auth, SSE streaming (order, `[DONE]`,
  malformed-line tolerance, cancellation), embeddings (+no-`--embedding` refusal), unknown-model
  404 mapping, readiness (503-while-loading → Failed, absent default model → Degraded), trailing-
  `/v1` normalization, model listing.

## Playbooks fixed while dogfooding

`references/vector.md` was written first from Qdrant + the TestKit and corrected against the live
store (probe-first facts that contradicted the recon: tenant/database path prefix required, item
routes address the collection UUID only, `hnsw.dimensions` not a create field, single-key
where-dicts only, numeric-only ranges, empty-where refusal → always-true Clear predicate,
unreliable `deleted` counts, and a live-observed WAL bug that forbids `null` metadata entries).
`references/ai.md` derives the no-kit posture (ARCH-0127) and declares the two proof postures.

## AOT

Probe recipe extended and run: scratch Web-SDK project + `PublishAot` + ProjectReferences to
Chroma and LlamaCpp. Three findings (all fixed or worked around, recorded in MEMORY.md):

1. **Framework fix (committed):** `AddKoanOptions<TOptions>` generic parameter now carries
   `[DynamicallyAccessedMembers(PublicParameterlessConstructor|PublicProperties|
   NonPublicProperties)]` — without it the binary died at `ValidateOnStart` with
   `MissingMethodException` on `DirectOptions`. Core unit suite 119/119.
2. **Probe recipe gap:** a scratch ProjectReference consumer must import
   `src/Koan.Core/build/Sylin.Koan.Core.targets` itself, or no `koan.modules.manifest` /
   `koan.trimroots.xml` are embedded and boot discovers no adapters.
3. **Metadata:** pass `IDictionary` (not anonymous objects) to `Vector<T>.Save` under ILC.

Result: the probe binary (single file, ~38 MB) boots with both adapters composed and performs a
full Chroma save/get/search/delete wire round-trip against the live container. LlamaCpp composes
at boot with its source inactive (dead endpoint by design). The verbose boot's
`EmitAssemblySummary` reflection-JsonSerializer crash (only under `KOAN_VERBOSE_ASSEMBLIES=1`) was
found by this probe and subsequently fixed on a source-generated `AssemblyScanJsonContext` — the
last reflection-JSON call site in Koan.Core; the verbose AOT boot now emits the payload and the
full round-trip passes with the flag set.

## Obligations sweep (both adapters)

- The AOT proof is durable, not scratch-only: `samples/fundamentals/AotVector` (Chroma +
  llama.cpp composition, publish-and-run, adapter receipt) and two new `aot-verify.ps1` cells —
  Firebird and CouchDb, both green with receipts — extended the machine-checked matrix to eight
  connectors (PMC-049 had answered the original server question on 2026-08-21; these closed the
  adapters this session added).
- Connector matrix regenerated (`38→39` providers; Chroma + llamacpp carry the **not assessed** ⚠
  marker); package-quality regenerated — fleet **107/107 structurally-ready, 0 findings**;
  `skills-verify -Structure` passes with both new playbooks.
- READMEs written to the quality-gate shape on the first write (exact `# <PackageId>`, install
  expression, what-it-adds/limits headings); TECHNICAL companions carry the probed provider facts.
- Pack verified (`Sylin.` ids, release-train `version.json`, offset 0 for new packages).
- `MakeGenericType` sweep over both adapters and their tests: 0 hits.
- Not-assessed adapters owe no capability-map rows and no recipe ingredients (ARCH-0120).

## Selection rationale (recorded)

- Chroma: Apache-2.0, absent from the matrix, pure REST (no driver), containerized. Lost: LanceDB
  (prerelease .NET bindings, native deps).
- llama.cpp: MIT, local runtime — the same class as shipped Ollama/LMStudio; ARCH-0127 gates
  *hosted* AI only. OpenAI-compatible surface shared with the LMStudio exemplar.
