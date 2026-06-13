# Epic Prompt Stack — Progress Ledger

The coordination spine for executing [E01–E16](README.md). Agents: **read
[CHARTER.md](CHARTER.md) first**, then update this file as you work — claim your row
(`in-progress`) when you start, close it (`done` / `blocked` / `postponed` / `obsolete`)
when you finish. Keep notes to one or two lines; link commits by short SHA. Because the
stack spans three repositories, prefix each SHA with its repo when ambiguous
(`koi:ab12cd3`, `zen:…`, `koan:…`). This ledger is the single source of truth for *what is
done and what is runnable next* — the operator and the next agent both read it before
picking work.

## Status vocabulary

| Status | Meaning |
|---|---|
| `pending` | Not started; prerequisites may or may not be met (see Readiness). |
| `in-progress` | An agent has claimed it. Put your model id in Agent and the date you started. |
| `done` | Definition of done met; guards left; SURFACES.md updated; commit(s) linked. |
| `blocked` | Started or attempted, cannot proceed. Record why in the Divergence log + Notes. |
| `postponed` | Deliberately deferred (premise wrong, dependency immature). Note the reason. |
| `obsolete` | Superseded by reality; the prompt no longer applies. Note what replaced it. |

**Rules of the ledger** (from CHARTER): do not mark `done` without the guard + SURFACES.md
update the prompt requires. Do not start a prompt whose prereqs are not actually green —
verify in the repos, not just in this table (the table can lag). If repo reality contradicts
a prompt, log it under Divergence and either adapt within the prompt's intent or `blocked`.

## Ledger

| ID | Phase | Repo(s) | Status | Date | Agent/model | Commit(s) | Notes |
|---|---|---|---|---|---|---|---|
| [E01](E01-stack-canon-adr.md) | A | all 3 | done | 2026-06-13 | claude-opus-4-8 | koan:bebecbac · zen:b5289b63 · koi:ba349fb | STACK-0001 ×3, byte-identical Decision block (sha256 95f2d4c2…); indexed + cross-linked in each repo's agent context. Transcription only. Unblocks E06/E12. |
| [E02](E02-surface-ledger.md) | A | all 3 | done | 2026-06-13 | claude-opus-4-8 | koan:`<this commit>` · zen:c7521f48 · koi:1b5ac99 | `docs/SURFACES.md` ×3 (koan 16 / zen 16 / koi 14 rows) + rotation contract + lint (koi `surfaces` ci.yml job, koan `surfaces.yml`; ZG none — no workflows). Honest: KOI proxy guard=none; dormant/parked rows `unknown since <date>`. |
| [E03](E03-koi-publish-closure.md) | B | Koi | pending | | | | Publish crate closure (incl. koi-udp); loud-fail pipeline. **Operator gate: irreversible `cargo publish`.** Unblocks E04. |
| [E04](E04-zen-published-deps.md) | B | Zen | pending | | | | Path deps → crates.io + `[patch.crates-io]` + clean-clone CI gate. Needs E03. |
| [E05](E05-koi-programmatic-contract.md) | B | Koi | pending | | | | Token story THEN bind flag; networking/security page. Independent. |
| [E06](E06-koan-satellite-inversion.md) | B | Koan | pending | | | | Invert 5 mainline ZenGarden refs → satellites + arch test. Needs E01. May split 6a/6b. |
| [E07](E07-cross-repo-contract-corpus.md) | B | all 3 | pending | | | | Contract fixtures for 4 seams. Needs E03+E04, E05. |
| [E08](E08-koi-csr-enrollment.md) | C | Koi | pending | | | | CSR enrollment — keys never travel. Needs E05; republish after. Unblocks E09/E10. |
| [E09](E09-zen-moss-client-auth.md) | C | Zen | pending | | | | moss verifies client certs; close :7185/deploy holes; kill `changeme`. Needs E08, E04. |
| [E10](E10-koan-trust-binding.md) | C | Koan | pending | | | | Koan serves/calls over pond-CA channels; tokens carry `koi_id`/`koi_ca`. Needs E06, E08. |
| [E11](E11-epic-demo.md) | D | all 3 | pending | | | | Two-machine end-to-end demo (the existence proof). **Operator gate: 2 machines/VMs.** Needs E05,E08,E09,E10. |
| [E12](E12-self-description-envelope.md) | D | all 3 | pending | | | | `/.well-known/sylin/self.json` envelope ≤12 fields + schema CI. Needs E01; deepens with KOAN 07-P1.1. |
| [E13](E13-agent-ready-lan.md) | E | Koi+Koan | pending | | | | koi-mcp (KOI P11) + `_mcp._tcp` announce satellite + composed demo. Needs E05, E12. |
| [E14](E14-zero-egress-sovereign-lane.md) | E | Koan-led | pending | | | | Sovereign profile + zero-egress CI lane (internal-only network). Needs E06. |
| [E15](E15-accidental-sysadmin-profiles.md) | E | all 3 | pending | | | | 4 community-org guides; every command executed before written. Needs Phase B truth. |
| [E16](E16-shows-its-work.md) | E | all 3 | pending | | | | Package the verifiability machinery + AI-method policy ×3. Needs E02, E12. |

