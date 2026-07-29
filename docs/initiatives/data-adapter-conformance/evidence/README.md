---
type: REFERENCE
domain: data
title: "Data Adapter Conformance Evidence Packets"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: evidence packet location, schema responsibilities, and retention boundary
---

# Data Adapter Conformance Evidence Packets

Each audited adapter receives `evidence/<adapter>/` with the primer §10.7 packet:

1. `identity.md` — exact source checkpoint, primer/profile fingerprint, provider/driver version, fixture, policies,
   date, and reproducible commands.
2. `probes.md` — `PRB-*` least-privilege provider observations and official-source/native artifacts.
3. `claims.json` — executable `CLM-*` Observed/Target/Declined scope and Advertised/Unadvertised publication projection.
4. `surfaces.md` — `SUR-*` inventory for every public, alternate, Direct, instruction, batch, and lifecycle path.
5. `scorecard.json` — claim-to-cell matrix plus atomic Framework/Family/Adapter rows and verdicts.
6. `evidence.json` — `EV-*` registry with safe retained artifact locations/hashes and exact reproduction commands.
7. `dependencies.json` — semantic-owner, source-path/hash, TestKit/Forge/schema, and profile fingerprints consumed by
   the verdict, used for impact-based invalidation.
8. `remediation.md` — `R-*` disposition ledger, linked owners, invalidated consumers, and re-entry proofs.
9. `README.md` — human summary generated or checked against the machine-readable packet.

A whole-adapter greenfield replacement additionally carries these paths under `evidence/<adapter>/`:

1. `harvest/lessons.md` — `L-*` provider facts, externally observable behavior, performance traps, and negative lessons;
   no legacy implementation recipe or internal structure.
2. `harvest/compatibility.json` and `harvest/black-box.json` — public continuity candidates and declarative scenarios.
3. `restricted/retirement.json` — the pre-rewrite inventory of legacy source, types, registrations, helpers, options,
   fixtures, and tests plus their final absence evidence.
4. `rewrite/replacement.json` — empty-start assertion, complete new-source manifest, compile/registration inventory,
   one execution path, and one necessary reason for every moving part.
5. `rewrite/lineage.md` — replacement architecture review covering moving-part necessity, duplicate ownership, copied
   structure, compatibility bridges, shadow/fallback paths, and warm-path discovery.

An orchestrator-leased parallel lane also writes `handoff.md` with lease ID, card, source identity, allowed paths,
result, verification, blockers, and requested central-ledger transition. It is coordination evidence, not permission
for the worker to edit `PROGRESS.md` or `NOW.md`.

The initiative does not create a parallel hand-maintained capability catalog. The control plane built by DAC-03 must
project executable adapter declarations into claims, TestKit applicability, runtime facts, and packet summaries.

Large logs, dumps, TRX files, and benchmark traces remain CI or ignored artifacts. The committed registry retains safe
summaries, hashes/links, native plans needed to prove a claim, and exact commands. No secret or business data belongs
in this tree.

An adapter directory is not evidence by existence. Its references must resolve, its commands must reproduce, and its
verdict must aggregate mechanically according to primer §10.

For a gold replacement, passing behavioral tests without a complete retirement/absence result is RED. A dead legacy
path is a defect even when it is unreachable in the tested configuration.

Packet validity is impact-based, not graph-direction-based. Any changed semantic owner, consumed source path/hash,
claim/profile schema, TestKit/Forge version, or provider fixture invalidates every matching packet—including an earlier
gold packet or sibling adapter. The impact query freezes those packets as stale before remediation and names the exact
certification cards that must rerun on the next common checkpoint.

## Reproducible source identity

A clean committed tree records its commit and a clean-status proof. An uncommitted initiative checkpoint records:

- the base commit;
- a sealed binary-capable patch/bundle containing only initiative-owned changes and its SHA-256;
- a relative-path/hash manifest for added and changed source files;
- the resultant source fingerprint and tool used to calculate it; and
- a reproduction command that applies the patch in a disposable clean worktree and verifies the manifest.

Unrelated user changes are excluded from the checkpoint. The patch artifact may remain in approved CI/ignored storage,
but its retained hash and location must resolve for certification. A session may create a phase commit only when the
user explicitly authorizes it. Certification stops when neither form can reproduce the exact code under test.

## Ground-up replacement identity

A ground-up replacement records the common-base fingerprint, an empty-start assertion, the complete legacy-retirement
inventory, the complete new-source path/hash manifest, compile and registration inventories, exactly one execution
path, and a necessary reason for every moving part. Reproduction applies the atomic deletion+replacement change to the
common base and reruns behavior, architecture, and absence checks. This proves a lean replacement without pretending
that process metadata can prove what an author thought or saw.
