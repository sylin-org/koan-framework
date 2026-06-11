# E04 — Zen Garden Builds From Published Crates

**Repo(s)**: Zen Garden · **Phase**: B · **Prereqs**: E03 (published versions; get the exact
list/versions from its session summary or crates.io) · **One session**
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Make a clean clone of Zen Garden compile: swap the five `../koi` path dependencies for
published crates.io versions, keep the maintainer's sibling dev loop via
`[patch.crates-io]`, and add the CI gate that prevents the path coupling from ever silently
returning. This is Zen Garden's own critical-path item #1 — CI, releases, and contributors
are all hard-blocked behind it.

## Context (verify each)

- The path deps: `ZEN/Cargo.toml:98-102` (koi-embedded, koi-certmesh, koi-common,
  koi-crypto, koi-truststore).
- moss requires koi-embedded with the udp feature
  (`ZEN/src/moss/src/bootstrap/run.rs:566-607`) — confirm the published koi-embedded
  carries it (E03 guaranteed this; verify, per CHARTER protocol 1).
- **Version-skew risk**: the local `../koi` checkout may be ahead of what E03 published. If
  ZG uses any API not in the published versions, the swap fails — that is a finding to
  report (it means Koi needs a republish), not something to work around with git deps.
- ZG may have no CI at all yet (zero workflows have ever run). If so, create the minimal
  workflow as part of this prompt; ZG's own assessment (maturity.md critical path #3)
  already orders minimal CI.

## DECIDED

1. `Cargo.toml` switches to caret version requirements matching E03's published versions.
2. A `[patch.crates-io]` section (in the workspace Cargo.toml or `.cargo/config.toml` per
   what cargo supports for this layout) points the five crates at `../koi` for local
   development — the dev loop must stay one-command fast.
3. **CI gate "clean-clone"**: a job that checks out the repo alone, strips/ignores the patch
   section (script: remove the `[patch.crates-io]` block, or build in a context where
   `../koi` does not exist), and runs `cargo check --workspace`. This job failing = the
   coupling regressed.
4. No git dependencies as a fallback. Published versions or a reported blocker.

## DEFAULT

- If no CI exists: create `.github/workflows/ci.yml` with exactly two jobs for now —
  clean-clone check (above) and `cargo test --workspace` — keeping it minimal per ZG's own
  plan (its full CI design belongs to its own prompt stash).
- Excluded crates (`src/orchestrators/*` are outside the workspace): give them a separate
  CI lane only if trivially cheap; otherwise note for ZG's own stash.

## Plan of record

1. Read E03's published list/versions; `cargo search`/index check to confirm availability.
2. Edit Cargo.toml: versions + patch section. 3. `cargo check --workspace` with patch active
(local loop intact). 4. Simulate clean clone: temp clone (or move `../koi` aside / strip the
patch) and `cargo check --workspace` — the real gate. 5. Wire the CI job(s). 6. Update
`docs/SURFACES.md` (row: "koi dependency seam → guard: clean-clone CI job"). 7. Commit.

## Verification

- Clean-clone simulation compiles with **no** `../koi` present.
- With patch active, a local edit in `../koi` is picked up (dev loop preserved).
- CI workflow file is valid (run it if the repo has runners; otherwise `act`-style local
  validation or YAML lint + a note).

## Definition of done

- [ ] Clean clone compiles against crates.io; patch section preserves the sibling loop.
- [ ] Clean-clone CI gate exists and is wired.
- [ ] Any API skew found is reported precisely (crate, symbol, needed version) — not
      papered over.
- [ ] SURFACES.md updated; commit pushed only if the operator asked.
