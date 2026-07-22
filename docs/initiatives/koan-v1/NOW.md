---
type: HANDOFF
domain: koan-v1
status: resolved
last_updated: 2026-07-22
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current objective

The 0.20 maturity cycle is complete. Preserve 0.20 as the supported-contract signal, keep release
automation proportionate, and return to ordinary value-led framework development. Historical package
counts do not define completion.

## Completed baseline: first supported wave and public consumer proof

The maintainer rejected the automatic release compiler after multiple long validation cycles exposed
failures in release orchestration rather than framework behavior. The later manual-from-`dev`
replacement also changed the integration boundary without explicit approval. The corrected decision
is recorded in [R12-06](work-items/r12/R12-06-publish-and-observe-first-wave.md) and ARCH-0110:

- commits and pull requests targeting `dev` trigger no validation or publication workflow;
- pull requests targeting `main` run the existing validation gate but cannot publish;
- the resulting `main` commit automatically runs one package publication job;
- each packable project keeps its local NBGV `version.json`;
- the workflow compiles the product surface, runs `dotnet pack` with `PublicRelease=true`, and runs
  `dotnet nuget push --skip-duplicate` only for the compiler-selected guaranteed 0.20 package owners,
  using the established `NUGET_API_KEY`;
- no lineage branch, synthetic commits, manifests, escrow, tags, GitHub Releases, recovery ledger,
  duplicated proof lanes, or full test ratchet participates in publication.

The former release subsystem and its implementation-detail tests are removed. Package
inventory, quality assessment, product-surface compilation, NBGV stamping, bounded internal
dependency ranges, and package metadata remain.

Focused implementation evidence:

- `Koan.Packaging` Release build: clean in 2.3 seconds;
- trimmed `Koan.Packaging.Tests` compile: clean in 2.1 seconds; tests were not executed;
- evaluated inventory: 93 independently versioned packages in 10.3 seconds;
- declared product surface: 18 supported claims map to exactly 38 guaranteed package owners, all
  with `versionIntent=0.20`;
- workflow: one `main`-push job, zero test commands, one API-key reference, and no Git mutation or
  legacy release state;
- `Sylin.Koan.Templates` is the one packable project outside `Koan.sln` and has one explicit standard
  pack command in the same job.
- Public `Sylin.Koan.Templates 0.20.6` now uses standard `0.20.*` patch floats. A fresh NuGet.org
  install generated both templates; isolated restores completed without warnings, the web project
  built with zero warnings/errors, and the console passed SQLite Entity save/load/query.
- SQLite startup explanation now follows adapter-owned fallback selection and no longer emits the
  misleading correction path for local auto mode. The fix is tracked as complete in `R12-05`.

## Accepted next epic: value-led 0.20 promotion

[ARCH-0120](../../decisions/ARCH-0120-terminal-package-maturity.md) keeps the useful 0.20 invariant:
supported claim owners use 0.20 intent, their public Koan dependency closure is supported, and every
0.20 owner belongs to a supported claim. It now rejects the larger fixed-owner program that grew
around that invariant.

Product intent—not reverse dependencies—decides whether a package belongs. Database, vector, search,
storage, authentication, and AI adapters are first-class public extensions even when applications
are their only consumers. R13 promotes those capabilities by meaningful family: shared semantics,
provider-specific real-boundary proof, a clean package consumer, and package/API integrity. The
historical 38/55 inventory remains discovery, not an execution ledger or completion count.

The lean [R13](work-items/R13-terminal-package-maturity.md) uses the first seven-owner testing and
operational slice to prove the smaller promotion path. After that publication decision, high-use Data
providers are next, followed by Vector/Search, AI providers, Storage/Media, external Auth, and only
already-decided public migrations. Families may progress independently when no real prerequisite
connects them.

## Exact pause point

The lean process change and first seven-owner slice are merged, published, indexed, and public-consumer
green. R12-07's ordinary public upgrade proof is also green, and the maintainer explicitly accepted
its GO recommendation on 2026-07-22. R12 and R13 are complete. This records 0.20 preview maturity; it
does not declare V1 GA or a V1 support date.

