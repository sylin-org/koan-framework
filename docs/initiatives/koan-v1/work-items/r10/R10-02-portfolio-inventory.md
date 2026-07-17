---
type: SPEC
domain: framework
title: "R10-02 - Reconcile the Sample Portfolio"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: physical project, solution membership, README, ghost-directory, and next-slice inventory
---

# R10-02 — Reconcile the sample portfolio

- Tranche: `T7B — maintained-sample graduation`
- Status: `passed`
- Depends on: R10-01 graduation standard
- Unlocks: S1.Web as the next high-value curriculum slice

## Exact physical baseline

`rg --files samples -g '*.csproj'` finds 27 projects:

- 10 are already under `samples/archive/` and remain outside the maintained portfolio;
- 17 are outside the archive;
- 13 non-archive projects are in `Koan.sln`;
- 4 non-archive projects are outside the solution (`S7.Meridian`, `S19.McpCatalogSample`, `S20.OpenGraph`, and
  the non-runnable `S8.Canon` umbrella project);
- only 16 of all 27 project directories contain a README;
- `S3.Mq.Sample` and `S16.PantryPal` are top-level ghost directories with no project file.

The canonical sample index also described absent `S8.PolyglotShop`, called solution-owned SnapVault broken,
called Meridian outside the solution, and omitted S19/S20. Those statements are not a maturity model.

## Graduation queue

| Group | Projects/directories | Current disposition | Why |
|---|---|---|---|
| Already contract-backed | FirstUse, GoldenJourney, g1c1.GardenCoop | `graduate` | meaningful source/business/facts proof exists; GardenCoop adds measured NativeAOT |
| Canonical curriculum | S0.ConsoleJsonRepo, S1.Web | `assess next` | public ladder and simplest Koan mental models; errors here damage first-use positioning |
| Capability/deployment rungs | g1c2.GardenCoopEmbedded, S10.DevPortal, S14.AdapterBench | `assess` | embedded sovereignty, provider switching, and jobs/benchmark claims need executable truth |
| Maintained dogfood | S5.Recs, S6.SnapVault, S18.Prism, S8.Canon.Api/Shared | `assess` | large valuable surfaces; graduate only with explicit prerequisites and focused business proof |
| Outside solution | S7.Meridian, S19.McpCatalogSample, S20.OpenGraph | `incubate pending assessment` | cannot be called maintained until solution/test/docs status is explicit |
| Ghost directories | S3.Mq.Sample, S16.PantryPal | `archive-or-delete pending content review` | no executable project exists; current sample-index claims are misleading |
| Archived tree | 10 archived projects | `archive` | explicitly outside maintained curriculum; no R10 modernization promised |

This is an execution queue, not an unsupported promotion. Only the first row is graduated today.

## Current graduation update

R10's later architect mandate makes `assess` and `incubate` temporary states: every project that remains in the
active V1 curriculum must graduate or leave it. S0, S1, S10, and g1c2 have now graduated. The remaining
capability/deployment queue is S14.AdapterBench. g1c2 now proves local Entity embeddings and semantic search
through ONNX, SQLite, and sqlite-vec, with a truthfully bounded self-contained deployment folder.

## Next slice selection

S14.AdapterBench goes next because it is the last capability/deployment sample. Its benchmark and durable-job
claims need one business reason to exist, repeatable workload boundaries, truthful provider prerequisites, and
operator evidence before the broader maintained dogfood applications are assessed.

## Acceptance evidence

- Physical/project/solution/README inventory is exact as of 2026-07-17.
- Every non-archive project or ghost directory has a visible current queue disposition.
- The inventory does not call `assess` or `incubate` supported.
- The GardenCoop-derived graduation standard is reusable without imposing a universal sample architecture.
- S1.Web is selected by product value and cognitive-load risk, not directory order.
