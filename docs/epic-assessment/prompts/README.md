# Epic Prompt Stack — Index

Self-contained, single-session prompts that drive the three projects toward the future state
defined by this analysis (`../00`–`06`). Written for **lesser models, possibly on other
providers, with no prior context**: each prompt embeds its own ground truth, cites
file-level evidence, closes design decisions in DECIDED blocks, and references
[CHARTER.md](CHARTER.md) for the shared contract (mission, canon, protocol, paths).

**How to use**: paste one prompt file as the session's instruction. The agent reads
CHARTER.md first, claims its row in [PROGRESS.md](PROGRESS.md), then executes. Run prompts in
order within a phase; phases compose — each enablement is a prerequisite the next phase
consumes (Koi publishes crates → Zen builds clean → Koi's API works for programs → certmesh
issues real keys → moss verifies clients → Koan binds tokens to those certificates → the
demo proves the chain → contracts freeze what the demo proved → agents get the whole thing
as a discoverable, governed surface).

**Tracking**: [PROGRESS.md](PROGRESS.md) is the live ledger — status per prompt, what is
runnable right now (the Readiness section), the divergence log for when repo reality
contradicts a prompt, and the operator gates (the irreversible/physical steps). Check it
before picking work; update it when you finish. Start with **E01** as the calibration
card.

These prompts add **only the cross-repo seam work**. Per-repo maturation already has its own
stashes — run those in parallel lanes as the operator chooses:
`KOAN/docs/assessment/06-prompt-stash.md` + `07-…`, `KOI/docs/prompts/P01–P13`,
`ZEN/.agentic/prompts/`.

## Sequence and composition

| # | Prompt | Repo(s) | Enables |
|---|---|---|---|
| **Phase A — Canon & memory** | | | |
| [E01](E01-stack-canon-adr.md) | The stack canon ADR (STACK-0001) | all 3 | Every later session stops contradicting the others |
| [E02](E02-surface-ledger.md) | Surface ledger + rotation contract | all 3 | "Leave a guard at the door" becomes mechanical |
| **Phase B — Make the seams real** | | | |
| [E03](E03-koi-publish-closure.md) | Koi publishes the crate closure | Koi | E04; any external Rust consumer |
| [E04](E04-zen-published-deps.md) | Zen builds from published crates | Zen | Zen's clean clone → CI → releases → contributors |
| [E05](E05-koi-programmatic-contract.md) | Koi works for programs (token + bind) | Koi | Agents, siblings, scripts can actually call Koi |
| [E06](E06-koan-satellite-inversion.md) | Koan satellite inversion | Koan | Koan works alone; sibling coupling becomes opt-in |
| [E07](E07-cross-repo-contract-corpus.md) | Cross-repo contract corpus | all 3 | Seams stay true while lanes rotate |
| **Phase C — The trust column, made real** | | | |
| [E08](E08-koi-csr-enrollment.md) | CSR enrollment (keys never travel) | Koi | PKI-correct identity; E09/E10 |
| [E09](E09-zen-moss-client-auth.md) | Moss verifies clients + closes the holes | Zen | Real mutual TLS in the fleet; E11 |
| [E10](E10-koan-trust-binding.md) | Koan consumes certmesh trust; KSVID binding | Koan | **Koan apps use Koi certificates under their tokens** |
| **Phase D — Proof & freeze** | | | |
| [E11](E11-epic-demo.md) | The end-to-end demo (two machines) | all 3 | The Epic's existence proof; tripwires extracted |
| [E12](E12-self-description-envelope.md) | Self-description envelope | all 3 | Composition audit; agent introspection; E13 |
| **Phase E — Agent-ready LAN & mission surfaces** | | | |
| [E13](E13-agent-ready-lan.md) | koi-mcp + MCP layering + composed demo | Koi+Koan | "Discover a named, trusted, governed tool surface" |
| [E14](E14-zero-egress-sovereign-lane.md) | Tested zero-egress sovereign profile | Koan-led | Sovereignty as a CI-verified claim |
| [E15](E15-accidental-sysadmin-profiles.md) | The accidental-sysadmin profiles | all 3 | The mission persona gets a front door |
| [E16](E16-shows-its-work.md) | "Software that shows its work" package | all 3 | AI-built-software trust tax → differentiator |

**Dependency graph** (beyond phase order): E04←E03 · E05 independent of E03/E04 ·
E07←{E03,E04} · E09←E08 · E10←{E08, E06} · E11←{E05,E08,E09,E10} · E12←E01 (lockfile payload
deepens when `KOAN` 07-P1.1 lands) · E13←{E05,E12; Koan governance deepens with KOAN 07-P3.1}
· E14←E06 · E15/E16 anytime after Phase B.

## Not prompts (operator/human work, listed so it isn't lost)

- The **Win10-ESU go/no-go** (mid-July checkpoint; rule in `../05` §4) — a human decision.
- The **repair-café / classroom channel** and **community GPU pool positioning** (`../06`
  §4.2–4.3) — outreach and narrative, not agent sessions.
- The **data-dignity sample** (`../06` §4.6) — sequenced after the truth set; write its
  prompt when Phase D is green.

## Conventions

Every prompt has: **Repo(s) / Phase / Prereqs / Mission / Context (ground truth + evidence) /
DECIDED / DEFAULT / Plan of record / Verification / Definition of done.** DECIDED is closed —
the architect has ruled; deviating requires a new operator decision, not agent judgment.
DEFAULT may be deviated from with a recorded one-paragraph justification. Every session
starts by claiming a [PROGRESS.md](PROGRESS.md) row and ends with guards left behind,
`docs/SURFACES.md` updated (E02 creates it), and the PROGRESS.md row closed.

## Files

- [CHARTER.md](CHARTER.md) — the shared session contract (read first, every time).
- [PROGRESS.md](PROGRESS.md) — the live execution ledger (status, readiness, divergence,
  operator gates).
- `E01`–`E16` — the prompts (this README's table).
