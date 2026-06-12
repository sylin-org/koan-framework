# E03 — Koi Publishes the Crate Closure

**Repo(s)**: Koi · **Phase**: B · **Prereqs**: none · **One session**
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Make Koi's crates installable from crates.io again — the **full dependency closure** Zen
Garden consumes — with a publish pipeline that cannot fail silently. This single repo's fix
unblocks Zen Garden's entire critical path (clean clone → CI → releases → contributors),
which is hard-blocked on `../koi` path deps today.

## Context (verify each before acting)

- Zen Garden depends on five koi crates by path (`ZEN/Cargo.toml:98-102`): koi-embedded,
  koi-certmesh, koi-common, koi-crypto, koi-truststore — and moss builds koi-embedded with
  `udp(true)` (`ZEN/src/moss/src/bootstrap/run.rs:566-607`), so the published koi-embedded
  must carry the udp feature, which pulls **koi-udp**.
- Koi's verification record (`KOI/docs/assessment/findings/verification-2026-06.md` claim 4):
  the crates.io publish step has been **silently failing since February** — the publish
  script's error handling is dead because the workflow lacks `pipefail`, and
  koi-udp/koi-runtime/command-surface are missing from the publish list. Published versions
  (e.g. koi-net) are stale at ~Feb-12. `cargo install koi-net` is currently a trap.
- koi-embedded composes the domain crates (mdns/dns/health/proxy/certmesh/runtime/config) —
  compute the real closure; do not hand-pick.

## DECIDED

1. **Publish the closure, compute it mechanically**: `cargo metadata` from koi-embedded +
   koi-udp + koi-client roots; everything path-internal in that graph gets published, in
   topological order.
2. **Fix the pipeline, don't hand-publish**: `set -euo pipefail` (or equivalent) in the
   publish script/workflow; the publish list replaced by the computed closure or an
   explicit, tested list; a post-publish verification step (below) that fails the job
   loudly.
3. **Versioning**: bump all published crates to a fresh minor (one workspace version),
   tag-driven (`v0.x.y` tag triggers publish). No more publish-on-every-push, no mutable
   tags (aligns with Koi's own Stage-4 plan).
4. **command-surface is NOT published** (its own assessment folds it into the binary —
   don't institutionalize it). koi-runtime publishes only if the closure requires it.

## DEFAULT

- Version number: next minor above the highest currently-published koi crate version.
- If a crate in the closure cannot publish (e.g. metadata missing), fix metadata
  (description/license/repository fields) rather than excluding it.

## Plan of record

1. Compute the closure; list it in the session notes. 2. Fix workflow/script (pipefail,
list, tag trigger). 3. Fill any missing crate metadata. 4. Dry-run: `cargo publish
--dry-run` per crate in topo order. 5. Tag and publish (with operator confirmation for the
actual `cargo publish` — it is irreversible). 6. **Post-publish verification**: in a temp
directory OUTSIDE the workspace, create a scratch crate depending on the published
`koi-embedded` (features incl. udp) + `koi-certmesh` + `koi-truststore` at the new versions;
`cargo build` must succeed. 7. Record the published set + versions in `docs/SURFACES.md`
("publish pipeline" row: guard = the post-publish verification job). 8. Update README's
install instructions if they reference stale crates.

## Verification

The scratch-crate build against crates.io (step 6) — this is the gate, not the publish
exit-code. CI publish job fails loudly on any error (prove by inspection of pipefail +
a deliberate dry-run failure if cheap).

## Definition of done

- [ ] All closure crates at the new version on crates.io; scratch build green.
- [ ] Publish is tag-driven with loud failure; list = computed closure.
- [ ] SURFACES.md updated; stale install docs corrected.
- [ ] Session summary records exact crate list + versions (E04 consumes it).