PostgreSQL R13-07 through Local Storage/Media R13-16 are merged, published, indexed, public-consumer
green, and baseline-captured at their exact first 0.20 versions. Redis's functional backend and inert
contract are separately supported while Cache Redis remains unassessed. R13-16 passed 621 focused tests,
staged and public consumers, product/API guards, lean coherence, and exact publication of its six owners.
S3 and Data Backup are shelved; they are not 0.20 claims or blockers.

R13-17 is also complete: Google, Microsoft, and Discord are public at exact `0.20.0`, indexed, and
public-consumer green. The focused definition suite passed 41/41 and the deterministic OAuth2/OIDC
authorization-code suite passed 5/5 without live credentials. Release publication created only those
three new identities; already-public Storage/Media packages were skipped at their existing versions.
Their immutable API floors are now recorded centrally without touching package-owned version paths.

R13 is complete. [R13-18](work-items/r13/R13-18-accepted-migration-disposition.md) rechecked the
already-decided Agyo and Zen Garden moves against their public repositories and NuGet.org. Agyo has no
public package IDs or migrated Agents/Orchestration/Eval/Review owners; Zen Garden has no newer public
tip or equivalent Models/HuggingFace lifecycle proof. The six departing Koan owners therefore remain at
truthful `0.17` intent with no supported claim until ARCH-0089's cross-repository destination gate is met.
No migration, deletion, promotion, or forwarding package is part of R13 closure. This does not reopen
S3 or Backup.
Untracked `tmp/` is unrelated user-owned material and must remain untouched and unstaged.

## Remote/public state

- PR `#93` merged to `main` as `ad9d739199da809fa44efc9a4ce3db8059348b42`. Main-boundary run
  `29792486934` succeeded: product-surface selection resolved the exact 38-package guaranteed 0.20
  closure, packing completed, and NuGet accepted the publication set.
- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`. Main-boundary run
  `29796113330` succeeded and NuGet.org indexed the corrected `Sylin.Koan.Templates 0.20.6`.
- PR `#95` merged to `main` as `a8a1bb61b53195ce44bef00024d722862deb949d`. Lean gate
  `29891719299` passed with one job and no tests/containers; release run `29891926990` published the
  45-package supported closure. NuGet.org indexed all seven newly supported owners and their clean
  public consumer passed.
- PR `#96` merged to `main` as `b89cec6266080186db4fdd3fee99aa04b089abbc`. Lean gate
  `29893297175` passed as one job with no tests/containers; release run `29893491621` published
  PostgreSQL and Npgsql `0.20.1`. Both are indexed, and the exact public package consumer passed.
- PR `#97` merged to `main` as `a8d3869adc84d15a330acb52cdf5c7dca916a6ad`. Lean gate
  `29894628337` passed as one job with no tests/containers; release run `29894829655` published SQL
  Server `0.20.1`. It is indexed, and the exact public package consumer passed.
- PR `#98` merged to `main` as `e90e8fecff4efb3d1a4dd2d956b8d8f1bc4b423a`. Lean gate
  `29895799297` passed as one job with no tests/containers; release run `29896020354` published MongoDB
  and Zen Garden Contracts `0.20.1`. Both are indexed, and the exact public package consumer passed
  MongoDB Entity behavior while proving the contract remained inert.
- PR `#99` merged to `main` as `38d00f841b9dcd0cc22e3540918436e8d2f542d3`. Lean gate
  `29898149195` passed as one job with no tests/containers; release run `29898380061` published
  Couchbase `0.20.1`. It is indexed, and the exact public package consumer passed against Community
  8.0.2.
- PR `#100` merged to `main` as `b5628a7abad1e275522bed74901e1db9a459de29`. Lean gate
  `29900348172` passed as one job with no tests/containers; release run `29900614297` published the
  Redis Data connector, shared backend, and inert abstractions at exact `0.20.0`. All are indexed,
  and a NuGet.org-only consumer restored into an empty cache and passed against Redis 8.8.
- PR `#101` was closed unmerged when protected `dev` could not be force-rebased across historical
  merge commits already present on `main`; no validation or publication ran from it. Exact one-commit
  replacement PR `#102` passed lean gate `29902727400`, squash-merged as
  `3ff7f1950931addd12a16e194299468bd442dcdf`, and release run `29903009583` published CockroachDB
  `0.20.0`. NuGet.org indexed it and the fresh public 26.2.3 consumer passed.