## Readiness (dependency gates)

The dependency graph lives in [README.md](README.md); this is the live view of *what an
agent can pick up right now*. Update it as rows close.

**Runnable now** (no unmet prerequisites): **E01, E02, E03, E05.**
Start with **E01** as the calibration card — pure transcription, no code, no hot path; how a
model handles its DECIDED/DEFAULT boundary tells you whether to trust it with Phase B+.

**Unblocks on completion** (what each `done` opens):

| When this is `done` | These become runnable |
|---|---|
| E01 | E06, E12 |
| E03 | E04 (and E07's build-against-published gate) |
| E03 + E04 + E05 | E07 |
| E05 | E08 |
| E08 | E09, E10 (E10 also needs E06) |
| E06 | E14 (and E10) |
| E05 + E08 + E09 + E10 | E11 |
| E12 | E13 (also needs E05), E16 (also needs E02) |
| Phase B (E03–E07) | E15 |

**Cross-stack inheritance** (per-repo stashes run in parallel lanes; these Epic prompts
*compose with* but do not own them): Koan `docs/assessment/06`/`07`, Koi `docs/prompts/`
P01–P13 (E13 executes P11 directly), Zen `.agentic/prompts/`. A prompt that says "deepens
when KOAN 07-Pxx lands" must not block on it — ship the v1 the Epic prompt specifies.

## Divergence log

When a pre-flight check fails or repo reality contradicts a prompt, record it here before
adapting or blocking: date, prompt ID, what was found, what was done instead. This is how
the next agent learns the map was wrong without re-discovering it.

| Date | ID | Finding | Action |
|---|---|---|---|
| 2026-06-13 | E01 | KOI `docs/adr/` uses 3-digit, no-prefix filenames (`013-…`), which can't carry the DEFAULT-fixed ADR id `STACK-0001`. | Filed the KOI copy as `STACK-0001-sylin-stack-canon.md` (DEFAULT deviation, justified in-file): cross-repo grep-identity is the ADR's whole purpose; an opaque `014-` sequence would bury the shared id. |
| 2026-06-13 | E01 | KOI has no `docs/adr/README`/index (the card's cited cross-link target does not exist). | Cross-linked from KOI `.agentic/CONTEXT.md` (the repo's actual agent-bootstrap surface, which already points at `docs/adr/`) instead of creating a new index file. |
| 2026-06-13 | E02 | ZEN has no `.github/workflows/` directory at all (only `.github/copilot-instructions.md`); the DEFAULT "CI lint where CI exists" has no host. | No lint added in ZEN; ledger marks tested surfaces guard `tests, unwired` and notes CI is absent — matches the card's own "ZG CI never ran" expectation. |
| 2026-06-13 | E02 | KOAN general build/test CI is disabled by design (`ci.yml` + `validate-main-pr.yml` are noop placeholders; only `release-on-main.yml` runs, build-only). | Added a standalone single-purpose `.github/workflows/surfaces.yml` (push/PR to main+dev) rather than wiring the lint into a disabled placeholder or the sensitive release pipeline. Tested-surface guards marked `(local)` to reflect CI-build-only. |
| 2026-06-13 | E02 | KOI working tree was clean at execution — the card-warned untracked `token.rs` in-flight work was not present in the tree. | Recorded the token/DAT-auth surface per the work-item note (exercised by `private downstream solution / in-flight`, guard none) WITHOUT inspecting or searching for anything (CHARTER rule 8). |

## Operator gates

Steps an agent must NOT take autonomously — they need a human decision or physical
resources. Listed here so they are visible before the prompt that hits them.

- **E03 — irreversible publish.** `cargo publish` cannot be undone (a version is permanent
  on crates.io). The agent does the dry-runs and the pipeline fix; the operator confirms the
  actual publish. Record the published crate set + versions in this ledger's E03 Notes so
  E04 can consume them.
- **E11 — two real nodes.** The end-to-end demo needs two machines (or two VMs/containers
  with distinct network identities). The agent cannot provision these; the operator supplies
  them and confirms the topology, which the agent records in `demo/run.md`.
- **Win10-ESU go/no-go (not a prompt).** Mid-July checkpoint; rule in
  [../05-leverage-plan.md](../05-leverage-plan.md) §4. Operator decision — keep it out of the
  prompt lanes.

When an operator gate produces follow-up the operator must run (a baseline capture, a manual
verification, a one-time migration), append a short `## <ID> operator gate: <title>` section
below this line with the exact commands — keep them copy-paste runnable.

<!-- Operator-gate detail sections get appended below as prompts produce them. -->