- PR `#103` passed lean gate `29905547144`, squash-merged as
  `e96a4dbe8fd83dd99f8d5a438f1765f31c420ec5`, and release run `29905812375` published the Vector
  runtime/abstractions at `0.20.0` and InMemory/sqlite-vec at `0.20.1`. All four are indexed and the
  fresh NuGet.org-only restart consumer passed unchanged.
- PR `#104` passed lean gate `29908221940`, squash-merged as
  `663b947f783ff0d9a445cce6c45b0330684e59d3`, and release run `29908506818` published SearchEngine,
  Elasticsearch, OpenSearch, Qdrant, Milvus, and Weaviate at exact `0.20.0`. All six are indexed and
  the unchanged fresh NuGet.org-only activation consumer passed.
- PR `#105` passed lean gate `29918564166`, squash-merged as
  `98c937b90b74e51d2a7b321214c7667e9743d6ce`, and release run `29918889215` published AI runtime,
  contracts, Ollama, and LM Studio at exact `0.20.0` plus ONNX `0.20.1`. All five are indexed and the
  fresh NuGet.org-only consumer passed.
- PR `#106` passed lean gate `29923443985`, squash-merged as
  `4f3eabe949bbb1b02b77cdb4f4afff85cb7a5917`, and release run `29923873005` attempt 2 published the
  six Local Storage/Media owners at exact `0.20.0`. All six are indexed and the fresh NuGet.org-only
  consumer passed. S3 and Backup remained unchanged and unpublished. Attempt 1 encountered a transient
  NuGet lookup miss before packing; rerunning the failed job succeeded without a code change.
- PR `#107` passed lean gate `29926425619`, squash-merged as
  `a12b2154907d9f75f8bdef77cf4470ecefa1aad8`, and release run `29926734114` published Google,
  Microsoft, and Discord Auth connectors at exact `0.20.0`. All three are indexed and the unchanged
  fresh NuGet.org-only consumer passed. Existing Storage/Media packages remained at `0.20.0` and were
  skipped as duplicates, proving central baseline capture did not create patch churn.
- PR `#108` passed lean gate `29927889388`, squash-merged as
  `138388e86eacad2c9c9b238e97548a465396f2a4`, and release run `29928202945` reported `76/76`
  immutable API floors. Every packed identity already existed; NuGet returned zero new package or
  symbol-package creations.
- The historical `sylin-labs` NuGet organization is retired. Ownership of all 166 indexed historical
  Sora and Koan package IDs was preserved under `sylin.org`; the authenticated account reports one
  organization, `sylin.org`, with 240 packages. No packages were deleted or unlisted. Public owner
  search is eventually consistent and may temporarily report stale `sylin-labs` results.
- No `automation/package-lineage-dev` branch, `release/dev/*` tag, release-wave escrow, or GitHub
  Release was created.
- There is no manual dispatch path. Any future `main` commit is a publication event; work on `dev`
  triggers nothing.

## Validation posture

- The smaller slice retains API baselines, generated-surface drift detection, host/container lifecycle,
  reusable family conformance, package-only consumption, and direct provider evidence.
- Focused repairs pass the host failure oracle, Communication 44/44, the affected Data correction 1/1,
  and the real Mongo Web owner 52/52. The package consumer builds the complete local dependency closure;
  its connected clean-network run passed.
- The main PR now runs only product/API agreement, one Release build, lockfile and structural
  documentation/tooling checks. Affected behavior and native evidence run at their owning family;
  complete certification is never inferred from promotion or publication.

## Next actions

No active initiative work remains.

1. Return to ordinary value-led framework development outside the closed maturity epics.
2. Resume ARCH-0089 migrations only from public Agyo/Zen Garden destination and consumer evidence.
3. Keep S3, Backup, and unclaimed `0.17` migration owners outside the supported 0.20 surface.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs only to an explicitly requested whole-framework milestone, not
  normal development, merge, promotion, version calculation, publication, or release plumbing.
